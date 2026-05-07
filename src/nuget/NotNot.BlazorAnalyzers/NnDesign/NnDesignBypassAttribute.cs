using System;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// Assembly-level marker that exempts the entire compilation from NNB022
/// (<see cref="NnDesignMudBlazorPolicyAnalyzer"/>) — direct MudBlazor references in this
/// assembly are intentional architecture, not a policy violation.
/// </summary>
/// <remarks>
/// <para>
/// Apply via: <c>[assembly: NotNot.BlazorAnalyzers.NnDesign.NnDesignBypass]</c>
/// </para>
/// <para>
/// Intended for two architectural roles:
/// <list type="bullet">
///   <item>
///     <description>
///     <b>The NnDesign wrapper layer itself</b> — e.g. <c>NotNot.BlazorDesign</c> —
///     where Mud* identifiers ARE the implementation detail being wrapped.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Sibling primitive layers</b> — e.g. <c>NotNot.BlazorComponents</c> — that sit
///     side-by-side with MudBlazor in the dependency graph (NOT consumers of NnDesign
///     wrappers). Per their AGENTS.md: "domain-agnostic design, minimal dependencies".
///     Direct MudBlazor consumption is intentional because these libraries don't
///     reference <c>NotNot.BlazorDesign</c> (which would reverse the dependency direction).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Detection mechanism</b>: The analyzer matches by <em>simple attribute name</em>
/// (<c>AttributeClass.Name == "NnDesignBypassAttribute"</c>). Consumer assemblies MAY
/// either reference this analyzer's types directly OR declare a local
/// <c>internal sealed class NnDesignBypassAttribute : Attribute</c> with the same name —
/// the analyzer treats both equivalently. Local declaration is the recommended path when
/// the analyzer is referenced as <c>OutputItemType="Analyzer" ReferenceOutputAssembly="false"</c>
/// (analyzer-only ProjectReference, no type import).
/// </para>
/// <para>
/// <b>Alternative exemption mechanisms</b> in
/// <see cref="NnDesignMudBlazorPolicyAnalyzer"/> (use the smallest mechanism that fits):
/// <list type="number">
///   <item>
///     <description>
///     <b>Folder path-pattern</b> — extend <c>IsExceptedPath</c> with
///     <c>p.IndexOf("/{folder}/", OrdinalIgnoreCase)</c>. Best for stable folder
///     conventions. See existing entries: <c>/NotNot.BlazorDesign/</c>,
///     <c>/NnDesignSamples/</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>File-name allow-list</b> — add to <c>AllowedFileNames</c>. Best for individual
///     bootstrapping/harness files (App.razor, Program.cs, harness pages).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Per-file opt-out comment</b> — <c>@* nnb022:allow-mudblazor: &lt;reason&gt; *@</c>
///     (Razor) or <c>// nnb022:allow-mudblazor: &lt;reason&gt;</c> (C#). Best for narrow
///     documented call-site exceptions.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Global compilation opt-out</b> — MSBuild
///     <c>&lt;NnDesignPolicyAnalyzerEnabled&gt;false&lt;/NnDesignPolicyAnalyzerEnabled&gt;</c>.
///     Disables NNB022 entirely for the compilation; coarsest mechanism.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>When to prefer this attribute over the alternatives</b>: an entire assembly is
/// exempt by architectural intent (sibling-primitive layer or the wrapper layer itself),
/// AND the assembly's folder name may not match a stable allow-list pattern, AND you
/// want the exemption to be visible in source rather than encoded in analyzer paths.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class NnDesignBypassAttribute : Attribute
{
}
