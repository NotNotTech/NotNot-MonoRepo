using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability;

/// <summary>
/// Analyzer that ensures proper exception handling in async endpoint methods
/// by suggesting the use of _ToMaybe() extension methods.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ToMaybeExceptionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_R004";

    private static readonly LocalizableString Title = "Use _ToMaybe() for exception handling";
    private static readonly LocalizableString MessageFormat = "Method '{0}' returns Task<{1}> without exception handling - consider using ._ToMaybe()";
    private static readonly LocalizableString Description = "Async methods returning non-Maybe types should use _ToMaybe() extension for proper exception handling.";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,  // Start with Info severity for gradual adoption
        isEnabledByDefault: true,
        description: Description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeMethod");

        if (context.Node is not MethodDeclarationSyntax method) return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (symbol == null) return;

        // Only analyze public methods in controller classes
        if (symbol.DeclaredAccessibility != Accessibility.Public) return;

        var containingType = symbol.ContainingType;
        if (containingType == null || !IsController(containingType)) return;

        // Check if method returns Task<T> where T is not Maybe
        if (!ShouldAnalyzeReturnType(symbol.ReturnType)) return;

        // Look for unprotected async operations in the method body
        if (method.Body == null && method.ExpressionBody == null) return;

        // Check if method has any _ToMaybe() calls
        var hasToMaybeCall = false;
        if (method.Body != null)
        {
            hasToMaybeCall = method.Body.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => IsToMaybeCall(inv));
        }
        else if (method.ExpressionBody != null)
        {
            hasToMaybeCall = method.ExpressionBody.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => IsToMaybeCall(inv));
        }

        if (!hasToMaybeCall)
        {
            var returnTypeName = GetSimpleReturnTypeName(symbol.ReturnType);
            var diagnosticLocation = method.ReturnType?.GetLocation() ?? method.Identifier.GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                diagnosticLocation,
                symbol.Name,
                returnTypeName));
        }
    }

    /// <summary>
    /// Determines if a type is an ASP.NET Core controller.
    /// </summary>
    private static bool IsController(INamedTypeSymbol type)
    {
        // Check for [ApiController] attribute
        if (type.GetAttributes().Any(a => a.AttributeClass?.Name == "ApiControllerAttribute"))
            return true;

        // Check if inherits from Controller or ControllerBase
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "Controller" || baseType.Name == "ControllerBase")
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Determines if a return type should be analyzed for _ToMaybe usage.
    /// </summary>
    private static bool ShouldAnalyzeReturnType(ITypeSymbol returnType)
    {
        // We want Task<T> or ValueTask<T> where T is not Maybe
        if (returnType is INamedTypeSymbol namedType)
        {
            if ((namedType.Name == "Task" || namedType.Name == "ValueTask") &&
                namedType.TypeArguments.Length == 1)
            {
                var innerType = namedType.TypeArguments[0];
                var innerTypeName = innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                // Skip if already returns Maybe
                if (innerTypeName == "Maybe" || innerTypeName.StartsWith("Maybe<"))
                    return false;

                // Analyze non-Maybe Task returns
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a simple display name for the return type.
    /// </summary>
    private static string GetSimpleReturnTypeName(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
        {
            return namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }
        return returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    /// <summary>
    /// Checks if an invocation is a call to _ToMaybe().
    /// </summary>
    private static bool IsToMaybeCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == "_ToMaybe";
        }
        return false;
    }
}