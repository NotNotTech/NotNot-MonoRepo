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
/// Code fix provider for TaskAwaitedOrReturnedAnalyzer (NN_R001)
/// Provides automatic fixes for unawaited tasks
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TaskAwaitedOrReturnedCodeFixProvider)), Shared]
public class TaskAwaitedOrReturnedCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Gets the diagnostic IDs that this code fix provider can fix
    /// </summary>
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(TaskAwaitedOrReturnedAnalyzer.DiagnosticId);

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
        var expression = root.FindNode(diagnosticSpan);

        // Find the expression statement containing the task call
        var expressionStatement = expression.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (expressionStatement == null) return;

        // Register code fix for adding await
        var awaitAction = CodeAction.Create(
            title: "Add 'await'",
            createChangedDocument: c => AddAwaitAsync(context.Document, expressionStatement, c),
            equivalenceKey: "AddAwait");

        context.RegisterCodeFix(awaitAction, diagnostic);

        // Register code fix for assigning to discard
        var discardAction = CodeAction.Create(
            title: "Assign to discard '_'",
            createChangedDocument: c => AssignToDiscardAsync(context.Document, expressionStatement, c),
            equivalenceKey: "AssignToDiscard");

        context.RegisterCodeFix(discardAction, diagnostic);

        // Register code fix for storing in variable
        var variableAction = CodeAction.Create(
            title: "Store in variable",
            createChangedDocument: c => StoreInVariableAsync(context.Document, expressionStatement, c),
            equivalenceKey: "StoreInVariable");

        context.RegisterCodeFix(variableAction, diagnostic);
    }

    /// <summary>
    /// Adds await to the task expression
    /// </summary>
    private static async Task<Document> AddAwaitAsync(Document document, ExpressionStatementSyntax expressionStatement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var expression = expressionStatement.Expression;
        var awaitExpression = SyntaxFactory.AwaitExpression(expression)
            .WithTriviaFrom(expression);

        var newExpressionStatement = expressionStatement.WithExpression(awaitExpression);
        var newRoot = root.ReplaceNode(expressionStatement, newExpressionStatement);

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Assigns the task expression to the discard variable '_'
    /// </summary>
    private static async Task<Document> AssignToDiscardAsync(Document document, ExpressionStatementSyntax expressionStatement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var expression = expressionStatement.Expression;
        var discardAssignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("_"),
            expression);

        var newExpressionStatement = expressionStatement.WithExpression(discardAssignment);
        var newRoot = root.ReplaceNode(expressionStatement, newExpressionStatement);

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Stores the task in a variable
    /// </summary>
    private static async Task<Document> StoreInVariableAsync(Document document, ExpressionStatementSyntax expressionStatement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var expression = expressionStatement.Expression;

        // Create a variable declaration for the task
        var variableName = "task";
        var variableDeclaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.IdentifierName("var"))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(variableName))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(expression))));

        var declarationStatement = SyntaxFactory.LocalDeclarationStatement(variableDeclaration)
            .WithTriviaFrom(expressionStatement);

        var newRoot = root.ReplaceNode(expressionStatement, declarationStatement);
        return document.WithSyntaxRoot(newRoot);
    }
}