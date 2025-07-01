using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NotNot;

public class AppSettingsGenConfig
{
	 /// <summary>
	 /// the root namespace of the consuming project.   may be null.
	 /// </summary>
	 public string? RootNamespace { get; set; }
	 /// <summary>
	 /// if false, the generated class will be internal
	 /// <para>set from the MSBuild property {NotNot_AppSettings_GenPublic}true{/NotNot_AppSettings_GenPublic} from the consuming project .csproj file</para>
	 /// </summary>
	 public bool IsPublic { get; set; }

	 /// <summary>
	 /// the "sourceTexts" from the consuming project, 
	 /// </summary>
	 public Dictionary<string, SourceText> CombinedSourceTexts { get; set; }


	 /// <summary>
	 /// nuget version of the generator, used only to add a comment to the generated code (for debugging)
	 /// </summary>
	 public string NugetVersion { get; set; }

	 /// <summary>
	 /// root namespace for all generated code
	 /// </summary>
	 public string StartingNamespace => string.IsNullOrWhiteSpace(RootNamespace) ? "AppSettingsGen" : $"{RootNamespace}.AppSettingsGen";

	 /// <summary>
	 /// public or internal, based on value of .IsPublic
	 /// </summary>
	 public string GenAccessModifier => IsPublic ? "public" : "internal";

}
