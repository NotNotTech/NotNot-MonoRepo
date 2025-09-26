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

namespace NotNot.GodotNet.SourceGen.Analyzers;

/// <summary>
/// Code fix provider that adds null-conditional operators to unprotected
/// member variable accesses in _ExitTree() methods.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GodotExitTreeNullCheckCodeFixProvider)), Shared]
public class GodotExitTreeNullCheckCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(GodotExitTreeNullCheckAnalyzer.DiagnosticId);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the member access expression
        var memberAccess = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault();

        if (memberAccess == null) return;

        // Register code fix
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add null-conditional operator",
                createChangedDocument: c => AddNullConditionalOperatorAsync(context.Document, memberAccess, c),
                equivalenceKey: "AddNullConditionalOperator"),
            diagnostic);

        // If the member access is part of a method call, also offer to wrap in null check
        if (memberAccess.Parent is InvocationExpressionSyntax)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Wrap in null check",
                    createChangedDocument: c => WrapInNullCheckAsync(context.Document, memberAccess, c),
                    equivalenceKey: "WrapInNullCheck"),
                diagnostic);
        }
    }

    /// <summary>
    /// Adds null-conditional operator to the member access
    /// </summary>
    private async Task<Document> AddNullConditionalOperatorAsync(Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        SyntaxNode newExpression;

        // Check if this is already part of a longer chain
        var parent = memberAccess.Parent;
        if (parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Expression == memberAccess)
        {
            // This is part of a chain like _field.Property.Method()
            // We need to add ?. at the beginning of the chain
            newExpression = CreateConditionalAccess(memberAccess);
        }
        else if (parent is InvocationExpressionSyntax invocation && invocation.Expression == memberAccess)
        {
            // This is a method call like _field.Method()
            // Transform to _field?.Method()
            var conditionalAccess = SyntaxFactory.ConditionalAccessExpression(
                memberAccess.Expression,
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberBindingExpression(memberAccess.Name),
                    invocation.ArgumentList));
            newExpression = conditionalAccess;
        }
        else
        {
            // Simple property/field access
            newExpression = CreateConditionalAccess(memberAccess);
        }

        // Replace the expression in the tree
        var nodeToReplace = parent is InvocationExpressionSyntax inv && inv.Expression == memberAccess ? parent : memberAccess;
        var newRoot = root.ReplaceNode(nodeToReplace, newExpression.WithTriviaFrom(nodeToReplace));
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Creates a conditional access expression from a member access expression
    /// </summary>
    private static ConditionalAccessExpressionSyntax CreateConditionalAccess(MemberAccessExpressionSyntax memberAccess)
    {
        return SyntaxFactory.ConditionalAccessExpression(
            memberAccess.Expression,
            SyntaxFactory.MemberBindingExpression(memberAccess.Name));
    }

    /// <summary>
    /// Wraps the member access in an if statement that checks for null
    /// </summary>
    private async Task<Document> WrapInNullCheckAsync(Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Find the statement containing this member access
        var statement = memberAccess.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null) return document;

        // Get the member name for the null check
        var memberName = GetMemberIdentifier(memberAccess);
        if (memberName == null) return document;

        // Create the if statement
        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.NotEqualsExpression,
            memberName,
            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));

        var ifStatement = SyntaxFactory.IfStatement(
            condition,
            SyntaxFactory.Block(statement));

        // Replace the original statement with the if statement
        var newRoot = root.ReplaceNode(statement, ifStatement.WithTriviaFrom(statement));
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Gets the identifier expression for the member being accessed
    /// </summary>
    private static ExpressionSyntax? GetMemberIdentifier(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            ThisExpressionSyntax thisExpression => memberAccess.Expression,
            _ => null
        };
    }
}