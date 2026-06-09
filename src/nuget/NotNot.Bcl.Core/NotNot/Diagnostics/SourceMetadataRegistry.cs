// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;
using System.Text;

namespace NotNot.Diagnostics;

/// <summary>
/// Thread-safe registry mapping an assembly name to a JSON source-metadata blob.
/// </summary>
/// <remarks>
/// App-neutral storage seam: a build-time source generator (or a runtime scanner) emits a
/// <c>[ModuleInitializer]</c> that calls <see cref="Register"/> when the assembly loads, so a
/// consuming host can aggregate per-assembly source metadata (e.g. element-to-line maps) without
/// referencing each producing component library directly. Lives in Bcl.Core so sibling libraries
/// that share this base can all register against the same store.
/// </remarks>
public static class SourceMetadataRegistry
{
    /// <summary>
    /// Metadata storage: AssemblyName → JSON metadata string
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _metadata = new();

    /// <summary>
    /// Register metadata from an assembly's generated metadata class.
    /// Called automatically via [ModuleInitializer] in generated code.
    /// </summary>
    /// <param name="assemblyName">The assembly name (e.g., "Novaleaf.VibeOverwatch.Server")</param>
    /// <param name="json">JSON metadata for the assembly</param>
    public static void Register(string assemblyName, string json)
    {
        _metadata[assemblyName] = json;
    }

    /// <summary>
    /// Get aggregated metadata from all registered assemblies as a single JSON object.
    /// </summary>
    /// <returns>
    /// JSON object with structure:
    /// {
    ///   "Novaleaf.VibeOverwatch.Server": { "Component.razor": { "elements": [...] }, ... },
    ///   "OtherAssembly": { ... }
    /// }
    /// </returns>
    public static string GetAllMetadataJson()
    {
        if (_metadata.IsEmpty)
        {
            return "{}";
        }

        var sb = new StringBuilder();
        sb.Append('{');

        bool first = true;
        foreach (var kvp in _metadata)
        {
            if (!first) sb.Append(',');
            first = false;

            sb.Append('"');
            sb.Append(EscapeJsonString(kvp.Key));
            sb.Append("\":");
            sb.Append(kvp.Value); // Value is already valid JSON
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Get metadata for a specific assembly.
    /// </summary>
    /// <param name="assemblyName">The assembly name</param>
    /// <returns>JSON metadata or null if not registered</returns>
    public static string? GetMetadata(string assemblyName)
    {
        return _metadata.TryGetValue(assemblyName, out var json) ? json : null;
    }

    /// <summary>
    /// Check if any metadata has been registered.
    /// </summary>
    public static bool HasMetadata => !_metadata.IsEmpty;

    /// <summary>
    /// Get count of registered assemblies.
    /// </summary>
    public static int AssemblyCount => _metadata.Count;

    /// <summary>
    /// Clear all registered metadata (for testing purposes).
    /// </summary>
    internal static void Clear()
    {
        _metadata.Clear();
    }

    private static string EscapeJsonString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32)
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
