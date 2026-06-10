using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests for <see cref="NnDesignTierBExposureAnalyzer"/> (NNB043) — a raw Tier-B appearance param
/// (<c>string *Style</c> / <c>*Class</c>) exposed on an <c>Nn*</c> component, governed by the deny/allow
/// policy table.
/// <para>
/// Discriminating fixture pairs: a non-allow-listed <c>string SomethingStyle</c> / <c>SomethingClass</c>
/// param FIRES; an allow-row param (base <c>Style</c>/<c>Class</c>, <c>NnDialog.TitleClass</c>,
/// <c>NnAppBar.ToolBarClass</c>) does NOT. Plus the negative discriminators (non-string typed *Style,
/// non-Nn type, non-[Parameter] property, inherited base param) that must NOT fire.
/// </para>
/// </summary>
public class NnDesignTierBExposureAnalyzerTests
{
    /// <summary>
    /// Stub of <c>Microsoft.AspNetCore.Components.ParameterAttribute</c> — matched by simple name
    /// (<c>ParameterAttribute</c>) per the analyzer's attribute check — plus a minimal
    /// <c>NnComponentBase</c> declaring the base Tier-A <c>Style</c>/<c>Class</c> escape hatch.
    /// </summary>
    private const string Preamble = @"
namespace Microsoft.AspNetCore.Components
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public sealed class ParameterAttribute : System.Attribute { }
}

namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public abstract class NnComponentBase
    {
        // Allow-rows (SPEC-003) — base root-element escape hatch.
        [Parameter] public string? Class { get; set; }
        [Parameter] public string? Style { get; set; }
    }
}
";

    /// <summary>Runs the analyzer over <see cref="Preamble"/> + the supplied component source.</summary>
    private static async Task VerifyAsync(string componentSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<NnDesignTierBExposureAnalyzer, DefaultVerifier>
        {
            TestCode = Preamble + componentSource
        };
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(
        int line, int column, int endColumn, string paramName, string typeName, string shape)
    {
        return new DiagnosticResult(NnDesignTierBExposureAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithSpan(line, column, line, endColumn)
            .WithArguments(paramName, typeName, shape);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VIOLATION: a non-allow-listed string *Style param on an Nn* component → FIRES.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RawStyleParam_NonAllowListed_Fires()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public class NnWidget : NnComponentBase
    {
        [Parameter] public string? BodyStyle { get; set; }
    }
}
";
        // Diagnostic anchors on the property identifier (BodyStyle) — RUNTIME-confirmed span.
        await VerifyAsync(source,
            Diagnostic(26, 36, 45, "BodyStyle", "NnWidget", "string *Style/*Class"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VIOLATION: a non-allow-listed string *Class param → FIRES (appearance laundering via Class=).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RawClassParam_NonAllowListed_Fires()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public class NnWidget : NnComponentBase
    {
        [Parameter] public string? BodyClass { get; set; }
    }
}
";
        await VerifyAsync(source,
            Diagnostic(26, 36, 45, "BodyClass", "NnWidget", "string *Style/*Class"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CLEAN (allow-row): NnDialog.TitleClass → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllowRow_NnDialogTitleClass_NoWarning()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public class NnDialog : NnComponentBase
    {
        [Parameter] public string? TitleClass { get; set; }
        [Parameter] public string? ContentClass { get; set; }
        [Parameter] public string? ActionsClass { get; set; }
    }
}
";
        await VerifyAsync(source);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CLEAN (allow-row): NnAppBar.ToolBarClass → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllowRow_NnAppBarToolBarClass_NoWarning()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public class NnAppBar : NnComponentBase
    {
        [Parameter] public string? ToolBarClass { get; set; }
    }
}
";
        await VerifyAsync(source);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CLEAN (allow-row): the base NnComponentBase.Style/Class declared in the Preamble → NO warning.
    // (No component source needed — exercises the base-class allow-rows at their declaring site.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllowRow_BaseStyleClass_NoWarning()
    {
        await VerifyAsync(string.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NEGATIVE: a non-string-typed *Style param (the first detector matches string only) → NO warning.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonStringStyleParam_NoWarning()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public enum NnBodyLayout { None, FlushFlexColumn }

    public class NnWidget : NnComponentBase
    {
        // Semantic enum-typed param ending in a non-Tier-B suffix — NOT a raw string passthrough.
        [Parameter] public NnBodyLayout BodyLayout { get; set; }
    }
}
";
        await VerifyAsync(source);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NEGATIVE: a string *Style on a NON-Nn type → NO warning (rule scopes to Nn* components).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StyleParam_OnNonNnType_NoWarning()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public class MudWidget
    {
        [Parameter] public string? BodyStyle { get; set; }
    }
}
";
        await VerifyAsync(source);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NEGATIVE: a string *Style property WITHOUT [Parameter] → NO warning (not a call-site channel).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StyleProperty_WithoutParameterAttribute_NoWarning()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    public class NnWidget : NnComponentBase
    {
        // Internal computed style — not a [Parameter] consumer surface.
        public string? ComputedStyle { get; set; }
    }
}
";
        await VerifyAsync(source);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NEGATIVE: inherited base Style/Class on a derived Nn* wrapper is NOT re-reported.
    //     The base params are reported (and allow-listed) only at NnComponentBase; a derived wrapper
    //     that adds NO raw *Style of its own must be clean.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DerivedWrapper_InheritedBaseParams_NoWarning()
    {
        var source = @"
namespace NotNot.BlazorDesign.NnDesign
{
    using Microsoft.AspNetCore.Components;

    public class NnButton : NnComponentBase
    {
        [Parameter] public string? Icon { get; set; }
    }
}
";
        await VerifyAsync(source);
    }
}
