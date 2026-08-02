using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for NNB_CSS010 (<see cref="CssModernizationAnalyzer.RuleNoViewportUnits"/>) — viewport-relative
/// units in consumer CSS declaration values + <c>.razor</c> quoted attribute values.
/// <para>
/// Positive fixtures reproduce the two 2026-06-10 runtime-proven incidents: <c>MaxHeight="60vh"</c> on an
/// <c>NnContentSection</c> (UserInputsPanel shape — section painted below the pane clip line) and
/// <c>height:100vh</c> on a page-host class (DebugSessionInfoPage shape — 64px overflow under shell
/// chrome). Negative fixtures pin the precision discriminators: container-bounded values, the per-file
/// allow-comment, dynamic Razor values, comments, producer/samples path buckets, prose mentions, and the
/// shared <c>CssAnalyzerEnabled</c> kill-switch.
/// </para>
/// </summary>
public class CssViewportUnitTests
{
    /// <summary>Runs the analyzer over the given content registered as an AdditionalFile.</summary>
    private static async Task VerifyFileAsync(
        string content,
        string filePath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssModernizationAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((filePath, content));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(
        string unitToken, string filePath, int line, int column, int endLine, int endColumn)
    {
        return new DiagnosticResult(CssModernizationAnalyzer.RuleNoViewportUnits.Id, DiagnosticSeverity.Error)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(unitToken);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P1 — scoped CSS (.razor.css) page-host 100vh → MUST fire (DebugSessionInfoPage incident shape).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Positive_RazorCss_Height100vh_Fires()
    {
        var css = @".vow-host {
    height: 100vh;
}";
        var path = "/TestProject/Components/Pages/DebugSessionInfoPage.razor.css";
        // Line 2: 4 spaces + "height: " (8 chars) = 12 → `100vh` at col 13, length 5 → end col 18.
        await VerifyFileAsync(css, path,
            Diagnostic("100vh", path, 2, 13, 2, 18));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P2 — .razor quoted attribute value 60vh → MUST fire (UserInputsPanel incident shape).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Positive_RazorAttribute_MaxHeight60vh_Fires()
    {
        var razor = @"<NnContentSection MaxHeight=""60vh"">
    <div>content</div>
</NnContentSection>";
        var path = "/TestProject/Components/Pages/UserInputsPanel.razor";
        // `<NnContentSection ` (18) + `MaxHeight=` (10) + `""` (1) = 29 → `60vh` at col 30, len 4 → end col 34.
        await VerifyFileAsync(razor, path,
            Diagnostic("60vh", path, 1, 30, 1, 34));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P3 — global CSS min-height 60vh → MUST fire (property-agnostic: min-height, not just height).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Positive_GlobalCss_MinHeight60vh_Fires()
    {
        var css = @".x { min-height: 60vh; }";
        var path = "/TestProject/wwwroot/css/site.css";
        // `.x { min-height: ` = 17 chars → `60vh` at col 18, len 4 → end col 22.
        await VerifyFileAsync(css, path,
            Diagnostic("60vh", path, 1, 18, 1, 22));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P4 — dynamic viewport variant (100dvh) → MUST fire (regex covers d/s/l-prefixed units).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Positive_GlobalCss_DynamicViewportVariant_Fires()
    {
        var css = @".y { height: 100dvh; }";
        var path = "/TestProject/wwwroot/css/site.css";
        // `.y { height: ` = 13 chars → `100dvh` at col 14, len 6 → end col 20.
        await VerifyFileAsync(css, path,
            Diagnostic("100dvh", path, 1, 14, 1, 20));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N1 — container-bounded values (%, calc(%), px) → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_ContainerBoundedValues_Silent()
    {
        var css = @".vow-host {
    height: 100%;
    max-height: calc(100% - 8px);
    width: 250px;
}";
        var path = "/TestProject/Components/Pages/DebugSessionInfoPage.razor.css";
        await VerifyFileAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N2 — per-file allow-comment (portal-overlay class) suppresses the whole file.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_AllowComment_Silent()
    {
        var css = @"/* nnb_css010:allow-viewport-unit: portal overlay is correctly viewport-anchored */
.vow-modal {
    height: 100vh;
    max-width: 90vw;
}";
        var path = "/TestProject/Components/Dialogs/VowModal.razor.css";
        await VerifyFileAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N3 — dynamic Razor values: `@` in the enclosing quoted value → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_RazorDynamicValue_Silent()
    {
        // First div: recipe-verbatim dynamic shape (no numeric token at all once @-bound).
        // Second div: a NUMERIC viewport token co-resident with a Razor expression in the SAME quoted
        // value — exercises the @-skip discriminator directly.
        var razor = @"<div style=""height:@(h)vh""></div>
<div style=""@_style; max-height:60vh""></div>
@code { private int h = 50; private string _style = """"; }";
        var path = "/TestProject/Components/Pages/UserInputsPanel.razor";
        await VerifyFileAsync(razor, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N4 — viewport unit inside a CSS comment → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_CssComment_Silent()
    {
        var css = @"/* was height: 100vh — replaced with container-bounded sizing, do not regress */
.vow-host {
    height: 100%;
}";
        var path = "/TestProject/Components/Pages/DebugSessionInfoPage.razor.css";
        await VerifyFileAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N5 — producer path bucket (NnDesign legitimately owns sanctioned viewport sizing) → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_ProducerPath_Silent()
    {
        var css = @".nn-app-shell {
    height: 100vh;
}
.nn-popup {
    max-width: 90vw;
    max-height: 85vh;
}";
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        await VerifyFileAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N6 — samples path bucket (.razor under Pages/Samples) → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_SamplesPath_Silent()
    {
        var razor = @"<NnContentSection Height=""60vh"">demo</NnContentSection>";
        var path = "/TestProject/Components/Pages/Samples/SampleViewport.razor";
        await VerifyFileAsync(razor, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N7 — razor prose/doc text outside an attribute value → silent (attribute-value scan scope).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_RazorProseMention_Silent()
    {
        var razor = @"<p>Use <code>""50vw""</code> only for viewport-owned surfaces; prefer 100% in bounded boxes.</p>";
        var path = "/TestProject/Components/Pages/SampleNnPaper.razor";
        await VerifyFileAsync(razor, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N8 — shared CssAnalyzerEnabled=false build-property kill-switch → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_BuildPropertyKillSwitch_Silent()
    {
        var css = @".vow-host {
    height: 100vh;
}";
        var path = "/TestProject/Components/Pages/DebugSessionInfoPage.razor.css";

        var test = new CSharpAnalyzerTest<CssModernizationAnalyzer, DefaultVerifier>
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
    // N9 — viewport units inside HTML / Razor comments in .razor markup → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Negative_RazorComments_Silent()
    {
        var razor = @"<!-- <div style=""height:100vh""></div> -->
@* <div style=""width:80vw""></div> *@
<div class=""ok""></div>";
        var path = "/TestProject/Components/Pages/UserInputsPanel.razor";
        await VerifyFileAsync(razor, path);
    }
}
