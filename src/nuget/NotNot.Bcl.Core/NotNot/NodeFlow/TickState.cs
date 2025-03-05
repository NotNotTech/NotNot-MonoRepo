// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.NodeFlow;

public record class TickState
{
	public required float Speed { get; init; }
	public required TimeSpan Elapsed { get; init; }
	public required TimeSpan Time { get; init; }
	/// <summary>
	/// the current turn number.   usually turns elapse 1 per second.
	/// <para>starts at zero</para>
	/// <para>useful (but not required) as a simple mechanism rules coordination for nodes</para>
	/// </summary>
	public required int Turn { get; init; }

	/// <summary>
	/// the count of updates (frames) that have occured over the NodeTree's lifetime.
	/// </summary>
	public required ulong UpdateCount { get; init; }

	/// <summary>
	/// the previous tickstate, nulled out after the next tick is computed.
	/// </summary>
	public required TickState Previous
	{
		get { return this._previous; }
		init { this._previous = value; }
	}
	private TickState? _previous;


	public static TickState ComputeNext(TickState? previous, TimeSpan timeDelta)
	{
		previous ??= TickState.Default;
		var justElapsed = timeDelta * previous.Speed;
		var newTime = previous.Time + justElapsed;

		var toReturn = new TickState()
		{
			Speed = previous.Speed,
			Elapsed = justElapsed,
			Turn = newTime.Seconds,
			UpdateCount = previous.UpdateCount + 1,
			Time = newTime,
			Previous = previous,
		};
		previous._previous = null;

		return toReturn;
	}

	public static TickState Default { get; } = new()
	{
		Speed = 1,
		Elapsed = TimeSpan.Zero,
		Time = TimeSpan.Zero,
		Turn = 0,
		UpdateCount = 0,
		Previous = default,
	};
}