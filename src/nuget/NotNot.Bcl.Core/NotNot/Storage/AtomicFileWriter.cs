using System.Collections.Concurrent;
using System.Text;

namespace NotNot.Storage;

/// <summary>
/// Concurrency-safe atomic file writer: writes content to a per-call UNIQUE temp file, then
/// atomically replaces the final file via rename. The unit of work (write + replace) is serialized
/// per final-path and bounded-retried on transient external locks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity guarantee</b>: content is first written to a temp file that NO concurrent writer
/// shares, then <see cref="File.Move(string, string, bool)"/> (a filesystem rename) replaces the
/// final file. A reader of the final path therefore always observes either the prior complete file
/// or a new complete file — never a torn / partially-written file.
/// </para>
/// <para>
/// <b>Concurrency-safety guarantee (per-path serialization + unique temp)</b>: two correctness
/// mechanisms are BOTH required — removing either re-opens a race. This is the regression class
/// the originating investigation flagged; do NOT "simplify" by dropping one.
/// <list type="number">
/// <item>
/// <b>Unique temp name</b> (<c>finalPath + "." + Guid + ".tmp"</c>): every writer gets a PRIVATE
/// temp file, so two concurrent writers never open the SAME <c>.tmp</c> path. This eliminates the
/// captured <c>WriteAllText</c>-on-shared-<c>.tmp</c> sharing violation (on Windows, the default
/// <c>FileShare.Read</c> on the temp open causes the second same-path writer to throw
/// <c>ERROR_SHARING_VIOLATION</c>). Necessary, but NOT sufficient on its own.
/// </item>
/// <item>
/// <b>Per-final-path write serialization</b>: with unique temp names, two racers each still
/// <c>Move(uniqueTmp → SAME finalPath, overwrite:true)</c>. Concurrent
/// <c>MoveFileEx</c>/<c>MOVEFILE_REPLACE_EXISTING</c> calls to ONE destination are not mutually
/// safe — the losing rename can throw. So the ENTIRE write+replace pair is serialized per
/// normalized final path. The lock keyed store (<see cref="_gates"/>) is keyed on the
/// <see cref="Path.GetFullPath(string)"/> result, case-folded on case-insensitive Windows
/// (<see cref="GateKey"/>): on Windows equivalent spellings (<c>C:\Foo\x</c> and <c>c:\foo\X</c>)
/// resolve to ONE gate and mutually exclude; on case-sensitive platforms (Linux) only the exact-case
/// full path shares a gate. Only the lock KEY is case-folded — the actual <c>finalPath</c>/<c>tempPath</c>
/// used for file I/O remain exact-case. A single
/// <see cref="SemaphoreSlim"/>-per-key store serves BOTH overloads: the sync overload uses
/// <see cref="SemaphoreSlim.Wait()"/>, the async overload uses
/// <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>. A <see cref="SemaphoreSlim"/> (not a
/// monitor <c>lock</c>) is the chosen primitive because a monitor cannot be held across an
/// <c>await</c> — the async overload requires an async-compatible lock, and sharing ONE store
/// across both overloads is what makes a sync write and an async write to the same path mutually
/// exclude. The gate store is process-lifetime; key cardinality is bounded by distinct file paths
/// (low), so entries are intentionally never evicted (a reaper would add complexity for negligible
/// memory).
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Retry semantics (external-lock tolerance ONLY)</b>: the locked write+replace is wrapped in a
/// small bounded retry on transient <see cref="IOException"/> /
/// <see cref="UnauthorizedAccessException"/>. This exists SOLELY to ride out a transient EXTERNAL
/// lock — an anti-virus scanner or filesystem indexer momentarily holding the freshly-created temp
/// or final file. It is explicitly NOT used to mask the internal same-destination Move race: that
/// race is fully PREVENTED by the per-path lock above (no-hacks — a fully-serializable internal
/// race is serialized, not retried-around). On retry-budget exhaustion the final exception
/// propagates so a genuine persistent failure stays VISIBLE to the caller (never silently
/// swallowed). Each failed attempt best-effort deletes its own unique temp so failures never litter
/// <c>.tmp</c> files.
/// </para>
/// </remarks>
public static class AtomicFileWriter
{
	/// <summary>
	/// Per-normalized-final-path async-compatible gate. Shared by BOTH the sync and async overloads
	/// (sync = <see cref="SemaphoreSlim.Wait()"/>, async = <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>)
	/// so a sync write and an async write to the same path mutually exclude. Process-lifetime; never evicted (see remarks).
	/// </summary>
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

	/// <summary>Max attempts of the locked write+replace under transient external-lock failure.</summary>
	private const int MaxAttempts = 3;

	private static SemaphoreSlim GetGate(string gateKey) =>
		_gates.GetOrAdd(gateKey, static _ => new SemaphoreSlim(1, 1));

