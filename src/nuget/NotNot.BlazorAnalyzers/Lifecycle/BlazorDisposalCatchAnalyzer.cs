using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects verbose catch blocks for cancel exceptions in DisposeAsync.
/// </summary>
/// <remarks>
/// Blazor disposal code often catches multiple cancel-related exceptions with separate catch blocks.
/// This is verbose, error-prone (easy to miss one exception type), and violates DRY.
/// The analyzer suggests using _WaitIgnoreCancel() which handles all cancel types consistently.
///
/// Scope v1: DisposeAsync methods in Blazor components only.
/// NOT applicable to patterns also catching ObjectDisposedException or JSException (different semantics).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BlazorDisposalCatchAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB009";

    private static readonly LocalizableString Title = "Verbose disposal exception catching - use _WaitIgnoreCancel()";
    private static readonly LocalizableString MessageFormat =
        "JS interop call '{0}' uses verbose catch blocks for disposal exceptions. " +
        "Consider using '._WaitIgnoreCancel()' which handles TaskCanceledException, " +
        "OperationCanceledException, and JSDisconnectedException automatically.";
    private static readonly LocalizableString Description =
        "Catching JSDisconnectedException, OperationCanceledException, or TaskCanceledException separately " +
        "in DisposeAsync is verbose and error-prone. Use _WaitIgnoreCancel() to handle all cancel " +
        "exceptions consistently in a single call.";
    private const string Category = "Lifecycle";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,  // WARNING severity per R3.2 - style improvement
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    // JS interop method names
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

        // Analyze method declarations to find DisposeAsync methods
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax methodDeclaration)
            return;

        // v1 Scope: Only analyze DisposeAsync methods (R3.7)
        if (methodDeclaration.Identifier.Text != "DisposeAsync")
            return;

        // Verify it's implementing IAsyncDisposable (returns ValueTask, no parameters)
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
            return;

        if (methodSymbol.Parameters.Length != 0)
            return;

        if (methodSymbol.ReturnType.Name != "ValueTask")
            return;

        // v1 Scope: Only analyze Blazor components (R3.3)
        var containingType = methodSymbol.ContainingType;
        if (containingType == null || !IsBlazorComponent(containingType))
            return;

        // Find all try statements in this method
        var tryStatements = methodDeclaration.DescendantNodes()
            .OfType<TryStatementSyntax>();

        foreach (var tryStatement in tryStatements)
        {
            AnalyzeTryStatement(context, tryStatement);
        }
    }

    private static void AnalyzeTryStatement(SyntaxNodeAnalysisContext context, TryStatementSyntax tryStatement)
    {
        // Check if try block contains JS interop calls
        var jsInteropInvocation = FindJsInteropCall(context, tryStatement.Block);
        if (jsInteropInvocation == null)
            return;

        // Analyze catch clauses
        bool hasCancelExceptionCatch = false;
        bool hasExcludedExceptionCatch = false;
        bool hasNonEmptyCatch = false;

        foreach (var catchClause in tryStatement.Catches)
        {
            // Check for excluded exception types (R2.2)
            if (IsExcludedExceptionCatch(context, catchClause))
            {
                hasExcludedExceptionCatch = true;
                continue;
            }

            // Check for cancel exception catches
            if (IsCancelExceptionCatch(context, catchClause))
            {
                // Skip if catch has non-trivial when filter
                if (HasNonTrivialWhenFilter(catchClause))
                    continue;

                // Check if catch body is empty
                if (IsEmptyCatchBody(catchClause.Block))
                {
                    hasCancelExceptionCatch = true;
                }
                else
                {
                    hasNonEmptyCatch = true;
                }
            }
        }

        // R2.2: Don't flag if pattern also catches ObjectDisposedException or JSException
        if (hasExcludedExceptionCatch)
            return;

        // Don't flag if catch has non-trivial logic
        if (hasNonEmptyCatch)
            return;

        // Report diagnostic if empty cancel exception catch found
        if (hasCancelExceptionCatch)
        {
            var callText = GetCallText(jsInteropInvocation);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                tryStatement.GetLocation(),
                callText));
        }
    }

    private static bool IsCancelExceptionCatch(SyntaxNodeAnalysisContext context, CatchClauseSyntax catchClause)
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
        return fullTypeName == "Microsoft.JSInterop.JSDisconnectedException" ||
               fullTypeName == "System.OperationCanceledException" ||
               fullTypeName == "System.Threading.Tasks.TaskCanceledException";
    }

    private static bool IsExcludedExceptionCatch(SyntaxNodeAnalysisContext context, CatchClauseSyntax catchClause)
    {
        if (catchClause.Declaration == null)
            return false;

        var exceptionType = catchClause.Declaration.Type;
        if (exceptionType == null)
            return false;

        // Use semantic model for robust type detection
        var typeSymbol = context.SemanticModel.GetTypeInfo(exceptionType).Type;
        if (typeSymbol == null)
            return false;

        var fullTypeName = typeSymbol.ToDisplayString();
        return fullTypeName == "System.ObjectDisposedException" ||
               fullTypeName == "Microsoft.JSInterop.JSException";
    }

    private static bool HasNonTrivialWhenFilter(CatchClauseSyntax catchClause)
    {
        if (catchClause.Filter == null)
            return false;

        // Trivial filter: when (true)
        if (catchClause.Filter.FilterExpression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.TrueLiteralExpression))
            return false;

        return true;
    }

    private static bool IsEmptyCatchBody(BlockSyntax? block)
    {
        if (block == null)
            return true;

        // Empty block
        if (block.Statements.Count == 0)
            return true;

        // Single statement patterns (R3.6)
        if (block.Statements.Count == 1)
        {
            var statement = block.Statements[0];

            // Return statements (return, return default, return null, return false)
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

            // Logging-only expressions
            if (statement is ExpressionStatementSyntax exprStmt)
            {
                return IsLoggingOnlyExpression(exprStmt.Expression);
            }
        }

        return false;
    }

    private static bool IsLoggingOnlyExpression(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                var fullCall = memberAccess.ToString();
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

    private static string GetCallText(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var expressionText = memberAccess.Expression.ToString();
            var methodName = memberAccess.Name.Identifier.Text;

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

    private static bool IsBlazorComponent(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            if (current.Name == "ComponentBase" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components")
            {
                return true;
            }
            current = current.BaseType;
        }

        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IComponent" &&
            i.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }
}
