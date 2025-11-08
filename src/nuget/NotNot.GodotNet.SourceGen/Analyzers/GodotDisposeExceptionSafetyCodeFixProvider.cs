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

namespace NotNot.GodotNet.SourceGen.Analyzers;

/// <summary>
/// Code fix provider that wraps Dispose cleanup code in try/catch blocks
/// to prevent unhandled exceptions from crashing Godot editor.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GodotDisposeExceptionSafetyCodeFixProvider)), Shared]
public class GodotDisposeExceptionSafetyCodeFixProvider : CodeFixProvider
{
	/// <inheritdoc/>
	public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(GodotDisposeExceptionSafetyAnalyzer.DiagnosticId);

	/// <inheritdoc/>
	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	/// <inheritdoc/>
	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null) return;

		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;

		// Find the Dispose method
		var method = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
			.OfType<MethodDeclarationSyntax>()
			.FirstOrDefault();

		if (method == null || method.Body == null) return;

		// Check if method has disposing guard
		var hasDisposingGuard = HasDisposingGuard(method.Body);

		if (hasDisposingGuard)
		{
			// Wrap the if (disposing) block content in try/catch
			context.RegisterCodeFix(
				CodeAction.Create(
					title: "Wrap disposing block in try/catch",
					createChangedDocument: c => WrapDisposingBlockAsync(context.Document, method, c),
					equivalenceKey: "WrapDisposingBlock"),
				diagnostic);
		}
		else
		{
			// Add disposing guard and wrap in try/catch
			context.RegisterCodeFix(
				CodeAction.Create(
					title: "Add disposing guard and wrap in try/catch",
					createChangedDocument: c => AddDisposingGuardWithTryCatchAsync(context.Document, method, c),
					equivalenceKey: "AddDisposingGuardWithTryCatch"),
				diagnostic);
		}
	}

	/// <summary>
	/// Checks if method has if (disposing) guard.
	/// NOTE: This uses textual matching for simplicity in code fix context.
	/// The analyzer uses proper semantic analysis.
	/// </summary>
	private static bool HasDisposingGuard(BlockSyntax body)
	{
		return body.Statements.OfType<IfStatementSyntax>()
			.Any(ifStmt => ifStmt.Condition.ToString().Contains("disposing"));
	}

	/// <summary>
	/// Wraps the content of the if (disposing) block in try/catch
	/// </summary>
	private async Task<Document> WrapDisposingBlockAsync(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null || method.Body == null) return document;

		// Find the if (disposing) statement
		var disposingIf = method.Body.Statements.OfType<IfStatementSyntax>()
			.FirstOrDefault(ifStmt => ifStmt.Condition.ToString().Contains("disposing"));

		if (disposingIf == null) return document;

		// Get the if block content
		var ifBlock = disposingIf.Statement as BlockSyntax;
		if (ifBlock == null)
		{
			// Single statement - wrap it in a block first
			ifBlock = SyntaxFactory.Block(disposingIf.Statement);
		}

		// Create catch block using ParseStatement for simplicity
		var catchBlockCode = @"catch (Exception ex)
{
    _GD.ThrowError(ex);
}";

		var catchClause = SyntaxFactory.ParseCompilationUnit(catchBlockCode)
			.DescendantNodes()
			.OfType<CatchClauseSyntax>()
			.First();

		// Create try/catch block wrapping the if block content
		var tryBlock = SyntaxFactory.TryStatement()
			.WithBlock(SyntaxFactory.Block(ifBlock.Statements))
			.WithCatches(SyntaxFactory.SingletonList(catchClause))
			.WithAdditionalAnnotations(Formatter.Annotation);

		// Create new if block with try/catch
		var newIfBlock = SyntaxFactory.Block(tryBlock);

		// Replace the if statement
		var newDisposingIf = disposingIf.WithStatement(newIfBlock);

		var newRoot = root.ReplaceNode(disposingIf, newDisposingIf.WithTriviaFrom(disposingIf));
		return document.WithSyntaxRoot(newRoot);
	}

	/// <summary>
	/// Adds if (disposing) guard and wraps cleanup code in try/catch.
	/// PRESERVES ORIGINAL STATEMENT ORDER - only wraps the content that should go in the guard.
	/// </summary>
	private async Task<Document> AddDisposingGuardWithTryCatchAsync(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
		if (root == null || method.Body == null) return document;

		var statements = method.Body.Statements;

		// Find the base.Dispose call position (if any)
		int baseDisposeIndex = -1;
		for (int i = 0; i < statements.Count; i++)
		{
			if (IsBaseDisposeCall(statements[i]))
			{
				baseDisposeIndex = i;
				break;
			}
		}

		// Determine which statements to wrap in the disposing guard
		// Strategy: Wrap all statements AFTER base.Dispose() (if present), or all statements (if no base.Dispose())
		System.Collections.Generic.List<StatementSyntax> statementsToWrap;
		System.Collections.Generic.List<StatementSyntax> statementsBeforeGuard;

		if (baseDisposeIndex >= 0)
		{
			// Base.Dispose exists - keep statements before and including it as-is, wrap rest
			statementsBeforeGuard = statements.Take(baseDisposeIndex + 1).ToList();
			statementsToWrap = statements.Skip(baseDisposeIndex + 1).ToList();
		}
		else
		{
			// No base.Dispose - wrap all statements except final base.Dispose (if last statement is base.Dispose, keep it last)
			if (statements.Count > 0 && IsBaseDisposeCall(statements[statements.Count - 1]))
			{
				statementsBeforeGuard = new System.Collections.Generic.List<StatementSyntax>();
				statementsToWrap = statements.Take(statements.Count - 1).ToList();
				// Note: We'll add the final base.Dispose back at the end
			}
			else
			{
				statementsBeforeGuard = new System.Collections.Generic.List<StatementSyntax>();
				statementsToWrap = statements.ToList();
			}
		}

		// If nothing to wrap, return unchanged
		if (statementsToWrap.Count == 0) return document;

		// Create catch block
		var catchBlockCode = @"catch (Exception ex)
{
    _GD.ThrowError(ex);
}";

		var catchClause = SyntaxFactory.ParseCompilationUnit(catchBlockCode)
			.DescendantNodes()
			.OfType<CatchClauseSyntax>()
			.First();

		// Create try/catch block
		var tryBlock = SyntaxFactory.TryStatement()
			.WithBlock(SyntaxFactory.Block(statementsToWrap))
			.WithCatches(SyntaxFactory.SingletonList(catchClause))
			.WithAdditionalAnnotations(Formatter.Annotation);

		// Create if (disposing) block
		var disposingIf = SyntaxFactory.IfStatement(
			SyntaxFactory.IdentifierName("disposing"),
			SyntaxFactory.Block(tryBlock));

		// Build new method body preserving original order
		var newStatements = new System.Collections.Generic.List<StatementSyntax>();
		newStatements.AddRange(statementsBeforeGuard);
		newStatements.Add(disposingIf);

		// Add back final base.Dispose if it wasn't already included
		if (baseDisposeIndex < 0 && statements.Count > 0 && IsBaseDisposeCall(statements[statements.Count - 1]))
		{
			newStatements.Add(statements[statements.Count - 1]);
		}

		var newBody = method.Body.WithStatements(SyntaxFactory.List(newStatements));
		var newMethod = method.WithBody(newBody);

		var newRoot = root.ReplaceNode(method, newMethod);
		return document.WithSyntaxRoot(newRoot);
	}

	/// <summary>
	/// Checks if statement is a base.Dispose call
	/// </summary>
	private static bool IsBaseDisposeCall(StatementSyntax statement)
	{
		if (statement is ExpressionStatementSyntax expr)
		{
			if (expr.Expression is InvocationExpressionSyntax invocation)
			{
				if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
				{
					return memberAccess.Expression is BaseExpressionSyntax &&
							 memberAccess.Name.Identifier.Text == "Dispose";
				}
			}
		}
		return false;
	}
}
