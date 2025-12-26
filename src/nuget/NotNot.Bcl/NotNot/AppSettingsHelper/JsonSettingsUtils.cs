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
    public static JsonNode? ComputeDiff(JsonNode? original, JsonNode? current)
    {
        // Both null = no diff
        if (original == null && current == null)
            return null;

        // Original null, current has value = entire current is the diff
        if (original == null)
            return current?.DeepClone();

        // Original has value, current null = deletion sentinel
        // This is the key deletion semantics: return explicit null
        if (current == null)
            return JsonValue.Create<object?>(null);

        // Different types = current wins entirely
        if (original.GetValueKind() != current.GetValueKind())
            return current.DeepClone();

        // Objects: recurse into properties
        if (current is JsonObject currentObj && original is JsonObject originalObj)
        {
            var diff = new JsonObject();

            // Check for changed/added properties
            foreach (var prop in currentObj)
            {
                originalObj.TryGetPropertyValue(prop.Key, out var origValue);
                var propDiff = ComputeDiff(origValue, prop.Value);
                if (propDiff != null)
                    diff[prop.Key] = propDiff;
            }

            // Check for deleted properties (in original but not in current)
            foreach (var prop in originalObj)
            {
                if (!currentObj.ContainsKey(prop.Key))
                {
                    // Property was deleted - add null sentinel
                    diff[prop.Key] = JsonValue.Create<object?>(null);
                }
            }

            return diff.Count > 0 ? diff : null;
        }

        // Arrays: compare serialized form (full replacement if any change)
        if (current is JsonArray currentArr && original is JsonArray originalArr)
        {
            if (currentArr.ToJsonString() != originalArr.ToJsonString())
                return current.DeepClone();
            return null;
        }

        // Values: simple equality via serialized form
        if (current.ToJsonString() != original.ToJsonString())
            return current.DeepClone();

        return null;
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
    public static JsonNode MergeJson(JsonNode target, JsonNode diff)
    {
        // If diff is not an object, it replaces target entirely
        if (diff is not JsonObject diffObj)
            return diff.DeepClone();

        JsonObject targetObj;
        if (target is JsonObject existingObj)
        {
            // Clone to avoid modifying original
            targetObj = JsonNode.Parse(existingObj.ToJsonString())!.AsObject();
        }
        else
        {
            targetObj = new JsonObject();
        }

        foreach (var prop in diffObj)
        {
            // Deletion semantics: null value means remove the key
            if (prop.Value == null || prop.Value.GetValueKind() == JsonValueKind.Null)
            {
                targetObj.Remove(prop.Key);
            }
            else if (prop.Value is JsonObject childDiff)
            {
                // Recursive merge for nested objects
                targetObj.TryGetPropertyValue(prop.Key, out var existing);
                targetObj[prop.Key] = MergeJson(existing ?? new JsonObject(), childDiff);
            }
            else
            {
                // Direct value replacement
                targetObj[prop.Key] = prop.Value.DeepClone();
            }
        }

        return targetObj;
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
            var node = await JsonNode.ParseAsync(stream, cancellationToken: ct);
            if (node is JsonObject obj)
            {
                merged = (JsonObject)MergeJson(merged, obj);
            }
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
