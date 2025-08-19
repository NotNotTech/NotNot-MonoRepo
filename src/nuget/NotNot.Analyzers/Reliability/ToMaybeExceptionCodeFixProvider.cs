using System.Collections.Generic;
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

        // Find the method declaration - diagnostic could be on return type or method identifier
        var node = root.FindNode(diagnosticSpan);
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>() ??
                     node.Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
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

        // First, update the return type to wrap it with Maybe<>
        var newReturnType = WrapReturnTypeWithMaybe(method.ReturnType);
        newMethod = method.WithReturnType(newReturnType);

        // Handle expression-bodied methods
        if (method.ExpressionBody != null)
        {
            var expression = method.ExpressionBody.Expression;
            // Check if this is an async expression that needs wrapping
            if (IsAsyncExpression(expression, semanticModel))
            {
                var newExpression = WrapExpressionWithToMaybe(expression, semanticModel);
                var newExpressionBody = method.ExpressionBody.WithExpression(newExpression);
                newMethod = newMethod.WithExpressionBody(newExpressionBody);
            }
            // For non-async expression bodies, leave them as-is
        }
        // Handle block-bodied methods
        else if (method.Body != null)
        {
            // Collect all return statements that need updating
            var replacements = new Dictionary<ReturnStatementSyntax, ReturnStatementSyntax>();

            // Find all return statements in the entire body (including nested ones)
            var returnStatements = method.Body.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(r => r.Expression != null && !UsesToMaybe(r.Expression))
                .ToList();

            foreach (var returnStmt in returnStatements)
            {
                var expression = returnStmt.Expression!;

                // Check if this is an async operation (await, Task.FromResult, etc)
                if (IsAsyncExpression(expression, semanticModel))
                {
                    var newExpression = WrapExpressionWithToMaybe(expression, semanticModel);
                    var newReturnStmt = returnStmt.WithExpression(newExpression);
                    replacements[returnStmt] = newReturnStmt;
                }
                // For non-async returns, leave them as-is (they'll be simple values that compile to Maybe)
            }

            // Apply all replacements at once
            if (replacements.Count > 0)
            {
                var newBody = method.Body.ReplaceNodes(
                    replacements.Keys,
                    (originalNode, rewrittenNode) => replacements[originalNode]);
                newMethod = newMethod.WithBody(newBody);
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
    /// Wraps a return type with Maybe<>
    /// </summary>
    private static TypeSyntax WrapReturnTypeWithMaybe(TypeSyntax returnType)
    {
        // Check if it's a generic type like Task<T>, ValueTask<T>, or ActionResult<T>
        if (returnType is GenericNameSyntax genericName)
        {
            if (genericName.Identifier.Text == "Task" && genericName.TypeArgumentList.Arguments.Count == 1)
            {
                var innerType = genericName.TypeArgumentList.Arguments[0];

                // Check if it's already wrapped with Maybe
                if (IsMaybeType(innerType))
                {
                    return returnType; // Already wrapped
                }

                // Wrap the inner type with Maybe<>
                var wrappedType = WrapTypeWithMaybe(innerType);

                // Return Task<NotNot.Maybe<T>>
                return SyntaxFactory.GenericName(genericName.Identifier)
                    .WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(wrappedType)));
            }
            else if (genericName.Identifier.Text == "ValueTask" && genericName.TypeArgumentList.Arguments.Count == 1)
            {
                var innerType = genericName.TypeArgumentList.Arguments[0];

                // Check if it's already wrapped with Maybe
                if (IsMaybeType(innerType))
                {
                    return returnType; // Already wrapped
                }

                // Wrap the inner type with Maybe<>
                var wrappedType = WrapTypeWithMaybe(innerType);

                // Return ValueTask<NotNot.Maybe<T>>
                return SyntaxFactory.GenericName(genericName.Identifier)
                    .WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(wrappedType)));
            }
        }
        // Handle Task (non-generic)
        else if (returnType is IdentifierNameSyntax identifier && identifier.Identifier.Text == "Task")
        {
            // Return Task<NotNot.Maybe>
            var maybeType = SyntaxFactory.QualifiedName(
                SyntaxFactory.IdentifierName("NotNot"),
                SyntaxFactory.IdentifierName("Maybe"));

            return SyntaxFactory.GenericName("Task")
                .WithTypeArgumentList(
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SingletonSeparatedList<TypeSyntax>(maybeType)));
        }

        return returnType;
    }

    /// <summary>
    /// Checks if a type is already a Maybe type
    /// </summary>
    private static bool IsMaybeType(TypeSyntax type)
    {
        if (type is GenericNameSyntax genericName && genericName.Identifier.Text == "Maybe")
            return true;

        if (type is QualifiedNameSyntax qualifiedName)
        {
            if (qualifiedName.Right is GenericNameSyntax rightGeneric && rightGeneric.Identifier.Text == "Maybe")
                return true;
            if (qualifiedName.Right is IdentifierNameSyntax rightIdent && rightIdent.Identifier.Text == "Maybe")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Wraps a type with NotNot.Maybe<>
    /// </summary>
    private static TypeSyntax WrapTypeWithMaybe(TypeSyntax innerType)
    {
        // For ActionResult<T>, wrap the whole type: Maybe<ActionResult<T>>
        // For other types, wrap with Maybe<T>
        var maybeType = SyntaxFactory.GenericName("Maybe")
            .WithTypeArgumentList(
                SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(innerType)));

        // Create NotNot.Maybe<T>
        return SyntaxFactory.QualifiedName(
            SyntaxFactory.IdentifierName("NotNot"),
            maybeType);
    }

    /// <summary>
    /// Wraps an expression with .ConfigureAwait(false)._ToMaybe() handling await expressions specially
    /// </summary>
    private static ExpressionSyntax WrapExpressionWithToMaybe(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // If it's an await expression, we need to handle it specially
        if (expression is AwaitExpressionSyntax awaitExpr)
        {
            // Get the task expression being awaited
            var taskExpression = awaitExpr.Expression;

            // Add _ToMaybe() and ConfigureAwait(false) to the task
            var wrappedTask = WrapTaskWithToMaybe(taskExpression, semanticModel);

            // Return the await of the wrapped expression
            return awaitExpr.WithExpression(wrappedTask);
        }
        else
        {
            // For non-await expressions (like direct task returns), wrap with ConfigureAwait and _ToMaybe
            return WrapTaskWithToMaybe(expression, semanticModel);
        }
    }

    /// <summary>
    /// Wraps a task expression with .ConfigureAwait(false)._ToMaybe()
    /// </summary>
    private static ExpressionSyntax WrapTaskWithToMaybe(ExpressionSyntax taskExpression, SemanticModel semanticModel)
    {
        // Check if it already has ConfigureAwait
        bool hasConfigureAwait = false;
        if (taskExpression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ConfigureAwait")
        {
            hasConfigureAwait = true;
        }

        ExpressionSyntax baseExpression = taskExpression;

        // Add ConfigureAwait(false) if not present
        if (!hasConfigureAwait)
        {
            baseExpression = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    taskExpression,
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

    /// <summary>
    /// Determines if an expression involves an async operation that should be wrapped with _ToMaybe.
    /// </summary>
    private static bool IsAsyncExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Check if it's an await expression
        if (expression is AwaitExpressionSyntax)
            return true;

        // Check if it's a Task-returning method call or property access
        var typeInfo = semanticModel.GetTypeInfo(expression);
        if (typeInfo.Type != null)
        {
            var typeName = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (typeName.StartsWith("Task<") || typeName == "Task" ||
                typeName.StartsWith("ValueTask<") || typeName == "ValueTask" ||
                typeName.StartsWith("ConfiguredTaskAwaitable"))
            {
                return true;
            }
        }

        // Check for Task.FromResult or similar
        if (expression is InvocationExpressionSyntax invocation)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                var returnTypeName = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (returnTypeName.StartsWith("Task<") || returnTypeName == "Task" ||
                    returnTypeName.StartsWith("ValueTask<") || returnTypeName == "ValueTask")
                {
                    return true;
                }
            }
        }

        return false;
    }
}