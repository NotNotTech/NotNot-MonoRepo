using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// NNB_CSS014 — a <c>@keyframes</c> block may declare only compositor-eligible properties
/// (<c>opacity</c>, <c>transform</c> and the individual <c>translate</c>/<c>rotate</c>/<c>scale</c>)
/// unless the file carries the <c>nnb_css014:allow-noncomposited-keyframe</c> opt-out marker.
/// <para>
/// <b>Property closure, not subject policing.</b> The reach-in family (NNB_CSS008/009/011) polices the
/// styled SUBJECT of a selector and NNB_CSS012 polices a declaration VALUE. NNB_CSS014 polices the
/// animated PROPERTY SET of a keyframe block. A keyframe that animates <c>filter</c>, <c>color</c>,
/// geometry (<c>top</c>/<c>left</c>/<c>width</c>/<c>height</c>) or SVG dash attributes forces the
/// browser to rebuild the display list and repaint on every refresh tick for as long as the animation
/// runs; two shipped incidents (notification-bell <c>filter</c> pulse, reduced-motion <c>color</c> +
/// <c>filter</c> pulse) measured this at 76-94% of one core. The cheapest correct fix is to move the
/// static presentation onto the rule (or a pseudo-layer) and animate that layer's <c>opacity</c>, or
/// to adopt an <c>.nns-motion-*</c> vocabulary utility from <c>nn-design.css</c>.
/// </para>
/// <para>
/// <b>Iteration-agnostic by design.</b> The rule does not resolve <c>animation:</c> declarations to
/// keyframe names (references routinely cross file boundaries), so bounded one-shots that animate a
/// disqualified property (focus-flash <c>border-color</c>, bell <c>background-color</c>) also fire and
/// carry the marker with a "bounded one-shot" justification. The marker documents the bound at the
/// keyframe, which is where a future <c>infinite</c> edit would be reviewed.
/// </para>
/// <para>
/// <b>Exemption posture is producer-INCLUSIVE (like NNB_CSS012/013, unlike the reach-in family).</b>
/// The producer owns <c>nn-design.css</c>, and an infinite non-composited keyframe there costs the same as anywhere else, so the
/// reference-check deliberately does NOT call <c>IsExceptedPath</c>. Only the vendor-file skip, the
/// shared <c>CssAnalyzerEnabled=false</c> kill-switch, and the per-file opt-out marker apply.
/// </para>
/// <para>
/// <b>Excluded by construction</b>: keyframe-shaped text inside CSS comments; vendor files
/// (<c>*.min.css</c>, <c>/lib/</c>, <c>/node_modules/</c> — the MudBlazor indeterminate keyframes are
/// an acknowledged residual covered only by the perf-monitor runtime detector). Inline
/// <c>&lt;style&gt;</c> blocks in <c>.razor</c> markup are an explicit non-goal (no keyframes live
/// there today). Custom properties (<c>--*</c>) inside keyframes fire: animating them is main-thread
/// work, not composited.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CssKeyframePropertyAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for the keyframe-property closure rule.</summary>
    public const string DiagnosticId = "NNB_CSS014";

    private const string Category = "CssModernization";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker — <c>nnb_css014:allow-noncomposited-keyframe: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb_css014:allow-noncomposited-keyframe";

    /// <summary>
    /// Properties the compositor can animate without main-thread paint. <c>translate</c>/<c>rotate</c>/
    /// <c>scale</c> composite identically to <c>transform</c>; everything else (filter, color, geometry,
    /// border-color, background-color, stroke-dash*, custom properties) forces per-tick paint.
    /// </summary>
    private static readonly HashSet<string> CompositedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "opacity",
        "transform",
        "translate",
        "rotate",
        "scale",
    };

    /// <summary>
    /// Finds <c>@keyframes &lt;name&gt; {</c> block starts, including vendor-prefixed
    /// (<c>@-webkit-keyframes</c>) and <c>@media</c>-nested forms. The body is brace-matched from the
    /// opening brace; the name group is used only for the diagnostic message.
    /// </summary>
    private static readonly Regex KeyframesOpen = new(
        @"@(?:-[a-z]+-)?keyframes\s+(?<name>[^\s{]+)\s*\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Matches one inner keyframe rule (<c>0%, 100% { decls }</c> / <c>from { decls }</c>) inside a
    /// brace-matched <c>@keyframes</c> body. Declaration chunks contain no braces by construction.
    /// </summary>
    private static readonly Regex KeyframeRule = new(
        @"(?<sel>[^{}]+)\{(?<decls>[^{}]*)\}",
        RegexOptions.Compiled);

    /// <summary>Validates a declaration property name (rejects value fragments split on ';' — see AnalyzeDeclarations).</summary>
    private static readonly Regex PropertyName = new(
        @"^[A-Za-z-][\w-]*$",
        RegexOptions.Compiled);

    /// <summary>
    /// NNB_CSS014 descriptor. Severity = Error — a non-composited keyframe is the exact regression class
    /// behind two measured 76%+ paint-loop incidents, and the tree is clean-or-annotated at HEAD, so any
    /// NEW violation breaks the build (mirrors the NNB_CSS012 Error precedent). Kill-switch for legitimate
    /// residuals (vendor-pattern dash marches, framework-owned overlays, bounded one-shots): the per-file
    /// nnb_css014:allow-noncomposited-keyframe comment, or the CssAnalyzerEnabled=false build property.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleKeyframePropertyClosure = new(
        DiagnosticId,
        "Keyframe animates a non-composited property — only opacity/transform stay off the paint loop",
        "Keyframe '{1}' animates '{0}', which forces main-thread paint every tick while the animation "
            + "runs — use opacity/transform (or an .nns-motion-* vocabulary utility). Bounded one-shot, "
            + "framework-owned, or otherwise justified? keep the keyframe and add a "
            + "/* nnb_css014:allow-noncomposited-keyframe: <reason> */ marker. (NNB_CSS014).",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "A @keyframes block that animates filter, color, geometry, border/background-color, "
            + "stroke-dash* or a custom property forces display-list rebuild + full-page repaint on every "
            + "refresh tick while the animation runs. Only opacity, transform, translate, rotate and scale "
            + "are compositor-eligible. The rule is iteration-agnostic (it does not resolve animation: "
            + "references across files): bounded one-shots on disqualified properties also fire and carry "
            + "the marker with a 'bounded one-shot' justification. Vendor files (*.min.css, /lib/, "
            + "/node_modules/) are skipped. Per-file opt-out: "
            + "nnb_css014:allow-noncomposited-keyframe: <reason>. Kill-switch: "
            + "<CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS014",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleKeyframePropertyClosure);

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

        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeFile(context, file);
        }
    }

    private static void AnalyzeFile(CompilationAnalysisContext context, AdditionalText file)
    {
        var path = file.Path;
        if (string.IsNullOrEmpty(path) || CssConsumerExemptions.IsVendorFile(path))
            return;

        // Covers both .css and .razor.css. (Inline <style> blocks in .razor markup are an explicit
        // non-goal: no keyframes live there today.)
        var isCss = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        if (!isCss)
            return;

        // NOTE: NNB_CSS014 does NOT call CssConsumerExemptions.IsExceptedPath — it MUST scan the NnDesign
        // producer CSS (nn-design.css), the very path
        // IsExceptedPath exempts. Only vendor-skip + kill-switch + per-file opt-out apply (exemption
        // posture producer-INCLUSIVE, like NNB_CSS012/013).

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the sibling analyzers).
        if (CssConsumerExemptions.HasOptOutComment(text, OptOutMarker))
            return;

        var comments = CssSelectorScanner.FindCssCommentRanges(text);

        foreach (Match open in KeyframesOpen.Matches(text))
        {
            // A @keyframes-shaped match inside a CSS comment is documentation, not a block.
            if (CssSelectorScanner.IsInComment(comments, open.Index))
                continue;

            var bodyOpen = open.Index + open.Length - 1; // the '{' the regex consumed
            if (!TryMatchBody(text, bodyOpen, out var bodyStart, out var bodyEnd))
                continue;

            AnalyzeBody(context, path, sourceText, text, comments, bodyStart, bodyEnd, open.Groups["name"].Value);
        }
    }

    // ── @keyframes body analysis ─────────────────────────────────────────────

    /// <summary>
    /// Brace-matches the <c>@keyframes</c> body starting at the opening brace at
    /// <paramref name="openBraceIndex"/>. Comment- and string-aware so braces inside comments or quoted
    /// values cannot corrupt the depth count. Returns false on unbalanced input (no crash on malformed
    /// CSS — the block is skipped).
    /// </summary>
    private static bool TryMatchBody(string text, int openBraceIndex, out int bodyStart, out int bodyEnd)
    {
        bodyStart = openBraceIndex + 1;
        bodyEnd = -1;

        var depth = 0;
        var i = openBraceIndex;
        while (i < text.Length)
        {
            var c = text[i];

            // Skip CSS comments.
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? text.Length : close + 2;
                continue;
            }

            // Skip quoted strings (single or double).
            if (c == '"' || c == '\'')
            {
                i = SkipString(text, i);
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    bodyEnd = i;
                    return true;
                }
            }

            i++;
        }

        return false;
    }

    private static int SkipString(string text, int quoteIndex)
    {
        var quote = text[quoteIndex];
        var i = quoteIndex + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i += 2;
                continue;
            }
            if (text[i] == quote)
                return i + 1;
            i++;
        }
        return i;
    }

    private static void AnalyzeBody(
        CompilationAnalysisContext context, string path, SourceText src, string text,
        List<(int Start, int End)> comments, int bodyStart, int bodyEnd, string keyframeName)
    {
        var body = text.Substring(bodyStart, bodyEnd - bodyStart);

        foreach (Match rule in KeyframeRule.Matches(body))
        {
            var decls = rule.Groups["decls"];
            AnalyzeDeclarations(context, path, src, body, bodyStart, decls.Value, decls.Index, comments, keyframeName);
        }
    }

    private static void AnalyzeDeclarations(
        CompilationAnalysisContext context, string path, SourceText src, string body, int bodyStart,
        string decls, int declsOffset, List<(int Start, int End)> comments, string keyframeName)
    {
        // Split on ';' with manual offset tracking. Values containing ';' (data URIs) fragment, but
        // the fragments fail PropertyName validation (parens/commas), so they are skipped, never flagged.
        var segmentStart = 0;
        for (var i = 0; i <= decls.Length; i++)
        {
            if (i < decls.Length && decls[i] != ';')
                continue;

            var segment = decls.Substring(segmentStart, i - segmentStart);
            var colon = segment.IndexOf(':');
            if (colon > 0)
            {
                var prop = segment.Substring(0, colon).Trim();
                if (PropertyName.IsMatch(prop) && !CompositedProperties.Contains(prop))
                {
                    var propOffsetInSegment = segment.IndexOf(prop, StringComparison.Ordinal);
                    var absoluteIndex = bodyStart + declsOffset + segmentStart + propOffsetInSegment;
                    if (!CssSelectorScanner.IsInComment(comments, absoluteIndex))
                        Report(context, path, src, absoluteIndex, prop.Length, prop, keyframeName);
                }
            }

            segmentStart = i + 1;
        }
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string property, string keyframeName)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(RuleKeyframePropertyClosure, location, property, keyframeName));
    }
}
