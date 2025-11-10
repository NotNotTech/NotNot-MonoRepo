using System.Collections.Concurrent;

namespace NotNot.Concurrency;

/// <summary>
/// Allows debouncing an action given a key.  The action will be executed at least once, but not more than once per period specified by MinDelay
/// <para>duplicate (debounced) calls get a task that completes when the allowed action call finishes.</para>
/// <para>optionally can limit concurrency of multiple debounceKeys</para>
/// </summary>
public partial class Debouncer
{
	/// <summary>
	/// The minimum amount of time to wait between calls to the debounced action function.
	/// <para>Default (TimeSpan.Zero) is no delay.   value should range Zero+</para>
	/// </summary>
	public TimeSpan MinDelay { get; init; } = TimeSpan.Zero;
	/// <summary>
	/// The minimum number of action (for different debounceKeys) to execute at once.
	/// can adjust upwards based on ParallelGrowthMultiplier, which defaults to 1 (allowing all debounced actions to execute in parallel)
	/// <para>default 1.  value should range 1+</para>
	/// </summary>
	public int MinimumParallel { get; init; } = 1;

	/// <summary>
	/// allows ParallelActions to increase if there is a backlog of actions.  
	/// <para>default is 0,  value should range 0 to 1</para>
	/// <para>a value of 0 means no growth</para>
	/// <para>a value of 0.05 means 1 additional (more than DefaultParallel) request  when less than 20 enqueued actions,  2 when 20-39 actions, 3 when 40-59, etc.</para>
	/// <para>a value of 1 means all debounced actions can run in parallel. </para>
	/// </summary>
	public double ParallelGrowthMultiplier { get; init; } = 0;

	private readonly ConcurrentDictionary<object, DateTime> _nextExecutionTimes = new ConcurrentDictionary<object, DateTime>();
	private readonly ConcurrentDictionary<object, Task> _ongoingTasks = new ConcurrentDictionary<object, Task>();

	private AsyncSlots _slots;

	public Debouncer()
	{
		_slots = new AsyncSlots(MinimumParallel);

		__.ThrowIfNot(MinimumParallel >= 1);
		__.ThrowIfNot(ParallelGrowthMultiplier >= 0 && ParallelGrowthMultiplier <= 1);
		__.ThrowIfNot(MinDelay >= TimeSpan.Zero);

	}

	/// <summary>
	/// allows waiting for the debounceKey to clear the queue.   if not in queue, immediately returns success
	/// </summary>
	public async Task AwaitComplete(object debounceKey, CancellationToken ct = default)
	{
		if (_ongoingTasks.TryGetValue(debounceKey, out var ongoingTask))
		{
			//await ongoingTask;

			__.DevTrace("await the ongoingTask, or the cancelation token, whichever comes first", debounceKey);
			var result= await Task.WhenAny(ongoingTask, Task.Delay(-1, ct));
			await result;
		}
		else
		{
			__.DevTrace("nothing ongoing to await", debounceKey, _ongoingTasks.Count, MinDelay);



		}
		ct.ThrowIfCancellationRequested();
	}

	public async Task EventuallyOnce(object debounceKey, Func<Task> action, CancellationToken ct = default)
	{
		var result = await EventuallyOnce(debounceKey, async () =>
		{
			await action();
			return true;
		}, ct);
	}

	/// <summary>
	/// Execute the action for the given debounceKey at least once, but not more than once per period specified by MinDelay.
	/// </summary>
	/// <param name="debounceKey">tracks all calls to the action for this key</param>
	/// <param name="action"></param>
	/// <param name="ct">allows canceling if needed</param>
	/// <returns>a Task that resolves when the action finally completes</returns>
	public async Task<TResult> EventuallyOnce<TResult>(object debounceKey, Func<Task<TResult>> action, CancellationToken ct = default)
	{
		__.DevTrace("staring EventuallyOnce");
		var toReturn = (Task<TResult>)_ongoingTasks.GetOrAdd(debounceKey, _ => Task.Run(async () =>
		{
			__.DevTrace("inside EventuallyOnce Task.Run");
			try
			{
				{
					//wait until the minimum delay has passed since the last execution
					//as a loop so we can adjust the next execution time (see below)
					while (true)
					{
						ct.ThrowIfCancellationRequested();

						var now = DateTime.UtcNow;
						var nextExecutionTime = _nextExecutionTimes.GetOrAdd(debounceKey, now.Add(MinDelay));
						if (nextExecutionTime > now)
						{
							var delay = nextExecutionTime - now;
							delay = delay < MinDelay ? delay : MinDelay; //can't use delay directly because below we sometimes set next time to DateTime.MaxValue
							await Task.Delay(delay, ct);
						}
						else
						{
							//done waiting
							break;
						}
					}

				}
				_BalanceSlots();


				__.DevTrace($"{debounceKey} done waiting");
				try
				{
					using (await _slots.Lock(ct))
					{
						//set next exec time to infinity, so that other calls to this function will wait until this one completes
						_nextExecutionTimes[debounceKey] = DateTime.MaxValue;
						//remove from ongoing tasks before executing, so that other calls can enqueue new requests while the action is executing
						__.DevTrace($"{debounceKey} remove from ongoing tasks before executing, so that other calls can enqueue new requests while the action is executing");
						_ongoingTasks.TryRemove(debounceKey, out var _);

						ct.ThrowIfCancellationRequested();

						__.DevTrace($"{debounceKey} action start");
						var result = await action();
						__.DevTrace($"{debounceKey} action finish");

						_BalanceSlots();

						return result;
					}

				}
				finally
				{
					//remove our infinite delay
					var result = _nextExecutionTimes.TryRemove(debounceKey, out var _);
					__.AssertIfNot(result);
					ct.ThrowIfCancellationRequested();
				}
			}
			finally
			{
				__.DevTrace("exiting EventuallyOnce Task.Run", debounceKey);
			}
		}, ct));


		var isInserted = _ongoingTasks.ContainsKey(debounceKey);
		__.DevTrace("was insert successful?", isInserted, debounceKey);

		if (_ongoingTasks.TryGetValue(debounceKey, out var ongoingTask))
		{
			__.DevTrace("(_ongoingTasks.TryGetValue TRUE", debounceKey);
		}
		else
		{
			__.DevTrace("(_ongoingTasks.TryGetValue FALSE", debounceKey);
		}
		//await to bubble up any problems within this callstack
		return await toReturn;
	}

	/// <summary>
	/// potentially adjust parallel slots
	/// </summary>
	private void _BalanceSlots()
	{
		var targetMaxSlots = MinimumParallel + (int)(_ongoingTasks.Count * ParallelGrowthMultiplier);
		//adjust slots avaiable
		if (_slots.Max != targetMaxSlots)
		{
			_slots.ChangeMax(targetMaxSlots);
		}
	}
}
