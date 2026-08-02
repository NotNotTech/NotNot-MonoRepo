using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.RenderMode;

namespace NotNot.BlazorAnalyzers.Tests.RenderMode;

/// <summary>
/// Tests for <see cref="NnRmRenderModeEnforcementAnalyzer"/> (NN_RM_001). Verifies the
/// inverted semantic introduced after the M1d Pattern 2 reflection architectural correction:
/// any per-page or per-layout <c>@rendermode</c> directive in non-exempt <c>.razor</c> files
/// fires the rule; absence of the directive (Pattern 2 fallback applies) does NOT fire.
/// Mirrors the <see cref="LiteDDD.LdddAssemblyFenceAnalyzerTests"/> framework setup but is
/// simpler — the analyzer's only detection pathway is AdditionalFiles text-scan, so most
/// fixtures use <see cref="SolutionState.AdditionalFiles"/> with a placeholder
/// <see cref="AnalyzerTest{TVerifier}.TestCode"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Path convention</b>: tests anchor the offending file at a path containing the
/// <c>Novaleaf.VibeOverwatch.Shared/Components/Pages/</c> segment to mirror the VOW layout.
/// The analyzer's detection does NOT require this path prefix — the only path-aware logic
/// is the negative-side path-exemption for <c>Pages/Samples/</c> and
/// <c>Pages/NnDesignSamples/</c>. The path prefix is realistic-fixture style, not
/// load-bearing for the positive case.
/// </para>
/// <para>
/// <b>Diagnostic anchor</b>: the analyzer reports at the precise line and column of the
/// offending <c>@rendermode</c> directive (computed via
/// <see cref="Microsoft.CodeAnalysis.Text.TextLineCollection.GetLinePosition(int)"/>).
/// The <c>WithSpan</c> assertion in tests matches the directive's line/column exactly.
/// </para>
/// </remarks>
public class NnRmRenderModeEnforcementAnalyzerTests
{
	// ── Test infrastructure ─────────────────────────────────────────────────────

	/// <summary>
	/// Trivial placeholder used as <see cref="AnalyzerTest{TVerifier}.TestCode"/> when the
	/// real subject under analysis is a <see cref="SolutionState.AdditionalFiles"/> entry
	/// (the analyzer scans AdditionalFiles, not the C# TestCode).
	/// </summary>
	private const string Placeholder = "class Placeholder { }";

	/// <summary>
	/// Canonical WASM render-mode directive — the prior-canonical literal text used as a
	/// representative directive value. Under the inverted semantic, ANY <c>@rendermode</c>
	/// directive value fires the rule, so the specific value here is not load-bearing.
	/// </summary>
	private const string CanonicalDirective =
		"@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: true))";

