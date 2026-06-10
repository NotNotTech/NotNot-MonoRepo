using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB044 — a STATIC-LITERAL inline <c>style=</c> / <c>Style=</c> attribute must not appear in CONSUMER
/// <c>.razor</c> markup. This is the consumer-side counterpart to NNB043 (producer-side Tier-B exposure):
/// a hard-coded literal inline style is "appearance laundered through a literal" — the static-padding /
/// static-layout class that the NnDesign semantic params (Density / BodyLayout / etc.) are meant to absorb.
/// <para>
/// <b>Value-shape ALLOWLIST (the core discriminator).</b> Inline style is only a violation when its value is
/// a STATIC LITERAL (no Razor expression). A DYNAMIC value — any <c>@</c>-bound / interpolated / expression
/// value — is ALLOWED: a sparkline height, a computed color, a per-row width are legitimately dynamic and
/// CANNOT be expressed as a static semantic param.
/// <list type="bullet">
///   <item><b>FIRE</b> (static literal): <c>style="padding: 4px 8px"</c>, <c>style="display:flex"</c> — no
///         <c>@</c> anywhere in the value.</item>
///   <item><b>ALLOW</b> (dynamic): <c>style="@foo"</c>, <c>style="background:@color"</c>,
///         <c>style="height:@(h)px"</c>, <c>style="@($"width:{w}px")"</c> — the value contains a Razor
///         expression (<c>@</c>), or the attribute is a directive-style binding (<c>style=@expr</c> /
///         <c>style="@..."</c>).</item>
/// </list>
/// The allowlist keys on the VALUE shape, not on the attribute name — a static literal value is the
/// migrate-to-semantic-param signal; a dynamic value is sanctioned by construction.
/// </para>
/// <para>
/// <b>Text-scan host.</b> Operates on <c>.razor</c> markup registered as AdditionalFiles by the package's
/// <c>.props</c> (same host as NNB022's markup pathway). Does NOT scan <c>.razor.cs</c> / <c>.cs</c>
/// (inline-style literals live in markup) nor <c>.css</c> (selectors, not attributes). Skips matches inside
/// HTML (<c>&lt;!-- --&gt;</c>) and Razor (<c>@* *@</c>) comments.
/// </para>
/// <para>
/// <b>Exemptions</b> (the same buckets NNB022 honors): the NnDesign producer's own
/// <c>NotNot.BlazorDesign</c> internals; <c>NnDesignSamples/**</c> and <c>Pages/Samples/**</c>; the named
/// root-bootstrap + dev-diagnostics / harness file allow-list; a per-file opt-out marker
/// (<c>nnb044:allow-inline-style: &lt;reason&gt;</c>); the assembly-level <c>[assembly: NnDesignBypass]</c>
/// marker; and the shared <c>NnDesignPolicyAnalyzerEnabled=false</c> build-property kill-switch.
/// </para>
/// <para>
/// <b>Severity</b> = Warning. The static-literal Slice-3 conformity sweep is NOT yet done, so the consumer
/// tree still carries the static-literal bulk; an Error would break the build. Warning surfaces the gap
/// honestly and reveals the authoritative Slice-3 migration scope (its fire count + file list). Ratchets to
/// Error only AFTER the conformity slice clears the consumer surface (Appendix A target: Warning → Error).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnDesignInlineStyleAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic ID for a static-literal inline <c>style=</c> in consumer markup.</summary>
	public const string DiagnosticId = "NNB044";

	private const string Category = "NnDesign";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Per-file opt-out marker — <c>nnb044:allow-inline-style: &lt;reason&gt;</c>.</summary>
	private const string OptOutMarker = "nnb044:allow-inline-style";

	private static readonly LocalizableString Title =
		"Static-literal inline style in consumer markup — use a semantic NnDesign param";

	private static readonly LocalizableString MessageFormat =
		"Static-literal inline style '{0}' in consumer markup is an appearance-laundering channel: a "
		+ "hard-coded layout/padding literal that bypasses the prescriptive NnDesign contract. Replace it "
		+ "with a SEMANTIC param (e.g. a Density / BodyLayout token on the surrounding Nn* component) or your "
		+ "own scoped CSS class. (A DYNAMIC value — style=\"@expr\" / style=\"prop:@value\" — is allowed; it "
		+ "is legitimately computed and cannot be a static param.) (NNB044)";

	private static readonly LocalizableString Description =
		"The NnDesign manifesto bans free-form inline styling in consumer markup — a static-literal "
		+ "inline 'style=' attribute (no Razor expression in its value) is appearance laundered through a "
		+ "literal: the static padding / display / layout the semantic params (Density / BodyLayout) are "
		+ "meant to absorb. NNB044 is the consumer-side counterpart to NNB043 (producer-side Tier-B "
		+ "exposure). Value-shape allowlist: only STATIC-LITERAL values fire; any '@'-bound / interpolated / "
		+ "expression value (style=\"@foo\", style=\"background:@color\", style=\"height:@(h)px\", or a "
		+ "directive binding style=@expr) is ALLOWED as legitimately dynamic. Scans '.razor' markup only "
		+ "(not .razor.cs / .cs / .css). Skips matches inside HTML (<!-- -->) and Razor (@* *@) comments. "
		+ "Exempt: NotNot.BlazorDesign producer internals; NnDesignSamples/** + Pages/Samples/**; the named "
		+ "root-bootstrap + dev-harness file allow-list. Per-file opt-out: @* nnb044:allow-inline-style: "
		+ "<reason> *@. Assembly opt-out: [assembly: NotNot.BlazorAnalyzers.NnDesign.NnDesignBypass]. "
		+ "Kill-switch: <NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled>. Severity = "
		+ "Warning (the static-literal conformity sweep is not yet done; the consumer still carries the bulk, "
		+ "so an Error would break the build) — ratchets to Error after the conformity slice clears the "
		+ "consumer surface. Authority: NotNot.BlazorDesign/AGENTS.md -> Consumer Policy; Appendix A NNB044.";

	/// <summary>NNB044 descriptor — Warning severity, NnDesign category.</summary>
	public static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: HelpBase + "nnb044");  // lowercase anchor — GitHub slugifies

	// ── Regex patterns ────────────────────────────────────────────────────

	/// <summary>
	/// Matches a quoted inline <c>style</c> attribute: <c>style="..."</c> or <c>style='...'</c> (case-
	/// insensitive on the attribute name to cover both raw-HTML <c>style=</c> and Blazor-component
	/// <c>Style=</c>). Captures the quote char and the raw value. Anchored on a leading boundary
	/// (whitespace / <c>&lt;</c> / quote) so longer attribute names ending in "style" (e.g.
	/// <c>BodyStyle=</c>, <c>data-style=</c>) do NOT match — only the standalone <c>style</c> attribute.
	/// Value-shape (static-vs-dynamic) is judged AFTER the match on the captured value.
	/// </summary>
	private static readonly Regex QuotedStyleAttr = new(
		@"(?<![\w-])[Ss][Tt][Yy][Ll][Ee]\s*=\s*(?<q>[""'])(?<val>.*?)\k<q>",
		RegexOptions.Compiled | RegexOptions.Singleline);

	/// <summary>
	/// Matches a directive-style binding: <c>style=@expr</c> (no quotes) — these are ALWAYS dynamic by
	/// construction. Located so the quoted-attribute scan can be confirmed as the only fire surface; an
	/// unquoted <c>style=@...</c> is never a static literal, so it is never reported (no separate handling
	/// beyond NOT matching <see cref="QuotedStyleAttr"/>, which requires a quote).
	/// </summary>
	private static readonly Regex UnquotedStyleBinding = new(
		@"(?<![\w-])[Ss][Tt][Yy][Ll][Ee]\s*=\s*@",
		RegexOptions.Compiled);

	// ── Exception buckets (mirror NnDesignMudBlazorPolicyAnalyzer) ─────────

	/// <summary>File-name allow-list — root provider wiring + dev/test/legacy harness pages.</summary>
	private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
	{
		// Root provider wiring
		"App.razor",

		// Test harnesses (Razor)
		"BlazorTermTestHarness.Wasm.razor",
		"BlazorTermTestHarness.Server.razor",
		"BlazorTermHarmonizedHarness.Wasm.razor",
		"BlazorTermHarmonizedHarness.Server.razor",
		"BlazorTermDiffTest.razor",
		"BlazorTermTabTest.razor",
		"BlazorTermTest.razor",
		"HarnessDummyTab.razor",
		"HarnessTerminalWrapper.razor",

		// Sample / example / legacy
		"RichEditExamplePage.razor",
		"NnDesignSamplesPage.razor",
		"DashboardLegacy.razor",
		"TestInputs.razor",
	};

	// ── DiagnosticAnalyzer overrides ─────────────────────────────────────

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationAction(AnalyzeCompilation);
	}

	// ── Main entry point ────────────────────────────────────────────────────

	private static void AnalyzeCompilation(CompilationAnalysisContext context)
	{
		// Shared kill-switch with the rest of the NnDesign policy rules.
		if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
			return;

		// Assembly-level opt-out — the wrapper layer / sibling primitive layers declare
		// [assembly: NnDesignBypass]. Matches by simple attribute name.
		if (HasNnDesignBypassAssemblyAttribute(context.Compilation))
			return;

		foreach (var file in context.Options.AdditionalFiles)
		{
			context.CancellationToken.ThrowIfCancellationRequested();
			AnalyzeFile(context, file);
		}
	}

	private static void AnalyzeFile(CompilationAnalysisContext context, AdditionalText file)
	{
		var path = file.Path;
		if (string.IsNullOrEmpty(path))
			return;

		// Only .razor markup carries inline style attributes in template form. (.razor.cs / .cs build
		// markup as strings — out of scope; .razor.css / .css are selectors, not attributes.)
		if (!path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
			return;

		// Path-bucket exemption — producer internals, samples, named harness/bootstrap files.
		if (IsExceptedPath(path))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();

		// Per-file opt-out marker (whole-file scan).
		if (HasOptOutComment(text))
			return;

		AnalyzeRazorText(context, file.Path, sourceText, text);
	}

	// ── Markup analysis ──────────────────────────────────────────────────

	private static void AnalyzeRazorText(
		CompilationAnalysisContext ctx, string filePath, SourceText src, string text)
	{
		var htmlComments = FindHtmlCommentRanges(text);
		var razorComments = FindRazorCommentRanges(text);

		foreach (Match m in QuotedStyleAttr.Matches(text))
		{
			if (IsInComment(htmlComments, m.Index) || IsInComment(razorComments, m.Index))
				continue;

			var value = m.Groups["val"].Value;

			// VALUE-SHAPE ALLOWLIST: a value containing a Razor expression ('@') is DYNAMIC → allowed.
			// Only a fully static literal value fires.
			if (IsDynamicValue(value))
				continue;

			// Empty / whitespace-only value carries no laundered appearance → not a violation.
			if (string.IsNullOrWhiteSpace(value))
				continue;

			var display = TrimForDisplay(value);
			Report(ctx, filePath, src, m.Index, m.Length, display);
		}

		// NOTE: an unquoted directive binding `style=@expr` is dynamic by construction and is intentionally
		// NOT reported. QuotedStyleAttr requires a quote, so it never matches the unquoted form; the
		// UnquotedStyleBinding pattern exists only to document that surface as a known-allowed shape.
		_ = UnquotedStyleBinding;
	}

	/// <summary>
	/// Value-shape discriminator. A value is DYNAMIC (allowed) when it contains a Razor expression marker
	/// (<c>@</c>) — covering <c>@foo</c>, <c>prop:@color</c>, <c>@(expr)</c>, and interpolated
	/// <c>@($"...")</c> forms. A value with NO <c>@</c> is a STATIC LITERAL (fires).
	/// </summary>
	private static bool IsDynamicValue(string value)
	{
		return value.IndexOf('@') >= 0;
	}

	/// <summary>Collapses a style value to a compact single-line display string for the diagnostic message.</summary>
	private static string TrimForDisplay(string value)
	{
		var collapsed = Regex.Replace(value.Trim(), @"\s+", " ");
		const int max = 60;
		return collapsed.Length <= max ? collapsed : collapsed.Substring(0, max) + "…";
	}

	// ── Shared helpers (mirror NnDesignMudBlazorPolicyAnalyzer) ───────────

	private static bool IsExceptedPath(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return false;

		var p = filePath.Replace('\\', '/');

		// The NnDesign producer / wrapper layer itself authors inline styles legitimately.
		if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		// Samples pages — canonical NnDesignSamples folder + generic Pages/Samples bucket.
		if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		var fileName = Path.GetFileName(p);
		return AllowedFileNames.Contains(fileName);
	}

	private static bool HasOptOutComment(string fileText)
	{
		if (string.IsNullOrEmpty(fileText))
			return false;
		return fileText.IndexOf(OptOutMarker, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsOptedOut(AnalyzerConfigOptions options)
	{
		return options.TryGetValue("build_property.NnDesignPolicyAnalyzerEnabled", out var value)
			&& string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Returns true when the compilation's assembly carries an <c>[assembly: NnDesignBypass]</c> marker
	/// (matched by simple attribute name — consumer may reference the analyzer type or declare a local
	/// internal copy). Mirrors <see cref="NnDesignMudBlazorPolicyAnalyzer"/>.
	/// </summary>
	private static bool HasNnDesignBypassAssemblyAttribute(Compilation compilation)
	{
		if (compilation?.Assembly is not IAssemblySymbol assembly)
			return false;

		foreach (var attribute in assembly.GetAttributes())
		{
			var name = attribute.AttributeClass?.Name;
			if (string.Equals(name, nameof(NnDesignBypassAttribute), StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static void Report(
		CompilationAnalysisContext ctx, string filePath, SourceText src,
		int position, int length, string displayName)
	{
		var start = src.Lines.GetLinePosition(position);
		var end = src.Lines.GetLinePosition(position + length);
		var location = Location.Create(
			filePath,
			new TextSpan(position, length),
			new LinePositionSpan(start, end));

		ctx.ReportDiagnostic(Diagnostic.Create(Rule, location, displayName));
	}

	// ── Comment range detection (mirror NnDesignMudBlazorPolicyAnalyzer) ──

	/// <summary>Builds sorted list of <c>&lt;!-- ... --&gt;</c> comment ranges in HTML/Razor text.</summary>
	private static List<(int Start, int End)> FindHtmlCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		var i = 0;
		while (i < text.Length - 3)
		{
			if (text[i] == '<' && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
			{
				var start = i;
				i += 4;
				while (i < text.Length - 2)
				{
					if (text[i] == '-' && text[i + 1] == '-' && text[i + 2] == '>')
					{
						i += 3;
						break;
					}
					i++;
				}
				ranges.Add((start, i));
			}
			else
			{
				i++;
			}
		}
		return ranges;
	}

	/// <summary>Builds sorted list of <c>@* ... *@</c> comment ranges in Razor text.</summary>
	private static List<(int Start, int End)> FindRazorCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		var i = 0;
		while (i < text.Length - 1)
		{
			if (text[i] == '@' && text[i + 1] == '*')
			{
				var start = i;
				i += 2;
				while (i < text.Length - 1)
				{
					if (text[i] == '*' && text[i + 1] == '@')
					{
						i += 2;
						break;
					}
					i++;
				}
				ranges.Add((start, i));
			}
			else
			{
				i++;
			}
		}
		return ranges;
	}

	private static bool IsInComment(List<(int Start, int End)> ranges, int position)
	{
		foreach (var (s, e) in ranges)
		{
			if (position >= s && position < e) return true;
			if (s > position) break;
		}
		return false;
	}
}
