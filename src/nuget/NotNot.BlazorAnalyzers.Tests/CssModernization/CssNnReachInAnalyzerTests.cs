using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for <see cref="CssNnReachInAnalyzer"/> (NNB_CSS008) — consumer scoped CSS reaching into
/// NnDesign internal <c>.nn-*</c> classes.
/// <para>
/// Covers the five mandated fixture cases:
/// <list type="bullet">
///   <item>(a) sanctioned react-to <c>html.nn-chord-revealed .my-class</c> → MUST NOT warn</item>
///   <item>(b) reach-in <c>::deep .nn-content-section-body</c> → MUST warn</item>
///   <item>(c) allow-listed <c>.nns-card</c> → MUST NOT warn</item>
///   <item>(d) path-exempt file with a reach-in → MUST NOT warn</item>
///   <item>(e) bare <c>.nn-content-section-header</c> subject restyle → MUST warn</item>
/// </list>
/// </para>
/// </summary>
public class CssNnReachInAnalyzerTests
{
    // ── Test helpers ────────────────────────────────────────────────────────

    /// <summary>Runs the analyzer over the given CSS content registered as an AdditionalFile.</summary>
    private static async Task VerifyCssAsync(
        string cssContent,
        string cssFilePath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssNnReachInAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((cssFilePath, cssContent));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(
        string nnClassName, string filePath, int line, int column, int endLine, int endColumn)
    {
        return new DiagnosticResult(CssNnReachInAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(nnClassName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (a) SANCTIONED react-to: html.nn-chord-revealed .my-class → NO warning.
    //     .nn-chord-revealed is an ANCESTOR/state gate; the SUBJECT is .my-class (non-nn).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task A_ReactTo_ChordRevealedAncestor_NoWarning()
    {
        var css = @"html.nn-chord-revealed .my-class {
    opacity: 1;
}";
        var path = "/TestProject/Components/Sessions/SessionListItem.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (b) REACH-IN: ::deep .nn-content-section-body → MUST warn.
    //     Subject is the .nn-content-section-body class (an internal NnDesign class).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task B_ReachIn_DeepNnInternalSubject_Warns()
    {
        var css = @"::deep .nn-content-section-body {
    max-height: 40vh;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `::deep ` = 7 chars; `.nn-content-section-body` starts at column 8.
        // Token (with dot) length = 24 → end column 32. Message arg drops the leading dot.
        await VerifyCssAsync(css, path,
            Diagnostic("nn-content-section-body", path, 1, 8, 1, 32));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (c) ALLOW-LISTED: .nns-card subject → NO warning (.nns-* sample-marker prefix).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task C_AllowListed_NnsPrefixSubject_NoWarning()
    {
        var css = @".nns-card {
    border: 1px solid red;
}";
        var path = "/TestProject/Components/Pages/VowDashboard.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (d) PATH-EXEMPT: a reach-in under an exempt path (NnDesign producer) → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task D_PathExempt_ProducerCss_NoWarning()
    {
        var css = @"::deep .nn-content-section-body {
    max-height: 40vh;
}";
        // Under /NotNot.BlazorDesign/ — the producer legitimately defines/styles .nn-* classes.
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (e) BARE REACH-IN: .nn-content-section-header { border-bottom:none } → MUST warn.
    //     No ::deep; the subject IS the .nn-* class.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task E_ReachIn_BareNnSubject_Warns()
    {
        var css = @".nn-content-section-header {
    border-bottom: none;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `.nn-content-section-header` at column 1; token length = 26 → end column 27.
        await VerifyCssAsync(css, path,
            Diagnostic("nn-content-section-header", path, 1, 1, 1, 27));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (f) Per-file opt-out comment suppresses the warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F_PerFileOptOut_SuppressesWarning()
    {
        var css = @"/* nnb_css008:allow-reachin: legacy transitional override pending NnContentSection param */
::deep .nn-content-section-body {
    max-height: 40vh;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (g) Build-property kill-switch (CssAnalyzerEnabled=false) suppresses the warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task G_BuildPropertyKillSwitch_NoWarning()
    {
        var css = @"::deep .nn-content-section-body {
    max-height: 40vh;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";

        var test = new CSharpAnalyzerTest<CssNnReachInAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((path, css));
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", @"
is_global = true
build_property.CssAnalyzerEnabled = false
"));
        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (h) State-gate with ::deep but non-nn subject → NO warning.
    //     `html.nn-chord-revealed ::deep .own-thing` — .nn-chord-revealed is ancestor; subject is .own-thing.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task H_StateGatePlusDeep_NonNnSubject_NoWarning()
    {
        var css = @".nn-chord-revealed .vow-panel {
    display: block;
}";
        var path = "/TestProject/Components/Pages/VowDashboard.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (i) Comma-separated selector list: only the reach-in part warns.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task I_SelectorList_OnlyReachInPartWarns()
    {
        var css = @".vow-panel, ::deep .nn-popup-body {
    padding: 0;
}";
        var path = "/TestProject/Components/Pages/FindSessionDialog.razor.css";
        // List: ".vow-panel" (no warn) , " ::deep .nn-popup-body".
        // Second part begins at index 12 (after ", "); within it `::deep ` precedes the token.
        // ".vow-panel, ::deep " = 19 chars → `.nn-popup-body` at column 20, length 14 → end col 34.
        await VerifyCssAsync(css, path,
            Diagnostic("nn-popup-body", path, 1, 20, 1, 34));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (j) Reach-in inside a CSS comment is NOT flagged.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task J_ReachInInComment_NoWarning()
    {
        var css = @"/* ::deep .nn-content-section-body { } -- documented, do not ship */
.vow-panel {
    color: red;
}";
        var path = "/TestProject/Components/Pages/VowDashboard.razor.css";
        await VerifyCssAsync(css, path);
    }
}
