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

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Code fix provider for DirectMaybeReturnAnalyzer that simplifies redundant Maybe reconstruction.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DirectMaybeReturnCodeFixProvider)), Shared]
public class DirectMaybeReturnCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DirectMaybeReturnAnalyzer.DiagnosticId);

    /// <inheritdoc/>
    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the if statement that triggered the diagnostic
        var ifStatement = root.FindNode(diagnosticSpan)
            .FirstAncestorOrSelf<IfStatementSyntax>();

        if (ifStatement == null) return;

        // Register a code action that will invoke the fix
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Return Maybe directly",
                createChangedDocument: c => SimplifyMaybeReturnAsync(context.Document, ifStatement, c),
                equivalenceKey: "SimplifyMaybeReturn"),
            diagnostic);
    }

    private async Task<Document> SimplifyMaybeReturnAsync(
        Document document,
        IfStatementSyntax ifStatement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        // Find the variable being checked (e.g., "result" in "if (!result.IsSuccess)")
        IdentifierNameSyntax? variableIdentifier = null;
        var condition = ifStatement.Condition;

        // Unwrap negation if present
        if (condition is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
        {
            condition = negation.Operand;
        }
        else if (condition is BinaryExpressionSyntax binaryExpr &&
                 binaryExpr.IsKind(SyntaxKind.EqualsExpression) &&
                 binaryExpr.Right is LiteralExpressionSyntax literal &&
                 literal.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            condition = binaryExpr.Left;
        }

        // Extract the variable identifier
        if (condition is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "IsSuccess" &&
            memberAccess.Expression is IdentifierNameSyntax identifier)
        {
            variableIdentifier = identifier;
        }

        if (variableIdentifier == null) return document;

        // Find the parent block
        var parentBlock = ifStatement.Parent as BlockSyntax;
        if (parentBlock == null) return document;

        var ifIndex = parentBlock.Statements.IndexOf(ifStatement);
        if (ifIndex < 0 || ifIndex >= parentBlock.Statements.Count - 1) return document;

        // The next statement should be the redundant return
        var nextStatement = parentBlock.Statements[ifIndex + 1];
        if (nextStatement is not ReturnStatementSyntax) return document;

        // Create the simplified return statement
        var simplifiedReturn = SyntaxFactory.ReturnStatement(
            SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            variableIdentifier.WithLeadingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        // Remove the if statement and the next return, replace with single return
        var newStatements = parentBlock.Statements
            .RemoveAt(ifIndex)  // Remove if statement
            .RemoveAt(ifIndex)  // Remove next return (now at same index)
            .Insert(ifIndex, simplifiedReturn);  // Insert simplified return

        var newBlock = parentBlock.WithStatements(newStatements);
        var newRoot = root.ReplaceNode(parentBlock, newBlock);

        return document.WithSyntaxRoot(newRoot);
    }
}