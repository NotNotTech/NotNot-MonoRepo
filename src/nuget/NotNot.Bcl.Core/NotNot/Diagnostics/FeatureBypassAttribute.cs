namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marker that exempts a scope from one or more ABMCS analyzer rules — <c>NN_ABMCS_001</c>
/// (Server-assembly type in shared code, folds the legacy server-namespace-using detection),
/// <c>NN_ABMCS_002</c> (direct call to <c>[FeatureServerLogic]</c> from a component),
/// <c>NN_ABMCS_003</c> (non-<c>[FeatureContract]</c> injection in <c>ComponentBase</c>),
/// <c>NN_ABMCS_004</c> (direct <c>DbContext</c> in shared code), and the additional ABMCS rules
/// <c>NN_ABMCS_005..010</c>. Mirrors the <c>NnDesignBypassAttribute</c> /
/// <see cref="LdddBypassAttribute"/> precedent — single bypass marker that suppresses the
/// entire analyzer rule family within the marked scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Relationship to <see cref="LdddBypassAttribute"/></b>: This attribute is an ADDITIVE
/// SIBLING of <see cref="LdddBypassAttribute"/>, NOT a subclass. Both attributes carry the
/// same suppression semantics — they are equivalent bypass markers under the ABMCS vocabulary
/// (this attribute) and the legacy LiteDDD vocabulary (<see cref="LdddBypassAttribute"/>). The
/// analyzers in <c>NotNot.BlazorAnalyzers</c> recognize BOTH simple names via simple-name OR
/// fully-qualified-name comparison, so consumers may apply either attribute with identical
/// effect to bypass either the legacy <c>NN_LDDD_*</c> rule family or the ABMCS
/// <c>NN_ABMCS_*</c> rule family (or both at once — since the analyzer's dual-emit pattern
/// fires both IDs on the same violation, a single bypass suppresses both). The legacy
/// <see cref="LdddBypassAttribute"/> remains for backward compatibility with external NuGet
/// consumers and projects still using the LiteDDD vocabulary — it is NOT retired.
/// </para>
/// <para>
/// <b>Scope of suppression</b>:
/// <list type="bullet">
///   <item>
///     <description>
///     <c>[assembly: FeatureBypass]</c> — entire compilation exempt from all
///     <c>NN_ABMCS_*</c> and <c>NN_LDDD_*</c> rules. Intended for the canonical sample/playground
///     projects (e.g. assemblies under <c>Pages/Samples/**</c>-equivalent folders) or sibling
///     primitive libraries where the architectural carve-out is project-wide.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[FeatureBypass]</c> on a class/struct/interface — type-scope exempt for
///     <c>NN_ABMCS_002</c> / <c>NN_ABMCS_003</c> (and legacy <c>NN_LDDD_003</c> /
///     <c>NN_LDDD_005</c>). Intended for individual sample/test fixtures that share an
///     assembly with policy-conformant code.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[FeatureBypass]</c> on a method — method-scope exempt. Intended for narrowly
///     documented call-site exceptions (e.g. a single helper method that must invoke a
///     server-logic service from a component for a specific architectural reason).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Detection mechanism</b>: The analyzer matches by <em>simple attribute name</em>
/// (<c>AttributeClass.Name == "FeatureBypassAttribute"</c>) OR fully-qualified name
/// (<c>NotNot.Bcl.Diagnostics.FeatureBypassAttribute</c>). Consumer assemblies MAY either
/// reference this analyzer's types directly OR declare a local
/// <c>internal sealed class FeatureBypassAttribute : Attribute</c> with the same name — the
/// analyzer treats both equivalently. Local declaration is the recommended path when the
/// analyzer is referenced as <c>OutputItemType="Analyzer" ReferenceOutputAssembly="false"</c>
/// (analyzer-only <c>ProjectReference</c>, no type import). This mirrors the
/// <c>NnDesignBypassAttribute</c> / <see cref="LdddBypassAttribute"/> conventions in this
/// repository.
/// </para>
/// <para>
/// <b>Alternative exemption mechanisms</b> in the ABMCS analyzer family (use the smallest
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
///     reason is recorded inline at the violation. Pattern: <c>// nn-abmcs:bypass: &lt;reason&gt;</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Global compilation opt-out</b> via MSBuild property
///     <c>&lt;LdddPolicyAnalyzerEnabled&gt;false&lt;/LdddPolicyAnalyzerEnabled&gt;</c> —
///     disables all <c>NN_ABMCS_*</c> and <c>NN_LDDD_*</c> rules for the compilation; coarsest
///     mechanism, use only when the architectural fit genuinely doesn't apply (e.g. console-app
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
/// harness" carve-outs, <c>[assembly: FeatureBypass]</c> is the canonical placement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct
			  | AttributeTargets.Interface | AttributeTargets.Method,
				AllowMultiple = false, Inherited = false)]
public sealed class FeatureBypassAttribute : Attribute
{
}
