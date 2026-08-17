using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests NNB050's producer-marker lookup and section-bounded direct-tag scan. The fixtures intentionally
/// keep the Razor AdditionalFiles as text: semantic resolution is limited to the catalog property and
/// its <c>NnSampleModalContentAttribute(Type)</c> relation, while markup recognition stays conservative.
/// </summary>
public class NnModalSampleInliningAnalyzerTests
{
	private const string PrimaryPath = "/TestProject/Components/Pages/NnDesignSamples/Sample.razor";
	private const string CompanionPath = "/TestProject/Components/Pages/NnDesignSamples/SampleDialog.razor";

	private const string CatalogStubs = """
using System;

namespace NotNot.BlazorDesign.NnDesign
{
    public sealed class NnSampleModalContentAttribute : Attribute
    {
        public NnSampleModalContentAttribute(Type subjectType) => SubjectType = subjectType;
        public Type SubjectType { get; }
    }

    public static class NnSampleCatalog
    {
        [NnSampleModalContent(typeof(NnPopupConfirmDialog))]
        public static object PopupConfirmVerification => null!;

        [NnSampleModalContent(typeof(NnDialog))]
        public static object NnDialog => null!;

        [NnSampleModalContent(typeof(NnColorPicker))]
        public static object NnColorPicker => null!;

        [NnSampleModalContent(typeof(NnRichTextTemplateEditor))]
        public static object NnRichTextTemplateEditor => null!;

        public static object NnSaveState => null!;
        public static object PopupFormDialog => null!;
        public static object ServerOsFilePicker => null!;
        public static object PopupCallout => null!;
    }

    public sealed class NnPopupConfirmDialog { }
    public sealed class NnPopupConfirmButton { }
    public sealed class NnDialog { }
    public sealed class NnColorPicker { }
    public sealed class NnRichTextTemplateEditor { }
    public sealed class NnPopupFormDialogBase { }
    public sealed class NnServerOsFilePicker { }
    public sealed class NnPopupCallout { }
    public sealed class NnButton { }
}
""";

	private const string BypassSource = """
using System;

[assembly: TestBypass.NnDesignBypass]

namespace TestBypass
{
    public sealed class NnDesignBypassAttribute : Attribute { }
}
""";

	private static CSharpAnalyzerTest<NnModalSampleInliningAnalyzer, DefaultVerifier> CreateTest(
		params (string Path, string Content)[] files)
	{
		var test = new CSharpAnalyzerTest<NnModalSampleInliningAnalyzer, DefaultVerifier>
		{
			TestCode = CatalogStubs
		};

		foreach (var (path, content) in files)
			test.TestState.AdditionalFiles.Add((path, content));

		return test;
	}

