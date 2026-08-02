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
        var test = new CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier>
        {
            // Minimal C# source — analyzer doesn't inspect C# trees
            TestCode = "class Placeholder { }"
        };

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
        var test = new CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };

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
        var test = new CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };

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

        await VerifyRazorAsync(razor, path);
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
        var test = new CSharpAnalyzerTest<LocalizationAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
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
}
