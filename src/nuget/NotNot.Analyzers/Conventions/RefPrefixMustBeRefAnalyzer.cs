using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Analyzer that enforces variables with r_ prefix must be declared with ref.
/// Prevents accidental misuse of the r_ naming convention on non-ref variables.
/// Complements NN_C001 (ref must use r_) for bidirectional enforcement.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RefPrefixMustBeRefAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NN_C002";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: "Variables with r_ prefix must be declared with ref",
		messageFormat: "Variable '{0}' uses the r_ prefix but is not declared with 'ref'. Either declare it as 'ref var {0}' or rename without the r_ prefix.",
		category: "Naming",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "The r_ prefix is reserved for ref variables to indicate reference semantics. Using this prefix on non-ref variables creates confusion about mutation behavior.",
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

		// Check local variable declarations
		context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
		// Check foreach variables
		context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
		// Check parameters
		context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
		// Check fields
		context.RegisterSyntaxNodeAction(AnalyzeField, SyntaxKind.FieldDeclaration);
	}

	private void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeLocalDeclaration");

		var localDecl = (LocalDeclarationStatementSyntax)context.Node;
		var type = localDecl.Declaration.Type;

		// If this IS a ref var, skip - NN_C001 handles it
		if (type is RefTypeSyntax)
			return;

		// Check each variable declarator for r_ prefix
		foreach (var declarator in localDecl.Declaration.Variables)
		{
			CheckNonRefVariable(context, declarator.Identifier);
		}
	}

	private void AnalyzeForEach(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeForEach");

		var forEach = (ForEachStatementSyntax)context.Node;

		// If this IS a ref foreach, skip - NN_C001 handles it
		if (forEach.Type is RefTypeSyntax)
			return;

		// Check the foreach variable identifier
		CheckNonRefVariable(context, forEach.Identifier);
	}

	private void AnalyzeParameter(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeParameter");

		var parameter = (ParameterSyntax)context.Node;

		// If this IS a ref parameter, skip
		if (parameter.Modifiers.Any(SyntaxKind.RefKeyword) ||
		    parameter.Modifiers.Any(SyntaxKind.OutKeyword) ||
		    parameter.Modifiers.Any(SyntaxKind.InKeyword))
			return;

		// Check parameter name
		if (parameter.Identifier.ValueText.StartsWith("r_", StringComparison.Ordinal))
		{
			var diagnostic = Diagnostic.Create(
				Rule,
				parameter.Identifier.GetLocation(),
				parameter.Identifier.ValueText);
			context.ReportDiagnostic(diagnostic);
		}
	}

	private void AnalyzeField(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeField");

		var fieldDecl = (FieldDeclarationSyntax)context.Node;

		// Fields can't be ref (except ref fields in ref structs, which is a special case)
		// Just check if any field uses r_ prefix
		foreach (var variable in fieldDecl.Declaration.Variables)
		{
			CheckNonRefVariable(context, variable.Identifier);
		}
	}

	/// <summary>
	/// Checks if a non-ref variable identifier incorrectly uses the r_ prefix.
	/// </summary>
	/// <param name="context">Analysis context.</param>
	/// <param name="identifier">Variable identifier token.</param>
	private void CheckNonRefVariable(SyntaxNodeAnalysisContext context, SyntaxToken identifier)
	{
		var varName = identifier.ValueText;

		// Check if name starts with r_ prefix
		if (varName.StartsWith("r_", StringComparison.Ordinal))
		{
			var diagnostic = Diagnostic.Create(
				Rule,
				identifier.GetLocation(),
				varName);
			context.ReportDiagnostic(diagnostic);
		}
	}
}
