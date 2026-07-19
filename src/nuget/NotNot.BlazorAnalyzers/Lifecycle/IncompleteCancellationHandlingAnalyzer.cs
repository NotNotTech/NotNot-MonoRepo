using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects catch blocks handling JSDisconnectedException but missing
/// TaskCanceledException/OperationCanceledException.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class IncompleteCancellationHandlingAnalyzer : DiagnosticAnalyzer
{
	/// <summary>The diagnostic ID (NNB012) reported when a catch handles JSDisconnectedException but
	/// not TaskCanceledException/OperationCanceledException.</summary>
	public const string DiagnosticId = "NNB012";

	private static readonly LocalizableString Title = "JS interop catch handles JSDisconnectedException but not TaskCanceledException";
	private static readonly LocalizableString MessageFormat =
		"Catch block handles JSDisconnectedException but not TaskCanceledException or OperationCanceledException. " +
		"JS interop calls throw TaskCanceledException when the circuit disconnects during an in-flight call.";
	private static readonly LocalizableString Description =
		"When a Blazor Server circuit disconnects, JS interop calls can throw either JSDisconnectedException " +
		"or TaskCanceledException. Both must be handled to prevent circuit crashes.";
	private const string Category = "Reliability";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId, Title, MessageFormat, Category,
		DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description,
		helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

	/// <summary>The single NNB012 rule this analyzer reports.</summary>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	private static readonly string[] JsInteropMethodNames =
		{ "InvokeAsync", "InvokeVoidAsync", "InvokeUnmarshalled", "InvokeUnmarshalledAsync", "Invoke" };

	private static readonly string[] SafeWrapperMethodNames =
		{ "_WaitIgnoreCancel", "_WaitIgnoreCancelOrNull" };

	/// <summary>Registers a syntax-node action over <c>try</c> statements to inspect their catch clauses.</summary>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeTryStatement, SyntaxKind.TryStatement);
	}

	private static void AnalyzeTryStatement(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not TryStatementSyntax tryStatement)
			return;

		bool handlesJsDisconnected = false;
		bool handlesCancellation = false;
		CatchClauseSyntax? firstJsDisconnectedCatch = null;

		foreach (var catchClause in tryStatement.Catches)
		{
			var result = AnalyzeCatchClause(catchClause);

			if (result.HandlesJsDisconnected)
			{
				handlesJsDisconnected = true;
				firstJsDisconnectedCatch ??= catchClause;
			}
			if (result.HandlesCancellation)
				handlesCancellation = true;
		}

		if (!handlesJsDisconnected || handlesCancellation || firstJsDisconnectedCatch == null)
			return;

		// Exempt if all JS interop calls in the try block use safe wrappers
		// Guard against analyzer crash in semantic model resolution
		try
		{
			if (AllJsInteropCallsSafeWrapped(context, tryStatement.Block))
				return;
		}
		catch (System.Exception)
		{
			// Safe-wrapper check uses semantic model which may fail — proceed with reporting
		}

		context.ReportDiagnostic(Diagnostic.Create(Rule, firstJsDisconnectedCatch.GetLocation()));
	}

	private static CatchAnalysisResult AnalyzeCatchClause(CatchClauseSyntax catchClause)
	{
		var result = new CatchAnalysisResult();

		if (catchClause.Declaration == null)
		{
			result.HandlesJsDisconnected = true;
			result.HandlesCancellation = true;
			return result;
		}

		var typeName = catchClause.Declaration.Type?.ToString() ?? "";

		// Direct catch of specific types
		if (typeName.Contains("JSDisconnectedException"))
			result.HandlesJsDisconnected = true;
		if (typeName.Contains("TaskCanceledException") || typeName.Contains("OperationCanceledException"))
			result.HandlesCancellation = true;

		// Blanket Exception catch WITHOUT filter
		if (IsExceptionType(typeName) && catchClause.Filter == null)
		{
			result.HandlesJsDisconnected = true;
			result.HandlesCancellation = true;
			return result;
		}

		// Exception with when filter — classify via positive-only matches
		if (IsExceptionType(typeName) && catchClause.Filter != null)
		{
			ClassifyFilterTypes(catchClause.Filter.FilterExpression, ref result);
		}

		return result;
	}

	private static bool IsExceptionType(string typeName)
	{
		return typeName == "Exception" || typeName == "System.Exception";
	}

	#region Filter Type Classification

	/// <summary>
	/// Classifies exception types in a filter expression, counting only positive (non-negated) matches.
	/// Correctly handles mixed filters like `ex is TaskCanceledException || ex is not OperationCanceledException`
	/// by collecting negated type text and excluding it from the positive match set.
	/// </summary>
	private static void ClassifyFilterTypes(ExpressionSyntax filterExpression, ref CatchAnalysisResult result)
	{
		// Collect all text inside `not` patterns — these are EXCLUDED from positive matching
		var negatedTexts = filterExpression.DescendantNodes()
			.OfType<UnaryPatternSyntax>()
			.Select(u => u.Pattern.ToString())
			.ToList();

		// Get the full filter text
		var filterText = filterExpression.ToString();

		// For each target type: check if it appears in the filter AND is not exclusively negated
		CheckPositiveMatch(filterText, negatedTexts, "JSDisconnectedException", ref result, isDisconnect: true);
		CheckPositiveMatch(filterText, negatedTexts, "TaskCanceledException", ref result, isDisconnect: false);
		CheckPositiveMatch(filterText, negatedTexts, "OperationCanceledException", ref result, isDisconnect: false);
	}

	private static void CheckPositiveMatch(string filterText, System.Collections.Generic.List<string> negatedTexts, string typeName, ref CatchAnalysisResult result, bool isDisconnect)
	{
		if (!filterText.Contains(typeName))
			return;

		// Check if ALL occurrences are inside negated patterns
		// If any occurrence is NOT negated, it's a positive match
		bool allNegated = negatedTexts.Any(nt => nt.Contains(typeName));

		// Count total occurrences in filter vs occurrences in negated text
		int totalCount = CountOccurrences(filterText, typeName);
		int negatedCount = 0;
		foreach (var nt in negatedTexts)
		{
			negatedCount += CountOccurrences(nt, typeName);
		}

		// Positive match only if there are more total occurrences than negated ones
		if (totalCount > negatedCount)
		{
			if (isDisconnect)
				result.HandlesJsDisconnected = true;
			else
				result.HandlesCancellation = true;
		}
	}

	private static int CountOccurrences(string text, string search)
	{
		int count = 0;
		int index = 0;
		while ((index = text.IndexOf(search, index, System.StringComparison.Ordinal)) != -1)
		{
			count++;
			index += search.Length;
		}
		return count;
	}

	#endregion

	#region JS Interop Detection & Safe Wrapper Check

	private static bool AllJsInteropCallsSafeWrapped(SyntaxNodeAnalysisContext context, BlockSyntax tryBlock)
	{
		var jsInteropCalls = tryBlock.DescendantNodes()
			.OfType<InvocationExpressionSyntax>()
			.Where(inv => IsJsInteropCall(context, inv))
			.ToList();

		if (jsInteropCalls.Count == 0)
			return false;

		return jsInteropCalls.All(call => IsWrappedWithSafeMethod(call));
	}

	private static bool IsJsInteropCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
	{
		string? methodName = null;
		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
			methodName = memberAccess.Name.Identifier.Text;
		else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
			methodName = memberBinding.Name.Identifier.Text;

		if (methodName == null || !JsInteropMethodNames.Contains(methodName))
			return false;

		var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
		if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
			return false;

		var containingType = calledMethod.ContainingType;
		if (containingType == null)
			return false;

		var containingNamespace = containingType.ContainingNamespace?.ToDisplayString();
		if (containingNamespace != "Microsoft.JSInterop")
			return false;

		var typeName = containingType.Name;
		return typeName.Contains("JS") ||
			   containingType.AllInterfaces.Any(i =>
				   i.Name.Contains("JS") &&
				   i.ContainingNamespace?.ToDisplayString() == "Microsoft.JSInterop");
	}

	private static bool IsWrappedWithSafeMethod(InvocationExpressionSyntax invocation)
	{
		SyntaxNode? current = invocation.Parent;
		while (current is ParenthesizedExpressionSyntax or ConditionalAccessExpressionSyntax)
			current = current.Parent;

		if (current is not MemberAccessExpressionSyntax memberAccess)
			return false;

		var methodName = memberAccess.Name.Identifier.Text;
		if (!SafeWrapperMethodNames.Contains(methodName))
			return false;

		return memberAccess.Parent is InvocationExpressionSyntax;
	}

	#endregion

	private struct CatchAnalysisResult
	{
		public bool HandlesJsDisconnected;
		public bool HandlesCancellation;
	}
}
