using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.GodotNet.SourceGen.Analyzers;

/// <summary>
/// Analyzer that prohibits usage of specific Godot APIs known to have bugs or cause confusion.
/// Currently prohibits Godot.MultiMesh.CustomAabb and Godot.MultiMeshInstance3D.CustomAabb
/// due to AABB corner vs center positioning bug.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GodotProhibitedApiAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "GODOT002";

	private static readonly LocalizableString Title = "Prohibited Godot API Usage";
	private static readonly LocalizableString MessageFormat = "API '{0}' is prohibited: {1}";
	private static readonly LocalizableString Description = "This analyzer prohibits usage of specific Godot APIs known to have bugs or cause confusion. See diagnostic message for alternatives.";
	private const string Category = "Reliability";

	private static readonly DiagnosticDescriptor Rule = new(
		 DiagnosticId,
		 Title,
		 MessageFormat,
		 Category,
		 DiagnosticSeverity.Error,
		 isEnabledByDefault: true,
		 description: Description);

	/// <summary>
	/// Dictionary of banned APIs with their fully-qualified names and prohibition reasons.
	/// Key: Full API name like "Godot.MultiMesh.CustomAabb"
	/// Value: Reason for prohibition and suggested alternatives
	/// </summary>
	private static readonly ImmutableDictionary<string, string> BannedApis = new Dictionary<string, string>
	{
		["Godot.MultiMesh.CustomAabb"] =
			"CustomAabb has a positioning bug where AABB corner (not center) is positioned at node center, causing incorrect culling. " +
			"Use MultiMesh.GenerateAabb() for automatic calculation, or manually adjust for corner offset if CustomAabb is required.",

		["Godot.MultiMeshInstance3D.CustomAabb"] =
			"CustomAabb has a positioning bug where AABB corner (not center) is positioned at node center, causing incorrect culling. " +
			"Use automatic AABB calculation or manually adjust for corner offset if CustomAabb is required."
	}.ToImmutableDictionary();

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		var memberAccess = (MemberAccessExpressionSyntax)context.Node;

		// Get the symbol for the member being accessed
		var symbol = context.SemanticModel.GetSymbolInfo(memberAccess).Symbol;

		// Only check properties (CustomAabb is a property)
		if (symbol is not IPropertySymbol property)
			return;

		// Build the full API name: "Godot.MultiMesh.CustomAabb"
		var fullApiName = property.ToDisplayString();

		// Check if this API is in the banned list
		if (BannedApis.TryGetValue(fullApiName, out var reason))
		{
			var diagnostic = Diagnostic.Create(
				Rule,
				memberAccess.GetLocation(),
				fullApiName,
				reason);

			context.ReportDiagnostic(diagnostic);
		}
	}
}
