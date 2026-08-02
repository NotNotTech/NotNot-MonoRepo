using System.Diagnostics;
using NotNot.Storage;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Concurrency contract-pin for <see cref="AtomicFileWriter.AppendWithRetry(string, string, System.Text.Encoding?)"/>
/// — the shared-lib primitive that makes the VOW quota-history per-event append tolerant of a cross-process
/// file-sharing collision (two VOW instances sharing one data root). TEST_AUTHORING_GATE: authored as a
/// shared-lib concurrency race-pin (the only test-authoring category this fix warrants).
/// <para>
/// Pins the two contract halves the VOW call site depends on:
/// (a) a TRANSIENT external holder released mid-retry-window → the append SUCCEEDS and the line is present
///     (retry absorbs the transient lock);
/// (b) a PERMANENT external holder → the append throws <see cref="System.IO.IOException"/> after
///     <c>MaxAttempts</c> (rethrow-on-exhaustion — the terminal behavior the caller relies on).
/// </para>
/// <para>
/// <b>Windows-guarded</b>: <c>FileShare</c> enforcement (<c>ERROR_SHARING_VIOLATION</c>) is a Windows kernel
/// behavior; on Linux/macOS <c>FileShare</c> is advisory and the exclusive-holder collision does not reproduce,
/// so these tests no-op off Windows.
/// </para>
/// </summary>
public class AtomicFileWriterAppendWithRetryTests
{
    /// <summary>
    /// (a) TRANSIENT collision: an external <see cref="FileShare.None"/> holder excludes the append's first
    /// attempt(s), then releases WELL WITHIN the retry window (~40ms release vs the ~150ms+ exhaustion floor
    /// of BackoffMs 50+100 plus jitter). EXPECTED: <c>AppendWithRetry</c> returns without throwing and the file
    /// content ends with the appended line (the append landed after the holder released).
    /// <para>
    /// <b>Retry-engagement floor</b>: the call is wrapped in a <see cref="Stopwatch"/> and asserted to take
    /// ≥ ~35ms — proving the FIRST attempt actually collided with the holder and the retry WAITED for the
    /// ~40ms release (first backoff base 50ms). Without this floor the pin is vacuous: if the first attempt
    /// were scheduled AFTER the holder released, it would succeed on attempt 1 without exercising the retry
    /// path, yet the no-throw + content assertions would still pass (false green). The margins (holder ~40ms,
    /// first backoff base 50ms) keep the ≥35ms assert robust: a genuine retry can return no earlier than the
    /// ~40ms release, well above the 35ms floor.
    /// </para>
    /// </summary>
    [Fact]
    public void AppendWithRetry_TransientHolderReleasedMidWindow_SucceedsAndLinePresent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FileShare enforcement is Windows-specific; collision does not reproduce elsewhere.
        }

        var path = MakeUniqueTempPath();
        try
        {
            const string line = "transient-line\n";

            // External holder excludes concurrent writers (FileShare.None) — the append's first attempt throws
            // ERROR_SHARING_VIOLATION and enters the jittered-backoff retry. Released after ~40ms: comfortably
            // between attempt 1 (t~0) and the ~150ms+ exhaustion floor, so a later attempt succeeds.
            var holder = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var releaser = Task.Run(async () =>
            {
                await Task.Delay(40);
                holder.Dispose();
            });

            // EXPECTED: append succeeds (retry rides out the transient lock) AND takes ≥ ~35ms (retry engaged —
            // the first attempt collided and waited for the ~40ms holder release). ACTUAL: captured from the
            // stopwatch elapsed, the call returning without throwing, and the on-disk content read below.
            var sw = Stopwatch.StartNew();
            AtomicFileWriter.AppendWithRetry(path, line);
            sw.Stop();
            releaser.GetAwaiter().GetResult();

            // Retry-engagement floor (closes the vacuous-pass window): a success that skipped the retry would
            // return in well under 35ms; a genuine retry cannot return before the ~40ms holder release.
            Assert.True(
                sw.ElapsedMilliseconds >= 35,
                $"Expected the retry to engage (elapsed ≥ 35ms proving the first attempt collided and waited for the ~40ms holder release), but the call returned in {sw.ElapsedMilliseconds}ms — the retry path was not exercised (vacuous pass).");

            var actual = File.ReadAllText(path);
            Assert.EndsWith(line, actual, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteTemp(path);
        }
    }

    /// <summary>
    /// (b) PERMANENT collision: an external <see cref="FileShare.None"/> holder is held for the ENTIRE call
    /// (never released). EXPECTED: <c>AppendWithRetry</c> exhausts its bounded attempts and the final
    /// <see cref="IOException"/> PROPAGATES — pinning the rethrow-on-exhaustion contract the VOW debounce-0
    /// path relies on (the fault surfaces to the AttributionSink continuation; debounce paths swallow it).
    /// </summary>
    [Fact]
    public void AppendWithRetry_PermanentHolder_ThrowsIOExceptionAfterExhaustion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // FileShare enforcement is Windows-specific; collision does not reproduce elsewhere.
        }

        var path = MakeUniqueTempPath();
        // Held for the whole call — every attempt collides, so the retry budget exhausts and the final throw
        // propagates (using-scope keeps the exclusive holder open across the entire AppendWithRetry call).
        using var holder = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        try
        {
            Assert.Throws<IOException>(() => AtomicFileWriter.AppendWithRetry(path, "never-lands\n"));
        }
        finally
        {
            holder.Dispose();
            TryDeleteTemp(path);
        }
    }

    private static string MakeUniqueTempPath() =>
        Path.Combine(Path.GetTempPath(), $"AtomicFileWriterAppendWithRetryTests.{Guid.NewGuid():N}.tmp");

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup — a lingering test temp must never fail the test.
        }
    }
}
