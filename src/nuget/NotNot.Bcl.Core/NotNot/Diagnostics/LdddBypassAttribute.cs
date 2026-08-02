namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marker that exempts a scope from one or more LiteDDD analyzer rules — <c>NN_LDDD_001</c>
/// (Server-assembly type in shared code), <c>NN_LDDD_002</c> (Server namespace
/// <c>using</c> directive), <c>NN_LDDD_003</c> (non-<c>[LdddDataService]</c> injection in
/// <c>ComponentBase</c>), <c>NN_LDDD_004</c> (direct <c>DbContext</c> in shared code),
/// and <c>NN_LDDD_005</c> (<c>[LdddDomainService]</c> invocation from <c>ComponentBase</c>).
/// Mirrors the <c>NnDesignBypassAttribute</c> precedent — single bypass marker that
/// suppresses the entire analyzer rule family within the marked scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope of suppression</b>:
/// <list type="bullet">
///   <item>
///     <description>
///     <c>[assembly: LdddBypass]</c> — entire compilation exempt from all
///     <c>NN_LDDD_*</c> rules. Intended for the canonical sample/playground projects
///     (e.g. assemblies under <c>Pages/Samples/**</c>-equivalent folders) where the
///     architectural carve-out is project-wide.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[LdddBypass]</c> on a class/struct/interface — type-scope exempt. Intended for
///     individual sample/test fixtures that share an assembly with policy-conformant code.
///     <i>Wave 2 — narrow-scope analyzer support lands with <c>NN_LDDD_003</c> /
///     <c>NN_LDDD_005</c>.</i>
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[LdddBypass]</c> on a method — method-scope exempt. Intended for narrowly
///     documented call-site exceptions (e.g. a single helper method that must invoke a
///     domain service from a component for a specific architectural reason).
///     <i>Wave 2.</i>
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Detection mechanism</b>: The analyzer matches by <em>simple attribute name</em>
/// (<c>AttributeClass.Name == "LdddBypassAttribute"</c>). Consumer assemblies MAY either
/// reference this analyzer's types directly OR declare a local
/// <c>internal sealed class LdddBypassAttribute : Attribute</c> with the same name — the
/// analyzer treats both equivalently. Local declaration is the recommended path when the
/// analyzer is referenced as <c>OutputItemType="Analyzer" ReferenceOutputAssembly="false"</c>
/// (analyzer-only <c>ProjectReference</c>, no type import). This mirrors the
/// <c>NnDesignBypassAttribute</c> / <c>RequireMaybeReturnAttribute</c> conventions in this
/// repository.
/// </para>
/// <para>
/// <b>Alternative exemption mechanisms</b> in the LDDD analyzer family (use the smallest
/// mechanism that fits the carve-out):
/// <list type="number">
///   <item>
///     <description>
///     <b>Folder path-pattern</b> in the analyzer's <c>IsExceptedPath</c> helper — best
///     for stable folder conventions (e.g. <c>/Samples/</c>, <c>/Playground/</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>File-name allow-list</b> — best for individual bootstrapping/harness files
///     (App.razor, Program.cs).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Per-file opt-out comment</b> — narrow documented call-site exceptions where the
///     reason is recorded inline at the violation. Pattern: <c>// nn-lddd:bypass: &lt;reason&gt;</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Global compilation opt-out</b> via MSBuild property
///     <c>&lt;LdddPolicyAnalyzerEnabled&gt;false&lt;/LdddPolicyAnalyzerEnabled&gt;</c> —
///     disables all <c>NN_LDDD_*</c> rules for the compilation; coarsest mechanism, use
///     only when the architectural fit genuinely doesn't apply (e.g. console-app
///     consumers of <c>NotNot.BlazorAnalyzers</c>).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>When to prefer this attribute over the alternatives</b>: the carve-out is
/// architectural intent (sample project · playground · explicit per-type exception) AND
/// you want the exemption to be visible in source rather than encoded in analyzer paths
/// or MSBuild properties. For broad "this whole assembly is intentionally a sample/sample-
/// harness" carve-outs, <c>[assembly: LdddBypass]</c> is the canonical placement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct
			  | AttributeTargets.Interface | AttributeTargets.Method,
				AllowMultiple = false, Inherited = false)]
public sealed class LdddBypassAttribute : Attribute
{
}
