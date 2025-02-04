using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NotNot.GodotNet.SourceGen.Helpers;

namespace NotNot.GodotNet.SourceGen.Generators.Modular;


public abstract class ModularGenerator_Base : IIncrementalGenerator
{
	public List<string>? TargetAttributes = new();

	public List<Regex>? TargetAdditionalFiles;





	/// <summary>
	/// Predicate method to identify class declarations with attributes.
	/// </summary>
	/// <param name="node">The syntax node to check.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>True if the node is a class declaration with attributes, otherwise false.</returns>
	private bool Predicate(SyntaxNode node, CancellationToken cancellationToken)
	{
		return node is ClassDeclarationSyntax { AttributeLists: { Count: > 0 } };
	}

	/// <summary>
	/// Transform method to extract class declarations with the NotNotScene attribute.
	/// </summary>
	/// <param name="context">The generator syntax context.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The class declaration if it contains the NotNotScene attribute, otherwise null.</returns>
	private ClassDeclarationSyntax? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		var classDeclaration = (ClassDeclarationSyntax)context.Node;
		foreach (var attributeList in classDeclaration.AttributeLists)
		{
			foreach (var attribute in attributeList.Attributes)
			{
				var name = attribute.Name.ToString();

				if (this.TargetAttributes.Contains(name))
				{
					return classDeclaration;
				}
			}
		}
		return null;
	}


	/// <summary>
	/// Initializes the incremental generator.
	/// </summary>
	/// <param name="context">The context for initializing the generator.</param>
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		////to debug
		//Debugger.Launch();


		//add the project.godot as this is  our "project root folder"
		TargetAdditionalFiles.Add(new Regex(@"project\.godot$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));


		// Register a syntax provider to find class declarations with attributes
		IncrementalValueProvider<ImmutableArray<ClassDeclarationSyntax?>> classDeclarations;

		//if (this.TargetAttributes is null)
		//{
		//   classDeclarations = new();
		//}
		//else
		{
			classDeclarations = context.SyntaxProvider
				.CreateSyntaxProvider(Predicate, Transform)
				.Where(static cls => cls != null)
				.Collect();
		}

		HashSet<string> allAdditionalFilePaths = new();

		// Collect additional files using multiple regex patterns
		var additionalFiles = context.AdditionalTextsProvider.Where(file =>
			{
				//list of all files
				allAdditionalFilePaths.Add(file.Path);
				foreach (var regex in TargetAdditionalFiles)
				{
					if (regex.IsMatch(file.Path))
					{
						return true;
					}
				}
				return false;
			});





		// Transform additional files to a single Dictionary entry
		var combinedSourceTextsProvider = additionalFiles.Collect().Select((files, ct) =>
		{
			var combinedFiles = new Dictionary<string, SourceText>();
			foreach (var file in files)
			{
				var sourceText = file.GetText(ct);
				if (sourceText != null)
				{
					combinedFiles[file.Path] = sourceText;
				}
			}
			return combinedFiles;
		});

		// Retrieve the root namespace from the AnalyzerConfigOptionsProvider
		var namespaceProvider = context.AnalyzerConfigOptionsProvider
		  .Select(static (provider, ct) =>
		  {
			  // Try to get the "build_property.rootnamespace" from GlobalOptions
			  provider.GlobalOptions.TryGetValue("build_property.rootnamespace", out string? rootNamespace);
			  return rootNamespace; // Return the root namespace or null if not found
		  });

		// Combine the namespace provider with the combined source texts provider and class declarations
		var combinedProvider = namespaceProvider.Combine(combinedSourceTextsProvider).Combine(classDeclarations);

		// Register the source output to pass the combined provider and class declarations to the ExecuteGenerator method
		context.RegisterSourceOutput(
		  combinedProvider,
		  (spc, content) =>
		  {
			  var rootNamespace = content.Left.Left; // The root namespace
			  var combinedSourceTexts = content.Left.Right; // The combined source texts
			  var classes = content.Right; // The class declarations

			  ExecuteGenerator(spc, rootNamespace, combinedSourceTexts, classes, allAdditionalFilePaths);
		  });
	}


	/// <summary>
	/// Executes the generator to produce partial classes.
	/// </summary>
	/// <param name="context">The source production context.</param>
	/// <param name="rootNamespace">The root namespace of the project.</param>
	/// <param name="additionalFiles">The additional files to process.</param>
	/// <param name="classes">The class declarations to process.</param>
	public void ExecuteGenerator(SourceProductionContext context, string? rootNamespace, Dictionary<string, SourceText> additionalFiles, ImmutableArray<ClassDeclarationSyntax> classes, HashSet<string> allAdditionalFilePaths)
	{
		var config = new GodotResourceGeneratorContextConfig
		{
			Context = context,
			RootNamespace = rootNamespace,
			AdditionalFiles = additionalFiles,
			Classes = classes,
			AllAdditionalFilePaths = allAdditionalFilePaths,
		};
		config.Initialize();

		GeneratePartialClasses(config);
	}

	/// <summary>
	/// Generates the partial classes based on the configuration.
	/// </summary>
	/// <param name="config">The configuration object containing context and parameters.</param>
	public abstract void GeneratePartialClasses(GodotResourceGeneratorContextConfig config);

}
