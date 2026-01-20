using System.Collections.Concurrent;

namespace NotNot.Concurrency;

/// <summary>
/// Key-based throttle/rate-limiter that ensures actions are executed with minimum delay between executions.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a throttle/rate-limiter, NOT a trailing-edge debouncer.</b>
/// For trailing-edge debounce (fire once after activity stops), use <see cref="Debouncer"/>.
/// </para>
/// <para>
/// <b>Key differences from <see cref="Debouncer"/>:</b>
/// <list type="bullet">
///   <item><b>KeyedThrottler</b>: Ensures minimum time BETWEEN executions. Multiple rapid calls result in multiple executions spaced by MinDelay.</item>
///   <item><b>Debouncer</b>: Executes ONCE after activity stops. Multiple rapid calls result in single execution after quiet period.</item>
/// </list>
/// </para>
/// <para>
/// Duplicate (throttled) calls get a Task that completes when the allowed action call finishes.
/// Optionally limits concurrency of multiple throttle keys via <see cref="MinimumParallel"/> and <see cref="ParallelGrowthMultiplier"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var throttler = new KeyedThrottler { MinDelay = TimeSpan.FromSeconds(1) };
///
/// // These will execute with ~1 second between them
/// await throttler.EventuallyOnce("api-call", () => CallApiAsync());
/// await throttler.EventuallyOnce("api-call", () => CallApiAsync());
/// </code>
/// </example>
public partial class KeyedThrottler
{
	/// <summary>
	/// The minimum amount of time to wait between calls to the throttled action function.
	/// <para>Default (TimeSpan.Zero) is no delay. Value should range Zero+</para>
	/// </summary>
	public TimeSpan MinDelay { get; init; } = TimeSpan.Zero;

	/// <summary>
	/// The minimum number of actions (for different throttle keys) to execute at once.
	/// Can adjust upwards based on <see cref="ParallelGrowthMultiplier"/>.
	/// <para>Default 1. Value should range 1+</para>
	/// </summary>
	public int MinimumParallel { get; init; } = 1;

	/// <summary>
	/// Allows parallel actions to increase if there is a backlog of actions.
	/// <para>Default is 0. Value should range 0 to 1.</para>
	/// <para>A value of 0 means no growth.</para>
	/// <para>A value of 0.05 means 1 additional (more than <see cref="MinimumParallel"/>) request when less than 20 enqueued actions, 2 when 20-39 actions, 3 when 40-59, etc.</para>
	/// <para>A value of 1 means all throttled actions can run in parallel.</para>
	/// </summary>
	public double ParallelGrowthMultiplier { get; init; } = 0;

	private readonly ConcurrentDictionary<object, DateTime> _nextExecutionTimes = new();
	private readonly ConcurrentDictionary<object, Task> _ongoingTasks = new();

	private AsyncSlots _slots;

	public KeyedThrottler()
	{
		_slots = new AsyncSlots(MinimumParallel);

		__.ThrowIfNot(MinimumParallel >= 1);
		__.ThrowIfNot(ParallelGrowthMultiplier >= 0 && ParallelGrowthMultiplier <= 1);
		__.ThrowIfNot(MinDelay >= TimeSpan.Zero);
	}

