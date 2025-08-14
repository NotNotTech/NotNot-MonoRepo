using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NotNot;

/// <summary>
/// Provides a simple cross-process "run once" async initializer using only .NET synchronization primitives
/// (named Mutex + named EventWaitHandle) without any filesystem side-channel.
/// WHY: Ensure expensive or destructive initialization (eg database reset) runs exactly once per process cluster start
/// while avoiding marker files (IO, cleanup concerns, race-on-filesystem, platform path issues).
/// HOW:
///   1. In-process fast path via ConcurrentDictionary<string, Lazy<Task>>
///   2. Cross-process critical section via named Global mutex (Windows) / local name (other OS)
///   3. Completion signaled by a named EventWaitHandle (ManualReset) => subsequent processes skip init delegate.
///      - Pattern: if event already signaled BEFORE entering mutex => skip
///                 else acquire mutex; if event signaled AFTER lock => skip; else run delegate then Set() the event.
/// </summary>
public static class MutexUtil
{
	private static readonly ConcurrentDictionary<string, Lazy<Task>> _inProc = new();

	/// <summary>
	/// Execute <paramref name="init"/> exactly once across all callers (and processes) until the process cluster ends (marker cleared manually or temp cleaned).
	/// </summary>
	public static Task RunOnce(string name, Func<Task> init, ILogger? logger = null, bool persistent = true)
	{
		// Local fast-path: if we've already scheduled/ran it in this process, return the same Task.
		var lazy = _inProc.GetOrAdd(name, n => new Lazy<Task>(() => Execute(n, init, logger), LazyThreadSafetyMode.ExecutionAndPublication));
		return lazy.Value;
	}

	private static async Task Execute(string name, Func<Task> init, ILogger? logger)
	{
		// Named handles — use a consistent prefix to avoid collisions.
		string mutexName = $"Global\\Cleartrix_{name}_Mtx"; // 'Global' ignored/non-special on non-Windows; safe fallback.
		string evtName = $"Global\\Cleartrix_{name}_Evt";

		bool createdNewEvent;
		// ManualReset: once Set, stays signaled until OS destroys handle when last reference closes.
		using var completedEvent = new EventWaitHandle(false, EventResetMode.ManualReset, evtName, out createdNewEvent);
		if (!createdNewEvent && completedEvent.WaitOne(0))
		{
			// Another process already finished init before we grabbed mutex.
			logger?._EzInfo($"MutexUtil[{name}] skipped (already signaled)");
			return;
		}

		using var mutex = new Mutex(false, mutexName);
		mutex.WaitOne();
		try
		{
			// Re-check completion after entering critical section to avoid duplicate execution.
			if (completedEvent.WaitOne(0))
			{
				logger?._EzInfo($"MutexUtil[{name}] skipped (signaled after lock)");
				return;
			}
			logger?._EzInfo($"MutexUtil[{name}] executing init delegate...");
			await init().ConfigureAwait(false);
			completedEvent.Set();
			logger?._EzInfo($"MutexUtil[{name}] init complete (event signaled)");
		}
		finally
		{
			try { mutex.ReleaseMutex(); } catch { /* ignored */ }
		}
	}
}
