using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.Localization;

namespace NotNot.BlazorAnalyzers.Tests.Localization;

/// <summary>
/// Tests for LocalizationAnalyzer (NNB014).
/// Verifies detection of hardcoded user-facing strings in Razor markup files.
/// </summary>
public class LocalizationAnalyzerTests
{
    private const string PerFileEnabledOption =
        "build_metadata.AdditionalFiles.LocalizationAnalyzerEnabled";

    /// <summary>
    /// Global analyzer config declaring the localization contract. NNB014 is opt-in, so
    /// every test that expects diagnostics runs under this declaration.
    /// </summary>
    private const string ContractDeclaredGlobalConfig = @"
is_global = true
build_property.LocalizationAnalyzerEnabled = true
";

    /// <summary>
    /// Creates a test for AdditionalText-based Razor analysis. By default the test project
    /// declares the localization contract; pass <paramref name="declareContract"/>=false to
    /// supply (or omit) the declaration explicitly.
    /// </summary>
    private static CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier> CreateRazorTest(
        bool declareContract = true)
    {
        var test = new CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier>
        {
            // Minimal C# source — analyzer doesn't inspect C# trees
            TestCode = "class Placeholder { }"
        };

        if (declareContract)
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", ContractDeclaredGlobalConfig));

        return test;
    }

    /// <summary>Creates a test whose only analyzer config is the given global config body.</summary>
    private static CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier> CreateRazorTestWithGlobalConfig(
        string globalConfig)
    {
        var test = CreateRazorTest(declareContract: false);
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", globalConfig));
        return test;
    }

