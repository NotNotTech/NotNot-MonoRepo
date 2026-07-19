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
/// Code fix provider that removes r_ prefix from non-ref variables.
/// Uses Roslyn Renamer API for safe, scope-aware renaming of all variable references.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RefPrefixMustBeRefCodeFixProvider)), Shared]
public class RefPrefixMustBeRefCodeFixProvider : CodeFixProvider
{
	public sealed override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(RefPrefixMustBeRefAnalyzer.DiagnosticId);

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
				title: "Remove 'r_' prefix from non-ref variable",
				createChangedSolution: c => RemoveRefPrefixAsync(context.Document, token, c),
				equivalenceKey: "RemoveRefVarPrefix"),
			diagnostic);
	}

	private async Task<Solution> RemoveRefPrefixAsync(Document document, SyntaxToken identifierToken, CancellationToken cancellationToken)
	{
		try
		{
			// Get semantic model
			var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
			if (semanticModel == null)
			{
				System.Diagnostics.Debug.WriteLine($"NN_C002 CodeFix: Semantic model unavailable for '{identifierToken.ValueText}'");
				return document.Project.Solution;
			}

			// Get symbol for the identifier
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
					symbol = semanticModel.GetDeclaredSymbol(foreachStmt, cancellationToken);
				}
				else if (currentNode is ParameterSyntax parameter)
				{
					symbol = semanticModel.GetDeclaredSymbol(parameter, cancellationToken);
				}
				else if (currentNode is FieldDeclarationSyntax fieldDecl)
				{
					// Field declarations contain multiple declarators, find the right one
					var declaratorWithToken = fieldDecl.Declaration.Variables
						.FirstOrDefault(v => v.Identifier.Span == identifierToken.Span);
					if (declaratorWithToken != null)
					{
						symbol = semanticModel.GetDeclaredSymbol(declaratorWithToken, cancellationToken);
					}
				}

				if (symbol != null) break;
				currentNode = currentNode.Parent;
			}

			if (symbol == null)
			{
				System.Diagnostics.Debug.WriteLine($"NN_C002 CodeFix: Symbol not found for '{identifierToken.ValueText}' (parent: {identifierToken.Parent?.GetType().Name})");
				return document.Project.Solution;
			}

			// Compute new name by removing r_ prefix
			var oldName = symbol.Name;
			if (!oldName.StartsWith("r_", StringComparison.Ordinal))
			{
				// Shouldn't happen, but safety check
				return document.Project.Solution;
			}

			var newName = oldName.Substring(2); // Remove "r_"

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
			System.Diagnostics.Debug.WriteLine($"NN_C002 CodeFix: Renamer failed for '{identifierToken.ValueText}': {ex.Message}");
			return document.Project.Solution;
		}
	}
}
