using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NotNot.GodotNet.SourceGen.Helpers;
using NotNot.GodotNet.SourceGen.Generators.Modular;


/// <summary>
/// This source generator scans for classes marked with the [NotNotScene] attribute
/// and generates partial classes for them. Additionally, it processes specific
/// additional files such as .tscn, project.godot, and .gd files.
/// </summary>
[Generator]
public class NotNotSceneRoot_Generator : ModularGenerator_Base
{
	public NotNotSceneRoot_Generator()
	{
		TargetAttributes = ["NotNotSceneRoot", "NotNotSceneRootAttribute"];

		// Define regex patterns to match additional files of interest
		TargetAdditionalFiles =
		[
			new Regex(@"\.tscn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
			new Regex(@"project\.godot$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled),
			new Regex(@"\.gd$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)
		];

	}




	/// <summary>
	/// Generates the partial classes based on the configuration.
	/// </summary>
	/// <param name="config">The configuration object containing context and parameters.</param>
	public override void GeneratePartialClasses(GodotResourceGeneratorContextConfig config)
	{


		foreach (var classDeclaration in config.Classes)
		{
			var namespaceName = classDeclaration._GetNamespaceName();
			var className = classDeclaration.Identifier.Text;

			var generatedCode = Gen_NotNotSceneAttribute(namespaceName, className, config);
			config.Context.AddSource($"{className}.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
		}

		// Generate the NotNotScene loader code
		var loaderCode = GenNotNotSceneTscnLoader(config);
		config.Context.AddSource("NotNotSceneLoader.g.cs", SourceText.From(loaderCode, Encoding.UTF8));


		//var details = Gen_ICtor(config);
		//config.Context.AddSource(details.fileName, SourceText.From(details.sourceCode, Encoding.UTF8));
	}



	/// <summary>
	/// All found [NotNotScene] classes should have a matching .tscn in the same folder location.
	/// This .tscn path is in "res://" format.
	/// Key = class name.  Value = "{ClassName}.tscn" path.
	/// </summary>
	public Dictionary<string, string> NotNotScenes = new();

	/// <summary>
	/// Gen partial classes for [NotNotScene]
	/// </summary>
	/// <param name="namespaceName">The namespace of the class.</param>
	/// <param name="className">The name of the class.</param>
	/// <param name="additionalFiles">The additional files to process.</param>
	/// <returns>The generated code for the partial class.</returns>
	public string Gen_NotNotSceneAttribute(string? namespaceName, string className, GodotResourceGeneratorContextConfig config)
	{

		// Determine the .tscn file path for the class
		string tscnResPath = string.Empty;
		{
			string tscnOsFilePath = string.Empty;
			string expectedTscnFileName = $"{className}.tscn";

			foreach (var kvp in config.AdditionalFiles)
			{
				if (kvp.Key.EndsWith(expectedTscnFileName, StringComparison.OrdinalIgnoreCase))
				{
					tscnOsFilePath = kvp.Key;
					break;
				}
			}
			if (config.TryConvertFilePathToResPath(tscnOsFilePath, out tscnResPath))
			{
				// Store the className and its associated .tscn path into the config.NotNotScenes dictionary
				NotNotScenes[className] = tscnResPath;
			}

		}


		var sb = new StringBuilder();


		sb.AppendLine($$"""

//usings
using Godot;
//using System.CodeDom.Compiler;

using NotNot;


//namespace (if any)
{{Func.Eval(() =>
		{
			if (!string.IsNullOrEmpty(namespaceName))
			{
				return $"namespace {namespaceName};";
			}
			else
			{
				return "//global namespace?";
			}
		})}}


//[GeneratedCode("NotNot.GodotNet.SourceGen.NotNotSceneGen", "1.0.0.0")]
public partial class {{className}} //[NotNotScene]
{
   //public {{className}}()
   //{
   //  this._SceneCtor_TryHotReload(); // Execute NotNot.Godot helper extension method for EditorReloading
   //  PrintAdditionalFiles();
   //  GD.Print("gen ctor");
   //  zz_Test.Test();
   //  _NN_Ctor();
   //}
   
   //partial void _NN_Ctor();

   //public override void _Notification(int what)
   //{
   //  GD.Print("SORCEGEN: GodotNotifications Notify");
   //   base._Notification(what);
   //}

   //if ResPath was properly discovered during sourcegen, add the ResPath and InstantiateTscn methods
   {{Func.Eval(() =>
		{

			var lines = new StringBuilder();

			if (string.IsNullOrWhiteSpace(tscnResPath) is false)
			{
				return $$"""

   public static readonly string ResPath = "{{tscnResPath}}";

   public static {{className}} InstantiateTscn()
   {
      return _GD.InstantiateScene<{{className}}>(ResPath);
   }
""";
			}

			config.Context._Error($"No ResPath found for {className}.  Should not be marked with [NotNotScene] attrib if not a .tscn");
			return "//ERROR: NO RESPATH FOUND!";
		})}}

                      
                      
    private void PrintAdditionalFiles()
    {
      // debug show listing of additional files...
      {{Func.Eval(() =>
		{
			var lines = new StringBuilder();
			//foreach (var kvp in config.ResPaths)
			{
				//lines.AppendLine($"  // {kvp.Value}");
			}

			return lines;
		})}}
   }
 
 
 
 
    //public override void _Notification(int what)
    //{
    //   this._PrintWarn("gen Notify");
    //}


 //end of class
 }
""");




		return sb.ToString();
	}




	/// <summary>
	/// Generates the code for loading the .tscn files for the [NotNotScene] classes.
	/// </summary>
	/// <param name="config">The configuration object containing context and parameters.</param>
	/// <returns>The generated code for loading the .tscn files.</returns>
	private string GenNotNotSceneTscnLoader(GodotResourceGeneratorContextConfig config)
	{
		var sb = new StringBuilder();


		sb.AppendLine(@$"

using Godot;

public static class NotNotSceneLoader
{{
   // Generate a dictionary to map class names to their .tscn paths
   private static readonly Dictionary<string, string> scenePaths = new Dictionary<string, string>
   {{

");
		foreach (var scenePair in NotNotScenes)
		{
			var className = scenePair.Key;
			var tscnResPath = scenePair.Value;

			sb.AppendLine($@"    {{ ""{className}"", ""{tscnResPath}"" }},");

		}
		sb.AppendLine("  };");

		// Generate the generic Load method
		sb.AppendLine(@"
  public static T InstantiateTscn<T>() where T : Node
  {
    var className = typeof(T).Name;
    if (scenePaths.TryGetValue(className, out var tscnResPath))
    {
      GD.Print($""Loading scene: {className}"");
      GD.Print($""TSN path: {tscnResPath}"");
      // Load the .tscn file and instantiate the scene

      var scene = ResourceLoader.Load<PackedScene>(tscnResPath);
      try
      {
         var toReturn = scene.Instantiate<T>();
         return toReturn;
      }
      catch (Exception e)
      {
         throw new InvalidOperationException(
            $""The type {className} could not be instantiated from the scene at {tscnResPath}. Are there two scenes with the same name?  [SceneType] must be unique, or we need to extend to allow path hints."", e);
      }

    }
    else
    {
      //GD.PrintErr($""No .tscn path found for class: {className}"");
      throw new InvalidOperationException($""No.tscn path found for class: { className }"");
      return null;
    }
  }
");

		sb.AppendLine("}");

		return sb.ToString();
	}

	/// <summary>
	/// Generates the code for loading the .tscn files for the [NotNotScene] classes.
	/// </summary>
	/// <param name="config">The configuration object containing context and parameters.</param>
	/// <returns>The generated code for loading the .tscn files.</returns>
	private static (string fileName, string sourceCode) Gen_ICtor(GodotResourceGeneratorContextConfig config)
	{
		var sb = new StringBuilder();


		sb.AppendLine(@$"
namespace NotNot.GodotNet.SourceGen;
public interface ICtor
{{
   void _Ctor()
   {{
      //default impl
   }}
}}

");
		return ("ICtor.g.cs", sb.ToString());
	}

}
