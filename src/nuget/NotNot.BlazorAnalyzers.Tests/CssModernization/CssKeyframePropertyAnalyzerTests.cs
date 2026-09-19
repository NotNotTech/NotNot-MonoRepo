using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for <see cref="CssKeyframePropertyAnalyzer"/> (NNB_CSS014) — a <c>@keyframes</c> block may
/// declare only compositor-eligible properties (<c>opacity</c>, <c>transform</c>, <c>translate</c>,
/// <c>rotate</c>, <c>scale</c>) unless the file carries the
/// <c>nnb_css014:allow-noncomposited-keyframe</c> opt-out marker.
/// <para>
/// Unlike NNB_CSS012 there is no Pass-A value SSOT: the composited set is fixed, so every test
/// registers exactly one fixture AdditionalFile. Fixture paths are shaped like the real fed files
/// (global <c>.css</c>, scoped <c>.razor.css</c>, vendor) to assert the covered file set per F5.
/// </para>
/// <para>
/// Covers the mandated fixture cases:
/// <list type="bullet">
///   <item>P1 <c>filter</c> keyframe (alert-glow shape) → fires on the property, names the keyframe</item>
///   <item>P2 geometry keyframes (reconnect shape) → fires per bad property; <c>opacity</c> in the same block stays silent</item>
///   <item>P3 <c>color</c> keyframe (color-pulse shape) → fires</item>
///   <item>P4 <c>@media</c>-nested <c>@keyframes</c> → fires (nesting is not an escape hatch)</item>
///   <item>P5 vendor-prefixed <c>@-webkit-keyframes</c> → fires</item>
///   <item>P6 <c>stroke-dashoffset</c> keyframe (spin shape) → fires</item>
///   <item>P7 <c>border-color</c> bounded one-shot (focus-flash shape) → fires (iteration-agnostic by design)</item>
///   <item>P8 custom property (<c>--x</c>) in keyframes → fires (main-thread work, not composited)</item>
///   <item>P9 <c>url(http://…)</c> value → exactly one diagnostic (value colons do not confuse property extraction)</item>
///   <item>N1 opacity-only keyframes → silent</item>
///   <item>N2 transform-only keyframes → silent</item>
///   <item>N3 opacity+transform mixed → silent</item>
///   <item>N4 individual <c>translate</c>/<c>rotate</c>/<c>scale</c> → silent (composite like transform)</item>
///   <item>N5 opt-out marker → silent (whole-file marker scan)</item>
///   <item>N6 keyframes inside a CSS comment → silent (documentation, not a block)</item>
///   <item>N7 vendor files (<c>*.min.css</c>, <c>/lib/</c>) → silent</item>
///   <item>N8 <c>.razor</c> text → silent (inline <c>&lt;style&gt;</c> is an explicit non-goal)</item>
/// </list>
/// </para>
/// </summary>
public class CssKeyframePropertyAnalyzerTests
{
    // ── Test helpers ─────────────────────────────────────────────────────────

