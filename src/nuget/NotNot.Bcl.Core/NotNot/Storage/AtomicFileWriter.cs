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

		WriteAtomicCore(finalPath, tempPath =>
		{
			if (encoding is null)
				File.WriteAllText(tempPath, contents);
			else
				File.WriteAllText(tempPath, contents, encoding);
		});
	}

	/// <summary>
	/// Atomically and concurrency-safely writes <paramref name="lines"/> to <paramref name="finalPath"/>
	/// (synchronous, line-oriented — the <see cref="File.WriteAllLines(string, IEnumerable{string})"/>
	/// analogue of <see cref="WriteAtomic(string, string, Encoding?)"/>). Use for line-delimited callers
	/// (e.g. JSONL trims / appends rewritten in place). Shares the SAME per-path serialization, unique
	/// temp, and bounded external-lock retry as the string overload — the lines are written to the per-call
	/// unique temp, then an atomic rename replaces the final file (so a reader never observes a torn file).
	/// </summary>
	/// <param name="finalPath">Destination file path. On Windows, case-equivalent spellings share one write gate; on case-sensitive platforms only exact-case spellings do.</param>
	/// <param name="lines">Lines to write (one per element), via <see cref="File.WriteAllLines(string, IEnumerable{string})"/> semantics.</param>
	/// <param name="encoding">Optional encoding (default: UTF-8 without BOM, matching <see cref="File.WriteAllLines(string, IEnumerable{string})"/>).</param>
	public static void WriteAtomic(string finalPath, IEnumerable<string> lines, Encoding? encoding = null)
	{
		ArgumentNullException.ThrowIfNull(finalPath);
		ArgumentNullException.ThrowIfNull(lines);

		WriteAtomicCore(finalPath, tempPath =>
		{
			if (encoding is null)
				File.WriteAllLines(tempPath, lines);
			else
				File.WriteAllLines(tempPath, lines, encoding);
		});
	}

	/// <summary>
	/// Shared synchronous core for the string + lines overloads: serializes per normalized final path,
	/// writes to a per-call UNIQUE temp via <paramref name="writeTempContent"/>, then atomic-renames it
	/// into place under a bounded transient-external-lock retry. Factored so the gate + retry machinery has
	/// ONE owner (the unique-temp + per-path-lock concurrency guard is identical regardless of payload shape).
	/// </summary>
	private static void WriteAtomicCore(string finalPath, Action<string> writeTempContent)
	{
		var full = Path.GetFullPath(finalPath);
		var gate = GetGate(GateKey(full));
		gate.Wait();
		try
		{
			// NOTE: `while (true)` (not `for (var attempt = 1; ; attempt++)`): an empty-condition
			// for-loop NRE-crashes ParallelHelper's PH_P008 analyzer (surfaced as AD0001), which
			// silently disables that rule for the whole compilation when the enclosing method is async.
			// The sync siblings mirror the async form for consistency + latent-async safety. `attempt`
			// is hoisted above and incremented at the loop tail so the retry count is IDENTICAL.
			var attempt = 1;
			while (true)
			{
				var tempPath = MakeTempPath(full);
				try
				{
					writeTempContent(tempPath);
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
				attempt++;
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
			// `while (true)` (not empty-condition `for`): see WriteAtomicCore — an empty-condition
			// for-loop NRE-crashes ParallelHelper's PH_P008 analyzer (AD0001) in async methods,
			// silently disabling the rule for the whole compilation. `attempt` is hoisted + tail-
			// incremented so the retry count is IDENTICAL to the original for-loop.
			var attempt = 1;
			while (true)
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
				attempt++;
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Appends <paramref name="contents"/> to <paramref name="finalPath"/> under the SAME bounded
	/// transient-external-lock retry policy the <see cref="WriteAtomic(string, string, Encoding?)"/>
	/// overloads use (<see cref="IsTransient"/> classification, <see cref="MaxAttempts"/>,
	/// <see cref="BackoffMs"/>), delivered as an O(1) direct append rather than the O(n) temp+rename
	/// rewrite of <c>WriteAtomic</c>. Intended for hot per-event line appends (e.g. JSONL capture logs).
	/// <para>
	/// <b>Package / namespace</b>: <c>NotNot.Bcl.Core</c> → <c>NotNot.Storage.AtomicFileWriter</c>
	/// (<c>using NotNot.Storage;</c>). Framework-agnostic (no ASP.NET dependency).
	/// </para>
	/// <para>
	/// <b>Retry contract</b>: each attempt is <see cref="File.AppendAllText(string, string)"/>, which opens
	/// <c>FileMode.Append</c> + <c>FileShare.Read</c> — a concurrent WRITER is excluded, so each append is
	/// whole (never a torn line); the loser retries. On a transient <see cref="IOException"/> (an AV/indexer
	/// OR another process momentarily holding the file — the cross-process case) it sleeps a JITTERED backoff
	/// and retries. Unlike <c>WriteAtomic*</c> (whose per-path <see cref="SemaphoreSlim"/> gate already
	/// serializes same-process retries, so jitter would be pointless), this append has NO such gate — jitter
	/// is REQUIRED here so two independent PROCESSES retrying on a fixed cadence do not re-collide in lockstep.
	/// </para>
	/// <para>
	/// <b>Rethrows on exhaustion</b>: after <see cref="MaxAttempts"/> genuine collisions the final
	/// <see cref="IOException"/> PROPAGATES (a persistent failure stays visible; the caller decides whether to
	/// swallow) — it is NOT silently dropped.
	/// </para>
	/// <para>
	/// <b>Synchronous by contract</b>: uses <see cref="Thread.Sleep(int)"/> (not <c>await Task.Delay</c>) so it
	/// is callable from inside a monitor <c>lock</c> (which cannot span an <c>await</c>) — the intended VOW
	/// quota-append call site appends under a per-account monitor.
	/// </para>
	/// </summary>
	/// <param name="finalPath">Destination file path to append to (created if absent).</param>
	/// <param name="contents">Content to append.</param>
	/// <param name="encoding">Optional encoding (default: UTF-8 without BOM, matching <see cref="File.AppendAllText(string, string)"/>).</param>
	public static void AppendWithRetry(string finalPath, string contents, Encoding? encoding = null)
	{
		ArgumentNullException.ThrowIfNull(finalPath);
		ArgumentNullException.ThrowIfNull(contents);

		// `while (true)` (not empty-condition `for`): see WriteAtomicCore — an empty-condition
		// for-loop NRE-crashes ParallelHelper's PH_P008 analyzer (AD0001) in async methods, silently
		// disabling the rule for the whole compilation; the sync siblings mirror the form for
		// consistency + latent-async safety. `attempt` is hoisted + tail-incremented so the retry
		// count is IDENTICAL to the original for-loop.
		var attempt = 1;
		while (true)
		{
			try
			{
				if (encoding is null)
					File.AppendAllText(finalPath, contents);
				else
					File.AppendAllText(finalPath, contents, encoding);
				return;
			}
			catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
			{
				// Transient external lock (AV/indexer OR another PROCESS momentarily holding the file):
				// jittered backoff breaks cross-process lockstep, then retry. The final attempt does NOT
				// enter this catch (attempt < MaxAttempts guard) — its exception propagates (rethrow-on-exhaustion).
				// IsTransient is the SOLE classifier (catch Exception, identical to WriteAtomicCore) so a transient
				// UnauthorizedAccessException (AV/indexer ACCESS_DENIED) is retried the same as a sharing-violation IOException.
				// Jitter derives from BackoffMs (up to +100% of the base) so it tracks the backoff base with no duplicated literal.
				Thread.Sleep(BackoffMs(attempt) + Random.Shared.Next(0, BackoffMs(attempt)));
			}
			attempt++;
		}
	}

	private static string MakeTempPath(string normalizedFinalPath) =>
		normalizedFinalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

	/// <summary>
	/// Transient = an EXTERNAL lock (AV/indexer) on the temp/final file, surfaced as
	/// <see cref="IOException"/> (incl. ERROR_SHARING_VIOLATION) or <see cref="UnauthorizedAccessException"/>.
	/// </summary>
	private static bool IsTransient(Exception ex) => ex is IOException or UnauthorizedAccessException;

	/// <summary>
	/// Short linear backoff base (50ms, 100ms) — these are sub-second external locks, not contention.
	/// The <c>WriteAtomic*</c> paths use it bare (their per-path <see cref="SemaphoreSlim"/> gate already
	/// prevents same-process lockstep, so no jitter is needed). <see cref="AppendWithRetry"/> layers random
	/// jitter ON TOP of this base because it has no such gate — jitter avoids two independent PROCESSES
	/// re-colliding on a fixed retry cadence.
	/// </summary>
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
