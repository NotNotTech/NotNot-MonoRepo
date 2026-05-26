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
/// - catch (IOException ex) { _logger.LogDebug(ex, "..."); } - logging
/// - catch (Exception ex) { throw; } - rethrow
/// - catch (IOException) { return fallback; } - explicit control flow
///
/// CONCURRENCY NOTE:
/// Pre-checks (File.Exists, dict.ContainsKey) have TOCTOU races in concurrent
/// scenarios. Atomic operations (FileMode.CreateNew, ConcurrentDictionary.TryAdd,
/// DB unique constraints) that throw on conflict are the correct concurrent pattern.
/// The catch block is legitimate — but it still must not be empty. At minimum use
/// __.DebugAssertOnce(ex) or logging to maintain observability.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EmptyCatchBlockAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NN_R006";

    private static readonly LocalizableString Title = "Empty catch block silently swallows exception";
    private static readonly LocalizableString MessageFormat =
        "Empty catch block silently swallows '{0}'. "
        + "Prefer: (1) avoid exceptions for control flow — but pre-checks have TOCTOU races in concurrent code; "
        + "atomic ops (FileMode.CreateNew, TryAdd) may need try/catch, "
        + "(2) __.DebugAssertOnce(ex) for unexpected, "
        + "(3) logging for expected. Even race-condition catches need handling, not empty bodies.";
    private static readonly LocalizableString Description =
        "Catch blocks with zero statements silently swallow exceptions, hiding bugs and failures. "
        + "Options: (1) Restructure to avoid exceptions for control flow — but note that pre-checks "
        + "(File.Exists, dict.ContainsKey) have TOCTOU races in concurrent scenarios; atomic operations "
        + "(FileMode.CreateNew, ConcurrentDictionary.TryAdd, DB unique constraints) legitimately use "
        + "try/catch for race handling, (2) Add __.DebugAssertOnce(ex) for unexpected exceptions with "
        + "graceful degradation, (3) Add logging for expected-but-notable exceptions (e.g., "
        + "_logger.LogDebug(ex, \"race-condition conflict\") for atomic-op catches), "
        + "(4) Suppress with #pragma only if the empty catch is truly the intended behavior. "
        + "Even legitimate race-condition catches must not be empty — observability requires at minimum "
        + "a DebugAssert or log statement.";
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
