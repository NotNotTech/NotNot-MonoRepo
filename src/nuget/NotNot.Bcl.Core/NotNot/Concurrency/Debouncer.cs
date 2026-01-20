namespace NotNot.Concurrency;

/// <summary>
/// Trailing-edge debouncer using <see cref="Timer.Change(int, int)"/> pattern.
/// Reschedules without throwing exceptions or allocating per trigger.
/// Thread-safe for <see cref="Trigger"/> and <see cref="Dispose"/>; see remarks for async callback race window.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a trailing-edge debouncer.</b> The action executes ONCE after activity stops.
/// For throttle/rate-limiting (minimum time between executions), use <see cref="KeyedThrottler"/>.
/// </para>
/// <para>
/// <b>Key differences from <see cref="KeyedThrottler"/>:</b>
/// <list type="bullet">
///   <item><b>Debouncer</b>: Executes ONCE after activity stops. Multiple rapid calls result in single execution after quiet period.</item>
///   <item><b>KeyedThrottler</b>: Ensures minimum time BETWEEN executions. Multiple rapid calls result in multiple executions spaced by MinDelay.</item>
/// </list>
/// </para>
/// <para>
/// Unlike CancellationTokenSource+Task.Delay pattern, this approach:
/// <list type="bullet">
///   <item>Throws zero exceptions per debounce reset</item>
///   <item>Allocates zero bytes per trigger (Timer reuse)</item>
///   <item>Creates no first-chance exception debugger noise</item>
/// </list>
/// </para>
/// <para>
/// Timer callbacks run on ThreadPool. If updating UI state,
/// marshal to the appropriate synchronization context (e.g., Blazor's <c>InvokeAsync</c>,
/// WPF's <c>Dispatcher.Invoke</c>, or WinForms' <c>Control.Invoke</c>).
/// </para>
/// <para>
/// <b>Disposal Race Window:</b> Because the lock is released before awaiting
/// the action, a race window exists where <see cref="Dispose"/> may complete
/// between the disposed check and action execution. Consumer code MUST verify
/// its own disposal state within the action callback:
/// <code>
/// var debouncer = new Debouncer(
///     async () =&gt;
///     {
///         if (_isDisposed) return;  // Guard inside callback
///         await DoWorkAsync();
///     },
///     delayMs: 150);
/// </code>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Basic usage
/// var debouncer = new Debouncer(
///     async () =&gt; await SaveDataAsync(),
///     delayMs: 300);
///
/// // Rapid calls - only last one fires after 300ms quiet period
/// debouncer.Trigger();  // Ignored
/// debouncer.Trigger();  // Ignored
/// debouncer.Trigger();  // This one fires after 300ms
///
/// // Cleanup
/// debouncer.Dispose();
/// </code>
/// </example>
public sealed class Debouncer : IDisposable
{
	private readonly Timer _timer;
	private readonly Func<Task> _action;
	private readonly int _delayMs;
	private readonly object _lock = new();
	private bool _disposed;

	/// <summary>
	/// Creates a new trailing-edge debouncer.
	/// </summary>
	/// <param name="action">
	/// Action to execute after the debounce delay.
	/// For UI frameworks, wrap in the appropriate synchronization context
	/// (e.g., Blazor's <c>InvokeAsync</c>, WPF's <c>Dispatcher.Invoke</c>).
	/// </param>
	/// <param name="delayMs">Debounce delay in milliseconds.</param>
	public Debouncer(Func<Task> action, int delayMs)
	{
		__.ThrowIfNot(delayMs >= 0, "delayMs must be non-negative");
		_action = action ?? throw new ArgumentNullException(nameof(action));
		_delayMs = delayMs;
		_timer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
	}

	/// <summary>
	/// Schedules or reschedules the debounced action.
	/// No exceptions thrown, no allocations.
	/// </summary>
	public void Trigger()
	{
		lock (_lock)
		{
			if (_disposed) return;
			_timer.Change(_delayMs, Timeout.Infinite);
		}
	}

	private async void OnTimerElapsed(object? state)
	{
		lock (_lock)
		{
			if (_disposed) return;
		}

		try
		{
			await _action();
		}
		catch (ObjectDisposedException)
		{
			// Expected during disposal - component may have been disposed
			// between timer firing and callback execution
		}
		// Async void timer callback MUST NOT propagate exceptions - they would crash the process.
		// This is the correct pattern for fire-and-forget Timer callbacks.
#pragma warning disable NN_R005
		catch (Exception ex)
#pragma warning restore NN_R005
		{
			// Timer callbacks are async void - unhandled exceptions would crash the process.
			// Use __.DebugAssert for visibility during development.
			__.DebugAssert(ex);
		}
	}

	/// <summary>
	/// Disposes the debouncer and stops any pending timer callbacks.
	/// </summary>
	public void Dispose()
	{
		lock (_lock)
		{
			if (_disposed) return;
			_disposed = true;
		}
		_timer.Dispose();
	}
}
