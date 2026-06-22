using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// NNB045 — flags a transition-guard field claim assigned AFTER an <c>await</c> inside an async
/// Blazor lifecycle override (<c>OnParametersSetAsync</c> primary; <c>OnInitializedAsync</c> /
/// <c>OnAfterRenderAsync</c> secondary). A guard of the form <c>if (x != _field) { … await …; _field = x; }</c>
/// leaves <c>_field</c> unclaimed across the await window.
/// <para>
/// Blazor re-invokes async lifecycle methods (esp. <c>OnParametersSetAsync</c>, on every parameter
/// change) and renders the component tree at each await suspension. A late claim means a re-render
/// during the await re-enters the still-unclaimed guard (duplicate side-effecting work), and any
/// child component whose post-render restore (e.g. persisted expand-state) lands during the window
/// is clobbered by the resumed continuation's late reset.
/// </para>
/// <para>
/// Detection is semantic for two facts — the containing type derives from <c>ComponentBase</c>
/// (<see cref="BlazorLifecycleHelpers.IsBlazorComponent"/>) and the guard-field symbol identity. Both
/// operands of the <c>!=</c> condition are candidate guard fields; the guard field is whichever
/// candidate is re-assigned after the await (the LHS of the transition claim, e.g. <c>_field</c> in
/// <c>if (newId != _field) { … await …; _field = newId; }</c>) — never a positional pick.
/// Ordering is syntactic (span comparison). Assignments/awaits inside nested lambdas or
/// local functions are excluded (deferred execution — not a synchronous claim).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransitionGuardClaimAfterAwaitAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "Lifecycle";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for a transition-guard claim assigned after an await in an async lifecycle override.</summary>
	public const string DiagnosticId = "NNB045";

	private static readonly LocalizableString Title =
		"Transition-guard field claimed after 'await' in async lifecycle method";

	private static readonly LocalizableString MessageFormat =
		"Async lifecycle '{0}' claims transition-guard field '{1}' after an 'await'. A re-render "
		+ "during the await re-enters the unclaimed guard (duplicate work) and a child's post-render "
		+ "restore can be clobbered by the late reset. Fix: hoist the '{1}' claim (and the block's "
		+ "synchronous state resets) ABOVE the first 'await' — claim synchronously, then do async "
		+ "teardown. Suppress with #pragma warning disable NNB045 only if awaiting before the claim is "
		+ "genuinely required (rare). See NNB045.";

	private static readonly LocalizableString Description =
		"Blazor re-invokes async lifecycle methods (esp. OnParametersSetAsync, on every parameter "
		+ "change) and renders the component tree at each await suspension. A transition guard "
		+ "if (x != _field) that assigns _field only AFTER an await leaves the guard unclaimed across "
		+ "the await window: a re-entrant pass re-runs the side-effecting block, and any child component "
		+ "whose post-render restore (e.g. persisted expand-state) lands during the window is "
		+ "overwritten by the resumed continuation's late reset. The fix is to claim the guard field — "
		+ "and perform the block's synchronous state resets — before the first await, so the first "
		+ "render emitted at the await already reflects the new transition's state (seed order: "
		+ "persisted -> snapshot -> declared default). Canonical reference: the claim-then-await pattern "
		+ "in VowSessionMetaPanel.OnParametersSetAsync.";

	/// <summary>NNB045 descriptor — Warning, Lifecycle category.</summary>
	public static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: HelpBase + "nnb045");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(Rule);

	/// <summary>Async lifecycle override names this rule analyzes.</summary>
	private static readonly string[] LifecycleMethodNames =
	{
		"OnParametersSetAsync",
		"OnInitializedAsync",
		"OnAfterRenderAsync",
	};

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
	}

	// ── Detection ─────────────────────────────────────────────────────────────

	private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
	{
		var method = (MethodDeclarationSyntax)context.Node;

		// Cheap syntactic pre-filter: async modifier + lifecycle method name.
		if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
			return;
		if (!LifecycleMethodNames.Contains(method.Identifier.ValueText))
			return;

		var body = method.Body;
		if (body == null)
			return;

		// Semantic gate: containing type must be a Blazor component.
		var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);
		var containingType = methodSymbol?.ContainingType;
		if (containingType == null || !BlazorLifecycleHelpers.IsBlazorComponent(containingType))
			return;

		var lifecycleMethodName = method.Identifier.ValueText;

		// Each transition-guard if-statement in the method body.
		foreach (var ifStatement in body.DescendantNodes().OfType<IfStatementSyntax>())
		{
			AnalyzeGuard(context, ifStatement, lifecycleMethodName);
		}
	}

	private static void AnalyzeGuard(SyntaxNodeAnalysisContext context, IfStatementSyntax ifStatement, string lifecycleMethodName)
	{
		// Condition must be a '!=' comparison; at least one operand must resolve to a field/property of the type.
		if (ifStatement.Condition is not BinaryExpressionSyntax binary
			|| !binary.IsKind(SyntaxKind.NotEqualsExpression))
			return;

		// Both operands are candidate guard fields. The real claim is whichever candidate is RE-ASSIGNED
		// after the await (the LHS of the transition claim), NOT a positional pick — in 'newId != _field'
		// the claim is '_field = newId', so the guard field is _field (the right operand here), and in the
		// real-world 'local newId vs field _field' shape only _field resolves to a member at all.
		var leftCandidate = ResolveMemberSymbol(context, binary.Left);
		var rightCandidate = ResolveMemberSymbol(context, binary.Right);
		if (leftCandidate == null && rightCandidate == null)
			return;

		var ifBlock = ifStatement.Statement;

		// First await in the if-block, excluding awaits inside nested lambdas / local functions.
		var firstAwait = ifBlock.DescendantNodes(descendIntoChildren: node => !IsDeferredScope(node))
			.OfType<AwaitExpressionSyntax>()
			.FirstOrDefault();
		if (firstAwait == null)
			return;

		var firstAwaitEnd = firstAwait.Span.End;

		// First post-await assignment (synchronous scope) whose LHS resolves to either candidate guard
		// field. That candidate is the guard field being claimed after the await.
		foreach (var assignment in ifBlock.DescendantNodes(descendIntoChildren: node => !IsDeferredScope(node))
			.OfType<AssignmentExpressionSyntax>())
		{
			if (assignment.SpanStart <= firstAwaitEnd)
				continue;

			var claimedField = ResolveClaimedCandidate(context, assignment, leftCandidate, rightCandidate);
			if (claimedField == null)
				continue;

			context.ReportDiagnostic(Diagnostic.Create(
				Rule,
				assignment.GetLocation(),
				lifecycleMethodName,
				claimedField.Name));
			return; // one diagnostic per guard is sufficient
		}
	}

	// ── Symbol resolution ─────────────────────────────────────────────────────

	/// <summary>
	/// Returns the candidate guard field whose symbol matches the assignment LHS, or null when the
	/// assignment claims neither candidate. Checks the left condition operand first, then the right.
	/// </summary>
	private static ISymbol? ResolveClaimedCandidate(
		SyntaxNodeAnalysisContext context,
		AssignmentExpressionSyntax assignment,
		ISymbol? leftCandidate,
		ISymbol? rightCandidate)
	{
		if (leftCandidate != null && IsAssignmentToSymbol(context, assignment, leftCandidate))
			return leftCandidate;
		if (rightCandidate != null && IsAssignmentToSymbol(context, assignment, rightCandidate))
			return rightCandidate;
		return null;
	}

	private static ISymbol? ResolveMemberSymbol(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
	{
		var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
		if (symbol is IFieldSymbol or IPropertySymbol)
			return symbol;
		return null;
	}

	private static bool IsAssignmentToSymbol(SyntaxNodeAnalysisContext context, AssignmentExpressionSyntax assignment, ISymbol guardField)
	{
		var lhsSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
		return lhsSymbol != null && SymbolEqualityComparer.Default.Equals(lhsSymbol, guardField);
	}

	// ── Deferred-scope exclusion ───────────────────────────────────────────────

	/// <summary>
	/// True for nodes that introduce a deferred-execution scope (lambdas, anonymous methods, local
	/// functions). Awaits and assignments inside these run later than the synchronous method body, so
	/// a claim there is NOT a synchronous claim and must be excluded from both the await search and the
	/// assignment search.
	/// </summary>
	private static bool IsDeferredScope(SyntaxNode node)
		=> node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;
}
