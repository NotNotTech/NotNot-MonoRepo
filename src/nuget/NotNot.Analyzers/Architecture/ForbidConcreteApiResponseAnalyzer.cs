using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Analyzer that forbids use of the concrete <c>Refit.ApiResponse&lt;T&gt;</c> class.
/// Code should use <c>IApiResponse&lt;T&gt;</c> (interface) for transport returns, or
/// <c>Maybe&lt;T&gt;</c> for domain/app layer code. The concrete ApiResponse requires
/// an HttpResponseMessage and cannot be constructed in the Server DI path.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ForbidConcreteApiResponseAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_A003";

    private static readonly LocalizableString Title = "Do not use concrete ApiResponse<T>";

    private static readonly LocalizableString MessageFormat =
        "Use IApiResponse<T> (interface) for transport returns, or Maybe<T> for domain/app layer code. " +
        "ApiResponse<T> requires HttpResponseMessage and cannot be constructed in Server DI path.";

    private static readonly LocalizableString Description =
        "The concrete Refit.ApiResponse<T> class requires an HttpResponseMessage for construction, " +
        "making it unusable in server-side DI paths. Use IApiResponse<T> for transport layer returns " +
        "or Maybe<T> for domain/application layer code.";

    private const string Category = "Architecture";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Check declarations that have explicit type syntax
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeMethodReturnType, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzePropertyType, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeVariableDeclaration");

        if (context.Node is not VariableDeclarationSyntax declaration) return;
        CheckTypeAndReport(context, declaration.Type);
    }

    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeParameter");

        if (context.Node is not ParameterSyntax parameter) return;
        if (parameter.Type == null) return;
        CheckTypeAndReport(context, parameter.Type);
    }

    private static void AnalyzeMethodReturnType(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeMethodReturnType");

        if (context.Node is not MethodDeclarationSyntax method) return;
        CheckTypeAndReport(context, method.ReturnType);
    }

    private static void AnalyzePropertyType(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzePropertyType");

        if (context.Node is not PropertyDeclarationSyntax property) return;
        CheckTypeAndReport(context, property.Type);
    }

    private static void CheckTypeAndReport(SyntaxNodeAnalysisContext context, TypeSyntax typeSyntax)
    {
        // Resolve the type symbol from the semantic model
        var typeInfo = context.SemanticModel.GetTypeInfo(typeSyntax);
        var typeSymbol = typeInfo.Type;

        if (typeSymbol == null) return;

        // Check if it's the concrete ApiResponse<T> directly
        if (IsConcreteApiResponse(typeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, typeSyntax.GetLocation()));
            return;
        }

        // Also check generic type arguments (e.g., Task<ApiResponse<T>>)
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                if (IsConcreteApiResponse(typeArg))
                {
                    // Find the syntax location of the type argument for precise reporting
                    if (typeSyntax is GenericNameSyntax genericSyntax)
                    {
                        var argSyntax = genericSyntax.TypeArgumentList.Arguments
                            .FirstOrDefault(a =>
                            {
                                var argTypeInfo = context.SemanticModel.GetTypeInfo(a);
                                return argTypeInfo.Type != null && IsConcreteApiResponse(argTypeInfo.Type);
                            });
                        if (argSyntax != null)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(Rule, argSyntax.GetLocation()));
                            return;
                        }
                    }
                    // Fallback: report on the whole type syntax
                    context.ReportDiagnostic(Diagnostic.Create(Rule, typeSyntax.GetLocation()));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Checks if the type symbol represents the concrete Refit.ApiResponse (not the IApiResponse interfaces).
    /// Uses Name + ContainingNamespace for robust matching across compilation environments.
    /// </summary>
    private static bool IsConcreteApiResponse(ITypeSymbol typeSymbol)
    {
        // Get the original definition for generic types to normalize
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            typeSymbol = namedType.OriginalDefinition;
        }

        // Match "ApiResponse" in "Refit" namespace — NOT "IApiResponse"
        return typeSymbol.Name == "ApiResponse"
            && typeSymbol.ContainingNamespace?.ToDisplayString() == "Refit";
    }
}
