using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NotNot.AppSettingsInternal;
using SGF;

[assembly: InternalsVisibleTo("NotNot.AppSettings.Tests")]

namespace NotNot;

/// <summary>
/// generate settings classes from appsettings.json files.  These will be namespaced as [TargetProjectNamespaceRoot].AppSettings.[ConfigName]
/// </summary>
[IncrementalGenerator]
internal class AppSettingsGen : IncrementalGenerator
{
	public AppSettingsGen() : base("AppSettingsGen")
	{
	}

	public override void OnInitialize(SgfInitializationContext context)
	{
		// SGF handles debugging automatically via SGF_DEBUGGER_LAUNCH environment variable

		/////////////  NEW ADDITIONAL FILES WORKFLOW
		{
			// Get the MSBuild property <NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic> from the consuming project .csproj file
			var genPublicProvider = context.AnalyzerConfigOptionsProvider
				.Select((provider, ct) =>
				{
					provider.GlobalOptions.TryGetValue("build_property.NotNot_AppSettings_GenPublic", out var genPublic);
					if (string.IsNullOrEmpty(genPublic))
					{
						provider.GlobalOptions.TryGetValue("build_metadata.NotNot_AppSettings_GenPublic", out genPublic);
					}
					return !string.IsNullOrEmpty(genPublic) && bool.TryParse(genPublic, out var result) && result;
				});

			// Note: Interfaces are ALWAYS generated in .Interfaces namespace (no opt-in needed)

			// get appsettings*.json via AdditionalFiles
			var regex = new Regex(@"[/\\]appsettings\..*json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
			var additionalFiles = context.AdditionalTextsProvider.Where(file => regex.IsMatch(file.Path));

			// group SourceText once
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

			var namespaceProvider = context.AnalyzerConfigOptionsProvider
				.Select(static (provider, ct) =>
				{
					provider.GlobalOptions.TryGetValue("build_property.rootnamespace", out string? rootNamespace);
					return rootNamespace;
				});

			// SIMPLE: get invoking project from Compilation
			var projectNameProvider = context.CompilationProvider
				.Select((compilation, ct) => compilation.AssemblyName ?? "UnknownProject");

			var combinedProvider = projectNameProvider
				.Combine(namespaceProvider)
				.Combine(combinedSourceTextsProvider)
				.Combine(genPublicProvider);

			context.RegisterSourceOutput(
				combinedProvider,
				(spc, content) =>
				{
					var projectName = content.Left.Left.Left;
					var rootNamespace = content.Left.Left.Right;
					var combinedSourceTexts = content.Left.Right;
					var genPublic = content.Right;

					string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
									?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyVersionAttribute>()?.Version.ToString()
									?? Assembly.GetExecutingAssembly().GetName().ToString();
					if (version.IndexOf("+") > 0)
					{
						version = version.Substring(0, version.IndexOf("+"));
					}

					var config = new AppSettingsGenConfig
					{
						ProjectName = projectName,
						RootNamespace = rootNamespace,
						IsPublic = genPublic,
						CombinedSourceTexts = combinedSourceTexts,
						NugetVersion = version,
					};

					ExecuteGenerator(spc, config);
				});
		}


		////////////////  OLD FILE.IO WORKFLOW  Works but frowned upon for sourcegen.  Switched to SourceText
		//{
		//	var projectDirProvider = context.AnalyzerConfigOptionsProvider
		//		 .Select(static (provider, ct) =>
		//		 {
		//			 provider.GlobalOptions.TryGetValue("build_property.projectdir", out string? projectDirectory);
		//			 provider.GlobalOptions.TryGetValue("build_property.rootnamespace", out string? assemblyName);
		//			 return (projectDirectory, assemblyName);
		//		 });
		//	context.RegisterSourceOutput(
		//		projectDirProvider,
		//		 (spc, settings) =>
		//		 {
		//			 ExecuteGenerator_FileIo(spc, settings);
		//		 });
		//}
	}

	public void ExecuteGenerator(SgfSourceProductionContext spc, AppSettingsGenConfig config)
	{
		var results = GenerateSourceFiles(config);

		foreach (var result in results)
		{
			spc.AddSource(result.Key, result.Value);
		}
		Logger.Information("Source generation completed successfully with " + results.Count + " files");
	}

	/// <summary>
	/// will generate strongly typed c# classes for each matched (appsettings).json
	/// </summary>
	/// <param name="config">Configuration containing source files and generation settings</param>
	/// <returns>the "output" C#, source generated files</returns>
	public Dictionary<string, SourceText> GenerateSourceFiles(AppSettingsGenConfig config)
	{

		var toReturn = new Dictionary<string, SourceText>();

		//if (rootNamespace is null)
		//{
		//	diagReport._Error($"missing required inputs. rootNamespace={rootNamespace}");
		//	return toReturn;
		//}
		if (config.CombinedSourceTexts.Count == 0)
		{
			Logger.Error($"No appSettings.json files were found in your project `{config.ProjectName}`. SourceGen aborted. In Project Properties, Make sure it's BuildAction=C# Analyzer, and copy-to-output=ALWAYS.");
			return toReturn;
		}

		// DETERMINISTIC_ORDERING + DIAGNOSTIC_VISIBILITY: emit the sorted file list so build logs show
		// exactly which appsettings*.json participated and in which merge order (REQ-3 closure, TDD §4.B2).
		// `fileCount=N` token retained for any grep-based test backward-compat.
		var sortedFileList = string.Join(", ", config.CombinedSourceTexts.Keys.OrderBy(p => p, System.StringComparer.Ordinal));
		Logger.Information($"Processing source generation: rootNamespace={config.RootNamespace}, fileCount={config.CombinedSourceTexts.Count}, files=[{sortedFileList}]");



		//merge into one big json
		var allJsonDict = JsonMerger.MergeJsonFiles(config.CombinedSourceTexts);
		var whitelistPolicy = ClientWhitelistPolicy.FromMergedJson(allJsonDict);
		var appSettingsJson = RemoveGeneratorMetadataNodes(allJsonDict);
		var clientSettingsJson = whitelistPolicy.HasWhitelist
			? BuildClientSettingsJson(appSettingsJson, whitelistPolicy)
			: appSettingsJson;

		//generate classes for the entire json hiearchy
		GenerateFilesWorker(toReturn, appSettingsJson, "AppSettings", $"{config.StartingNamespace}", config);
		GenerateFilesWorker(toReturn, clientSettingsJson, "_ClientAppSettings", $"{config.StartingNamespace}", config, whitelistPolicy);

		AddClientSettingsAttributeShims(toReturn, config);
		AddBinderShims(toReturn, config);

		return toReturn;

	}

	/// <summary>
	/// add helper service to automatically populate appsettings from disk
	/// </summary>
	/// <param name="toReturn">Dictionary to add generated source files to</param>
	/// <param name="config">Configuration for the generation</param>
	private void AddBinderShims(Dictionary<string, SourceText> toReturn, AppSettingsGenConfig config)
	{
		var builder = new StringBuilder();
		builder.Append(@$"
#pragma warning disable
/** 
 * This file is generated by the NotNot.AppSettings nuget package (v{config.NugetVersion}).
 * Do not edit this file directly, instead edit the appsettings.json files and rebuild the project.
 * `AddBinderShims()` was called.
**/

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using System.CodeDom.Compiler;
using System.Text.Json.Nodes;

namespace {config.StartingNamespace}
{{

	/// <summary>
	/// Strongly typed AppSettings.json, recreated every build.
	/// <para>You can use this directly, extend it (it's a partial class),
	/// or get a populated instance of it via the <see cref=""AppSettingsBinder""/> DI service</para>
	/// </summary>
	{config.GenAccessModifier} partial class AppSettings
	{{
	}}

	/// <summary>
	/// a DI service that contains a strongly-typed copy of your appsettings.json
	/// <para><strong>DI Usage:</strong></para>
	/// <para><c>builder.Services.AddSingleton&lt;IAppSettingsBinder, AppSettingsBinder&gt;();</c></para>
	/// <para><c>var app = builder.Build();</c></para>
	///  <para><c>var appSettings = app.Services.GetRequiredService&lt;IAppSettingsBinder&gt;().AppSettings;</c></para>
	/// <para><strong>Non-DI Usage:</strong></para>
	/// <para><c>var appSettings = AppSettingsBinder.LoadDirect();</c></para>
	/// </summary>
	{config.GenAccessModifier} partial class AppSettingsBinder : IAppSettingsBinder
	{{
		public AppSettings AppSettings {{ get; protected set; }}

		public AppSettingsBinder(IConfiguration _config)
		{{
			AppSettings = new AppSettings();

			//automatically reads and binds to config file
			_config.Bind(AppSettings);
		}}

		// ============================================================================================
		// FACADE HELPERS (emitted once per consumer assembly; Phase D of Option C implementation).
		// LoadDirect* methods are [Obsolete] and route through NotNot.AppSettingsHelper.JsonSettingsUtils
		// (from NotNot.Bcl.Core) to share deep-merge + null-delete + array-REPLACE semantics with
		// NotNot.Storage.SimpleStorageManager<T>. See TDD §4.D for the unification contract.
		// ============================================================================================

		/// <summary>
		/// FACADE HELPER: walks a merged JsonNode into a flat colon-delimited dictionary suitable for
		/// Microsoft.Extensions.Configuration.AddInMemoryCollection. Bridges unified-merge output to
		/// IConfiguration.Bind (preserves ASP.NET Core binding semantics: env-var substitution,
		/// enum parsing, nullability, type coercion).
		/// </summary>
		private static System.Collections.Generic.Dictionary<string, string?> _FlattenJson(global::System.Text.Json.Nodes.JsonNode? node, string prefix = """")
		{{
			var result = new System.Collections.Generic.Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase);
			if (node is not global::System.Text.Json.Nodes.JsonObject obj) return result;
			foreach (var prop in obj)
			{{
				var key = string.IsNullOrEmpty(prefix) ? prop.Key : $""{{prefix}}:{{prop.Key}}"";
				if (prop.Value is global::System.Text.Json.Nodes.JsonObject nested)
				{{
					foreach (var kvp in _FlattenJson(nested, key))
					{{
						result[kvp.Key] = kvp.Value;
					}}
				}}
				else if (prop.Value is global::System.Text.Json.Nodes.JsonArray arr)
				{{
					// IConfiguration array convention: indexed keys ""Name:0"", ""Name:1"", ...
					for (int i = 0; i < arr.Count; i++)
					{{
						var item = arr[i];
						var arrKey = $""{{key}}:{{i}}"";
						if (item is global::System.Text.Json.Nodes.JsonObject itemObj)
						{{
							foreach (var kvp in _FlattenJson(itemObj, arrKey))
							{{
								result[kvp.Key] = kvp.Value;
							}}
						}}
						else
						{{
							result[arrKey] = item?.ToString();
						}}
					}}
				}}
				else
				{{
					result[key] = prop.Value?.ToString();
				}}
			}}
			return result;
		}}

		/// <summary>
		/// FACADE HELPER: binds a merged JsonNode to a new AppSettings instance via IConfiguration.Bind.
		/// </summary>
		private static AppSettings _BindMergedNode(global::System.Text.Json.Nodes.JsonNode? merged)
		{{
			var flat = _FlattenJson(merged);
			var configBuilder = new ConfigurationBuilder();
			configBuilder.AddInMemoryCollection(flat);
			IConfigurationRoot configuration = configBuilder.Build();
			var binder = new AppSettingsBinder(configuration);
			return binder.AppSettings;
		}}

		/// <summary>
		/// [Obsolete facade] Manually construct an AppSettings from your appsettings.json files.
		/// Routes through the unified JSON merge core (deep-merge objects, REPLACE arrays,
		/// null-literal DELETES key per RFC-7396) via <see cref=""LoadDirectFromStreams""/>.
		/// <para>Prefer <c>NotNot.Storage.SimpleStorageManager&lt;T&gt;</c> (with an <c>IStorageAdapter</c> such as <c>FileStorageAdapter</c>) for new code that needs runtime load/save.</para>
		/// <para>NOTE: This method is provided for non-DI users.  If you use DI, don't use this method.  Instead just register this class as a service.</para>
		/// </summary>
		/// <param name=""appSettingsLocation"">folder where to search for appsettings.json.  defaults to current app folder.</param>
		/// <param name=""appSettingsFileNames"">lets you override the files to load up.  defaults to 'appsettings.json' and 'appsettings.{{DOTNET_ENVIRONMENT}}.json'</param>
		/// <param name=""throwIfFilesMissing"">default is to silently ignore if any of the .json files are missing.</param>
		/// <returns>your strongly typed appsettings with values from your .json loaded in</returns>
		[System.Obsolete(""Use NotNot.Storage.SimpleStorageManager<T> for runtime settings load/save. LoadDirect* is preserved as a facade over the unified merge core; API signatures unchanged. Future versions may remove."", error: false)]
		public static AppSettings LoadDirect(string? appSettingsLocation = null, IEnumerable<string>? appSettingsFileNames = null, bool throwIfFilesMissing = false)
		{{
			//pick what .json files to load
			if (appSettingsFileNames is null)
			{{
				//figure out what env
				var env = System.Environment.GetEnvironmentVariable(""DOTNET_ENVIRONMENT"");
				env ??= System.Environment.GetEnvironmentVariable(""ASPNETCORE_ENVIRONMENT"");
				env ??= System.Environment.GetEnvironmentVariable(""ENVIRONMENT"");
				if (env is null)
				{{
					appSettingsFileNames = new[] {{ ""appsettings.json"" }};
				}}
				else
				{{
					appSettingsFileNames = new[] {{ ""appsettings.json"", $""appsettings.{{env}}.json"" }};
				}}
			}}

			// Resolve file paths to streams and delegate to LoadDirectFromStreams (unified merge core).
			var streams = new System.Collections.Generic.List<Stream>();
			try
			{{
				foreach (var fileName in appSettingsFileNames)
				{{
					var fullPath = appSettingsLocation != null ? Path.Combine(appSettingsLocation, fileName) : fileName;
					if (File.Exists(fullPath))
					{{
						streams.Add(File.OpenRead(fullPath));
					}}
					else if (throwIfFilesMissing)
					{{
						throw new FileNotFoundException($""appsettings file not found: {{fullPath}}"", fullPath);
					}}
				}}
				return LoadDirectFromStreams(streams);
			}}
			finally
			{{
				foreach (var s in streams) s.Dispose();
			}}
		}}

		/// <summary>
		/// [Obsolete facade] Create an AppSettings from a single string of JSON.
		/// Delegates to <see cref=""LoadDirectFromTexts""/>, which routes through the unified
		/// JSON merge core (deep-merge, REPLACE arrays, null-delete per RFC-7396).
		/// <para>Prefer <c>NotNot.Storage.SimpleStorageManager&lt;T&gt;</c> (with an <c>IStorageAdapter</c> such as <c>FileStorageAdapter</c>) for new code that needs runtime load/save.</para>
		/// </summary>
		/// <param name=""appSettingsJsonText"">The JSON text to bind.</param>
		/// <returns>A strongly-typed AppSettings populated from the JSON text.</returns>
		[System.Obsolete(""Use NotNot.Storage.SimpleStorageManager<T> for runtime settings load/save. LoadDirect* is preserved as a facade over the unified merge core; API signatures unchanged. Future versions may remove."", error: false)]
		public static AppSettings LoadDirectFromText(string appSettingsJsonText)
		{{
			// Single-text overload delegates to multi-text path for unified merge semantics.
			return LoadDirectFromTexts(appSettingsJsonText);
		}}

		/// <summary>
		/// [Obsolete facade] Create an AppSettings from multiple JSON text sources (merged layered, last-wins).
		/// Converts each text to a MemoryStream and delegates to <see cref=""LoadDirectFromStreams""/>, which
		/// routes through the unified JSON merge core (deep-merge objects, REPLACE arrays, null-literal
		/// DELETES key per RFC-7396).
		/// <para>Prefer <c>NotNot.Storage.SimpleStorageManager&lt;T&gt;</c> (with an <c>IStorageAdapter</c> such as <c>FileStorageAdapter</c>) for new code that needs runtime load/save.</para>
		/// </summary>
		/// <param name=""appSettingsJsonTexts"">JSON text sources, in ascending priority order (last wins).</param>
		/// <returns>A strongly-typed AppSettings populated from the merged JSON.</returns>
		[System.Obsolete(""Use NotNot.Storage.SimpleStorageManager<T> for runtime settings load/save. LoadDirect* is preserved as a facade over the unified merge core; API signatures unchanged. Future versions may remove."", error: false)]
		public static AppSettings LoadDirectFromTexts(params string[] appSettingsJsonTexts)
		{{
			// Convert each text to a MemoryStream and delegate to LoadDirectFromStreams (unified merge core).
			var streams = new System.Collections.Generic.List<Stream>();
			try
			{{
				foreach (var text in appSettingsJsonTexts)
				{{
					streams.Add(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty)));
				}}
				return LoadDirectFromStreams(streams);
			}}
			finally
			{{
				foreach (var s in streams) s.Dispose();
			}}
		}}

		/// <summary>
		/// [Obsolete facade] Create an AppSettings from a list of streams containing your JSON.
		/// CANONICAL FACADE — all other <c>LoadDirect*</c> overloads route through this method.
		/// Routes through <c>NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync</c>
		/// (unified merge core from <c>NotNot.Bcl.Core</c>) for deep-merge objects,
		/// REPLACE-arrays, and null-literal DELETES key per RFC-7396 (REQ-4, REQ-7).
		/// <para>Prefer <c>NotNot.Storage.SimpleStorageManager&lt;T&gt;</c> (with an <c>IStorageAdapter</c> such as <c>FileStorageAdapter</c>) for new code that needs runtime load/save.</para>
		/// </summary>
		/// <param name=""appSettingsStreams"">Streams to merge, in ascending priority order (last wins).</param>
		/// <returns>A strongly-typed AppSettings populated from the merged streams.</returns>
		[System.Obsolete(""Use NotNot.Storage.SimpleStorageManager<T> for runtime settings load/save. LoadDirect* is preserved as a facade over the unified merge core; API signatures unchanged. Future versions may remove."", error: false)]
		public static AppSettings LoadDirectFromStreams(List<Stream> appSettingsStreams)
		{{
			// Route through unified merge core (JsonSettingsUtils in NotNot.Bcl.Core).
			var merged = global::NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync(appSettingsStreams).GetAwaiter().GetResult();
			return _BindMergedNode(merged);
		}}

	}}

	/// <summary>
	/// a DI service that contains a strongly-typed copy of your appsettings.json
	/// <para><strong>DI Usage:</strong></para>
	/// <para><c>builder.Services.AddSingleton&lt;IAppSettingsBinder, AppSettingsBinder&gt;();</c></para>
	/// <para><c>var app = builder.Build();</c></para>
	///  <para><c>var appSettings = app.Services.GetRequiredService&lt;IAppSettingsBinder&gt;().AppSettings;</c></para>
	/// <para><strong>Non-DI Usage:</strong></para>
	/// <para><c>var appSettings = AppSettingsBinder.LoadDirect();</c></para>
	/// </summary>
	{config.GenAccessModifier} interface IAppSettingsBinder
	{{
		public AppSettings AppSettings {{ get; }}
	}}
}} //end namespace

/// <summary>
/// An extension method to easily obtain the AppSettings object from the builder.Configuration (IConfiguration).
/// </summary>
internal static class zz_AppSettingsExtensions_IConfiguration
{{
    private static {config.StartingNamespace}.AppSettings? _cachedAppSettings;


    /// <summary>
    /// Obtain NotNot.AppSettings (strongly typed appsettings.json)
    /// </summary>
    /// <param name=""configuration"">builder.Configuration</param>
    /// <param name=""ignoreCache"">true to recreate the AppSettings even if it's already been created</param>
    [System.Obsolete(""Use NotNot.Storage.SimpleStorageManager<AppSettings> with a NotNot.Storage.IStorageAdapter (e.g. FileStorageAdapter) for runtime settings load/save."")]
    internal static {config.StartingNamespace}.AppSettings _AppSettings(this IConfiguration configuration, bool ignoreCache=false)
    {{
        if (ignoreCache == false && _cachedAppSettings is not null)
        {{
            return _cachedAppSettings;
        }}
        var appSettingsBinder = new {config.StartingNamespace}.AppSettingsBinder(configuration);
        _cachedAppSettings = appSettingsBinder.AppSettings;
        return _cachedAppSettings;
    }}
}}







");

		var source = SourceText.From(builder.ToString(), Encoding.UTF8);
		toReturn.Add("_BinderShims.g.cs", source);

	}

	private void AddClientSettingsAttributeShims(Dictionary<string, SourceText> toReturn, AppSettingsGenConfig config)
	{
		var builder = new StringBuilder();
		builder.Append(@$"
#pragma warning disable
/**
 * This file is generated by the NotNot.AppSettings nuget package (v{config.NugetVersion}).
 * Do not edit this file directly, instead edit the appsettings.json files and rebuild the project.
 * `AddClientSettingsAttributeShims()` was called.
**/

using System;
using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace NotNot.AppSettings;

[CompilerGenerated]
[GeneratedCode(""{Assembly.GetExecutingAssembly().GetName().Name}"",""{Assembly.GetExecutingAssembly().GetName().Version.ToString()}"" )]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ClientReadAttribute : Attribute
{{
}}

[CompilerGenerated]
[GeneratedCode(""{Assembly.GetExecutingAssembly().GetName().Name}"",""{Assembly.GetExecutingAssembly().GetName().Version.ToString()}"" )]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ClientWriteLocalAttribute : Attribute
{{
}}

[CompilerGenerated]
[GeneratedCode(""{Assembly.GetExecutingAssembly().GetName().Name}"",""{Assembly.GetExecutingAssembly().GetName().Version.ToString()}"" )]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ClientWriteServerAttribute : Attribute
{{
}}
");

		toReturn["NotNot.AppSettings.ClientSettingsAttributes.g.cs"] = SourceText.From(builder.ToString(), Encoding.UTF8);
	}

	private enum ClientSettingAccess
	{
		None = 0,
		ServerOnly,
		ClientRead,
		ClientWriteLocal,
		ClientWriteServer,
	}

	private sealed class ClientWhitelistPolicy
	{
		private readonly Dictionary<string, ClientSettingAccess> _policies;

		private ClientWhitelistPolicy(Dictionary<string, ClientSettingAccess> policies)
		{
			_policies = policies;
		}

		public bool HasWhitelist => _policies.Count > 0;

		public static ClientWhitelistPolicy FromMergedJson(Dictionary<string, JsonElement> mergedJson)
		{
			if (!mergedJson.TryGetValue("NotNotAppSettings", out var metadataRoot)
				|| metadataRoot.ValueKind != JsonValueKind.Object
				|| !metadataRoot.TryGetProperty("whitelist", out var whitelistNode)
				|| whitelistNode.ValueKind != JsonValueKind.Object)
			{
				return new ClientWhitelistPolicy(new Dictionary<string, ClientSettingAccess>(StringComparer.OrdinalIgnoreCase));
			}

			var policies = new Dictionary<string, ClientSettingAccess>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in whitelistNode.EnumerateObject())
			{
				if (entry.Value.ValueKind != JsonValueKind.String)
				{
					continue;
				}

				var normalizedPath = NormalizePath(entry.Name);
				if (TryParseAccess(entry.Value.GetString(), out var access))
				{
					policies[normalizedPath] = access;
				}
			}

			return new ClientWhitelistPolicy(policies);
		}

		public ClientSettingAccess GetEffectiveAccess(string path)
		{
			if (!HasWhitelist)
			{
				return ClientSettingAccess.None;
			}

			var currentPath = NormalizePath(path);
			while (!string.IsNullOrEmpty(currentPath))
			{
				if (_policies.TryGetValue(currentPath, out var access))
				{
					return access;
				}

				var separatorIndex = currentPath.LastIndexOf(':');
				if (separatorIndex < 0)
				{
					break;
				}

				currentPath = currentPath.Substring(0, separatorIndex);
			}

			return ClientSettingAccess.ServerOnly;
		}

		public string? GetAttributeTypeName(string path)
		{
			if (!HasWhitelist)
			{
				return null;
			}

			return GetEffectiveAccess(path) switch
			{
				ClientSettingAccess.ClientRead => "global::NotNot.AppSettings.ClientReadAttribute",
				ClientSettingAccess.ClientWriteLocal => "global::NotNot.AppSettings.ClientWriteLocalAttribute",
				ClientSettingAccess.ClientWriteServer => "global::NotNot.AppSettings.ClientWriteServerAttribute",
				_ => null,
			};
		}

		private static bool TryParseAccess(string? value, out ClientSettingAccess access)
		{
			switch (value?.Trim())
			{
				case "ServerOnly":
					access = ClientSettingAccess.ServerOnly;
					return true;
				case "ClientRead":
					access = ClientSettingAccess.ClientRead;
					return true;
				case "ClientWriteLocal":
					access = ClientSettingAccess.ClientWriteLocal;
					return true;
				case "ClientWriteServer":
					access = ClientSettingAccess.ClientWriteServer;
					return true;
				default:
					access = ClientSettingAccess.None;
					return false;
			}
		}
	}

	private static Dictionary<string, JsonElement> RemoveGeneratorMetadataNodes(Dictionary<string, JsonElement> currentNode)
	{
		var filtered = new Dictionary<string, JsonElement>(currentNode.Count, StringComparer.Ordinal);
		foreach (var kvp in currentNode)
		{
			if (string.Equals(kvp.Key, "NotNotAppSettings", StringComparison.Ordinal))
			{
				continue;
			}

			filtered[kvp.Key] = kvp.Value;
		}

		return filtered;
	}

	private static Dictionary<string, JsonElement> BuildClientSettingsJson(Dictionary<string, JsonElement> currentNode, ClientWhitelistPolicy whitelistPolicy, string currentPath = "")
	{
		var filtered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		var includedPropertyNames = new HashSet<string>(StringComparer.Ordinal);

		foreach (var kvp in currentNode)
		{
			if (IsMetadataKey(kvp.Key))
			{
				continue;
			}

			var propertyPath = CombinePath(currentPath, kvp.Key);
			var directAccess = whitelistPolicy.GetEffectiveAccess(propertyPath);
			var includeDirectly = directAccess != ClientSettingAccess.ServerOnly;

			if (kvp.Value.ValueKind == JsonValueKind.Object && !IsDictionaryTypeObject(kvp.Value))
			{
				var childNode = kvp.Value.Deserialize<Dictionary<string, JsonElement>>(JsonMerger._serializerOptions)!;
				var filteredChild = BuildClientSettingsJson(childNode, whitelistPolicy, propertyPath);
				if (includeDirectly || filteredChild.Count > 0)
				{
					filtered[kvp.Key] = JsonSerializer.SerializeToElement(filteredChild, JsonMerger._serializerOptions);
					includedPropertyNames.Add(kvp.Key);
				}
			}
			else if (includeDirectly)
			{
				filtered[kvp.Key] = kvp.Value;
				includedPropertyNames.Add(kvp.Key);
			}
		}

		foreach (var propertyName in includedPropertyNames)
		{
			if (currentNode.TryGetValue($"{propertyName}__min", out var minValue))
			{
				filtered[$"{propertyName}__min"] = minValue;
			}
			if (currentNode.TryGetValue($"{propertyName}__max", out var maxValue))
			{
				filtered[$"{propertyName}__max"] = maxValue;
			}
		}

		if (filtered.Count > 0 && currentNode.TryGetValue("__type", out var typeValue))
		{
			filtered["__type"] = typeValue;
		}

		return filtered;
	}

	private static bool IsMetadataKey(string key)
	{
		return key.EndsWith("__min", StringComparison.Ordinal)
			|| key.EndsWith("__max", StringComparison.Ordinal)
			|| key == "__type";
	}

	private static bool IsDictionaryTypeObject(JsonElement value)
	{
		return value.ValueKind == JsonValueKind.Object
			&& value.TryGetProperty("__type", out var typeMetaElm)
			&& typeMetaElm.GetString() == "dictionary";
	}

	private static string NormalizePath(string path)
	{
		return path.Trim().Trim(':');
	}

	private static string CombinePath(string currentPath, string nextSegment)
	{
		return string.IsNullOrEmpty(currentPath) ? nextSegment : $"{currentPath}:{nextSegment}";
	}

	private static string GetGeneratedTypeName(string nodeName)
	{
		var trimmed = nodeName?.Trim() ?? string.Empty;
		if (trimmed.Length == 0)
		{
			return "Unnamed";
		}

		var preserveLeadingUnderscore = trimmed.StartsWith("_", StringComparison.Ordinal);
		var normalized = trimmed.TrimStart('_')._ConvertToAlphanumericCaps();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			normalized = "Unnamed";
		}

		return preserveLeadingUnderscore ? $"_{normalized}" : normalized;
	}

	private static string GetChildNamespace(string currentNamespace, string currentClassName)
	{
		return currentClassName.StartsWith("_", StringComparison.Ordinal)
			? $"{currentNamespace}.{currentClassName}Types"
			: $"{currentNamespace}._{currentClassName}";
	}

	/// <summary>
	/// get the c# type of the element.  however keep in mind that arrays won't include the '[]'.  Check if array via `elm.ValueKind == JsonValueKind.Array`
	/// </summary>
	/// <param name="elm"></param>
	/// <param name="currentName"></param>
	/// <param name="currentNamespace"></param>
	/// <returns>returns name of json primitive, "object" for null/undefined nodes,  named nodes for other json objects</returns>
	/// <exception cref="ArgumentException"></exception>
	public string GetSourceTypeName(JsonElement elm, string currentName, string currentNamespace, AppSettingsGenConfig config)
	{
		string toReturn;
		switch (elm.ValueKind)
		{
			case JsonValueKind.String:
				toReturn = "string";
				break;
			case JsonValueKind.Number:
				toReturn = "double";
				break;
			case JsonValueKind.True:
			case JsonValueKind.False:
				toReturn = "bool";
				break;
			case JsonValueKind.Null:
			case JsonValueKind.Undefined:
				toReturn = "object";
				break;
			case JsonValueKind.Object:
				toReturn = $"{currentNamespace}.{currentName}";
				break;
			case JsonValueKind.Array:
				//unify all children into one type
				//if mix of various types (such as object+primitive, or different primitive types), will return back "object" and user will have to cast manually.
				string? unifiedChildType = null;
				foreach (var child in elm.EnumerateArray())
				{
					var childType = GetSourceTypeName(child, currentName, currentNamespace, config);
					if (unifiedChildType is null)
					{
						unifiedChildType = childType;
					}
					else if (unifiedChildType != childType)
					{
						unifiedChildType = "object";
						break;
					}
				}
				unifiedChildType ??= "object";
				toReturn = unifiedChildType;
				break;
			default:
				throw new ArgumentException($"unknown type returned from json, {elm}", nameof(elm));
		}

		return toReturn;
	}

	/// <summary>
	/// Checks if a type name represents a primitive JSON type.
	/// </summary>
	private static bool IsPrimitiveTypeName(string typeName)
	{
		return typeName == "string" || typeName == "double" || typeName == "bool" || typeName == "object";
	}

	/// <summary>
	/// generate files for the given json hierarchy, recursively calling itself for each child node
	/// </summary>
	private void GenerateFilesWorker(Dictionary<string, SourceText> generatedSourceFiles, Dictionary<string, JsonElement> currentNode, string currentNodeName, string currentNamespace, AppSettingsGenConfig config, ClientWhitelistPolicy? clientWhitelistPolicy = null, string currentPath = "")
	{
		//build currentNode into file
		var currentClassName = GetGeneratedTypeName(currentNodeName);
		var filename = $"{currentNamespace}.{currentClassName}.g.cs";

		var fieldBuilder = new StringBuilder();
		var propertyBuilder = new StringBuilder();
		var propagateCallbackBuilder = new StringBuilder();

		// Extract __min/__max metadata before property generation
		var metadataLookup = new Dictionary<string, (double? min, double? max)>(StringComparer.Ordinal);
		foreach (var kvp in currentNode)
		{
			if (kvp.Key.EndsWith("__min", StringComparison.Ordinal) && kvp.Value.ValueKind == JsonValueKind.Number)
			{
				var baseName = kvp.Key.Substring(0, kvp.Key.Length - 5);
				if (!metadataLookup.TryGetValue(baseName, out var existing))
					existing = (null, null);
				metadataLookup[baseName] = (kvp.Value.GetDouble(), existing.max);
			}
			else if (kvp.Key.EndsWith("__max", StringComparison.Ordinal) && kvp.Value.ValueKind == JsonValueKind.Number)
			{
				var baseName = kvp.Key.Substring(0, kvp.Key.Length - 5);
				if (!metadataLookup.TryGetValue(baseName, out var existing))
					existing = (null, null);
				metadataLookup[baseName] = (existing.min, kvp.Value.GetDouble());
			}
		}

		foreach (var kvp in currentNode)
		{
			// Skip metadata keys - they're not properties
			if (kvp.Key.EndsWith("__min", StringComparison.Ordinal) || kvp.Key.EndsWith("__max", StringComparison.Ordinal) || kvp.Key == "__type")
				continue;

			var propertyName = kvp.Key._ConvertToAlphanumericCaps();
			var fieldName = "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
			var propertyNamespace = GetChildNamespace(currentNamespace, currentClassName);
			var propertyPath = CombinePath(currentPath, kvp.Key);
			var valueType = GetSourceTypeName(kvp.Value, propertyName, propertyNamespace, config);
			var isArray = kvp.Value.ValueKind == JsonValueKind.Array;

			// Check for __type: "dictionary" convention on JSON objects
			var isDictionaryType = false;
			if (kvp.Value.ValueKind == JsonValueKind.Object
				&& kvp.Value.TryGetProperty("__type", out var typeMetaElm)
				&& typeMetaElm.GetString() == "dictionary")
			{
				// Unify value types of all non-metadata children
				string? dictValueType = null;
				foreach (var child in kvp.Value.EnumerateObject())
				{
					if (child.Name.StartsWith("__", StringComparison.Ordinal)) continue;
					var childType = GetSourceTypeName(child.Value, propertyName, propertyNamespace, config);
					if (dictValueType is null)
						dictValueType = childType;
					else if (dictValueType != childType)
					{
						dictValueType = "object";
						break;
					}
				}
				dictValueType ??= "string";
				valueType = $"System.Collections.Generic.Dictionary<string, {dictValueType}>";
				isDictionaryType = true;
			}

			if (isArray)
			{
				valueType += "[]";
			}

			// Generate backing field
			fieldBuilder.Append($"   private {valueType}? {fieldName};\n");

			// Check if this is a complex type (object/nested class) that can propagate callbacks
			var isComplexType = kvp.Value.ValueKind == JsonValueKind.Object && !isDictionaryType;
			var isArrayOfComplexType = isArray && !IsPrimitiveTypeName(GetSourceTypeName(kvp.Value, propertyName, propertyNamespace, config));

			// Check for min/max metadata for this property
			metadataLookup.TryGetValue(kvp.Key, out var propMeta);
			var hasMin = propMeta.min.HasValue;
			var hasMax = propMeta.max.HasValue;
			var isNumericType = valueType == "double";

			// Generate property with change detection
			var attributeTypeName = clientWhitelistPolicy?.GetAttributeTypeName(propertyPath);
			if (!string.IsNullOrWhiteSpace(attributeTypeName))
			{
				propertyBuilder.Append($@"
   [{attributeTypeName}]");
			}

			propertyBuilder.Append($@"
   public {valueType}? {propertyName}
   {{
      get => {fieldName};
      set
      {{");

			// For numeric types with min/max constraints, apply clamping
			if (isNumericType && (hasMin || hasMax))
			{
				if (hasMin && hasMax)
				{
					propertyBuilder.Append($@"
         var clamped = Math.Max({propMeta.min!}, Math.Min({propMeta.max!}, value ?? {propMeta.min!}));");
				}
				else if (hasMin)
				{
					propertyBuilder.Append($@"
         var clamped = Math.Max({propMeta.min!}, value ?? {propMeta.min!});");
				}
				else // hasMax only
				{
					propertyBuilder.Append($@"
         var clamped = Math.Min({propMeta.max!}, value ?? 0.0);");
				}

				propertyBuilder.Append($@"
         if (!Equals({fieldName}, clamped))
         {{
            {fieldName} = clamped;");
			}
			else
			{
				// No clamping - use original logic
				propertyBuilder.Append($@"
         if (!Equals({fieldName}, value))
         {{
            {fieldName} = value;");
			}

			// For complex types, propagate the callback to nested objects
			if (isComplexType)
			{
				propertyBuilder.Append($@"
            (value as global::NotNot.AppSettingsHelper.ISettingsChangeAware)?._SetChangeCallback(_onChanged);");
			}
			else if (isArrayOfComplexType)
			{
				propertyBuilder.Append($@"
            if (value != null)
            {{
               foreach (var item in value)
               {{
                  (item as global::NotNot.AppSettingsHelper.ISettingsChangeAware)?._SetChangeCallback(_onChanged);
               }}
            }}");
			}

			propertyBuilder.Append($@"
            _onChanged?.Invoke();
         }}
      }}
   }}
");

			// Add propagation for _SetChangeCallback (complex types only)
			if (isComplexType)
			{
				propagateCallbackBuilder.Append($@"
         ({fieldName} as global::NotNot.AppSettingsHelper.ISettingsChangeAware)?._SetChangeCallback(callback);");
			}
			else if (isArrayOfComplexType)
			{
				propagateCallbackBuilder.Append($@"
         if ({fieldName} != null)
         {{
            foreach (var item in {fieldName})
            {{
               (item as global::NotNot.AppSettingsHelper.ISettingsChangeAware)?._SetChangeCallback(callback);
            }}
         }}");
			}
		}

		// Build interface (always generated, in separate .Interfaces namespace)
		var interfaceName = $"I{currentClassName}";
		var interfaceNamespace = $"{currentNamespace}.Interfaces";
		var interfaceFullName = $"global::{interfaceNamespace}.{interfaceName}";

		var interfaceSourceBuilder = new StringBuilder();
		interfaceSourceBuilder.Append($@"
#pragma warning disable
/**
 * This file is generated by the NotNot.AppSettings nuget package  (v{config.NugetVersion}).
 * Do not edit this file directly, instead edit the appsettings.json files and rebuild the project.
 * Interface generated for {currentNodeName}
**/
using System;
using System.Runtime.CompilerServices;
using System.CodeDom.Compiler;
namespace {interfaceNamespace};

/// <summary>
/// Interface for {currentClassName}. Use with SimpleStorageManager and an IStorageAdapter for change detection.
/// </summary>
/// <remarks>
/// <para>This interface enables the SimpleStorageManager + IStorageAdapter workflow for settings change detection.</para>
/// <para>Nested properties use concrete types (C# property invariance constraint).</para>
/// </remarks>
[CompilerGenerated]
[GeneratedCode(""{Assembly.GetExecutingAssembly().GetName().Name}"",""{Assembly.GetExecutingAssembly().GetName().Version.ToString()}"")]
{config.GenAccessModifier} interface {interfaceName}
{{");
		// Add interface properties - same signatures as class properties
		foreach (var kvp in currentNode)
		{
			// Skip metadata keys
			if (kvp.Key.EndsWith("__min", StringComparison.Ordinal) || kvp.Key.EndsWith("__max", StringComparison.Ordinal) || kvp.Key == "__type")
				continue;

			var propName = kvp.Key._ConvertToAlphanumericCaps();
			var propNamespace = GetChildNamespace(currentNamespace, currentClassName);
			var propType = GetSourceTypeName(kvp.Value, propName, propNamespace, config);
			var isArray = kvp.Value.ValueKind == JsonValueKind.Array;

			// Check for __type: "dictionary" convention
			if (kvp.Value.ValueKind == JsonValueKind.Object
				&& kvp.Value.TryGetProperty("__type", out var typeMetaElm)
				&& typeMetaElm.GetString() == "dictionary")
			{
				string? dictValueType = null;
				foreach (var child in kvp.Value.EnumerateObject())
				{
					if (child.Name.StartsWith("__", StringComparison.Ordinal)) continue;
					var childType = GetSourceTypeName(child.Value, propName, propNamespace, config);
					if (dictValueType is null)
						dictValueType = childType;
					else if (dictValueType != childType)
					{
						dictValueType = "object";
						break;
					}
				}
				dictValueType ??= "string";
				propType = $"System.Collections.Generic.Dictionary<string, {dictValueType}>";
			}

			if (isArray)
			{
				propType += "[]";
			}

			interfaceSourceBuilder.Append($@"
   {propType}? {propName} {{ get; set; }}");
		}
		interfaceSourceBuilder.Append(@"
}
");
		// Add interface to generated files (separate file for clean namespace separation)
		var interfaceFilename = $"{currentNamespace}.Interfaces.{interfaceName}.g.cs";
		var interfaceSource = SourceText.From(interfaceSourceBuilder.ToString(), Encoding.UTF8);
		generatedSourceFiles[interfaceFilename] = interfaceSource;

		// Build class declaration - always implements the interface from .Interfaces namespace
		var classInterfaces = $"{interfaceFullName}, global::NotNot.AppSettingsHelper.ISettingsChangeAware";

		var sourceBuilder = new StringBuilder();
		sourceBuilder.Append(@$"
#pragma warning disable
/**
 * This file is generated by the NotNot.AppSettings nuget package  (v{config.NugetVersion}).
 * Do not edit this file directly, instead edit the appsettings.json files and rebuild the project.
 * `GenerateFilesWorker()` was called for {currentNodeName}
**/
using System;
using System.Runtime.CompilerServices;
using System.CodeDom.Compiler;
namespace {currentNamespace};

[CompilerGenerated]
[GeneratedCode(""{Assembly.GetExecutingAssembly().GetName().Name}"",""{Assembly.GetExecutingAssembly().GetName().Version.ToString()}"")]
{config.GenAccessModifier} partial class {currentClassName} : {classInterfaces} {{
   private global::System.Action? _onChanged;

{fieldBuilder}
{propertyBuilder}
   /// <summary>
   /// Sets the callback to be invoked when any property changes.
   /// Propagates the callback recursively to all nested settings objects.
   /// </summary>
   void global::NotNot.AppSettingsHelper.ISettingsChangeAware._SetChangeCallback(global::System.Action? callback)
   {{
      _onChanged = callback;{propagateCallbackBuilder}
   }}
}}
");

		var source = SourceText.From(sourceBuilder.ToString(), Encoding.UTF8);
		generatedSourceFiles.Add(filename, source);

		//recurse into children
		foreach (var kvp in currentNode)
		{
			// Skip metadata keys - they're not properties
			if (kvp.Key.EndsWith("__min", StringComparison.Ordinal) || kvp.Key.EndsWith("__max", StringComparison.Ordinal) || kvp.Key == "__type")
				continue;

			var propertyNamespace = GetChildNamespace(currentNamespace, currentClassName);
			var jsonKind = kvp.Value.ValueKind;
			var propertyName = kvp.Key._ConvertToAlphanumericCaps();
			var propertyPath = CombinePath(currentPath, kvp.Key);
			switch (jsonKind)
			{
				case JsonValueKind.Object:
					{
						// Skip dictionary-typed objects — they emit Dictionary<K,V>, not nested classes
						if (kvp.Value.TryGetProperty("__type", out var typeElm) && typeElm.GetString() == "dictionary")
							break;
						var childNode = kvp.Value.Deserialize<Dictionary<string, JsonElement>>(JsonMerger._serializerOptions)!;
						GenerateFilesWorker(generatedSourceFiles, childNode, propertyName, propertyNamespace, config, clientWhitelistPolicy, propertyPath);
					}
					break;
				case JsonValueKind.Array:
					{

						var childNodes = kvp.Value.Deserialize<List<JsonElement>>(JsonMerger._serializerOptions)!;
						//get name of node
						var arrayTypeName = GetSourceTypeName(kvp.Value, propertyName, propertyNamespace, config);
						switch (arrayTypeName)
						{
							case "string":
							case "double":
							case "bool":
							case "object": //returns object for null/undefined nodes.  (named nodes for other objects)
										   //no need to recurse
								break;
							default:
								//squash children into singular object then generate for it
								var squashedChildren = new Dictionary<string, JsonElement>();
								foreach (var child in childNodes)
								{
									JsonMerger.MergeJson(squashedChildren, child);
								}
								GenerateFilesWorker(generatedSourceFiles, squashedChildren, propertyName, propertyNamespace, config, clientWhitelistPolicy, propertyPath);
								break;
						}

					}
					break;
				default:
					//a "primitive" json type, so no need to recurse
					break;
			}
		}

	}


	//[Obsolete("uses File.IO to read.  Works but frowned upon for sourcegen.  Switched to SourceText", true)]
	//public void ExecuteGenerator_FileIo(SourceProductionContext spc, (string? projectDirectory, string? startingNamespace) settings)
	//{
	//	var diagReports = new List<Diagnostic>();
	//	var results = GenerateSourceFiles_FileIo(settings, diagReports);
	//	foreach (var report in diagReports)
	//	{
	//		spc.ReportDiagnostic(report);
	//	}
	//	foreach (var result in results)
	//	{
	//		spc.AddSource(result.Key, result.Value);
	//	}
	//	spc._Info("done");
	//}


	///// <summary>
	///// for the given fileSearchPattern, will generate strongly typed c# classes for each matched (appsettings).json file found in the projectDirectory
	///// </summary>
	///// <param name="settings"></param>
	///// <param name="diagReport">helper for accumulating diag messages.  caller should relay them to appropriate log writer afterwards.</param>
	///// <param name="fileSearchPattern">defaults to "appsettings*.json"</param>
	///// <returns></returns>
	//[Obsolete("uses File.IO to read.  Works but frowned upon for sourcegen.  Switched to SourceText", true)]
	//public Dictionary<string, SourceText> GenerateSourceFiles_FileIo((string? projectDirectory
	//	, string? startingNamespace) settings, List<Diagnostic> diagReport
	//	, string fileSearchPattern = "appsettings*.json")
	//{
	//	var (projectDir, startingNamespace) = settings;
	//	var toReturn = new Dictionary<string, SourceText>();
	//	if (projectDir is null || startingNamespace is null)
	//	{
	//		diagReport._Error($"null required inputs  projectDir={projectDir}, startingNamespace={startingNamespace}");
	//		return toReturn;
	//	}
	//	else
	//	{
	//		diagReport._Info($"projectDir {projectDir} ");
	//	}
	//	startingNamespace = $"{startingNamespace}.AppSettingsGen";
	//	//do stuff with project dir
	//	var dir = new DirectoryInfo(projectDir);
	//	var files = dir.EnumerateFiles(fileSearchPattern, SearchOption.TopDirectoryOnly).ToList();
	//	diagReport._Info($"files count {files.Count()} ");
	//	//merge into one big json
	//	var allJsonDict = JsonMerger.MergeJsonFiles(files, diagReport);
	//	//generate classes for the entire json hiearchy
	//	GenerateFilesWorker(diagReport, toReturn, allJsonDict, "AppSettings", $"{startingNamespace}");
	//	AddBinderShims(diagReport, toReturn, startingNamespace);
	//	return toReturn;
	//}

}