    private static async Task VerifyCssAsync(
        string cssContent,
        string cssFilePath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssKeyframePropertyAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((cssFilePath, cssContent));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(
        string property, string keyframeName, string filePath, int line, int column, int endLine, int endColumn)
    {
        return new DiagnosticResult(CssKeyframePropertyAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(property, keyframeName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P1 — filter keyframe (alert-glow shape) fires, names the keyframe.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P1_FilterKeyframe_Fires()
    {
        var css = "@keyframes a-glow { 0%, 100% { filter: drop-shadow(0 0 1px red); } }";
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        // `@keyframes a-glow { 0%, 100% { ` = 31 chars → `filter` at column 32, length 6 → end 38.
        await VerifyCssAsync(css, path,
            Diagnostic("filter", "a-glow", path, 1, 32, 1, 38));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P2 — geometry keyframes (reconnect shape) fire per property; opacity stays silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P2_GeometryKeyframes_FirePerProperty_OpacitySilent()
    {
        var css = "@keyframes rejoin { 0% { top: 0; left: 0; width: 0; opacity: 0; } }";
        var path = "/TestProject/Components/Layout/ReconnectModal.razor.css";
        // `@keyframes rejoin { 0% { ` = 25 chars → `top` col 26–29, `left` col 34–38, `width` col 43–48.
        await VerifyCssAsync(css, path,
            Diagnostic("top", "rejoin", path, 1, 26, 1, 29),
            Diagnostic("left", "rejoin", path, 1, 34, 1, 38),
            Diagnostic("width", "rejoin", path, 1, 43, 1, 48));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P3 — color keyframe (color-pulse shape) fires.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P3_ColorKeyframe_Fires()
    {
        var css = ".r { animation: x 1s infinite; } @keyframes x { 50% { color: red; filter: none; } }";
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        // First rule + space = 33 chars; `@keyframes x { 50% { ` = 21 → `color` col 55–60, `filter` col 67–73.
        await VerifyCssAsync(css, path,
            Diagnostic("color", "x", path, 1, 55, 1, 60),
            Diagnostic("filter", "x", path, 1, 67, 1, 73));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P4 — @media-nested @keyframes fires (nesting is not an escape hatch).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P4_MediaNestedKeyframes_Fires()
    {
        var css = @"@media (prefers-reduced-motion: reduce) {
@keyframes nested-pulse {
0%, 100% { opacity: 1; }
50% { color: blue; }
}
}";
        var path = "/TestProject/Components/Widget.razor.css";
        // Line 4 `50% { color: blue; }`: `50% { ` = 6 chars → `color` col 7–12.
        await VerifyCssAsync(css, path,
            Diagnostic("color", "nested-pulse", path, 4, 7, 4, 12));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P5 — vendor-prefixed @-webkit-keyframes fires.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P5_WebkitPrefixedKeyframes_Fires()
    {
        var css = "@-webkit-keyframes wk { from { left: 0; } to { left: 9px; } }";
        var path = "/TestProject/Components/Widget.razor.css";
        // `@-webkit-keyframes wk { from { ` = 31 → `left` col 32–36 and col 48–52.
        await VerifyCssAsync(css, path,
            Diagnostic("left", "wk", path, 1, 32, 1, 36),
            Diagnostic("left", "wk", path, 1, 48, 1, 52));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P6 — stroke-dashoffset keyframe (spin shape) fires.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P6_StrokeDashoffsetKeyframe_Fires()
    {
        var css = "@keyframes spin { from { stroke-dashoffset: 0; } to { stroke-dashoffset: -38; } }";
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        // `@keyframes spin { from { ` = 25 → first col 26–43, second col 55–72 (length 17).
        await VerifyCssAsync(css, path,
            Diagnostic("stroke-dashoffset", "spin", path, 1, 26, 1, 43),
            Diagnostic("stroke-dashoffset", "spin", path, 1, 55, 1, 72));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P7 — border-color bounded one-shot (focus-flash shape) fires: iteration-agnostic by design.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P7_BoundedOneShot_Fires()
    {
        var css = "@keyframes blazorterm-focus-flash { 0% { border-color: transparent; } 100% { border-color: green; } }";
        var path = "/TestProject/Terminal/BlazorTerm/BlazorTermDisplay.razor.css";
        // `@keyframes blazorterm-focus-flash { 0% { ` = 41 → `border-color` col 42–54 and col 78–90.
        await VerifyCssAsync(css, path,
            Diagnostic("border-color", "blazorterm-focus-flash", path, 1, 42, 1, 54),
            Diagnostic("border-color", "blazorterm-focus-flash", path, 1, 78, 1, 90));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P8 — custom property (--x) in keyframes fires.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P8_CustomProperty_Fires()
    {
        var css = "@keyframes cp { 0% { --x: 0; } 100% { --x: 1; } }";
        var path = "/TestProject/Components/Widget.razor.css";
        // `@keyframes cp { 0% { ` = 21 → `--x` col 22–25 and col 39–42.
        await VerifyCssAsync(css, path,
            Diagnostic("--x", "cp", path, 1, 22, 1, 25),
            Diagnostic("--x", "cp", path, 1, 39, 1, 42));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P9 — url(http://…) value yields exactly one diagnostic: the declaration colon wins,
    // value colons never produce a second property.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P9_UrlValue_SingleDiagnostic()
    {
        var css = "@keyframes bg { 0% { background-image: url(http://example/x.png); } }";
        var path = "/TestProject/Components/Widget.razor.css";
        // `@keyframes bg { 0% { ` = 21 → `background-image` col 22–38.
        await VerifyCssAsync(css, path,
            Diagnostic("background-image", "bg", path, 1, 22, 1, 38));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N1 — opacity-only keyframes → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N1_OpacityOnly_Silent()
    {
        var css = "@keyframes ok { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N2 — transform-only keyframes → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N2_TransformOnly_Silent()
    {
        var css = "@keyframes spin-ok { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }";
        var path = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N3 — opacity+transform mixed → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N3_OpacityTransformMixed_Silent()
    {
        var css = "@keyframes fade { 0% { opacity: 0; transform: scale(0.8); } 100% { opacity: 1; transform: scale(1); } }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N4 — individual translate/rotate/scale → silent (composite like transform).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N4_IndividualTransformProperties_Silent()
    {
        var css = "@keyframes ind { from { translate: 0 0; rotate: 0deg; scale: 1; } to { translate: 4px 0; rotate: 90deg; scale: 2; } }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N5 — per-file opt-out marker suppresses (whole-file marker scan).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N5_OptOutMarker_Silent()
    {
        var css = @"/* nnb_css014:allow-noncomposited-keyframe: bounded one-shot */
@keyframes bell { 0% { background-color: white; } 100% { background-color: black; } }";
        var path = "/TestProject/Terminal/BlazorTerm/BlazorTermDisplay.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N6 — keyframes inside a CSS comment → silent (documentation, not a block).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N6_KeyframesInComment_Silent()
    {
        var css = @"/* @keyframes fake { 0% { color: red; } } legacy */
.x{ color: red; }";
        var path = "/TestProject/Components/Widget.razor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N7 — vendor files (*.min.css, /lib/) → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N7_MinCssVendorFile_Silent()
    {
        var css = "@keyframes mud-x { 0% { left: 0; } 100% { left: 9px; } }";
        var path = "/TestProject/wwwroot/lib/vendor/MudBlazor.min.css";
        await VerifyCssAsync(css, path);
    }

    [Fact]
    public async Task N7_LibPathVendorFile_Silent()
    {
        var css = "@keyframes mud-x { 0% { stroke-dashoffset: 0; } 100% { stroke-dashoffset: -9; } }";
        var path = "/TestProject/wwwroot/lib/mudblazor/MudBlazor.css";
        await VerifyCssAsync(css, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N8 — .razor text → silent (inline <style> is an explicit non-goal).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task N8_RazorFile_Silent()
    {
        var css = "@keyframes x { 0% { color: red; } }";
        var path = "/TestProject/Components/Widget.razor";
        await VerifyCssAsync(css, path);
    }
}
