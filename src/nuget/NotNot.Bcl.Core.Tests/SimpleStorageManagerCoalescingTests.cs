using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NotNot;
using NotNot.Storage;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Regression coverage for the coalescing / debounced-write / flush-on-dispose behavior of
/// <see cref="SimpleStorageManager{TData}"/> and, specifically, the cancellation-await path in
/// <c>AwaitWithCancellation</c> that was touched by the PH_P009 fix (the cancellation-token
/// registration is now disposed via <c>await using</c> instead of synchronous <c>using</c>).
///
/// These tests establish the behavioral invariants that must NOT regress:
///   1. Coalescing — multiple mutations inside the write-debounce window collapse into ONE
///      underlying adapter write; every caller resolves with the same success outcome.
///   2. Cancellation-await path (PH_P009 fix) — a cancelled UpdateAsync await returns the
///      "WaitCancelled" <see cref="Maybe{T}"/> error (never throws), and the underlying write
///      still proceeds to persist the data.
///   3. Flush-on-dispose — pending dirty data is written to the backing store during
///      <see cref="SimpleStorageManager{TData}.DisposeAsync"/>.
///   4. Cancellation-await with an already-cancelled token still short-circuits cleanly and
///      disposes the registration without hanging (exercises the await-using teardown).
/// </summary>
public class SimpleStorageManagerCoalescingTests
{
    /// <summary>
    /// POCO under persistence. Only field existence matters; content shape is irrelevant.
    /// </summary>
    public class TestData
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// Storage adapter that (a) counts underlying <see cref="WriteAsync"/> calls so coalescing can
    /// be asserted, (b) retains the last-written JSON so flush-on-dispose can be verified, and
    /// (c) optionally blocks the first write on a gate so a caller's await can be cancelled while a
    /// coalesced write is deliberately held in flight.
    /// </summary>
    private sealed class CountingStorageAdapter : IStorageAdapter
    {
        private int _writeCount;
        private string? _data;
        private readonly ManualResetEventSlim? _releaseFirstWrite;
        private int _firstWriteSeen;

        public CountingStorageAdapter(ManualResetEventSlim? releaseFirstWrite = null)
        {
            _releaseFirstWrite = releaseFirstWrite;
        }

        public int WriteCount => Volatile.Read(ref _writeCount);
        public string? LastWrittenJson => Volatile.Read(ref _data);

        public ValueTask<string?> ReadAsync(CancellationToken ct = default) => new(Volatile.Read(ref _data));

        public async ValueTask WriteAsync(string data, CancellationToken ct = default)
        {
            // Gate ONLY the first write when a release event is supplied — lets a test hold a
            // coalesced write in flight while it cancels a caller's await.
            if (_releaseFirstWrite is not null && Interlocked.Exchange(ref _firstWriteSeen, 1) == 0)
            {
                await Task.Run(() => _releaseFirstWrite.Wait(TimeSpan.FromSeconds(10)), CancellationToken.None);
            }

            Interlocked.Increment(ref _writeCount);
            Volatile.Write(ref _data, data);
        }

        public ValueTask DeleteAsync(CancellationToken ct = default)
        {
            Volatile.Write(ref _data, null);
            return default;
        }
    }

    /// <summary>
    /// Coalescing invariant: N mutations issued inside the same write-debounce quiet window resolve
    /// into ONE underlying adapter write, and every caller observes the same success outcome. A long
    /// (250ms) debounce guarantees all four UpdateAsync calls queue before the debouncer fires.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_MultipleWritesInDebounceWindow_CoalesceToSingleAdapterWrite()
    {
        var adapter = new CountingStorageAdapter();
        await using var manager = new SimpleStorageManager<TestData>(adapter, new SimpleStorageOptions
        {
            WriteDebounce = TimeSpan.FromMilliseconds(250),
        });
        await manager.InitializeAsync();

        // Fire four mutations back-to-back — all land inside the 250ms quiet window.
        var tasks = Enumerable.Range(1, 4)
            .Select(i => manager.UpdateAsync(d => d.Value = i))
            .ToArray();

        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        // Every caller resolves successfully with the coalesced terminal outcome.
        Assert.All(results, r => Assert.True(r.IsSuccess));
        // Coalescing: the four mutations collapsed into exactly ONE underlying adapter write.
        Assert.Equal(1, adapter.WriteCount);
        // Last-writer-wins is persisted.
        Assert.Equal(4, manager.Data.Value);
    }

