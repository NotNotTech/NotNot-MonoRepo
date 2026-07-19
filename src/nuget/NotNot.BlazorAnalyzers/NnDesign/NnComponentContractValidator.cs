using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NnDesign component-contract DIFF/VALIDATION engine (Phase-3.3 5B — schema-as-data, FIRST increment).
/// <para>
/// A component-contract SCHEMA (the declarative <c>NnComponentContracts.NnContract.md</c> in the producer)
/// declares, per component, its Tier-A parameter surface, supported <c>data-nns-fill</c> modes, and slots.
/// A schema describes what SHOULD be; it does NOT by itself prove the live components match it. THIS class
/// is the closure: it parses the schema text + the LIVE <c>.razor</c> component source + the canonical
/// <c>nn-design.css</c>, and emits <see cref="ContractDrift"/> findings for every divergence —
/// a live Tier-A param absent from the schema, a schema param absent from the component, a slot mismatch,
/// or a declared <c>data-nns-fill</c> mode with no supporting CSS hook.
/// </para>
/// <para>
/// <b>Text-grounded, dependency-free (netstandard2.0).</b> Mirrors <see cref="NnTokensGenerator"/>'s
/// regex-over-source approach (CSS/data stays canonical; the tool READS it). No <c>System.Text.Json</c>,
/// no semantic model — so the same engine drives both an in-session xUnit diff test (reads files from disk)
/// and a future analyzer (reads <c>AdditionalText</c>) with zero machinery change. The DRIFT-CATCH is the
/// proof: feeding a deliberately-drifted schema or component MUST yield a finding.
/// </para>
/// </summary>
public static class NnComponentContractValidator
{
    // ── Public model ─────────────────────────────────────────────────────────

    /// <summary>The kind of contract divergence a <see cref="ContractDrift"/> records.</summary>
    public enum DriftKind
    {
        /// <summary>A live <c>[Parameter]</c> (Tier-A) on the component is NOT declared in the schema.</summary>
        LiveTierAParamMissingFromSchema,

        /// <summary>A schema-declared Tier-A param is NOT present on the live component.</summary>
        SchemaParamMissingFromComponent,

        /// <summary>A live <c>RenderFragment</c> slot on the component is NOT declared in the schema.</summary>
        LiveSlotMissingFromSchema,

        /// <summary>A schema-declared slot is NOT present on the live component.</summary>
        SchemaSlotMissingFromComponent,

        /// <summary>A schema-declared non-<c>content</c> <c>data-nns-fill</c> mode has no supporting CSS hook.</summary>
        DeclaredFillModeUnsupportedByCss,

        /// <summary>The schema names a <c>razor:</c> path that could not be read.</summary>
        SchemaRazorFileUnreadable,
    }

    /// <summary>One contract divergence between the schema and the live producer artifacts.</summary>
    public sealed class ContractDrift
    {
        /// <summary>Creates a drift record for <paramref name="component"/> classified as
        /// <paramref name="kind"/> with human-readable <paramref name="detail"/>.</summary>
        public ContractDrift(string component, DriftKind kind, string detail)
        {
            Component = component;
            Kind = kind;
            Detail = detail;
        }

        /// <summary>The component (schema block) the drift belongs to.</summary>
        public string Component { get; }

        /// <summary>The classification of the divergence.</summary>
        public DriftKind Kind { get; }

        /// <summary>Human-readable specifics (the offending param/slot/mode name + direction).</summary>
        public string Detail { get; }

        /// <summary>Renders the drift as <c>[component] kind: detail</c> for diagnostic messages.</summary>
        public override string ToString() => $"[{Component}] {Kind}: {Detail}";
    }

    /// <summary>A parsed schema block for ONE component.</summary>
    public sealed class ComponentSchema
    {
        /// <summary>Creates a schema block from the parsed declared component contract.</summary>
        public ComponentSchema(
            string component,
            string razorPath,
            ImmutableArray<string> tierAParams,
            ImmutableArray<string> dataNnFillModes,
            ImmutableHashSet<string> cssExemptModes,
            ImmutableArray<string> slots)
        {
            Component = component;
            RazorPath = razorPath;
            TierAParams = tierAParams;
            DataNnFillModes = dataNnFillModes;
            CssExemptModes = cssExemptModes;
            Slots = slots;
        }

