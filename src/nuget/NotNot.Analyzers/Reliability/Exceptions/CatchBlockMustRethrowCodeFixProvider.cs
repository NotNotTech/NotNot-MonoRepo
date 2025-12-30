using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NotNot.Analyzers.Reliability.Exceptions;

/// <summary>
/// Code fix provider for NN_R005 that adds a throw statement to catch blocks.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CatchBlockMustRethrowCodeFixProvider)), Shared]
public class CatchBlockMustRethrowCodeFixProvider : CodeFixProvider
{
    private const string TitleAddThrow = "Add 'throw;' to rethrow exception";

    /// <inheritdoc/>
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(CatchBlockMustRethrowAnalyzer.DiagnosticId);

    /// <inheritdoc/>
    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the catch clause
        var catchKeyword = root.FindToken(diagnosticSpan.Start);
        var catchClause = catchKeyword.Parent?.FirstAncestorOrSelf<CatchClauseSyntax>();
        if (catchClause == null) return;

        // Register code fix to add throw;
        context.RegisterCodeFix(
            CodeAction.Create(
                title: TitleAddThrow,
                createChangedDocument: ct => AddThrowStatementAsync(context.Document, catchClause, ct),
                equivalenceKey: nameof(TitleAddThrow)),
            diagnostic);
    }

    private static async Task<Document> AddThrowStatementAsync(
        Document document,
        CatchClauseSyntax catchClause,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var block = catchClause.Block;
        if (block == null) return document;

        var existingStatements = block.Statements;

        // Determine indentation from existing statements or block
        var indentationTrivia = GetIndentationTrivia(block, existingStatements);

        // Create throw; statement with proper indentation
        var throwStatement = SyntaxFactory.ThrowStatement()
            .WithLeadingTrivia(indentationTrivia)
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        SyntaxList<StatementSyntax> newStatements;

        if (existingStatements.Any())
        {
            var lastStatement = existingStatements.Last();

            // Check if last statement is a control transfer that would make throw unreachable
            if (IsControlTransferStatement(lastStatement))
            {
                // Insert throw BEFORE the control transfer statement (replacing it)
                // The throw will handle the exception, making the return/break unnecessary
                var statementsWithoutLast = existingStatements.RemoveAt(existingStatements.Count - 1);
                newStatements = statementsWithoutLast.Add(throwStatement);
            }
            else
            {
                // Normal case: append throw at end
                newStatements = existingStatements.Add(throwStatement);
            }
        }
        else
        {
            // Empty catch block: just add throw
            newStatements = SyntaxFactory.SingletonList<StatementSyntax>(throwStatement);
        }

        var newBlock = block.WithStatements(newStatements);
        var newCatchClause = catchClause.WithBlock(newBlock);

        var newRoot = root.ReplaceNode(catchClause, newCatchClause);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Gets appropriate indentation trivia for a new statement in the block.
    /// </summary>
    private static SyntaxTriviaList GetIndentationTrivia(BlockSyntax block, SyntaxList<StatementSyntax> existingStatements)
    {
        if (existingStatements.Any())
        {
            // Copy whitespace-only trivia from existing statement (skip comments)
            var lastStatement = existingStatements.Last();
            var whitespaceTrivia = lastStatement.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
                .ToArray();

            if (whitespaceTrivia.Length > 0)
            {
                return SyntaxFactory.TriviaList(whitespaceTrivia);
            }
        }

        // Fall back to block indentation + one level
        var blockLeadingTrivia = block.OpenBraceToken.LeadingTrivia;
        var blockWhitespace = blockLeadingTrivia
            .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
            .LastOrDefault();

        if (blockWhitespace != default)
        {
            var indentation = blockWhitespace.ToString() + "    ";
            return SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indentation));
        }

        // Last resort: use tab
        return SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("\t"));
    }

    /// <summary>
    /// Determines if a statement is a control transfer that would make subsequent code unreachable.
    /// </summary>
    private static bool IsControlTransferStatement(StatementSyntax statement)
    {
        return statement is ReturnStatementSyntax
            or BreakStatementSyntax
            or ContinueStatementSyntax
            or ThrowStatementSyntax
            or GotoStatementSyntax;
    }
}
