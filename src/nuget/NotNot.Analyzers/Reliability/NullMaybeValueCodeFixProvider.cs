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

        // Register code action to replace with Maybe.SuccessResult()
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with Maybe.SuccessResult()",
                createChangedDocument: c => ReplaceWithSuccessResultAsync(context.Document, invocation, c),
                equivalenceKey: "UseSuccessResult"),
            diagnostic);
    }

    private async Task<Document> ReplaceWithSuccessResultAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Create Maybe.SuccessResult() call
        var newInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("Maybe"),
                SyntaxFactory.IdentifierName("SuccessResult")));

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}