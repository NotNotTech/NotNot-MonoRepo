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
/// Code fix provider for NullMaybeValueAnalyzer that suggests alternatives to passing null to Maybe.Success.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NullMaybeValueCodeFixProvider)), Shared]
public class NullMaybeValueCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(NullMaybeValueAnalyzer.DiagnosticId);

    /// <inheritdoc/>
    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the invocation expression
        var node = root.FindNode(diagnosticSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation == null) return;

        // Register code action to replace with an error indicating null value
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with error for null value",
                createChangedDocument: c => ReplaceWithErrorAsync(context.Document, invocation, c),
                equivalenceKey: "UseError"),
            diagnostic);
    }

    private async Task<Document> ReplaceWithErrorAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null) return document;

        // Get the type argument from the method
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

        if (methodSymbol == null) return document;

        // Determine the type argument and how to build the replacement
        string typeArg;
        bool isStaticMethod = false;

        // Check if this is a generic method (static like Maybe.Success<T>)
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            typeArg = methodSymbol.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            isStaticMethod = true;
        }
        // Check if this is an instance method (like Maybe<T>.Success)
        else if (methodSymbol.ContainingType?.TypeArguments.Length > 0)
        {
            typeArg = methodSymbol.ContainingType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            isStaticMethod = false;
        }
        else
        {
            typeArg = "T";
            isStaticMethod = false;
        }

        // Create new Problem { Title = "Null value not allowed" }
        var problemCreation = SyntaxFactory.ObjectCreationExpression(
            SyntaxFactory.IdentifierName("Problem"))
            .WithInitializer(
                SyntaxFactory.InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    SyntaxFactory.SeparatedList<ExpressionSyntax>(new[]
                    {
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName("Title"),
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal("Null value not allowed")))
                    })));

        // Build the replacement based on whether it's static or instance method
        InvocationExpressionSyntax newInvocation;

        if (isStaticMethod && invocation.Expression is MemberAccessExpressionSyntax originalAccess)
        {
            // For static methods, check if the original was Maybe.Success<T> format
            if (originalAccess.Expression is IdentifierNameSyntax maybeIdent && maybeIdent.Identifier.Text == "Maybe")
            {
                // Keep the static format but change to Maybe<T>.Error
                newInvocation = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.GenericName("Maybe")
                            .WithTypeArgumentList(
                                SyntaxFactory.TypeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                        SyntaxFactory.ParseTypeName(typeArg)))),
                        SyntaxFactory.IdentifierName("Error")))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(problemCreation))));
            }
            else
            {
                // Default to Maybe<T>.Error format
                newInvocation = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.GenericName("Maybe")
                            .WithTypeArgumentList(
                                SyntaxFactory.TypeArgumentList(
                                    SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                        SyntaxFactory.ParseTypeName(typeArg)))),
                        SyntaxFactory.IdentifierName("Error")))
                    .WithArgumentList(
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(problemCreation))));
            }
        }
        else
        {
            // For instance methods, use Maybe<T>.Error format
            newInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.GenericName("Maybe")
                        .WithTypeArgumentList(
                            SyntaxFactory.TypeArgumentList(
                                SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                    SyntaxFactory.ParseTypeName(typeArg)))),
                    SyntaxFactory.IdentifierName("Error")))
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(problemCreation))));
        }

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}