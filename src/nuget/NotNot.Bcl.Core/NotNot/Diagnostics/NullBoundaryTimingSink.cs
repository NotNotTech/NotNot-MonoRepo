// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Diagnostics;

/// <summary>
/// Null-object <see cref="IBoundaryTimingSink"/>: never records, never starts a stopwatch.
/// Component libraries that bracket hot paths against the sink register THIS type as the
/// library-default via <c>TryAddSingleton&lt;IBoundaryTimingSink, NullBoundaryTimingSink&gt;()</c>
/// inside their <c>Add{Library}Services()</c> extension, guaranteeing the interface ALWAYS resolves —
/// Blazor <c>[Inject]</c> is never optional, so an unregistered sink would throw
/// <c>InvalidOperationException</c> at component activation in any host that forgot to register one.
/// </summary>
/// <remarks>
/// <para>
/// A host that wants real boundary timing overrides this default through standard MS.DI composition:
/// register the concrete sink BEFORE calling the library's <c>Add{Library}Services()</c> (the
/// <c>TryAdd</c> then no-ops) — or use a plain <c>AddSingleton</c> replacement. No library-specific
/// opt-in API exists; ordinary DI registration is the whole contract.
/// </para>
/// <para>
/// <see cref="ShouldRecord"/> is hard <c>false</c>, so every <see cref="BoundaryTimingScope"/>
/// constructed against this sink is a pure construct + no-op dispose (no stopwatch). <see cref="Record"/>
/// is unreachable through <see cref="BoundaryTimingScope"/> (the scope only calls it after a
/// <c>true</c> gate) and is a no-op for any direct caller.
/// </para>
/// </remarks>
public sealed class NullBoundaryTimingSink : IBoundaryTimingSink
{
	/// <inheritdoc />
	public bool ShouldRecord => false;

	/// <inheritdoc />
	public void Record(string rowName, double elapsedMs) { }
}