        /// <summary>The component name this schema block declares.</summary>
        public string Component { get; }
        /// <summary>The <c>razor:</c> path of the live producer artifact the schema is checked against.</summary>
        public string RazorPath { get; }
        /// <summary>The declared Tier-A parameter names (excludes Tier-B style/class params).</summary>
        public ImmutableArray<string> TierAParams { get; }
        /// <summary>The declared <c>data-nns-fill</c> modes the component supports.</summary>
        public ImmutableArray<string> DataNnFillModes { get; }
        /// <summary>The fill modes exempt from the supporting-CSS-hook requirement.</summary>
        public ImmutableHashSet<string> CssExemptModes { get; }
        /// <summary>The declared <c>RenderFragment</c> slot names.</summary>
        public ImmutableArray<string> Slots { get; }
    }

    /// <summary>The Tier-A parameter surface + slot surface parsed from a live <c>.razor</c> component.</summary>
    public sealed class LiveComponentSurface
    {
        /// <summary>Creates the live surface from the Tier-A parameter and slot names parsed from the
        /// producer <c>.razor</c> component.</summary>
        public LiveComponentSurface(ImmutableArray<string> tierAParams, ImmutableArray<string> slots)
        {
            TierAParams = tierAParams;
            Slots = slots;
        }

        /// <summary>Public <c>[Parameter]</c> names classified Tier-A (excludes Tier-B <c>*Style</c>/<c>*Class</c>
        /// string params and <c>[CascadingParameter]</c> members) — INCLUDES RenderFragment slot params.</summary>
        public ImmutableArray<string> TierAParams { get; }

        /// <summary>The <c>RenderFragment</c> / <c>RenderFragment&lt;T&gt;</c> <c>[Parameter]</c> slot names.</summary>
        public ImmutableArray<string> Slots { get; }
    }

    // ── Schema parsing ────────────────────────────────────────────────────────

    private static readonly Regex BlockOpen = new(
        @"^\[component\]\s+(?<name>\S+)\s*$", RegexOptions.Compiled);

    private static readonly Regex FieldLine = new(
        @"^(?<key>razor|tier-a|data-nns-fill|slots)\s*:\s*(?<val>.*)$", RegexOptions.Compiled);

    /// <summary>Strips a trailing inline <c>#</c> comment (the schema allows end-of-line comments).</summary>
    private static string StripInlineComment(string s)
    {
        var hash = s.IndexOf('#');
        return hash >= 0 ? s.Substring(0, hash) : s;
    }

