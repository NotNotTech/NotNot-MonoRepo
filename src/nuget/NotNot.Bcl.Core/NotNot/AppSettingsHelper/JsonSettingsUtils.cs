using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NotNot.AppSettingsHelper;

/// <summary>
/// Utility methods for computing JSON diffs and merging JSON objects.
/// Supports deletion semantics: when a value is set to null in current but was non-null in original,
/// the diff will contain an explicit null sentinel that, when merged, removes the key from the target.
/// </summary>
public static class JsonSettingsUtils
{
    /// <summary>
    /// Default JSON serializer options for settings serialization.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null, // Keep original casing
    };

    /// <summary>
    /// Computes a diff between original and current JSON, returning only changed values.
    /// <para>
    /// Deletion semantics: When original has a value but current is null/missing,
    /// the diff will contain an explicit null sentinel for that key. This indicates
    /// the key should be removed from the user settings file when merged.
    /// </para>
    /// </summary>
    /// <param name="original">The original JSON state (baseline for comparison).</param>
    /// <param name="current">The current JSON state.</param>
    /// <returns>
    /// A JsonNode containing only the differences, or null if no changes.
    /// Explicit null values in the result indicate deletions.
    /// </returns>
    /// <remarks>
    /// Delegates to internal <c>JsonMergeCore</c> shared-source-linked from NotNot.AppSettings.
    /// </remarks>
    public static JsonNode? ComputeDiff(JsonNode? original, JsonNode? current)
    {
        return NotNot.AppSettingsInternal.JsonMergeCore.ComputeDiff(original, current);
    }

    /// <summary>
    /// Deep merges diff into target, preserving existing keys in target.
    /// <para>
    /// Deletion semantics: When a null sentinel is encountered in the diff,
    /// the corresponding key is removed from the target (not set to null).
    /// </para>
    /// </summary>
    /// <param name="target">The target JSON to merge into.</param>
    /// <param name="diff">The diff containing changes to apply.</param>
    /// <returns>A new JsonNode with the merged result.</returns>
    /// <remarks>
    /// Delegates to internal <c>JsonMergeCore</c> shared-source-linked from NotNot.AppSettings.
    /// </remarks>
    public static JsonNode MergeJson(JsonNode target, JsonNode diff)
    {
        // JsonMergeCore.Merge accepts nullable inputs and returns nullable;
        // with non-null diff the result is guaranteed non-null (either diff.DeepClone or a JsonObject).
        return NotNot.AppSettingsInternal.JsonMergeCore.Merge(target, diff)!;
    }

    /// <summary>
    /// Merges multiple JSON streams into a single JsonObject.
    /// Later streams override earlier ones (layered merge).
    /// </summary>
    /// <param name="streams">Streams to merge, in order of priority (last wins).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A merged JsonObject.</returns>
    public static async ValueTask<JsonObject> MergeStreamsAsync(
        IEnumerable<Stream> streams,
        CancellationToken ct = default)
    {
        var merged = new JsonObject();
        foreach (var stream in streams)
        {
            // PARSER_OPTIONS_PARITY (Phase-5 H2 closure): consume the same JsonDocumentOptions the
            // generator uses at build-time (AllowTrailingCommas + CommentHandling.Skip) so that a
            // human-authored appsettings.json with trailing commas or // comments that compiles at
            // build-time ALSO parses at runtime through this facade. Single source of truth lives on
            // the shared-source JsonMergeCore.DefaultDocumentOptions.
            var node = await JsonNode.ParseAsync(
                stream,
                nodeOptions: null,
                documentOptions: NotNot.AppSettingsInternal.JsonMergeCore.DefaultDocumentOptions,
                cancellationToken: ct);
            if (node is null)
            {
                continue;
            }
            if (node is not JsonObject obj)
            {
                // NON_OBJECT_ROOT_PARITY (Phase-5 M1 closure): match the generator-side behavior
                // (JsonMergeCore.MergeAll) by failing loudly rather than silently discarding. A
                // non-object root in an appsettings stream is a caller error per RULE_4.
                throw new JsonException(
                    $"appsettings JSON streams must have a JSON object at the root; got '{node.GetValueKind()}'. " +
                    "Arrays and primitives are not valid root shapes for a settings stream.");
            }
            merged = (JsonObject)MergeJson(merged, obj);
        }
        return merged;
    }

    /// <summary>
    /// Deserializes a JsonNode to the specified type.
    /// </summary>
    public static T? Deserialize<T>(JsonNode? node, JsonSerializerOptions? options = null)
    {
        if (node == null)
            return default;
        return node.Deserialize<T>(options ?? DefaultOptions);
    }

    /// <summary>
    /// Serializes an object to JsonNode.
    /// </summary>
    public static JsonNode? SerializeToNode<T>(T value, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.SerializeToNode(value, options ?? DefaultOptions);
    }
}
