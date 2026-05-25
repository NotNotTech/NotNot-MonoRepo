using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.ABMCS;

/// <summary>
/// ABMCS — <c>NN_ABMCS_007</c> (SKELETON, full impl deferred to parent-repo Loop 4):
/// Per-file LoC threshold (AMB-12 user-locked: per-file threshold, default 800 LoC). Aims to
/// prompt sub-feature promotion review when a single file grows past the navigation-burden
/// threshold. See <c>docs/protocols/abmcs-analyzers.VowHuman.md</c> NN_ABMCS_007 section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status</b>: Skeleton — descriptor registered, detection body empty. Full implementation
/// lands in Loop 4 after the workspace folder canonicalization is complete.
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Info"/> per the analyzer-rules table — this
/// is a smell indicator, not a hard limit. Configurable per project via
/// <c>.editorconfig</c> severity override.
/// </para>
/// <para>
/// <b>Planned detection</b>: <see cref="CompilationAnalysisContext"/> compilation-end action
/// that counts non-blank, non-comment lines per <see cref="SyntaxTree"/> and emits at the
/// first line of any file exceeding the threshold (default 800). Threshold itself will be
/// configurable via <see cref="AnalyzerConfigOptionsProvider"/>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbmcsFeatureSizeAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "ABMCS.FeatureSize";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for the per-file LoC threshold.</summary>
	public const string ABMCS007_DiagnosticId = "NN_ABMCS_007";

	private static readonly LocalizableString ABMCS007_Title =
		"File exceeds per-file LoC threshold — consider sub-feature promotion";

	private static readonly LocalizableString ABMCS007_MessageFormat =
		"File '{0}' is {1} LoC (threshold {2}). Consider sub-feature promotion or refactor. "
		+ "(NN_ABMCS_007)";

	private static readonly LocalizableString ABMCS007_Description =
		"ABMCS principle (per protocols/abmcs-analyzers.VowHuman.md): per-file LoC threshold is "
		+ "a smell indicator tied to navigation burden, not a hard limit. The default (800) is "
		+ "configurable per project via .editorconfig + analyzer options. The intent is to "
		+ "prompt sub-feature promotion review, not block the build.";

	/// <summary>NN_ABMCS_007 descriptor — Info severity per the analyzer-rules table.</summary>
	public static readonly DiagnosticDescriptor ABMCS007_Rule = new(
		ABMCS007_DiagnosticId,
		ABMCS007_Title,
		ABMCS007_MessageFormat,
		Category,
		DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		description: ABMCS007_Description,
		helpLinkUri: HelpBase + "nn_abmcs_007");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(ABMCS007_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// TODO (Loop 4): CompilationAnalysisContext action — count non-blank/non-comment lines
		// per SyntaxTree, emit at line 1 of any file over the threshold (default 800, config via
		// AnalyzerConfigOptionsProvider). Skeleton produces zero diagnostics today.
	}
}
