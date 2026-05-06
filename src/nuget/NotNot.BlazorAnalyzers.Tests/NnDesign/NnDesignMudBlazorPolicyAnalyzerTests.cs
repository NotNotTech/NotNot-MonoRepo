using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests for <see cref="NnDesignMudBlazorPolicyAnalyzer"/> (NNB022).
/// Verifies the hybrid analyzer's three pathways:
/// <list type="bullet">
///   <item>Razor markup text-scan via AdditionalFiles (.razor)</item>
///   <item>C# code-behind text-scan via AdditionalFiles (.razor.cs)</item>
///   <item>Plain .cs semantic detection via Roslyn SymbolInfo (NEW in v2)</item>
/// </list>
/// All 4 exception buckets (NotNot.BlazorDesign internals; NnDesignSamples folder; root provider
/// wiring; explicit dev/test/legacy harness page allow-list) are covered with negative tests.
/// </summary>
public class NnDesignMudBlazorPolicyAnalyzerTests
{
	// ── MudBlazor namespace stub (required for semantic-pathway symbol resolution) ──

	private const string MudBlazorStubs = @"
namespace MudBlazor
{
    public class MudCard { }
    public class MudText { }
    public class MudButton { }
    public class MudIconButton { }
    public class MudIconButtonGroup { }
    public class MudThemeProvider { }
    public class MudPopoverProvider { }
    public class MudDialogProvider { }
    public class MudMenu { }
    public class MudMenuItem { }
    public class MudAppBar { }

    public struct MudColor
    {
        public MudColor(string hex) { }
    }

    public enum Color
    {
        Default,
        Primary,
        Secondary,
    }

    public interface IMudPopover { }
}

namespace MudBlazor.Services
{
    public static class MudServicesExtensions
    {
        public static System.IServiceProvider AddMudServices(this System.IServiceProvider sp) => sp;
    }
}
";

	// ── Test helpers ────────────────────────────────────────────────────

