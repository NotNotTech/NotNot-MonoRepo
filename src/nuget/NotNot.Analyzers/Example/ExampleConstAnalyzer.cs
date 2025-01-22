//namespace LoLo.Analyzers.Example;

//// Import the necessary namespaces
//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using Microsoft.CodeAnalysis.Diagnostics;
//using System.Collections.Immutable;

//// Define the analyzer class
//[DiagnosticAnalyzer(LanguageNames.CSharp)]
//public class ExampleConstAnalyzer : DiagnosticAnalyzer
//{
//	// Define the diagnostic descriptor
//	public const string DiagnosticId = "LL_Ex01";
//	private static readonly LocalizableString Title = "Example CSharp Code Analyzer: CONST";
//	private static readonly LocalizableString MessageFormat = "The local variable '{0}' could be declared as const";
//	private static readonly LocalizableString Description = "Local variable could be declared as const.";
//	private const string Category = "Usage";

//	private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
//		DiagnosticId, Title, MessageFormat, Category, 
//		DiagnosticSeverity.Error, 
//		isEnabledByDefault: false,
//		description: Description);

//	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

//	public override void Initialize(AnalysisContext context)
//	{
//		context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
//	}

//	private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
//	{
//		// Cast the node to a LocalDeclarationStatementSyntax
//		var localDeclaration = (LocalDeclarationStatementSyntax)context.Node;

//		// Check if the local variable could be declared as const
//		if (localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
//		{
//			return;
//		}

//		// Report a diagnostic
//		var diagnostic = Diagnostic.Create(Rule, localDeclaration.GetLocation(), localDeclaration.Declaration.Variables.First().Identifier.ValueText);
//		context.ReportDiagnostic(diagnostic);
//	}

//}

