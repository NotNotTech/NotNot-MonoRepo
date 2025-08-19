using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability;

/// <summary>
/// Analyzer that prevents passing null to Maybe.Success<T>() as a preventive measure
/// to avoid potential null reference issues in Maybe monad usage.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NullMaybeValueAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_R003";

    private static readonly LocalizableString Title = "Do not pass null to Maybe.Success";
    private static readonly LocalizableString MessageFormat = "Passing null to Maybe.Success<{0}>() is not allowed - use Maybe.SuccessResult() for null scenarios";
    private static readonly LocalizableString Description = "Maybe.Success should not be called with null values as this defeats the purpose of the Maybe pattern for null safety.";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeInvocation");
        
        if (context.Node is not InvocationExpressionSyntax invocation) return;

        // Check if this is a call to Maybe.Success or Maybe<T>.Success
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text != "Success")
        {
            return;
        }

        // Get the symbol for the method being called
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) return;

        // Check if this is Maybe.Success or Maybe<T>.Success
        var containingType = methodSymbol.ContainingType;
        if (containingType == null) return;

        var typeName = containingType.Name;
        if (typeName != "Maybe") return;

        // Check if we're passing null
        if (invocation.ArgumentList.Arguments.Count != 1) return;
        
        var argument = invocation.ArgumentList.Arguments[0];
        var argumentExpression = argument.Expression;

        // Check for null literal
        if (argumentExpression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NullLiteralExpression))
        {
            ReportDiagnostic(context, argument, containingType);
            return;
        }

        // Check for default literal (could be null for reference types)
        if (argumentExpression is LiteralExpressionSyntax defaultLiteral &&
            defaultLiteral.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            // Check if the type argument is a reference type
            if (containingType.TypeArguments.Length > 0)
            {
                var typeArg = containingType.TypeArguments[0];
                if (typeArg.IsReferenceType)
                {
                    ReportDiagnostic(context, argument, containingType);
                    return;
                }
            }
        }

        // Check for default(T) where T is reference type
        if (argumentExpression is DefaultExpressionSyntax defaultExpression)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(defaultExpression);
            if (typeInfo.Type?.IsReferenceType == true)
            {
                ReportDiagnostic(context, argument, containingType);
                return;
            }
        }

        // Check for constant null values from variables/fields
        var constantValue = context.SemanticModel.GetConstantValue(argumentExpression);
        if (constantValue.HasValue && constantValue.Value == null)
        {
            ReportDiagnostic(context, argument, containingType);
        }
    }

    private static void ReportDiagnostic(
        SyntaxNodeAnalysisContext context,
        ArgumentSyntax argument,
        INamedTypeSymbol containingType)
    {
        var typeArgument = containingType.TypeArguments.Length > 0 
            ? containingType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : "T";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            argument.GetLocation(),
            typeArgument));
    }
}