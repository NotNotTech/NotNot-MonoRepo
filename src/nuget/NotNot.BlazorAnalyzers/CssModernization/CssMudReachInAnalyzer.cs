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
/// NNB_CSS009 — consumer scoped CSS must not REACH INTO MudBlazor internal <c>.mud-*</c> classes.
/// <para>
/// Sibling of <see cref="CssNnReachInAnalyzer"/> (NNB_CSS008, which guards the <c>.nns-*</c> namespace).
/// MudBlazor is the primitive layer wrapped internally by NnDesign; its <c>.mud-*</c> classes are
/// PRIVATE implementation detail two layers down from the consumer. A consumer that restyles one
/// (e.g. <c>::deep .mud-tab { min-width:80px }</c> or a bare <c>.mud-tabs-header { min-height:unset }</c>)
/// couples to MudBlazor's private DOM through the NnDesign wrapper and breaks the moment either layer's
/// DOM changes. The sanctioned alternative is the NnDesign wrapper's published contract (e.g. the
/// declarative-<c>NnTabs</c> trigger-sizing default + <c>FillPanels</c>/per-panel <c>data-nn-fill</c>),
/// NEVER a raw <c>.mud-*</c> restyle.
/// </para>
/// <para>
/// <b>Core discriminator (react-to vs reach-into)</b>: identical to NNB_CSS008. The styled SUBJECT is
/// the rightmost compound selector — the thing actually being restyled. A <c>.mud-*</c> class in SUBJECT
/// position is a reach-in (violation). The SAME <c>.mud-*</c> class as an ANCESTOR / state gate on a
/// non-<c>mud</c> subject is "react-to" (sanctioned). Keys on subject position, not on <c>::deep</c>
/// presence (bare reach-ins exist; <c>::deep</c> into a sibling-library is out of scope).
/// </para>
/// <para>
/// <b>Allow-list</b>: EMPTY. Unlike <c>.nns-*</c> (which publishes <c>.nns-chord-revealed</c> as a
/// public contract), MudBlazor exposes NO public <c>.mud-*</c> styling contract TO THIS CONSUMER — it
/// is wrapped, not consumed directly. Every non-exempt <c>.mud-*</c> subject in consumer CSS is by
/// definition a reach-in.
/// </para>
/// <para>
/// <b>Exemptions</b> (ported from <see cref="CssNnReachInAnalyzer"/>): path buckets (the NnDesign producer's
/// own <c>NotNot.BlazorDesign</c> internals, which legitimately restyle <c>.mud-*</c> internals as the
/// wrapper's job; <c>Pages/Samples/**</c> and <c>NnDesignSamples/**</c>; samples CSS; global theme CSS),
/// a per-file opt-out comment (<c>nnb_css009:allow-reachin: &lt;reason&gt;</c>), and the shared
/// <c>CssAnalyzerEnabled=false</c> build-property kill-switch. Severity = Error (the consumer tree is
/// verified <c>.mud-*</c>-reach-in-clean, so a new reach-in breaks the build).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CssMudReachInAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for the MudBlazor internal-class reach-in.</summary>
    public const string DiagnosticId = "NNB_CSS009";

    private const string Category = "CssModernization";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker — <c>nnb_css009:allow-reachin: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb_css009:allow-reachin";

    /// <summary>
    /// NNB_CSS009 descriptor. Severity = Error — the consumer tree is verified <c>.mud-*</c>-reach-in-clean
    /// (fires ZERO across the consumer CSS), so any NEW reach-in breaks the build (mirrors the NNB_CSS008
    /// Error path). Kill-switch for legitimate suppression: per-file nnb_css009:allow-reachin comment, or the
    /// CssAnalyzerEnabled=false build property; soften locally via dotnet_diagnostic.NNB_CSS009.severity.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleNoMudReachIn = new(
        DiagnosticId,
        "Do not reach into MudBlazor internal .mud-* classes",
        "Consumer CSS restyles MudBlazor internal class '{0}'. MudBlazor is the primitive layer wrapped "
            + "by NnDesign — its '.mud-*' classes are private implementation detail. Use the NnDesign "
            + "wrapper's published contract (e.g. NnTabs trigger-sizing default / FillPanels / per-panel "
            + "data-nn-fill, NnContentSection params) or style your OWN element instead of reaching into "
            + "'.mud-*' internals. If no wrapper contract covers the need, file an NnDesign-gap. (NNB_CSS009)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "MudBlazor '.mud-*' classes are the private DOM of the primitive layer that NnDesign "
            + "wraps. A consumer that restyles one (the rightmost/subject selector being a '.mud-*' class — "
            + "with or without ::deep) couples to MudBlazor's private DOM TWO layers down and breaks when "
            + "either NnDesign's wrapper DOM or MudBlazor's internal DOM changes. Sanctioned alternative: "
            + "use the NnDesign wrapper's published parameter/CSS contract (declarative-NnTabs owns "
            + "trigger sizing + fill/scroll via FillPanels / data-nn-fill), or style your OWN (non-mud) "
            + "element, optionally gated on a published ancestor state. The '.mud-*' as a STATE GATE on a "
            + "non-mud subject is allowed; the '.mud-*' as the SUBJECT is the violation. Allow-list: EMPTY "
            + "(MudBlazor publishes no public '.mud-*' styling contract to the consumer, unlike '.nns-*' "
            + "which publishes '.nns-chord-revealed'). Exempt: NnDesign's "
            + "own producer CSS / NotNot.BlazorDesign internals (the wrapper legitimately restyles "
            + "'.mud-*'), Pages/Samples/** + NnDesignSamples/**, samples CSS, global theme CSS. Per-file "
            + "opt-out: nnb_css009:allow-reachin: <reason>. Kill-switch: "
            + "<CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS009");

    // ── Regex patterns ────────────────────────────────────────────────────

    /// <summary>
    /// Matches a single <c>.mud-*</c> class token (the leading dot is required so attribute values and
    /// identifiers like <c>data-mud-*</c> or element names don't match). Case-sensitive: CSS class names
    /// are case-sensitive and the MudBlazor convention is lowercase.
    /// </summary>
    private static readonly Regex MudClassToken = new(
        @"\.mud-[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    /// <summary>Path segments identifying vendor/third-party files to skip (mirrors CssNnReachInAnalyzer).</summary>
    private static readonly string[] VendorPathSegments =
    {
        "/lib/", "/node_modules/", "/xterm", "/prism", "/tiny-mde"
    };

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleNoMudReachIn);

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
        // attributes, not selectors, so it cannot reach into a MudBlazor subject by construction.)
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

        // Per-file opt-out marker (whole-file scan, same coarse semantics as NNB_CSS008).
        if (HasOptOutComment(text))
            return;

        var comments = FindCssCommentRanges(text);
        AnalyzeSelectors(context, file.Path, sourceText, text, comments);
    }

    // ── Selector analysis ────────────────────────────────────────────────────

    /// <summary>
    /// Walks every selector list (the text preceding each top-level <c>{</c>), splits on commas into
    /// individual selectors, isolates the SUBJECT (rightmost compound selector), and reports when the
    /// subject restyles a <c>.mud-*</c> class.
    /// </summary>
    private static void AnalyzeSelectors(
        CompilationAnalysisContext ctx, string filePath, SourceText src, string text,
        List<(int Start, int End)> comments)
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
        // combinator. A .mud-* appearing earlier (ancestor/state-gate position) is "react-to" → ignored.
        var subjectStart = FindSubjectStart(selector);

        var subject = selector.Substring(subjectStart);
        foreach (Match m in MudClassToken.Matches(subject))
        {
            var token = m.Value;               // e.g. ".mud-tab"
            // Allow-list is EMPTY for .mud-* (no public consumer styling contract) — every non-exempt
            // .mud-* subject is a reach-in.

            var absoluteIndex = selectorOffset + subjectStart + m.Index;
            if (IsInComment(comments, absoluteIndex))
                continue;

            Report(ctx, filePath, src, absoluteIndex, m.Length, token.TrimStart('.'));
        }
    }

    /// <summary>
    /// Returns the start index (within <paramref name="selector"/>) of the SUBJECT — the rightmost
    /// compound selector. Everything before this index is ancestor / state-gate context.
    /// </summary>
    /// <remarks>
    /// Identical algorithm to <see cref="CssNnReachInAnalyzer"/>: the subject begins after the last
    /// top-level combinator (descendant whitespace, <c>&gt;</c>, <c>+</c>, <c>~</c>). Blazor's
    /// <c>::deep</c> is whitespace-separated from its following compound, so the trailing-whitespace
    /// scan already treats it as an ancestor boundary.
    /// </remarks>
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

    // ── Exemptions (ported from CssNnReachInAnalyzer) ──────────────

    /// <summary>
    /// Path-bucket exemption. The NnDesign producer / wrapper layer legitimately restyles <c>.mud-*</c>
    /// internals (that is the wrapper's job); samples + global theme are not consumers reaching in.
    /// </summary>
    private static bool IsExceptedPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var p = filePath.Replace('\\', '/');

        // The NnDesign producer / wrapper layer itself legitimately restyles .mud-* internals.
        if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Samples pages — both the canonical NnDesignSamples folder and the generic Pages/Samples bucket.
        if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        var fileName = Path.GetFileName(p);
        return AllowedFileNames.Contains(fileName);
    }

    /// <summary>
    /// File-name allow-list — samples CSS + global theme CSS, where wrapper-level <c>.mud-*</c> overrides
    /// (theme tuning) are authored legitimately rather than reached into from a feature component.
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

    // ── Reporting + comment infra (mirrors CssNnReachInAnalyzer) ──────────

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

        ctx.ReportDiagnostic(Diagnostic.Create(RuleNoMudReachIn, location, displayName));
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
