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

namespace NotNot.Analyzers.Reliability.Concurrency;

/// <summary>
/// Code fix provider for TaskResultNotObservedAnalyzer (NN_R002)
/// Provides automatic fixes for unobserved Task&lt;T&gt; results
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TaskResultNotObservedCodeFixProvider)), Shared]
public class TaskResultNotObservedCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Gets the diagnostic IDs that this code fix provider can fix
    /// </summary>
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TaskResultNotObservedAnalyzer.DiagnosticId);

    /// <summary>
    /// Gets the fix all provider for batch fixes
    /// </summary>
    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <summary>
    /// Registers code fixes for the given context
    /// </summary>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.FirstOrDefault(d => FixableDiagnosticIds.Contains(d.Id));
        if (diagnostic == null) return;

        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var awaitExpression = root.FindNode(diagnosticSpan) as AwaitExpressionSyntax;
        if (awaitExpression == null) return;

        // Find the expression statement containing the await
        var expressionStatement = awaitExpression.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (expressionStatement == null) return;

        // Register code fix for assigning to variable
        var assignAction = CodeAction.Create(
            title: "Assign result to variable",
            createChangedDocument: c => AssignToVariableAsync(context.Document, expressionStatement, awaitExpression, c),
            equivalenceKey: "AssignToVariable");

        context.RegisterCodeFix(assignAction, diagnostic);

        // Register code fix for assigning to discard
        var discardAction = CodeAction.Create(
            title: "Assign result to discard '_'",
            createChangedDocument: c => AssignToDiscardAsync(context.Document, expressionStatement, awaitExpression, c),
            equivalenceKey: "AssignToDiscard");

        context.RegisterCodeFix(discardAction, diagnostic);

        // Register code fix for using in return statement
        var returnAction = CodeAction.Create(
            title: "Return the result",
            createChangedDocument: c => ReturnResultAsync(context.Document, expressionStatement, awaitExpression, c),
            equivalenceKey: "ReturnResult");

        context.RegisterCodeFix(returnAction, diagnostic);
    }

    /// <summary>
    /// Assigns the await result to a variable
    /// </summary>
    private static async Task<Document> AssignToVariableAsync(Document document, ExpressionStatementSyntax expressionStatement, AwaitExpressionSyntax awaitExpression, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Create a variable declaration for the result
        var variableName = "result";
        var variableDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var"))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(variableName))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(awaitExpression))));

        var declarationStatement = SyntaxFactory.LocalDeclarationStatement(variableDeclaration)
            .WithTriviaFrom(expressionStatement);

        var newRoot = root.ReplaceNode(expressionStatement, declarationStatement);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Assigns the await result to the discard variable '_'
    /// </summary>
    private static async Task<Document> AssignToDiscardAsync(Document document, ExpressionStatementSyntax expressionStatement, AwaitExpressionSyntax awaitExpression, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var discardAssignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("_"),
            awaitExpression);

        var newExpressionStatement = expressionStatement.WithExpression(discardAssignment);
        var newRoot = root.ReplaceNode(expressionStatement, newExpressionStatement);

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Converts the expression statement to a return statement
    /// </summary>
    private static async Task<Document> ReturnResultAsync(Document document, ExpressionStatementSyntax expressionStatement, AwaitExpressionSyntax awaitExpression, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var returnStatement = SyntaxFactory.ReturnStatement(awaitExpression)
            .WithTriviaFrom(expressionStatement);

        var newRoot = root.ReplaceNode(expressionStatement, returnStatement);
        return document.WithSyntaxRoot(newRoot);
    }
}