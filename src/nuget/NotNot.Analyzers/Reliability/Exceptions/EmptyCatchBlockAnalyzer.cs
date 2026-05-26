using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability.Exceptions;

/// <summary>
/// Analyzer that flags catch blocks with zero statements in their body.
/// Empty catch blocks silently swallow exceptions regardless of exception type.
/// </summary>
/// <remarks>
/// Complementary to NN_R005 (CatchBlockMustRethrowAnalyzer) which only flags
/// general exception types. NN_R006 targets empty bodies on ANY exception type.
///
/// FLAGGED (error):
/// - catch { } - bare empty catch
/// - catch (IOException) { } - specific type, empty body
/// - catch (IOException) { // comment } - comments are trivia, not statements
/// - catch (Exception ex) when (cond) { } - filter doesn't justify empty body
///
/// ALLOWED (has statements):
/// - catch (IOException) { __.DebugAssertOnce(ex); } - debug assertion
/// - catch (IOException ex) { Log(ex); } - logging
/// - catch (Exception ex) { throw; } - rethrow
/// - catch (IOException) { return fallback; } - explicit control flow
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EmptyCatchBlockAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NN_R006";

    private static readonly LocalizableString Title = "Empty catch block silently swallows exception";
    private static readonly LocalizableString MessageFormat =
        "Empty catch block silently swallows '{0}'. "
        + "Prefer: (1) avoid exceptions for control flow, "
        + "(2) __.DebugAssertOnce(ex) for unexpected, "
        + "(3) logging for expected. Suppress only if truly needed.";
    private static readonly LocalizableString Description =
        "Catch blocks with zero statements silently swallow exceptions, hiding bugs and failures. "
        + "Options: (1) Restructure to avoid exceptions for control flow (e.g., check File.Exists() first), "
        + "(2) Add __.DebugAssertOnce(ex) for unexpected exceptions with graceful degradation, "
        + "(3) Add logging for expected-but-notable exceptions, "
        + "(4) Suppress with #pragma if the empty catch is truly the intended behavior.";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

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

        // Only flag catch blocks with no meaningful statements
        // Comments are Roslyn trivia, not statements, so comment-only blocks are empty
        // Standalone semicolons (EmptyStatementSyntax) are not meaningful statements
        if (catchClause.Block == null || catchClause.Block.Statements.Any(s => s is not EmptyStatementSyntax)) return;

        // Get display name for the caught type
        var displayName = catchClause.Declaration?.Type.ToString() ?? "(bare catch)";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            catchClause.CatchKeyword.GetLocation(),
            displayName));
    }
}
