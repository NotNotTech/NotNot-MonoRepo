using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.ABMCS;

/// <summary>
/// ABMCS — <c>NN_ABMCS_006</c> (SKELETON, full impl deferred to parent-repo Loop 4):
/// Cross-feature internal access. Code in <c>Features/A/</c> references types in
/// <c>Features/B/</c> outside of <c>B</c>'s <c>Contracts/</c> folder. See
/// <c>docs/protocols/abmcs-feature-layout.VowHuman.md</c> for the feature-isolation rationale.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status</b>: Skeleton — descriptor registered, detection body empty. Full implementation
/// lands in Loop 4 after the VOW workspace folder structure is canonicalized to the
/// <c>Features/{Name}/</c> shape that this rule depends on.
/// </para>
/// <para>
/// <b>Planned detection</b>: compare <see cref="SyntaxTree.FilePath"/> of the calling site
/// against the called type's syntax-reference paths. Extract <c>Features/{Name}/</c> segments
/// and fire when the names differ AND the call target's path does NOT contain
/// <c>/Contracts/</c>. <c>Shared/Features/Contracts/</c> is exempt (cross-cutting shared kernel
/// per AMB-10 user-locked decision).
/// </para>
/// <para>
/// <b>Known limitations</b> (documented in protocols/abmcs-analyzers.VowHuman.md):
/// projects deviating from the <c>Features/{Name}/</c> convention silently produce zero
/// diagnostics; in-memory compilations with empty/synthetic <see cref="SyntaxTree.FilePath"/>
/// also produce zero diagnostics.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbmcsCrossFeatureIsolationAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "ABMCS.FeatureIsolation";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for cross-feature internal access.</summary>
	public const string ABMCS006_DiagnosticId = "NN_ABMCS_006";

	private static readonly LocalizableString ABMCS006_Title =
		"Cross-feature internal access";

	private static readonly LocalizableString ABMCS006_MessageFormat =
		"Type '{0}' in feature '{1}' is referenced from feature '{2}' outside of '{1}/Contracts/'. "
		+ "Cross-feature access must go through the Contracts/ folder. (NN_ABMCS_006)";

	private static readonly LocalizableString ABMCS006_Description =
		"ABMCS principle (per protocols/abmcs-feature-layout.VowHuman.md): each feature owns "
		+ "its internal types; cross-feature access flows through the feature's Contracts/ "
		+ "folder. Direct references to internal types break the isolation that makes refactor "
		+ "and feature ownership tractable.";

	/// <summary>NN_ABMCS_006 descriptor — Warning, ABMCS.FeatureIsolation category.</summary>
	public static readonly DiagnosticDescriptor ABMCS006_Rule = new(
		ABMCS006_DiagnosticId,
		ABMCS006_Title,
		ABMCS006_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: ABMCS006_Description,
		helpLinkUri: HelpBase + "nn_abmcs_006");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(ABMCS006_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// TODO (Loop 4): wire detection — compare SyntaxTree.FilePath segments between
		// reference site and definition site, exempt /Contracts/ and Shared/Features/Contracts/
		// (per AMB-10 user-locked decision). Skeleton produces zero diagnostics today.
	}
}