	/// <summary>
	/// Waits for the throttle key to clear the queue. If not in queue, immediately returns success.
	/// </summary>
	public async Task AwaitComplete(object throttleKey, CancellationToken ct = default)
	{
		if (_ongoingTasks.TryGetValue(throttleKey, out var ongoingTask))
		{
			__.DevTrace("await the ongoingTask, or the cancellation token, whichever comes first", throttleKey);
			var result = await Task.WhenAny(ongoingTask, Task.Delay(-1, ct));
			await result;
		}
		else
		{
			__.DevTrace("nothing ongoing to await", throttleKey, _ongoingTasks.Count, MinDelay);
		}
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// Execute the action for the given throttle key at least once, but not more than once per period specified by <see cref="MinDelay"/>.
	/// </summary>
	public async Task EventuallyOnce(object throttleKey, Func<Task> action, CancellationToken ct = default)
	{
		var result = await EventuallyOnce(throttleKey, async () =>
		{
			await action();
			return true;
		}, ct);
	}

	/// <summary>
	/// Execute the action for the given throttle key at least once, but not more than once per period specified by <see cref="MinDelay"/>.
	/// </summary>
	/// <param name="throttleKey">Tracks all calls to the action for this key.</param>
	/// <param name="action">The action to execute.</param>
	/// <param name="ct">Allows canceling if needed.</param>
	/// <returns>A Task that resolves when the action finally completes.</returns>
	public async Task<TResult> EventuallyOnce<TResult>(object throttleKey, Func<Task<TResult>> action, CancellationToken ct = default)
	{
		__.DevTrace("starting EventuallyOnce");
		var toReturn = (Task<TResult>)_ongoingTasks.GetOrAdd(throttleKey, _ => Task.Run(async () =>
		{
			__.DevTrace("inside EventuallyOnce Task.Run");
			try
			{
				{
					// Wait until the minimum delay has passed since the last execution
					// as a loop so we can adjust the next execution time (see below)
					while (true)
					{
						ct.ThrowIfCancellationRequested();

						var now = DateTime.UtcNow;
						var nextExecutionTime = _nextExecutionTimes.GetOrAdd(throttleKey, now.Add(MinDelay));
						if (nextExecutionTime > now)
						{
							var delay = nextExecutionTime - now;
							delay = delay < MinDelay ? delay : MinDelay; // Can't use delay directly because below we sometimes set next time to DateTime.MaxValue
							await Task.Delay(delay, ct);
						}
						else
						{
							// Done waiting
							break;
						}
					}
				}
				_BalanceSlots();

				__.DevTrace($"{throttleKey} done waiting");
				try
				{
					using (await _slots.Lock(ct))
					{
						// Set next exec time to infinity, so that other calls to this function will wait until this one completes
						_nextExecutionTimes[throttleKey] = DateTime.MaxValue;
						// Remove from ongoing tasks before executing, so that other calls can enqueue new requests while the action is executing
						__.DevTrace($"{throttleKey} remove from ongoing tasks before executing, so that other calls can enqueue new requests while the action is executing");
						_ongoingTasks.TryRemove(throttleKey, out var _);

						ct.ThrowIfCancellationRequested();

						__.DevTrace($"{throttleKey} action start");
						var result = await action();
						__.DevTrace($"{throttleKey} action finish");

						_BalanceSlots();

						return result;
					}
				}
				finally
				{
					// Remove our infinite delay
					var result = _nextExecutionTimes.TryRemove(throttleKey, out var _);
					__.AssertIfNot(result);
					ct.ThrowIfCancellationRequested();
				}
			}
			finally
			{
				__.DevTrace("exiting EventuallyOnce Task.Run", throttleKey);
			}
		}, ct));

		var isInserted = _ongoingTasks.ContainsKey(throttleKey);
		__.DevTrace("was insert successful?", isInserted, throttleKey);

		if (_ongoingTasks.TryGetValue(throttleKey, out var ongoingTask))
		{
			__.DevTrace("(_ongoingTasks.TryGetValue TRUE", throttleKey);
		}
		else
		{
			__.DevTrace("(_ongoingTasks.TryGetValue FALSE", throttleKey);
		}
		// Await to bubble up any problems within this callstack
		return await toReturn;
	}

	/// <summary>
	/// Potentially adjust parallel slots based on current load.
	/// </summary>
	private void _BalanceSlots()
	{
		var targetMaxSlots = MinimumParallel + (int)(_ongoingTasks.Count * ParallelGrowthMultiplier);
		// Adjust slots available
		if (_slots.Max != targetMaxSlots)
		{
			_slots.ChangeMax(targetMaxSlots);
		}
	}
}
