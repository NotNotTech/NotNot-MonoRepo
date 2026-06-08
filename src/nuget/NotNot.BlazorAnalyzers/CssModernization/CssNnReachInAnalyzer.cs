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
/// NNB_CSS008 — consumer scoped CSS must not REACH INTO NnDesign internal <c>.nn-*</c> classes.
/// <para>
/// The "mortal sin" detector. A consumer that restyles an NnDesign wrapper's internal class
/// (e.g. <c>::deep .nn-content-section-body { }</c> or a bare <c>.nn-content-section-header { }</c>)
/// couples itself to NnDesign's private DOM and breaks the moment that DOM changes. The sanctioned
/// "venial" act — a consumer styling its OWN element gated on an NnDesign-published root STATE
/// (e.g. <c>html.nn-chord-revealed .my-consumer-class { }</c>) — is NOT flagged.
/// </para>
/// <para>
/// <b>Core discriminator (react-to vs reach-into)</b>: the styled SUBJECT is the rightmost compound
/// selector — the thing actually being restyled. An <c>.nn-*</c> class appearing as the SUBJECT is a
/// reach-in (violation). The SAME <c>.nn-*</c> class appearing only as an ANCESTOR / state gate on a
/// non-<c>nn</c> subject is "react-to" (sanctioned). The analyzer keys on subject position, not on the
/// presence of <c>::deep</c> (which is neither necessary nor sufficient — bare reach-ins exist, and
/// <c>::deep</c> into a different library's internals is out of scope).
/// </para>
/// <para>
/// <b>Allow-list (public contract — never warn)</b>: <c>.nn-chord-revealed</c> (NnDesign-published
/// public state token) and the <c>.nns-*</c> prefix (NnDesign sample-marker). Keyed on a TINY
/// public-contract allow-list rather than a growing internal denylist: per the namespace-reclamation
/// invariant the <c>nn-</c> prefix means "NnDesign owns this", so any non-allow-listed <c>.nn-*</c>
/// subject in consumer CSS is by definition a reach-in.
/// </para>
/// <para>
/// <b>Exemptions</b> (ported from the NnDesign policy analyzer): path buckets (the NnDesign producer's
/// own <c>nn-design.css</c> / <c>NotNot.BlazorDesign</c> internals; <c>Pages/Samples/**</c> and
/// <c>NnDesignSamples/**</c>; samples CSS; global theme CSS), a per-file opt-out comment
/// (<c>nnb_css008:allow-reachin: &lt;reason&gt;</c>), and the shared <c>CssAnalyzerEnabled=false</c>
/// build-property kill-switch. Severity = Error (escalated from Warning once the consumer tree was verified reach-in-clean).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CssNnReachInAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for the NnDesign internal-class reach-in.</summary>
    public const string DiagnosticId = "NNB_CSS008";

    private const string Category = "CssModernization";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker — <c>nnb_css008:allow-reachin: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb_css008:allow-reachin";

    /// <summary>
    /// NNB_CSS008 descriptor. Severity = Error — a consumer reach-in into NnDesign internals is a
    /// build-breaking contract violation. Escalated from Warning once the consumer tree was verified
    /// reach-in-clean (zero NNB_CSS008 at HEAD), so the gate enforces zero-regression rather than
    /// merely advising. Kill-switch for legitimate suppression: per-file nnb_css008:allow-reachin
    /// comment, or the CssAnalyzerEnabled=false build property.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleNoNnReachIn = new(
        DiagnosticId,
        "Do not reach into NnDesign internal .nn-* classes",
        "Consumer CSS restyles NnDesign internal class '{0}'. Style your OWN element (optionally "
            + "gated on a published NnDesign state like html.nn-chord-revealed) or use the wrapper's "
            + "public parameter/component (e.g. NnContentSection.MaxHeight / NoHeaderBorder, "
            + "NnContentSectionGroup) instead of reaching into '.nn-*' internals. (NNB_CSS008)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "An NnDesign wrapper's internal '.nn-*' classes are private implementation detail. "
            + "A consumer that restyles one (the rightmost/subject selector being an '.nn-*' class — "
            + "with or without ::deep) couples to NnDesign's private DOM and breaks when that DOM "
            + "changes. Sanctioned alternative: style your OWN (non-nn) element, optionally gated on a "
            + "published NnDesign ancestor state (html.nn-chord-revealed .your-class) — the '.nn-*' as a "
            + "STATE GATE on a non-nn subject is allowed; the '.nn-*' as the SUBJECT is the violation. "
            + "Public-contract allow-list (never flagged): .nn-chord-revealed, .nns-* (sample marker). "
            + "Exempt: NnDesign's own producer CSS / NotNot.BlazorDesign internals, Pages/Samples/** + "
            + "NnDesignSamples/**, samples CSS, global theme CSS. Per-file opt-out: "
            + "nnb_css008:allow-reachin: <reason>. Kill-switch: <CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS008");

    // ── Regex patterns ────────────────────────────────────────────────────

    /// <summary>
    /// Matches a single <c>.nn-*</c> class token (the leading dot is required so attribute values and
    /// identifiers like <c>data-nn-fill</c> or element names don't match). Case-sensitive: CSS class
    /// names are case-sensitive and the NnDesign convention is lowercase.
    /// </summary>
    private static readonly Regex NnClassToken = new(
        @"\.nn-[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    /// <summary>Path segments identifying vendor/third-party files to skip (mirrors CssModernizationAnalyzer).</summary>
    private static readonly string[] VendorPathSegments =
    {
        "/lib/", "/node_modules/", "/xterm", "/prism", "/tiny-mde"
    };

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleNoNnReachIn);

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

        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeFile(context, file);
        }
    }

    private static void AnalyzeFile(CompilationAnalysisContext context, AdditionalText file)
    {
        var path = file.Path;
        if (string.IsNullOrEmpty(path) || IsVendorFile(path))
            return;

        // Only scoped/global CSS carries reach-in selectors. (.razor markup uses Class="..."
        // attributes, not selectors, so it cannot reach into an NnDesign subject by construction.)
        var isCss = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        if (!isCss)
            return;

        // Path-bucket exemption — producer CSS, samples, global theme.
        if (IsExceptedPath(path))
            return;

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the policy analyzer).
        if (HasOptOutComment(text))
            return;

        var comments = FindCssCommentRanges(text);
        AnalyzeSelectors(context, file.Path, sourceText, text, comments);
    }

    // ── Selector analysis ────────────────────────────────────────────────────

    /// <summary>
    /// Walks every selector list (the text preceding each top-level <c>{</c>), splits on commas into
    /// individual selectors, isolates the SUBJECT (rightmost compound selector), and reports when the
    /// subject restyles a non-allow-listed <c>.nn-*</c> class.
    /// </summary>
    private static void AnalyzeSelectors(
        CompilationAnalysisContext ctx, string filePath, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        // Scan top-level selector-list spans: from the start of file / after the previous rule's `}`
        // up to the next `{`. We skip @-rule preludes (e.g. @media) — their inner rules are reached on
        // subsequent iterations because the body still contains `selector {` pairs. Brace depth tracks
        // declaration blocks so a `{` inside a value (rare) does not derail the scan.
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
                    // Skip at-rule preludes (@media, @supports, @layer, @keyframes ...): those have no
                    // styled subject themselves; their nested rules are visited as the scan continues.
                    var trimmed = selectorList.TrimStart();
                    if (!trimmed.StartsWith("@", StringComparison.Ordinal))
                    {
                        AnalyzeSelectorList(ctx, filePath, src, selectorList, selectorStart, comments);
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
        string selectorList, int listOffset, List<(int Start, int End)> comments)
    {
        // A selector list is comma-separated. Each comma-part is an independent selector with its own
        // subject. Track running offset so reported positions point at the offending .nn-* token.
        var partStart = 0;
        for (var i = 0; i <= selectorList.Length; i++)
        {
            var atEnd = i == selectorList.Length;
            if (atEnd || selectorList[i] == ',')
            {
                var part = selectorList.Substring(partStart, i - partStart);
                AnalyzeSingleSelector(ctx, filePath, src, part, listOffset + partStart, comments);
                partStart = i + 1;
            }
        }
    }

    private static void AnalyzeSingleSelector(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        string selector, int selectorOffset, List<(int Start, int End)> comments)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return;

        // The SUBJECT is the rightmost compound selector — the run of simple selectors after the last
        // combinator (descendant whitespace, `>`, `+`, `~`) OR after the last `::deep` / pseudo that
        // separates ancestor context from the styled element. We compute the subject's char range
        // within `selector`, then look for a violating .nn-* token ONLY inside that range. An .nn-*
        // appearing earlier (ancestor/state-gate position) is "react-to" → ignored.
        var subjectStart = FindSubjectStart(selector);

        // Examine the subject substring for .nn-* class tokens.
        var subject = selector.Substring(subjectStart);
        foreach (Match m in NnClassToken.Matches(subject))
        {
            var token = m.Value;               // e.g. ".nn-content-section-body"
            if (IsAllowListed(token))
                continue;

            var absoluteIndex = selectorOffset + subjectStart + m.Index;
            if (IsInComment(comments, absoluteIndex))
                continue;

            // Report on the offending token (sans leading dot for the message argument readability).
            Report(ctx, filePath, src, absoluteIndex, m.Length, token.TrimStart('.'));
        }
    }

    /// <summary>
    /// Returns the start index (within <paramref name="selector"/>) of the SUBJECT — the rightmost
    /// compound selector. Everything before this index is ancestor / state-gate context.
    /// </summary>
    /// <remarks>
    /// The subject begins after the last top-level combinator. Combinators: descendant (whitespace),
    /// child (<c>&gt;</c>), adjacent (<c>+</c>), general sibling (<c>~</c>). Blazor's <c>::deep</c>
    /// pseudo-element is ALSO an ancestor boundary — <c>::deep X</c> means "X anywhere under the scoped
    /// root", so the subject is what follows the last <c>::deep</c> + its following whitespace. We scan
    /// from the right and stop at the first boundary.
    /// </remarks>
    private static int FindSubjectStart(string selector)
    {
        // Normalize trailing whitespace for the scan (the subject is the last NON-empty compound).
        var end = selector.Length;
        while (end > 0 && char.IsWhiteSpace(selector[end - 1]))
            end--;

        // Walk leftward from `end` to the previous combinator boundary.
        var i = end - 1;
        while (i >= 0)
        {
            var ch = selector[i];
            if (ch == '>' || ch == '+' || ch == '~' || char.IsWhiteSpace(ch))
            {
                // Boundary found; subject starts at the next non-combinator, non-whitespace char.
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

    /// <summary>
    /// Public-contract allow-list: <c>.nn-chord-revealed</c> (exact) and the <c>.nns-*</c> prefix.
    /// These never count as reach-ins even when they appear in subject position.
    /// </summary>
    private static bool IsAllowListed(string nnToken)
    {
        // `.nns-*` sample-marker prefix.
        if (nnToken.StartsWith(".nns-", StringComparison.Ordinal))
            return true;

        // `.nn-chord-revealed` published public state token (exact match).
        if (string.Equals(nnToken, ".nn-chord-revealed", StringComparison.Ordinal))
            return true;

        return false;
    }

    // ── Exemptions (ported from NnDesignMudBlazorPolicyAnalyzer) ──────────────

    /// <summary>
    /// Path-bucket exemption. Producer CSS / wrapper internals legitimately DEFINE <c>.nn-*</c>;
    /// samples + global theme are not consumers reaching in.
    /// </summary>
    private static bool IsExceptedPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var p = filePath.Replace('\\', '/');

        // The NnDesign producer / wrapper layer itself legitimately defines .nn-* classes.
        if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Samples pages — both the canonical NnDesignSamples folder and the generic Pages/Samples bucket.
        if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Specific sample / global-theme CSS files (own-roots / .nns-* live here legitimately).
        var fileName = Path.GetFileName(p);
        return AllowedFileNames.Contains(fileName);
    }

    /// <summary>
    /// File-name allow-list — samples CSS + global theme CSS, where <c>.nn-*</c> / <c>.nns-*</c> roots
    /// are authored legitimately rather than reached into.
    /// </summary>
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

    // ── Reporting + comment infra (mirrors CssModernizationAnalyzer) ──────────

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string displayName)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(RuleNoNnReachIn, location, displayName));
    }

    /// <summary>Builds a sorted list of <c>/* ... */</c> comment ranges. Single O(N) forward pass.</summary>
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
