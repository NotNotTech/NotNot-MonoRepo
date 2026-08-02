using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// NNB_CSS013 — an unknown, BARE <c>var(--nns-*)</c> design-token reference. A bare <c>var()</c> (no
/// fallback) to a token declared in neither authority CSS file silently resolves to nothing: no compiler
/// error, no browser error, no fallback. That is the single, high-value bug this rule guards.
/// <para>
/// <b>Bare-only, by construction.</b> The Pass-2 regex captures the trailing delimiter and branches on it:
/// a BARE reference (trailing <c>)</c>) genuinely resolves to nothing and fires; a FALLBACK-GUARDED
/// reference (<c>var(--nns-x, &lt;value&gt;)</c>, trailing <c>,</c>) resolves to its fallback and is
/// SKIPPED — it is categorically outside the "resolves to nothing" target bug. Interpolated
/// (<c>var(--nns-color-{role})</c>) and malformed tokens are likewise skipped as documented false-negatives.
/// </para>
/// <para>
/// <b>The valid set is harvested from the AUTHORITY sources, two-pass.</b> Pass 1 builds the valid
/// <c>--nns-*</c> set from the authority CSS files present in the AdditionalFiles (<c>nn-design.css</c> /
/// <c>nn-colors.css</c>, both the <c>@property --nns-x { … }</c> registration and the <c>:root</c>
/// <c>--nns-x:</c> value forms) UNIONED with producer <c>.razor</c> / <c>.razor.cs</c> C#-string token
/// declarations (e.g. <c>--nns-switch-accent</c>, declared inline yet referenced bare in the authority
/// CSS). Component-scoped <c>.css</c> / <c>.razor.css</c> are deliberately NOT declaration sources — a
/// stray/experimental local must not launder a look-declared token into the valid set. Pass 2 then checks
/// every bare reference against that global set; two passes are mandatory because a reference may precede
/// its declaration file in AdditionalFiles order.
/// </para>
/// <para>
/// <b>Authority-presence gate (producer-scoped).</b> The analyzer is INERT (zero diagnostics) unless at
/// least one authority CSS file is registered as an AdditionalFile. The authority CSS lives only in the
/// producer (<c>NotNot.BlazorDesign/wwwroot/{nn-design,nn-colors}.css</c>); a ProjectReference /
/// PackageReference does not import an RCL's <c>wwwroot</c> CSS, so the gate holds true ONLY in the
/// producer build — exactly where every reference is producer-resident and the C#-string harvest is safe.
/// It gates on authority-file PRESENCE (not valid-set emptiness) so a consumer with a stray local
/// <c>--nns-*</c> can never yield a non-empty-but-incomplete set that partially storms. Mirrors the shipped
/// <see cref="CssFontSizeTokenAnalyzer"/> inert-when-absent scoping, strengthened to key on presence.
/// </para>
/// <para>
/// <b>Exemption posture is producer-INCLUSIVE (like NNB_CSS012, unlike the reach-in family).</b>
/// Token-validity is universal — a bare typo'd <c>var(--nns-*)</c> is a bug in the producer authority
/// files and samples too, and those files are exactly where bare references live. So the reference-check
/// deliberately does NOT call <see cref="CssConsumerExemptions.IsExceptedPath"/>. Only the vendor-file
/// skip, the shared <c>CssAnalyzerEnabled=false</c> kill-switch, and the per-file
/// <c>nnb_css013:allow-unknown-token</c> opt-out marker apply.
/// </para>
/// <para>
/// <b>Severity is Error.</b> An unknown bare reference is a hard correctness defect (peer of NNB_CSS012).
/// The authority-presence gate makes the producer build the only place the rule is live, and that build is
/// proven clean, so the rule fails the build on any new unknown bare <c>var(--nns-*)</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CssNnTokenValidityAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for the unknown bare <c>var(--nns-*)</c> reference.</summary>
    public const string DiagnosticId = "NNB_CSS013";

    private const string Category = "CssModernization";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker — <c>nnb_css013:allow-unknown-token: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb_css013:allow-unknown-token";

    /// <summary>Authority CSS file names — the ONLY declaration sources that open the gate.</summary>
    private static readonly HashSet<string> AuthorityFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nn-design.css",
        "nn-colors.css",
    };

    /// <summary>
    /// NNB_CSS013 descriptor. Severity = Error — a bare <c>var(--nns-typo)</c> reference to an undeclared
    /// token silently resolves to nothing. Kill-switch for a runtime/third-party injected token the analyzer
    /// cannot see: the per-file <c>nnb_css013:allow-unknown-token</c> comment, or the
    /// <c>CssAnalyzerEnabled=false</c> build property.
    /// </summary>
    public static readonly DiagnosticDescriptor RuleUnknownToken = new(
        DiagnosticId,
        "bare var(--nns-*) reference to an unknown design token",
        "bare var(--{0}) references design token --{0}, declared in no authority CSS "
            + "(nn-design.css / nn-colors.css) — fix the token name, or add a fallback: var(--{0}, <value>). "
            + "Runtime-injected token the analyzer cannot see? add a same-line "
            + "/* nnb_css013:allow-unknown-token: <reason> */ marker. (NNB_CSS013).",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "A BARE var(--nns-*) reference (no fallback) to a token declared in neither authority "
            + "CSS file silently resolves to nothing — no compiler error, no browser error, no fallback. The "
            + "valid --nns-* set is harvested from nn-design.css / nn-colors.css (@property + :root "
            + "declarations) unioned with producer .razor / .razor.cs C#-string token declarations. Only BARE "
            + "references fire; a fallback-guarded var(--nns-x, <value>) resolves to its fallback and is "
            + "skipped, as are interpolated var(--nns-color-{role}) and malformed tokens. The analyzer is "
            + "inert unless an authority CSS file is registered as an AdditionalFile (producer-scoped). "
            + "Per-file opt-out: nnb_css013:allow-unknown-token: <reason>. Kill-switch: "
            + "<CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS013",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    // ── Regex patterns ────────────────────────────────────────────────────

    /// <summary>
    /// Harvests a <c>@property --nns-&lt;name&gt; { … }</c> token registration. Its <c>:</c> lives inside the
    /// block, so the value-form regex below does not catch it — this form is required to admit
    /// <c>@property</c>-only tokens (no <c>:root</c> value) into the valid set.
    /// </summary>
    private static readonly Regex PropertyDeclaration = new(
        @"@property\s+--(?<name>nns-[a-z0-9-]+)\s*\{",
        RegexOptions.Compiled);

    /// <summary>
    /// Harvests a <c>--nns-&lt;name&gt;:</c> value declaration (<c>:root</c> assignment or a producer
    /// C#-string token). The <c>(?&lt;![\w-])</c> lookbehind anchors the start of the custom-property name;
    /// the trailing <c>\s*:</c> distinguishes a DECLARATION from a <c>var(--nns-x)</c> / <c>var(--nns-x, …)</c>
    /// REFERENCE (which is followed by <c>)</c> / <c>,</c>, never <c>:</c>).
    /// </summary>
    private static readonly Regex ValueDeclaration = new(
        @"(?<![\w-])--(?<name>nns-[a-z0-9]+(?:-[a-z0-9]+)*)\s*:",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a <c>var(--nns-&lt;tok&gt;)</c> reference and CAPTURES its trailing delimiter. The
    /// <c>tok</c> class is broadened to <c>[A-Za-z0-9{}@-]</c> so interpolated / dynamic tokens
    /// (<c>var(--nns-color-{role})</c>) are captured and then explicitly skipped. <c>delim</c> is the F1
    /// bare-vs-fallback discriminator: <c>)</c> = bare (fires), <c>,</c> = fallback-guarded (skipped).
    /// </summary>
    private static readonly Regex VarReference = new(
        @"var\(\s*--(?<tok>nns-[A-Za-z0-9{}@-]+)\s*(?<delim>[),])",
        RegexOptions.Compiled);

    /// <summary>Well-formed lowercase-kebab token grammar — a token failing this is the naming rule's job.</summary>
    private static readonly Regex WellFormedToken = new(
        @"^nns-[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled);

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(RuleUnknownToken);

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

        // Authority-presence gate (G2) — INERT unless an authority CSS file is registered as an
        // AdditionalFile. Gate on PRESENCE, not valid-set emptiness (a stray consumer-local --nns-* must not
        // yield a non-empty-but-incomplete set that partially storms).
        if (!HasAuthorityFile(context))
            return;

        // Pass 1 — harvest the valid --nns-* set from the AUTHORITY sources only (authority CSS + producer
        // .razor/.razor.cs C#-string tokens). Both passes run inside this one compilation action because a
        // reference may precede its declaration file in AdditionalFiles order.
        var valid = HarvestValidTokens(context);

        // Pass 2 — check BARE references on CSS surfaces only.
        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeFileReferences(context, file, valid);
        }
    }

    private static bool HasAuthorityFile(CompilationAnalysisContext context)
    {
        foreach (var file in context.Options.AdditionalFiles)
        {
            if (IsAuthorityFile(file.Path))
                return true;
        }
        return false;
    }

    // ── Pass 1: declaration harvest ───────────────────────────────────────────

    /// <summary>
    /// Builds the valid <c>--nns-*</c> set from the authority CSS files (<c>@property</c> + <c>:root</c>
    /// forms) unioned with producer <c>.razor</c> / <c>.razor.cs</c> C#-string token declarations. Comment
    /// ranges (<c>/* … */</c>) are masked. Non-authority <c>.css</c> / <c>.razor.css</c> are NOT declaration
    /// sources (a stray local must not enter the valid set).
    /// </summary>
    private static HashSet<string> HarvestValidTokens(CompilationAnalysisContext context)
    {
        var valid = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var path = file.Path;
            if (string.IsNullOrEmpty(path) || CssConsumerExemptions.IsVendorFile(path))
                continue;

            var isAuthority = IsAuthorityFile(path);
            var isCSharpMarkup =
                path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);

            // Non-authority .css / .razor.css / anything else → not a declaration source.
            if (!isAuthority && !isCSharpMarkup)
                continue;

            var sourceText = file.GetText(context.CancellationToken);
            if (sourceText == null || sourceText.Length == 0)
                continue;

            var text = sourceText.ToString();
            var comments = CssSelectorScanner.FindCssCommentRanges(text);

            // The @property registration form only enters the set for authority CSS (its `:` lives inside
            // the block, so the value form below cannot catch it).
            if (isAuthority)
            {
                foreach (Match m in PropertyDeclaration.Matches(text))
                {
                    var g = m.Groups["name"];
                    if (!CssSelectorScanner.IsInComment(comments, g.Index))
                        valid.Add(g.Value);
                }
            }

            // The value form (`--nns-x:`) harvests :root assignments (authority CSS) and C#-string token
            // declarations (producer .razor/.razor.cs, e.g. --nns-switch-accent).
            foreach (Match m in ValueDeclaration.Matches(text))
            {
                var g = m.Groups["name"];
                if (!CssSelectorScanner.IsInComment(comments, g.Index))
                    valid.Add(g.Value);
            }
        }

        return valid;
    }

    // ── Pass 2: reference check (CSS surfaces only, bare only) ─────────────────

    private static void AnalyzeFileReferences(
        CompilationAnalysisContext context, AdditionalText file, HashSet<string> valid)
    {
        var path = file.Path;
        if (string.IsNullOrEmpty(path) || CssConsumerExemptions.IsVendorFile(path))
            return;

        // CSS surfaces ONLY — covers .css and .razor.css; .razor / .razor.cs are Pass-1 declaration sources
        // only (G4: restricting the CHECK to CSS means the only comment form met is /* */, which
        // FindCssCommentRanges handles by construction — the non-CSS-comment false-positive class is gone).
        if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            return;

        // NOTE: NNB_CSS013 does NOT call CssConsumerExemptions.IsExceptedPath — token-validity is universal
        // and the producer authority files are exactly where bare references live (posture INVERTED vs the
        // reach-in family; identical to NNB_CSS012). Only vendor-skip + kill-switch + per-file opt-out apply.

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        // Per-file opt-out marker (whole-file scan, same coarse semantics as the sibling analyzers).
        if (CssConsumerExemptions.HasOptOutComment(text, OptOutMarker))
            return;

        var comments = CssSelectorScanner.FindCssCommentRanges(text);

        foreach (Match m in VarReference.Matches(text))
        {
            var tokGroup = m.Groups["tok"];
            if (CssSelectorScanner.IsInComment(comments, tokGroup.Index))
                continue;

            var tok = tokGroup.Value;

            // Interpolated / dynamic (C#-string-interpolated var(--nns-color-{role})) — not literal-checkable.
            if (ContainsDynamicMarker(tok))
                continue;

            // Malformed token → the deferred naming rule's job, not this one.
            if (!WellFormedToken.IsMatch(tok))
                continue;

            // Fallback-guarded (F1) — resolves to the fallback, not the target bug.
            if (m.Groups["delim"].Value == ",")
                continue;

            // BARE reference to a token in no authority/C#-string declaration → the target bug.
            if (!valid.Contains(tok))
                Report(context, path, sourceText, tokGroup.Index, tokGroup.Length, tok);
        }
    }

    // ── Predicates ────────────────────────────────────────────────────────────

    private static bool IsAuthorityFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return AuthorityFileNames.Contains(GetFileName(path));
    }

    private static bool ContainsDynamicMarker(string tok)
    {
        foreach (var ch in tok)
        {
            if (ch == '{' || ch == '}' || ch == '@' || (ch >= 'A' && ch <= 'Z'))
                return true;
        }
        return false;
    }

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
    }

    // ── Reporting ────────────────────────────────────────────────────────────

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string token)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(RuleUnknownToken, location, token));
    }
}
