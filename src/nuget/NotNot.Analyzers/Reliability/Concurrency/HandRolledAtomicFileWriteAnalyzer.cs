using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability.Concurrency;

/// <summary>
/// Flags a hand-rolled atomic file write — a write to a DETERMINISTIC <c>.tmp</c> path followed by
/// <c>File.Move(temp, final, overwrite: true)</c> — that lacks the unique-temp + per-path-serialization
/// concurrency guard. This idiom is NOT concurrency-safe: two writers racing on the SAME deterministic
/// <c>.tmp</c> name collide (on Windows the second open of the shared temp throws
/// <c>ERROR_SHARING_VIOLATION</c>), and two writers racing an overwrite-rename to ONE destination are
/// not mutually safe (a <c>MoveFileEx</c>/<c>MOVEFILE_REPLACE_EXISTING</c> loser can throw).
/// </summary>
/// <remarks>
/// Complementary to NN_R001/NN_R002 (Task-concurrency) and NN_R005/NN_R006 (catch-reliability):
/// NN_R007 is the file-I/O concurrency-reliability rule. No existing rule covers hand-rolled atomic
/// file write.
///
/// FLAGGED (error) — deterministic temp + overwrite-rename:
/// <code>
/// var tempPath = filePath + ".tmp";          // deterministic concat ending in ".tmp"
/// File.WriteAllText(tempPath, json);
/// File.Move(tempPath, filePath, overwrite: true);   // ← fires here
/// </code>
///
/// ALLOWED (silent):
/// - <c>NotNot.Storage.AtomicFileWriter.WriteAtomic(...)</c> — the sanctioned helper (its temp is
///   minted via a method call, never a literal-bearing deterministic concat, so it is not matched).
/// - Unique temp: <c>var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";</c> — a
///   Guid/Random/Ticks/GetRandomFileName/GetTempFileName component makes the temp unique, not
///   deterministic; the precision case that keeps AtomicFileWriter's own pattern silent.
/// - <c>File.Move(a, b)</c> — no <c>overwrite: true</c>, not the temp-then-rename idiom.
/// - <c>File.WriteAllText(finalPath, json)</c> — a direct final write, no temp + Move (out of scope).
///
/// DETECTION (perf-conscious): registers on <c>InvocationExpression</c>; SemanticModel is used ONLY to
/// confirm the receiver type is <c>System.IO.File</c> and to resolve the temp arg to its local symbol.
/// The deterministic-vs-unique decision stays SYNTACTIC (walk the initializer concat tree for a
/// uniqueness identifier and a trailing <c>".tmp"</c> literal).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HandRolledAtomicFileWriteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NN_R007";

    private static readonly LocalizableString Title = "Hand-rolled atomic file write is not concurrency-safe";

    private static readonly LocalizableString MessageFormat =
        "Hand-rolled atomic write via deterministic temp '{0}' + rename is not concurrency-safe "
        + "(concurrent writers collide on the shared temp; racing overwrite-renames to one path are unsafe). "
        + "Fix: (1) use NotNot.Storage.AtomicFileWriter.WriteAtomic/WriteAtomicAsync "
        + "(unique temp + per-path lock + bounded retry); "
        + "(2) if AtomicFileWriter is unavailable here, use a UNIQUE temp (finalPath + Guid + \".tmp\") "
        + "AND serialize the write+rename per final path — unique-name alone is insufficient; "
        + "(3) #pragma warning disable NN_R007 only if this write is provably single-threaded AND "
        + "single-process for the file's lifetime.";

    private static readonly LocalizableString Description =
        "A write to a DETERMINISTIC temp path (a string concat ending in a \".tmp\" literal with no "
        + "Guid/Random/Ticks/GetRandomFileName component) followed by File.Move(temp, final, overwrite: true) "
        + "is a hand-rolled atomic write missing the concurrency guard. Two correctness mechanisms are BOTH "
        + "required — removing either re-opens a race: "
        + "(1) a UNIQUE temp name per writer (on Windows the default FileShare.Read on a SHARED .tmp open "
        + "makes the second same-path writer throw ERROR_SHARING_VIOLATION); and "
        + "(2) per-final-path serialization of the entire write+rename (concurrent "
        + "MoveFileEx/MOVEFILE_REPLACE_EXISTING calls racing ONE destination are not mutually safe — the "
        + "losing rename throws). "
        + "Preferred fix: (1) use NotNot.Storage.AtomicFileWriter.WriteAtomic/WriteAtomicAsync, which "
        + "supplies a unique temp, a per-path lock, and a bounded external-lock retry; "
        + "(2) if AtomicFileWriter is unavailable, use a unique temp (finalPath + Guid + \".tmp\") AND a "
        + "per-final-path lock — the unique temp and the per-path lock are BOTH required, removing either "
        + "re-opens a race; "
        + "(3) suppress with #pragma warning disable NN_R007 only if the write is provably single-threaded "
        + "AND single-process for the file's lifetime. See the NN_R007 ReadMe section for the full rationale.";

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

    /// <summary>Identifiers whose presence anywhere in the temp-path concat tree marks it UNIQUE (⇒ silent).</summary>
    private static readonly string[] UniquenessMarkers =
    {
        "Guid", "Random", "Ticks", "GetRandomFileName", "GetTempFileName",
    };

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

        // Match `<receiver>.Move(...)` with at least 3 arguments (src, dest, overwrite).
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.ValueText != "Move") return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 3) return;

        // SemanticModel: confirm the receiver is System.IO.File (covers `File.Move`, `IO.File.Move`,
        // fully-qualified, or an aliased import — the syntactic member name alone is insufficient).
        var receiverSymbol = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
        if (receiverSymbol is not IMethodSymbol moveMethod) return;
        if (moveMethod.ContainingType?.ToDisplayString() != "System.IO.File") return;

        // 3rd arg (overwrite) must be the literal `true` — positional or `overwrite:`-named.
        if (!IsOverwriteTrue(args)) return;

        // arg0 = the temp source. Resolve it to a deterministic-.tmp source; FIRE only if so.
        var tempArg = args[0].Expression;
        if (!TryGetDeterministicTempText(tempArg, context, out var tempText)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            tempText));
    }

    /// <summary>
    /// True iff the overwrite argument resolves to the literal <c>true</c>. Honors the named form
    /// <c>overwrite: true</c> in ANY position, else falls back to the positional 3rd argument.
    /// </summary>
    private static bool IsOverwriteTrue(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var named = args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "overwrite");
        var overwriteExpr = named?.Expression ?? args[2].Expression;
        return overwriteExpr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.TrueLiteralExpression);
    }

    /// <summary>
    /// Resolves the temp-source argument to a DETERMINISTIC <c>.tmp</c> path and yields its text.
    /// Accepts two shapes:
    /// (1) a simple local identifier whose single-method local declaration initializer is a deterministic
    ///     concat (SemanticModel resolves the local symbol; the deterministic check stays syntactic), or
    /// (2) an inline deterministic concat expression passed directly as arg0.
    /// A method-call source (e.g. <c>MakeTempPath(x)</c>) is NOT matched — that is how AtomicFileWriter
    /// stays silent. A concat containing a uniqueness marker (Guid/Random/Ticks/...) is NOT matched.
    /// </summary>
    private static bool TryGetDeterministicTempText(
        ExpressionSyntax tempArg,
        SyntaxNodeAnalysisContext context,
        out string tempText)
    {
        tempText = "";

        // Shape (2): inline concat passed directly — `File.Move(path + ".tmp", final, true)`.
        if (tempArg is BinaryExpressionSyntax)
        {
            if (!IsDeterministicTmpConcat(tempArg)) return false;
            tempText = tempArg.ToString();
            return true;
        }

        // Shape (1): a simple local identifier — resolve to its declaration initializer.
        if (tempArg is not IdentifierNameSyntax identifier) return false;

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        if (symbol is not ILocalSymbol local) return false;

        var declRef = local.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef?.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax declarator) return false;
        if (declarator.Initializer?.Value is not { } initializer) return false;

        if (!IsDeterministicTmpConcat(initializer)) return false;

        tempText = identifier.Identifier.ValueText;
        return true;
    }

    /// <summary>
    /// SYNTACTIC test: <paramref name="expr"/> is a string concat ('+' tree) that ENDS in a
    /// <c>".tmp"</c> string literal AND contains NO uniqueness marker
    /// (Guid/Random/Ticks/GetRandomFileName/GetTempFileName) anywhere in the tree ⇒ DETERMINISTIC.
    /// </summary>
    private static bool IsDeterministicTmpConcat(ExpressionSyntax expr)
    {
        if (expr is not BinaryExpressionSyntax concat || !concat.IsKind(SyntaxKind.AddExpression)) return false;

        // Rightmost operand of the '+' tree must be the literal ".tmp".
        if (concat.Right is not LiteralExpressionSyntax rightLit
            || !rightLit.IsKind(SyntaxKind.StringLiteralExpression)
            || rightLit.Token.ValueText != ".tmp")
        {
            return false;
        }

        // No uniqueness identifier anywhere in the concat tree.
        foreach (var node in concat.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IdentifierNameSyntax id when UniquenessMarkers.Contains(id.Identifier.ValueText):
                    return false;
                case GenericNameSyntax gen when UniquenessMarkers.Contains(gen.Identifier.ValueText):
                    return false;
            }
        }

        return true;
    }
}