    /// <summary>
    /// PH_P009-fix regression (cancellation-await path): when a caller cancels its await while the
    /// coalesced write is still in flight, UpdateAsync must RETURN a "WaitCancelled" error
    /// (<see cref="HttpStatusCode.Gone"/> / Timeout category) rather than throw, and the underlying
    /// write must still proceed to persist the mutation. Exercises the <c>await using</c> teardown of
    /// the cancellation-token registration on the cancelled branch.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_AwaitCancelledWhileWriteInFlight_ReturnsWaitCancelledAndStillPersists()
    {
        using var releaseWrite = new ManualResetEventSlim(false);
        var adapter = new CountingStorageAdapter(releaseWrite);
        await using var manager = new SimpleStorageManager<TestData>(adapter, new SimpleStorageOptions
        {
            WriteDebounce = TimeSpan.FromMilliseconds(1),
        });
        await manager.InitializeAsync();

        using var cts = new CancellationTokenSource();
        // The adapter holds this write open (releaseWrite not yet Set), so the await cannot complete
        // via the write; cancelling the token drives the cancelled branch of AwaitWithCancellation.
        var updateTask = manager.UpdateAsync(d => d.Value = 99, cts.Token);

        // Give the debounced write time to enter the gated WriteAsync, then cancel the await.
        await Task.Delay(100);
        cts.Cancel();

        var result = await updateTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancelled await returns the synthesized WaitCancelled error — NOT an exception.
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Problem);
        Assert.Equal(HttpStatusCode.Gone, result.Problem!.Status);
        Assert.Equal(Problem.CategoryNames.Timeout, result.Problem.category);

        // The underlying write still proceeds once released — cancellation of the AWAIT does not
        // cancel the debounced write.
        releaseWrite.Set();

        // Flush confirms the mutation was persisted despite the cancelled await.
        await manager.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(99, manager.Data.Value);
    }

    /// <summary>
    /// Cancellation-await teardown with an ALREADY-cancelled token: the cancelled branch must
    /// short-circuit and dispose the registration (await using) without hanging. A long debounce
    /// ensures the write has NOT completed, so the cancelled path — not the write-completed path —
    /// is taken.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PreCancelledToken_ReturnsWaitCancelledWithoutHanging()
    {
        var adapter = new CountingStorageAdapter();
        await using var manager = new SimpleStorageManager<TestData>(adapter, new SimpleStorageOptions
        {
            WriteDebounce = TimeSpan.FromSeconds(30),
        });
        await manager.InitializeAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await manager.UpdateAsync(d => d.Value = 7, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.Problem!.Status);
    }

    /// <summary>
    /// Flush-on-dispose: a mutation that has NOT yet flushed (long debounce keeps it pending) must be
    /// written to the backing store during <see cref="SimpleStorageManager{TData}.DisposeAsync"/>.
    /// Asserts the adapter received the write AND the persisted JSON reflects the mutation.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WithPendingDirtyData_FlushesToBackingStore()
    {
        var adapter = new CountingStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter, new SimpleStorageOptions
        {
            // 30s debounce guarantees the write has NOT fired on its own before dispose.
            WriteDebounce = TimeSpan.FromSeconds(30),
        });
        await manager.InitializeAsync();

        // Mutate but do NOT await terminal write (fire-and-forget the returned task) — the debounced
        // write stays pending until dispose flushes it.
        _ = manager.UpdateAsync(d => d.Value = 123);

        Assert.Equal(0, adapter.WriteCount); // still pending — debounce not elapsed

        await manager.DisposeAsync();

        // Flush-on-dispose persisted the pending mutation.
        Assert.Equal(1, adapter.WriteCount);
        Assert.NotNull(adapter.LastWrittenJson);
        Assert.Contains("123", adapter.LastWrittenJson!, StringComparison.Ordinal);
    }
}
