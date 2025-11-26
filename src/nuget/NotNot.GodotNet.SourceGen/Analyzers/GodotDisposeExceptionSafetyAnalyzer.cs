using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.GodotNet.SourceGen.Analyzers;

/// <summary>
/// Analyzer that ensures GodotObject.Dispose(bool disposing) overrides
/// use try/catch blocks to prevent unhandled exceptions from crashing Godot editor.
/// Unhandled exceptions in Dispose during editor shutdown or hot-reload cause
/// fatal crashes due to native/managed boundary issues.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GodotDisposeExceptionSafetyAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "GODOT003";

	private static readonly LocalizableString Title = "Dispose override must be protected with try/catch";
	private static readonly LocalizableString MessageFormat = "GodotObject.Dispose(bool disposing) override in '{0}' lacks exception protection. Unhandled exceptions during Dispose can crash Godot editor. Wrap cleanup code in try/catch block.";
	private static readonly LocalizableString MessageFormatNoDisposingGuard = "GodotObject.Dispose(bool disposing) override in '{0}' lacks 'if (disposing)' guard and exception protection. Add guard and wrap cleanup in try/catch block.";
	private static readonly LocalizableString Description = "GodotObject.Dispose overrides must use try/catch blocks to prevent unhandled exceptions from causing fatal crashes during editor shutdown or hot-reload. Exceptions crossing the native/managed boundary can cause unrecoverable crashes (0xC0000005).";
	private const string Category = "Reliability";

	private static readonly DiagnosticDescriptor Rule = new(
		 DiagnosticId,
		 Title,
		 MessageFormat,
		 Category,
		 DiagnosticSeverity.Error,
		 isEnabledByDefault: true,
		 description: Description);

	private static readonly DiagnosticDescriptor RuleNoDisposingGuard = new(
		 DiagnosticId,
		 Title,
		 MessageFormatNoDisposingGuard,
		 Category,
		 DiagnosticSeverity.Error,
		 isEnabledByDefault: true,
		 description: Description);

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule, RuleNoDisposingGuard);

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

		// Check if this is Dispose(bool) override
		if (method.Identifier.Text != "Dispose") return;
		if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;

		// Check parameter signature: single bool parameter
		if (method.ParameterList.Parameters.Count != 1) return;

		var symbol = context.SemanticModel.GetDeclaredSymbol(method);
		if (symbol == null) return;

		// Verify parameter is bool type
		var parameterSymbol = symbol.Parameters.FirstOrDefault();
		if (parameterSymbol?.Type.SpecialType != SpecialType.System_Boolean) return;

		// Check if containing type derives from Godot.GodotObject
		if (!IsGodotObject(symbol.ContainingType)) return;

		// Analyze method body (including expression-bodied methods)
		if (method.Body == null && method.ExpressionBody == null) return;

		// For expression-bodied methods, report error (they're single expressions, can't wrap in try/catch)
		if (method.ExpressionBody != null)
		{
			// Expression-bodied Dispose methods should use block form to add try/catch
			var typeName = symbol.ContainingType.Name;
			var diagnostic = Diagnostic.Create(
				RuleNoDisposingGuard,
				method.ExpressionBody.GetLocation(),
				typeName);
			context.ReportDiagnostic(diagnostic);
			return;
		}

		var body = method.Body!;

		// Check if method is empty or only calls base.Dispose
		if (IsEmptyOrOnlyBaseDispose(body)) return;

		// Check if has comprehensive try/catch protection
		if (HasComprehensiveTryCatch(body, parameterSymbol, context.SemanticModel, out var hasDisposingGuard, out var disposingIfStatement))
		{
			return; // Already protected
		}

		// Report diagnostic at the specific location that needs fixing
		Location diagnosticLocation;
		if (disposingIfStatement != null)
		{
			// Report at the if (disposing) statement that needs try/catch wrapper
			diagnosticLocation = disposingIfStatement.GetLocation();
		}
		else
		{
			// No guard exists - report at method body opening to indicate entire body needs wrapping
			diagnosticLocation = body.OpenBraceToken.GetLocation();
		}

		var typeName2 = symbol.ContainingType.Name;
		var diagnostic2 = Diagnostic.Create(
			hasDisposingGuard ? Rule : RuleNoDisposingGuard,
			diagnosticLocation,
			typeName2);

		context.ReportDiagnostic(diagnostic2);
	}

	/// <summary>
	/// Determines if a type derives from Godot.GodotObject
	/// </summary>
	private static bool IsGodotObject(INamedTypeSymbol? type)
	{
		if (type == null) return false;

		var currentType = type;
		while (currentType != null)
		{
			if (currentType.ToString() == "Godot.GodotObject" ||
				 (currentType.Name == "GodotObject" && currentType.ContainingNamespace?.ToString() == "Godot"))
			{
				return true;
			}
			currentType = currentType.BaseType;
		}

		return false;
	}

	/// <summary>
	/// Checks if method body is empty or only contains base.Dispose call
	/// </summary>
	private static bool IsEmptyOrOnlyBaseDispose(BlockSyntax body)
	{
		var statements = body.Statements;
		if (statements.Count == 0) return true;

		// Check if only contains base.Dispose(disposing) call
		if (statements.Count == 1 && statements[0] is ExpressionStatementSyntax expr)
		{
			if (expr.Expression is InvocationExpressionSyntax invocation)
			{
				if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
				{
					if (memberAccess.Expression is BaseExpressionSyntax &&
						 memberAccess.Name.Identifier.Text == "Dispose")
					{
						return true;
					}
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Checks if method body has comprehensive try/catch protection.
	/// Uses semantic analysis to verify disposing parameter references.
	/// Returns the disposing if statement via out parameter for diagnostic location.
	/// </summary>
	private static bool HasComprehensiveTryCatch(BlockSyntax body, IParameterSymbol disposingParameter, SemanticModel semanticModel, out bool hasDisposingGuard, out IfStatementSyntax? disposingIfStatement)
	{
		hasDisposingGuard = false;
		disposingIfStatement = null;
		var statements = body.Statements;

		// Look for if (disposing) pattern using semantic analysis
		foreach (var statement in statements)
		{
			if (statement is IfStatementSyntax ifStmt)
			{
				// Use semantic analysis to check if condition references the disposing parameter
				if (ConditionReferencesParameter(ifStmt.Condition, disposingParameter, semanticModel))
				{
					disposingIfStatement = ifStmt;
					hasDisposingGuard = true;
					break;
				}
			}
		}

		// If no disposing guard found, check if entire body is wrapped in try/catch
		if (!hasDisposingGuard)
		{
			// Check if there's a try statement with catch clause that covers most of the method body
			var tryStatements = body.DescendantNodes().OfType<TryStatementSyntax>().ToList();
			if (tryStatements.Count == 0) return false;

			// Filter to only try statements that have catch clauses
			var tryStatementsWithCatch = tryStatements.Where(t => t.Catches.Count > 0).ToList();
			if (tryStatementsWithCatch.Count == 0) return false;

			// Consider protected if try/catch block contains substantial portion (at least half of statements excluding base.Dispose)
			var nonBaseStatements = statements.Count(s => !IsBaseDisposeCall(s));
			if (nonBaseStatements == 0) return true; // No risky statements

			return tryStatementsWithCatch.Any(t => t.Block.Statements.Count >= nonBaseStatements);
		}

		// If has disposing guard, check if the if block content is wrapped in try/catch
		if (disposingIfStatement != null)
		{
			var ifBlock = disposingIfStatement.Statement as BlockSyntax;
			if (ifBlock == null)
			{
				// Single statement without block - check if it's a try statement with catch
				if (disposingIfStatement.Statement is TryStatementSyntax trySingle)
				{
					return trySingle.Catches.Count > 0;
				}
				return false;
			}

			// Check if if block is empty or only has base calls
			if (ifBlock.Statements.Count == 0) return true;

			// Check if the if block contains try/catch (not just try/finally)
			var tryInIf = ifBlock.Statements.OfType<TryStatementSyntax>().Where(t => t.Catches.Count > 0).ToList();
			if (tryInIf.Count == 0) return false;

			// Consider protected if try/catch covers most of the if block
			return tryInIf.Any(t => t.Block.Statements.Count >= ifBlock.Statements.Count);
		}

		return false;
	}

	/// <summary>
	/// Uses semantic analysis to determine if a condition expression references a specific parameter.
	/// </summary>
	private static bool ConditionReferencesParameter(ExpressionSyntax condition, IParameterSymbol parameter, SemanticModel semanticModel)
	{
		// Get all identifier names in the condition
		var identifiers = condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();

		foreach (var identifier in identifiers)
		{
			var symbolInfo = semanticModel.GetSymbolInfo(identifier);
			if (symbolInfo.Symbol is IParameterSymbol paramSym &&
				SymbolEqualityComparer.Default.Equals(paramSym, parameter))
			{
				return true;
			}
		}

		return false;
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
