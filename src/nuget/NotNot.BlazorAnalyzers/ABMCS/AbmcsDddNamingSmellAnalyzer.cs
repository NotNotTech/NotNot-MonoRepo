using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.ABMCS;

/// <summary>
/// ABMCS — <c>NN_ABMCS_008</c> (SKELETON, full impl deferred to parent-repo Loop 4):
/// DDD-vocabulary naming smell. Fires when a type name ends with <c>Repository</c>,
/// <c>Aggregate</c>, <c>ValueObject</c>, <c>DomainService</c>, or <c>Factory</c> — the rule
/// catches the careless name, NOT the DDD conformance of the type. See
/// <c>docs/protocols/abmcs-analyzers.VowHuman.md</c> NN_ABMCS_008 section + AGENTS.md tenet
/// "do not borrow DDD pattern names for non-DDD types".
/// </summary>
/// <remarks>
/// <para>
/// <b>Status</b>: Skeleton — descriptor registered, detection body empty. Full implementation
/// lands in Loop 4.
/// </para>
/// <para>
/// <b>Planned detection</b>: <see cref="SymbolKind.NamedType"/> action; check name suffix
/// against the forbidden list; honor <c>[FeatureBypass]</c> on the type per the documented
/// carve-out path (rename OR mark + document in feature's <c>AGENTS.md</c>).
/// </para>
/// <para>
/// <b>Design constraint</b> (from protocols/abmcs-analyzers.VowHuman.md): resist building
/// "structural DDD conformance" logic into this analyzer. The value is in catching the
/// careless name; adjudicating DDD doctrine is out of scope.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbmcsDddNamingSmellAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "ABMCS.Naming";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for the DDD-vocabulary naming smell.</summary>
	public const string ABMCS008_DiagnosticId = "NN_ABMCS_008";

	private static readonly LocalizableString ABMCS008_Title =
		"DDD-vocabulary naming smell — rename or apply [FeatureBypass] with documented rationale";

	private static readonly LocalizableString ABMCS008_MessageFormat =
		"Type '{0}' ends with DDD-pattern suffix '{1}'. Either rename or mark [FeatureBypass] "
		+ "with rationale recorded in the feature's AGENTS.md. (NN_ABMCS_008)";

	private static readonly LocalizableString ABMCS008_Description =
		"ABMCS principle (per protocols/abmcs-analyzers.VowHuman.md + protocols/"
		+ "abmcs-overview.VowHuman.md): DDD pattern names (Repository, Aggregate, ValueObject, "
		+ "DomainService, Factory) carry strong architectural connotations. Borrowing them for "
		+ "types that do not conform to the corresponding DDD pattern misleads readers about "
		+ "the surrounding code's intent. Roslyn detects the name suffix only; conformance to "
		+ "the corresponding DDD pattern is not verifiable at the analyzer level — the fix is "
		+ "either to rename OR to mark [FeatureBypass] with a recorded rationale.";

	/// <summary>NN_ABMCS_008 descriptor — Warning, ABMCS.Naming category.</summary>
	public static readonly DiagnosticDescriptor ABMCS008_Rule = new(
		ABMCS008_DiagnosticId,
		ABMCS008_Title,
		ABMCS008_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: ABMCS008_Description,
		helpLinkUri: HelpBase + "nn_abmcs_008");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(ABMCS008_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// TODO (Loop 4): SymbolKind.NamedType action — check name suffix against the forbidden
		// list { Repository, Aggregate, ValueObject, DomainService, Factory }; honor
		// [FeatureBypass] on the type. Skeleton produces zero diagnostics today.
	}
}
