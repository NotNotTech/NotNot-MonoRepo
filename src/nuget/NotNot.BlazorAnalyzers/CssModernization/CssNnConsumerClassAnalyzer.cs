using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// compound, computed by <see cref="CssSelectorScanner.FindSubjectStart"/>) is a PLAIN consumer <c>.class</c> token (NOT
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
        "Consumer CSS class '.{0}' (in {2}) lands on the Nn* component '<{1}>'. A consumer never "
            + "styles an Nn* element — visual OR layout. Resolve: (1) DELETE the rule if it restates a "
            + "producer default (fix the NnDesign default once); (2) use/add an NnDesign Tier-A param or "
            + "a Layout Contract mode (Fill / MaxHeight / data-nns-fill); (3) only if genuinely intended, "
            + "add a per-file nnb_css011:allow-reachin comment. Relocating the CSS between a .razor.css "
            + "::deep rule and an inline <style> block is NOT a fix — both are the same violation. "
            + "(NNB_CSS011).",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Tier A/B govern Nn* PARAMETERS; this governs consumer CSS. A consumer-authored CSS "
            + "declaration must never land on an Nn* element, regardless of delivery — internal-class "
            + "reach-in (NNB_CSS008/009) OR a consumer-owned class applied to the Nn* via two CSS "
            + "deliveries: a scoped .razor.css '::deep .my-class { … }' rule, OR an inline .razor "
            + "'<style> .my-class { … } </style>' block (the inline block is unscoped/global, so it needs "
            + "no ::deep — the plain class selector IS the violation). Both bind '.my-class' to an Nn* "
            + "root via a literal Class=\"…\" in the same/paired .razor (e.g. a section-root min-height "
            + "floor that silently defeats the component's collapse). Relocating CSS between the two "
            + "deliveries is NOT a fix — both are the same violation. Property-agnostic (layout counts "
            + "as much as visual). Sanctioned alternative: a Layout Contract mode (Fill / MaxHeight / "
            + "data-nns-fill) or a Tier-A param — never restyle the Nn* element. Exempt: NnDesign "
            + "producer / NotNot.BlazorDesign internals, NnDesignSamples/** + Pages/Samples/**, samples "
            + "+ global theme CSS, gray-zone consumer-local Nn* (NnBlazorTermTab, NnBlazorTermTabFooter, "
            + "NnPerfMonitorPanel). Per-file opt-out: nnb_css011:allow-reachin: <reason>. Kill-switch: "
            + "<CssAnalyzerEnabled>false</CssAnalyzerEnabled>. Known false-negatives: dynamic "
            + "Class=\"@expr\" (not literal-matchable, both deliveries); @media/@supports-nested rules "
            + "(top-level @-blocks are skipped, both deliveries); inline Style= attribute (owned by "
            + "NNB044).",
        helpLinkUri: HelpBase + "NNB_CSS011",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

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

    /// <summary>
    /// Matches an inline <c>&lt;style&gt;…&lt;/style&gt;</c> block in a <c>.razor</c>, capturing the
    /// INNER CSS text (group <c>body</c>) so its absolute offset within the <c>.razor</c> is recoverable
    /// via <c>match.Groups["body"].Index</c>. Singleline so a multi-line block is captured whole;
    /// IgnoreCase so <c>&lt;Style&gt;</c>/<c>&lt;STYLE&gt;</c> match; non-greedy <c>body</c> so multiple
    /// blocks in one file are matched independently.
    /// </summary>
    private static readonly Regex StyleBlock = new(
        @"<style[^>]*>(?<body>.*?)</style>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Matches a Razor server-comment span <c>@* … *@</c> (non-greedy, Singleline). Used by
    /// <see cref="StripRazorAndHtmlComments"/> to blank dead markup before <c>&lt;style&gt;</c> extraction
    /// and binding-map construction, so a commented-out <c>&lt;style&gt;</c>+<c>&lt;Nn* Class&gt;</c> pair
    /// never false-positives on dead code (the inline path scans <c>.razor</c>, which — unlike pure
    /// <c>.razor.css</c> — carries Razor/HTML comment forms the CSS comment scanner cannot see).
    /// </summary>
    private static readonly Regex RazorComment = new(
        @"@\*.*?\*@",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Matches an HTML comment span <c>&lt;!-- … --&gt;</c> (non-greedy, Singleline).</summary>
    private static readonly Regex HtmlComment = new(
        @"<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

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
        if (CssConsumerExemptions.IsOptedOut(context))
            return;

        // Index .razor AdditionalFiles by path so the paired-file lookup is O(1) per candidate CSS file.
        var razorByPath = BuildRazorIndex(context);

        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeCssFile(context, file, razorByPath);
        }

        // SECOND delivery surface: an inline <style> block authored directly in a .razor whose plain
        // (non-::deep) class selector binds to an <Nn*> via a static-literal Class="…" in the SAME
        // .razor. Inline <style> is unscoped (global) — the same consumer-CSS-on-Nn* violation by a
        // different delivery. Reuses the same engine/exemptions; the only difference is requireDeep:false.
        foreach (var razorFile in razorByPath.Values)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeRazorInlineStyle(context, razorFile);
        }
    }

    /// <summary>
    /// Detects the inline-<c>&lt;style&gt;</c> delivery: a plain class selector inside an inline
    /// <c>&lt;style&gt;</c> block in a <c>.razor</c> whose class binds to an <c>Nn*</c> via a static
    /// literal <c>Class="…"</c> in the SAME <c>.razor</c>. Reuses the binding map, exemptions, selector
    /// engine, and reporting verbatim; the <c>::deep</c> necessary-condition gate is disabled
    /// (<c>requireDeep:false</c>) because an inline block is already global, so any class selector whose
    /// subject binds to an <c>Nn*</c> is the violation.
    /// </summary>
    private static void AnalyzeRazorInlineStyle(
        CompilationAnalysisContext context, AdditionalText razorFile)
    {
        var path = razorFile.Path;
        if (string.IsNullOrEmpty(path) || CssConsumerExemptions.IsVendorFile(path) ||
            CssConsumerExemptions.IsExceptedPath(path))
            return;

        var razorSource = razorFile.GetText(context.CancellationToken);
        if (razorSource == null || razorSource.Length == 0)
            return;

        var razorText = razorSource.ToString();

        // Perf early-return: no inline <style> anywhere → no inline delivery to analyze. OrdinalIgnoreCase
        // to match StyleBlock's IgnoreCase (a case-sensitive guard would skip <Style>/<STYLE>, dropping a
        // real violation). Runs BEFORE the comment-strip + map build so the common no-<style> .razor pays
        // only one cheap scan.
        if (razorText.IndexOf("<style", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the scoped path).
        if (CssConsumerExemptions.HasOptOutComment(razorText, OptOutMarker))
            return;

        // STRIP Razor (@* *@) and HTML (<!-- -->) comment spans BEFORE <style>-block extraction AND the
        // binding map, so a commented-out <style>+<Nn* Class> pair never false-positives on dead code.
        // Comment spans are blanked with same-length whitespace so every surviving character keeps its
        // absolute offset (line/column reporting stays correct).
        var scrubbed = StripRazorAndHtmlComments(razorText);

        // Build the class→Nn* binding map over the .razor MARKUP only, with live <style> block BODIES
        // blanked (same-length whitespace, identical technique to the comment-strip). Nn* tags live in
        // real markup, never in CSS — but tag-shaped text inside a <style> body (e.g.
        // content: '<NnSidebarPanel Class="sidebar">') would otherwise manufacture a FALSE binding. The
        // per-block SELECTOR scan below still iterates `scrubbed` (real <style> bodies intact); only the
        // binding-map INPUT is style-body-blanked. Blanking is same-length so offsets are unaffected.
        var markupForBinding = StripStyleBlockBodies(scrubbed);
        var classToNnComponent = BuildClassBindingMap(markupForBinding);
        if (classToNnComponent.Count == 0)
            return;

        // Analyze each inline <style> block at its own absolute offset within the .razor.
        foreach (Match block in StyleBlock.Matches(scrubbed))
        {
            var body = block.Groups["body"].Value;
            if (body.Length == 0)
                continue;

            var baseOffset = block.Groups["body"].Index;
            // Comment ranges are block-relative (computed over `body`); the selector scan is also
            // block-relative. baseOffset is applied ONCE, at the Report boundary.
            var comments = CssSelectorScanner.FindCssCommentRanges(body);
            CssSelectorScanner.ForEachSelector(body, comments,
                (selector, offset) => AnalyzeSingleSelector(
                    context, path, razorSource, selector, offset, comments, classToNnComponent,
                    requireDeep: false, deliveryNoun: "an inline <style> block", baseOffset: baseOffset));
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
        if (string.IsNullOrEmpty(path) || CssConsumerExemptions.IsVendorFile(path))
            return;

        // Only component-scoped CSS (.razor.css) can pair with a .razor and carry a ::deep reach-in.
        // Global .css has no paired component, so it cannot bind a class to an Nn* root by construction.
        if (!path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
            return;

        // Path-bucket exemption — producer CSS, samples, global theme.
        if (CssConsumerExemptions.IsExceptedPath(path))
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
        if (CssConsumerExemptions.HasOptOutComment(cssText, OptOutMarker))
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

        var comments = CssSelectorScanner.FindCssCommentRanges(cssText);
        CssSelectorScanner.ForEachSelector(cssText, comments,
            (selector, offset) => AnalyzeSingleSelector(
                context, file.Path, cssSource, selector, offset, comments, classToNnComponent,
                requireDeep: true, deliveryNoun: "a scoped ::deep rule", baseOffset: 0));
    }

    /// <summary>
    /// Blanks Razor (<c>@* … *@</c>) and HTML (<c>&lt;!-- … --&gt;</c>) comment spans in a <c>.razor</c>
    /// by replacing each with the same number of space characters. Same-length replacement preserves the
    /// absolute offset of every surviving character so downstream line/column reporting is unaffected.
    /// Dead (commented-out) <c>&lt;style&gt;</c> blocks and <c>&lt;Nn* Class&gt;</c> bindings disappear,
    /// closing the commented-block false-positive vector unique to the <c>.razor</c> inline surface.
    /// </summary>
    private static string StripRazorAndHtmlComments(string razorText)
    {
        string Blank(Match m) => new string(' ', m.Length);
        var afterRazor = RazorComment.Replace(razorText, Blank);
        return HtmlComment.Replace(afterRazor, Blank);
    }

    /// <summary>
    /// Blanks the BODY of every live <c>&lt;style&gt;…&lt;/style&gt;</c> block (the captured
    /// <c>body</c> group) with same-length whitespace, leaving the <c>&lt;style&gt;</c> open/close tags
    /// intact. Used ONLY to build the class→<c>Nn*</c> binding map from real markup: tag-shaped CSS text
    /// inside a <c>&lt;style&gt;</c> body (e.g. <c>content: '&lt;NnSidebarPanel Class="sidebar"&gt;'</c>)
    /// would otherwise be matched by <see cref="NnOpenTag"/> and manufacture a FALSE binding. Nn* tags
    /// live in real markup, never in CSS, so blanking style bodies removes only spurious matches.
    /// Same-length replacement preserves every surviving character's absolute offset.
    /// </summary>
    private static string StripStyleBlockBodies(string razorText)
    {
        return StyleBlock.Replace(razorText, m =>
        {
            var body = m.Groups["body"];
            var sb = new System.Text.StringBuilder(m.Value);
            // body.Index is absolute; offset into the match's own value to blank in place.
            var bodyStartInMatch = body.Index - m.Index;
            for (var i = 0; i < body.Length; i++)
                sb[bodyStartInMatch + i] = ' ';
            return sb.ToString();
        });
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
    // The selector-list walk (top-level spans → comma-split individuals) is the shared
    // CssSelectorScanner.ForEachSelector engine; each call site passes a closure capturing this rule's
    // classToNnComponent / requireDeep / deliveryNoun / baseOffset. The per-selector classification +
    // cross-file binding lookup stay here.

    private static void AnalyzeSingleSelector(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        string selector, int selectorOffset, List<(int Start, int End)> comments,
        Dictionary<string, string> classToNnComponent,
        bool requireDeep, string deliveryNoun, int baseOffset)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return;

        // The ::deep pseudo is the necessary condition for the SCOPED .razor.css delivery — it is what
        // pierces the scoped boundary into the child Nn* component's DOM. A scoped selector with no
        // ::deep styles the consumer's OWN element (own-element styling never carries ::deep), which is
        // not an Nn*-element reach-in. The inline-<style> delivery (requireDeep:false) is already global,
        // so any plain class selector whose subject binds to an Nn* is the violation — ::deep is inert
        // there and NOT required.
        if (requireDeep && selector.IndexOf("::deep", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        // The SUBJECT is the rightmost compound selector (everything after the last combinator / ::deep).
        var subjectStart = CssSelectorScanner.FindSubjectStart(selector);
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

            // scanIndex is BLOCK-RELATIVE (relative to the text that was scanned + its comment ranges)
            // — it is the ONLY value passed to IsInComment, whose ranges are in the same coordinate
            // space. The ABSOLUTE offset into `src` is baseOffset + scanIndex, computed ONCE at the
            // Report boundary (scoped path: baseOffset == 0, so behavior is identical).
            var scanIndex = selectorOffset + subjectStart + m.Index;
            if (CssSelectorScanner.IsInComment(comments, scanIndex))
                continue;

            Report(ctx, filePath, src, baseOffset + scanIndex, m.Length, bare, componentName,
                deliveryNoun);
            // One diagnostic per selector — the subject is a single styled element.
            return;
        }
    }

    // ── Reporting (shared exemption / scanner infra lives in CssConsumerExemptions / CssSelectorScanner) ──

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string className, string componentName, string deliveryNoun)
    {
        // `position` is the ABSOLUTE offset into `src` (the full .razor.css or .razor SourceText) — both
        // the TextSpan and the line/column lookups use it, so span-offset and line/column agree.
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(
            RuleNoConsumerNnStyling, location, className, componentName, deliveryNoun));
    }
}
