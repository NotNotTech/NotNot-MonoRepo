//// Path/Filename: BannedApiAnalyzer.cs

//using System.Collections.Immutable;
//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using Microsoft.CodeAnalysis.Diagnostics;

//[DiagnosticAnalyzer(LanguageNames.CSharp)]
//public class BannedApiAnalyzer : DiagnosticAnalyzer
//{
//   public const string DiagnosticId = "BannedApiUsage";

//   private static readonly DiagnosticDescriptor Rule = new(
//      DiagnosticId,
//      "Usage of banned API",
//      "API '{0}' is banned as per bannedPhrase '{1}' found in .editorconfig",
//      "Usage",
//      DiagnosticSeverity.Error,
//      isEnabledByDefault: true);

//   public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

//   public AnalyzerOptions Options { get; set; }

//   //public override void Initialize(AnalysisContext context)
//   //{
//   //   // Register an action to analyze invocation expressions
//   //   context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);

//   //   //get options
//   //   context.RegisterCompilationStartAction(RegisterCompilationStart);
//   //}

//   //private static void RegisterCompilationStart(CompilationStartAnalysisContext startContext)
//   //{
//   //   var optionsProvider = startContext.Options.AnalyzerConfigOptionsProvider;
//   //   startContext.RegisterCodeBlockAction(actionContext => AnalyzeCodeBlock(actionContext, optionsProvider));
//   //}

//   //private static void AnalyzeCodeBlock(CodeBlockAnalysisContext context, AnalyzerConfigOptionsProvider optionsProvider)
//   //{
//   //   var options = optionsProvider.GlobalOptions;
//   //   // The options contains the .editorconfig settings
//   //   //var options = optionsProvider.GetOptions(context.CodeBlock.SyntaxTree);
//   //   var isFound = options.TryGetValue("dotnet_diagnostic.XA0001.level", out var value);
//   //}

//   public override void Initialize(AnalysisContext context)
//   {
//      context.RegisterCompilationStartAction(compilationStartContext =>
//      {
//         var options = compilationStartContext.Compilation.Options;
//         var optionsProvider = compilationStartContext.Options.AnalyzerConfigOptionsProvider;

//         //foreach(var additionalFile in compilationStartContext.Options.AdditionalFiles.Where(f=>f.Path.EndsWith(".editorconfig")))
//         //{
//         //   var editorConfigOptions = optionsProvider.GetOptions(additionalFile);
//         //   editorConfigOptions.TryGetValue("taco", out var o1);
//         //   editorConfigOptions.TryGetValue("tacos", out var o2);

//         //   var fileText = additionalFile.GetText().ToString();
//         //   var reader = new StringReader(fileText);

//         //   var ecFile = EditorConfig.Core.EditorConfigFile.Parse(reader,Path.GetDirectoryName(additionalFile.Path));

//         //   var parser = new EditorConfig.Core.EditorConfigParser((filePath) => {


//         //   });


//         //}


//         //ecFile.pro


//         //compilationStartContext.RegisterAdditionalFileAction((context) =>
//         //{
//         //   var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.AdditionalFile);
//         //   options.TryGetValue("taco", out var o1);
//         //   options.TryGetValue("tacos", out var o2);
//         //});


//         // Retrieve the banned APIs from .editorconfig
//         var bannedApis = ReadBannedApis(compilationStartContext.Options);

//         // Register other actions like SyntaxNodeAction with the banned APIs
//         compilationStartContext.RegisterSyntaxNodeAction(
//            ctx => AnalyzeNode(ctx, bannedApis),
//            SyntaxKind.InvocationExpression);
//      });
//   }


//   private void AnalyzeNode(SyntaxNodeAnalysisContext context, ImmutableArray<string> bannedApis)
//   {
//      var syntaxTree = context.Node.SyntaxTree;
//      var configOptions = context.Options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);


//      var invocationExpr = (InvocationExpressionSyntax)context.Node;

//      // Getting the semantic model
//      var semanticModel = context.SemanticModel;

//      // Getting the symbol for the method being invoked
//      var methodSymbol = semanticModel.GetSymbolInfo(invocationExpr).Symbol as IMethodSymbol;

//      if (methodSymbol != null)
//      {
//         // Full name of the method including namespace and containing type
//         var fullMethodName = methodSymbol.ToDisplayString(); // $"{methodSymbol.ContainingType}.{methodSymbol.Name}";

//         foreach (var bannedPhrase in bannedApis)
//         {
//            if (fullMethodName.Contains(bannedPhrase))
//            {
//               var diagnostic = Diagnostic.Create(Rule, invocationExpr.GetLocation(), fullMethodName, bannedPhrase);
//               context.ReportDiagnostic(diagnostic);
//            }
//         }
//      }
//   }

//   private ImmutableArray<string> ReadBannedApis(AnalyzerOptions mainOptions)
//   {
//      var bannedApisBuilder = ImmutableArray.CreateBuilder<string>();
//      var options = mainOptions.AnalyzerConfigOptionsProvider;
//      //foreach (var options in mainOptions.AnalyzerConfigOptionsProvider optionsProvider.AnalyzerConfigOptions)
//      {
//         // Assuming that banned APIs are listed under a key like "dotnet_code_quality.BannedApis"
//         if (options.GlobalOptions.TryGetValue("dotnet_code_quality.BannedApis", out var bannedApisString))
//         {
//            bannedApisBuilder.AddRange(bannedApisString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(api => api.Trim()));
//         }
//      }
//      //
//      return bannedApisBuilder.ToImmutable();
//   }

//   private ImmutableArray<string> ReadBannedApis()
//   {
//      // TODO: Implement logic to read banned APIs from .editorconfig
//      // This is a placeholder implementation
//      var filePath = ".editorconfig"; // Path to the .editorconfig file
//      var bannedApis = ImmutableArray.CreateBuilder<string>();

//      bannedApis.Add("System.Console.WriteLine");
//      bannedApis.Add("System.Threading.Tasks.Task.Run");

//      //if (File.Exists(filePath))
//      //{
//      //   var lines = File.ReadAllLines(filePath);
//      //   foreach (var line in lines)
//      //   {
//      //      if (line.StartsWith("banned_apis=")) // Assuming this is the format in .editorconfig
//      //      {
//      //         bannedApis.AddRange(line.Substring("banned_apis=".Length).Split(',').Select(api => api.Trim()));
//      //      }
//      //   }
//      //}

//      return bannedApis.ToImmutable();
//   }
//}
