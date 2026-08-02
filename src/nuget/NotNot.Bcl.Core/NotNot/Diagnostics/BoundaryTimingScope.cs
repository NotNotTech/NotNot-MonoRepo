// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;

namespace NotNot.Diagnostics;

/// <summary>
/// Zero-allocation, stack-only <c>using var</c> scope that brackets a managed region and folds its elapsed
/// time into an <see cref="IBoundaryTimingSink"/> as a <c>subsystem:{area}:{name}</c> row. Constructed
/// against the app-neutral sink seam, so a sibling component library (terminal renderer, design widgets) can
/// instrument a hot path without referencing the host app's concrete collector.
/// </summary>
/// <remarks>
/// <para>
/// Committed as a <c>readonly ref struct</c> (cannot be boxed, fielded, captured in a lambda, or stored on
/// the heap) so it stays purely on the stack — there is no allocation on the measured path even at the
/// recording level. Usage idiom:
/// </para>
/// <code>
/// using var _ = new BoundaryTimingScope(sink, "terminal", "render-tick");
/// // ... measured managed work ...
/// </code>
/// <para>
/// OFF / NULL-SINK CONTRACT: the constructor reads <see cref="IBoundaryTimingSink.ShouldRecord"/> ONCE
/// (and treats a null sink as not-recording) and only THEN calls <see cref="Stopwatch.GetTimestamp"/>. When
/// the sink is null or Off the scope is a pure construct + no-op dispose — NO stopwatch is started and NO
/// row is recorded, so the cold-start default costs nothing at the bracket.
/// </para>
/// </remarks>
public readonly ref struct BoundaryTimingScope
{
	private readonly IBoundaryTimingSink? _sink;
	private readonly string? _rowName;
	private readonly long _startTimestamp;

	/// <summary>
	/// Begin a timing scope. Reads <paramref name="sink"/>.<see cref="IBoundaryTimingSink.ShouldRecord"/>
	/// once; when recording, formats the <c>subsystem:{area}:{name}</c> row name and captures the start
	/// timestamp. When <paramref name="sink"/> is null or not recording, the scope is inert (no stopwatch,
	/// no row).
	/// </summary>
	/// <param name="sink">The app-neutral boundary-timing sink (resolve-or-null; a null sink yields an inert scope).</param>
	/// <param name="area">The subsystem area (e.g. <c>"terminal"</c>, <c>"livevalue"</c>) — the middle row-name segment.</param>
	/// <param name="name">The region name (e.g. <c>"render-tick"</c>) — the trailing row-name segment.</param>
	public BoundaryTimingScope(IBoundaryTimingSink? sink, string area, string name)
	{
		// Single Off-query up front; a null sink is treated as not-recording. Only a recording sink starts
		// the stopwatch — an Off/null scope never touches Stopwatch.GetTimestamp().
		if (sink is null || !sink.ShouldRecord)
		{
			_sink = null;
			_rowName = null;
			_startTimestamp = 0;
			return;
		}

		_sink = sink;
		_rowName = $"subsystem:{area}:{name}";
		_startTimestamp = Stopwatch.GetTimestamp();
	}

	/// <summary>
	/// Fold the bracketed region's elapsed milliseconds into the sink (a no-op when the scope was constructed
	/// inert — null/Off sink). The sink applies its own level-gate / threshold policy inside
	/// <see cref="IBoundaryTimingSink.Record"/>.
	/// </summary>
	public void Dispose()
	{
		if (_sink is null || _rowName is null)
		{
			return;
		}

		double elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
		_sink.Record(_rowName, elapsedMs);
	}
}

/// <summary>
/// Ergonomic factory for <see cref="BoundaryTimingScope"/> over an <see cref="IBoundaryTimingSink"/>.
/// </summary>
public static class BoundaryTimingSinkExtensions
{
	/// <summary>
	/// Begin a <see cref="BoundaryTimingScope"/> from a dotted <c>"area.name"</c> identifier — e.g.
	/// <c>using var _ = sink.Scope("terminal.render-tick");</c>. Splits on the FIRST <c>'.'</c> into the
	/// area / name segments (no dot ⇒ the whole string is the area and the name is empty). A null sink
	/// yields an inert scope (delegates to the scope's null-sink contract).
	/// </summary>
	public static BoundaryTimingScope Scope(this IBoundaryTimingSink? sink, string areaDotName)
	{
		int dot = areaDotName.IndexOf('.');
		string area = dot < 0 ? areaDotName : areaDotName[..dot];
		string name = dot < 0 ? string.Empty : areaDotName[(dot + 1)..];
		return new BoundaryTimingScope(sink, area, name);
	}
}
