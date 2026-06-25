using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// NNB_CSS011 — a consumer NEVER applies CSS to an <c>Nn*</c> element, even through a CONSUMER-OWNED
/// class (the loophole left open by NNB_CSS008/009, which ban only the <c>.nns-*</c>/<c>.mud-*</c>
/// internal-namespace reach-in).
/// <para>
/// <b>The loophole this closes.</b> NNB_CSS008/009 key on the styled SUBJECT being a namespaced
/// internal class. They do NOT fire on <c>::deep .my-class { … }</c> where <c>.my-class</c> is a
/// consumer-authored class that is BOUND to an <c>Nn*</c> component root (e.g.
/// <c>::deep .vow-metaside-actions-section { min-height: 80px }</c> in <c>Foo.razor.css</c> paired with
/// <c>&lt;NnContentSection Class="vow-metaside-actions-section"&gt;</c> in <c>Foo.razor</c>). The
/// declaration still lands on an <c>Nn*</c> element — a section-root floor that silently defeats the
/// component's collapse — so it is a consumer styling of an <c>Nn*</c> element regardless of the class
/// being consumer-named. Property-agnostic: a <c>min-height</c>/<c>margin</c> layout declaration counts
/// exactly as a <c>color</c>/<c>background</c> one.
/// </para>
/// <para>
/// <b>Detection (cross-file AdditionalText correlation — VIABLE_COSTLY).</b> A candidate is a
/// <c>::deep &lt;compound&gt;</c> selector in a consumer <c>.razor.css</c> whose SUBJECT (rightmost
/// compound, computed by <see cref="FindSubjectStart"/>) is a PLAIN consumer <c>.class</c> token (NOT
/// <c>.nns-*</c>/<c>.mud-*</c> — those are CSS008/009's concern). The <c>::deep</c> is REQUIRED: it is
/// what pierces the scoped boundary into the child component's DOM — own-element styling omits
/// <c>::deep</c>, so its presence is a necessary condition for "this rule reaches into a child". The
/// analyzer then reads the PAIRED <c>.razor</c> (same path, <c>.css</c> stripped) from the
/// <c>AdditionalFiles</c> set and looks for an <c>Nn*</c> open-tag carrying a LITERAL
/// <c>Class="… class …"</c> that contains the subject class as a space-separated token. Match (and the
/// component is not a gray-zone exemption) → report at the CSS selector subject.
/// </para>
/// <para>
/// <b>Documented false-negatives (accepted at authoring, never guessed-around).</b>
/// <list type="bullet">
///   <item>Dynamic <c>Class="@expr"</c> on the <c>Nn*</c> tag → not literal-matchable → SKIP (gap).</item>
///   <item>Inline <c>Style=</c> on an <c>Nn*</c> tag → owned by NNB044 (this rule is CSS-selector-only,
///         so it never double-emits on inline styles by construction).</item>
///   <item>A subject class bound to the <c>Nn*</c> in a DIFFERENT, non-paired <c>.razor</c> (shared
///         class authored once, applied across files) → only the paired <c>.razor</c> is consulted.</item>
/// </list>
/// </para>
/// <para>
/// <b>Exemptions</b> (ported from <see cref="CssNnReachInAnalyzer"/>): path buckets
/// (<c>NotNot.BlazorDesign</c> producer, <c>NnDesignSamples/</c>, <c>Pages/Samples/</c>, vendor),
/// file-name allow-list (<c>app.css</c>, <c>nn-design-samples.css</c>), a per-file opt-out comment
/// (<c>nnb_css011:allow-reachin: &lt;reason&gt;</c>), the shared <c>CssAnalyzerEnabled=false</c>
/// kill-switch, PLUS the VOW-local GRAY-ZONE <c>Nn*</c> components (<c>NnBlazorTermTab</c>,
/// <c>NnBlazorTermTabFooter</c>, <c>NnPerfMonitorPanel</c>) that retain the <c>Nn*</c> prefix as
/// branding markers yet live in the consumer assembly — styling them is consumer-local, not a design-
/// system reach-in.
/// </para>
/// <para>
/// <b>Severity = Warning (interim ratchet).</b> The consumer tree is NOT yet reach-in-clean — the
/// step-3 sweep left a documented <c>TODO(NnDesign-sweep)</c> tail of <c>::deep</c>-on-<c>Nn*</c> rules.
/// Warning surfaces that tail as a worklist without breaking the build. Escalates to Error once the
/// consumer surface is verified clean (the doctrine end-state).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CssNnConsumerClassAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for the consumer-class-on-Nn* styling rule.</summary>
    public const string DiagnosticId = "NNB_CSS011";

    private const string Category = "CssModernization";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker — <c>nnb_css011:allow-reachin: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb_css011:allow-reachin";

    /// <summary>
    /// NNB_CSS011 descriptor. Severity = Warning (interim ratchet) — surfaces the not-yet-clean
    /// <c>::deep</c>-on-<c>Nn*</c> consumer tail as a worklist without breaking the build; escalates to
    /// Error once the consumer surface is verified clean. Suppression: per-file
    /// <c>nnb_css011:allow-reachin</c> comment, the <c>CssAnalyzerEnabled=false</c> build property, or
    /// the gray-zone component exemption list.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleNoConsumerNnStyling = new(
        DiagnosticId,
        "Do not apply consumer CSS to an Nn* element (even via a consumer-owned class)",
        "Consumer scoped CSS '::deep .{0}' lands on the Nn* component '<{1}>' (paired .razor binds "
            + "Class=\"…{0}…\" to it). A consumer never styles an Nn* element — visual OR layout. "
            + "Resolve: (1) DELETE the rule if it restates a producer default (fix the default once); "
            + "(2) use/add an NnDesign Tier-A param or a Layout Contract mode (Fill / MaxHeight / "
            + "data-nns-fill); (3) only if genuinely intended, add a per-file "
            + "nnb_css011:allow-reachin comment. (NNB_CSS011)",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Tier A/B govern Nn* PARAMETERS; this governs consumer CSS. A consumer-authored CSS "
            + "declaration must never land on an Nn* element, regardless of delivery — internal-class "
            + "reach-in (NNB_CSS008/009) OR a consumer-owned class applied to the Nn* via ::deep. The "
            + "live loophole NNB_CSS008/009 miss: '::deep .my-class { … }' where '.my-class' is a "
            + "consumer class bound to an Nn* root in the paired .razor (e.g. a section-root min-height "
            + "floor that silently defeats the component's collapse). Property-agnostic (layout counts "
            + "as much as visual). Sanctioned alternative: a Layout Contract mode (Fill / MaxHeight / "
            + "data-nns-fill) or a Tier-A param — never restyle the Nn* element. Exempt: NnDesign "
            + "producer / NotNot.BlazorDesign internals, NnDesignSamples/** + Pages/Samples/**, samples "
            + "+ global theme CSS, gray-zone consumer-local Nn* (NnBlazorTermTab, NnBlazorTermTabFooter, "
            + "NnPerfMonitorPanel). Per-file opt-out: nnb_css011:allow-reachin: <reason>. Kill-switch: "
            + "<CssAnalyzerEnabled>false</CssAnalyzerEnabled>. Known false-negatives: dynamic "
            + "Class=\"@expr\" (not literal-matchable); inline Style= (owned by NNB044).",
        helpLinkUri: HelpBase + "NNB_CSS011");

    // ── Regex patterns ────────────────────────────────────────────────────

    /// <summary>
    /// Matches a single plain CSS class token (the leading dot is required). Used to read the SUBJECT's
    /// class; namespaced <c>.nns-*</c>/<c>.mud-*</c> subjects are filtered out separately (they are
    /// CSS008/009's concern, not this rule's).
    /// </summary>
    private static readonly Regex ClassToken = new(
        @"\.[A-Za-z_][A-Za-z0-9_-]*",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches an <c>Nn*</c> component open-tag and captures its simple name (group <c>name</c>) plus the
    /// full open-tag span (up to the first <c>&gt;</c>, group <c>attrs</c> = everything after the name).
    /// The <c>Nn[A-Z]</c> boundary requires a capital after <c>Nn</c> so <c>Nnsomething</c> / plain words
    /// don't match; the component must be a recognized PascalCase <c>Nn*</c> tag. Multiline/Singleline so
    /// a tag spanning several lines is captured whole.
    /// </summary>
    private static readonly Regex NnOpenTag = new(
        @"<(?<name>Nn[A-Z]\w*)(?<attrs>[^>]*?)/?>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Path segments identifying vendor/third-party files to skip (mirrors CssNnReachInAnalyzer).</summary>
    private static readonly string[] VendorPathSegments =
    {
        "/lib/", "/node_modules/", "/xterm", "/prism", "/tiny-mde"
    };

    /// <summary>
    /// GRAY-ZONE consumer-local <c>Nn*</c> components (VOW-local per Novaleaf.VibeOverwatch/AGENTS.md):
    /// they retain the <c>Nn*</c> prefix as branding markers but cannot relocate into NnDesign without
    /// inverting the dependency graph, so they live in the consumer assembly. Styling them is consumer-
    /// local, not a design-system reach-in → never flagged.
    /// </summary>
    private static readonly HashSet<string> GrayZoneComponents = new(StringComparer.Ordinal)
    {
        "NnBlazorTermTab",
        "NnBlazorTermTabFooter",
        "NnPerfMonitorPanel",
    };

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleNoConsumerNnStyling);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    // ── Main entry point ────────────────────────────────────────────────────

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        // Shared kill-switch with the rest of the CSS modernization rules.
        if (IsOptedOut(context))
            return;

        // Index .razor AdditionalFiles by path so the paired-file lookup is O(1) per candidate CSS file.
        var razorByPath = BuildRazorIndex(context);

        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeCssFile(context, file, razorByPath);
        }
    }

    /// <summary>
    /// Builds a path → <c>.razor</c> AdditionalText index (normalized to forward slashes,
    /// case-insensitive) so a candidate <c>Foo.razor.css</c> can resolve its paired <c>Foo.razor</c>.
    /// </summary>
    private static Dictionary<string, AdditionalText> BuildRazorIndex(CompilationAnalysisContext context)
    {
        var index = new Dictionary<string, AdditionalText>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in context.Options.AdditionalFiles)
        {
            var path = file.Path;
            if (string.IsNullOrEmpty(path))
                continue;
            // .razor (but NOT .razor.css — that ends with .css, handled on the CSS side).
            if (path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
            {
                index[path.Replace('\\', '/')] = file;
            }
        }
        return index;
    }

    private static void AnalyzeCssFile(
        CompilationAnalysisContext context, AdditionalText file,
        Dictionary<string, AdditionalText> razorByPath)
    {
        var path = file.Path;
        if (string.IsNullOrEmpty(path) || IsVendorFile(path))
            return;

        // Only component-scoped CSS (.razor.css) can pair with a .razor and carry a ::deep reach-in.
        // Global .css has no paired component, so it cannot bind a class to an Nn* root by construction.
        if (!path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
            return;

        // Path-bucket exemption — producer CSS, samples, global theme.
        if (IsExceptedPath(path))
            return;

        // Resolve the paired .razor (strip the trailing ".css"). No pair → cannot correlate → skip.
        var razorPath = path.Substring(0, path.Length - ".css".Length).Replace('\\', '/');
        if (!razorByPath.TryGetValue(razorPath, out var razorFile))
            return;

        var cssSource = file.GetText(context.CancellationToken);
        if (cssSource == null || cssSource.Length == 0)
            return;

        var cssText = cssSource.ToString();

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the sibling analyzers).
        if (HasOptOutComment(cssText))
            return;

        var razorSource = razorFile.GetText(context.CancellationToken);
        if (razorSource == null || razorSource.Length == 0)
            return;

        var razorText = razorSource.ToString();

        // Map: consumer-class token (sans dot) → the Nn* component simple-name(s) it is literally bound
        // to in the paired .razor. A class is reportable only if it appears in this map (and the bound
        // component is not gray-zone).
        var classToNnComponent = BuildClassBindingMap(razorText);
        if (classToNnComponent.Count == 0)
            return;

        var comments = FindCssCommentRanges(cssText);
        AnalyzeSelectors(context, file.Path, cssSource, cssText, comments, classToNnComponent);
    }

    // ── Paired .razor → class-binding map ─────────────────────────────────────

    /// <summary>
    /// Scans the paired <c>.razor</c> markup for <c>Nn*</c> open-tags carrying a LITERAL
    /// <c>Class="…"</c> attribute and maps each space-separated class token → the bound component's
    /// simple name. Dynamic <c>Class="@expr"</c> values are skipped (not literal-matchable). Gray-zone
    /// components are excluded here so they never produce a reportable binding.
    /// </summary>
    private static Dictionary<string, string> BuildClassBindingMap(string razorText)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match tag in NnOpenTag.Matches(razorText))
        {
            var componentName = tag.Groups["name"].Value;
            if (GrayZoneComponents.Contains(componentName))
                continue;

            var attrs = tag.Groups["attrs"].Value;
            var classValue = ExtractLiteralClassValue(attrs);
            if (classValue == null)
                continue; // no Class= attribute, or a dynamic @-bound value.

            foreach (var token in classValue.Split(
                         new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // First binding wins for the message component name (a class bound to multiple Nn*
                // components is still a single violation per CSS rule; the name is illustrative).
                if (!map.ContainsKey(token))
                    map[token] = componentName;
            }
        }

        return map;
    }

    /// <summary>
    /// Extracts the LITERAL value of a <c>Class="…"</c> attribute from an open-tag's attribute text.
    /// Returns <c>null</c> when there is no <c>Class</c> attribute, or when its value is a dynamic Razor
    /// expression (<c>Class="@…"</c> — leading <c>@</c> after optional whitespace), which cannot be
    /// literal-matched (documented false-negative). Case-insensitive on the attribute name
    /// (<c>Class</c>/<c>class</c>); a longer attribute ending in <c>class</c> (e.g. <c>BodyClass</c>) is
    /// NOT matched — a leading boundary guard isolates the standalone attribute.
    /// </summary>
    private static string? ExtractLiteralClassValue(string attrs)
    {
        var m = ClassAttr.Match(attrs);
        if (!m.Success)
            return null;

        var value = m.Groups["value"].Value;
        // Dynamic value: a Razor expression. Leading '@' (after trimming) → not literal-matchable.
        if (value.TrimStart().StartsWith("@", StringComparison.Ordinal))
            return null;

        return value;
    }

    /// <summary>
    /// Matches a standalone <c>Class="…"</c> attribute (case-insensitive name), capturing the quoted
    /// value. The <c>(?&lt;![\w])</c> look-behind ensures the attribute is standalone — it will not match
    /// the <c>Class</c> tail of <c>BodyClass=</c> / <c>HeaderClass=</c> producer params (those are
    /// Tier-B's concern, not a consumer-class binding).
    /// </summary>
    private static readonly Regex ClassAttr = new(
        @"(?<![\w])[Cc]lass\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    // ── Selector analysis (subject = consumer class under ::deep) ──────────────

    private static void AnalyzeSelectors(
        CompilationAnalysisContext ctx, string filePath, SourceText src, string text,
        List<(int Start, int End)> comments, Dictionary<string, string> classToNnComponent)
    {
        var selectorStart = 0;
        var depth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (IsInComment(comments, i))
                continue;

            if (ch == '{')
            {
                if (depth == 0)
                {
                    var selectorList = text.Substring(selectorStart, i - selectorStart);
                    var trimmed = selectorList.TrimStart();
                    if (!trimmed.StartsWith("@", StringComparison.Ordinal))
                    {
                        AnalyzeSelectorList(ctx, filePath, src, selectorList, selectorStart, comments,
                            classToNnComponent);
                    }
                }
                depth++;
            }
            else if (ch == '}')
            {
                if (depth > 0)
                    depth--;
                selectorStart = i + 1;
            }
        }
    }

    private static void AnalyzeSelectorList(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        string selectorList, int listOffset, List<(int Start, int End)> comments,
        Dictionary<string, string> classToNnComponent)
    {
        var partStart = 0;
        for (var i = 0; i <= selectorList.Length; i++)
        {
            var atEnd = i == selectorList.Length;
            if (atEnd || selectorList[i] == ',')
            {
                var part = selectorList.Substring(partStart, i - partStart);
                AnalyzeSingleSelector(ctx, filePath, src, part, listOffset + partStart, comments,
                    classToNnComponent);
                partStart = i + 1;
            }
        }
    }

    private static void AnalyzeSingleSelector(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        string selector, int selectorOffset, List<(int Start, int End)> comments,
        Dictionary<string, string> classToNnComponent)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return;

        // The ::deep pseudo is the REQUIRED necessary condition — it is what pierces the scoped boundary
        // into the child Nn* component's DOM. A selector with no ::deep styles the consumer's OWN element
        // (own-element styling never carries ::deep), which is not an Nn*-element reach-in.
        if (selector.IndexOf("::deep", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        // The SUBJECT is the rightmost compound selector (everything after the last combinator / ::deep).
        var subjectStart = FindSubjectStart(selector);
        var subject = selector.Substring(subjectStart);

        // Examine each plain class token in the subject. The FIRST class token that resolves to an Nn*
        // binding is the violation (a subject typically has one class). Namespaced .nns-*/.mud-* tokens
        // are NOT this rule's concern (CSS008/009 own them) — they will simply not appear in the binding
        // map, so they are skipped naturally.
        foreach (Match m in ClassToken.Matches(subject))
        {
            var token = m.Value;                 // e.g. ".vow-metaside-actions-section"
            var bare = token.Substring(1);       // strip leading dot

            // .nns-*/.mud-* are explicitly out of scope (sibling analyzers own them).
            if (bare.StartsWith("nns-", StringComparison.Ordinal) ||
                bare.StartsWith("mud-", StringComparison.Ordinal))
                continue;

            if (!classToNnComponent.TryGetValue(bare, out var componentName))
                continue;

            var absoluteIndex = selectorOffset + subjectStart + m.Index;
            if (IsInComment(comments, absoluteIndex))
                continue;

            Report(ctx, filePath, src, absoluteIndex, m.Length, bare, componentName);
            // One diagnostic per selector — the subject is a single styled element.
            return;
        }
    }

    /// <summary>
    /// Returns the start index (within <paramref name="selector"/>) of the SUBJECT — the rightmost
    /// compound selector. Mirrors <see cref="CssNnReachInAnalyzer.FindSubjectStart"/>: everything before
    /// this index is ancestor / state-gate / <c>::deep</c> context. The subject begins after the last
    /// top-level combinator (descendant whitespace, <c>&gt;</c>, <c>+</c>, <c>~</c>); <c>::deep</c>
    /// resolves to a whitespace boundary (it is followed by whitespace then the subject).
    /// </summary>
    private static int FindSubjectStart(string selector)
    {
        var end = selector.Length;
        while (end > 0 && char.IsWhiteSpace(selector[end - 1]))
            end--;

        var i = end - 1;
        while (i >= 0)
        {
            var ch = selector[i];
            if (ch == '>' || ch == '+' || ch == '~' || char.IsWhiteSpace(ch))
            {
                var start = i + 1;
                while (start < end && (char.IsWhiteSpace(selector[start]) ||
                       selector[start] == '>' || selector[start] == '+' || selector[start] == '~'))
                    start++;
                return start;
            }
            i--;
        }
        return 0;
    }

    // ── Exemptions (ported from CssNnReachInAnalyzer) ─────────────────────────

    private static bool IsExceptedPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var p = filePath.Replace('\\', '/');

        if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        var fileName = Path.GetFileName(p);
        return AllowedFileNames.Contains(fileName);
    }

    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nn-design-samples.css",
        "app.css",
    };

    private static bool HasOptOutComment(string fileText)
    {
        if (string.IsNullOrEmpty(fileText))
            return false;
        return fileText.IndexOf(OptOutMarker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsOptedOut(CompilationAnalysisContext context)
    {
        return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                   .TryGetValue("build_property.CssAnalyzerEnabled", out var value) &&
               string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVendorFile(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');

        if (normalized.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var segment in VendorPathSegments)
        {
            if (normalized.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    // ── Reporting + comment infra (mirrors CssNnReachInAnalyzer) ───────────────

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string className, string componentName)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(
            RuleNoConsumerNnStyling, location, className, componentName));
    }

    private static List<(int Start, int End)> FindCssCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '/' && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i < text.Length - 1)
                {
                    if (text[i] == '*' && text[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }
                    i++;
                }
                ranges.Add((start, i));
            }
            else
            {
                i++;
            }
        }
        return ranges;
    }

    private static bool IsInComment(List<(int Start, int End)> ranges, int position)
    {
        foreach (var (s, e) in ranges)
        {
            if (position >= s && position < e) return true;
            if (s > position) break;
        }
        return false;
    }
}
