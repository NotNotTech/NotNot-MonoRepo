using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Analyzer that detects <c>IApiResponse</c> or <c>IApiResponse&lt;T&gt;</c> results from
/// await expressions that are implicitly discarded (used as expression statements). The result
/// should be assigned and inspected, or explicitly discarded with <c>_ = await ...</c>.
/// </summary>
/// <remarks>
/// FLAGGED (warning):
/// - <c>await client.MutateAsync();</c> — result implicitly discarded
///
/// ALLOWED:
/// - <c>var result = await client.MutateAsync();</c> — assigned for inspection
/// - <c>_ = await client.MutateAsync();</c> — explicitly discarded (best-effort call)
/// - <c>await client.DoSomethingAsync();</c> — returns Task (not IApiResponse)
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class IgnoredApiResponseAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_A005";

    private static readonly LocalizableString Title = "IApiResponse result not inspected";

    private static readonly LocalizableString MessageFormat =
        "IApiResponse result from mutation call is not inspected. " +
        "Check IsSuccessStatusCode to handle business failures, " +
        "or use '_ = await ...' to explicitly discard for best-effort calls.";

    private static readonly LocalizableString Description =
        "When an await expression returns IApiResponse or IApiResponse<T>, the result must be " +
        "assigned and checked (e.g., IsSuccessStatusCode) to handle business failures. " +
        "If the call is intentionally best-effort, use '_ = await ...' to explicitly discard.";

    private const string Category = "Architecture";

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
        context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeExpressionStatement");

        if (context.Node is not ExpressionStatementSyntax expressionStatement) return;

        // Check if the expression is an await expression
        var awaitExpression = expressionStatement.Expression as AwaitExpressionSyntax;
        if (awaitExpression == null) return;

        // Get the type of the await expression (the type AFTER awaiting, i.e. the Task<T> unwrapped to T)
        var typeInfo = context.SemanticModel.GetTypeInfo(awaitExpression);
        var resultType = typeInfo.Type;

        if (resultType == null) return;

        // Check if the awaited result type implements IApiResponse
        if (ImplementsIApiResponse(resultType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                awaitExpression.GetLocation()));
        }
    }

    /// <summary>
    /// Checks if the type implements or is <c>Refit.IApiResponse</c> or <c>Refit.IApiResponse&lt;T&gt;</c>.
    /// </summary>
    private static bool ImplementsIApiResponse(ITypeSymbol typeSymbol)
    {
        // Check the type itself
        if (IsIApiResponseType(typeSymbol))
            return true;

        // Check all implemented interfaces
        return typeSymbol.AllInterfaces.Any(IsIApiResponseType);
    }

    /// <summary>
    /// Checks if a specific type symbol is <c>Refit.IApiResponse</c> or <c>Refit.IApiResponse&lt;T&gt;</c>.
    /// Uses Name + ContainingNamespace for robust matching across compilation environments.
    /// </summary>
    private static bool IsIApiResponseType(ITypeSymbol typeSymbol)
    {
        // Check for IApiResponse or IApiResponse<T> in the Refit namespace
        if (typeSymbol.Name == "IApiResponse" &&
            typeSymbol.ContainingNamespace?.ToDisplayString() == "Refit")
        {
            return true;
        }

        return false;
    }
}
