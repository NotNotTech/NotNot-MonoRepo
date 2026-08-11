using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests for <see cref="NnDesignInlineStyleAnalyzer"/> (NNB044) — static-literal inline <c>style=</c>
/// attributes in consumer <c>.razor</c> markup.
/// <para>
/// Discriminating fixtures exercise the VALUE-SHAPE allowlist: a STATIC literal value FIRES (Error); a
/// DYNAMIC <c>@</c>-bound value is CLEAN; the exempt path buckets, the per-file opt-out marker, and the
/// build-property kill-switch all suppress.
/// </para>
/// </summary>
public class NnDesignInlineStyleAnalyzerTests
{
	/// <summary>Runs the analyzer over the given <c>.razor</c> content registered as an AdditionalFile.</summary>
	private static async Task VerifyRazorAsync(
		string razorContent,
		string razorFilePath,
		params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NnDesignInlineStyleAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		test.TestState.AdditionalFiles.Add((razorFilePath, razorContent));
		if (expected?.Length > 0)
			test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	private static DiagnosticResult Diagnostic(
		string displayValue, string filePath, int line, int column, int endLine, int endColumn)
	{
		return new DiagnosticResult(NnDesignInlineStyleAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithSpan(filePath, line, column, endLine, endColumn)
			.WithArguments(displayValue);
	}

	private const string ConsumerPath =
		"/TestProject/Components/Sessions/VowSessionMetaPanel.razor";

	// ═══════════════════════════════════════════════════════════════════════
	// FIRE: a static-literal inline style → Error.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task StaticLiteral_PaddingStyle_Fires()
	{
		// `<div style="padding: 4px 8px">` — the style attr starts at column 6, full match
		// `style="padding: 4px 8px"` is 24 chars → end column 30.
		var razor = @"<div style=""padding: 4px 8px"">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath,
			Diagnostic("padding: 4px 8px", ConsumerPath, 1, 6, 1, 30));
	}

	[Fact]
	public async Task StaticLiteral_DisplayFlex_Fires()
	{
		// `<div style="display:flex">` — style attr at column 6, match `style="display:flex"` is 20
		// chars → end column 26.
		var razor = @"<div style=""display:flex"">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath,
			Diagnostic("display:flex", ConsumerPath, 1, 6, 1, 26));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// CLEAN: a dynamic @-bound value → NO warning (legitimately computed).
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task Dynamic_FullExpressionValue_NoWarning()
	{
		var razor = @"<div style=""@_panelStyle"">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	[Fact]
	public async Task Dynamic_InterpolatedPropertyValue_NoWarning()
	{
		// `style="background:@color"` — has an '@' expression → dynamic → allowed.
		var razor = @"<div style=""background:@color"">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	[Fact]
	public async Task Dynamic_ParenthesizedExpressionWithUnit_NoWarning()
	{
		// A sparkline-height shape: `style="height:@(h)px"` → dynamic → allowed.
		var razor = @"<div style=""height:@(h)px"">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	[Fact]
	public async Task Dynamic_UnquotedDirectiveBinding_NoWarning()
	{
		// `style=@_expr` (no quotes) is a directive binding, dynamic by construction → never fires.
		var razor = @"<div style=@_expr>x</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// ATTRIBUTE-NAME BOUNDARY: a longer attribute ending in "style" must NOT match.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task BodyStyleParam_StaticLiteral_NoWarning()
	{
		// `BodyStyle="padding:0"` is a COMPONENT PARAM (NNB043's concern at the producer), not the raw
		// `style=` attribute — the leading-boundary guard prevents a match on the `Style` suffix.
		var razor = @"<NnContentSection BodyStyle=""padding:0"">x</NnContentSection>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// EMPTY VALUE: an empty style attribute carries no laundered appearance → NO warning.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task EmptyStyleValue_NoWarning()
	{
		var razor = @"<div style="""">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// COMMENT: a static-literal style inside an HTML or Razor comment → NOT flagged.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task StaticLiteralInHtmlComment_NoWarning()
	{
		var razor = "<!-- <div style=\"padding:4px\">x</div> -->\n<div>ok</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	[Fact]
	public async Task StaticLiteralInRazorComment_NoWarning()
	{
		var razor = "@* <div style=\"padding:4px\">x</div> *@\n<div>ok</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// PATH-EXEMPT: producer internals + samples buckets → NO warning.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task PathExempt_ProducerInternals_NoWarning()
	{
		var razor = @"<div style=""padding: 4px 8px"">x</div>";
		var path = "/TestProject/NotNot.BlazorDesign/NnDesign/Layout/NnContentSection.razor";
		await VerifyRazorAsync(razor, path);
	}

	[Fact]
	public async Task PathExempt_DesktopProducerInternals_NoWarning()
	{
		var razor = @"<div style=""position: fixed"">toast</div>";
		var path = "/TestProject/NotNot.BlazorDesign.Desktop/NnDesign/Desktop/EzToast/EzToast.razor";
		await VerifyRazorAsync(razor, path);
	}

	[Fact]
	public async Task PathExempt_NnDesignSamples_NoWarning()
	{
		var razor = @"<div style=""display:flex"">x</div>";
		var path = "/TestProject/NnDesignSamples/SampleNnContentSection.razor";
		await VerifyRazorAsync(razor, path);
	}

	[Fact]
	public async Task PathExempt_PagesSamples_NoWarning()
	{
		var razor = @"<div style=""display:flex"">x</div>";
		var path = "/TestProject/Components/Pages/Samples/StyleDemo.razor";
		await VerifyRazorAsync(razor, path);
	}

	[Fact]
	public async Task PathExempt_SampleProjectConvention_NoWarning()
	{
		// A sample project whose exemption signal is its PROJECT NAME ending in ".Samples"
		// (NotNot.BlazorComponents.Samples), with pages NOT under a /Pages/Samples/ segment.
		var razor = @"<div style=""display:flex"">x</div>";
		var path = "/private-proj/NotNot.BlazorComponents.Samples/Components/Pages/RailwayTest.razor";
		await VerifyRazorAsync(razor, path);
	}

	[Fact]
	public async Task PathExempt_NamedHarnessFile_NoWarning()
	{
		var razor = @"<div style=""display:flex"">x</div>";
		var path = "/TestProject/Components/BlazorTermTest.razor";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// PER-FILE OPT-OUT: nnb044:allow-inline-style comment suppresses the warning.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task PerFileOptOut_SuppressesWarning()
	{
		var razor = "@* nnb044:allow-inline-style: legacy transitional layout pending semantic param *@\n"
			+ @"<div style=""padding: 4px 8px"">x</div>";
		await VerifyRazorAsync(razor, ConsumerPath);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// BUILD-PROPERTY KILL-SWITCH (shared NnDesignPolicyAnalyzerEnabled=false) suppresses.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task BuildPropertyKillSwitch_NoWarning()
	{
		var razor = @"<div style=""padding: 4px 8px"">x</div>";

		var test = new CSharpAnalyzerTest<NnDesignInlineStyleAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		test.TestState.AdditionalFiles.Add((ConsumerPath, razor));
		test.TestState.AnalyzerConfigFiles.Add(
			("/.globalconfig", @"
is_global = true
build_property.NnDesignPolicyAnalyzerEnabled = false
"));
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════
	// MIXED: a static literal and a dynamic value in the same file — only the static fires.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task MixedStaticAndDynamic_OnlyStaticFires()
	{
		// Line 1: static literal (FIRES). Line 2: dynamic @-bound (clean).
		var razor = "<div style=\"padding:4px\">a</div>\n<div style=\"@_dyn\">b</div>";
		// Line 1: `<div ` = 5 chars → `style="padding:4px"` at column 6, length 19 → end column 25.
		await VerifyRazorAsync(razor, ConsumerPath,
			Diagnostic("padding:4px", ConsumerPath, 1, 6, 1, 25));
	}
}
