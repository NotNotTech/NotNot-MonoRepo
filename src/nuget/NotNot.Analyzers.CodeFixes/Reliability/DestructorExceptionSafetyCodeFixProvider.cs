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
using Microsoft.CodeAnalysis.Formatting;

namespace NotNot.Analyzers.Reliability;

/// <summary>
/// Code fix provider that wraps destructor logic in try/catch blocks
/// to prevent unhandled exceptions from crashing the application.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DestructorExceptionSafetyCodeFixProvider)), Shared]
public class DestructorExceptionSafetyCodeFixProvider : CodeFixProvider
{
	/// <inheritdoc/>
	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DestructorExceptionSafetyAnalyzer.DiagnosticId);

	/// <inheritdoc/>
	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	/// <inheritdoc/>
	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null) return;

		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;

		// Find the destructor at the diagnostic location
		var node = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().FirstOrDefault();
		if (node == null) return;

		var destructor = node.AncestorsAndSelf().OfType<DestructorDeclarationSyntax>().FirstOrDefault();
		if (destructor == null) return;

		// Handle both block-bodied and expression-bodied destructors
		if (destructor.Body != null)
		{
			context.RegisterCodeFix(
				CodeAction.Create(
					title: "Wrap destructor body in try/catch",
					createChangedDocument: c => WrapDestructorBodyAsync(context.Document, destructor, c),
					equivalenceKey: "WrapDestructorBody"),
				diagnostic);
		}
		else if (destructor.ExpressionBody != null)
		{
			context.RegisterCodeFix(
				CodeAction.Create(
					title: "Convert to block form and wrap in try/catch",
					createChangedDocument: c => ConvertExpressionBodyAndWrapAsync(context.Document, destructor, c),
					equivalenceKey: "ConvertExpressionBodyAndWrap"),
				diagnostic);
		}
	}

	/// <summary>
	/// Creates catch clause using SyntaxFactory for IDE reliability.
	/// Uses __RethrowUnlessAppShutdownOrRelease() to allow exceptions during normal execution
	/// but suppress them during app shutdown or release builds when cleanup is best-effort.
	/// </summary>
	private static CatchClauseSyntax CreateCatchClause()
	{
		// Build: catch (Exception ex) { ex.__RethrowUnlessAppShutdownOrRelease(); }
		return SyntaxFactory.CatchClause()
			.WithDeclaration(
				SyntaxFactory.CatchDeclaration(
					SyntaxFactory.IdentifierName("Exception"),
					SyntaxFactory.Identifier("ex")))
			.WithBlock(
				SyntaxFactory.Block(
					SyntaxFactory.ExpressionStatement(
						SyntaxFactory.InvocationExpression(
							SyntaxFactory.MemberAccessExpression(
								SyntaxKind.SimpleMemberAccessExpression,
								SyntaxFactory.IdentifierName("ex"),
								SyntaxFactory.IdentifierName("__RethrowUnlessAppShutdownOrRelease"))))));
	}

	/// <summary>
	/// Wraps the entire destructor body in try/catch.
	/// </summary>
	private async Task<Document> WrapDestructorBodyAsync(Document document, DestructorDeclarationSyntax destructor, CancellationToken cancellationToken)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null || destructor.Body == null) return document;

		var body = destructor.Body;

		// Create try/catch block wrapping the entire body
		var tryBlock = SyntaxFactory.TryStatement()
			.WithBlock(SyntaxFactory.Block(body.Statements))
			.WithCatches(SyntaxFactory.SingletonList(CreateCatchClause()))
			.WithAdditionalAnnotations(Formatter.Annotation);

		// Create new body with just the try/catch
		var newBody = body.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(tryBlock));

		// Replace the destructor
		var newDestructor = destructor.WithBody(newBody);

		var newRoot = root.ReplaceNode(destructor, newDestructor.WithTriviaFrom(destructor));
		return document.WithSyntaxRoot(newRoot);
	}

	/// <summary>
	/// Converts expression-bodied destructor to block form and wraps in try/catch.
	/// </summary>
	private async Task<Document> ConvertExpressionBodyAndWrapAsync(Document document, DestructorDeclarationSyntax destructor, CancellationToken cancellationToken)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null || destructor.ExpressionBody == null) return document;

		// Convert expression body to statement
		var expressionStatement = SyntaxFactory.ExpressionStatement(destructor.ExpressionBody.Expression);

		// Wrap in try/catch
		var tryBlock = SyntaxFactory.TryStatement()
			.WithBlock(SyntaxFactory.Block(expressionStatement))
			.WithCatches(SyntaxFactory.SingletonList(CreateCatchClause()))
			.WithAdditionalAnnotations(Formatter.Annotation);

		// Create block body with try/catch
		var newBody = SyntaxFactory.Block(tryBlock);

		// Replace expression body with block body
		var newDestructor = destructor
			.WithExpressionBody(null)
			.WithSemicolonToken(default)
			.WithBody(newBody);

		var newRoot = root.ReplaceNode(destructor, newDestructor.WithTriviaFrom(destructor));
		return document.WithSyntaxRoot(newRoot);
	}
}
