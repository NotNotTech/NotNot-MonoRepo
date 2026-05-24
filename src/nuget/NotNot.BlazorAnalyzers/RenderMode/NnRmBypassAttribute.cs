using System;

namespace NotNot.BlazorAnalyzers.RenderMode;

/// <summary>
/// Marker that exempts a whole assembly from the <c>NN_RM_001</c> render-mode enforcement
/// rule. Apply at the assembly level (only valid scope) for non-VOW assemblies that pick up
/// the <c>NotNot.BlazorAnalyzers</c> package transitively but should NOT be subject to VOW
/// render-mode policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: Assembly-only. Per-type / per-method scopes are intentionally NOT supported
/// — a single non-conforming page in an otherwise-VOW assembly is a violation by
/// definition. The exemption mechanism for legitimate non-app pages (NnDesign samples,
/// feature samples) is the documented path-exemption (<c>Pages/Samples/**</c> +
/// <c>Pages/NnDesignSamples/**</c>) baked into the analyzer.
/// </para>
/// <para>
/// <b>Detection</b>: The analyzer matches by <em>simple attribute name</em>
/// (<c>AttributeClass.Name == "NnRmBypassAttribute"</c>), with a fully-qualified-name
/// fallback. Consumer assemblies MAY either reference this attribute directly OR declare a
/// local <c>internal sealed class NnRmBypassAttribute : Attribute</c> with the same name —
/// the analyzer treats both equivalently. Mirrors the <c>LdddBypassAttribute</c> /
/// <c>NnDesignBypassAttribute</c> conventions in this repository.
/// </para>
/// <para>
/// <b>When to use</b>: Non-VOW Blazor assemblies that consume <c>NotNot.BlazorAnalyzers</c>
/// transitively (e.g. sibling NnDesign / NotNot.BlazorComponents libraries, future Blazor
/// consumer projects outside VOW) where the Pattern-2-reflection convention at VOW's
/// <c>App.razor</c> does not apply. Whole-assembly opt-out is the only supported scope —
/// per-type / per-method scopes are intentionally NOT supported.
/// </para>
/// <para>
/// <b>For VOW pages that need a non-default render mode</b>: do NOT use this attribute, and
/// do NOT add a per-page <c>@rendermode @(new ...)</c> directive — the latter is forbidden
/// under NN_RM_001 because it re-introduces the LCA reconciliation defect surface
/// (<c>dotnet/aspnetcore#52768</c>) AND bypasses the central Pattern 2 reflection getter.
/// The canonical mechanism is the C# attribute form:
/// <c>@attribute [RenderModeInteractiveServer]</c> (or
/// <c>[RenderModeInteractiveAuto]</c> / <c>[RenderModeInteractiveWebAssembly]</c>). The
/// attribute carries through endpoint metadata which Pattern 2 reflection at
/// <c>Server/Components/App.razor</c> reads via
/// <c>HttpContext.GetEndpoint()?.Metadata.GetMetadata&lt;RenderModeAttribute&gt;()?.Mode</c>,
/// so the per-page override flows through the SINGLE declaration site rather than around it.
/// </para>
/// <para>
/// <b>For per-page exemption of NN_RM_001 itself</b> (e.g. samples that demonstrate the
/// per-page override mechanism), place the file under <c>Pages/Samples/**</c> or
/// <c>Pages/NnDesignSamples/**</c> — these paths are exempt via the analyzer's path-prefix
/// check; per-page directives are permitted there as demonstration of the override
/// mechanism.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class NnRmBypassAttribute : Attribute
{
}
