using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Reliability;

/// <summary>
/// Analyzer that ensures destructors (finalizers) wrap their logic in try/catch blocks
/// to prevent unhandled exceptions from crashing the application.
/// Unhandled exceptions in finalizers can cause fatal crashes and process termination.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DestructorExceptionSafetyAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NOTNOT001";

	private static readonly LocalizableString Title = "Destructor must be protected with try/catch";
	private static readonly LocalizableString MessageFormat = "Destructor ~{0}() lacks exception protection. Unhandled exceptions in finalizers can crash the application. Wrap logic in try/catch block.";
	private static readonly LocalizableString Description = "Destructors (finalizers) must use try/catch blocks to prevent unhandled exceptions from causing fatal crashes. Exceptions in finalizers can cause process termination.";
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
		context.RegisterSyntaxNodeAction(AnalyzeDestructor, SyntaxKind.DestructorDeclaration);
	}

	private static void AnalyzeDestructor(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not DestructorDeclarationSyntax destructor) return;

		// Check if destructor has a body
		if (destructor.Body == null && destructor.ExpressionBody == null) return;

		// Expression-bodied destructors should use block form to add try/catch
		if (destructor.ExpressionBody != null)
		{
			var typeName = context.SemanticModel.GetDeclaredSymbol(destructor)?.ContainingType.Name ?? "Unknown";
			var diagnostic = Diagnostic.Create(
				Rule,
				destructor.ExpressionBody.GetLocation(),
				typeName);
			context.ReportDiagnostic(diagnostic);
			return;
		}

		var body = destructor.Body!;

		// Check if method is empty
		if (body.Statements.Count == 0) return;

		// Check if has comprehensive try/catch protection
		if (HasComprehensiveTryCatch(body))
		{
			return; // Already protected
		}

		// Report diagnostic at method body opening
		var typeName2 = context.SemanticModel.GetDeclaredSymbol(destructor)?.ContainingType.Name ?? "Unknown";
		var diagnostic2 = Diagnostic.Create(
			Rule,
			body.OpenBraceToken.GetLocation(),
			typeName2);

		context.ReportDiagnostic(diagnostic2);
	}

	/// <summary>
	/// Checks if destructor body has comprehensive try/catch protection.
	/// Requires the entire body to consist of a single try/catch statement wrapping all logic.
	/// </summary>
	private static bool HasComprehensiveTryCatch(BlockSyntax body)
	{
		var statements = body.Statements;

		// Empty body is safe
		if (statements.Count == 0) return true;

		// Body must consist of exactly one statement: a try/catch block
		if (statements.Count != 1) return false;

		// That single statement must be a try/catch (not try/finally)
		if (statements[0] is not TryStatementSyntax tryStatement) return false;

		// Must have at least one catch clause
		return tryStatement.Catches.Count > 0;
	}
}
