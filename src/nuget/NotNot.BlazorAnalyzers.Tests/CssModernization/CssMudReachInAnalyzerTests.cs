using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for <see cref="CssMudReachInAnalyzer"/> (NNB_CSS009) — consumer scoped CSS reaching into
/// MudBlazor internal <c>.mud-*</c> classes.
/// <para>
/// Discriminating fixture pairs (deliberate violation + clean/allow case), plus the conformity-relevant
/// reach-ins the seed wave removed (<c>.mud-tab</c> / <c>.mud-tabs-header</c>) asserted to FIRE so the
/// rule genuinely covers the migrated class.
/// </para>
/// </summary>
public class CssMudReachInAnalyzerTests
{
    /// <summary>Runs the analyzer over the given CSS content registered as an AdditionalFile.</summary>
    private static async Task VerifyCssAsync(
        string cssContent,
        string cssFilePath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssMudReachInAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((cssFilePath, cssContent));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(
        string mudClassName, string filePath, int line, int column, int endLine, int endColumn)
    {
        return new DiagnosticResult(CssMudReachInAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(mudClassName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VIOLATION: ::deep .mud-foo restyle → MUST warn (subject is the .mud-* class).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReachIn_DeepMudInternalSubject_Warns()
    {
        var css = @"::deep .mud-foo {
    max-height: 40vh;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `::deep ` = 7 chars; `.mud-foo` starts at column 8, token length 8 → end column 16.
        await VerifyCssAsync(css, path,
            Diagnostic("mud-foo", path, 1, 8, 1, 16));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VIOLATION (conformity-relevant): the tab-trigger / tab-header reach-ins the seed wave removed.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReachIn_BareMudTabSubject_Warns()
    {
        var css = @".mud-tab {
    min-width: 80px;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `.mud-tab` at column 1, token length 8 → end column 9.
        await VerifyCssAsync(css, path,
            Diagnostic("mud-tab", path, 1, 1, 1, 9));
    }

    [Fact]
    public async Task ReachIn_MudTabsHeaderSubject_Warns()
    {
        var css = @".mud-tabs-header {
    min-height: unset;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        // `.mud-tabs-header` at column 1, token length 16 → end column 17.
        await VerifyCssAsync(css, path,
            Diagnostic("mud-tabs-header", path, 1, 1, 1, 17));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CLEAN: a consumer styling its OWN .nn-*-namespaced / own class → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Clean_OwnSubjectClass_NoWarning()
    {
        var css = @".vow-metatabs-content {
    overflow: auto;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        await VerifyCssAsync(css, path);
    }

    [Fact]
    public async Task Clean_NnNamespacedSubject_NoWarning()
    {
        // NNB_CSS009 polices .mud-* only; a .nn-* subject is NNB_CSS008's concern, not this rule's.
        var css = @".nn-tabs-content {
    height: 100%;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // REACT-TO: .mud-* as an ANCESTOR/state gate on a non-mud subject → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReactTo_MudAncestor_NonMudSubject_NoWarning()
    {
        var css = @".mud-tabs-active .vow-panel {
    display: block;
}";
        var path = "/TestProject/Components/Pages/VowDashboard.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PATH-EXEMPT: a .mud-* reach-in under the producer path (NnDesign legitimately restyles mud) → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PathExempt_ProducerCss_NoWarning()
    {
        var css = @".nn-tabs.mud-tabs .mud-tab {
    text-transform: none;
}";
        // Under /NotNot.BlazorDesign/ — the wrapper layer legitimately restyles .mud-* internals.
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PER-FILE OPT-OUT: nnb_css009:allow-reachin comment suppresses the warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PerFileOptOut_SuppressesWarning()
    {
        var css = @"/* nnb_css009:allow-reachin: legacy transitional override pending NnTabs trigger-sizing */
::deep .mud-tab {
    min-width: 80px;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BUILD-PROPERTY KILL-SWITCH (shared CssAnalyzerEnabled=false) suppresses the diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildPropertyKillSwitch_NoWarning()
    {
        var css = @"::deep .mud-tab {
    min-width: 80px;
}";
        var path = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";

        var test = new CSharpAnalyzerTest<CssMudReachInAnalyzer, DefaultVerifier>
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
    // SELECTOR LIST: only the .mud-* reach-in part warns.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SelectorList_OnlyReachInPartWarns()
    {
        var css = @".vow-panel, ::deep .mud-tab-panel {
    padding: 0;
}";
        var path = "/TestProject/Components/Pages/FindSessionDialog.razor.css";
        // ".vow-panel, ::deep " = 19 chars → `.mud-tab-panel` at column 20, length 14 → end col 34.
        await VerifyCssAsync(css, path,
            Diagnostic("mud-tab-panel", path, 1, 20, 1, 34));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // COMMENT: a .mud-* reach-in inside a CSS comment is NOT flagged.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReachInInComment_NoWarning()
    {
        var css = @"/* .mud-tab { min-width: 80px } -- documented, do not ship */
.vow-panel {
    color: red;
}";
        var path = "/TestProject/Components/Pages/VowDashboard.razor.css";
        await VerifyCssAsync(css, path);
    }
}
