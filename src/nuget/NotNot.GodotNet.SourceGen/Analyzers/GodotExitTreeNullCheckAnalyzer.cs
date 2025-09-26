using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.GodotNet.SourceGen.Analyzers;

/// <summary>
/// Analyzer that ensures member variables accessed in _ExitTree() overrides
/// use null-conditional operators or explicit null checks to prevent
/// NullReferenceExceptions when _ExitTree() is called on objects with
/// only default constructors invoked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GodotExitTreeNullCheckAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "GODOT001";

	private static readonly LocalizableString Title = "Member variables must be null-checked in _ExitTree";
	private static readonly LocalizableString MessageFormat = "In Editor, _ExitTree() may be called when disposing (cold-reload).  Member variable '{0}' in _ExitTree() must use null-conditional operator or null check";
	private static readonly LocalizableString Description = "_ExitTree()  may be called when disposing (cold-reload).  so can be called on objects with only default constructors invoked, so member variables may be null. Use null-conditional operators (?.) or explicit null checks to prevent NullReferenceExceptions.";
	private const string Category = "Reliability";

	private static readonly DiagnosticDescriptor Rule = new(
		 DiagnosticId,
		 Title,
		 MessageFormat,
		 Category,
		 DiagnosticSeverity.Error,
		 isEnabledByDefault: true,
		 description: Description);

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
	}

	private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MethodDeclarationSyntax method) return;

		// Check if this is _ExitTree override
		if (method.Identifier.Text != "_ExitTree") return;
		if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;

		var symbol = context.SemanticModel.GetDeclaredSymbol(method);
		if (symbol == null) return;

		// Check if containing type derives from Godot.Node
		if (!IsGodotNode(symbol.ContainingType)) return;

		// Analyze method body for member accesses
		if (method.Body == null && method.ExpressionBody == null) return;

		var nodes = method.Body?.DescendantNodes() ?? method.ExpressionBody?.DescendantNodes() ?? Enumerable.Empty<SyntaxNode>();

		// Check both member access expressions and invocations on identifiers
		foreach (var node in nodes)
		{
			ExpressionSyntax? memberExpression = null;
			Location? diagnosticLocation = null;

			switch (node)
			{
				case MemberAccessExpressionSyntax memberAccess:
					if (IsMemberVariableAccess(memberAccess, context.SemanticModel, symbol.ContainingType))
					{
						memberExpression = memberAccess;
						diagnosticLocation = memberAccess.GetLocation();
					}
					break;

				case InvocationExpressionSyntax invocation when invocation.Expression is IdentifierNameSyntax identifier:
					// Direct invocation on field like _field.Method()
					var invocationSymbol = context.SemanticModel.GetSymbolInfo(identifier).Symbol;
					if (invocationSymbol is IFieldSymbol field &&
						 SymbolEqualityComparer.Default.Equals(field.ContainingType, symbol.ContainingType) &&
						 !field.Type.IsValueType)
					{
						memberExpression = identifier;
						diagnosticLocation = invocation.GetLocation();
					}
					break;

				case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax memberAccess2:
					// Member access invocation like _field.Method()
					// Check if the object being accessed (not the method) is a field
					if (memberAccess2.Expression is IdentifierNameSyntax fieldIdentifier)
					{
						var fieldSymbol = context.SemanticModel.GetSymbolInfo(fieldIdentifier).Symbol;
						if (fieldSymbol is IFieldSymbol fieldSym &&
							 SymbolEqualityComparer.Default.Equals(fieldSym.ContainingType, symbol.ContainingType) &&
							 !fieldSym.Type.IsValueType)
						{
							memberExpression = fieldIdentifier;
							diagnosticLocation = invocation.GetLocation();
						}
					}
					else if (memberAccess2.Expression is ThisExpressionSyntax)
					{
						// Handle this.field.Method()
						var fieldSymbol = context.SemanticModel.GetSymbolInfo(memberAccess2).Symbol;
						if (fieldSymbol?.ContainingSymbol is IFieldSymbol fieldSym2 &&
							 SymbolEqualityComparer.Default.Equals(fieldSym2.ContainingType, symbol.ContainingType) &&
							 !fieldSym2.Type.IsValueType)
						{
							memberExpression = memberAccess2;
							diagnosticLocation = invocation.GetLocation();
						}
					}
					break;
			}

			if (memberExpression != null && diagnosticLocation != null && !IsProtectedExpression(memberExpression))
			{
				var memberName = GetMemberNameFromExpression(memberExpression);
				if (!string.IsNullOrEmpty(memberName))
				{
					context.ReportDiagnostic(Diagnostic.Create(
						 Rule,
						 diagnosticLocation,
						 memberName));
				}
			}
		}
	}

	/// <summary>
	/// Determines if a type derives from Godot.Node
	/// </summary>
	private static bool IsGodotNode(INamedTypeSymbol? type)
	{
		if (type == null) return false;

		var currentType = type;
		while (currentType != null)
		{
			if (currentType.ToString() == "Godot.Node" ||
				 currentType.Name == "Node" && currentType.ContainingNamespace?.ToString() == "Godot")
			{
				return true;
			}
			currentType = currentType.BaseType;
		}

		return false;
	}

	/// <summary>
	/// Determines if a member access expression accesses a member variable of the containing class
	/// </summary>
	private static bool IsMemberVariableAccess(MemberAccessExpressionSyntax access, SemanticModel semanticModel, INamedTypeSymbol containingType)
	{
		// Check if the expression is a simple identifier or 'this'
		if (access.Expression is not (IdentifierNameSyntax or ThisExpressionSyntax))
		{
			return false;
		}

		var symbol = semanticModel.GetSymbolInfo(access).Symbol;
		if (symbol == null) return false;

		// Check if it's a field or property
		if (symbol is not (IFieldSymbol or IPropertySymbol)) return false;

		// Check if it's a member of the containing type
		if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType, containingType)) return false;

		// Skip value types and primitives (they can't be null)
		var memberType = symbol switch
		{
			IFieldSymbol field => field.Type,
			IPropertySymbol property => property.Type,
			_ => null
		};

		if (memberType?.IsValueType == true) return false;

		// Skip members that are initialized inline or have non-null default values
		// This is a simplified check - could be enhanced
		if (symbol is IFieldSymbol { HasConstantValue: true }) return false;

		return true;
	}

	/// <summary>
	/// Determines if an expression is protected by null-conditional operator or null check
	/// </summary>
	private static bool IsProtectedExpression(ExpressionSyntax expression)
	{
		var parent = expression.Parent;

		// Check for null-conditional operator
		if (parent is ConditionalAccessExpressionSyntax) return true;

		// Check if the expression is a member access that uses null-conditional
		if (expression is MemberAccessExpressionSyntax memberAccess &&
			 memberAccess.OperatorToken.IsKind(SyntaxKind.QuestionToken)) return true;

		// Check if within an if statement that checks for null
		var ifStatement = expression.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
		if (ifStatement != null)
		{
			// Simplified check - could be enhanced to verify the condition actually checks this member
			var condition = ifStatement.Condition.ToString();
			var memberName = GetMemberNameFromExpression(expression);
			if (!string.IsNullOrEmpty(memberName) &&
				 (condition.Contains($"{memberName} != null") ||
				  condition.Contains($"{memberName} is not null") ||
				  condition.Contains($"null != {memberName}")))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Gets the name of the member from various expression types
	/// </summary>
	private static string GetMemberNameFromExpression(ExpressionSyntax expression)
	{
		return expression switch
		{
			IdentifierNameSyntax identifier => identifier.Identifier.Text,
			MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is IdentifierNameSyntax id => id.Identifier.Text,
			MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
			_ => string.Empty
		};
	}
}