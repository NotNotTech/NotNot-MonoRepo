using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// NNB_CSS008 — consumer scoped CSS must not REACH INTO NnDesign internal <c>.nns-*</c> classes.
/// <para>
/// The "mortal sin" detector. A consumer that restyles an NnDesign wrapper's internal class
/// (e.g. <c>::deep .nns-content-section-body { }</c> or a bare <c>.nns-content-section-header { }</c>)
/// couples itself to NnDesign's private DOM and breaks the moment that DOM changes. The sanctioned
/// "venial" act — a consumer styling its OWN element gated on an NnDesign-published root STATE
/// (e.g. <c>html.nns-chord-revealed .my-consumer-class { }</c>) — is NOT flagged.
/// </para>
/// <para>
/// <b>Core discriminator (react-to vs reach-into)</b>: the styled SUBJECT is the rightmost compound
/// selector — the thing actually being restyled. An <c>.nns-*</c> class appearing as the SUBJECT is a
/// reach-in (violation). The SAME <c>.nns-*</c> class appearing only as an ANCESTOR / state gate on a
/// non-<c>nns</c> subject is "react-to" (sanctioned). The analyzer keys on subject position, not on the
/// presence of <c>::deep</c> (which is neither necessary nor sufficient — bare reach-ins exist, and
/// <c>::deep</c> into a different library's internals is out of scope).
/// </para>
/// <para>
/// <b>Allow-list (public contract — never warn)</b>: <c>.nns-chord-revealed</c> (NnDesign-published
/// public state token) only. Keyed on a TINY public-contract allow-list rather than a growing internal
/// denylist: per the namespace-reclamation invariant the <c>nns-</c> prefix means "NnDesign owns this",
/// so any non-allow-listed <c>.nns-*</c> subject in consumer CSS is by definition a reach-in. The
/// samples surface is exempted by PATH bucket (below), never by a blanket prefix allow.
/// </para>
/// <para>
/// <b>Exemptions</b> (ported from the NnDesign policy analyzer): path buckets (the NnDesign producer's
/// own <c>nn-design.css</c> / <c>NotNot.BlazorDesign</c> internals; <c>Pages/Samples/**</c> and
/// <c>NnDesignSamples/**</c>; samples CSS; global theme CSS — these are the SOLE samples exemption now
/// that the blanket <c>.nns-*</c> prefix allow is gone), a per-file opt-out comment
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
        "Do not reach into NnDesign internal .nns-* classes",
        "Consumer CSS restyles NnDesign internal class '{0}'. Style your OWN element (optionally "
            + "gated on a published NnDesign state like html.nns-chord-revealed) or use the wrapper's "
            + "public parameter/component (e.g. NnContentSection.MaxHeight / NoHeaderBorder, "
            + "NnContentSectionGroup) instead of reaching into '.nns-*' internals. (NNB_CSS008).",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "An NnDesign wrapper's internal '.nns-*' classes are private implementation detail. "
            + "A consumer that restyles one (the rightmost/subject selector being an '.nns-*' class — "
            + "with or without ::deep) couples to NnDesign's private DOM and breaks when that DOM "
            + "changes. Sanctioned alternative: style your OWN (non-nns) element, optionally gated on a "
            + "published NnDesign ancestor state (html.nns-chord-revealed .your-class) — the '.nns-*' as a "
            + "STATE GATE on a non-nns subject is allowed; the '.nns-*' as the SUBJECT is the violation. "
            + "Public-contract allow-list (never flagged): .nns-chord-revealed. "
            + "Exempt: NnDesign's own producer CSS / NotNot.BlazorDesign internals, Pages/Samples/** + "
            + "NnDesignSamples/**, samples CSS, global theme CSS. Per-file opt-out: "
            + "nnb_css008:allow-reachin: <reason>. Kill-switch: <CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS008",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    // ── Regex patterns ────────────────────────────────────────────────────

    /// <summary>
    /// Matches a single <c>.nns-*</c> class token (the leading dot is required so attribute values and
    /// identifiers like <c>data-nns-fill</c> or element names don't match). Case-sensitive: CSS class
    /// names are case-sensitive and the NnDesign convention is lowercase.
    /// </summary>
    private static readonly Regex NnClassToken = new(
        @"\.nns-[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

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

        // Only scoped/global CSS carries reach-in selectors. (.razor markup uses Class="..."
        // attributes, not selectors, so it cannot reach into an NnDesign subject by construction.)
        var isCss = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        if (!isCss)
            return;

        // Path-bucket exemption — producer CSS, samples, global theme.
        if (CssConsumerExemptions.IsExceptedPath(path))
            return;

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the policy analyzer).
        if (CssConsumerExemptions.HasOptOutComment(text, OptOutMarker))
            return;

        var comments = CssSelectorScanner.FindCssCommentRanges(text);
        // Walk every individual selector (shared engine), applying the .nns-* subject classification.
        CssSelectorScanner.ForEachSelector(text, comments,
            (selector, offset) => AnalyzeSingleSelector(context, file.Path, sourceText, selector, offset, comments));
    }

    // ── Selector analysis ────────────────────────────────────────────────────

    private static void AnalyzeSingleSelector(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        string selector, int selectorOffset, List<(int Start, int End)> comments)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return;

        // The SUBJECT is the rightmost compound selector — the run of simple selectors after the last
        // combinator (descendant whitespace, `>`, `+`, `~`) OR after the last `::deep` / pseudo that
        // separates ancestor context from the styled element. We compute the subject's char range
        // within `selector`, then look for a violating .nns-* token ONLY inside that range. An .nns-*
        // appearing earlier (ancestor/state-gate position) is "react-to" → ignored.
        var subjectStart = CssSelectorScanner.FindSubjectStart(selector);

        // Examine the subject substring for .nns-* class tokens.
        var subject = selector.Substring(subjectStart);
        foreach (Match m in NnClassToken.Matches(subject))
        {
            var token = m.Value;               // e.g. ".nns-content-section-body"
            if (IsAllowListed(token))
                continue;

            var absoluteIndex = selectorOffset + subjectStart + m.Index;
            if (CssSelectorScanner.IsInComment(comments, absoluteIndex))
                continue;

            // Report on the offending token (sans leading dot for the message argument readability).
            Report(ctx, filePath, src, absoluteIndex, m.Length, token.TrimStart('.'));
        }
    }

    /// <summary>
    /// Public-contract allow-list: <c>.nns-chord-revealed</c> (exact) only. Never counts as a reach-in
    /// even when it appears in subject position. There is deliberately NO blanket <c>.nns-*</c> prefix
    /// allow — the <c>.nns-*</c> prefix now denotes NnDesign INTERNALS (the very thing this analyzer
    /// guards), so a blanket allow would silently dormant the rule. Samples are exempted by PATH bucket
    /// (<see cref="CssConsumerExemptions.IsExceptedPath"/>), not by prefix.
    /// </summary>
    private static bool IsAllowListed(string nnToken)
    {
        // `.nns-chord-revealed` published public state token (exact match).
        if (string.Equals(nnToken, ".nns-chord-revealed", StringComparison.Ordinal))
            return true;

        return false;
    }

    // ── Reporting (shared exemption / scanner infra lives in CssConsumerExemptions / CssSelectorScanner) ──

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
}
