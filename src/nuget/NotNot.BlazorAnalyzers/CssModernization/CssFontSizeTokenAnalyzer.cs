using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// NNB_CSS012 — a <c>font-size:</c> declaration VALUE that duplicates an <c>--nns-font-size-*</c>
/// design-token value (rem OR its px equivalent) must use the token instead of the literal.
/// <para>
/// <b>Value discipline, not subject policing.</b> The reach-in family (NNB_CSS008/009/011) polices the
/// styled SUBJECT of a selector. NNB_CSS012 is a different axis — it policies the VALUE of a
/// <c>font-size:</c> declaration. A literal <c>font-size: 0.875rem</c> (or <c>14px</c>) silently restates
/// the <c>--nns-font-size-md</c> token; when the scale is retuned the literal detaches from the ladder.
/// The cheapest correct fix is token substitution: <c>font-size: var(--nns-font-size-md)</c>.
/// </para>
/// <para>
/// <b>Token values are the SSOT parsed from <c>nn-design.css</c>.</b> Pass A finds the canonical
/// <c>nn-design.css</c> among the AdditionalFiles and parses its <c>:root</c> <c>--nns-font-size-*</c>
/// declarations into a flag-set = each rem value + its px equivalent (rem × 16, root = 16px). If
/// <c>nn-design.css</c> is not registered as an AdditionalFile (a consumer build) the token-map is empty
/// and the analyzer is inert — this correctly scopes NNB_CSS012 to the producer build that owns the
/// token scale (the same scoping <c>NnTokensGenerator</c> uses).
/// </para>
/// <para>
/// <b>Exemption posture is INVERTED vs the reach-in family (precedent NNB043).</b> The reach-in
/// analyzers exempt the NnDesign producer path (<see cref="CssConsumerExemptions.IsExceptedPath"/>) because
/// they police CONSUMER usage; NNB_CSS012 MUST scan the producer's own CSS (that is where the tokens and
/// their literal-duplicating <c>font-size</c> declarations live), so it deliberately does NOT call
/// <c>IsExceptedPath</c>. Only the vendor-file skip, the shared <c>CssAnalyzerEnabled=false</c>
/// kill-switch, and the per-file <c>nnb_css012:allow-fontsize-literal</c> opt-out marker apply.
/// </para>
/// <para>
/// <b>Excluded by construction</b>: the <c>--nns-font-size-*:</c> token DEFINITIONS and
/// <c>var(--nns-font-size-*)</c> references — the <c>(?&lt;![\w-])</c> lookbehind on the
/// <c>font-size</c> match fails on the preceding <c>-</c>, so neither the token definition line nor a
/// <c>var()</c> ref matches. Inline <c>style="font-size:…"</c> in <c>.razor</c> markup is owned by NNB044
/// (an explicit non-goal here). Off-ladder values with no matching token never fire.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CssFontSizeTokenAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for the font-size literal / design-token duplication.</summary>
    public const string DiagnosticId = "NNB_CSS012";

    private const string Category = "CssModernization";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker — <c>nnb_css012:allow-fontsize-literal: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb_css012:allow-fontsize-literal";

    /// <summary>Canonical token-source file name (matches <c>NnTokensGenerator.CanonicalCssFileName</c>).</summary>
    private const string CanonicalCssFileName = "nn-design.css";

    /// <summary>Root font-size assumption for the rem→px equivalence (browser default).</summary>
    private const double RootPx = 16.0;

    /// <summary>
    /// NNB_CSS012 descriptor. Severity = Error — a font-size literal that duplicates a design-token value
    /// silently detaches from scale retuning; the token substitution is the cheapest correct fix. Kill-switch
    /// for legitimate off-ladder / icon-glyph literals: the per-file nnb_css012:allow-fontsize-literal
    /// comment, or the CssAnalyzerEnabled=false build property.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleFontSizeToken = new(
        DiagnosticId,
        "font-size literal duplicates an --nns-font-size-* design token — use the token",
        "font-size literal '{0}' equals design token --nns-font-size-{1}; replace with "
            + "var(--nns-font-size-{1}). Intentional off-ladder or icon-glyph size? keep the literal and "
            + "add a same-line /* nnb_css012:allow-fontsize-literal: <reason> */ marker. (NNB_CSS012)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "A font-size value that restates an --nns-font-size-* token value silently detaches "
            + "from scale retuning: when the token ladder is re-tuned the literal stays frozen. The scale "
            + "SSOT is nn-design.css :root --nns-font-size-sm/md/lg = 0.75/0.875/1rem = 12/14/16px. This "
            + "fires when a rem OR px font-size literal in a .css / .razor.css declaration equals a token "
            + "value. It does NOT fire on off-ladder values with no matching token (e.g. 0.8125rem / 13px), "
            + "on the --nns-font-size-* token definitions or var(--nns-font-size-*) references (excluded by "
            + "construction), or on inline style= attributes (owned by NNB044). Fix: use "
            + "var(--nns-font-size-<step>). Per-file opt-out: nnb_css012:allow-fontsize-literal: <reason>. "
            + "Kill-switch: <CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS012");

    // ── Regex patterns ────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>--nns-font-size-&lt;step&gt;: &lt;value&gt;;</c> token definition. <c>step</c> is the
    /// ladder step (sm/md/lg); <c>val</c> is the raw value text up to the terminating <c>;</c>.
    /// </summary>
    private static readonly Regex TokenDefinition = new(
        @"--nns-font-size-(?<step>[a-z]+)\s*:\s*(?<val>[^;]+);",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a real <c>font-size:</c> property declaration with a rem/px literal value. The
    /// <c>(?&lt;![\w-])</c> lookbehind excludes the <c>--nns-font-size-*:</c> token DEFINITIONS and
    /// <c>var(--nns-font-size-*)</c> refs for free (the preceding <c>-</c> fails the lookbehind). Only the
    /// <c>lit</c> literal is reported.
    /// </summary>
    private static readonly Regex FontSizeLiteral = new(
        @"(?<![\w-])font-size\s*:\s*(?<lit>[0-9.]+(?:rem|px))\b",
        RegexOptions.Compiled);

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleFontSizeToken);

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

        // Pass A — parse the token scale from the canonical nn-design.css (the value SSOT).
        var flagSet = BuildTokenFlagSet(context);
        // No canonical token source registered (a consumer build) → analyzer is inert.
        if (flagSet.Count == 0)
            return;

        // Pass B — scan every .css / .razor.css AdditionalFile for token-duplicating font-size literals.
        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeFile(context, file, flagSet);
        }
    }

    // ── Pass A: token flag-set ────────────────────────────────────────────────

    /// <summary>
    /// Builds the flag-set {literal → step} from the canonical <c>nn-design.css</c>: for each
    /// <c>--nns-font-size-&lt;step&gt;</c> token, both the rem literal (normalized) and its px equivalent
    /// (rem × 16). Empty if <c>nn-design.css</c> is not registered as an AdditionalFile.
    /// </summary>
    private static Dictionary<string, string> BuildTokenFlagSet(CompilationAnalysisContext context)
    {
        var flagSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in context.Options.AdditionalFiles)
        {
            var path = file.Path;
            if (string.IsNullOrEmpty(path))
                continue;

            var fileName = GetFileName(path);
            if (!string.Equals(fileName, CanonicalCssFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var sourceText = file.GetText(context.CancellationToken);
            if (sourceText == null || sourceText.Length == 0)
                continue;

            var text = sourceText.ToString();
            foreach (Match m in TokenDefinition.Matches(text))
            {
                var step = m.Groups["step"].Value;
                var rawVal = m.Groups["val"].Value.Trim();
                RegisterTokenValue(flagSet, rawVal, step);
            }
        }

        return flagSet;
    }

    /// <summary>
    /// Registers a token's rem literal + its px equivalent under the ladder <paramref name="step"/>. Only
    /// rem-denominated token values participate in the px-equivalence (the tokens are authored in rem).
    /// </summary>
    private static void RegisterTokenValue(Dictionary<string, string> flagSet, string rawVal, string step)
    {
        // The canonical token literal itself (e.g. "0.875rem") — normalized.
        var normalized = NormalizeLiteral(rawVal);
        if (normalized == null)
            return;

        flagSet[normalized] = step;

        // px equivalent (rem × 16) for the same step — only when the token is rem-denominated.
        if (normalized.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
            TryParseRem(normalized, out var rem))
        {
            var px = rem * RootPx;
            var pxLiteral = FormatPx(px);
            flagSet[pxLiteral] = step;
        }
    }

    // ── Pass B: per-file scan ────────────────────────────────────────────────

    private static void AnalyzeFile(
        CompilationAnalysisContext context, AdditionalText file, Dictionary<string, string> flagSet)
    {
        var path = file.Path;
        if (string.IsNullOrEmpty(path) || CssConsumerExemptions.IsVendorFile(path))
            return;

        // Covers both .css and .razor.css. (Inline style= attributes in .razor markup are owned by NNB044.)
        var isCss = path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        if (!isCss)
            return;

        // NOTE: NNB_CSS012 does NOT call CssConsumerExemptions.IsExceptedPath — it MUST scan the NnDesign
        // producer CSS (the token owner), the very path IsExceptedPath exempts. Only vendor-skip +
        // kill-switch + per-file opt-out apply (exemption posture INVERTED vs the reach-in family, NNB043).

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the reach-in family).
        if (CssConsumerExemptions.HasOptOutComment(text, OptOutMarker))
            return;

        var comments = CssSelectorScanner.FindCssCommentRanges(text);

        foreach (Match m in FontSizeLiteral.Matches(text))
        {
            var litGroup = m.Groups["lit"];
            var literal = litGroup.Value;
            var normalized = NormalizeLiteral(literal);
            if (normalized == null || !flagSet.TryGetValue(normalized, out var step))
                continue;

            // The token DEFINITION line and var() refs are excluded by the lookbehind; comment-embedded
            // font-size text is excluded here.
            if (CssSelectorScanner.IsInComment(comments, litGroup.Index))
                continue;

            Report(context, path, sourceText, litGroup.Index, litGroup.Length, literal, step);
        }
    }

    // ── Value normalization / equivalence ────────────────────────────────────

    /// <summary>
    /// Canonicalizes a rem/px literal for flag-set comparison: lowercases the unit, drops a leading zero
    /// AND a trailing zero ambiguity by re-formatting the numeric part (so <c>0.875rem</c>, <c>.875rem</c>
    /// compare equal). Returns null if the literal is not a parseable rem/px number.
    /// </summary>
    private static string? NormalizeLiteral(string literal)
    {
        if (string.IsNullOrWhiteSpace(literal))
            return null;

        var trimmed = literal.Trim();
        string unit;
        if (trimmed.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
            unit = "rem";
        else if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            unit = "px";
        else
            return null;

        var numberPart = trimmed.Substring(0, trimmed.Length - unit.Length).Trim();
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        return unit == "rem" ? FormatRem(value) : FormatPx(value);
    }

    private static bool TryParseRem(string normalizedRem, out double rem)
    {
        rem = 0;
        if (!normalizedRem.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
            return false;
        var numberPart = normalizedRem.Substring(0, normalizedRem.Length - 3);
        return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out rem);
    }

    /// <summary>Formats a rem value with a canonical numeric representation (invariant, no trailing zeros).</summary>
    private static string FormatRem(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture) + "rem";

    /// <summary>Formats a px value with a canonical numeric representation (invariant, no trailing zeros).</summary>
    private static string FormatPx(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture) + "px";

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string literal, string step)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(RuleFontSizeToken, location, literal, step));
    }
}