    /// <summary>
    /// Parses the declarative schema text into per-component <see cref="ComponentSchema"/> blocks.
    /// Honors the line-based block format documented in <c>NnComponentContracts.NnContract.md</c>:
    /// <c>[component] Name</c> opens a block; <c>razor:</c> / <c>tier-a:</c> / <c>data-nns-fill:</c> /
    /// <c>slots:</c> set its fields; a trailing <c>|</c> on a <c>tier-a:</c> line continues the list; a
    /// <c>(css-exempt)</c> suffix on a mode marks it as not requiring a CSS hook.
    /// </summary>
    public static ImmutableArray<ComponentSchema> ParseSchema(string schemaText)
    {
        var result = ImmutableArray.CreateBuilder<ComponentSchema>();

        string? name = null;
        string razor = "";
        var tierA = new List<string>();
        var modes = new List<string>();
        var cssExempt = new HashSet<string>(StringComparer.Ordinal);
        var slots = new List<string>();

        void Flush()
        {
            if (name == null) return;
            result.Add(new ComponentSchema(
                name,
                razor,
                tierA.ToImmutableArray(),
                modes.ToImmutableArray(),
                cssExempt.ToImmutableHashSet(StringComparer.Ordinal),
                slots.ToImmutableArray()));
        }

        var inFence = false;
        foreach (var rawLine in SplitLines(schemaText))
        {
            var line = rawLine.TrimEnd();

            // Skip ```-fenced regions: the schema file documents its OWN block format inside a fenced code
            // example (a pseudo-block with placeholder <SimpleTypeName>/<path> tokens) — that example must
            // NOT be parsed as a real component block.
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            if (line.Length == 0) continue;

            var open = BlockOpen.Match(line);
            if (open.Success)
            {
                Flush();
                name = open.Groups["name"].Value;
                razor = "";
                tierA = new List<string>();
                modes = new List<string>();
                cssExempt = new HashSet<string>(StringComparer.Ordinal);
                slots = new List<string>();
                continue;
            }

            if (name == null) continue;            // ignore preamble / fenced-format docs before first block
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

            var field = FieldLine.Match(line.TrimStart());
            if (!field.Success) continue;

            var key = field.Groups["key"].Value;
            var val = StripInlineComment(field.Groups["val"].Value).Trim();

            switch (key)
            {
                case "razor":
                    razor = val;
                    break;
                case "tier-a":
                    // A trailing '|' continues the list onto the next tier-a line; strip it then split.
                    var hasContinuation = val.EndsWith("|", StringComparison.Ordinal);
                    if (hasContinuation) val = val.Substring(0, val.Length - 1);
                    tierA.AddRange(SplitCsv(val));
                    break;
                case "data-nns-fill":
                    foreach (var token in SplitCsv(val))
                    {
                        var mode = token;
                        var exempt = false;
                        var paren = mode.IndexOf('(');
                        if (paren >= 0)
                        {
                            exempt = mode.IndexOf("css-exempt", paren, StringComparison.OrdinalIgnoreCase) >= 0;
                            mode = mode.Substring(0, paren).Trim();
                        }
                        if (mode.Length == 0 || string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
                            continue;
                        modes.Add(mode);
                        if (exempt) cssExempt.Add(mode);
                    }
                    break;
                case "slots":
                    foreach (var s in SplitCsv(val))
                    {
                        if (string.Equals(s, "none", StringComparison.OrdinalIgnoreCase)) continue;
                        slots.Add(s);
                    }
                    break;
            }
        }

        Flush();
        return result.ToImmutable();
    }

    // ── Live .razor surface parsing ───────────────────────────────────────────

    // A [Parameter] (NOT [CascadingParameter]) property declaration. Captures the declared type + name.
    // Razor @code blocks declare params exactly like C#: `[Parameter] public <Type> <Name> { get; set; }`.
    private static readonly Regex ParameterDecl = new(
        @"\[\s*Parameter\s*\]\s*public\s+(?<type>[\w<>?\.]+)\s+(?<name>\w+)\s*\{\s*get;\s*set;",
        RegexOptions.Compiled);

    // A RenderFragment / RenderFragment<T> type (the slot shape), with optional nullable '?'.
    private static readonly Regex RenderFragmentType = new(
        @"^RenderFragment(<[^>]+>)?\??$", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the Tier-A parameter surface + slot surface from live <c>.razor</c> source. Tier-A =
    /// public <c>[Parameter]</c> members EXCLUDING the Tier-B <c>*Style</c>/<c>*Class</c> string shape that
    /// <see cref="NnDesignTierBExposureAnalyzer"/> bans (those are never schema'd as Tier-A) and EXCLUDING
    /// <c>[CascadingParameter]</c> members (infrastructure, not a consumer call-site surface). Slots = the
    /// subset whose declared type is <c>RenderFragment</c>/<c>RenderFragment&lt;T&gt;</c>.
    /// </summary>
    public static LiveComponentSurface ParseLiveSurface(string razorSource)
    {
        var tierA = new List<string>();
        var slots = new List<string>();

        foreach (Match m in ParameterDecl.Matches(razorSource))
        {
            // Exclude [CascadingParameter] — the regex matched on the [Parameter] token, but a property may
            // carry [CascadingParameter] instead; guard by checking the attribute immediately preceding.
            // (ParameterDecl already requires the literal "[Parameter]" token, so cascading members — which
            // use "[CascadingParameter]" — are not matched. No extra guard needed.)
            var type = m.Groups["type"].Value;
            var name = m.Groups["name"].Value;

            // Tier-B *Style/*Class string shape (NNB043) is never a Tier-A schema entry — skip it so the
            // diff does not demand the schema list a banned appearance-laundering param.
            if (string.Equals(type, "string", StringComparison.Ordinal)
                && (name.EndsWith("Style", StringComparison.Ordinal)
                 || name.EndsWith("Class", StringComparison.Ordinal)))
            {
                continue;
            }

            tierA.Add(name);
            if (RenderFragmentType.IsMatch(type))
            {
                slots.Add(name);
            }
        }

        return new LiveComponentSurface(tierA.ToImmutableArray(), slots.ToImmutableArray());
    }

    // ── CSS mode-hook scan ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the set of <c>data-nns-fill</c> mode VALUES that have at least one supporting CSS hook
    /// (<c>[data-nns-fill="value"]</c>) anywhere in the supplied stylesheet. Used to verify a schema-declared
    /// non-<c>content</c> mode is actually backed by CSS (the manifesto's "one block per supported value"
    /// rule) — unless the schema marks the mode <c>(css-exempt)</c>.
    /// </summary>
    public static ImmutableHashSet<string> ScanCssFillModes(string cssText)
    {
        var set = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (Match m in CssFillHook.Matches(cssText))
        {
            set.Add(m.Groups["mode"].Value);
        }
        return set.ToImmutable();
    }

    private static readonly Regex CssFillHook = new(
        @"\[\s*data-nns-fill\s*=\s*""(?<mode>[a-z]+)""\s*\]", RegexOptions.Compiled);

    // ── The diff/validation pass ──────────────────────────────────────────────

    /// <summary>
    /// THE F5 closure. Diffs the parsed <paramref name="schema"/> against the LIVE surfaces +
    /// CSS-supported modes and returns every divergence. Empty result = in-sync. A non-empty result is the
    /// drift the schema describes but the components no longer honor (or vice-versa).
    /// </summary>
    /// <param name="schema">Parsed schema blocks (<see cref="ParseSchema"/>).</param>
    /// <param name="liveSurfaces">Per-component live surface, keyed by component name
    /// (<see cref="ParseLiveSurface"/> over each schema block's <c>.razor</c> source). A component whose
    /// <c>.razor</c> could not be read is OMITTED from this map and yields a
    /// <see cref="DriftKind.SchemaRazorFileUnreadable"/> finding.</param>
    /// <param name="cssSupportedModes">The CSS-backed mode set (<see cref="ScanCssFillModes"/>).</param>
    public static ImmutableArray<ContractDrift> Diff(
        ImmutableArray<ComponentSchema> schema,
        IReadOnlyDictionary<string, LiveComponentSurface> liveSurfaces,
        ImmutableHashSet<string> cssSupportedModes)
    {
        var drift = ImmutableArray.CreateBuilder<ContractDrift>();

        foreach (var block in schema)
        {
            if (!liveSurfaces.TryGetValue(block.Component, out var live))
            {
                drift.Add(new ContractDrift(
                    block.Component, DriftKind.SchemaRazorFileUnreadable,
                    $"schema names razor '{block.RazorPath}' but no live surface was supplied/readable"));
                continue;
            }

            var schemaParams = block.TierAParams.ToImmutableHashSet(StringComparer.Ordinal);
            var liveParams = live.TierAParams.ToImmutableHashSet(StringComparer.Ordinal);

            foreach (var p in live.TierAParams)
            {
                if (!schemaParams.Contains(p))
                {
                    drift.Add(new ContractDrift(
                        block.Component, DriftKind.LiveTierAParamMissingFromSchema,
                        $"live Tier-A param '{p}' is not declared in the schema"));
                }
            }

            foreach (var p in block.TierAParams)
            {
                if (!liveParams.Contains(p))
                {
                    drift.Add(new ContractDrift(
                        block.Component, DriftKind.SchemaParamMissingFromComponent,
                        $"schema Tier-A param '{p}' is not present on the live component"));
                }
            }

            var schemaSlots = block.Slots.ToImmutableHashSet(StringComparer.Ordinal);
            var liveSlots = live.Slots.ToImmutableHashSet(StringComparer.Ordinal);

            foreach (var s in live.Slots)
            {
                if (!schemaSlots.Contains(s))
                {
                    drift.Add(new ContractDrift(
                        block.Component, DriftKind.LiveSlotMissingFromSchema,
                        $"live slot '{s}' is not declared in the schema"));
                }
            }

            foreach (var s in block.Slots)
            {
                if (!liveSlots.Contains(s))
                {
                    drift.Add(new ContractDrift(
                        block.Component, DriftKind.SchemaSlotMissingFromComponent,
                        $"schema slot '{s}' is not present on the live component"));
                }
            }

            foreach (var mode in block.DataNnFillModes)
            {
                if (string.Equals(mode, "content", StringComparison.Ordinal)) continue; // default: never a CSS hook
                if (block.CssExemptModes.Contains(mode)) continue;                       // documented no-auto-CSS mode
                if (!cssSupportedModes.Contains(mode))
                {
                    drift.Add(new ContractDrift(
                        block.Component, DriftKind.DeclaredFillModeUnsupportedByCss,
                        $"schema declares data-nns-fill='{mode}' but nn-design.css has no [data-nns-fill=\"{mode}\"] hook"));
                }
            }
        }

        return drift.ToImmutable();
    }

    // ── Small helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static IEnumerable<string> SplitCsv(string val) =>
        val.Split(',')
           .Select(s => s.Trim())
           .Where(s => s.Length > 0);
}
