using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests for <see cref="NnSampleSectionIdUniquenessAnalyzer"/> (NNB047) — duplicate
/// <c>&lt;NnSampleSection Id="..."&gt;</c> static-literal Id across the compilation.
/// <para>
/// Fires (Error) when two sections share an Id (single-file OR cross-file, including multi-line tags);
/// silent on distinct ids, dynamic <c>Id="@expr"</c>, commented duplicates, and non-NnSampleSection
/// elements. Unlike NNB044, NNB047 does NOT exempt the samples path buckets — it keys on the component.
/// </para>
/// </summary>
public class NnSampleSectionIdUniquenessAnalyzerTests
{
	// A real samples path (NNB044 would EXEMPT this bucket; NNB047 does not).
	private const string PathA = "/TestProject/Components/Pages/NnDesignSamples/SampleA.razor";
	private const string PathB = "/TestProject/Components/Pages/NnDesignSamples/SampleB.razor";

	// Column of the `Id="dup"` attribute in `<NnSampleSection Id="dup">…`.
	private const int IdCol = 18;                 // 1-based: after "<NnSampleSection " (17 chars)
	private const int IdEndCol = IdCol + 8;       // `Id="dup"` is 8 chars

	private static DiagnosticResult Dup(string id, int count, string path, int line, int col, int endCol)
		=> new DiagnosticResult(NnSampleSectionIdUniquenessAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithSpan(path, line, col, line, endCol)
			.WithArguments(id, count);

	private static async Task VerifyAsync((string Path, string Content)[] files, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NnSampleSectionIdUniquenessAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		foreach (var (path, content) in files)
			test.TestState.AdditionalFiles.Add((path, content));
		if (expected?.Length > 0)
			test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════
	// FIRE: duplicate Id → Error on every occurrence.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task DuplicateId_SameFile_FiresOnBoth()
	{
		var razor =
			"<NnSampleSection Id=\"dup\">a</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"dup\">b</NnSampleSection>";
		await VerifyAsync(new[] { (PathA, razor) },
			Dup("dup", 2, PathA, 1, IdCol, IdEndCol),
			Dup("dup", 2, PathA, 2, IdCol, IdEndCol));
	}

	[Fact]
	public async Task DuplicateId_AcrossFiles_FiresInBoth()
	{
		// The real-world scenario: the originating defect spanned TWO sample files.
		var razorA = "<NnSampleSection Id=\"dup\">a</NnSampleSection>";
		var razorB = "<NnSampleSection Id=\"dup\">b</NnSampleSection>";
		await VerifyAsync(new[] { (PathA, razorA), (PathB, razorB) },
			Dup("dup", 2, PathA, 1, IdCol, IdEndCol),
			Dup("dup", 2, PathB, 1, IdCol, IdEndCol));
	}

	[Fact]
	public async Task DuplicateId_MultiLineTag_FiresOnBoth()
	{
		// Id on line 1, XRaySource on line 2, '>' on line 2 — proves the multi-line tag-span scan
		// ([^>]*? spans newlines but never crosses '>').
		var razorA = "<NnSampleSection Id=\"dup\"\n                 XRaySource=\"@X()\">a</NnSampleSection>";
		var razorB = "<NnSampleSection Id=\"dup\"\n                 XRaySource=\"@X()\">b</NnSampleSection>";
		await VerifyAsync(new[] { (PathA, razorA), (PathB, razorB) },
			Dup("dup", 2, PathA, 1, IdCol, IdEndCol),
			Dup("dup", 2, PathB, 1, IdCol, IdEndCol));
	}

	[Fact]
	public async Task NnDesignSamplesPagePath_NotExempt_Fires()
	{
		// NNB044 file-name-exempts NnDesignSamplesPage.razor; NNB047 does NOT — it keys on the component,
		// not on a consumer path. This pins the exemption-drop against future template copy-paste.
		var path = "/TestProject/Components/Pages/NnDesignSamplesPage.razor";
		var razor =
			"<NnSampleSection Id=\"dup\">a</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"dup\">b</NnSampleSection>";
		await VerifyAsync(new[] { (path, razor) },
			Dup("dup", 2, path, 1, IdCol, IdEndCol),
			Dup("dup", 2, path, 2, IdCol, IdEndCol));
	}

	[Fact]
	public async Task TripleDuplicate_ReportsCountThree_OnEach()
	{
		var razor =
			"<NnSampleSection Id=\"dup\">a</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"dup\">b</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"dup\">c</NnSampleSection>";
		await VerifyAsync(new[] { (PathA, razor) },
			Dup("dup", 3, PathA, 1, IdCol, IdEndCol),
			Dup("dup", 3, PathA, 2, IdCol, IdEndCol),
			Dup("dup", 3, PathA, 3, IdCol, IdEndCol));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// CLEAN: distinct ids, dynamic ids, comments, non-NnSampleSection.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task DistinctIds_NoWarning()
	{
		var razor =
			"<NnSampleSection Id=\"a\">x</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"b\">y</NnSampleSection>";
		await VerifyAsync(new[] { (PathA, razor) });
	}

	[Fact]
	public async Task DistinctIds_AcrossFiles_NoWarning()
	{
		await VerifyAsync(new[]
		{
			(PathA, "<NnSampleSection Id=\"a\">x</NnSampleSection>"),
			(PathB, "<NnSampleSection Id=\"b\">y</NnSampleSection>"),
		});
	}

	[Fact]
	public async Task DynamicId_Duplicate_NoWarning()
	{
		// Id="@x" is not a static literal → not statically comparable → skipped.
		var razor =
			"<NnSampleSection Id=\"@x\">a</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"@x\">b</NnSampleSection>";
		await VerifyAsync(new[] { (PathA, razor) });
	}

	[Fact]
	public async Task DuplicateId_OneInsideHtmlComment_NoWarning()
	{
		// One live + one inside an HTML comment → count 1 → no report.
		var razor =
			"<NnSampleSection Id=\"dup\">a</NnSampleSection>\n"
			+ "<!-- <NnSampleSection Id=\"dup\">b</NnSampleSection> -->";
		await VerifyAsync(new[] { (PathA, razor) });
	}

	[Fact]
	public async Task DuplicateId_OneInsideRazorComment_NoWarning()
	{
		var razor =
			"<NnSampleSection Id=\"dup\">a</NnSampleSection>\n"
			+ "@* <NnSampleSection Id=\"dup\">b</NnSampleSection> *@";
		await VerifyAsync(new[] { (PathA, razor) });
	}

	[Fact]
	public async Task DuplicateIdOnNonSampleSection_NoWarning()
	{
		// Component-keyed scope: plain elements with duplicate ids are out of scope.
		var razor = "<div id=\"dup\">a</div>\n<div id=\"dup\">b</div>";
		await VerifyAsync(new[] { (PathA, razor) });
	}

	[Fact]
	public async Task SingleOccurrence_NoWarning()
	{
		await VerifyAsync(new[] { (PathA, "<NnSampleSection Id=\"only\">x</NnSampleSection>") });
	}

	// ═══════════════════════════════════════════════════════════════════════
	// ESCAPE HATCHES: assembly bypass + build-property kill-switch suppress.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task BuildPropertyKillSwitch_NoWarning()
	{
		var razor =
			"<NnSampleSection Id=\"dup\">a</NnSampleSection>\n"
			+ "<NnSampleSection Id=\"dup\">b</NnSampleSection>";

		var test = new CSharpAnalyzerTest<NnSampleSectionIdUniquenessAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		test.TestState.AdditionalFiles.Add((PathA, razor));
		test.TestState.AnalyzerConfigFiles.Add(
			("/.globalconfig", @"
is_global = true
build_property.NnDesignPolicyAnalyzerEnabled = false
"));
		await test.RunAsync();
	}
}
