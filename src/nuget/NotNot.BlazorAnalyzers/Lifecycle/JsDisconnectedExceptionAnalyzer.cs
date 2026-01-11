using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects JS interop calls in DisposeAsync methods that don't handle JSDisconnectedException.
/// </summary>
/// <remarks>
/// When a Blazor Server circuit disconnects, JS interop calls throw JSDisconnectedException.
/// If not caught in DisposeAsync, this can crash the component disposal chain.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class JsDisconnectedExceptionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB007";

    private static readonly LocalizableString Title = "JSDisconnectedException not handled in DisposeAsync";
    private static readonly LocalizableString MessageFormat =
        "JS interop call '{0}' in DisposeAsync should catch JSDisconnectedException. " +
        "This exception is thrown when the Blazor circuit is disconnected (e.g., during navigation).";
    private static readonly LocalizableString Description =
        "JS interop calls in DisposeAsync methods can throw JSDisconnectedException when the Blazor Server " +
        "circuit is disconnected. This is expected during navigation and should be caught to prevent crashes.";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    // JS interop method names that can throw JSDisconnectedException
    private static readonly string[] JsInteropMethodNames =
    {
        "InvokeAsync",
        "InvokeVoidAsync",
        "InvokeUnmarshalled",
        "InvokeUnmarshalledAsync",
        "Invoke"
    };

    // Safe wrapper methods that internally handle JSDisconnectedException (from NotNot.Bcl.Core)
    // - _WaitIgnoreCancel: catches TaskCanceledException, OperationCanceledException, JSDisconnectedException
    // - _WaitIgnoreCancelOrNull: nullable wrapper around _WaitIgnoreCancel
    private static readonly string[] SafeWrapperMethodNames =
    {
        "_WaitIgnoreCancel",
        "_WaitIgnoreCancelOrNull"
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

        // Only analyze DisposeAsync methods
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

        // Only analyze Blazor components - check containing type inherits from ComponentBase or implements IComponent
        var containingType = methodSymbol.ContainingType;
        if (containingType == null || !IsBlazorComponent(containingType))
            return;

        // Find all invocation expressions in this method
        var invocations = methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            // Check if this is a JS interop call
            if (!IsJsInteropCall(context, invocation))
                continue;

            // Check if this call is inside a try-catch that catches JSDisconnectedException
            if (IsInsideJsDisconnectedExceptionHandler(invocation))
                continue;

            // Check if this call is wrapped with a safe wrapper method (e.g., ._SafeWait())
            if (IsWrappedWithSafeMethod(invocation))
                continue;

            // Report diagnostic
            var callText = GetCallText(invocation);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                callText));
        }
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

    private static bool IsInsideJsDisconnectedExceptionHandler(InvocationExpressionSyntax invocation)
    {
        // Walk up the syntax tree to find a try-catch block
        var current = invocation.Parent;
        while (current != null)
        {
            if (current is TryStatementSyntax tryStatement)
            {
                // Check if any catch clause catches JSDisconnectedException
                foreach (var catchClause in tryStatement.Catches)
                {
                    if (catchClause.Declaration == null)
                    {
                        // Bare catch {} catches everything
                        return true;
                    }

                    var exceptionType = catchClause.Declaration.Type;
                    if (exceptionType == null)
                        continue;

                    var typeName = exceptionType.ToString();

                    // Check for JSDisconnectedException or Exception (catches all)
                    if (typeName.Contains("JSDisconnectedException") ||
                        typeName == "Exception" ||
                        typeName == "System.Exception")
                    {
                        // Verify the invocation is actually inside the try block, not the catch/finally
                        if (IsNodeInsideTryBlock(invocation, tryStatement))
                            return true;
                    }
                }
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsNodeInsideTryBlock(SyntaxNode node, TryStatementSyntax tryStatement)
    {
        // Check if the node is inside the try block (not catch or finally)
        var tryBlock = tryStatement.Block;
        return tryBlock.Span.Contains(node.Span);
    }

    private static bool IsWrappedWithSafeMethod(InvocationExpressionSyntax invocation)
    {
        // Check if this invocation is the target of a safe wrapper call
        // Pattern 1: invocation._SafeWait() where invocation is the JS interop call
        // Pattern 2: (invocation)._WaitIgnoreCancelOrNull() with parentheses for null-conditional
        // Pattern 3: (_obj?.invocation)._WaitIgnoreCancelOrNull() with null-conditional access
        // Syntax tree can have: ConditionalAccessExpression -> ParenthesizedExpression -> MemberAccess -> Invocation

        // Skip through wrapping syntax nodes to find the member access for the safe wrapper
        SyntaxNode? current = invocation.Parent;
        while (current is ParenthesizedExpressionSyntax or ConditionalAccessExpressionSyntax)
        {
            current = current.Parent;
        }

        if (current is not MemberAccessExpressionSyntax memberAccess)
            return false;

        // Check if the member being accessed is a safe wrapper method
        var methodName = memberAccess.Name.Identifier.Text;
        if (!SafeWrapperMethodNames.Contains(methodName))
            return false;

        // Verify this member access is actually being invoked (i.e., there's a () after it)
        // The member access should be the expression of another invocation
        return memberAccess.Parent is InvocationExpressionSyntax;
    }

    private static string GetCallText(InvocationExpressionSyntax invocation)
    {
        // Get a readable representation of the call
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            // For _module.InvokeVoidAsync(...) return "_module.InvokeVoidAsync"
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

    private static bool IsBlazorComponent(INamedTypeSymbol typeSymbol)
    {
        // Check inheritance chain for ComponentBase
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

        // Check if implements IComponent
        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IComponent" &&
            i.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }
}
