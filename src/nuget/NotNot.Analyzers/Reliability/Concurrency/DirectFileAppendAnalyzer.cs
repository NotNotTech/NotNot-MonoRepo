using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability.Concurrency;

/// <summary>
/// Flags a direct <c>System.IO.File</c> APPEND convenience call — <c>File.AppendAllText</c>,
/// <c>File.AppendAllLines</c>, or their async <c>AppendAllTextAsync</c>/<c>AppendAllLinesAsync</c>
/// variants — to a final path, invoked OUTSIDE <c>NotNot.Storage.AtomicFileWriter</c>. These APIs open
/// the file <c>FileShare.Read</c>, so a SECOND OS process appending the SAME file (e.g. a shared growing
/// log) throws <c>IOException "being used by another process"</c> (Win32 <c>ERROR_SHARING_VIOLATION</c>).
/// An in-process <c>lock</c> cannot arbitrate a cross-process collision — the loser must retry.
/// </summary>
/// <remarks>
/// Complementary to NN_R007 (hand-rolled atomic full-file REWRITE via deterministic temp + overwrite
/// rename) on a disjoint axis: NN_R007 covers temp+rename, NN_R008 covers direct append (no temp, no
/// Move). Together they form a gap-free file-I/O concurrency matrix; <c>NotNot.Storage.AtomicFileWriter</c>
/// is silent under both. NN_R008 does NOT cover direct full-writes (<c>File.WriteAllText</c>) — mostly
/// legitimate single-process, intentionally scoped out to avoid noise.
///
/// FLAGGED (error) — direct append to a final path:
/// <code>
/// File.AppendAllText(finalPath, line);          // ← fires here — no cross-process retry
/// </code>
///
/// ALLOWED (silent):
/// - <c>NotNot.Storage.AtomicFileWriter.AppendWithRetry(finalPath, contents)</c> — the sanctioned helper
///   (bounded transient-lock retry). Not a <c>System.IO.File</c> member, so never matched.
/// - A <c>File.AppendAllText</c> INSIDE <c>NotNot.Storage.AtomicFileWriter</c> itself — its
///   <c>AppendWithRetry</c> legitimately calls <c>File.AppendAllText</c> internally (the sanctioned impl);
///   exempted by containing-type.
/// - <c>File.WriteAllText(finalPath, ...)</c> — a direct full-file write, not an append (out of scope).
/// - <c>File.Move(temp, final, overwrite: true)</c> — the NN_R007 temp-then-rename domain, not append.
///
/// DETECTION (perf-conscious): registers on <c>InvocationExpression</c>; a cheap SYNTACTIC member-name
/// prefilter runs FIRST, and SemanticModel is used ONLY after it — to confirm the invoked method's
/// containing type is <c>System.IO.File</c> (covers <c>File.</c>, <c>IO.File.</c>, fully-qualified, or an
/// aliased import) and to walk the enclosing symbol for the containing-type exemption.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DirectFileAppendAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NN_R008";

    private static readonly LocalizableString Title = "Direct file append has no cross-process retry";

    private static readonly LocalizableString MessageFormat =
        "Direct File.{0} to a final path has no cross-process retry — a second process appending the "
        + "same file throws IOException 'being used by another process'. "
        + "Fix: (1) NotNot.Storage.AtomicFileWriter.AppendWithRetry(path, contents) — bounded "
        + "transient-lock retry; "
        + "(2) AtomicFileWriter.WriteAtomic for a full-file rewrite; "
        + "(3) #pragma warning disable NN_R008 only if this append is provably single-process for the "
        + "file's lifetime.";

    private static readonly LocalizableString Description =
        "A direct System.IO.File append (AppendAllText/AppendAllLines and their async variants) opens "
        + "the file with the default FileShare.Read: on Windows a SECOND OS process appending the SAME "
        + "file throws IOException 'being used by another process' (ERROR_SHARING_VIOLATION). An "
        + "in-process lock cannot arbitrate a cross-process collision — each append is whole per call "
        + "(FileShare.Read excludes concurrent writers), so the losing process must RETRY the transient "
        + "lock. "
        + "Preferred fix: (1) use NotNot.Storage.AtomicFileWriter.AppendWithRetry, which reuses the "
        + "AtomicFileWriter transient-lock retry policy (IsTransient / BackoffMs / MaxAttempts + jitter, "
        + "rethrow-on-exhaustion); "
        + "(2) if the intent is a full-file rewrite rather than an append, use "
        + "NotNot.Storage.AtomicFileWriter.WriteAtomic; "
        + "(3) suppress with #pragma warning disable NN_R008 only if this append is provably "
        + "single-process for the file's lifetime. See the NN_R008 ReadMe section for the full rationale.";

    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
        customTags: new[] { "Concurrency", "Reliability", "FileIO" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>The direct-append convenience APIs on <c>System.IO.File</c> this rule flags (sync + async).</summary>
    private static readonly ImmutableHashSet<string> AppendApiNames = ImmutableHashSet.Create(
        "AppendAllText", "AppendAllLines", "AppendAllTextAsync", "AppendAllLinesAsync");

    /// <summary>The single sanctioned type whose internal <c>File.Append*</c> calls are exempt.</summary>
    private const string ExemptContainingType = "NotNot.Storage.AtomicFileWriter";

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

        // Cheap SYNTACTIC prefilter FIRST: `<receiver>.AppendAll*(...)`. Bail before touching the
        // SemanticModel unless the member name is one of the flagged append APIs.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        var apiName = memberAccess.Name.Identifier.ValueText;
        if (!AppendApiNames.Contains(apiName)) return;

        // SemanticModel: confirm the invoked method's containing type is System.IO.File (covers
        // `File.`, `IO.File.`, fully-qualified, or an aliased import — the syntactic member name alone
        // is insufficient).
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol appendMethod) return;
        if (appendMethod.ContainingType?.ToDisplayString() != "System.IO.File") return;

        // Exemption: skip if the ENCLOSING type of the invocation is NotNot.Storage.AtomicFileWriter —
        // its AppendWithRetry legitimately calls File.AppendAllText internally (the sanctioned impl).
        if (IsInsideExemptType(context.ContainingSymbol)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            apiName));
    }

    /// <summary>
    /// Walks up from the invocation's containing symbol to its enclosing <see cref="INamedTypeSymbol"/>
    /// and returns true iff that type's full display name is <c>NotNot.Storage.AtomicFileWriter</c>.
    /// </summary>
    private static bool IsInsideExemptType(ISymbol? containingSymbol)
    {
        for (var symbol = containingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (symbol is INamedTypeSymbol namedType)
            {
                return namedType.ToDisplayString() == ExemptContainingType;
            }
        }

        return false;
    }
}
