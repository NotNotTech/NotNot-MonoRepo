using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;

namespace NotNot.AppSettingsInternal;

/// <summary>
/// helper to merge json files into a single object
/// </summary>
internal static class JsonMerger
{
	 // Delegates to the shared-source JsonMergeCore canonical options so that build-time (here)
	 // and runtime (JsonSettingsUtils.MergeStreamsAsync in NotNot.Bcl.Core) parse with identical
	 // lenient settings (AllowTrailingCommas + CommentHandling.Skip). Phase-5 H2 closure.
	 public static JsonDocumentOptions _options => JsonMergeCore.DefaultDocumentOptions;
	 public static JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
	 {
		  ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
		  AllowTrailingCommas = true,
		  PropertyNameCaseInsensitive = true,
		  PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		  MaxDepth = 10,
		  NumberHandling = JsonNumberHandling.AllowReadingFromString,
		  ReferenceHandler = ReferenceHandler.IgnoreCycles,
		  UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
		  UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
		  WriteIndented = true,
	 };

	 public static Dictionary<string, JsonElement> MergeJsonFiles(Dictionary<string, SourceText> sourceTexts)
	 {
		  // DETERMINISTIC_ORDERING: sort file entries by ordinal path so generator output is stable
		  // regardless of AdditionalTextsProvider enumeration order (REQ-3 closure, TDD §4.B1).
		  var sorted = sourceTexts.OrderBy(pair => pair.Key, System.StringComparer.Ordinal).ToList();

		  // BOUNDARY_CONVERSION: parse each SourceText directly into JsonNode and fold via JsonMergeCore.
		  // Contract delta vs. prior behavior: arrays are REPLACED (later file wins) instead of concatenated.
		  // This is the REQ-4-aligned semantics; JsonMerger is `internal static` with zero external callers,
		  // and file-level array concat was unused by consumers (per VibeConsider C1).
		  var nodes = new List<JsonNode>();
		  foreach (var pair in sorted)
		  {
				var node = JsonNode.Parse(pair.Value.ToString(), documentOptions: _options);
				if (node is not null)
				{
					 nodes.Add(node);
				}
		  }

		  var mergedObject = JsonMergeCore.MergeAll(nodes);

		  // Public signature preserved: downstream GenerateFilesWorker consumes Dictionary<string, JsonElement>.
		  // Round-trip through JsonSerializer keeps JsonElement semantics (ValueKind, GetProperty, etc.) intact.
		  var finalJson = mergedObject.ToJsonString();
		  return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(finalJson, _serializerOptions)!;
	 }

	 //[Obsolete("uses File.IO to read.  Works but frowned upon for sourcegen.  Switched to SourceText",true)]
	 //public static Dictionary<string, JsonElement> MergeJsonFiles_FileIo(List<FileInfo> fileInfos, List<Diagnostic> diagReport)
	 //{
	 //	var mergedObject = new Dictionary<string, JsonElement>();

	 //	foreach (var info in fileInfos)
	 //	{
	 //		diagReport._Info($"obtaining settings from {info.FullName}");

	 //		using var jsonDoc = JsonDocument.Parse(info.OpenRead(), _options);

	 //		MergeJson(mergedObject, jsonDoc.RootElement);
	 //	}
	 //	return mergedObject;
	 //}


	 /// <summary>
	 /// merges json objects into a single object   /// 
	 /// </summary>
	 /// <param name="target"></param>
	 /// <param name="source"></param>
	 public static void MergeJson(Dictionary<string, JsonElement> target, JsonElement source)
	 {
		  if (source.ValueKind != JsonValueKind.Object)
		  {
				throw new ArgumentException("source must be an object", nameof(source));
		  }

		  foreach (var property in source.EnumerateObject())
		  {
				if (target.ContainsKey(property.Name))
				{
					 // If both are objects, merge them
					 if (target[property.Name].ValueKind == JsonValueKind.Object && property.Value.ValueKind == JsonValueKind.Object)
					 {
						  var mergedNestedObject = new Dictionary<string, JsonElement>();
						  MergeJson(mergedNestedObject, target[property.Name]);
						  MergeJson(mergedNestedObject, property.Value);
						  target[property.Name] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(mergedNestedObject));
					 }
					 else
					 {

						  // If both are arrays, concatenate them
						  if (target[property.Name].ValueKind == JsonValueKind.Array && property.Value.ValueKind == JsonValueKind.Array)
						  {
								var combinedArray = target[property.Name].EnumerateArray().Concat(property.Value.EnumerateArray()).ToArray();
								target[property.Name] = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(combinedArray));
						  }
						  // Handle other types (e.g., conflicting types) - this part depends on your specific merging strategy
						  else
						  {
								// Example strategy: Overwrite with the new value
								target[property.Name] = property.Value.Clone();
						  }

					 }

				}
				else
				{
					 // No conflict, just add the new key-value pair
					 target[property.Name] = property.Value.Clone();
				}
		  }
	 }
}
