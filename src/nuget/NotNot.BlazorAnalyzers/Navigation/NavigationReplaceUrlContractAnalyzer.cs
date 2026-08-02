using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.BlazorAnalyzers.Lifecycle;

namespace NotNot.BlazorAnalyzers.Navigation;

/// <summary>
/// NNB041 — flags <c>NavigationManagerExtensions.ReplaceUrlStateAsync</c> invocations from Blazor
/// component methods when no synchronous UI-state mutation precedes the call. Turns the documented
/// producer contract at <c>NavigationManagerExtensions.cs:103-117</c> (regression class introduced
/// at commit <c>407350ad</c>) into a compile-time invariant.
/// <para>
/// <c>ReplaceUrlStateAsync</c> writes the browser URL via <c>history.pushState</c>/<c>replaceState</c>
/// through JS interop. Unlike <c>NavigationManager.NavigateTo</c>, it does NOT drive a Blazor router
/// navigation — <c>[SupplyParameterFromQuery]</c> parameters are NOT re-bound and
/// <c>OnParametersSetAsync</c> does NOT fire. Callers must update component state synchronously
/// before the call.
/// </para>
/// <para>
/// <b>Compliance modes</b> (any preceding statement in the same method body satisfies the contract):
/// <list type="bullet">
///   <item><description>(a) <c>_selectedSession = &lt;non-null&gt;;</c></description></item>
///   <item><description>(b) call to <c>ActivateSession*</c>/<c>SelectSession*</c>/<c>OpenTerminalTab*</c>/<c>EnsureSessionVisibleInSidebar*</c></description></item>
///   <item><description>(c) <c>_selectedSession = null;</c> or <c>SelectSession(null)</c> (cleanup)</description></item>
/// </list>
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NavigationReplaceUrlContractAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "Navigation";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Diagnostic ID for missing adjacent state mutation before <c>ReplaceUrlStateAsync</c>.</summary>
	public const string DiagnosticId = "NNB041";

	private static readonly LocalizableString Title =
		"ReplaceUrlStateAsync requires adjacent component-state mutation";

	private static readonly LocalizableString MessageFormat =
		"'{0}' calls ReplaceUrlStateAsync (router-bypassing history.pushState/replaceState) with no "
		+ "preceding synchronous UI-state update in the same method — the Blazor router will NOT "
		+ "re-bind [SupplyParameterFromQuery] params and OnParametersSetAsync will NOT fire, so the "
		+ "component silently desyncs from the URL (regression class introduced at commit 407350ad). "
		+ "Fix, cheapest-correct first: (1) call the ActivateSessionAndUpdateUrl helper "
		+ "(VowDashboard.Events.cs) which bundles state + URL atomically; (2) else assign the backing "
		+ "state field (e.g. '_selectedSession = <value>;') before this call; (3) cleanup flows: set "
		+ "'_selectedSession = null;' before this call; (4) genuinely URL-only (rare): '#pragma warning "
		+ "disable NNB041' with a documented reason, or set severity in .editorconfig. Contract: "
		+ "NavigationManagerExtensions.cs:103-117. (NNB041)";

	private static readonly LocalizableString Description =
		"NavigationManagerExtensions.ReplaceUrlStateAsync writes the browser URL via "
		+ "history.pushState/replaceState through JS interop. Unlike NavigationManager.NavigateTo, it "
		+ "does NOT drive a Blazor router navigation — [SupplyParameterFromQuery]-bound parameters "
		+ "retain their previous value and OnParametersSet/OnParametersSetAsync do not run. Any component "
		+ "event handler that calls this helper MUST therefore have already updated its own UI state "
		+ "synchronously (the backing field, e.g. _selectedSession, and any dependent collections) before "
		+ "the call. The migration at commit 407350ad moved 14 cosmetic URL updates from NavigateTo to "
		+ "ReplaceUrlStateAsync; 2 sites had no adjacent state mutation and silently relied on the "
		+ "eliminated re-binding cascade, producing a no-op click with zero error signal that took three "
		+ "investigation turns to root-cause. The fix consolidated the activation contract into the "
		+ "ActivateSessionAndUpdateUrl helper (VowDashboard.Events.cs). This analyzer makes the documented "
		+ "xml-doc contract (NavigationManagerExtensions.cs:103-117) a compile-time invariant. Suppress "
		+ "with #pragma warning disable NNB041 plus a documented reason only when the URL update genuinely "
		+ "requires no UI-state change.";

	/// <summary>NNB041 descriptor — Warning, Navigation category.</summary>
	public static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: HelpBase + "nnb041");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
	}

	// ── Detection ─────────────────────────────────────────────────────────────

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		// Cheap syntactic pre-filter: only resolve invocations whose callee name is
		// "ReplaceUrlStateAsync" (skip the millions of other invocations).
		if (!HasCalleeSimpleName(invocation, "ReplaceUrlStateAsync"))
			return;

		// Semantic resolve — confirm the method is NavigationManagerExtensions.ReplaceUrlStateAsync.
		var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
		var symbol = symbolInfo.Symbol as IMethodSymbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
		if (symbol == null)
			return;

		if (!string.Equals(symbol.Name, "ReplaceUrlStateAsync", StringComparison.Ordinal))
			return;
		if (!string.Equals(symbol.ContainingType?.Name, "NavigationManagerExtensions", StringComparison.Ordinal))
			return;

		// Path exemption — skip sample/demo pages.
		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
		if (IsExemptedPath(syntaxTreePath))
			return;

		// Enclosing method scope.
		var enclosingMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
		if (enclosingMethod == null)
			return;

		// Enclosing type must be a Blazor component (ComponentBase-derived).
		var enclosingClassDecl = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (enclosingClassDecl == null)
			return;

		var enclosingType = context.SemanticModel.GetDeclaredSymbol(enclosingClassDecl, context.CancellationToken);
		if (enclosingType == null)
			return;

		if (!BlazorLifecycleHelpers.IsBlazorComponent(enclosingType))
			return;

		// Adjacency walk — scan preceding statements in the method body for compliance.
		if (HasPrecedingCompliantStatement(invocation, enclosingMethod))
			return;

		// No compliant preceding statement — report NNB041.
		var methodName = enclosingMethod.Identifier.ValueText;
		context.ReportDiagnostic(Diagnostic.Create(
			Rule,
			invocation.GetLocation(),
			methodName));
	}

	// ── Syntactic pre-filter ──────────────────────────────────────────────────

	/// <summary>
	/// Returns true when the invocation's callee simple-name matches <paramref name="name"/>.
	/// Handles both <c>Method(...)</c> and <c>receiver.Method(...)</c> forms.
	/// </summary>
	private static bool HasCalleeSimpleName(InvocationExpressionSyntax invocation, string name)
	{
		return invocation.Expression switch
		{
			MemberAccessExpressionSyntax memberAccess =>
				string.Equals(memberAccess.Name.Identifier.ValueText, name, StringComparison.Ordinal),
			IdentifierNameSyntax identifier =>
				string.Equals(identifier.Identifier.ValueText, name, StringComparison.Ordinal),
			_ => false
		};
	}

	// ── Path exemption ────────────────────────────────────────────────────────

	/// <summary>
	/// Returns true for files under <c>/Pages/Samples/</c> or <c>/Pages/NnDesignSamples/</c>
	/// (pedagogical/demo pages — closest sibling convention from the RenderMode analyzer).
	/// </summary>
	private static bool IsExemptedPath(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return false;

		// Normalize backslash to forward slash for consistent matching.
		var normalized = filePath.Replace('\\', '/');
		return normalized.Contains("/Pages/Samples/")
			|| normalized.Contains("/Pages/NnDesignSamples/");
	}

	// ── Adjacency walk — compliance modes (a), (b), (c) ──────────────────────

	/// <summary>
	/// Scans statements preceding the <paramref name="invocation"/> in document order within
	/// the <paramref name="enclosingMethod"/> body. Returns true when any preceding statement
	/// satisfies one of the compliance modes.
	/// </summary>
	private static bool HasPrecedingCompliantStatement(
		InvocationExpressionSyntax invocation,
		MethodDeclarationSyntax enclosingMethod)
	{
		// Gather all statements from the method body (block body or expression body).
		var statements = enclosingMethod.Body?.Statements;
		if (statements == null || statements.Value.Count == 0)
			return false;

		var invocationSpanStart = invocation.SpanStart;

		foreach (var statement in statements.Value)
		{
			// Only check statements that precede the invocation in source order.
			if (statement.SpanStart >= invocationSpanStart)
				break;

			if (IsCompliantStatement(statement))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Returns true when the statement satisfies any compliance mode:
	/// (a) <c>_selectedSession = &lt;non-null&gt;;</c>,
	/// (b) call to <c>ActivateSession*</c> / <c>SelectSession*</c> / <c>OpenTerminalTab*</c> / <c>EnsureSessionVisibleInSidebar*</c>,
	/// (c) <c>_selectedSession = null;</c> or <c>SelectSession(null)</c>.
	/// </summary>
	private static bool IsCompliantStatement(StatementSyntax statement)
	{
		// Expression statements are the primary host for both assignments and invocations.
		if (statement is ExpressionStatementSyntax exprStatement)
		{
			var expr = exprStatement.Expression;

			// Mode (a) + (c): assignment to _selectedSession.
			if (expr is AssignmentExpressionSyntax assignment)
			{
				if (IsSelectedSessionAssignment(assignment))
					return true;
			}

			// Mode (b): invocation of ActivateSession* / SelectSession* / OpenTerminalTab* / EnsureSessionVisibleInSidebar*.
			if (expr is InvocationExpressionSyntax call)
			{
				if (IsComplianceHelperCall(call))
					return true;
			}

			// Mode (b) — also check await expressions wrapping invocations.
			if (expr is AwaitExpressionSyntax awaitExpr && awaitExpr.Expression is InvocationExpressionSyntax awaitedCall)
			{
				if (IsComplianceHelperCall(awaitedCall))
					return true;
			}

			// Mode (c): SelectSession(null) call.
			if (expr is InvocationExpressionSyntax selectNullCall)
			{
				if (IsSelectSessionNullCall(selectNullCall))
					return true;
			}

			if (expr is AwaitExpressionSyntax awaitExpr2 && awaitExpr2.Expression is InvocationExpressionSyntax awaitedSelectNull)
			{
				if (IsSelectSessionNullCall(awaitedSelectNull))
					return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Mode (a) + (c): checks for <c>_selectedSession = &lt;expr&gt;</c>.
	/// Returns true for ANY assignment to <c>_selectedSession</c> (both non-null and null).
	/// </summary>
	private static bool IsSelectedSessionAssignment(AssignmentExpressionSyntax assignment)
	{
		var target = assignment.Left;
		var targetName = target switch
		{
			IdentifierNameSyntax id => id.Identifier.ValueText,
			MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
			_ => null
		};

		return string.Equals(targetName, "_selectedSession", StringComparison.Ordinal);
	}

	/// <summary>
	/// Mode (b): checks for calls to helper methods whose names start with
	/// <c>ActivateSession</c>, <c>SelectSession</c>, <c>OpenTerminalTab</c>, or
	/// <c>EnsureSessionVisibleInSidebar</c>.
	/// </summary>
	private static bool IsComplianceHelperCall(InvocationExpressionSyntax invocation)
	{
		var calleeName = GetCalleeSimpleName(invocation);
		if (calleeName == null)
			return false;

		return calleeName.StartsWith("ActivateSession", StringComparison.Ordinal)
			|| calleeName.StartsWith("SelectSession", StringComparison.Ordinal)
			|| calleeName.StartsWith("OpenTerminalTab", StringComparison.Ordinal)
			|| calleeName.StartsWith("EnsureSessionVisibleInSidebar", StringComparison.Ordinal);
	}

	/// <summary>
	/// Mode (c): checks for <c>SelectSession(null)</c> invocation.
	/// </summary>
	private static bool IsSelectSessionNullCall(InvocationExpressionSyntax invocation)
	{
		var calleeName = GetCalleeSimpleName(invocation);
		if (!string.Equals(calleeName, "SelectSession", StringComparison.Ordinal))
			return false;

		// Verify the argument is null-literal.
		var args = invocation.ArgumentList.Arguments;
		if (args.Count == 0)
			return false;

		return args[0].Expression is LiteralExpressionSyntax literal
			&& literal.IsKind(SyntaxKind.NullLiteralExpression);
	}

	/// <summary>
	/// Extracts the simple-name of the callee from an invocation expression.
	/// </summary>
	private static string? GetCalleeSimpleName(InvocationExpressionSyntax invocation)
	{
		return invocation.Expression switch
		{
			MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			_ => null
		};
	}
}
