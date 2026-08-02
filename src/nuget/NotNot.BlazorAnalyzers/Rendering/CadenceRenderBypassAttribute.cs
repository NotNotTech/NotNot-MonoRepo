using System;

namespace NotNot.BlazorAnalyzers.Rendering;

/// <summary>
/// Marker that exempts a whole assembly from the <c>NNB042</c> cadence-unconditional-render
/// rule (<see cref="CadenceUnconditionalRenderAnalyzer"/>). Apply at the assembly level (only
/// valid scope) for assemblies that pick up the <c>NotNot.BlazorAnalyzers</c> package
/// transitively but should NOT be subject to the cadence render-redundancy policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: Assembly-only. Per-type / per-method scopes are intentionally NOT supported —
/// a single component whose cadence callback fires <c>StateHasChanged</c> with no dirty-check
/// guard is a render-redundancy candidate by definition. The exemption mechanism for
/// legitimate non-app pages (NnDesign samples, feature samples) is the documented
/// path-exemption (<c>Pages/Samples/**</c> + <c>Pages/NnDesignSamples/**</c>) baked into the
/// analyzer.
/// </para>
/// <para>
/// <b>Detection</b>: The analyzer matches by <em>simple attribute name</em>
/// (<c>AttributeClass.Name == "CadenceRenderBypassAttribute"</c>), with a fully-qualified-name
/// fallback against <c>NotNot.BlazorAnalyzers.Rendering.CadenceRenderBypassAttribute</c>.
/// Consumer assemblies MAY either reference this attribute directly OR declare a local
/// <c>internal sealed class CadenceRenderBypassAttribute : Attribute</c> with the same name —
/// the analyzer treats both equivalently. Mirrors the <c>NnRmBypassAttribute</c> /
/// <c>LdddBypassAttribute</c> conventions in this repository.
/// </para>
/// <para>
/// <b>For a component that genuinely needs an unconditional cadence render</b>: prefer adding
/// an equality / dirty-check guard around the <c>StateHasChanged</c> call, or delegate the
/// cadence to <c>NnLiveValue&lt;T&gt;</c> (whose recompute kernel IS the dirty-check). The
/// assembly-level bypass is the escape hatch for whole assemblies outside the render-policy
/// scope, not for silencing an individual unguarded callback.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class CadenceRenderBypassAttribute : Attribute
{
}