	/// <summary>
	/// Creates a test with the given <c>.razor</c> content as an AdditionalFile and runs it.
	/// </summary>
	private static async Task VerifyRazorAsync(
		string razorContent,
		string razorFilePath,
		params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NnDesignMudBlazorPolicyAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		test.TestState.AdditionalFiles.Add((razorFilePath, razorContent));
		if (expected?.Length > 0)
			test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	/// <summary>
	/// Creates a test with the given <c>.razor.cs</c> content as an AdditionalFile and runs it.
	/// </summary>
	private static async Task VerifyRazorCsAsync(
		string csharpContent,
		string razorCsFilePath,
		params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NnDesignMudBlazorPolicyAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		test.TestState.AdditionalFiles.Add((razorCsFilePath, csharpContent));
		if (expected?.Length > 0)
			test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	/// <summary>
	/// Creates a test for the SEMANTIC pathway — plain <c>.cs</c> source (NOT <c>.razor.cs</c>).
	/// The MudBlazor namespace stub is added so symbol resolution can identify <c>MudBlazor</c> types.
	/// </summary>
	private static async Task VerifyPlainCsAsync(
		string csharpContent,
		string csFilePath,
		params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NnDesignMudBlazorPolicyAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		// Add primary source under test at the desired path (so SyntaxTree.FilePath matches)
		test.TestState.Sources.Add((csFilePath, csharpContent));
		// Add MudBlazor stub
		test.TestState.Sources.Add(MudBlazorStubs);
		if (expected?.Length > 0)
			test.ExpectedDiagnostics.AddRange(expected);
		await test.RunAsync();
	}

	/// <summary>
	/// Creates a test with the build-property opt-out (<c>NnDesignPolicyAnalyzerEnabled=false</c>).
	/// </summary>
	private static async Task VerifyOptedOutAsync(string razorContent, string razorFilePath)
	{
		var test = new CSharpAnalyzerTest<NnDesignMudBlazorPolicyAnalyzer, DefaultVerifier>
		{
			TestCode = "class Placeholder { }"
		};
		test.TestState.AdditionalFiles.Add((razorFilePath, razorContent));
		test.TestState.AnalyzerConfigFiles.Add(
			("/.globalconfig", @"
is_global = true
build_property.NnDesignPolicyAnalyzerEnabled = false
"));
		// Expect NO diagnostics
		await test.RunAsync();
	}

	private static DiagnosticResult Diagnostic(string typeName, string filePath, int line, int column, int endLine, int endColumn)
	{
		return new DiagnosticResult(NnDesignMudBlazorPolicyAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
			.WithSpan(filePath, line, column, endLine, endColumn)
			.WithArguments(typeName);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T2 — RUNS FIRST per SME F2: verifies .razor.cs AdditionalFile framework support
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T2_RazorCsMudColor_ReportsNNB022()
	{
		var csharp = @"using MudBlazor;
namespace TestProject.Components;
public partial class TestComponent
{
    private MudColor _color = new(""#fff"");
}
";
		var path = "/TestProject/Components/TestComponent.razor.cs";
		await VerifyRazorCsAsync(csharp, path,
			// using MudBlazor;
			Diagnostic("using MudBlazor", path, 1, 1, 1, 17),
			// MudColor (line 5, after "    private ")
			Diagnostic("MudColor", path, 5, 13, 5, 21));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T2-prime — Plain .cs semantic pathway (NEW in v2 per Q1.B)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T2Prime_PlainCsMudColor_ReportsNNB022_Semantic()
	{
		var csharp = @"using MudBlazor;
namespace TestProject.Helpers;
public static class StatusFormatHelper
{
    public static Color GetStatusColor(string status) => Color.Primary;
}
";
		var path = "/TestProject/Helpers/StatusFormatHelper.cs";
		// Semantic pathway:
		//   - using MudBlazor; (UsingDirectiveSyntax) → 1 diagnostic
		//   - Color (return type, line 5) → 1 diagnostic (resolves to MudBlazor.Color via using)
		//   - Color (in Color.Primary expression) → 1 diagnostic
		// Total: 3 diagnostics.
		await VerifyPlainCsAsync(csharp, path,
			// `using MudBlazor;` — full directive span line 1 col 1 → line 1 col 17 (`using MudBlazor;`)
			Diagnostic("using MudBlazor", path, 1, 1, 1, 17),
			// `Color` return type, line 5 col 19 (after "    public static ")
			Diagnostic("Color", path, 5, 19, 5, 24),
			// `Color` in `Color.Primary` expression, line 5
			Diagnostic("Color", path, 5, 58, 5, 63));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T1 — Razor markup detection (covers AC1)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T1_RazorMudCard_ReportsNNB022()
	{
		var razor = @"<MudCard Elevation=""2""><MudText>Hi</MudText></MudCard>";
		var path = "/TestProject/Components/TestComponent.razor";
		// `<MudCard` at col 1 (length 8: `<MudCard`)
		// `<MudText` at col 24 (length 8)
		// Closing tag `</MudCard>` does NOT match (regex requires `<Mud`, not `</Mud`)
		// Closing tag `</MudText>` does NOT match
		await VerifyRazorAsync(razor, path,
			Diagnostic("MudCard", path, 1, 1, 1, 9),
			Diagnostic("MudText", path, 1, 24, 1, 32));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T3 — NnDesignSamples folder exception (covers AC3 + Exception bucket #2)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T3_NnDesignSamplesPath_NoNNB022()
	{
		var razor = @"<MudButton Text=""Sample"" />
<MudCard><MudText>Side-by-side demo</MudText></MudCard>";
		var path = "/TestProject/Components/Pages/NnDesignSamples/SampleFoo.razor";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T4 — NnButton element does not match (covers AC4 — negative control)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T4_NnButtonElement_NoNNB022()
	{
		var razor = @"<NnButton Text=""Submit"" OnClick=""HandleClick"" />";
		var path = "/TestProject/Components/TestComponent.razor";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T5 — Per-file opt-out comment suppresses diagnostics
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T5_OptOutComment_SuppressesNNB022()
	{
		var razor = @"@* nnb022:allow-mudblazor: legitimate transitional retention pending NnMenu wrapper *@
<MudMenu>
    <MudMenuItem>One</MudMenuItem>
</MudMenu>";
		var path = "/TestProject/Components/Layout/MainLayout.razor";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T6 — NotNot.BlazorDesign internals exception (Exception bucket #1)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T6_NotNotBlazorDesignPath_NoNNB022()
	{
		var razor = @"<MudAppBar Class=""nn-page-nav-bar"">
    <MudIconButton Icon=""@Icons.Material.Filled.Menu"" />
</MudAppBar>";
		var path = "/TestProject/NotNot.BlazorDesign/NnDesign/Layout/NnPageNavBar.razor";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T7 — Diagnostic message contains the user-instructed verbatim phrase (AC7)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T7_MessageContainsSmellPhrase_RuntimeVerified()
	{
		// Run the analyzer end-to-end via a manual CSharpCompilation + WithAnalyzers pipeline so we can
		// capture the ACTUAL emitted Diagnostic and inspect its rendered text via GetMessage(). This is
		// stronger than asserting against Rule.MessageFormat directly: it proves the analyzer's
		// ReportDiagnostic call site supplied the correct format-argument AND that the verbatim REQ-2
		// phrase survives the full message-format substitution + Diagnostic.GetMessage() rendering.
		// Per PEER_REVIEW M-3 and Plan §8 — upgrades AC7 evidence from CODE_INSPECTION_ONLY to
		// RUNTIME_VERIFIED.
		var razor = @"<MudCard Elevation=""2"">Hi</MudCard>";
		var path = "/TestProject/Components/TestComponent.razor";

		// Build a minimal in-memory compilation; the analyzer registers a CompilationAction that scans
		// AdditionalFiles, so the source-tree contents are inert here.
		var syntaxTree = CSharpSyntaxTree.ParseText("class Placeholder { }");
		var compilation = CSharpCompilation.Create(
			"T7_RuntimeVerified",
			new[] { syntaxTree },
			new[]
			{
				MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			},
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		// Register the .razor source as an AdditionalFile so the text-scan pathway sees it.
		var additionalFile = new InMemoryAdditionalText(path, razor);
		var analyzerOptions = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(additionalFile));

		var compilationWithAnalyzers = compilation.WithAnalyzers(
			ImmutableArray.Create<DiagnosticAnalyzer>(new NnDesignMudBlazorPolicyAnalyzer()),
			analyzerOptions);

		var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

		var nnb022 = diagnostics
			.Where(d => d.Id == NnDesignMudBlazorPolicyAnalyzer.DiagnosticId)
			.ToList();
		nnb022.Should().NotBeEmpty(
			because: "the test fixture contains <MudCard> outside any exception bucket and must produce NNB022");

		// RUNTIME-RENDERED message — calls Diagnostic.GetMessage() which performs the full MessageFormat
		// argument substitution that end users see in the IDE / build log.
		var renderedMessage = nnb022[0].GetMessage();
		renderedMessage.Should().Contain(
			"End-using applications must not reference MudBlazor directly",
			because: "REQ-2 user-instructed verbatim phrase MUST appear in the rendered diagnostic message");
		renderedMessage.Should().Contain(
			"MudCard",
			because: "the format-argument MUST be substituted into the rendered message");
	}

	/// <summary>In-memory <see cref="AdditionalText"/> for analyzer-pipeline tests that need to drive
	/// the text-scan pathway without going through the analyzer-testing framework.</summary>
	private sealed class InMemoryAdditionalText : AdditionalText
	{
		private readonly SourceText _text;
		public InMemoryAdditionalText(string path, string text)
		{
			Path = path;
			_text = SourceText.From(text);
		}
		public override string Path { get; }
		public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T8 — Build-property opt-out kill-switch
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T8_BuildPropertyOptOut_NoNNB022()
	{
		var razor = @"<MudCard />";
		var path = "/TestProject/Components/TestComponent.razor";
		await VerifyOptedOutAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T9 — Comment ranges skipped (HTML + Razor)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T9_CommentRangeSkip_NoNNB022()
	{
		var razor = @"<!-- <MudButton>HTML comment</MudButton> -->
@* <MudCard>Razor comment</MudCard> *@
<MudCard>This is real</MudCard>";
		var path = "/TestProject/Components/TestComponent.razor";
		// Only the third line's `<MudCard>` should fire.
		await VerifyRazorAsync(razor, path,
			Diagnostic("MudCard", path, 3, 1, 3, 9));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T10 — Word-boundary safety (LOCKED expectation: 3 diagnostics, regex strategy)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T10_WordBoundarySafety_RegexExpectations()
	{
		var razor = @"<MudIconButton />
<MudCard />
<MudIconButtonGroup />";
		var path = "/TestProject/Components/TestComponent.razor";
		// Greedy regex `<Mud[A-Z]\w*` matches the FULL identifier — 3 diagnostics.
		await VerifyRazorAsync(razor, path,
			Diagnostic("MudIconButton", path, 1, 1, 1, 15),
			Diagnostic("MudCard", path, 2, 1, 2, 9),
			Diagnostic("MudIconButtonGroup", path, 3, 1, 3, 20));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T11 — Multi-using fixture: `using MudBlazor;` reports, body MudColor reports.
	// (Removed misleading "no false-positive on System.Linq" claim per PEER_REVIEW M-1: the
	// CsUsingMudBlazorRegex is hard-anchored to `MudBlazor` so System.Linq cannot match by construction;
	// the unrelated `using System.Linq;` line is retained in the fixture as a realistic multi-using
	// scenario and is implicitly covered by the exhaustive ExpectedDiagnostics match.)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T11_MudBlazorUsingDirective_RazorCs_ReportsNNB022()
	{
		var csharp = @"using MudBlazor;
using System.Linq;
namespace TestProject.Components;
public partial class TestComponent
{
    private MudColor _color = new(""#fff"");
}
";
		var path = "/TestProject/Components/TestComponent.razor.cs";
		await VerifyRazorCsAsync(csharp, path,
			Diagnostic("using MudBlazor", path, 1, 1, 1, 17),
			Diagnostic("MudColor", path, 6, 13, 6, 21));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T12 — Bare `using MudBlazor;` in .razor.cs DOES report (CHANGED FROM v1 per Q2.C)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T12_BareUsingMudBlazorWithoutMudReference_StillReports()
	{
		var csharp = @"using MudBlazor;
namespace TestProject.Components;
public partial class TestComponent
{
    // No Mud* references — bare import for transitively-rendered children
}
";
		var path = "/TestProject/Components/TestComponent.razor.cs";
		await VerifyRazorCsAsync(csharp, path,
			Diagnostic("using MudBlazor", path, 1, 1, 1, 17));
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T13 — App.razor root provider wiring exception (Exception bucket #3a)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T13_AppRazorRootBootstrap_NoNNB022()
	{
		var razor = @"<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />";
		var path = "/TestProject/Components/App.razor";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T13b — Program.cs root provider wiring exception (Exception bucket #3b)
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T13b_ProgramCs_NoNNB022()
	{
		var csharp = @"using MudBlazor;
using MudBlazor.Services;
namespace TestProject;
public class Program
{
    public static void Main(string[] args)
    {
        var color = Color.Primary;
    }
}
";
		var path = "/TestProject/Program.cs";
		await VerifyPlainCsAsync(csharp, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T13c — Multi-segment namespace declaration: LEFT segment must NOT fire NNB022.
	// Regression-pin for PEER_REVIEW Fix-1: prior IsDeclarationSite logic walked the qualified-name chain
	// only when the current node was the RIGHT child of QualifiedNameSyntax, so the LEFT segment
	// (`MudBlazor` in `namespace MudBlazor.Services`) failed the declaration-site check and fired NNB022.
	// Post-fix: the loop walks regardless of Left/Right, so both segments correctly anchor to
	// BaseNamespaceDeclarationSyntax.Name and are recognized as declaration sites.
	// ═══════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task T13c_MultiSegmentNamespaceDecl_LeftSegment_NoNNB022()
	{
		// Path is OUTSIDE all exception buckets (no allow-list filename, no NotNot.BlazorDesign/NnDesignSamples
		// folder, not Program.cs / App.razor / VowMudLocalizer.cs). This forces the analyzer to evaluate every
		// IdentifierName via the semantic pathway with NO exemption fast-path.
		var csharp = @"namespace MudBlazor.Subnamespace
{
    public class CustomThing { }
}
";
		var path = "/TestProject/Components/CustomNs.cs";
		// Pre-fix bug would have produced 1 diagnostic on the LEFT `MudBlazor` token (line 1, cols 11-19).
		// Post-fix expectation: ZERO diagnostics — both segments are declaration sites.
		await VerifyPlainCsAsync(csharp, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T14 — Harness file allow-list exception (Exception bucket #4 — EXHAUSTIVE)
	// PARAMETERIZED per PEER_REVIEW H-2: every entry in AllowedFileNames must be exercised so a
	// silent typo on any single entry is caught. Razor entries flow through the text-scan pathway;
	// the two .cs entries (Program.cs, VowMudLocalizer.cs) flow through the semantic pathway and are
	// exercised by T14b below.
	// ═══════════════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("App.razor")]
	[InlineData("BlazorTermTestHarness.Wasm.razor")]
	[InlineData("BlazorTermTestHarness.Server.razor")]
	[InlineData("BlazorTermHarmonizedHarness.Wasm.razor")]
	[InlineData("BlazorTermHarmonizedHarness.Server.razor")]
	[InlineData("BlazorTermDiffTest.razor")]
	[InlineData("BlazorTermTabTest.razor")]
	[InlineData("BlazorTermTest.razor")]
	[InlineData("HarnessDummyTab.razor")]
	[InlineData("HarnessTerminalWrapper.razor")]
	[InlineData("RichEditExamplePage.razor")]
	[InlineData("NnDesignSamplesPage.razor")]
	[InlineData("DashboardLegacy.razor")]
	[InlineData("TestInputs.razor")]
	public async Task T14_HarnessFileAllowList_Razor_NoNNB022(string fileName)
	{
		var razor = @"<MudButton Text=""Harness Test"" />
<MudIconButton Icon=""@Icons.Material.Filled.Refresh"" />";
		// Path layout chosen so file-name allow-list (not folder-pattern) is the gate under test.
		var path = $"/TestProject/Components/Pages/{fileName}";
		await VerifyRazorAsync(razor, path);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// T14b — Plain .cs allow-list entries (Exception bucket #4 — semantic pathway)
	// Exercises the Program.cs and VowMudLocalizer.cs entries. Note: T13b already exercises
	// Program.cs in /TestProject/Program.cs; this theory additionally guards against a silent
	// typo in the VowMudLocalizer.cs entry by running both through the same fixture.
	// ═══════════════════════════════════════════════════════════════════════

	[Theory]
	[InlineData("Program.cs")]
	[InlineData("VowMudLocalizer.cs")]
	public async Task T14b_HarnessFileAllowList_PlainCs_NoNNB022(string fileName)
	{
		var csharp = @"using MudBlazor;
namespace TestProject;
public class Bootstrap
{
    public static MudColor StaticColor = new(""#fff"");
}
";
		var path = $"/TestProject/{fileName}";
		await VerifyPlainCsAsync(csharp, path);
	}
}
