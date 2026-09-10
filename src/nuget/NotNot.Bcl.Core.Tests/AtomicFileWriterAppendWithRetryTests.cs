using System.Runtime.ExceptionServices;
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
    /// attempt, then releases synchronously when this test observes the actual target sharing violation.
    /// EXPECTED: <c>AppendWithRetry</c> returns without throwing and the file content ends with the appended line
    /// (the append landed after the holder released).
    /// <para>
    /// <b>Collision evidence</b>: a narrowly filtered <see cref="AppDomain.FirstChanceException"/> handler runs on
    /// the invoking thread, matches the Windows sharing-violation HRESULT and this unique target path, and releases
    /// the holder inside the first actual failed open. This proves the retry path without relying on thread-pool
    /// scheduling or wall-clock timing. The handler never asserts or intercepts unrelated exceptions.
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

            // External holder excludes concurrent writers (FileShare.None). The first actual sharing violation
            // releases it synchronously from the narrowly filtered FirstChanceException handler below.
            var holder = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var invokingThreadId = Environment.CurrentManagedThreadId;
            var normalizedPath = Path.GetFullPath(path);
            var collisionObserved = 0;
            const int sharingViolationHResult = unchecked((int)0x80070020);
            void ReleaseHolderOnTargetSharingViolation(object? _, FirstChanceExceptionEventArgs args)
            {
                if (Environment.CurrentManagedThreadId != invokingThreadId
                    || args.Exception is not IOException io
                    || io.HResult != sharingViolationHResult
                    || !io.Message.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase)
                    || Interlocked.Exchange(ref collisionObserved, 1) != 0)
                {
                    return;
                }

                try
                {
                    holder.Dispose();
                }
                catch (IOException)
                {
                    // The append owns the assertion; a release failure leaves the real retry outcome visible.
                }
            }

            AppDomain.CurrentDomain.FirstChanceException += ReleaseHolderOnTargetSharingViolation;
            try
            {
                // EXPECTED: the first attempt collides, the handler releases the holder, and a later retry
                // succeeds. The explicit collision flag closes the vacuous-pass window without timing assumptions.
                AtomicFileWriter.AppendWithRetry(path, line);
                Assert.Equal(1, Volatile.Read(ref collisionObserved));

                var actual = File.ReadAllText(path);
                Assert.EndsWith(line, actual, StringComparison.Ordinal);
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= ReleaseHolderOnTargetSharingViolation;
                holder.Dispose();
            }
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
