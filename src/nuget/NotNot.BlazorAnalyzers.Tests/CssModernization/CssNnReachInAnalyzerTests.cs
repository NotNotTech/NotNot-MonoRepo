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
/// NnDesign internal <c>.nns-*</c> classes.
/// <para>
/// Covers the mandated fixture cases:
/// <list type="bullet">
///   <item>(a) sanctioned react-to <c>html.nns-chord-revealed .my-class</c> → MUST NOT warn</item>
///   <item>(b) reach-in <c>::deep .nns-content-section-body</c> → MUST warn</item>
///   <item>(c) allow-listed <c>.nns-chord-revealed</c> subject → MUST NOT warn</item>
///   <item>(d) path-exempt file with a reach-in → MUST NOT warn</item>
///   <item>(e) bare <c>.nns-content-section-header</c> subject restyle → MUST warn</item>
///   <item>(k) NON-VACUOUS reach-in <c>.nns-content-section-body</c> in a non-sample consumer path
///         → MUST warn (proves the guard fires after the .nn→.nns repoint, not vacuously dormant)</item>
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
        return new DiagnosticResult(CssNnReachInAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(nnClassName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (a) SANCTIONED react-to: html.nns-chord-revealed .my-class → NO warning.
    //     .nns-chord-revealed is an ANCESTOR/state gate; the SUBJECT is .my-class (non-nns).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task A_ReactTo_ChordRevealedAncestor_NoWarning()
    {
        var css = @"html.nns-chord-revealed .my-class {
    opacity: 1;
}";
        var path = "/TestProject/Components/Sessions/SessionListItem.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (b) REACH-IN: ::deep .nns-content-section-body → MUST warn.
    //     Subject is the .nns-content-section-body class (an internal NnDesign class).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task B_ReachIn_DeepNnInternalSubject_Warns()
    {
        var css = @"::deep .nns-content-section-body {
    max-height: 40vh;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `::deep ` = 7 chars; `.nns-content-section-body` starts at column 8.
        // Token (with dot) length = 25 → end column 33. Message arg drops the leading dot.
        await VerifyCssAsync(css, path,
            Diagnostic("nns-content-section-body", path, 1, 8, 1, 33));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (c) ALLOW-LISTED: .nns-chord-revealed subject → NO warning.
    //     The published public state token is the SOLE allow-list survivor (the blanket .nns-*
    //     sample-marker prefix allow was DELETED — .nns-* now denotes internals the analyzer guards).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task C_AllowListed_ChordRevealedSubject_NoWarning()
    {
        var css = @".nns-chord-revealed {
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
        var css = @"::deep .nns-content-section-body {
    max-height: 40vh;
}";
        // Under /NotNot.BlazorDesign/ — the producer legitimately defines/styles .nns-* classes.
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (e) BARE REACH-IN: .nns-content-section-header { border-bottom:none } → MUST warn.
    //     No ::deep; the subject IS the .nns-* class.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task E_ReachIn_BareNnSubject_Warns()
    {
        var css = @".nns-content-section-header {
    border-bottom: none;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `.nns-content-section-header` at column 1; token length = 27 → end column 28.
        await VerifyCssAsync(css, path,
            Diagnostic("nns-content-section-header", path, 1, 1, 1, 28));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (f) Per-file opt-out comment suppresses the warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F_PerFileOptOut_SuppressesWarning()
    {
        var css = @"/* nnb_css008:allow-reachin: legacy transitional override pending NnContentSection param */
::deep .nns-content-section-body {
    max-height: 40vh;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (g) Build-property kill-switch (CssAnalyzerEnabled=false) suppresses the diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task G_BuildPropertyKillSwitch_NoWarning()
    {
        var css = @"::deep .nns-content-section-body {
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
    // (h) State-gate with ::deep but non-nns subject → NO warning.
    //     `html.nns-chord-revealed ::deep .own-thing` — .nns-chord-revealed is ancestor; subject is .own-thing.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task H_StateGatePlusDeep_NonNnSubject_NoWarning()
    {
        var css = @".nns-chord-revealed .vow-panel {
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
        var css = @".vow-panel, ::deep .nns-popup-body {
    padding: 0;
}";
        var path = "/TestProject/Components/Pages/FindSessionDialog.razor.css";
        // List: ".vow-panel" (no warn) , " ::deep .nns-popup-body".
        // Second part begins at index 12 (after ", "); within it `::deep ` precedes the token.
        // ".vow-panel, ::deep " = 19 chars → `.nns-popup-body` at column 20, length 15 → end col 35.
        await VerifyCssAsync(css, path,
            Diagnostic("nns-popup-body", path, 1, 20, 1, 35));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (j) Reach-in inside a CSS comment is NOT flagged.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task J_ReachInInComment_NoWarning()
    {
        var css = @"/* ::deep .nns-content-section-body { } -- documented, do not ship */
.vow-panel {
    color: red;
}";
        var path = "/TestProject/Components/Pages/VowDashboard.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (k) NON-VACUOUS GUARD: bare .nns-content-section-body reach-in in a NON-sample consumer path
    //     → MUST warn. Anti-dormancy proof for the .nn-* → .nns-* repoint: after the prefix was
    //     reclaimed AND the blanket .nns-* allow was deleted, a real consumer reach-in into an
    //     internal .nns-* class must still produce NNB_CSS008. Without this assertion an "analyzer
    //     clean" build would pass vacuously even if the regex/allow-list went dormant.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task K_NonVacuous_NnsInternalReachIn_Warns()
    {
        var css = @".nns-content-section-body {
    overflow: auto;
}";
        // A genuine consumer component path — NOT under NnDesignSamples/ or Pages/Samples/, NOT a
        // producer NotNot.BlazorDesign path, NOT an allow-listed file name.
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `.nns-content-section-body` at column 1; token length = 25 → end column 26.
        await VerifyCssAsync(css, path,
            Diagnostic("nns-content-section-body", path, 1, 1, 1, 26));
    }
}
