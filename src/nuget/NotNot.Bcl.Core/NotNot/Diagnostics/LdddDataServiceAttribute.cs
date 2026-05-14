namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marks an interface or class as a LiteDDD "Data Service" — the canonical Refit-attributed
/// surface for Server↔Client communication. Detected by name in <c>NN_LDDD_003</c>
/// (analyzer in <c>NotNot.BlazorAnalyzers</c>, <c>LiteDDD/LdddInjectAndCallAnalyzer</c>)
/// to allow <c>ComponentBase</c>-derived classes to <c>[Inject]</c> only types bearing this
/// marker (plus the analyzer's framework allow-list — <c>NavigationManager</c>,
/// <c>ILogger</c>, <c>HubConnection</c>, etc).
/// </summary>
/// <remarks>
/// <para>
/// Marker attribute — has no runtime behavior. The analyzer matches by simple attribute
/// name (<c>AttributeClass.Name == "LdddDataServiceAttribute"</c>); consumers may either
/// reference this attribute directly from <c>NotNot.Bcl.Core</c> or declare a local
/// <c>internal sealed class LdddDataServiceAttribute : Attribute</c> with the same simple
/// name. <c>NotNot.Bcl.Core</c>'s full-name match is the canonical path; the local-copy
/// fallback is for consumers that wish to avoid a <c>NotNot.Bcl.Core</c> reference solely
/// for the attribute (the pattern used by <c>RequireMaybeReturnAttribute</c> and
/// <c>NnDesignBypassAttribute</c>).
/// </para>
/// <para>
/// <b>Intended placement</b>: applied to Refit-attributed contract interfaces in
/// <c>Shared/Features/{Feature}/Contracts/I{Feature}DataService.cs</c> (and equivalent
/// Refit-attributed concrete classes if any). Mirrors the per-feature LiteDDD contract
/// boundary documented in <c>Novaleaf.VibeOverwatch/LiteDDD-BlazorAspNetCore.vibeKnowledge.md</c>.
/// </para>
/// <para>
/// <b>Bypass</b>: violations of <c>NN_LDDD_003</c> can be suppressed for the entire
/// compilation via <c>[assembly: LdddBypass]</c>, or at narrower scope (Wave 2) via
/// <c>[LdddBypass]</c> on the offending class/method.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface,
				Inherited = true, AllowMultiple = false)]
public sealed class LdddDataServiceAttribute : Attribute
{
}
