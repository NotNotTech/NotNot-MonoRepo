using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects JS interop try-catch blocks catching cancellation exceptions
/// but missing JSDisconnectedException — the converse of NNB012.
/// </summary>
/// <remarks>
/// NNB012 catches: JSDisconnectedException handled, TaskCanceledException missing.
/// NNB013 catches: TaskCanceledException/OperationCanceledException handled, JSDisconnectedException missing.
///
/// Unlike NNB012, this rule requires a JS-interop gate because OperationCanceledException
/// is commonly caught for non-JS-interop cancellation (CancellationToken, Task cancellation).
/// NNB013 only fires when the try block contains actual JS interop calls.
///
/// Complements NNB007 (which is scoped to DisposeAsync in Blazor components only).
/// NNB013 applies to any method in any class.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class JsInteropIncompleteHandlingAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NNB013";

	private static readonly LocalizableString Title = "JS interop catch handles cancellation but not JSDisconnectedException";
	private static readonly LocalizableString MessageFormat =
		"Try block contains JS interop calls and catches TaskCanceledException/OperationCanceledException " +
		"but not JSDisconnectedException. JS interop throws JSDisconnectedException when the circuit " +
		"is already disconnected before the call.";
	private static readonly LocalizableString Description =
		"When a Blazor Server circuit disconnects, JS interop calls can throw either JSDisconnectedException " +
		"(when the runtime knows the circuit is gone before the call) or TaskCanceledException (when a call " +
		"is already in-flight and the CancellationToken fires). Both must be handled to prevent circuit crashes. " +
		"Add JSDisconnectedException to the catch filter.";
	private const string Category = "Reliability";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	// JS interop method names that can throw during circuit disconnect
	private static readonly string[] JsInteropMethodNames =
	{
		"InvokeAsync",
		"InvokeVoidAsync",
		"InvokeUnmarshalled",
		"InvokeUnmarshalledAsync",
		"Invoke"
	};

	// Safe wrapper methods that internally handle all cancellation types (from NotNot.Bcl.Core)
	private static readonly string[] SafeWrapperMethodNames =
	{
		"_WaitIgnoreCancel",
		"_WaitIgnoreCancelOrNull"
	};

	/// <inheritdoc/>
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

		// JS-interop gate: only analyze try blocks containing JS interop calls
		// (OperationCanceledException is commonly caught for non-JS purposes)
		// Guard against analyzer crash in semantic model resolution
		try
		{
			if (!ContainsJsInteropCall(context, tryStatement.Block))
				return;

			// Exempt if all JS interop calls use safe wrappers
			if (AllJsInteropCallsSafeWrapped(context, tryStatement.Block))
				return;
		}
		catch (System.Exception)
		{
			// Semantic model failed — skip JS-interop gate (fail-open: may produce
			// false positives on non-JS code, but won't miss JS interop defects)
		}

		bool handlesCancellation = false;
		bool handlesJsDisconnected = false;
		CatchClauseSyntax? firstCancellationCatch = null;

		foreach (var catchClause in tryStatement.Catches)
		{
			var result = AnalyzeCatchClause(context, catchClause);

			if (result.HandlesCancellation)
			{
				handlesCancellation = true;
				firstCancellationCatch ??= catchClause;
			}

			if (result.HandlesJsDisconnected)
			{
				handlesJsDisconnected = true;
			}
		}

		// Report if cancellation is caught but JSDisconnectedException is not
		if (handlesCancellation && !handlesJsDisconnected && firstCancellationCatch != null)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				Rule,
				firstCancellationCatch.GetLocation()));
		}
	}

	private static CatchAnalysisResult AnalyzeCatchClause(SyntaxNodeAnalysisContext context, CatchClauseSyntax catchClause)
	{
		var result = new CatchAnalysisResult();

		if (catchClause.Declaration == null)
		{
			// Bare catch {} — catches everything
			result.HandlesJsDisconnected = true;
			result.HandlesCancellation = true;
			return result;
		}

		var declType = catchClause.Declaration.Type;
		if (declType == null) return result;

		var typeSymbol = context.SemanticModel.GetTypeInfo(declType).Type;
		bool isBaseException;

		if (typeSymbol != null && typeSymbol.TypeKind != TypeKind.Error)
		{
			ClassifyTypeSymbol(typeSymbol, ref result);
			isBaseException = IsSystemException(typeSymbol);
		}
		else
		{
			var typeText = declType.ToString();
			ClassifyByName(typeText, ref result);
			isBaseException = typeText == "Exception" || typeText == "System.Exception";
		}

		// Blanket catch WITHOUT filter — catches everything
		if (catchClause.Filter == null)
		{
			if (isBaseException)
			{
				result.HandlesJsDisconnected = true;
				result.HandlesCancellation = true;
			}
			return result;
		}

		// Any catch with when filter — classify via positive-only matches
		if (catchClause.Filter != null)
		{
			ClassifyFilterTypes(catchClause.Filter.FilterExpression, ref result);
		}

		return result;
	}

	private static void ClassifyTypeSymbol(ITypeSymbol typeSymbol, ref CatchAnalysisResult result)
	{
		var name = typeSymbol.Name;
		var ns = typeSymbol.ContainingNamespace?.ToDisplayString();

		if (name == "JSDisconnectedException" && ns == "Microsoft.JSInterop")
			result.HandlesJsDisconnected = true;

		if ((name == "TaskCanceledException" && ns == "System.Threading.Tasks") ||
			(name == "OperationCanceledException" && ns == "System"))
			result.HandlesCancellation = true;
	}

	private static void ClassifyByName(string typeName, ref CatchAnalysisResult result)
	{
		if (typeName.Contains("JSDisconnectedException"))
			result.HandlesJsDisconnected = true;
		if (typeName.Contains("TaskCanceledException") || typeName.Contains("OperationCanceledException"))
			result.HandlesCancellation = true;
	}

	private static bool IsSystemException(ITypeSymbol typeSymbol)
	{
		return typeSymbol.Name == "Exception" &&
			   typeSymbol.ContainingNamespace?.ToDisplayString() == "System";
	}

	#region Filter Type Classification

	private static void ClassifyFilterTypes(ExpressionSyntax filterExpression, ref CatchAnalysisResult result)
	{
		var negatedTexts = filterExpression.DescendantNodes()
			.OfType<UnaryPatternSyntax>()
			.Select(u => u.Pattern.ToString())
			.ToList();

		var filterText = filterExpression.ToString();

		CheckPositiveMatch(filterText, negatedTexts, "JSDisconnectedException", ref result, isDisconnect: true);
		CheckPositiveMatch(filterText, negatedTexts, "TaskCanceledException", ref result, isDisconnect: false);
		CheckPositiveMatch(filterText, negatedTexts, "OperationCanceledException", ref result, isDisconnect: false);
	}

	private static void CheckPositiveMatch(string filterText, System.Collections.Generic.List<string> negatedTexts, string typeName, ref CatchAnalysisResult result, bool isDisconnect)
	{
		if (!filterText.Contains(typeName))
			return;

		int totalCount = CountOccurrences(filterText, typeName);
		int negatedCount = 0;
		foreach (var nt in negatedTexts)
		{
			negatedCount += CountOccurrences(nt, typeName);
		}

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

	#region JS Interop Detection

	private static bool ContainsJsInteropCall(SyntaxNodeAnalysisContext context, BlockSyntax tryBlock)
	{
		return tryBlock.DescendantNodes()
			.OfType<InvocationExpressionSyntax>()
			.Any(inv => IsJsInteropCall(context, inv));
	}

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
		{
			methodName = memberAccess.Name.Identifier.Text;
		}
		else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
		{
			methodName = memberBinding.Name.Identifier.Text;
		}

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
		{
			current = current.Parent;
		}

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
