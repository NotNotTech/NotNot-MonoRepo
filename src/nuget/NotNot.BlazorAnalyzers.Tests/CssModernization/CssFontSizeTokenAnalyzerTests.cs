using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for <see cref="CssFontSizeTokenAnalyzer"/> (NNB_CSS012) — a <c>font-size:</c> declaration VALUE
/// duplicating an <c>--nns-font-size-*</c> design-token value (rem OR its px equivalent) must use the
/// token.
/// <para>
/// EVERY test supplies the canonical <c>nn-design.css</c> (with the <c>--nns-font-size-sm/md/lg</c>
/// token scale) AS an AdditionalFile — that is the Pass A value SSOT. Without it the analyzer is inert
/// (correctly scoping the rule to the producer build). The fixture-under-test is a SECOND AdditionalFile.
/// </para>
/// <para>
/// Covers the mandated fixture cases:
/// <list type="bullet">
///   <item>P1 <c>.x{font-size:0.875rem}</c> → fires at the literal, step <c>md</c></item>
///   <item>P2 <c>.x{font-size:14px}</c> → fires (px equivalence), step <c>md</c></item>
///   <item>N1 <c>.x{font-size:var(--nns-font-size-md)}</c> → silent (var ref excluded by construction)</item>
///   <item>N2 <c>.x{font-size:0.8125rem}</c> → silent (off-ladder, no matching token)</item>
///   <item>N3 the <c>:root{--nns-font-size-md:0.875rem}</c> token definition line → silent (lookbehind)</item>
///   <item>N4 <c>.icon{font-size:16px; /* nnb_css012:allow-fontsize-literal: icon glyph */}</c> → silent (marker)</item>
///   <item>N5 <c>/* font-size:0.875rem */</c> comment → silent (comment range)</item>
/// </list>
/// </para>
/// </summary>
public class CssFontSizeTokenAnalyzerTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Canonical token-source registered as an AdditionalFile in every test (Pass A SSOT).</summary>
    private const string CanonicalCss =
        ":root{--nns-font-size-sm:0.75rem;--nns-font-size-md:0.875rem;--nns-font-size-lg:1rem;}";

    private const string CanonicalCssPath = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";

    // ── Test helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the analyzer over the given fixture CSS registered as an AdditionalFile, ALONGSIDE the
    /// canonical nn-design.css token source (also an AdditionalFile).
    /// </summary>
    private static async Task VerifyCssAsync(
        string cssContent,
        string cssFilePath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssFontSizeTokenAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((CanonicalCssPath, CanonicalCss));
        test.TestState.AdditionalFiles.Add((cssFilePath, cssContent));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(
        string literal, string step, string filePath, int line, int column, int endLine, int endColumn)
    {
        return new DiagnosticResult(CssFontSizeTokenAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(literal, step);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P1 — rem literal equal to a token value fires at the literal, names step md.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P1_RemLiteral_EqualsToken_Fires_StepMd()
    {
        var css = @".x{ font-size: 0.875rem; }";
        var path = "/TestProject/Components/Widget.razor.css";
        // `.x{ font-size: ` = 15 chars → `0.875rem` at column 16, length 8 → end column 24.
        await VerifyCssAsync(css, path,
            Diagnostic("0.875rem", "md", path, 1, 16, 1, 24));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P2 — px equivalent (14px == 0.875rem × 16) fires, step md.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P2_PxLiteral_EqualsTokenPxEquivalent_Fires_StepMd()
    {
        var css = @".x{ font-size: 14px; }";
        var path = "/TestProject/Components/Widget.razor.css";
        // `.x{ font-size: ` = 15 chars → `14px` at column 16, length 4 → end column 20.
        await VerifyCssAsync(css, path,
            Diagnostic("14px", "md", path, 1, 16, 1, 20));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N1 — var(--nns-font-size-md) reference → silent (excluded by the lookbehind).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N1_VarReference_Silent()
    {
        var css = @".x{ font-size: var(--nns-font-size-md); }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N2 — off-ladder rem literal (no matching token) → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N2_OffLadderRem_Silent()
    {
        var css = @".x{ font-size: 0.8125rem; }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N3 — a --nns-font-size-md token DEFINITION line → silent (lookbehind on the leading `-`).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N3_TokenDefinitionLine_Silent()
    {
        var css = @":root{ --nns-font-size-md: 0.875rem; }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N4 — same-line per-file opt-out marker suppresses (whole-file marker scan).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N4_OptOutMarker_Silent()
    {
        var css = @".icon{ font-size: 16px; /* nnb_css012:allow-fontsize-literal: icon glyph */ }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N5 — font-size literal inside a CSS comment → silent (comment range).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N5_LiteralInComment_Silent()
    {
        var css = @"/* font-size: 0.875rem legacy */
.x{ color: red; }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }
}
