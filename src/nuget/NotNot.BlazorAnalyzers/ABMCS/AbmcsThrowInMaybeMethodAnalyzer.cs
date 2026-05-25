using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.ABMCS;

/// <summary>
/// ABMCS — <c>NN_ABMCS_010</c> (SKELETON, full impl deferred to parent-repo Loop 4):
/// <c>throw</c> for a business outcome inside a method declared with return type
/// <c>Maybe</c>, <c>Maybe&lt;T&gt;</c>, <c>Result&lt;T&gt;</c>, <c>Task&lt;Maybe&gt;</c>,
/// <c>Task&lt;Maybe&lt;T&gt;&gt;</c>, <c>Task&lt;Result&lt;T&gt;&gt;</c>, or
/// <c>ValueTask&lt;...&gt;</c> variants. Infrastructure exceptions remain unwrapped per
/// <c>docs/protocols/abmcs-error-handling.VowHuman.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status</b>: Skeleton — descriptor registered, detection body empty. Full implementation
/// lands in Loop 4.
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Warning"/> per AMB-11 user-locked decision.
/// </para>
/// <para>
/// <b>Planned detection</b>: <see cref="SyntaxKind.MethodDeclaration"/> action that inspects
/// the declared return type against the Maybe/Result family, then scans body
/// <see cref="SyntaxKind.ThrowStatement"/> + <see cref="SyntaxKind.ThrowExpression"/> events.
/// Distinguishing business outcomes from infrastructure exceptions is heuristic — proposed
/// heuristic: any <c>throw new X(...)</c> where <c>X</c> is in the
/// <see cref="System.ArgumentException"/> / <see cref="System.InvalidOperationException"/> /
/// <see cref="System.Collections.Generic.KeyNotFoundException"/> family is treated as a
/// business outcome and flagged; <see cref="System.Net.Sockets.SocketException"/> /
/// <see cref="System.IO.IOException"/> / DB exceptions / re-throws (<c>throw;</c> or
/// <c>throw caughtVar;</c>) are infrastructure and ignored.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbmcsThrowInMaybeMethodAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "ABMCS.ErrorHandling";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for <c>throw</c> inside a Maybe/Result method.</summary>
	public const string ABMCS010_DiagnosticId = "NN_ABMCS_010";

	private static readonly LocalizableString ABMCS010_Title =
		"Business 'throw' inside Maybe/Result method — return a structured failure instead";

	private static readonly LocalizableString ABMCS010_MessageFormat =
		"Method '{0}' returns a Maybe/Result-family type but contains a 'throw' for a business "
		+ "outcome. Use Maybe.Error/Result.Failure with a Problem instead. "
		+ "(NN_ABMCS_010)";

	private static readonly LocalizableString ABMCS010_Description =
		"ABMCS principle (per protocols/abmcs-error-handling.VowHuman.md): business outcomes "
		+ "are structured results, not exceptions. A method declared to return Maybe<T> / "
		+ "Result<T> (or their Task/ValueTask variants) that throws for a business outcome "
		+ "produces two failure modes: callers who should handle the structured failure "
		+ "silently degrade, and the operational observability story (logging, metrics) loses "
		+ "context. Infrastructure exceptions (network, disk, database) remain unwrapped and "
		+ "are NOT diagnosed by this rule.";

	/// <summary>NN_ABMCS_010 descriptor — Warning per AMB-11 user-locked decision.</summary>
	public static readonly DiagnosticDescriptor ABMCS010_Rule = new(
		ABMCS010_DiagnosticId,
		ABMCS010_Title,
		ABMCS010_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: ABMCS010_Description,
		helpLinkUri: HelpBase + "nn_abmcs_010");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(ABMCS010_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// TODO (Loop 4): wire detection — MethodDeclarationSyntax action checks return-type
		// family (Maybe/Maybe<T>/Result<T>/Task/ValueTask wrappers); scan body ThrowStatement /
		// ThrowExpression events; heuristic-classify business vs infrastructure exception.
		// Skeleton produces zero diagnostics today.
	}
}
