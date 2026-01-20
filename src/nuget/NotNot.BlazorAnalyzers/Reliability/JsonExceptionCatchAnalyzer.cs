using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Reliability;

/// <summary>
/// Analyzer that detects catching+eating JsonException from JS interop calls.
/// </summary>
/// <remarks>
/// When JS interop calls fail with JsonException, it typically indicates the JavaScript function
/// threw an unhandled exception (null reference, undefined property) that got serialized as JSON error.
/// The correct fix is to improve the JavaScript function with defensive coding, NOT to catch in C#.
///
/// FAIL_FAST_PRINCIPLE: Masking JS bugs violates fail-fast. Developer must fix root cause.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class JsonExceptionCatchAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB008";

    private static readonly LocalizableString Title = "JsonException caught from JS interop - fix the JavaScript instead";
    private static readonly LocalizableString MessageFormat =
        "JS interop call '{0}' catches JsonException. This typically indicates a JS function bug " +
        "(null reference, undefined property). Fix the JS function with defensive coding " +
        "(null checks, optional chaining) instead of catching in C#.";
    private static readonly LocalizableString Description =
        "Catching JsonException from JS interop calls masks JavaScript bugs. When a JS function throws " +
        "an unhandled exception, it gets serialized as a JSON error. The correct fix is to improve " +
        "the JavaScript function with defensive coding (null checks, optional chaining, try-catch in JS), " +
        "not to silently swallow the exception in C#.";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,  // ERROR severity per R3.1 - FAIL_FAST violation
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    // JS interop method names that can throw JsonException when JS function fails
    private static readonly string[] JsInteropMethodNames =
    {
        "InvokeAsync",
        "InvokeVoidAsync",
        "InvokeUnmarshalled",
        "InvokeUnmarshalledAsync",
        "Invoke"
    };

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // Must analyze generated code because Razor components compile to generated C#
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        // Analyze catch clauses to find JsonException catches
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not CatchClauseSyntax catchClause)
            return;

        // Check if this catches JsonException
        if (!IsJsonExceptionCatch(context, catchClause))
            return;

        // Skip if catch has non-trivial when filter (conditional handling is intentional)
        if (HasNonTrivialWhenFilter(catchClause))
            return;

        // Check if catch body is "empty" (eating the exception)
        if (!IsEmptyCatchBody(catchClause.Block))
            return;

        // Find the enclosing try statement
        if (catchClause.Parent is not TryStatementSyntax tryStatement)
            return;

        // Check if try block contains JS interop calls
        var jsInteropInvocation = FindJsInteropCall(context, tryStatement.Block);
        if (jsInteropInvocation == null)
            return;

        // P0-1 SAFETY: Skip if try block contains any System.Text.Json namespace method calls
        // This prevents false positives on legitimate STJ usage
        if (ContainsSystemTextJsonCall(context, tryStatement.Block))
            return;

        // Report diagnostic
        var callText = GetCallText(jsInteropInvocation);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            catchClause.GetLocation(),
            callText));
    }

    private static bool IsJsonExceptionCatch(SyntaxNodeAnalysisContext context, CatchClauseSyntax catchClause)
    {
        if (catchClause.Declaration == null)
            return false;

        var exceptionType = catchClause.Declaration.Type;
        if (exceptionType == null)
            return false;

        // Use semantic model for robust type detection (handles aliases, global::, etc.)
        var typeSymbol = context.SemanticModel.GetTypeInfo(exceptionType).Type;
        if (typeSymbol == null)
            return false;

        var fullTypeName = typeSymbol.ToDisplayString();
        return fullTypeName == "System.Text.Json.JsonException";
    }

    private static bool HasNonTrivialWhenFilter(CatchClauseSyntax catchClause)
    {
        // No filter = not filtered
        if (catchClause.Filter == null)
            return false;

        // Check for trivial filters like "when (true)"
        if (catchClause.Filter.FilterExpression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.TrueLiteralExpression))
            return false;

        // Any other filter = non-trivial, skip this catch
        return true;
    }

    private static bool IsEmptyCatchBody(BlockSyntax? block)
    {
        if (block == null)
            return true;

        // Empty block
        if (block.Statements.Count == 0)
            return true;

        // Single statement patterns
        if (block.Statements.Count == 1)
        {
            var statement = block.Statements[0];

            // return statements (return, return default, return null, return false)
            if (statement is ReturnStatementSyntax returnStmt)
            {
                if (returnStmt.Expression == null) return true; // bare return
                if (returnStmt.Expression is DefaultExpressionSyntax) return true; // return default
                if (returnStmt.Expression is LiteralExpressionSyntax lit)
                {
                    return lit.IsKind(SyntaxKind.NullLiteralExpression) ||
                           lit.IsKind(SyntaxKind.FalseLiteralExpression) ||
                           lit.IsKind(SyntaxKind.DefaultLiteralExpression);
                }
                return false;
            }

            // Comment-only or logging-only would be in ExpressionStatement
            // For simplicity, we consider single expression statements as "logic"
            // unless it's a logging call (which we treat as empty)
            if (statement is ExpressionStatementSyntax exprStmt)
            {
                return IsLoggingOnlyExpression(exprStmt.Expression);
            }
        }

        return false;
    }

    private static bool IsLoggingOnlyExpression(ExpressionSyntax expression)
    {
        // Check for logging method calls
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var fullCall = memberAccess.ToString();
                // Logging patterns allowlist
                return fullCall.StartsWith("Debug.Write") ||
                       fullCall.StartsWith("Console.Write") ||
                       fullCall.StartsWith("Trace.Write") ||
                       fullCall.StartsWith("_logger.Log") ||
                       fullCall.Contains(".LogDebug") ||
                       fullCall.Contains(".LogWarning") ||
                       fullCall.Contains(".LogError") ||
                       fullCall.Contains(".LogInformation");
            }
        }
        return false;
    }

    private static InvocationExpressionSyntax? FindJsInteropCall(SyntaxNodeAnalysisContext context, BlockSyntax tryBlock)
    {
        var invocations = tryBlock.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            if (IsJsInteropCall(context, invocation))
                return invocation;
        }

        return null;
    }

    private static bool IsJsInteropCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        // Get the method name being called
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

        // Get the symbol to verify it's from IJSRuntime or IJSObjectReference
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
            return false;

        var containingType = calledMethod.ContainingType;
        if (containingType == null)
            return false;

        // Check if it's a JS interop type
        var containingNamespace = containingType.ContainingNamespace?.ToDisplayString();
        if (containingNamespace != "Microsoft.JSInterop")
            return false;

        // Check for IJSRuntime, IJSObjectReference, or related types
        var typeName = containingType.Name;
        return typeName.Contains("JS") ||
               containingType.AllInterfaces.Any(i =>
                   i.Name.Contains("JS") &&
                   i.ContainingNamespace?.ToDisplayString() == "Microsoft.JSInterop");
    }

    private static bool ContainsSystemTextJsonCall(SyntaxNodeAnalysisContext context, BlockSyntax tryBlock)
    {
        var invocations = tryBlock.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol method)
            {
                var containingNamespace = method.ContainingType?.ContainingNamespace?.ToDisplayString();
                if (containingNamespace != null && containingNamespace.StartsWith("System.Text.Json"))
                    return true;
            }
        }

        return false;
    }

    private static string GetCallText(InvocationExpressionSyntax invocation)
    {
        // Get a readable representation of the call
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var expressionText = memberAccess.Expression.ToString();
            var methodName = memberAccess.Name.Identifier.Text;

            // Truncate long expressions
            if (expressionText.Length > 20)
                expressionText = expressionText.Substring(0, 17) + "...";

            return $"{expressionText}.{methodName}";
        }

        if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
        {
            return $"?.{memberBinding.Name.Identifier.Text}";
        }

        return invocation.Expression.ToString();
    }
}