	/// <summary>
	/// Derives the per-path LOCK-KEY from an already <see cref="Path.GetFullPath(string)"/>-normalized
	/// path. On case-insensitive Windows the key is upper-cased so equivalent spellings
	/// (<c>C:\Foo\x</c> vs <c>c:\foo\X</c>) resolve to ONE gate and mutually exclude; on case-sensitive
	/// platforms (Linux) the exact-case full path is the key. This affects ONLY the dictionary key used
	/// for locking — the real <c>finalPath</c>/<c>tempPath</c> used for file I/O stay exact-case.
	/// </summary>
	private static string GateKey(string fullPath) =>
		OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;

	/// <summary>
	/// Atomically and concurrency-safely writes <paramref name="contents"/> to
	/// <paramref name="finalPath"/> (synchronous). Use for synchronous callers (e.g. repository
	/// <c>Save*</c> methods). Serializes per final path; bounded-retries transient external locks;
	/// throws on final failure.
	/// </summary>
	/// <param name="finalPath">Destination file path. On Windows, case-equivalent spellings share one write gate; on case-sensitive platforms only exact-case spellings do.</param>
	/// <param name="contents">File content.</param>
	/// <param name="encoding">Optional encoding (default: UTF-8 without BOM, matching <see cref="File.WriteAllText(string, string)"/>).</param>
	public static void WriteAtomic(string finalPath, string contents, Encoding? encoding = null)
	{
		ArgumentNullException.ThrowIfNull(finalPath);
		ArgumentNullException.ThrowIfNull(contents);

		var full = Path.GetFullPath(finalPath);
		var gate = GetGate(GateKey(full));
		gate.Wait();
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				var tempPath = MakeTempPath(full);
				try
				{
					if (encoding is null)
						File.WriteAllText(tempPath, contents);
					else
						File.WriteAllText(tempPath, contents, encoding);
					File.Move(tempPath, full, overwrite: true);
					return;
				}
				catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
				{
					TryDelete(tempPath);
					Thread.Sleep(BackoffMs(attempt));
				}
				catch
				{
					// Final attempt (or non-transient): clean up the unique temp, then let it propagate
					// so a genuine persistent failure stays visible to the caller.
					TryDelete(tempPath);
					throw;
				}
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Atomically and concurrency-safely writes <paramref name="contents"/> to
	/// <paramref name="finalPath"/> (asynchronous). Use for asynchronous callers (e.g.
	/// <see cref="FileStorageAdapter"/>). Serializes per final path (mutually exclusive with the
	/// synchronous overload on the same path); bounded-retries transient external locks; throws on
	/// final failure.
	/// </summary>
	/// <param name="finalPath">Destination file path. On Windows, case-equivalent spellings share one write gate; on case-sensitive platforms only exact-case spellings do.</param>
	/// <param name="contents">File content.</param>
	/// <param name="encoding">Optional encoding (default: UTF-8 without BOM, matching <see cref="File.WriteAllTextAsync(string, string, CancellationToken)"/>).</param>
	/// <param name="ct">Cancellation token.</param>
	public static async Task WriteAtomicAsync(string finalPath, string contents, Encoding? encoding = null, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(finalPath);
		ArgumentNullException.ThrowIfNull(contents);

		var full = Path.GetFullPath(finalPath);
		var gate = GetGate(GateKey(full));
		await gate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			for (var attempt = 1; ; attempt++)
			{
				var tempPath = MakeTempPath(full);
				try
				{
					if (encoding is null)
						await File.WriteAllTextAsync(tempPath, contents, ct).ConfigureAwait(false);
					else
						await File.WriteAllTextAsync(tempPath, contents, encoding, ct).ConfigureAwait(false);
					File.Move(tempPath, full, overwrite: true);
					return;
				}
				catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
				{
					TryDelete(tempPath);
					await Task.Delay(BackoffMs(attempt), ct).ConfigureAwait(false);
				}
				catch
				{
					TryDelete(tempPath);
					throw;
				}
			}
		}
		finally
		{
			gate.Release();
		}
	}

	private static string MakeTempPath(string normalizedFinalPath) =>
		normalizedFinalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

	/// <summary>
	/// Transient = an EXTERNAL lock (AV/indexer) on the temp/final file, surfaced as
	/// <see cref="IOException"/> (incl. ERROR_SHARING_VIOLATION) or <see cref="UnauthorizedAccessException"/>.
	/// </summary>
	private static bool IsTransient(Exception ex) => ex is IOException or UnauthorizedAccessException;

	/// <summary>Short linear backoff (50ms, 100ms) — these are sub-second external locks, not contention.</summary>
	private static int BackoffMs(int attempt) => 50 * attempt;

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Best-effort cleanup: a leftover unique .tmp must NEVER escalate / mask the real write
			// failure, so we deliberately do not rethrow. DebugAssertOnce keeps the swallow OBSERVABLE
			// (fires once in DEBUG, no-op in RELEASE) instead of silent — satisfying NN_R005/NN_R006
			// without breaking best-effort semantics. Narrowed to the only exceptions a delete of our
			// own unique temp can plausibly raise (file locked by AV/indexer, or ACL); anything else
			// is unexpected and propagates.
			__.DebugAssertOnce(ex);
		}
	}
}