    /// <summary>
    /// Creates a test with the given .razor content as an AdditionalFile and runs it.
    /// The analyzer scans AdditionalTexts (not C# syntax trees), so we add the Razor
    /// content via <see cref="AnalyzerTest{TVerifier}.TestState"/> AdditionalFiles.
    /// </summary>
    private static async Task VerifyRazorAsync(
        string razorContent,
        string razorFilePath = "/TestProject/Components/TestComponent.razor",
        params DiagnosticResult[] expected)
    {
        var test = CreateRazorTest();

        // Register the .razor file as an AdditionalText (same as .props does at build time)
        test.TestState.AdditionalFiles.Add((razorFilePath, razorContent));

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    /// <summary>
    /// Creates a test with opt-out enabled (<c>LocalizationAnalyzerEnabled=false</c>).
    /// </summary>
    private static async Task VerifyRazorOptedOutAsync(string razorContent)
    {
        var test = CreateRazorTest(declareContract: false);

        test.TestState.AdditionalFiles.Add(
            ("/TestProject/Components/TestComponent.razor", razorContent));

        // Simulate the build property opt-out via .globalconfig
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", @"
is_global = true
build_property.LocalizationAnalyzerEnabled = false
"));

        // No expected diagnostics — analyzer should be disabled
        await test.RunAsync();
    }

    /// <summary>
    /// Creates a test with the given .razor.cs content as an AdditionalFile and runs it.
    /// Used for testing code-behind file analysis.
    /// </summary>
    private static async Task VerifyRazorCsAsync(
        string csharpContent,
        string razorCsFilePath = "/TestProject/Components/TestComponent.razor.cs",
        params DiagnosticResult[] expected)
    {
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((razorCsFilePath, csharpContent));

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    /// <summary>
    /// Helper to create a DiagnosticResult for NNB014 at a specific AdditionalFile location.
    /// </summary>
    private static DiagnosticResult Diagnostic(string displayText, string filePath, int line, int column)
    {
        return new DiagnosticResult(LocalizationAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithLocation(filePath, line, column)
            .WithArguments(displayText);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Hardcoded text node in markup → NNB014
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HardcodedTextInMarkup_ReportsNNB014()
    {
        var razor = @"<h1>Welcome to our app</h1>";
        // The text "Welcome to our app" is between <h1> and </h1>
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Welcome to our app", path, 1, 5));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Hardcoded Label attribute → NNB014
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HardcodedLabelAttribute_ReportsNNB014()
    {
        var razor = @"<MudTextField Label=""Submit Form"" />";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Submit Form", path, 1, 22));
    }

    [Fact]
    public async Task HardcodedTitleAttribute_ReportsNNB014()
    {
        var razor = @"<MudButton Title=""Click Me"">Go</MudButton>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Click Me", path, 1, 19),
            Diagnostic("Go", path, 1, 29));
    }

    [Fact]
    public async Task HardcodedPlaceholderAttribute_ReportsNNB014()
    {
        var razor = @"<MudTextField Placeholder=""Enter your name"" />";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Enter your name", path, 1, 28));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: @L["Key"] text → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LocalizedText_NoWarning()
    {
        var razor = @"<h1>@L[""Welcome""]</h1>";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task LocalizedAttribute_NoWarning()
    {
        var razor = @"<MudTextField Label=""@L[""""Submit Form""""]"" />";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: @page directive → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PageDirective_NoWarning()
    {
        var razor = @"@page ""/dashboard""
@using SomeNamespace
@inject SomeService Svc";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Razor directives/control-flow lines are syntax, not text
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorDirectiveAndControlFlowLines_NoWarning()
    {
        var razor = @"@namespace TestProject.Pages
@using TestProject.Components
@inject TestService Service
@page ""/route""
@if (true)
@else
@foreach (var item in items)
@for (var i = 0; i < 1; i++)
@switch (value)
@case 1:
@while (false)
@default:
@rendermode InteractiveWebAssembly";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Control-flow filtering does not hide real markup text
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorControlFlow_PreservesMarkupTextDetection()
    {
        var razor = @"@if (show)
{
    <p>Visible text</p>
}
else
{
    <p>Fallback text</p>
}";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible text", path, 3, 8),
            Diagnostic("Fallback text", path, 7, 8));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Mixed Razor control-flow bodies classify C# as code
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorControlFlowBody_SkipsStatementsReportsMarkup()
    {
        var razor = @"@if (show)
{
    var detail = GetDetail();
    var pct = detail.Percent;
    if (pct > 0)
    {
        <span>Visible percentage</span>
    }
    return;
}";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible percentage", path, 7, 15));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Multiline nested Razor expressions do not expose C# fragments
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NestedRazorExpression_MultilineFragments_NoWarning()
    {
        var razor = @"@foreach (var item in items)
{
    <span>@(
        item.Detail?.Name ?? ""fallback""
    )</span>
}";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Implicit Razor code retains localizable assignment detection
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ImplicitRazorCode_LocalizableAssignmentStillReports()
    {
        var razor = @"@{
    var detail = GetDetail();
    Title = ""Rendered title"";
}
<p>Visible text</p>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Rendered title", path, 3, 14),
            Diagnostic("Visible text", path, 5, 4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: CSS inside a Razor style element is not user-facing text
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorStyleBlock_SkipsCssReportsMarkup()
    {
        var razor = @"<style>
    .demo {
        display: flex;
    }
</style>
<p>Visible text</p>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible text", path, 6, 4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Pure numeric → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PureNumericText_NoWarning()
    {
        var razor = @"<span>42</span>
<span>3.14</span>
<span>-100</span>";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: NnDesignSamples path → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NnDesignSamplesPath_NoWarning()
    {
        var razor = @"<h1>Sample Heading</h1>";
        var path = "/TestProject/NnDesignSamples/SampleComponent.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((path, razor));
        test.TestState.AnalyzerConfigFiles.Add(
            ("/TestProject/.editorconfig", $@"
root = true

[NnDesignSamples/SampleComponent.razor]
{PerFileEnabledOption} = false
"));

        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Opt-out LocalizationAnalyzerEnabled=false → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OptOut_NoWarning()
    {
        var razor = @"<h1>This should not trigger</h1>";

        await VerifyRazorOptedOutAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Per-AdditionalText enable overrides the global opt-out
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PerFileEnabled_OverridesGlobalOptOut_ReportsNNB014()
    {
        var razor = @"<h1>Enabled file</h1>";
        var path = "/TestProject/Enabled.razor";
        var test = CreateRazorTest(declareContract: false);

        test.TestState.AdditionalFiles.Add((path, razor));
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", @"
is_global = true
build_property.LocalizationAnalyzerEnabled = false
"));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Enabled.razor]
{PerFileEnabledOption} = true
"));
        test.ExpectedDiagnostics.Add(
            Diagnostic("Enabled file", path, 1, 5));

        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Per-AdditionalText disable skips one file
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PerFileDisabled_NoWarning()
    {
        var razor = @"<h1>Disabled file</h1>";
        var path = "/TestProject/Disabled.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((path, razor));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Disabled.razor]
{PerFileEnabledOption} = false
"));

        await test.RunAsync();
    }

    [Fact]
    public async Task NestedPathPerFileDisabled_NoWarning()
    {
        var razor = @"<h1>Disabled nested file</h1>";
        var path = "/TestProject/Components/Pages/NnDesignSamples/Sample.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((path, razor));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Components/Pages/NnDesignSamples/**/*.razor]
{PerFileEnabledOption} = false
"));

        await test.RunAsync();
    }

    [Fact]
    public async Task NestedLocalizedAttributes_NoWarning()
    {
        var razor = "<NnTextField T=\"string\"\n"
            + "                    Label=\"@L[\"SearchTranscriptsPlaceholder:UI.Prompt\"]\" Placeholder=\"@null\"\n"
            + "                    Validation=\"@(v => L[\"FieldRequired:UI.Format\", v])\" />\n"
            + "<NnChip Label=\"@ProviderLabel(provider)\" />";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task StandaloneMarkupClose_NoWarning()
    {
        var razor = @"<NnTextField
    Label=""@L[""""SearchTranscriptsPlaceholder:UI.Prompt""""]""
/>";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task MultilineMarkupLambdaExpression_SkipsCodeFragments()
    {
        var razor = string.Join("\n", new[]
        {
            "<NnLiveValue Projection=\"@(() =>",
            "{",
            "    var detail = GetDetail();",
            "    var has = detail is not null;",
            "    return new Snapshot(",
            "        detail.Value,",
            "        has);",
            "})\" />"
        });

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task MemberAndExpressionContinuations_SkipCodeButKeepMarkupText()
    {
        var razor = string.Join("\n", new[]
        {
            "@if (show)",
            "{",
            "    option.ValueKind is SessionAppConfigValueKind.Text or SessionAppConfigValueKind.Boolean",
            "    @(",
            "        detail.ThroughputSamples,",
            "        snap,",
            "        prod);",
            "    )",
            "    <p>Visible label</p>",
            "}"
        });
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible label", path, 9, 8));
    }

    [Fact]
    public async Task MarkupEntitiesIconsAndSyntax_SkipNonTextTokens()
    {
        var razor = string.Join("\n", new[]
        {
            "<span>&gt;</span>",
            "data-nn-toc-anchor>",
            "=\"",
            "<span>🗙</span>",
            "<p>Visible label</p>"
        });
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible label", path, 5, 4));
    }

    [Fact]
    public async Task AttributeExpressionsAndInlineStyleContinuations_NoWarning()
    {
        var razor = string.Join("\n", new[]
        {
            "<NnButton Variant=\"Variant.Outlined\"",
            "          Disabled=\"@OfferActionsDisabled\"",
            "          Title=\"@ActionsDisabledReason\"",
            "          OnClick=\"OnIgnoreAsync\">",
            "    @L[\"Ignore:UI.Action\"]",
            "</NnButton>",
            "<div style=\"flex: 1; min-width: 2px;",
            "            height: @(Math.Max(pct, 1))%; border-radius: 1px;\">",
            "    <span>Visible text</span>",
            "</div>"
        });

        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible text", path, 9, 11));
    }

    [Fact]
    public async Task ActualVowMultilineAttributeExpressions_NoFalsePositives()
    {
        var razor = string.Join("\n", new[]
        {
            "<NnTextField T=\"string\" @bind-Value=\"_query\" @bind-Value:after=\"HandleSearchValueChanged\"",
            "              Label=\"@L[\"SearchTranscriptsPlaceholder:UI.Prompt\"]\" Placeholder=\"@null\" Variant=\"Variant.Outlined\"",
            "              Immediate=\"true\" DebounceInterval=\"1000\" Adornment=\"Adornment.Start\" AdornmentIcon=\"@NnIcons.Search\" />",
            "<NnTextField T=\"string\"",
            "             Value=\"_sessionUuid\"",
            "             ValueChanged=\"@((string? v) => HandleUuidChanged(v))\"",
            "             Label=\"@L[\"LabelSessionUuid:UI.Label\"].Value\"",
            "             Placeholder=\"@L[\"SessionUuidPlaceholder:UI.Prompt\"].Value\"",
            "             Variant=\"Variant.Outlined\" AutoFocus=\"true\" />",
            "<NnTooltip Text=\"@status.ToStatusTooltipText(L)\">",
            "    <NnText>@status.ToStatusText(L)</NnText>",
            "</NnTooltip>",
            "<NnChip Label=\"@ProviderLabel(a.Provider)\" />",
            "<NnTextField Label=\"@DisplayLabel\" Placeholder=\"@Placeholder\" />",
            "<NnTooltip Text=\"@RevealUnavailableReason()\">",
            "    <span>@L[\"Visible:UI.Label\"]</span>",
            "</NnTooltip>",
            "<NnButton Title=\"@ActionsDisabledReason\" OnClick=\"OnInstallAsync\">Install</NnButton>",
            "<NnSwitch T=\"bool\"",
            "          Label=\"@option.Label\"",
            "          Value=\"@GetBooleanOption(option)\"",
            "          ValueChanged=\"@(value => HandleBooleanOptionChanged(option, value))\" />",
            "<NnText>",
            "    <text> [@L[\"VowEditReadOnly:UI.Label\"].Value]</text>",
            "    <text> @L[\"VowEditCreated:UI.Label\"].Value:@created.ToLocalTime().ToString(\"g\")</text>",
            "</NnText>"
        });

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task ActualVowAccountsFile_NoFalsePositives()
    {
        var sourcePath = @"V:\r\2026-claude-conversation-tree\src\private-proj\Novaleaf.VibeOverwatch\Novaleaf.VibeOverwatch.Shared\Components\Pages\VowAccountsPage.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add(("/TestProject/VowAccountsPage.razor", File.ReadAllText(sourcePath)));

        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Mixed per-AdditionalText settings share one compilation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedPerFileConfiguration_AnalyzesOnlyEnabledAdditionalText()
    {
        var enabledPath = "/TestProject/Enabled.razor";
        var disabledPath = "/TestProject/Disabled.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((enabledPath, @"<h1>Enabled file</h1>"));
        test.TestState.AdditionalFiles.Add((disabledPath, @"<h1>Disabled file</h1>"));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Enabled.razor]
{PerFileEnabledOption} = true

[Disabled.razor]
{PerFileEnabledOption} = false
"));
        test.ExpectedDiagnostics.Add(
            Diagnostic("Enabled file", enabledPath, 1, 5));

        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: @code block content → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlockContent_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private string title = ""Hello World"";
    private void DoStuff() { var x = ""test""; }
}";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Non-localizable attributes → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonLocalizableAttributes_NoWarning()
    {
        var razor = @"<MudButton Class=""my-class"" Style=""color: red"" Id=""btn1"" Href=""/home"" Icon=""@Icons.Material.Add"" Color=""Primary"" Variant=""Filled"" />";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Multiline component attributes do not become text nodes
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultilineAttributeContinuation_SkipsSyntaxButChecksLocalizableAttribute()
    {
        var razor = @"<MudButton
    OnClick=""HandleClick""
    Label=""Save""
    Value=""@value"">
</MudButton>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Save", path, 3, 12));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: HTML comments → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HtmlComment_NoWarning()
    {
        var razor = @"<!-- This is a comment with hardcoded text -->";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Razor comments → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorComment_NoWarning()
    {
        var razor = @"@* This is a Razor comment *@";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Single character → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SingleCharacterText_NoWarning()
    {
        var razor = @"<span>X</span>";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Pure whitespace text → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WhitespaceOnlyText_NoWarning()
    {
        var razor = @"<div>   </div>";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: .css file → not analyzed (Phase 3A is .razor only)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CssFile_NotAnalyzed()
    {
        // Even though this has "label: Submit" it's a CSS file, not Razor
        var test = CreateRazorTest();
        test.TestState.AdditionalFiles.Add(
            ("/TestProject/wwwroot/app.css", "label { content: 'Submit'; }"));

        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Razor expression text → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorExpressionText_NoWarning()
    {
        var razor = @"<span>@someVariable</span>
<span>@(DateTime.Now.ToString())</span>";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Multiple hardcoded strings → multiple NNB014
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultipleHardcodedStrings_ReportsMultipleNNB014()
    {
        var razor = @"<h1>Welcome</h1>
<p>Please log in to continue</p>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Welcome", path, 1, 5),
            Diagnostic("Please log in to continue", path, 2, 4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Mixed localized and hardcoded → only hardcoded reports
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedLocalizedAndHardcoded_OnlyHardcodedReports()
    {
        var razor = @"<h1>@L[""Welcome""]</h1>
<p>Please sign in</p>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Please sign in", path, 2, 4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 3B: C# string detection in @code blocks
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlock_HardcodedTitleAssignment_ReportsNNB014()
    {
        var razor = @"<div></div>
@code {
    private void Init()
    {
        Title = ""Submit"";
    }
}";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Submit", path, 5, 18));
    }

    [Fact]
    public async Task CodeBlock_LocalizedAssignment_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void Init()
    {
        Title = L[""Key""];
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_LoggingCall_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void DoWork()
    {
        _logger.LogInformation(""Processing started"");
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_FilePath_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void DoWork()
    {
        var path = ""/api/sessions"";
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_NameOf_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void DoWork()
    {
        var name = nameof(Property);
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_NonLocalizableClass_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void DoWork()
    {
        Class = ""my-class"";
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_PascalCaseSingleWord_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void DoWork()
    {
        Title = ""SessionId"";
    }
}";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Phase 3B: .razor.cs code-behind file analysis
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorCs_HardcodedTitleAssignment_ReportsNNB014()
    {
        var csharp = @"using Microsoft.AspNetCore.Components;

namespace TestProject.Components;

public partial class TestComponent
{
    private void Init()
    {
        Title = ""Submit Form"";
    }
}";
        var path = "/TestProject/Components/TestComponent.razor.cs";

        await VerifyRazorCsAsync(csharp, path,
            Diagnostic("Submit Form", path, 9, 18));
    }

    [Fact]
    public async Task RazorCs_NonLocalizableAssignment_NoWarning()
    {
        var csharp = @"namespace TestProject.Components;

public partial class TestComponent
{
    private void Init()
    {
        var path = ""/api/sessions"";
        _logger.LogInformation(""Starting"");
        Class = ""my-class"";
    }
}";

        await VerifyRazorCsAsync(csharp);
    }

    [Fact]
    public async Task RazorCs_LocalizedAssignment_NoWarning()
    {
        var csharp = @"namespace TestProject.Components;

public partial class TestComponent
{
    private void Init()
    {
        Title = L[""Key""];
    }
}";

        await VerifyRazorCsAsync(csharp);
    }

    [Fact]
    public async Task CodeBlock_InterpolatedString_ReportsNNB014()
    {
        var razor = @"<div></div>
@code {
    private void Init()
    {
        Title = $""Hello {name}"";
    }
}";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Hello {name}", path, 5, 19));
    }

    [Fact]
    public async Task CodeBlock_CaseLabel_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void Process(string status)
    {
        switch (status)
        {
            case ""active"":
                break;
        }
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_ConsoleWrite_NoWarning()
    {
        var razor = @"<div></div>
@code {
    private void Debug()
    {
        Console.WriteLine(""debug output"");
    }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task CodeBlock_CSharpComment_NoWarning()
    {
        var razor = @"<div></div>
@code {
    // Title = ""This is a comment""
    /* Label = ""Also a comment"" */
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task RazorCs_MultipleFindings_ReportsAll()
    {
        var csharp = @"namespace TestProject.Components;

public partial class TestComponent
{
    private void Init()
    {
        Title = ""Welcome Back"";
        Label = ""Enter Name"";
    }
}";
        var path = "/TestProject/Components/TestComponent.razor.cs";

        await VerifyRazorCsAsync(csharp, path,
            Diagnostic("Welcome Back", path, 7, 18),
            Diagnostic("Enter Name", path, 8, 18));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: HelperText and Description attributes → NNB014
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HelperTextAttribute_ReportsNNB014()
    {
        var razor = @"<MudTextField HelperText=""Enter a valid email"" />";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Enter a valid email", path, 1, 27));
    }

    [Fact]
    public async Task DescriptionAttribute_ReportsNNB014()
    {
        var razor = @"<MudField Description=""This field is required"" />";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("This field is required", path, 1, 24));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Attribute with Razor binding → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AttributeWithRazorBinding_NoWarning()
    {
        var razor = @"<MudTextField Label=""@myLabel"" Title=""@title"" />";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // H1: Braces inside string literal in @code block should not corrupt depth
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlock_BracesInsideStringLiteral_DoesNotCorruptDepth()
    {
        // The string literal contains braces — they should NOT affect brace depth tracking.
        // After the @code block closes, the <p> text node should be detected as markup.
        var razor = @"<div></div>
@code {
    private string GetJson()
    {
        var json = ""{ \""key\"": \""value\"" }"";
        Title = ""Submit"";
        return json;
    }
}
<p>Hardcoded after code</p>";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Submit", path, 6, 18),
            Diagnostic("Hardcoded after code", path, 10, 4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // H2: Escaped quotes in Razor expression should not break SkipBalanced
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RazorExpression_EscapedQuotes_NoWarning()
    {
        // The string inside @( ) contains escaped quotes — SkipBalanced should not
        // terminate early and expose interior text as a false positive.
        var razor = @"<span>@(""He said \""hello\"""")</span>";

        await VerifyRazorAsync(razor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // M3: Line with both logging and localizable assignment → only flag the assignment
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlock_LoggingAndLocalizableOnSameLine_ReportsOnlyAssignment()
    {
        // Two statements on one line: logging call + localizable assignment.
        // The logging call should NOT cause the localizable assignment to be skipped.
        var razor = @"<div></div>
@code {
    private void Init()
    {
        _logger.LogInformation(""Starting""); Title = ""Submit"";
    }
}";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Submit", path, 5, 54));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // M4: Multi-line block comment containing localizable pattern → no warning
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CodeBlock_MultiLineBlockComment_NoWarning()
    {
        // A multi-line block comment where middle lines do not start with *.
        // The localizable pattern inside the comment should NOT produce a warning.
        var razor = @"<div></div>
@code {
    /*
    Title = ""Should not flag this""
    Label = ""Also not flagged""
    */
    private void DoWork() { }
}";

        await VerifyRazorAsync(razor);
    }

    [Fact]
    public async Task RazorCs_MultiLineBlockComment_NoWarning()
    {
        // Same test for .razor.cs code-behind files.
        var csharp = @"namespace TestProject.Components;

public partial class TestComponent
{
    /*
    Title = ""Should not flag this""
    Label = ""Also not flagged""
    */
    private void DoWork() { }
}";

        await VerifyRazorCsAsync(csharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Opt-in contract: parse rule for LocalizationAnalyzerEnabled
    // ═══════════════════════════════════════════════════════════════════════

    private const string ContractProbeRazor = @"<h1>Welcome to our app</h1>";
    private const string ContractProbePath = "/TestProject/Components/TestComponent.razor";

    [Fact]
    public async Task ContractAbsent_NoWarning()
    {
        var test = CreateRazorTest(declareContract: false);
        test.TestState.AdditionalFiles.Add((ContractProbePath, ContractProbeRazor));

        await test.RunAsync();
    }

    [Fact]
    public async Task ContractEmptyValue_NoWarning()
    {
        // MSBuild writes an empty value for an undefined compiler-visible property.
        var test = CreateRazorTestWithGlobalConfig(@"
is_global = true
build_property.LocalizationAnalyzerEnabled =
");
        test.TestState.AdditionalFiles.Add((ContractProbePath, ContractProbeRazor));

        await test.RunAsync();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public async Task ContractTrue_ReportsNNB014(string value)
    {
        var test = CreateRazorTestWithGlobalConfig($@"
is_global = true
build_property.LocalizationAnalyzerEnabled = {value}
");
        test.TestState.AdditionalFiles.Add((ContractProbePath, ContractProbeRazor));
        test.ExpectedDiagnostics.Add(Diagnostic("Welcome to our app", ContractProbePath, 1, 5));

        await test.RunAsync();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    public async Task ContractFalse_NoWarning(string value)
    {
        var test = CreateRazorTestWithGlobalConfig($@"
is_global = true
build_property.LocalizationAnalyzerEnabled = {value}
");
        test.TestState.AdditionalFiles.Add((ContractProbePath, ContractProbeRazor));

        await test.RunAsync();
    }

    [Fact]
    public async Task ContractInvalidValue_ReportsNNB014()
    {
        // Any non-empty value other than false keeps the analyzer on.
        var test = CreateRazorTestWithGlobalConfig(@"
is_global = true
build_property.LocalizationAnalyzerEnabled = yes
");
        test.TestState.AdditionalFiles.Add((ContractProbePath, ContractProbeRazor));
        test.ExpectedDiagnostics.Add(Diagnostic("Welcome to our app", ContractProbePath, 1, 5));

        await test.RunAsync();
    }

    [Fact]
    public async Task PerFileFalse_UnderGlobalTrue_NoWarning()
    {
        var path = "/TestProject/Excluded.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((path, ContractProbeRazor));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Excluded.razor]
{PerFileEnabledOption} = false
"));

        await test.RunAsync();
    }

    [Fact]
    public async Task PerFileEmpty_UnderGlobalTrue_ReportsNNB014()
    {
        // An empty per-file value is "not set" and falls through to the global declaration.
        var path = "/TestProject/Inherits.razor";
        var test = CreateRazorTest();

        test.TestState.AdditionalFiles.Add((path, ContractProbeRazor));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Inherits.razor]
{PerFileEnabledOption} =
"));
        test.ExpectedDiagnostics.Add(Diagnostic("Welcome to our app", path, 1, 5));

        await test.RunAsync();
    }

    [Fact]
    public async Task GlobProbePerFileTrue_WithoutGlobalContract_ReportsNNB014()
    {
        // The double-star glob only matches through the synthetic probe directory; a
        // per-file true reached that way enables the analyzer with no global declaration.
        var path = "/TestProject/Components/Pages/Localized/Page.razor";
        var test = CreateRazorTest(declareContract: false);

        test.TestState.AdditionalFiles.Add((path, ContractProbeRazor));
        test.TestState.AnalyzerConfigFiles.Add(("/TestProject/.editorconfig", $@"
root = true

[Components/Pages/Localized/**/*.razor]
{PerFileEnabledOption} = true
"));
        test.ExpectedDiagnostics.Add(Diagnostic("Welcome to our app", path, 1, 5));

        await test.RunAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // <script> bodies and @{ } statement blocks
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] AppRazorShapeLines =
    {
        "@inject ILogger<App> Logger",
        "<head>",
        "    <script>",
        "    window.reportCssLoadError = function(el) {",
        "        var msg = '[CSS] Failed to load stylesheet: ' + href + '. Scoped component styles may be missing.';",
        "        console.error(msg);",
        "    };",
        "    </script>",
        "    <script>",
        "    (function () {",
        "        try {",
        "            if (m === \"dark\") {",
        "                isDark = true;",
        "            } else if (m === \"light\") {",
        "                isDark = false;",
        "            } else {",
        "                isDark = !!(window.matchMedia &&",
        "                    window.matchMedia(\"(prefers-color-scheme: dark)\").matches);",
        "            }",
        "        } catch (e) {",
        "            // Fallback: leave default (no class, no inline color-scheme).",
        "        }",
        "    })();",
        "    </script>",
        "    @{",
        "        string cssHref;",
        "        try",
        "        {",
        "            cssHref = Assets[$\"{CssBundleAssemblyName}.styles.css\"];",
        "        }",
        "        catch (Exception ex)",
        "        {",
        "            cssHref = $\"{CssBundleAssemblyName}.styles.css\";",
        "            Logger.LogError(ex, \"CSS isolation bundle '{Bundle}' not found in static asset manifest. \" +",
        "                \"AssemblyName='{AssemblyName}'. Falling back to literal path (will likely 404).\",",
        "                $\"{CssBundleAssemblyName}.styles.css\", CssBundleAssemblyName);",
        "        }",
        "    }",
        "    <link rel=\"stylesheet\" href=\"@cssHref\" onerror=\"reportCssLoadError(this)\" />",
        "    <script src=\"_content/MudBlazor/MudBlazor.min.js\"></script>",
        "    <script type=\"module\" src=\"_content/NotNot.BlazorDesign/nn-ellipsis-title.js\"></script>",
        "</head>",
    };

    [Fact]
    public async Task AppRazorShape_ScriptsAndStatementBlock_NoWarning()
    {
        await VerifyRazorAsync(string.Join("\n", AppRazorShapeLines));
    }

    [Fact]
    public async Task AppRazorShape_AdjacentMarkupText_ReportsOnlyMarkup()
    {
        // Real markup text next to the script and statement blocks stays detected.
        var lines = new List<string>(AppRazorShapeLines);
        lines.Insert(24, "    <p>After the scripts</p>");   // line 25, before @{
        lines.Insert(39, "    <p>After the statement block</p>"); // line 40, after the closing }
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(string.Join("\n", lines), path,
            Diagnostic("After the scripts", path, 25, 8),
            Diagnostic("After the statement block", path, 40, 8));
    }

    [Fact]
    public async Task StatementBlock_EmbeddedMarkupText_ReportsNNB014()
    {
        // SampleNnScrollAnchor.razor shape: a RenderFragment declared inside @{ } whose
        // markup text is user-facing and must stay detected.
        var razor = string.Join("\n", new[]
        {
            "@{",
            "    // Shared row list declared in the render body.",
            "    RenderFragment demoLines =",
            "        @<text>",
            "            @foreach (var line in _lines)",
            "            {",
            "                <div style=\"padding: 3px 10px;\">Hello world</div>",
            "            }",
            "        </text>;",
            "}",
        });
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Hello world", path, 7, 49));
    }

    [Fact]
    public async Task SameLineScript_TrailingTextOnly_ReportsNNB014()
    {
        var razor = "<script>var greeting = 'Hello there';</script> Visible text";
        var path = "/TestProject/Components/TestComponent.razor";

        await VerifyRazorAsync(razor, path,
            Diagnostic("Visible text", path, 1, 47));
    }
}
