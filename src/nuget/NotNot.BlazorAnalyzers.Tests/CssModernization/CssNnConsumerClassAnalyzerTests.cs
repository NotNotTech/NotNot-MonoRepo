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

    private static DiagnosticResult Diagnostic(
        string className, string componentName, string filePath,
        int line, int column, int endLine, int endColumn)
    {
        return new DiagnosticResult(CssNnConsumerClassAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(className, componentName);
    }

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
            Diagnostic("my-section", "NnContentSection", cssPath, 1, 8, 1, 19));
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
            Diagnostic("my-section", "NnContentSection", cssPath, 1, 8, 1, 19));
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
}
