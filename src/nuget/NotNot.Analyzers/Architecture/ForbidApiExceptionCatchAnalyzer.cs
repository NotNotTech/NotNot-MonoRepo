using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Analyzer that warns against catching <c>Refit.ApiException</c> for business outcomes.
/// Mutation interfaces should return <c>Task&lt;IApiResponse&gt;</c> for structured non-throwing
/// error handling. Catching ApiException is acceptable only in infrastructure retry/logging code.
/// </summary>
/// <remarks>
/// Severity is Warning (not Error) because some legitimate infrastructure uses exist.
///
/// FLAGGED (warning):
/// - catch (ApiException) { ... }
/// - catch (ApiException ex) { ... }
///
/// ALLOWED:
/// - catch (HttpRequestException) { ... } - different exception type
/// - catch (Exception) { ... } - not specifically ApiException (handled by other analyzers)
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ForbidApiExceptionCatchAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_A004";

    private static readonly LocalizableString Title = "Avoid catching ApiException for business outcomes";

    private static readonly LocalizableString MessageFormat =
        "Avoid catching ApiException for business outcomes. Mutation interfaces should return " +
        "Task<IApiResponse> for structured non-throwing error handling. " +
        "Catch ApiException only in infrastructure retry/logging code.";

    private static readonly LocalizableString Description =
        "Catching Refit.ApiException to determine business outcomes (success/failure of mutations) " +
        "is an anti-pattern. Instead, use IApiResponse-based return types that provide structured " +
        "error information without exceptions. Reserve ApiException catches for infrastructure " +
        "concerns like retry logic and centralized logging.";

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
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeCatchClause");

        if (context.Node is not CatchClauseSyntax catchClause) return;

        // Bare catch without a declaration has no typed exception
        if (catchClause.Declaration == null) return;

        var caughtType = catchClause.Declaration.Type;
        var typeInfo = context.SemanticModel.GetTypeInfo(caughtType);
        var typeSymbol = typeInfo.Type;

        // Fallback: GetSymbolInfo for cases where GetTypeInfo returns null
        if (typeSymbol == null)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(caughtType);
            typeSymbol = symbolInfo.Symbol as ITypeSymbol
                ?? (symbolInfo.CandidateSymbols.Length > 0 ? symbolInfo.CandidateSymbols[0] as ITypeSymbol : null);
        }

        if (typeSymbol == null) return;

        // Check if the caught type is or derives from Refit.ApiException
        if (IsOrDerivesFromApiException(typeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                catchClause.CatchKeyword.GetLocation()));
        }
    }

    /// <summary>
    /// Checks if the type symbol is <c>Refit.ApiException</c> or derives from it.
    /// </summary>
    private static bool IsOrDerivesFromApiException(ITypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current != null)
        {
            if (current.Name == "ApiException" &&
                current.ContainingNamespace?.ToDisplayString() == "Refit")
            {
                return true;
            }
            current = current.BaseType;
        }

        return false;
    }
}
