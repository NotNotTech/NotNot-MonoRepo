using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NotNot;
using NotNot.Storage;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Tests for <see cref="SimpleStorageManager{TData}"/> covering REQ-N1-3 (RegisterDisposalAction
/// vs DisposeAsync race — locked re-check ensures no silent no-op) and REQ-F5-3-NEW (public
/// <c>RaiseWriteError</c> raiser method emits a <see cref="WriteEventError{TData}"/> on the
/// <see cref="SimpleStorageManager{TData}.Writes"/> observable).
/// </summary>
public class SimpleStorageManagerTests
{
    /// <summary>
    /// POCO used by these tests. Matches the <c>where TData : class, new()</c> constraint of
    /// <see cref="SimpleStorageManager{TData}"/>; the data shape is irrelevant — only its
    /// existence (not its content) is exercised by these tests.
    /// </summary>
    public class TestData
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// REQ-N1-3 (H3-barrier-revised): widens the TOCTOU window between
    /// <see cref="SimpleStorageManager{TData}.RegisterDisposalAction"/> and
    /// <see cref="SimpleStorageManager{TData}.DisposeAsync"/> using a
    /// <see cref="ManualResetEventSlim"/> start-barrier so all 33 tasks launch simultaneously.
    /// Without the barrier, ThreadPool may schedule all registers before-or-after dispose →
    /// invariant trivially holds even on broken pre-fix code → false-pass. With the barrier,
    /// race collisions are reliably observable across 100 [Theory] iterations.
    ///
    /// Invariant: every register either (a) completed and its action will be invoked by
    /// DisposeAsync, OR (b) threw <see cref="ObjectDisposedException"/>. ZERO silent-no-op
    /// is acceptable, so <c>invokedCount + disposedExCount == 32</c> on every iteration.
    /// </summary>
    [Theory]
    [MemberData(nameof(IterationData))]
    public async Task RegisterDisposalAction_RaceWithDispose_NeverSilentNoOp(int iteration)
    {
        // iteration parameter is the [Theory] discriminator for flake-detection across 100
        // independent runs — its only role is to make each xUnit test case unique.
        _ = iteration;

        var adapter = new EphemeralMemoryStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter);

        int invokedCount = 0;
        int disposedExCount = 0;
        using var startBarrier = new ManualResetEventSlim(false);

        var registerTasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            startBarrier.Wait();
            try
            {
                manager.RegisterDisposalAction(() => Interlocked.Increment(ref invokedCount));
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Increment(ref disposedExCount);
            }
        })).ToArray();

        var disposeTask = Task.Run(async () =>
        {
            startBarrier.Wait();
            await manager.DisposeAsync();
        });

        // Simultaneous launch — widens TOCTOU race window so locked re-check in
        // RegisterDisposalAction is meaningfully exercised.
        startBarrier.Set();
        await Task.WhenAll(registerTasks.Append(disposeTask));

        // Invariant: every register either succeeded (action invoked) OR threw
        // ObjectDisposedException — ZERO silent-no-op.
        Assert.Equal(32, invokedCount + disposedExCount);
    }

    /// <summary>
    /// 100 iterations of the race test — each <see cref="MemberData"/> entry produces a
    /// distinct <see cref="Theory"/> case so xUnit reports per-iteration pass/fail and
    /// flake-detection is granular.
    /// </summary>
    public static IEnumerable<object[]> IterationData =>
        Enumerable.Range(0, 100).Select(i => new object[] { i });

    /// <summary>
    /// REQ-F5-3-NEW (post-async-contract): <see cref="SimpleStorageManager{TData}.RaiseWriteError"/>
    /// must emit a <see cref="WriteEventError{TData}"/> on the
    /// <see cref="SimpleStorageManager{TData}.Writes"/> observable, carrying the supplied exception
    /// inside the <see cref="NotNot.Problem"/>. Validates the public raiser used by
    /// <see cref="ProjectedSubtreeStorageAdapter"/>'s getter ObjectDisposedException arm to route
    /// diagnostic exceptions through the unified observability channel.
    /// </summary>
    [Fact]
    public void RaiseWriteError_EmitsWriteEventErrorOnWritesStream()
    {
        var adapter = new EphemeralMemoryStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter);

        WriteEventError<TestData>? captured = null;
        using var sub = manager.Writes.Subscribe(new ActionObserver<WriteEvent<TestData>>(evt =>
        {
            if (evt is WriteEventError<TestData> err) captured = err;
        }));

        var sentinel = new InvalidCastException("test sentinel");
        manager.RaiseWriteError(sentinel);

        Assert.NotNull(captured);
        Assert.Contains("test sentinel", captured!.Problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Provenance guard (F-H1): the consolidated <c>EmitSyntheticWriteError</c> helper forwards
    /// <c>[CallerMemberName]</c> to <see cref="NotNot.Problem.FromEx"/>, so the emitted
    /// <see cref="NotNot.Problem"/>'s <c>source</c> continues to name the ORIGINATING method
    /// (<c>RaiseWriteError</c>) rather than the helper. Asserts member-name only (stable);
    /// file-path/line-number are intentionally NOT asserted (brittle). Fails on a naive extraction
    /// that drops caller-info forwarding; passes with the forwarding signature.
    /// </summary>
    [Fact]
    public void RaiseWriteError_PreservesCallerProvenanceMemberName()
    {
        var adapter = new EphemeralMemoryStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter);

        WriteEventError<TestData>? captured = null;
        using var sub = manager.Writes.Subscribe(new ActionObserver<WriteEvent<TestData>>(evt =>
        {
            if (evt is WriteEventError<TestData> err) captured = err;
        }));

        manager.RaiseWriteError(new InvalidCastException("provenance sentinel"));

        Assert.NotNull(captured);
        Assert.Equal("RaiseWriteError", captured!.Problem.DecomposeSource().memberName);
    }

    /// <summary>
    /// Disposal-path regression (site 3 — previously uncovered): a throwing disposal action must
    /// (a) surface a <see cref="WriteEventError{TData}"/> with a non-null <c>Data</c> snapshot
    /// (proving <c>GetDataSnapshotOrDefault()</c> still feeds the disposal-action catch),
    /// (b) carry the thrown exception's message in <c>Problem.Detail</c>, and
    /// (c) NOT block a second registered disposal action from running (best-effort isolation).
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ThrowingDisposalAction_EmitsWriteErrorAndRunsSubsequentActions()
    {
        var adapter = new EphemeralMemoryStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter);
        await manager.InitializeAsync();

        WriteEventError<TestData>? captured = null;
        using var sub = manager.Writes.Subscribe(new ActionObserver<WriteEvent<TestData>>(evt =>
        {
            if (evt is WriteEventError<TestData> err) captured = err;
        }));

        bool secondActionRan = false;
        manager.RegisterDisposalAction(() => throw new InvalidOperationException("disposal sentinel"));
        manager.RegisterDisposalAction(() => secondActionRan = true);

        await manager.DisposeAsync();

        // (a) error surfaced with a non-null default-allowed snapshot
        Assert.NotNull(captured);
        Assert.NotNull(captured!.Data);
        // (b) thrown message carried through
        Assert.Contains("disposal sentinel", captured.Problem.Detail, StringComparison.Ordinal);
        // (c) best-effort isolation — the subsequent action still ran
        Assert.True(secondActionRan);
    }

    [Fact]
    public async Task RaiseWriteError_WaitsForInFlightMutationBeforeReadingDataSnapshot()
    {
        var adapter = new EphemeralMemoryStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter, new SimpleStorageOptions
        {
            WriteDebounce = TimeSpan.FromMilliseconds(1),
        });
        await manager.InitializeAsync();

        WriteEventError<TestData>? captured = null;
        using var sub = manager.Writes.Subscribe(new ActionObserver<WriteEvent<TestData>>(evt =>
        {
            if (evt is WriteEventError<TestData> error) captured = error;
        }));
        using var mutationEntered = new ManualResetEventSlim(false);
        using var releaseMutation = new ManualResetEventSlim(false);
        using var errorReadStarted = new ManualResetEventSlim(false);

        var updateTask = Task.Run(async () => await manager.UpdateAsync(data =>
        {
            mutationEntered.Set();
            releaseMutation.Wait();
            data.Value = 42;
        }));

        Assert.True(mutationEntered.Wait(TimeSpan.FromSeconds(5)));
        var errorTask = Task.Run(() =>
        {
            errorReadStarted.Set();
            manager.RaiseWriteError(new InvalidOperationException("snapshot sentinel"));
        });
        Assert.True(errorReadStarted.Wait(TimeSpan.FromSeconds(5)));
        await Task.Delay(50);
        Assert.False(errorTask.IsCompleted);

        releaseMutation.Set();
        await errorTask.WaitAsync(TimeSpan.FromSeconds(5));
        _ = await updateTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(captured);
        Assert.Equal(42, captured!.Data.Value);
        await manager.DisposeAsync();
    }
}
