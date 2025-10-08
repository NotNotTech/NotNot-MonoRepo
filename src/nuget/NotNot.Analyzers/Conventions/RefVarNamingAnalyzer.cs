using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Analyzer that enforces r_ prefix naming convention for ref var declarations.
/// Ref variables directly mutate underlying data - the prefix makes this danger visible.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RefVarNamingAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NN_C001";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: "ref var declarations must use r_ prefix",
		messageFormat: "Variable '{0}' is declared with 'ref var' and must be prefixed with 'r_' to indicate reference semantics. Suggested: '{1}'. Ref variables directly mutate underlying data - the prefix makes this danger visible.",
		category: "Naming",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "Reference variables (ref var) provide direct memory access to underlying data. The r_ prefix makes this mutation danger immediately visible in code review and reduces cognitive load.",
		helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
		customTags: new[] { "Naming", "Conventions", "RefSemantics" }
	);

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

		context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
		context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
		context.RegisterSyntaxNodeAction(AnalyzeForEachVariable, SyntaxKind.ForEachVariableStatement);
	}

	private void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeLocalDeclaration");

		var localDecl = (LocalDeclarationStatementSyntax)context.Node;
		var type = localDecl.Declaration.Type;

		// Check if type is RefTypeSyntax wrapping 'var'
		// AST structure: ref var → RefTypeSyntax { Type = IdentifierNameSyntax("var") }
		// AST structure: ref readonly var → RefTypeSyntax { Type = RefTypeSyntax { ReadOnlyKeyword, Type = IdentifierNameSyntax("var") } }
		if (!IsRefVarType(type, out var hasReadonly))
			return;

		// Check each variable declarator
		foreach (var declarator in localDecl.Declaration.Variables)
		{
			CheckVariableNaming(context, declarator.Identifier);
		}
	}

	private void AnalyzeForEach(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeForEach");

		var forEach = (ForEachStatementSyntax)context.Node;

		// Check if type is ref var (ref is part of type syntax, not a separate keyword)
		// Same pattern as local declarations: RefTypeSyntax wrapping 'var'
		if (!IsRefVarType(forEach.Type, out var hasReadonly))
			return;

		// Check identifier name
		CheckVariableNaming(context, forEach.Identifier);
	}

	private void AnalyzeForEachVariable(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeForEachVariable");

		// Handles deconstruction: foreach (ref var (a, b) in collection)
		var forEach = (ForEachVariableStatementSyntax)context.Node;

		// NOTE: ForEachVariableStatement type checking is different
		// For now, skip this pattern as it's complex and may not be commonly used
		// TODO: Research if ref var deconstruction is valid C# syntax and implement if needed
		return;
	}

	/// <summary>
	/// Determines if a type syntax represents ref var (including ref readonly var).
	/// </summary>
	/// <param name="typeSyntax">The type syntax to check.</param>
	/// <param name="hasReadonlyModifier">True if ref readonly var pattern detected.</param>
	/// <returns>True if this is a ref var declaration.</returns>
	private bool IsRefVarType(TypeSyntax typeSyntax, out bool hasReadonlyModifier)
	{
		hasReadonlyModifier = false;

		if (typeSyntax is not RefTypeSyntax refType)
			return false;

		var innerType = refType.Type;

		// Pattern 1: ref var
		if (innerType is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "var")
		{
			return true;
		}

		// Pattern 2: ref readonly var (nested RefTypeSyntax)
		if (innerType is RefTypeSyntax innerRefType &&
			innerRefType.ReadOnlyKeyword.IsKind(SyntaxKind.ReadOnlyKeyword) &&
			innerRefType.Type is IdentifierNameSyntax innerIdentifier &&
			innerIdentifier.Identifier.ValueText == "var")
		{
			hasReadonlyModifier = true;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Checks if a variable identifier follows the r_ prefix naming convention.
	/// Reports diagnostic if non-compliant.
	/// </summary>
	/// <param name="context">Analysis context.</param>
	/// <param name="identifier">Variable identifier token.</param>
	private void CheckVariableNaming(SyntaxNodeAnalysisContext context, SyntaxToken identifier)
	{
		var varName = identifier.ValueText;

		// Check if name starts with lowercase "r_"
		if (!varName.StartsWith("r_", StringComparison.Ordinal))
		{
			var suggestedName = "r_" + varName;
			var diagnostic = Diagnostic.Create(
				Rule,
				identifier.GetLocation(),
				varName,
				suggestedName);
			context.ReportDiagnostic(diagnostic);
		}
	}
}
