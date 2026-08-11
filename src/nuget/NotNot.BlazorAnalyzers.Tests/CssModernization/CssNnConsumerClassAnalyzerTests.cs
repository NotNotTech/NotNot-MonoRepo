using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for <see cref="CssNnConsumerClassAnalyzer"/> (NNB_CSS011) — a consumer applying CSS to an
/// <c>Nn*</c> element through a CONSUMER-OWNED class (<c>::deep .my-class</c> in <c>Foo.razor.css</c>
/// paired with <c>&lt;Nn* Class="my-class"&gt;</c> in <c>Foo.razor</c>), the loophole NNB_CSS008/009
/// leave open. Exercises the cross-file <c>.razor.css</c> ↔ paired <c>.razor</c> AdditionalText
/// correlation.
/// <para>
/// Mandated fixtures: POSITIVE fire on <c>::deep .my-section</c> + <c>&lt;NnContentSection
/// Class="my-section"&gt;</c>; NEGATIVES — own-element (no <c>::deep</c>); gray-zone
/// <c>NnBlazorTermTab</c>; <c>::deep .nns-*</c> (CSS008's job); dynamic <c>Class="@expr"</c>.
/// </para>
/// </summary>
public class CssNnConsumerClassAnalyzerTests
{
    // ── Test helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the analyzer over a paired <c>.razor.css</c> + <c>.razor</c> registered as AdditionalFiles.
    /// </summary>
    private static async Task VerifyPairAsync(
        string cssContent, string cssPath,
        string razorContent, string razorPath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssNnConsumerClassAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((cssPath, cssContent));
        test.TestState.AdditionalFiles.Add((razorPath, razorContent));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Runs the analyzer over a single <c>.razor</c> (registered as the only AdditionalFile) — the
    /// inline-<c>&lt;style&gt;</c> delivery needs no paired <c>.razor.css</c>. Mirrors
    /// <see cref="VerifyPairAsync"/>.
    /// </summary>
    private static async Task VerifyRazorAsync(
        string razorContent, string razorPath,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CssNnConsumerClassAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((razorPath, razorContent));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>The scoped <c>.razor.css</c> <c>::deep</c> delivery noun (NNB_CSS011 message arg {2}).</summary>
    private const string ScopedDelivery = "a scoped ::deep rule";

    /// <summary>The inline <c>.razor</c> <c>&lt;style&gt;</c> block delivery noun (NNB_CSS011 message arg {2}).</summary>
    private const string InlineDelivery = "an inline <style> block";

    private static DiagnosticResult Diagnostic(
        string className, string componentName, string filePath,
        int line, int column, int endLine, int endColumn, string deliveryNoun)
    {
        return new DiagnosticResult(CssNnConsumerClassAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(className, componentName, deliveryNoun);
    }

    /// <summary>
    /// Renders the NNB_CSS011 descriptor's MessageFormat with the three diagnostic args, so a test can
    /// substring-assert the delivery noun actually reaches the rendered message (F3 — the Roslyn verifier
    /// compares expected vs actual using the SAME descriptor, so an omitted {2} placeholder would be
    /// silently masked; this pins the {2} surface at runtime).
    /// </summary>
    private static string RenderedMessage(string className, string componentName, string deliveryNoun)
        => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            CssNnConsumerClassAnalyzer.RuleNoConsumerNnStyling.MessageFormat.ToString(),
            className, componentName, deliveryNoun);

    // ═══════════════════════════════════════════════════════════════════════
    // (a) POSITIVE: ::deep .my-section on a class bound to <NnContentSection> → MUST warn.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task A_DeepConsumerClass_BoundToNnComponent_Warns()
    {
        var css = @"::deep .my-section {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        // `::deep ` = 7 chars; `.my-section` subject starts at column 8, length 11 → end column 19.
        await VerifyPairAsync(css, cssPath, razor, razorPath,
            Diagnostic("my-section", "NnContentSection", cssPath, 1, 8, 1, 19, ScopedDelivery));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (b) NEGATIVE: own-element styling without ::deep → silent (no reach-in into a child Nn*).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task B_OwnElement_NoDeep_NoWarning()
    {
        var css = @".my-section {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (c) NEGATIVE: gray-zone NnBlazorTermTab → silent (consumer-local Nn*, not a design-system reach-in).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task C_GrayZoneComponent_NoWarning()
    {
        var css = @"::deep .term-host {
    min-height: 80px;
}";
        var razor = @"<NnBlazorTermTab Class=""term-host"" />";
        var cssPath = "/TestProject/Components/Sessions/SessionTermPane.razor.css";
        var razorPath = "/TestProject/Components/Sessions/SessionTermPane.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (d) NEGATIVE: ::deep .nns-* internal reach-in → silent for CSS011 (CSS008 owns the .nns-* vector).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task D_DeepNnsInternal_SilentForCss011()
    {
        var css = @"::deep .nns-content-section-body {
    max-height: 40vh;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (e) NEGATIVE: dynamic Class="@expr" → not literal-matchable → silent (documented false-negative).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task E_DynamicClassExpression_NoWarning()
    {
        var css = @"::deep .my-section {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""@_panelClass"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (f) NEGATIVE: class bound to a NON-Nn (plain HTML) element → silent (declaration lands on a div).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F_ConsumerClassOnPlainElement_NoWarning()
    {
        var css = @"::deep .my-section {
    min-height: 80px;
}";
        var razor = @"<div class=""my-section"">
    <p>body</p>
</div>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (g) NEGATIVE: class appears in the .razor.css but is NOT bound to the Nn* in the paired .razor
    //     (bound to a different, plain element) → silent. Anti-false-positive guard.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task G_ClassNotBoundToNn_NoWarning()
    {
        var css = @"::deep .other-class {
    color: red;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <span class=""other-class"">x</span>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (h) POSITIVE: class is one of several space-separated tokens on the Nn* Class="…" → MUST warn.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task H_ClassAmongMultiple_Warns()
    {
        var css = @"::deep .my-section {
    margin: 0;
}";
        var razor = @"<NnContentSection Class=""pa-2 my-section vow-flush"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath,
            Diagnostic("my-section", "NnContentSection", cssPath, 1, 8, 1, 19, ScopedDelivery));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (i) NEGATIVE: per-file opt-out comment suppresses the warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task I_PerFileOptOut_SuppressesWarning()
    {
        var css = @"/* nnb_css011:allow-reachin: transitional override pending NnContentSection.MaxHeight param */
::deep .my-section {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (j) NEGATIVE: build-property kill-switch (CssAnalyzerEnabled=false) suppresses the diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task J_BuildPropertyKillSwitch_NoWarning()
    {
        var css = @"::deep .my-section {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";
        var razorPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

        var test = new CSharpAnalyzerTest<CssNnConsumerClassAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((cssPath, css));
        test.TestState.AdditionalFiles.Add((razorPath, razor));
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", @"
is_global = true
build_property.CssAnalyzerEnabled = false
"));
        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (k) NEGATIVE: path-exempt (Pages/Samples/) reach-in → silent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task K_PathExempt_Samples_NoWarning()
    {
        var css = @"::deep .my-section {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""my-section"">
    <p>body</p>
</NnContentSection>";
        var cssPath = "/TestProject/Components/Pages/Samples/DemoPage.razor.css";
        var razorPath = "/TestProject/Components/Pages/Samples/DemoPage.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
    }

    [Fact]
    public async Task K2_PathExempt_DesktopProducer_NoWarning()
    {
        var css = @"::deep .toast-root {
    min-height: 80px;
}";
        var razor = @"<NnContentSection Class=""toast-root"">toast</NnContentSection>";
        var cssPath = "/TestProject/NotNot.BlazorDesign.Desktop/NnDesign/Desktop/EzToast/EzToast.razor.css";
        var razorPath = "/TestProject/NotNot.BlazorDesign.Desktop/NnDesign/Desktop/EzToast/EzToast.razor";

        await VerifyPairAsync(css, cssPath, razor, razorPath);
        await VerifyRazorAsync(@"<style>.toast-root{min-height:80px}</style>
<NnContentSection Class=""toast-root"">toast</NnContentSection>", razorPath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (l) NEGATIVE: no paired .razor surfaced (only the .razor.css) → silent (cannot correlate).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task L_NoPairedRazor_NoWarning()
    {
        var css = @"::deep .my-section {
    min-height: 80px;
}";
        var cssPath = "/TestProject/Components/Sessions/VowSessionMetaPanel.razor.css";

        var test = new CSharpAnalyzerTest<CssNnConsumerClassAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.AdditionalFiles.Add((cssPath, css));
        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // (F3) Rendered-message accuracy: the scoped delivery noun reaches the message text.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ScopedFixture_RenderedMessage_ContainsScopedDeliveryNoun()
    {
        var message = RenderedMessage("my-section", "NnContentSection", ScopedDelivery);
        // Assert the SUBSTITUTION CONTEXT: the `(in ` prefix originates ONLY from the `(in {2})`
        // placeholder, never the descriptor's relocation clause (which mentions the bare delivery noun
        // without the parenthesized prefix). A literal "a scoped ::deep rule" substring would also match
        // that clause, masking an empty {2} render — the parenthesized form pins {2} actually substituted.
        Assert.Contains("(in a scoped ::deep rule)", message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INLINE <style> DELIVERY — POSITIVE fixtures (fire NNB_CSS011 at the in-block selector).
    // ═══════════════════════════════════════════════════════════════════════

    // POS-1: single-line inline <style>, layout property; mirrors Options.razor sidebar
    // (.options-nav-sidebar { width: 220px } + <NnSidebarPanel Class="options-nav-sidebar">).
    [Fact]
    public async Task POS1_InlineStyle_BoundToNn_Warns()
    {
        // `<style>` = 7 chars; `.sidebar` selector starts at column 8 (1-based), length 8 → end col 16.
        var razor = @"<style>.sidebar{width:220px}</style>
<NnSidebarPanel Class=""sidebar"" />";
        var razorPath = "/TestProject/Components/Pages/Options.razor";

        await VerifyRazorAsync(razor, razorPath,
            Diagnostic("sidebar", "NnSidebarPanel", razorPath, 1, 8, 1, 16, InlineDelivery));
    }

    // POS-2: visual property, static-literal Class on an Nn* element.
    [Fact]
    public async Task POS2_InlineStyle_VisualProperty_Warns()
    {
        // `<style>` = 7 chars; `.nav-active` starts at column 8, length 11 → end col 19.
        var razor = @"<style>.nav-active{background:var(--nns-action-hover)}</style>
<NnNavLink Class=""nav-active"">x</NnNavLink>";
        var razorPath = "/TestProject/Components/Pages/Nav.razor";

        await VerifyRazorAsync(razor, razorPath,
            Diagnostic("nav-active", "NnNavLink", razorPath, 1, 8, 1, 19, InlineDelivery));
    }

    // POS-3: MULTI-LINE inline <style> NOT at file start — stresses the baseOffset + SourceText
    // line-table column math (the selector lands on line 3, indented).
    [Fact]
    public async Task POS3_InlineStyle_MultiLineNotAtStart_Warns()
    {
        var razor = "<h1>Title</h1>\n<style>\n  .panel {\n    min-height: 80px;\n  }\n</style>\n<NnContentSection Class=\"panel\" />";
        var razorPath = "/TestProject/Components/Pages/Dashboard.razor";

        // `.panel` is on line 3, after 2 leading spaces → column 3 (1-based), length 6 → end col 9.
        await VerifyRazorAsync(razor, razorPath,
            Diagnostic("panel", "NnContentSection", razorPath, 3, 3, 3, 9, InlineDelivery));
    }

    // F3: the inline delivery noun reaches the rendered message text.
    [Fact]
    public void InlineFixture_RenderedMessage_ContainsInlineDeliveryNoun()
    {
        var message = RenderedMessage("sidebar", "NnSidebarPanel", InlineDelivery);
        // Assert the SUBSTITUTION CONTEXT: the `(in ` prefix originates ONLY from the `(in {2})`
        // placeholder, NOT the relocation clause ("…and an inline <style> block is NOT a fix…") which
        // also contains the bare "an inline <style> block" substring — a bare-substring assert would pass
        // even with an empty {2} render. The parenthesized form pins {2} actually substituted.
        Assert.Contains("(in an inline <style> block)", message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INLINE <style> DELIVERY — NEGATIVE fixtures (silent).
    // ═══════════════════════════════════════════════════════════════════════

    // NEG-1: class bound to a plain element (div), not an Nn* → silent.
    [Fact]
    public async Task NEG1_InlineStyle_PlainElement_NoWarning()
    {
        var razor = @"<style>.page-root{display:flex}</style>
<div class=""page-root"">body</div>";
        var razorPath = "/TestProject/Components/Pages/Layout.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-2: dynamic Class="@expr" → not literal-matchable → silent (documented false-negative;
    // mirrors the REAL Options.razor nav-active case).
    [Fact]
    public async Task NEG2_InlineStyle_DynamicClass_NoWarning()
    {
        var razor = @"<style>.thing{color:red}</style>
<NnNavLink Class=""@NavLinkClass"">y</NnNavLink>";
        var razorPath = "/TestProject/Components/Pages/Options.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-3: gray-zone consumer-local Nn* (NnBlazorTermTab) → silent.
    [Fact]
    public async Task NEG3_InlineStyle_GrayZone_NoWarning()
    {
        var razor = @"<style>.x{min-height:80px}</style>
<NnBlazorTermTab Class=""x"" />";
        var razorPath = "/TestProject/Components/Sessions/SessionTermPane.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-4: per-file opt-out comment present → silent.
    [Fact]
    public async Task NEG4_InlineStyle_PerFileOptOut_NoWarning()
    {
        var razor = @"@* nnb_css011:allow-reachin: transitional override pending NnSidebarPanel.Width param *@
<style>.sidebar{width:220px}</style>
<NnSidebarPanel Class=""sidebar"" />";
        var razorPath = "/TestProject/Components/Pages/Options.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-5: path-exempt (Pages/Samples/) → silent.
    [Fact]
    public async Task NEG5_InlineStyle_PathExempt_NoWarning()
    {
        var razor = @"<style>.sidebar{width:220px}</style>
<NnSidebarPanel Class=""sidebar"" />";
        var razorPath = "/TestProject/Components/Pages/Samples/Demo.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-6: styled class NOT bound to any Nn* (bound to a div nested inside an Nn*) → silent.
    [Fact]
    public async Task NEG6_InlineStyle_ClassNotBoundToNn_NoWarning()
    {
        var razor = @"<style>.only-div{color:red}</style>
<NnContentSection Class=""other"">
    <span class=""only-div"">x</span>
</NnContentSection>";
        var razorPath = "/TestProject/Components/Pages/Detail.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-7 (F2): commented-out <style>+<Nn* Class> pair (Razor @* *@) → silent (dead code is stripped
    // before <style> extraction + binding-map construction).
    [Fact]
    public async Task NEG7_InlineStyle_RazorCommentedOut_NoWarning()
    {
        var razor = @"@* <style>.sidebar{width:220px}</style> <NnSidebarPanel Class=""sidebar"" /> *@
<p>live content</p>";
        var razorPath = "/TestProject/Components/Pages/Options.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-7b (F2): HTML-comment variant of the commented-out pair → silent.
    [Fact]
    public async Task NEG7b_InlineStyle_HtmlCommentedOut_NoWarning()
    {
        var razor = @"<!-- <style>.sidebar{width:220px}</style> <NnSidebarPanel Class=""sidebar"" /> -->
<p>live content</p>";
        var razorPath = "/TestProject/Components/Pages/Options.razor";

        await VerifyRazorAsync(razor, razorPath);
    }

    // NEG-8 (FIX-2): tag-shaped CSS text inside a <style> body must NOT manufacture a binding. The
    // `content: '<NnSidebarPanel Class="sidebar">'` string lives in CSS, not markup; the real `.sidebar`
    // selector binds to NOTHING in actual markup → silent. Proves the binding map is built from
    // style-body-blanked markup, not live <style> bodies (PLAN.md:48 safety invariant).
    [Fact]
    public async Task NEG8_InlineStyle_TagShapedCssText_NoFalseBinding_NoWarning()
    {
        var razor = @"<style>.sidebar{content:'<NnSidebarPanel Class=""sidebar"">';width:220px}</style>
<div class=""sidebar"">real markup, not an Nn*</div>";
        var razorPath = "/TestProject/Components/Pages/Options.razor";

        await VerifyRazorAsync(razor, razorPath);
    }
}
