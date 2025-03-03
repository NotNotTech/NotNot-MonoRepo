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
	public TimeSpan Time { get; init; }
	/// <summary>
	/// the current turn number.   usually turns elapse 1 per second.
	/// <para>starts at zero</para>
	/// <para>useful (but not required) as a simple mechanism rules coordination for nodes</para>
	/// </summary>
	public required int Turn { get; init; }
}