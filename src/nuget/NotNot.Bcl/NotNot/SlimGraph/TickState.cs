// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.SlimGraph;

public record class TickState
{
	public readonly TemporalDelta _storage;

	public float Speed => _storage.Speed;
	public TimeSpan Elapsed => _storage.ElapsedTime;
	public TimeSpan Time => _storage.Time;
	public int Turn => _storage.Turn;
	public ulong UpdateTickCount => _storage.UpdateTickCount;


	public TickState(TemporalDelta storage)
	{
		_storage = storage;
	}

	//public static TickState ComputeNext(TickState previous, TimeSpan realTimeDelta)
	//{
	//	var internalNext = previous._storage.ComputeElapse(realTimeDelta);
	//	return new TickState(internalNext);
	//}

	public static TickState Default => new TickState(TemporalDelta.Default);




	public TemporalDelta ComputeElapse(TickState next, bool allowNegative = false)
	{
		var detail = this._storage.ComputeElapse(next._storage, allowNegative);
		return detail;
	}

	public TickState ComputeElapse(TimeSpan realTimeDelta, bool allowNegative = false)
	{
		var internalNext = this._storage.ComputeElapse(realTimeDelta, allowNegative);
		return new TickState(internalNext);
	}

}


public record struct TemporalDelta
{
	/// <summary>
	/// speed at which time is passing.   1 = real time, 2 = double speed, 0.5 = half speed.
	/// </summary>
	public required float Speed { get; init; }
	/// <summary>
	/// elapsed since last tick, adjusted for speed.
	/// </summary>
	public required TimeSpan ElapsedTime { get; init; }
	/// <summary>
	/// total time the simulation has been running, adjusted for speed.
	/// </summary>
	public required TimeSpan Time { get; init; }
	/// <summary>
	/// the current turn number.   usually turns elapse 1 per second.
	/// <para>starts at zero</para>
	/// <para>useful (but not required) as a simple mechanism rules coordination for nodes</para>
	/// </summary>
	public required int Turn { get; init; }
	public required int ElapsedTurns { get; init; }


	/// <summary>
	/// the count of updates (frame ticks) that have occured over the NodeTree's lifetime.
	/// </summary>
	public required ulong UpdateTickCount { get; init; }
	public required ulong ElapsedTicks { get; init; }

	public static TemporalDelta Empty { get; } = new()
	{
		Speed = 0,
		ElapsedTime = TimeSpan.Zero,
		Time = TimeSpan.Zero,
		Turn = 0,
		UpdateTickCount = 0,
		ElapsedTicks = 0,
		ElapsedTurns = 0,
	};

	public static TemporalDelta Default { get; } = new()
	{
		Speed = 1,
		ElapsedTime = TimeSpan.Zero,
		Time = TimeSpan.Zero,
		Turn = 0,
		UpdateTickCount = 0,
		ElapsedTicks = 0,
		ElapsedTurns = 0,
	};

	///// <summary>
	///// Computes the elapsed time from a prior TickState to this one.
	///// Returns TimeSpan.Zero if prior is null or if delta would be negative.
	///// </summary>
	///// <param name="prior">The earlier TickState to compute delta from, or null for zero delta</param>
	///// <returns>Non-negative elapsed time since prior tick</returns>
	//public TimeSpan ElapsedSince(TemporalDetail prior)
	//{
	//	if (prior == null)
	//		return TimeSpan.Zero;
	//	var delta = this.Time - prior.Time;
	//	return delta >= TimeSpan.Zero ? delta : TimeSpan.Zero;
	//}

	/// <summary>
	/// given the current TemporalDetail, will compute the delta to the next TemporalDetail.
	/// <para>.speed of next is returned.</para>
	/// </summary>
	/// <param name="next"></param>
	/// <returns></returns>
	public TemporalDelta ComputeElapse(TemporalDelta next, bool allowNegative = false)
	{
		var elapsed = next.Time - this.Time;
		var elapsedTurns = next.Turn - this.Turn;
		var elapsedTicks = next.UpdateTickCount - this.UpdateTickCount;

		if (elapsed < TimeSpan.Zero && allowNegative is false)
		{
			//			elapsed = TimeSpan.Zero;
			throw __.Throw("negative elapsed not allowed");
		}
		return new TemporalDelta()
		{
			Speed = next.Speed,
			ElapsedTime = elapsed,
			ElapsedTurns = elapsedTurns,
			ElapsedTicks = elapsedTicks,
			Time = next.Time,
			Turn = next.Turn,
			UpdateTickCount = next.UpdateTickCount,
		};
	}

	/// <summary>
	/// given the current TemporalDetail, will compute the next TemporalDetail after the given realTimeDelta.
	/// </summary>
	/// <param name="realTimeDelta"></param>
	/// <returns></returns>
	public TemporalDelta ComputeElapse(TimeSpan realTimeDelta, bool allowNegative = false)
	{
		var elapsed = realTimeDelta * Speed;
		if (elapsed < TimeSpan.Zero && allowNegative is false)
		{
			//			elapsed = TimeSpan.Zero;
			throw __.Throw("negative elapsed not allowed");
		}
		var newTime = Time + elapsed;
		var turn = newTime.Seconds;

		var toReturn = new TemporalDelta()
		{
			Speed = Speed,
			ElapsedTime = elapsed,
			ElapsedTurns = turn - this.Turn,
			ElapsedTicks = 1,
			Turn = newTime.Seconds,
			UpdateTickCount = UpdateTickCount + 1,
			Time = newTime,
		};

		return toReturn;
	}
}
