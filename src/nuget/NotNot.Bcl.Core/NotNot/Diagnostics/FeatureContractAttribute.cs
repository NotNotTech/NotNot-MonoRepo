namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marks an interface or class as an ABMCS "Feature Contract" — the canonical Refit-attributed
/// surface for Server↔Client communication across a feature's transport boundary. Detected by
/// name in <c>NN_ABMCS_003</c> (analyzer in <c>NotNot.BlazorAnalyzers</c>,
/// <c>LiteDDD/LdddInjectAndCallAnalyzer</c>) to allow <c>ComponentBase</c>-derived classes to
/// <c>[Inject]</c> only types bearing this marker (plus the analyzer's framework allow-list —
/// <c>NavigationManager</c>, <c>ILogger</c>, <c>HubConnection</c>, etc).
/// </summary>
/// <remarks>
/// <para>
/// <b>Relationship to <see cref="LdddDataServiceAttribute"/></b>: This attribute is an ADDITIVE
/// SIBLING of <see cref="LdddDataServiceAttribute"/>, NOT a subclass. Both attributes carry the
/// same architectural meaning — they are equivalent transport-interface markers under the ABMCS
/// vocabulary (this attribute) and the legacy LiteDDD vocabulary
/// (<see cref="LdddDataServiceAttribute"/>). The analyzer (NN_ABMCS_003 / NN_LDDD_003) matches
/// BOTH simple names via simple-name OR fully-qualified-name comparison, so consumers may apply
/// either attribute with identical effect. The legacy <see cref="LdddDataServiceAttribute"/>
/// remains for backward compatibility with external NuGet consumers and future
/// ABMCS-vocabulary conversions in unrelated projects — it is NOT retired.
/// </para>
/// <para>
/// <b>Detection mechanism</b>: Marker attribute — has no runtime behavior. The analyzer matches
/// by simple attribute name (<c>AttributeClass.Name == "FeatureContractAttribute"</c>) OR by
/// fully-qualified name (<c>NotNot.Bcl.Diagnostics.FeatureContractAttribute</c>); consumers may
/// either reference this attribute directly from <c>NotNot.Bcl.Core</c> or declare a local
/// <c>internal sealed class FeatureContractAttribute : Attribute</c> with the same simple name.
/// <c>NotNot.Bcl.Core</c>'s full-name match is the canonical path; the local-copy fallback is
/// for consumers that wish to avoid a <c>NotNot.Bcl.Core</c> reference solely for the attribute
/// (mirrors the precedent set by <see cref="LdddDataServiceAttribute"/>,
/// <c>RequireMaybeReturnAttribute</c>, and <c>NnDesignBypassAttribute</c>).
/// </para>
/// <para>
/// <b>Intended placement</b>: applied to Refit-attributed contract interfaces in
/// <c>Shared/Features/{Feature}/Contracts/I{Feature}DataService.cs</c> (and equivalent
/// Refit-attributed concrete classes if any). Mirrors the per-feature ABMCS contract boundary
/// documented in <c>docs/protocols/abmcs-contracts-transport.VowHuman.md</c>.
/// </para>
/// <para>
/// <b>Bypass</b>: violations of <c>NN_ABMCS_003</c> can be suppressed for the entire compilation
/// via <c>[assembly: FeatureBypass]</c> (or its legacy <c>[assembly: LdddBypass]</c> equivalent),
/// or at narrower scope via <c>[FeatureBypass]</c> / <c>[LdddBypass]</c> on the offending
/// class/method.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface,
				Inherited = true, AllowMultiple = false)]
public sealed class FeatureContractAttribute : Attribute
{
}
