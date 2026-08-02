// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Diagnostics;

/// <summary>
/// App-neutral seam for folding a bracketed managed region's elapsed time into a host application's
/// boundary-timing collector. A sibling component library (terminal renderer, design-language widgets)
/// brackets a hot path with <see cref="BoundaryTimingScope"/> against THIS interface and never references
/// the host app's concrete collector type — the host supplies the concrete sink at composition time.
/// </summary>
/// <remarks>
/// <para>
/// The interface ALWAYS resolves: a consumer obtains it via <c>[Inject] IBoundaryTimingSink Sink</c>
/// (Blazor <c>[Inject]</c> is never optional) and receives either the host-registered concrete sink or
/// the library-default <see cref="NullBoundaryTimingSink"/> — component libraries
/// <c>TryAddSingleton</c> the null-object inside their <c>Add{Library}Services()</c> extension. A host
/// that wants real boundary timing overrides that default through standard MS.DI composition: register
/// the concrete sink BEFORE calling the library's <c>Add{Library}Services()</c> (the <c>TryAdd</c> then
/// no-ops) — ordinary DI registration is the whole contract.
/// </para>
/// <para>
/// The Off-queryable gate (<see cref="ShouldRecord"/>) lets a caller decide whether to even start a
/// stopwatch WITHOUT referencing the host's diagnostics-level type — the implementation projects its own
/// level state onto a single bool. When it reports false the scope is a pure construct + no-op dispose.
/// </para>
/// </remarks>
public interface IBoundaryTimingSink
{
	/// <summary>
	/// Cheap Off-query: true when the sink is currently recording (the host's diagnostics level is above
	/// Off), false otherwise. A <see cref="BoundaryTimingScope"/> reads this ONCE at construction and only
	/// starts a stopwatch when it is true — so an Off sink costs nothing at the bracket. Implementations
	/// MUST make this allocation-free and side-effect-free (it is read on every hot-path entry).
	/// </summary>
	bool ShouldRecord { get; }

	/// <summary>
	/// Fold one measured region's duration into the host's boundary-timing accumulator, keyed by
	/// <paramref name="rowName"/> (a fully-formed <c>subsystem:{area}:{name}</c> row name supplied by the
	/// caller — see <see cref="BoundaryTimingScope"/>). The host applies its own level-gate / threshold
	/// policy inside this call, so a caller may invoke it unconditionally after a non-Off bracket.
	/// </summary>
	/// <param name="rowName">The fully-formed subsystem row name (already carrying its <c>subsystem:</c> prefix).</param>
	/// <param name="elapsedMs">The bracketed region's elapsed time in milliseconds.</param>
	void Record(string rowName, double elapsedMs);
}
