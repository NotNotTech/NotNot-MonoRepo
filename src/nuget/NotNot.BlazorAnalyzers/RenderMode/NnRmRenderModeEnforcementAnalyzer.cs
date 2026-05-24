using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.RenderMode;

/// <summary>
/// VOW render-mode enforcement — <c>NN_RM_001</c>. Compile-time check that no non-exempt
/// <c>.razor</c> file declares a per-page or per-layout <c>@rendermode</c> directive. VOW
/// centralizes render-mode declaration at a SINGLE site via Pattern 2 reflection at
/// <c>Server/Components/App.razor</c> (<c>PageRenderMode</c> getter bound to
/// <c>&lt;Routes&gt;</c> + <c>&lt;HeadOutlet&gt;</c>); per-page or per-layout directives are
/// forbidden because they re-introduce the LCA reconciliation defect surface documented in
/// <c>dotnet/aspnetcore#52768</c> AND bypass the central reflection getter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection mechanism</b>: AdditionalFiles text-scan over <c>.razor</c> files (the
/// <c>NotNot.BlazorAnalyzers.props</c> shipped in the package registers all <c>.razor</c>
/// AdditionalFiles for analyzer consumption). For each non-exempt file:
/// <list type="number">
///   <item><description>Apply path-exemption: skip when the file path contains
///     <c>/Pages/Samples/</c> or <c>/Pages/NnDesignSamples/</c> (segment-match,
///     case-insensitive) — these are NnDesign / sample-library pages permitted to declare
///     per-page directives as demonstration of the override mechanism.</description></item>
///   <item><description>Read the file text; apply the
///     <see cref="RenderModeDirectiveRegex"/> regex. If matched, the file declares a
///     <c>@rendermode</c> directive at the matched line — emit <c>NN_RM_001</c> at that
///     line.</description></item>
///   <item><description>If no match, no diagnostic (Pattern 2 reflection's fallback applies
///     and the page inherits the canonical WASM+prerender default).</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Broadened regex coverage</b>: the line-start <c>^\s*@rendermode\b</c> regex matches
/// ANY <c>@rendermode</c> directive value: the prior canonical literal
/// (<c>@(new InteractiveWebAssemblyRenderMode(prerender: true))</c>),
/// <c>InteractiveServer</c> (WASM router crash), <c>InteractiveAuto</c> (re-introduces the
/// dual-DI bug class), <c>InteractiveWebAssembly</c> shorthand, attribute-form
/// (<c>@attribute [RenderModeInteractive*]</c> is NOT matched — only the <c>@rendermode</c>
/// directive form). Layout-tier directives on <c>MainLayout.razor</c> and sub-layouts are
/// matched the same way and emit the same diagnostic.
/// </para>
/// <para>
/// <b>Bypass</b>: <c>[assembly: NotNot.BlazorAnalyzers.RenderMode.NnRmBypass]</c>
/// short-circuits the analyzer for the entire compilation. Intended for non-VOW assemblies
/// that pick up the package (the analyzer NuGet is consumed library-wide) but should not be
/// subject to VOW render-mode enforcement.
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Warning"/> by default. May escalate to
/// error via <c>.editorconfig</c> in CI builds.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnRmRenderModeEnforcementAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "RenderMode";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	// ── NN_RM_001 — Forbidden per-page / per-layout @rendermode directive ─────

	/// <summary>Diagnostic ID for VOW <c>.razor</c> files that declare a per-page or per-layout <c>@rendermode</c> directive (forbidden; Pattern 2 reflection at App.razor is the single declaration site).</summary>
	public const string RM001_DiagnosticId = "NN_RM_001";

	private static readonly LocalizableString RM001_Title =
		"Per-page `@rendermode` directive forbidden — use Pattern 2 reflection at App.razor";

	private static readonly LocalizableString RM001_MessageFormat =
		"File '{0}' declares an `@rendermode` directive. VOW centralizes render-mode declaration "
		+ "at the SINGLE Pattern 2 reflection site (Server/Components/App.razor `PageRenderMode` "
		+ "getter bound to <Routes> + <HeadOutlet>). Per-page or per-layout directives "
		+ "re-introduce LCA reconciliation defects (dotnet/aspnetcore#52768) and bypass the "
		+ "central reflection getter. Remove the directive; the page inherits the canonical "
		+ "WASM+prerender default. If the page genuinely needs a non-default render mode, use "
		+ "the standard `@attribute [RenderModeInteractive*]` form (endpoint metadata flows "
		+ "through Pattern 2 reflection) — or place the file under Pages/Samples/** or "
		+ "Pages/NnDesignSamples/** if it is a library sample demonstrating the override "
		+ "mechanism.";

	private static readonly LocalizableString RM001_Description =
		"VOW (Novaleaf.VibeOverwatch) is an InteractiveWebAssembly app with a single canonical "
		+ "render-mode declaration site (Pattern 2 reflection at Server/Components/App.razor). "
		+ "The reflection getter reads endpoint metadata via "
		+ "`HttpContext.GetEndpoint()?.Metadata.GetMetadata<RenderModeAttribute>()?.Mode` and "
		+ "falls back to `new InteractiveWebAssemblyRenderMode(prerender: true)`. Per-page and "
		+ "per-layout `@rendermode` directives are FORBIDDEN because (1) they re-introduce the "
		+ "LCA reconciliation defect surface that downgrades stacked WASM directives to "
		+ "`type:auto` boundary markers (dotnet/aspnetcore#52768 — breaks JS interop reach for "
		+ "popovers/tooltips/dropdowns), and (2) they bypass the central reflection getter that "
		+ "endpoint metadata flows through. Pages that need a non-default render mode SHOULD "
		+ "use the standard `@attribute [RenderModeInteractive*]` form — endpoint metadata "
		+ "carries the value through Pattern 2 reflection at App.razor without re-introducing "
		+ "the stacked-directive defect surface. Files under `Pages/Samples/**` and "
		+ "`Pages/NnDesignSamples/**` are exempt (documented carve-out — these pedagogical "
		+ "pages MAY declare per-page directives to demonstrate the override mechanism). For "
		+ "non-VOW assemblies that pick up the analyzer package, apply "
		+ "`[assembly: NotNot.BlazorAnalyzers.RenderMode.NnRmBypass]` to short-circuit the rule.";

	/// <summary>NN_RM_001 descriptor — Warning, RenderMode category.</summary>
	public static readonly DiagnosticDescriptor RM001_Rule = new(
		RM001_DiagnosticId,
		RM001_Title,
		RM001_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: RM001_Description,
		helpLinkUri: HelpBase + "nn_rm_001");

	// ── Detection regex ─────────────────────────────────────────────────────────

	/// <summary>
	/// Matches a line-leading <c>@rendermode</c> directive regardless of value (Pattern 2
	/// reflection at <c>Server/Components/App.razor</c> is the single declaration site; any
	/// per-page / per-layout directive value is forbidden). Multiline so the directive may
	/// appear on any line of the file; word-boundary prevents accidental match of
	/// identifiers like <c>@rendermodeName</c>. Compiled once at type-load.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The regex anchors at line-start (with optional leading whitespace) so it does not
	/// match the literal string inside a code-fenced documentation block or an inline
	/// comment that begins mid-line. The <c>@(rendermode|RenderMode)Attribute</c>
	/// C#-attribute form is NOT matched — only the <c>@rendermode</c> Razor directive form.
	/// </para>
	/// <para>
	/// The match index + line-offset calculation determines the 1-based line number of the
	/// offending directive so the diagnostic anchors precisely (not at line 1 as the prior
	/// absence-based rule did).
	/// </para>
	/// </remarks>
	private static readonly Regex RenderModeDirectiveRegex = new(
		@"^\s*@rendermode\b",
		RegexOptions.Compiled | RegexOptions.Multiline);

	// ── DiagnosticAnalyzer overrides ──────────────────────────────────────────

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(RM001_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// Single text-scan pathway over AdditionalFiles — no syntax/semantic actions needed.
		context.RegisterCompilationAction(AnalyzeCompilation);
	}

	// ── Compilation gate ──────────────────────────────────────────────────────

	/// <summary>
	/// Returns <c>true</c> when the compilation should be analyzed. False when the assembly
	/// carries <c>[assembly: NnRmBypass]</c> (short-circuit — non-VOW assemblies opt out
	/// here).
	/// </summary>
	private static bool ShouldAnalyzeCompilation(Compilation compilation)
	{
		var assembly = compilation?.Assembly;
		if (assembly == null)
			return false;

		// Assembly-level opt-out — short-circuit before any file scan fires.
		if (HasNnRmBypassAttribute(assembly))
			return false;

		return true;
	}

	/// <summary>
	/// Returns true when the assembly carries an <c>[assembly: NnRmBypass]</c> marker.
	/// Matched by <em>simple attribute name</em> first
	/// (<c>AttributeClass.Name == "NnRmBypassAttribute"</c>), with a fully-qualified
	/// fallback against <c>NotNot.BlazorAnalyzers.RenderMode.NnRmBypassAttribute</c> — same
	/// dual-match convention as <c>LdddAnalyzerHelpers.HasLdddBypassAttribute</c>.
	/// </summary>
	private static bool HasNnRmBypassAttribute(IAssemblySymbol assembly)
	{
		const string SimpleName = "NnRmBypassAttribute";
		const string FullName = "NotNot.BlazorAnalyzers.RenderMode.NnRmBypassAttribute";

		foreach (var attribute in assembly.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass == null)
				continue;

			if (string.Equals(attrClass.Name, SimpleName, StringComparison.Ordinal))
				return true;

			if (string.Equals(attrClass.ToDisplayString(), FullName, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	// ── Path-exemption ─────────────────────────────────────────────────────────

	/// <summary>
	/// Returns true when the given file path falls under a documented NN_RM exemption:
	/// <list type="bullet">
	///   <item><description><c>Pages/Samples/**</c> — feature-specific sample pages
	///     (LiteDDD §4c companion exemption); MAY declare per-page directives as override
	///     demonstration.</description></item>
	///   <item><description><c>Pages/NnDesignSamples/**</c> — NnDesign component sample
	///     pages (AGENTS.md UI Component Policy carve-out); MAY declare per-page
	///     directives.</description></item>
	/// </list>
	/// Case-insensitive segment match after normalizing path separators to <c>/</c>.
	/// </summary>
	private static bool IsExceptedPath(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return false;

		var normalized = filePath.Replace('\\', '/');

		// Documented carve-outs — pedagogical / library-sample pages MAY declare per-page
		// directives (the override mechanism IS what these samples demonstrate).
		if (normalized.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		if (normalized.IndexOf("/Pages/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		return false;
	}

	// ── Compilation analysis ──────────────────────────────────────────────────

	private static void AnalyzeCompilation(CompilationAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		foreach (var file in context.Options.AdditionalFiles)
		{
			context.CancellationToken.ThrowIfCancellationRequested();
			AnalyzeAdditionalFile(context, file);
		}
	}

	private static void AnalyzeAdditionalFile(CompilationAnalysisContext context, AdditionalText file)
	{
		var path = file.Path;
		if (string.IsNullOrEmpty(path))
			return;

		// Only .razor files participate. The build/NotNot.BlazorAnalyzers.props file already
		// scopes AdditionalFiles to .razor / .razor.cs / .css, so a secondary extension check
		// here defends against any consumer manually adding other paths to AdditionalFiles.
		var isRazor = path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
			&& !path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase)
			&& !path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase);

		if (!isRazor)
			return;

		// Documented path-exemption — sample / NnDesign sample pages MAY declare per-page
		// directives (the override mechanism IS what these samples demonstrate).
		if (IsExceptedPath(path))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();

		// If NO @rendermode directive present, the file inherits Pattern 2 reflection's
		// fallback — no diagnostic.
		var match = RenderModeDirectiveRegex.Match(text);
		if (!match.Success)
			return;

		// Anchor the diagnostic at the `@` of the directive, not at any preceding whitespace
		// consumed by the regex's leading `^\s*`. Scan forward from match.Index to the first
		// `@` character so the reported span starts on the directive token.
		var spanStart = match.Index;
		var spanEnd = match.Index + match.Length;
		while (spanStart < spanEnd && text[spanStart] != '@')
			spanStart++;

		// Compute 0-based line/column via TextLineCollection from the SourceText (handles
		// CRLF / LF / mixed line endings correctly without manual scanning).
		var startLinePosition = sourceText.Lines.GetLinePosition(spanStart);
		var endLinePosition = sourceText.Lines.GetLinePosition(spanEnd);

		var location = Location.Create(
			path,
			new TextSpan(spanStart, spanEnd - spanStart),
			new LinePositionSpan(startLinePosition, endLinePosition));

		// Use the basename (no directories) as the file identifier in the message — keeps
		// per-file hits greppable across multi-page emission scenarios.
		var pageName = ExtractPageName(path);

		context.ReportDiagnostic(Diagnostic.Create(
			RM001_Rule,
			location,
			pageName));
	}

	/// <summary>
	/// Returns the file basename without the <c>.razor</c> extension. Used for the
	/// page-name placeholder in the diagnostic message.
	/// </summary>
	private static string ExtractPageName(string path)
	{
		var normalized = path.Replace('\\', '/');
		var lastSlash = normalized.LastIndexOf('/');
		var filename = lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;

		const string razorExt = ".razor";
		if (filename.EndsWith(razorExt, StringComparison.OrdinalIgnoreCase))
			return filename.Substring(0, filename.Length - razorExt.Length);

		return filename;
	}
}
