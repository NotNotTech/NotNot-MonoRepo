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

	public Dictionary<string, SourceText> AppSettingsJsonSourceFiles { get; set; }
	/// <summary>
	/// root namespace for all generated code
	/// </summary>
	public string startingNamespace;

	/// <summary>
	/// public or internal, based on value of .IsPublic
	/// </summary>
	public string GenAccessModifier=> IsPublic ? "public" : "internal";

	public string GenSemVer;
}