using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Advanced;

/// <summary>
/// Advanced context-aware analyzer that provides more intelligent detection
/// of async/await issues based on the surrounding code context
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextAwareTaskAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic for potentially blocking async calls in UI contexts
	/// </summary>
	public const string UiBlockingDiagnosticId = "NN_R003";


	private static readonly DiagnosticDescriptor UiBlockingRule = new(
		 id: UiBlockingDiagnosticId,
		 title: "Potentially blocking async call in UI context",
		 messageFormat: "Async call '{0}' may block the UI thread. Consider using ConfigureAwait(false) or ensuring proper async/await patterns.",
		 category: "Performance",
		 defaultSeverity: DiagnosticSeverity.Warning,
		 isEnabledByDefault: true,
		 description: "Blocking async calls in UI contexts can cause deadlocks and poor user experience. " +
						  "Use ConfigureAwait(false) for library calls or ensure proper async patterns.",
		 helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{UiBlockingDiagnosticId}",
		 customTags: new[] { "Performance", "UI", "Deadlock" });


	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		 ImmutableArray.Create(UiBlockingRule);

	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

		context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);
	}

	private static void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking("NN_R003", "AnalyzeAwaitExpression");

		var awaitExpression = (AwaitExpressionSyntax)context.Node;
		var semanticModel = context.SemanticModel;

		// Skip server contexts - they don't have UI thread blocking concerns
		if (IsInServerContext(awaitExpression, semanticModel))
		{
			return;
		}

		// Check for UI context blocking potential
		if (IsInUiContext(awaitExpression, semanticModel))
		{
			if (CouldCauseUiBlocking(awaitExpression, semanticModel))
			{
				var diagnostic = Diagnostic.Create(
					 UiBlockingRule,
					 awaitExpression.GetLocation(),
					 awaitExpression.Expression.ToString());

				context.ReportDiagnostic(diagnostic);
			}
		}

		//// Check for missing ConfigureAwait(false) in library code
		//if (IsInLibraryCode(awaitExpression, semanticModel))
		//{
		//	if (!HasConfigureAwait(awaitExpression))
		//	{
		//		var diagnostic = Diagnostic.Create(
		//			 ConfigureAwaitRule,
		//			 awaitExpression.GetLocation(),
		//			 awaitExpression.Expression.ToString());

		//		context.ReportDiagnostic(diagnostic);
		//	}
		//}
	}

	private static bool IsInUiContext(SyntaxNode node, SemanticModel semanticModel)
	{
		// Early exit: if this is already identified as server context, it's not UI
		if (IsInServerContext(node, semanticModel))
		{
			return false;
		}

		// Check if we're in a class that likely represents a UI component
		var containingClass = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (containingClass != null)
		{
			var className = containingClass.Identifier.ValueText;

			// Exclude server-side controller patterns from UI detection
			if (className.EndsWith("Controller") || className.Contains("Controller"))
			{
				return false;
			}

			// Common UI class patterns
			if (className.EndsWith("Page") ||
				 className.EndsWith("Window") ||
				 className.EndsWith("Form") ||
				 className.EndsWith("Control") ||
				 className.EndsWith("Component") ||
				 className.EndsWith("Activity") ||
				 className.EndsWith("Fragment") ||
				 className.EndsWith("ViewModel"))
			{
				return true;
			}

			// Check for UI framework base classes
			var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
			if (classSymbol?.BaseType != null)
			{
				var baseTypeName = classSymbol.BaseType.Name;
				
				// Exclude server base classes
				if (baseTypeName == "ControllerBase" || 
					 baseTypeName == "Controller" || 
					 baseTypeName == "ApiController")
				{
					return false;
				}

				// UI base classes
				if (baseTypeName.Contains("Page") ||
					 baseTypeName.Contains("Window") ||
					 baseTypeName.Contains("Form") ||
					 baseTypeName.Contains("Control") ||
					 baseTypeName.Contains("Activity") ||
					 baseTypeName.Contains("Fragment"))
				{
					return true;
				}
			}
		}

		// Check namespace patterns to exclude server namespaces
		var namespaceDeclaration = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>() ??
									node.FirstAncestorOrSelf<FileScopedNamespaceDeclarationSyntax>() as BaseNamespaceDeclarationSyntax;

		if (namespaceDeclaration != null)
		{
			var namespaceName = namespaceDeclaration.Name.ToString();

			// Exclude server-side namespaces from UI detection
			if (namespaceName.Contains(".Api") ||
				 namespaceName.Contains(".Controllers") ||
				 namespaceName.Contains(".WebApi") ||
				 namespaceName.Contains(".Server") ||
				 namespaceName.Contains(".Services") ||
				 namespaceName.Contains(".Infrastructure"))
			{
				return false;
			}
		}

		// Check method names that suggest UI event handlers
		var containingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
		if (containingMethod != null)
		{
			var methodName = containingMethod.Identifier.ValueText;
			if (methodName.EndsWith("_Click") ||
				 methodName.EndsWith("_Tapped") ||
				 methodName.EndsWith("_Changed") ||
				 methodName.StartsWith("On") && methodName.Contains("Click"))
			{
				return true;
			}
		}

		return false;
	}

	private static bool CouldCauseUiBlocking(AwaitExpressionSyntax awaitExpression, SemanticModel semanticModel)
	{
		// Check if the awaited expression doesn't use ConfigureAwait(false)
		if (awaitExpression.Expression is InvocationExpressionSyntax invocation)
		{
			if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
			{
				if (memberAccess.Name.Identifier.ValueText == "ConfigureAwait")
				{
					// Check if it's ConfigureAwait(false)
					if (invocation.ArgumentList.Arguments.Count == 1)
					{
						var argument = invocation.ArgumentList.Arguments[0];
						if (argument.Expression is LiteralExpressionSyntax literal &&
							 literal.Token.IsKind(SyntaxKind.FalseKeyword))
						{
							return false; // ConfigureAwait(false) is used, no blocking risk
						}
					}
				}
			}
		}

		// If no ConfigureAwait(false), there's potential for blocking
		return true;
	}

	private static bool IsInLibraryCode(SyntaxNode node, SemanticModel semanticModel)
	{
		// Check if we're in a public API (public classes/methods)
		var containingClass = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (containingClass?.Modifiers.Any(SyntaxKind.PublicKeyword) == true)
		{
			var containingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
			if (containingMethod?.Modifiers.Any(SyntaxKind.PublicKeyword) == true ||
				 containingMethod?.Modifiers.Any(SyntaxKind.ProtectedKeyword) == true)
			{
				return true;
			}
		}

		// Check namespace - avoid UI-related namespaces
		var namespaceDeclaration = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>() ??
											node.FirstAncestorOrSelf<FileScopedNamespaceDeclarationSyntax>() as BaseNamespaceDeclarationSyntax;

		if (namespaceDeclaration != null)
		{
			var namespaceName = namespaceDeclaration.Name.ToString();

			// Don't apply rule to UI namespaces
			if (namespaceName.Contains(".UI") ||
				 namespaceName.Contains(".Views") ||
				 namespaceName.Contains(".Pages") ||
				 namespaceName.Contains(".Controls"))
			{
				return false;
			}

			// Apply to library/service namespaces
			if (namespaceName.Contains(".Services") ||
				 namespaceName.Contains(".Library") ||
				 namespaceName.Contains(".Core") ||
				 namespaceName.Contains(".Infrastructure"))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines if the code is executing in a server context where ConfigureAwait(false) is unnecessary
	/// </summary>
	/// <param name="node">The syntax node to analyze</param>
	/// <param name="semanticModel">The semantic model for symbol analysis</param>
	/// <returns>True if in server context, false otherwise</returns>
	private static bool IsInServerContext(SyntaxNode node, SemanticModel semanticModel)
	{
		// Check if we're in a Web API controller class
		var containingClass = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (containingClass != null)
		{
			var className = containingClass.Identifier.ValueText;

			// ASP.NET Core controller patterns
			if (className.EndsWith("Controller") || 
				 className.Contains("Controller"))
			{
				return true;
			}

			// Check for controller base classes
			var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
			if (classSymbol?.BaseType != null)
			{
				var baseTypeName = classSymbol.BaseType.Name;
				if (baseTypeName.Contains("Controller") ||
					 baseTypeName == "ControllerBase" ||
					 baseTypeName == "ApiController")
				{
					return true;
				}

				// Check inheritance chain for controller base types
				var baseType = classSymbol.BaseType;
				while (baseType != null)
				{
					if (baseType.Name == "ControllerBase" || 
						 baseType.Name == "Controller" ||
						 baseType.Name == "ApiController")
					{
						return true;
					}
					baseType = baseType.BaseType;
				}
			}
		}

		// Check namespace patterns for server/API code
		var namespaceDeclaration = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>() ??
									node.FirstAncestorOrSelf<FileScopedNamespaceDeclarationSyntax>() as BaseNamespaceDeclarationSyntax;

		if (namespaceDeclaration != null)
		{
			var namespaceName = namespaceDeclaration.Name.ToString();

			// Server-side namespace patterns
			if (namespaceName.Contains(".Api") ||
				 namespaceName.Contains(".Controllers") ||
				 namespaceName.Contains(".WebApi") ||
				 namespaceName.Contains(".Server") ||
				 namespaceName.Contains(".Services") ||
				 namespaceName.Contains(".Core") ||
				 namespaceName.Contains(".Infrastructure") ||
				 namespaceName.Contains(".Background") ||
				 namespaceName.Contains(".Workers"))
			{
				return true;
			}
		}

		// Check for common server framework attributes
		var containingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
		if (containingMethod != null)
		{
			var hasServerAttributes = containingMethod.AttributeLists
				.SelectMany(al => al.Attributes)
				.Any(attr =>
				{
					var attrName = attr.Name.ToString();
					return attrName.Contains("Http") ||  // HttpGet, HttpPost, etc.
						   attrName == "Route" ||
						   attrName == "ApiController" ||
						   attrName == "Authorize";
				});

			if (hasServerAttributes)
			{
				return true;
			}
		}

		// Check class-level attributes for server indicators
		if (containingClass != null)
		{
			var hasServerClassAttributes = containingClass.AttributeLists
				.SelectMany(al => al.Attributes)
				.Any(attr =>
				{
					var attrName = attr.Name.ToString();
					return attrName == "ApiController" ||
						   attrName == "Route" ||
						   attrName == "Authorize";
				});

			if (hasServerClassAttributes)
			{
				return true;
			}
		}

		return false;
	}

	//private static bool HasConfigureAwait(AwaitExpressionSyntax awaitExpression)
	//{
	//	if (awaitExpression.Expression is InvocationExpressionSyntax invocation)
	//	{
	//		if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
	//		{
	//			return memberAccess.Name.Identifier.ValueText == "ConfigureAwait";
	//		}
	//	}

	//	return false;
	//}
}