using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Code fix provider that automatically adds r_ prefix to non-compliant ref var declarations.
/// Uses Roslyn Renamer API for safe, scope-aware renaming of all variable references.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RefVarNamingCodeFixProvider)), Shared]
public class RefVarNamingCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(RefVarNamingAnalyzer.DiagnosticId);

	public sealed override FixAllProvider GetFixAllProvider() =>
		WellKnownFixAllProviders.BatchFixer;

	public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root == null) return;

		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;

		// Find the identifier token at the diagnostic location
		var token = root.FindToken(diagnosticSpan.Start);
		if (token.Parent == null) return;

		// Register the code fix
		context.RegisterCodeFix(
			CodeAction.Create(
				title: "Add 'r_' prefix to ref variable",
				createChangedSolution: c => AddRefPrefixAsync(context.Document, token, c),
				equivalenceKey: "AddRefVarPrefix"),
			diagnostic);
	}

	private async Task<Solution> AddRefPrefixAsync(Document document, SyntaxToken identifierToken, CancellationToken cancellationToken)
	{
		try
		{
			// Get semantic model
			var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
			if (semanticModel == null)
			{
				System.Diagnostics.Debug.WriteLine($"NN_C001 CodeFix: Semantic model unavailable for '{identifierToken.ValueText}'");
				return document.Project.Solution;
			}

			// Get symbol for the identifier
			// The context varies: VariableDeclaratorSyntax, ForEachStatementSyntax, etc.
			ISymbol? symbol = null;

			// Walk up the tree to find a node we can get a symbol from
			var currentNode = identifierToken.Parent;
			while (currentNode != null && symbol == null)
			{
				if (currentNode is VariableDeclaratorSyntax declarator)
				{
					symbol = semanticModel.GetDeclaredSymbol(declarator, cancellationToken);
				}
				else if (currentNode is ForEachStatementSyntax foreachStmt)
				{
					// For foreach, GetDeclaredSymbol on the foreach statement itself
					symbol = semanticModel.GetDeclaredSymbol(foreachStmt, cancellationToken);
				}
				else if (currentNode is SingleVariableDesignationSyntax designation)
				{
					symbol = semanticModel.GetDeclaredSymbol(designation, cancellationToken);
				}

				if (symbol != null) break;
				currentNode = currentNode.Parent;
			}

			if (symbol == null)
			{
				System.Diagnostics.Debug.WriteLine($"NN_C001 CodeFix: Symbol not found for '{identifierToken.ValueText}' (parent: {identifierToken.Parent?.GetType().Name})");
				return document.Project.Solution;
			}

			// Compute new name
			var oldName = symbol.Name;
			var newName = "r_" + oldName;

			// Use Renamer API - handles collision detection and all reference updates automatically
			var solution = document.Project.Solution;

			var newSolution = await Renamer.RenameSymbolAsync(
				solution,
				symbol,
				new SymbolRenameOptions(
					RenameOverloads: false,
					RenameInStrings: false,
					RenameInComments: false,
					RenameFile: false),
				newName,
				cancellationToken).ConfigureAwait(false);

			return newSolution;
		}
		catch (Exception ex)
		{
			// Renamer may fail in edge cases (incomplete code, generated code, etc.)
			System.Diagnostics.Debug.WriteLine($"NN_C001 CodeFix: Renamer failed for '{identifierToken.ValueText}': {ex.Message}");
			return document.Project.Solution;
		}
	}
}