	private static DiagnosticResult Diagnostic(
		string sample,
		string subject,
		string path,
		string content,
		string openingTag)
	{
		var position = content.IndexOf(openingTag, StringComparison.Ordinal);
		Assert.True(position >= 0, $"Fixture opening tag was not found: {openingTag}");

		var sourceText = SourceText.From(content);
		var start = sourceText.Lines.GetLinePosition(position);
		var end = sourceText.Lines.GetLinePosition(position + openingTag.Length);
		return new DiagnosticResult(NnModalSampleInliningAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithSpan(path, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1)
			.WithArguments(sample, subject);
	}

	[Fact]
	public async Task EachMarkedRelation_FiresAtExactSubjectOpeningTag()
	{
		var razor = """
<NnSampleSection Entry="@NnSampleCatalog.PopupConfirmVerification">
    <NnPopupConfirmDialog />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnDialog">
    <NnDialog />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <NnColorPicker Label="Pick" />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnRichTextTemplateEditor">
    <NnRichTextTemplateEditor @bind-Value="value" />
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		test.ExpectedDiagnostics.AddRange(
			Diagnostic("PopupConfirmVerification", "NnPopupConfirmDialog", PrimaryPath, razor, "<NnPopupConfirmDialog />"),
			Diagnostic("NnDialog", "NnDialog", PrimaryPath, razor, "<NnDialog />"),
			Diagnostic("NnColorPicker", "NnColorPicker", PrimaryPath, razor, "<NnColorPicker Label=\"Pick\" />"),
			Diagnostic("NnRichTextTemplateEditor", "NnRichTextTemplateEditor", PrimaryPath, razor, "<NnRichTextTemplateEditor @bind-Value=\"value\" />"));

		await test.RunAsync();
	}

	[Fact]
	public async Task MarkedSubject_InsideRazorIf_FiresAtSubjectTag()
	{
		var razor = """
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    @if (true)
    {
        <NnColorPicker />
    }
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		test.ExpectedDiagnostics.Add(
			Diagnostic("NnColorPicker", "NnColorPicker", PrimaryPath, razor, "<NnColorPicker />"));
		await test.RunAsync();
	}

	[Fact]
	public async Task MarkedGoodPrimaryShapes_AndCompanionFiles_AreSilent()
	{
		var primary = """
<NnSampleSection Entry="@NnSampleCatalog.NnDialog">
    <button @onclick="OpenDialog">Open dialog</button>
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.PopupConfirmVerification">
    <NnPopupConfirmButton />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <button @onclick="OpenPicker">Edit color</button>
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnRichTextTemplateEditor">
    <code>committed value</code>
    <button @onclick="OpenEditor">Edit</button>
</NnSampleSection>
""";
		var companion = """
<NnDialog />
<NnPopupConfirmDialog />
<NnColorPicker />
<NnRichTextTemplateEditor />
""";

		var test = CreateTest((PrimaryPath, primary), (CompanionPath, companion));
		await test.RunAsync();
	}

	[Fact]
	public async Task UnmarkedCrossCuttingAndSelfModalizingSamples_AreSilent()
	{
		var razor = """
<NnSampleSection Entry="@NnSampleCatalog.NnSaveState">
    <NnColorPicker />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.PopupFormDialog">
    <NnPopupFormDialogBase />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.ServerOsFilePicker">
    <NnServerOsFilePicker />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.PopupCallout">
    <NnPopupCallout />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnSaveState">
    <NnButton>Save</NnButton>
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		await test.RunAsync();
	}

	[Fact]
	public async Task CommentsAndDocumentationOutsideSection_AreSilent()
	{
		var razor = """
@code {
    private const string Documentation = "<NnColorPicker />";
}
<!-- <NnColorPicker /> -->
@* <NnColorPicker /> *@
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <MudText>Use <code>&lt;NnColorPicker&gt;</code> from a companion dialog.</MudText>
    <!-- <NnColorPicker /> -->
    @* <NnColorPicker /> *@
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		await test.RunAsync();
	}

	[Fact]
	public async Task MultipleSections_OnlyMatchingMarkedSectionIsScanned()
	{
		var razor = """
<NnSampleSection Entry="@NnSampleCatalog.NnSaveState">
    <NnColorPicker />
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <MudText>Committed color and Edit trigger</MudText>
</NnSampleSection>
<NnSampleSection Entry="@NnSampleCatalog.NnDialog">
    <NnButton>Open</NnButton>
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		await test.RunAsync();
	}

	[Fact]
	public async Task BuildPropertyKillSwitch_SuppressesDiagnostics()
	{
		var razor = """
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <NnColorPicker />
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", """
is_global = true
build_property.NnDesignPolicyAnalyzerEnabled = false
"""));
		await test.RunAsync();
	}

	[Fact]
	public async Task AssemblyBypass_SuppressesDiagnostics()
	{
		var razor = """
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <NnColorPicker />
</NnSampleSection>
""";

		var test = CreateTest((PrimaryPath, razor));
		test.TestState.Sources.Add(BypassSource);
		await test.RunAsync();
	}

	[Fact]
	public void Descriptor_UsesRequiredSeverityMessageAndHelpLink()
	{
		Assert.Equal("NNB050", NnModalSampleInliningAnalyzer.DiagnosticId);
		Assert.Equal(DiagnosticSeverity.Error, NnModalSampleInliningAnalyzer.Rule.DefaultSeverity);
		Assert.Equal(
			"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#nnb050",
			NnModalSampleInliningAnalyzer.Rule.HelpLinkUri);
		Assert.Contains("1) Show the committed value plus a trigger", NnModalSampleInliningAnalyzer.Rule.MessageFormat.ToString());
		Assert.Contains("3) For an intentional exception", NnModalSampleInliningAnalyzer.Rule.MessageFormat.ToString());
		Assert.Contains("does not prove that an arbitrary trigger opens a dialog", NnModalSampleInliningAnalyzer.Rule.Description.ToString());
	}
}
