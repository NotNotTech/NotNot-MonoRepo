using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NnDesign token-schema source generator (Phase-1.4 5A — schema-as-data FIRST increment).
/// Reads the CANONICAL CSS custom properties (<c>--nns-*</c>) from the NnDesign authority stylesheets
/// (<c>nn-design.css</c> and <c>nn-colors.css</c>) via <see cref="AdditionalText"/> and emits two
/// artifacts into the producer:
/// <list type="number">
///   <item>
///     <b>NnTokens.g.cs</b> — a strongly-typed <c>NnTokens</c> accessor: one member per token exposing
///     both the CSS <c>var(--nns-*)</c> reference (for C# to emit style strings without stringly-typed
///     token names) and the authored default value; plus <c>AllTokenNames</c> + <c>ManifestJson</c>.
///   </item>
///   <item>
///     <b>NnDesignTokenManifest.g.cs</b> — the machine-readable canonical list of valid <c>--nns-*</c>
///     token names (as embedded JSON + a string array): the schema-as-data SSOT for token names. This
///     generator PRODUCES the manifest only; it does NOT validate (the NNB_CSS013 token-validity
///     analyzer reads the authority CSS directly — it does NOT reflect this manifest).
///   </item>
/// </list>
/// <para>
/// CSS stays canonical (U-5=A): tokens are AUTHORED in the authority stylesheets and READ here — this
/// generator never inverts the source. Mirrors <see cref="XRay.XRayMetadataGenerator"/>'s
/// AdditionalTexts-driven incremental-generator structure (opt-out build property + deterministic
/// sorted output + minimal-file-on-empty to confirm the generator ran).
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class NnTokensGenerator : IIncrementalGenerator
{
    /// <summary>The canonical authority stylesheet filenames the generator reads tokens FROM.</summary>
    private static readonly ImmutableHashSet<string> CanonicalCssFileNames =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "nn-design.css", "nn-colors.css");

    /// <summary>
    /// Matches a CSS custom-property declaration <c>--nns-name: value;</c> for the canonical
    /// <c>--nns-*</c> token family. Captures the name (sans leading <c>--</c>) and the raw value
    /// (sans trailing <c>;</c>). The <c>@property --nns-x { … }</c> registration form does NOT match
    /// (its name is followed by <c>{</c>, not <c>:</c>) — only <c>:root</c> value declarations are read.
    /// </summary>
    private static readonly Regex TokenDeclaration = new(
        @"(?<![\w-])--(?<name>nns-[a-z0-9]+(?:-[a-z0-9]+)*)\s*:\s*(?<value>[^;{}]+?)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Wires the incremental pipeline that harvests <c>--nns-*</c> token declarations from
    /// authority CSS and emits the generated token metadata (gated by the
    /// <c>NnTokensGeneratorEnabled</c> build property).</summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Opt-out via AnalyzerConfig (set NnTokensGeneratorEnabled=false to disable).
        var optionsProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.NnTokensGeneratorEnabled", out var enabled);
                return !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase);
            });

        // The canonical NnDesign stylesheets (registered as AdditionalFiles via the .props CSS glob).
        var canonicalCss = context.AdditionalTextsProvider
            .Where(static file =>
                CanonicalCssFileNames.Contains(Path.GetFileName(file.Path)))
            .Select(static (file, ct) =>
            {
                var content = file.GetText(ct)?.ToString() ?? "";
                return (FilePath: file.Path, Tokens: ParseTokens(content));
            })
            .Collect();

        var assemblyAndTokens = context.CompilationProvider
            .Combine(canonicalCss)
            .Combine(optionsProvider);

        context.RegisterSourceOutput(assemblyAndTokens, static (spc, source) =>
        {
            var ((compilation, cssFiles), isEnabled) = source;

            if (!isEnabled)
            {
                return;
            }

            var assemblyName = compilation.AssemblyName ?? "NotNot.BlazorDesign";

            // Merge token sets across any matching files (first-declaration-wins per name = canonical :root).
            var tokens = MergeTokens(cssFiles.SelectMany(f => f.Tokens));

            if (tokens.Length == 0)
            {
                // Emit a minimal file confirming the generator ran but found no tokens (parity with XRay).
                var sourcedFrom = cssFiles.Length > 0 ? cssFiles[0].FilePath : "(no authority CSS AdditionalFile)";
                var minimal = $@"// <auto-generated/>
// NnTokens: generator ran but found zero --nns-* tokens. Source: {EscapeComment(sourcedFrom)}
namespace NotNot.BlazorDesign.Tokens;
public static partial class NnTokens
{{
    /// <summary>Count of canonical --nns-* tokens discovered (0 = generator ran, source had none).</summary>
    public const int Count = 0;
}}
";
                spc.AddSource("NnTokens.g.cs", SourceText.From(minimal, Encoding.UTF8));
                spc.AddSource("NnDesignTokenManifest.g.cs", SourceText.From(BuildManifest(ImmutableArray<TokenInfo>.Empty), Encoding.UTF8));
                return;
            }

            spc.AddSource("NnTokens.g.cs", SourceText.From(BuildAccessor(tokens), Encoding.UTF8));
            spc.AddSource("NnDesignTokenManifest.g.cs", SourceText.From(BuildManifest(tokens), Encoding.UTF8));
        });
    }

    // ── Parsing ─────────────────────────────────────────────────────────────

    private readonly struct TokenInfo
    {
        public TokenInfo(string cssName, string value)
        {
            CssName = cssName;       // e.g. "nns-spacing-md"  (no leading --)
            Value = value;           // authored CSS value, e.g. "16px"
        }

        public string CssName { get; }
        public string Value { get; }

        /// <summary>The CSS variable reference, e.g. <c>var(--nns-spacing-md)</c>.</summary>
        public string VarRef => $"var(--{CssName})";

        /// <summary>PascalCase C# member identifier, e.g. <c>SpacingMd</c> (drops the <c>nns-</c> prefix).</summary>
        public string MemberName => ToPascalCase(CssName);
    }

    private static ImmutableArray<TokenInfo> ParseTokens(string css)
    {
        if (string.IsNullOrEmpty(css))
        {
            return ImmutableArray<TokenInfo>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<TokenInfo>();
        foreach (Match m in TokenDeclaration.Matches(css))
        {
            var name = m.Groups["name"].Value;
            var value = m.Groups["value"].Value.Trim();
            // Collapse internal whitespace runs (multi-line / aligned declarations) to single spaces.
            value = Regex.Replace(value, @"\s+", " ");
            builder.Add(new TokenInfo(name, value));
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Dedupe by token name (FIRST declaration wins = the canonical <c>:root</c> definition; later
    /// dark-mode/override redeclarations are ignored), then sort by name for deterministic output.
    /// </summary>
    private static ImmutableArray<TokenInfo> MergeTokens(IEnumerable<TokenInfo> all)
    {
        var seen = new Dictionary<string, TokenInfo>(StringComparer.Ordinal);
        foreach (var t in all)
        {
            if (!seen.ContainsKey(t.CssName))
            {
                seen[t.CssName] = t;
            }
        }
        return seen.Values
            .OrderBy(t => t.CssName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    // ── Code generation ─────────────────────────────────────────────────────

    private static string BuildAccessor(ImmutableArray<TokenInfo> tokens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace NotNot.BlazorDesign.Tokens;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Strongly-typed accessor for the canonical NnDesign <c>--nns-*</c> design tokens, generated");
        sb.AppendLine("/// FROM <c>nn-design.css</c> + <c>nn-colors.css</c> (CSS is the authority — U-5=A). Each member");
        sb.AppendLine("/// exposes the CSS <c>var(--nns-*)</c> reference so C# may emit style strings without stringly-typed token names.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class NnTokens");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Count of canonical --nns-* tokens discovered from the authority stylesheets.</summary>");
        sb.AppendLine($"    public const int Count = {tokens.Length};");
        sb.AppendLine();

        foreach (var t in tokens)
        {
            sb.AppendLine($"    /// <summary><c>var(--{t.CssName})</c> — authored default: <c>{EscapeXmlDoc(t.Value)}</c>.</summary>");
            sb.AppendLine($"    public const string {t.MemberName} = \"{EscapeCSharp(t.VarRef)}\";");
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>The authored default VALUE per token (as written in nn-design.css), keyed by CSS name.</summary>");
        sb.AppendLine("    public static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> Defaults =");
        sb.AppendLine("        new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal)");
        sb.AppendLine("        {");
        foreach (var t in tokens)
        {
            sb.AppendLine($"            [\"{EscapeCSharp(t.CssName)}\"] = \"{EscapeCSharp(t.Value)}\",");
        }
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>All canonical --nns-* token names (sans leading <c>--</c>), sorted ordinally.</summary>");
        sb.AppendLine("    public static readonly string[] AllTokenNames = new string[]");
        sb.AppendLine("    {");
        foreach (var t in tokens)
        {
            sb.AppendLine($"        \"{EscapeCSharp(t.CssName)}\",");
        }
        sb.AppendLine("    };");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the manifest source. The manifest is the canonical valid-token-name set, surfaced as
    /// <c>NnDesignTokenManifest.ValidTokenNames</c> (string array) + <c>ManifestJson</c> (embedded JSON):
    /// the schema-as-data SSOT for <c>--nns-*</c> token names, available to future C# consumers.
    /// </summary>
    private static string BuildManifest(ImmutableArray<TokenInfo> tokens)
    {
        var json = BuildManifestJson(tokens);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace NotNot.BlazorDesign.Tokens;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// MANIFEST of the canonical NnDesign <c>--nns-*</c> token names, generated FROM");
        sb.AppendLine("/// <c>nn-design.css</c> + <c>nn-colors.css</c>. The schema-as-data SSOT for token names,");
        sb.AppendLine("/// available to future <c>--nns-*</c> C# consumers. This generator produces it; it does NOT validate.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class NnDesignTokenManifest");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Number of canonical --nns-* token names in the manifest.</summary>");
        sb.AppendLine($"    public const int Count = {tokens.Length};");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The canonical valid --nns-* token names (sans leading <c>--</c>), sorted ordinally.</summary>");
        sb.AppendLine("    public static readonly string[] ValidTokenNames = new string[]");
        sb.AppendLine("    {");
        foreach (var t in tokens)
        {
            sb.AppendLine($"        \"{EscapeCSharp(t.CssName)}\",");
        }
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Machine-readable manifest as JSON: <c>{\"version\":1,\"tokens\":[{\"name\":..,\"value\":..}]}</c>.</summary>");
        sb.AppendLine("    public const string ManifestJson = \"\"\"");
        sb.AppendLine($"    {json}");
        sb.AppendLine("    \"\"\";");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildManifestJson(ImmutableArray<TokenInfo> tokens)
    {
        var jb = new StringBuilder();
        jb.Append("{\"version\":1,\"tokens\":[");
        bool first = true;
        foreach (var t in tokens)
        {
            if (!first) jb.Append(',');
            first = false;
            jb.Append("{\"name\":\"").Append(EscapeJson(t.CssName)).Append("\",\"value\":\"")
              .Append(EscapeJson(t.Value)).Append("\"}");
        }
        jb.Append("]}");
        return jb.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string ToPascalCase(string cssName)
    {
        // "nns-spacing-md" → drop "nns-" prefix → "spacing-md" → "SpacingMd".
        var name = cssName.StartsWith("nns-", StringComparison.Ordinal) ? cssName.Substring(4) : cssName;
        var parts = name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(name.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            // Leading digit (e.g. a hypothetical "8x") would be an invalid identifier start — prefix '_'.
            if (char.IsDigit(part[0]))
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpper(part[0], CultureInfo.InvariantCulture));
            if (part.Length > 1)
            {
                sb.Append(part.Substring(1));
            }
        }
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    private static string EscapeCSharp(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string EscapeXmlDoc(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeComment(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("*/", "* /").Replace("\r", " ").Replace("\n", " ");
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
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
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