	/// <summary>
	/// Stub for the canonical <see cref="NnRmBypassAttribute"/>. The analyzer matches by
	/// simple name OR fully-qualified name, so consumer assemblies may declare the
	/// attribute locally — but the canonical
	/// <c>NotNot.BlazorAnalyzers.RenderMode</c> location is used here to exercise the
	/// fully-qualified match path.
	/// </summary>
	private const string BypassAttributeStub = @"
namespace NotNot.BlazorAnalyzers.RenderMode
{
    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class NnRmBypassAttribute : System.Attribute { }
}
";

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-1: Negative — VOW-app page WITHOUT a directive → no diagnostic
	//                    (Pattern 2 reflection fallback applies)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// NEGATIVE — a <c>.razor</c> file anchored at a VOW-app-style path that does NOT carry
	/// any <c>@rendermode</c> directive. Pattern 2 reflection at <c>App.razor</c> applies
	/// the canonical WASM+prerender fallback for this page; no diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NoDiagnostic_When_NoDirectivePresent()
	{
		// Fake VOW-app page WITHOUT any @rendermode directive.
		const string razorContent = @"@page ""/fake-page""
<h1>I have no render-mode directive (Pattern 2 reflection applies)</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Expect ZERO diagnostics — absence of directive is the canonical state.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-2: Positive — VOW-app page WITH the prior-canonical directive
	//                    → fires at the directive's line
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — VOW-app page declares the prior-canonical WASM render-mode directive.
	/// Under the inverted semantic, any per-page <c>@rendermode</c> directive is forbidden
	/// because it bypasses Pattern 2 reflection at <c>App.razor</c>. The diagnostic fires
	/// at the directive's line (line 2 in this fixture — line 1 is <c>@page</c>).
	/// </summary>
	[Fact]
	public async Task Diagnostic_FiresOn_VowAppPage_WithCanonicalDirective_AtDirectiveLine()
	{
		// Line 1: @page "/fake-page"
		// Line 2: @rendermode @(new InteractiveWebAssemblyRenderMode(prerender: true))
		// Line 3: <h1>...</h1>
		var razorContent = $@"@page ""/fake-page""
{CanonicalDirective}
<h1>I declare the prior-canonical directive — forbidden under Pattern 2 reflection</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Diagnostic anchored at line 2 (the @rendermode line); col 1 → col 1 + match length.
		// "@rendermode" = 11 chars → end column = 12 (1-based: cols 1..12).
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NnRmRenderModeEnforcementAnalyzer.RM001_Rule)
				.WithSpan(razorPath, 2, 1, 2, 12)
				.WithArguments("FakePage"));

		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-3: Bypass — [assembly: NnRmBypass] short-circuits even with directive
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// BYPASS — VOW-app page declares the prior-canonical directive (would normally fire),
	/// but the consuming assembly carries
	/// <c>[assembly: NotNot.BlazorAnalyzers.RenderMode.NnRmBypass]</c>. The
	/// <see cref="NnRmRenderModeEnforcementAnalyzer.ShouldAnalyzeCompilation"/> gate
	/// short-circuits before any file scan, so no diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NoDiagnostic_When_AssemblyLevelBypassApplied()
	{
		var razorContent = $@"@page ""/fake-page""
{CanonicalDirective}
<h1>Directive present but assembly is bypassed — no diagnostic</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		// TestCode declares the assembly-level bypass attribute.
		const string consumerSource = @"[assembly: NotNot.BlazorAnalyzers.RenderMode.NnRmBypass]
namespace TestProject
{
    public class Marker { }
}
";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		// Add the bypass attribute declaration as a separate source so the
		// `[assembly: ...]` reference in TestCode resolves.
		test.TestState.Sources.Add(BypassAttributeStub);
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Expect ZERO diagnostics — [assembly: NnRmBypass] short-circuits the rule.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-4: Path-exemption — Pages/Samples/** stays exempt even WITH directive
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// PATH-EXEMPTION — sample page anchored under <c>Pages/Samples/</c> declares the
	/// prior-canonical directive. The
	/// <see cref="NnRmRenderModeEnforcementAnalyzer.IsExceptedPath"/> check recognizes the
	/// segment and skips analysis → no diagnostic. Samples MAY declare per-page directives
	/// (the override mechanism IS what samples demonstrate).
	/// </summary>
	[Fact]
	public async Task NoDiagnostic_When_FileUnderPagesSamples_WithDirective()
	{
		var razorContent = $@"@page ""/fake-sample""
{CanonicalDirective}
<h1>Sample page WITH directive — exempt path → no diagnostic</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/Samples/FakeSample.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Expect ZERO diagnostics — Pages/Samples/** is exempt per IsExceptedPath.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-5: Path-exemption — Pages/NnDesignSamples/** stays exempt even WITH directive
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// PATH-EXEMPTION — NnDesign sample page anchored under <c>Pages/NnDesignSamples/</c>
	/// declares the prior-canonical directive. Same exemption rationale as T-M5-4 —
	/// AGENTS.md UI Component Policy carve-out; samples may demonstrate the per-page
	/// override mechanism.
	/// </summary>
	[Fact]
	public async Task NoDiagnostic_When_FileUnderPagesNnDesignSamples_WithDirective()
	{
		var razorContent = $@"@page ""/fake-nndesign-sample""
{CanonicalDirective}
<h1>NnDesign sample page WITH directive — exempt path → no diagnostic</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/NnDesignSamples/FakeNnDesignSample.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Expect ZERO diagnostics — Pages/NnDesignSamples/** is exempt per IsExceptedPath.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-6: Broadened regex — InteractiveServer page directive → fires
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — VOW-app page declares <c>@rendermode InteractiveServer</c> shorthand. The
	/// broadened <c>^\s*@rendermode\b</c> regex matches ANY <c>@rendermode</c> directive
	/// value, not just the prior-canonical literal. This case is doubly forbidden under
	/// Pattern 2 reflection: (1) bypasses the reflection getter, (2)
	/// <c>InteractiveServer</c> on a WASM-routed page crashes the router with
	/// <c>NotSupportedException</c>. The diagnostic fires at the directive line.
	/// </summary>
	[Fact]
	public async Task Diagnostic_FiresOn_VowAppPage_WithInteractiveServerDirective()
	{
		const string razorContent = @"@page ""/fake-page""
@rendermode InteractiveServer
<h1>InteractiveServer shorthand — fires under broadened regex</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Diagnostic at line 2, cols 1..12 (matched span = "@rendermode" = 11 chars).
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NnRmRenderModeEnforcementAnalyzer.RM001_Rule)
				.WithSpan(razorPath, 2, 1, 2, 12)
				.WithArguments("FakePage"));

		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-7: Layout-tier directive on MainLayout.razor → fires
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — layout component declares <c>@rendermode</c>. Layout-tier directives are
	/// forbidden under Pattern 2 reflection because the
	/// <c>LayoutComponentBase.RenderFragment Body</c> is non-serializable across an SSR→
	/// WASM-island boundary; placing <c>@rendermode</c> on the layout triggers the LCA
	/// reconciliation downgrade documented in <c>dotnet/aspnetcore#52768</c> (stacked WASM
	/// directives become <c>type:auto</c> boundary markers and break JS interop reach).
	/// The broadened regex matches layout files the same way as page files — no special
	/// case needed.
	/// </summary>
	[Fact]
	public async Task Diagnostic_FiresOn_LayoutFile_WithDirective()
	{
		// MainLayout.razor declares @rendermode at line 2.
		var razorContent = $@"@inherits LayoutComponentBase
{CanonicalDirective}
<div class=""layout-root"">@Body</div>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Layout/MainLayout.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Diagnostic at line 2, cols 1..12.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NnRmRenderModeEnforcementAnalyzer.RM001_Rule)
				.WithSpan(razorPath, 2, 1, 2, 12)
				.WithArguments("MainLayout"));

		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-8: Diagnostic anchoring — leading whitespace, directive at indented column
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — VOW-app page declares <c>@rendermode</c> with 4-space leading whitespace
	/// on the directive line. Pins the diagnostic-location-anchoring code path in
	/// <see cref="NnRmRenderModeEnforcementAnalyzer.AnalyzeAdditionalFile"/>:
	/// <c>while (text[spanStart] != '@') spanStart++</c> must advance past the leading
	/// whitespace consumed by the regex's <c>^\s*</c> so the reported span starts at the
	/// <c>@</c> of the directive token, not at the preceding whitespace. The broadened
	/// <c>^\s*@rendermode\b</c> regex matches via the optional leading whitespace; the
	/// diagnostic anchors at column 5 (1-based: cols 5..16, matching <c>@rendermode</c>).
	/// </summary>
	/// <remarks>
	/// Companion to the column-1-anchored cases (T-M5-2, T-M5-6, T-M5-7) which exercise the
	/// anchoring code without forcing the while-loop advance. This test discriminates the
	/// "match.Index points at the leading space, must scan forward to '@'" path.
	/// </remarks>
	[Fact]
	public async Task Diagnostic_FiresOn_VowAppPage_WithLeadingWhitespace_DirectiveAtIndentedColumn()
	{
		// Line 1: @page "/test"
		// Line 2: "    @rendermode InteractiveServer" — 4 leading spaces; '@' at col 5.
		const string razorContent = "@page \"/test\"\n    @rendermode InteractiveServer\n<h1>indented directive</h1>\n";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Diagnostic at line 2, cols 5..16 (1-based) — '@' at col 5, end after '@rendermode' (11 chars).
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NnRmRenderModeEnforcementAnalyzer.RM001_Rule)
				.WithSpan(razorPath, 2, 5, 2, 16)
				.WithArguments("FakePage"));

		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-9: Word-boundary — `@rendermodeFoo` is NOT a directive, no diagnostic
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// NEGATIVE — VOW-app page contains the token <c>@rendermodeFoo</c> (no word boundary
	/// after <c>rendermode</c>; an identifier character follows immediately). The
	/// <c>\b</c> word-boundary anchor in the
	/// <see cref="NnRmRenderModeEnforcementAnalyzer.RenderModeDirectiveRegex"/> regex
	/// distinguishes the <c>@rendermode</c> directive from arbitrary identifiers that
	/// happen to start with the same character sequence; this test pins that semantic.
	/// </summary>
	/// <remarks>
	/// Without the <c>\b</c>, the regex would incorrectly fire on any identifier prefixed
	/// with <c>@rendermode</c> (e.g. Razor implicit-expression syntax referencing a field
	/// named <c>rendermodeFoo</c>). The word-boundary is the only line of defense against
	/// that false-positive class.
	/// </remarks>
	[Fact]
	public async Task NoDiagnostic_When_TokenIsRendermodeFollowedByIdentifierChar()
	{
		// Line 2: '@rendermodeFoo bar' — no word boundary between 'rendermode' and 'Foo'.
		const string razorContent = @"@page ""/test""
@rendermodeFoo bar
<h1>not a directive — identifier suffix breaks word boundary</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// Expect ZERO diagnostics — the \b word boundary rejects this token.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// T-M5-10: Known limitation — directive inside Razor block comment fires
	//                              (text-scan analyzer lacks Razor lexical context)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// KNOWN-LIMITATION — VOW-app page contains a Razor <c>@* ... *@</c> block comment whose
	/// body includes the line <c>@rendermode InteractiveServer</c>. The
	/// <see cref="NnRmRenderModeEnforcementAnalyzer"/> uses an AdditionalFiles text-scan
	/// (no Razor parser), so the block-comment delimiters are invisible to it; the regex
	/// matches the commented-out directive and fires NN_RM_001 as a false-positive.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This is BY DESIGN.</b> <c>NotNot.BlazorAnalyzers</c> uses text-scan analyzers per
	/// Microsoft.CodeAnalysis precedent for cross-language source analysis — adding a
	/// Razor-parser dependency would inflate the analyzer's transitive closure with the
	/// <c>Microsoft.AspNetCore.Razor.*</c> assemblies and tie analyzer evolution to the
	/// Razor compiler's API surface.
	/// </para>
	/// <para>
	/// <b>Realistic impact</b>: LOW. Block-commented <c>@rendermode</c> documentation in
	/// Razor files is uncommon; if encountered, the workaround is to remove the <c>@</c>
	/// prefix in the comment body (e.g. write <c>at-rendermode</c> in prose). If this
	/// limitation surfaces in real codebases more frequently than expected, a future
	/// enhancement could add a line-by-line <c>@*</c> / <c>*@</c> pairing tracker — file
	/// kanban (<c>/vow-kanban-new</c>) at that point.
	/// </para>
	/// <para>
	/// <b>Pinning purpose</b>: this test locks the current behavior so a future "improve
	/// the regex" change (e.g. add line-level comment-tracking) doesn't silently break the
	/// documented contract — any change must explicitly invert this test's expectation and
	/// update the documentation in tandem.
	/// </para>
	/// </remarks>
	[Fact]
	public async Task KnownLimitation_FiresOn_DirectiveInsideRazorBlockComment_TextScanAnalyzerLacksRazorLexicalContext()
	{
		// Line 1: @page "/test"
		// Line 2: @*
		// Line 3: Documentation block:
		// Line 4: @rendermode InteractiveServer    ← false-positive fires here
		// Line 5: *@
		// Line 6: <h1>page</h1>
		const string razorContent = @"@page ""/test""
@*
Documentation block:
@rendermode InteractiveServer
*@
<h1>page</h1>
";
		const string razorPath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/FakePage.razor";

		var test = new CSharpAnalyzerTest<NnRmRenderModeEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));

		// KNOWN-LIMITATION: false-positive at line 4, cols 1..12 (text-scan ignores @* ... *@).
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NnRmRenderModeEnforcementAnalyzer.RM001_Rule)
				.WithSpan(razorPath, 4, 1, 4, 12)
				.WithArguments("FakePage"));

		await test.RunAsync();
	}
}
