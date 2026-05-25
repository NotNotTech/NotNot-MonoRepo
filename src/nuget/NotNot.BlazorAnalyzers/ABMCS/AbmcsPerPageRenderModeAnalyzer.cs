using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.ABMCS;

/// <summary>
/// ABMCS — <c>NN_ABMCS_005</c> (SKELETON, full impl deferred to parent-repo Loop 4):
/// Per-page <c>@rendermode</c> directive — Blazor-specific. See
/// <c>docs/protocols/abmcs-project-topology.VowHuman.md</c> for the rationale (page-level
/// rendermode declarations couple presentation to render strategy in a way that fights
/// InteractiveWebAssembly's whole-app discipline).
/// </summary>
/// <remarks>
/// <para>
/// <b>Status</b>: Skeleton — descriptor registered + analyzer class wired so
/// <see cref="SupportedDiagnostics"/> surfaces the rule ID via the existing
/// <c>ConfiguredRules</c> aggregation, but the detection body is intentionally empty.
/// Real implementation lands in the parent-repo workflow's Loop 4 alongside the other
/// project-topology checks.
/// </para>
/// <para>
/// <b>Planned detection</b>: <see cref="Microsoft.CodeAnalysis.AdditionalText"/> scan over
/// <c>.razor</c> files for the <c>@rendermode</c> directive at the page level. The fix is
/// to lift rendermode into the App-level routing config instead of per-page declarations.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbmcsPerPageRenderModeAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "ABMCS.Topology";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for the per-page <c>@rendermode</c> directive rule.</summary>
	public const string ABMCS005_DiagnosticId = "NN_ABMCS_005";

	private static readonly LocalizableString ABMCS005_Title =
		"Per-page @rendermode directive — prefer App-level rendermode";

	private static readonly LocalizableString ABMCS005_MessageFormat =
		"Page '{0}' declares an @rendermode directive. Lift to App-level routing config instead. "
		+ "(NN_ABMCS_005)";

	private static readonly LocalizableString ABMCS005_Description =
		"ABMCS principle (per protocols/abmcs-project-topology.VowHuman.md): page-level "
		+ "@rendermode declarations couple presentation to render strategy and fight "
		+ "InteractiveWebAssembly's whole-app discipline. Declare rendermode at the App or "
		+ "Routes level instead.";

	/// <summary>NN_ABMCS_005 descriptor — Warning, ABMCS.Topology category.</summary>
	public static readonly DiagnosticDescriptor ABMCS005_Rule = new(
		ABMCS005_DiagnosticId,
		ABMCS005_Title,
		ABMCS005_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: ABMCS005_Description,
		helpLinkUri: HelpBase + "nn_abmcs_005");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(ABMCS005_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// TODO (Loop 4): wire detection — AdditionalText scan of *.razor for "@rendermode"
		// directive. Skeleton intentionally has no action registrations so it produces zero
		// diagnostics; the descriptor still surfaces via SupportedDiagnostics so .editorconfig
		// severity overrides can target the ID now.
	}
}
