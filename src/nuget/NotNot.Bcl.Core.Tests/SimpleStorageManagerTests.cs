using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NotNot.Storage;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Tests for <see cref="SimpleStorageManager{TData}"/> covering REQ-N1-3 (RegisterDisposalAction
/// vs DisposeAsync race — locked re-check ensures no silent no-op) and REQ-F5-3-NEW (public
/// <c>RaiseError</c> raiser method fires <c>OnError</c> subscribers).
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
    /// REQ-F5-3-NEW: <see cref="SimpleStorageManager{TData}.RaiseError"/> must fire
    /// <c>OnError</c> subscribers with the supplied exception. Validates the public raiser
    /// method that <see cref="ProjectedSubtreeStorageAdapter"/> getter ObjectDisposedException
    /// arm uses to route diagnostic exceptions through the unified observability channel.
    /// </summary>
    [Fact]
    public void RaiseError_FiresOnErrorSubscribers()
    {
        var adapter = new EphemeralMemoryStorageAdapter();
        var manager = new SimpleStorageManager<TestData>(adapter);

        Exception? captured = null;
        manager.OnError += ex => captured = ex;

        var sentinel = new InvalidCastException("test sentinel");
        manager.RaiseError(sentinel);

        Assert.Same(sentinel, captured);
    }
}
