using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NotNot.GodotNet.SourceGen.Helpers;

/// <summary>
/// Define the GodotResourceGeneratorContextConfig class to hold the configuration parameters
/// </summary>
public class GodotResourceGeneratorContextConfig
{
	public SourceProductionContext Context { get; set; }
	public string? RootNamespace { get; set; }
	public Dictionary<string, SourceText> AdditionalFiles { get; set; }
	public ImmutableArray<ClassDeclarationSyntax> Classes { get; set; }

	public HashSet<string> AllAdditionalFilePaths { get; set; }

	/// <summary>
	/// The res root is the folder where the project.godot file is located, e.g., "C:/projects/myproject/[project.godot]".
	/// </summary>
	public string resRootPath;

	public bool TryConvertFilePathToResPath(string filePath, out string resPath)
	{
		if (!string.IsNullOrEmpty(filePath) && filePath.StartsWith(resRootPath))
		{
			// Convert the file path to "res://" format
			resPath = "res://" + filePath.Substring(resRootPath.Length + 1).Replace("\\", "/");
			return true;
		}
		else
		{
			resPath = string.Empty;
			return false;
		}
	}

	///// <summary>
	///// maps filepaths to respath
	///// E.g., "C:/projects/myproject/project.godot" ==> "res://project.godot".
	///// </summary>
	//public Dictionary<string, string> ResPaths = new();

	/// <summary>
	/// Initializes the GodotResourceGeneratorContextConfig object, populating resRootPath and NotNotScenesResPath.
	/// </summary>
	public void Initialize()
	{
		// Find the path of the project.godot file
		foreach (var file in AdditionalFiles)
		{
			if (file.Key.EndsWith("project.godot", StringComparison.OrdinalIgnoreCase))
			{
				// Get the directory of the project.godot file
				resRootPath = System.IO.Path.GetDirectoryName(file.Key);
				break;
			}
		}

		if (string.IsNullOrEmpty(resRootPath))
		{
			this.Context._Error("Could not find the project.godot file to determine the resRootPath.");
			//throw new InvalidOperationException("Could not find the project.godot file to determine the resRootPath.");
			return;
		}

		//// Populate the ResPaths dictionary
		//foreach (var scene in AdditionalFiles)
		//{
		//   var filePath = scene.Key;

		//   if (!string.IsNullOrEmpty(filePath) && filePath.StartsWith(resRootPath))
		//   {
		//      // Convert the file path to "res://" format
		//      var resPath = "res://" + filePath.Substring(resRootPath.Length + 1).Replace("\\", "/");
		//      ResPaths[filePath] = resPath;
		//   }
		//   else
		//   {
		//      ResPaths[filePath] = string.Empty;
		//   }
		//}
	}
}
