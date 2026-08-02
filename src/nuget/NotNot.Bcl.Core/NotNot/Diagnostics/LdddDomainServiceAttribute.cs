namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marks a class or interface as a LiteDDD "Domain Service" — a server-side business-logic
/// type that owns domain compute, validation, or data processing. Detected by name in
/// <c>NN_LDDD_005</c> (analyzer in <c>NotNot.BlazorAnalyzers</c>,
/// <c>LiteDDD/LdddInjectAndCallAnalyzer</c>), which fires when a
/// <c>ComponentBase</c>-derived class invokes an instance method on a receiver whose type
/// (or any base type) bears this marker — catching direct domain-algorithm calls from
/// presentation-layer components, the canonical LiteDDD violation.
/// </summary>
/// <remarks>
/// <para>
/// Marker attribute — has no runtime behavior. The analyzer matches by simple attribute
/// name (<c>AttributeClass.Name == "LdddDomainServiceAttribute"</c>); consumers may either
/// reference this attribute directly from <c>NotNot.Bcl.Core</c> or declare a local
/// <c>internal sealed class LdddDomainServiceAttribute : Attribute</c> with the same simple
/// name. The local-copy fallback exists for consumers that wish to avoid a
/// <c>NotNot.Bcl.Core</c> reference solely for the attribute marker.
/// </para>
/// <para>
/// <b>Intended placement</b>: applied to public service types under
/// <c>Server/Features/{Feature}/AppLogic/</c> (and equivalent per-feature server-side
/// domain folders). Domain types reachable only from the server contract surface
/// (<c>IDataService</c> controllers + use-case bridges) are correctly marked; types that
/// must be invoked from both server and shared-helper code may need targeted
/// <c>[LdddBypass]</c> at call sites or refactor of the helper into the server layer.
/// </para>
/// <para>
/// <b>Inheritance semantics</b>: <c>Inherited = true</c> — derived classes inherit the
/// marker, so a single annotation on a base service class covers its subclasses. This
/// matches the <c>RequireMaybeReturnAttribute</c> precedent.
/// </para>
/// <para>
/// <b>Bypass</b>: violations of <c>NN_LDDD_005</c> can be suppressed for the entire
/// compilation via <c>[assembly: LdddBypass]</c>, or at narrower scope (Wave 2) via
/// <c>[LdddBypass]</c> on the offending class/method.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface,
				Inherited = true, AllowMultiple = false)]
public sealed class LdddDomainServiceAttribute : Attribute
{
}
