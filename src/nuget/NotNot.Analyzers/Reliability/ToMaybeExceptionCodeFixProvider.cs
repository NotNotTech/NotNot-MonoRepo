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

namespace NotNot.Analyzers.Reliability;

/// <summary>
/// Code fix provider for ToMaybeExceptionAnalyzer that adds _ToMaybe() to async operations.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ToMaybeExceptionCodeFixProvider)), Shared]
public class ToMaybeExceptionCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ToMaybeExceptionAnalyzer.DiagnosticId);

    /// <inheritdoc/>
    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the method declaration
        var method = root.FindNode(diagnosticSpan).FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null) return;

        // Register code action to add _ToMaybe() to return statements
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add _ToMaybe() for exception handling",
                createChangedDocument: c => AddToMaybeAsync(context.Document, method, c),
                equivalenceKey: "AddToMaybe"),
            diagnostic);
    }

    private async Task<Document> AddToMaybeAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        var newMethod = method;

        // Handle expression-bodied methods
        if (method.ExpressionBody != null)
        {
            var newExpression = WrapWithToMaybe(method.ExpressionBody.Expression);
            var newExpressionBody = method.ExpressionBody.WithExpression(newExpression);
            newMethod = method.WithExpressionBody(newExpressionBody);
        }
        // Handle block-bodied methods
        else if (method.Body != null)
        {
            var newBody = method.Body;
            var returnStatements = method.Body.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(r => r.Expression != null && 
                           !UsesToMaybe(r.Expression) && 
                           IsAsyncOperation(r.Expression, semanticModel))
                .ToList();

            foreach (var returnStmt in returnStatements)
            {
                var newExpression = WrapWithToMaybe(returnStmt.Expression!);
                var newReturnStmt = returnStmt.WithExpression(newExpression);
                newBody = newBody.ReplaceNode(
                    newBody.DescendantNodes().OfType<ReturnStatementSyntax>()
                        .First(r => r.Span == returnStmt.Span),
                    newReturnStmt);
            }

            if (newBody != method.Body)
            {
                newMethod = method.WithBody(newBody);
            }
        }

        if (newMethod != method)
        {
            var newRoot = root.ReplaceNode(method, newMethod);
            return document.WithSyntaxRoot(newRoot);
        }

        return document;
    }

    /// <summary>
    /// Wraps an expression with .ConfigureAwait(false)._ToMaybe()
    /// </summary>
    private static ExpressionSyntax WrapWithToMaybe(ExpressionSyntax expression)
    {
        // Check if it already has ConfigureAwait
        bool hasConfigureAwait = false;
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ConfigureAwait")
        {
            hasConfigureAwait = true;
        }

        ExpressionSyntax baseExpression = expression;

        // Add ConfigureAwait(false) if not present
        if (!hasConfigureAwait)
        {
            baseExpression = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    expression,
                    SyntaxFactory.IdentifierName("ConfigureAwait")))
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.FalseLiteralExpression)))));
        }

        // Add _ToMaybe()
        var result = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                baseExpression,
                SyntaxFactory.IdentifierName("_ToMaybe")));

        return result;
    }

    /// <summary>
    /// Checks if an expression already uses _ToMaybe().
    /// </summary>
    private static bool UsesToMaybe(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "_ToMaybe")
            {
                return true;
            }

            return UsesToMaybe(invocation.Expression);
        }

        if (expression is MemberAccessExpressionSyntax memberAccessExpr)
        {
            return UsesToMaybe(memberAccessExpr.Expression);
        }

        return false;
    }

    /// <summary>
    /// Determines if an expression is an async operation.
    /// </summary>
    private static bool IsAsyncOperation(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type == null) return false;

        var typeName = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        
        return typeName.StartsWith("Task<") || typeName == "Task" ||
               typeName.StartsWith("ValueTask<") || typeName == "ValueTask" ||
               typeName.StartsWith("ConfiguredTaskAwaitable");
    }
}