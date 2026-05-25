namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marks a class or interface as an ABMCS "Feature Server Logic" — a server-side
/// business-logic type that owns domain compute, validation, or data processing for a feature.
/// Detected by name in <c>NN_ABMCS_002</c> (analyzer in <c>NotNot.BlazorAnalyzers</c>,
/// <c>LiteDDD/LdddInjectAndCallAnalyzer</c>), which fires when a <c>ComponentBase</c>-derived
/// class invokes an instance method on a receiver whose type (or any base type) bears this
/// marker — catching direct server-logic calls from presentation-layer components, the canonical
/// ABMCS violation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Relationship to <see cref="LdddDomainServiceAttribute"/></b>: This attribute is an ADDITIVE
/// SIBLING of <see cref="LdddDomainServiceAttribute"/>, NOT a subclass. Both attributes carry
/// the same architectural meaning — they are equivalent server-logic-class markers under the
/// ABMCS vocabulary (this attribute) and the legacy LiteDDD vocabulary
/// (<see cref="LdddDomainServiceAttribute"/>). The analyzer (NN_ABMCS_002 / NN_LDDD_005) matches
/// BOTH simple names via simple-name OR fully-qualified-name comparison, so consumers may apply
/// either attribute with identical effect. The legacy <see cref="LdddDomainServiceAttribute"/>
/// remains for backward compatibility with external NuGet consumers and future
/// ABMCS-vocabulary conversions in unrelated projects — it is NOT retired.
/// </para>
/// <para>
/// <b>Rule renumbering note</b>: under the ABMCS taxonomy, the direct-invocation rule moves from
/// <c>NN_LDDD_005</c> to <c>NN_ABMCS_002</c> (intentional renumber for grouping with
/// <c>NN_ABMCS_001</c> assembly-fence rules). Both rule IDs fire on the same violation under
/// the analyzer's dual-emit pattern; consumers may suppress either ID via <c>.editorconfig</c>.
/// </para>
/// <para>
/// <b>Detection mechanism</b>: Marker attribute — has no runtime behavior. The analyzer matches
/// by simple attribute name (<c>AttributeClass.Name == "FeatureServerLogicAttribute"</c>) OR by
/// fully-qualified name (<c>NotNot.Bcl.Diagnostics.FeatureServerLogicAttribute</c>); consumers
/// may either reference this attribute directly from <c>NotNot.Bcl.Core</c> or declare a local
/// <c>internal sealed class FeatureServerLogicAttribute : Attribute</c> with the same simple
/// name. The local-copy fallback exists for consumers that wish to avoid a
/// <c>NotNot.Bcl.Core</c> reference solely for the attribute marker.
/// </para>
/// <para>
/// <b>Intended placement</b>: applied to public service types under
/// <c>Server/Features/{Feature}/AppLogic/</c> (and equivalent per-feature server-side
/// logic folders). Server-logic types reachable only from the server contract surface
/// (<c>IDataService</c> controllers + use-case bridges) are correctly marked; types that
/// must be invoked from both server and shared-helper code may need targeted
/// <c>[FeatureBypass]</c> at call sites or refactor of the helper into the server layer.
/// </para>
/// <para>
/// <b>Inheritance semantics</b>: <c>Inherited = true</c> — derived classes inherit the
/// marker, so a single annotation on a base service class covers its subclasses. This
/// matches the <see cref="LdddDomainServiceAttribute"/> precedent.
/// </para>
/// <para>
/// <b>Bypass</b>: violations of <c>NN_ABMCS_002</c> can be suppressed for the entire
/// compilation via <c>[assembly: FeatureBypass]</c> (or its legacy
/// <c>[assembly: LdddBypass]</c> equivalent), or at narrower scope via
/// <c>[FeatureBypass]</c> / <c>[LdddBypass]</c> on the offending class/method.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface,
				Inherited = true, AllowMultiple = false)]
public sealed class FeatureServerLogicAttribute : Attribute
{
}
