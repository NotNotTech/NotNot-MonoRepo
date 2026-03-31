using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.BannedComponents;

/// <summary>
/// Detects direct usage of MudTooltip in Razor files and recommends NnTooltip instead.
/// Scans AdditionalTexts registered via the .props file during build.
/// <para>
/// NnTooltip extends MudTooltip with keyboard shortcut display (<c>ShortcutKeys</c> parameter),
/// command chord visibility integration, and configurable shortcut display modes (Tooltip, Inline, Hidden).
/// It is the standard tooltip component for all projects except NnDesign internals.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BannedMudTooltipAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "BannedComponents";
	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	// ── Diagnostic Descriptor ───────────────────────────────────────────

	/// <summary>NNB031: Use NnTooltip instead of MudTooltip.</summary>
	public static readonly DiagnosticDescriptor RuleBannedMudTooltip = new(
		"NNB031",
		"Use NnTooltip instead of MudTooltip",
		"Use NnTooltip instead of MudTooltip. NnTooltip supports ShortcutKeys display and command chord integration. (NNB031)",
		Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
		description: "Direct usage of MudTooltip bypasses keyboard shortcut display integration. "
			+ "Use NnTooltip which extends MudTooltip with ShortcutKeys parameter, command chord "
			+ "visibility, and configurable display modes (Tooltip, Inline, Hidden).",
		helpLinkUri: HelpBase + "NNB031");

	// ── Banned tag definitions ──────────────────────────────────────────

	private static readonly (string Tag, string DisplayName)[] BannedTags =
	{
		("<MudTooltip", "MudTooltip"),
	};

	// ── DiagnosticAnalyzer overrides ────────────────────────────────────

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(RuleBannedMudTooltip);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterCompilationAction(AnalyzeCompilation);
	}

	// ── Main entry point ────────────────────────────────────────────────

	private static void AnalyzeCompilation(CompilationAnalysisContext context)
	{
		if (IsOptedOut(context))
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

		// Only scan .razor files (not .razor.css, not .css)
		if (path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
			return;
		if (!path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
			return;

		// Skip the wrapper component itself — NnTooltip.razor legitimately uses MudTooltip internally
		if (path.EndsWith("NnTooltip.razor", StringComparison.OrdinalIgnoreCase))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();

		// Per-file opt-out: @* nnb031:allow-mudtooltip *@ anywhere in file skips analysis
		if (text.Contains("nnb031:allow-mudtooltip", StringComparison.OrdinalIgnoreCase))
			return;

		var htmlComments = FindHtmlCommentRanges(text);
		var razorComments = FindRazorCommentRanges(text);

		foreach (var (tag, displayName) in BannedTags)
		{
			CheckBannedTag(context, file, sourceText, text, htmlComments, razorComments, tag, displayName);
		}
	}

	// ── Detection logic ─────────────────────────────────────────────────

	private static void CheckBannedTag(
		CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
		List<(int Start, int End)> htmlComments, List<(int Start, int End)> razorComments,
		string tag, string displayName)
	{
		int pos = 0;
		while ((pos = text.IndexOf(tag, pos, StringComparison.Ordinal)) >= 0)
		{
			// Word boundary check: character after tag must be whitespace, '>', '/', or end-of-string.
			int afterTag = pos + tag.Length;
			if (afterTag < text.Length)
			{
				char next = text[afterTag];
				if (next != ' ' && next != '\t' && next != '\r' && next != '\n'
					&& next != '>' && next != '/')
				{
					pos += tag.Length;
					continue;
				}
			}

			// Skip matches inside comments
			if (IsInComment(htmlComments, pos) || IsInComment(razorComments, pos))
			{
				pos += tag.Length;
				continue;
			}

			Report(ctx, file.Path, src, pos, tag.Length, RuleBannedMudTooltip, displayName);
			pos += tag.Length;
		}
	}

	// ── Helpers ─────────────────────────────────────────────────────────

	private static bool IsOptedOut(CompilationAnalysisContext context)
	{
		return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
				   .TryGetValue("build_property.BannedTooltipAnalyzerEnabled", out var value) &&
			   string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
	}

	private static void Report(
		CompilationAnalysisContext ctx, string filePath, SourceText src,
		int position, int length, DiagnosticDescriptor rule, params object[] args)
	{
		var start = src.Lines.GetLinePosition(position);
		var end = src.Lines.GetLinePosition(position + length);
		var location = Location.Create(
			filePath,
			new TextSpan(position, length),
			new LinePositionSpan(start, end));

		ctx.ReportDiagnostic(args.Length > 0
			? Diagnostic.Create(rule, location, args)
			: Diagnostic.Create(rule, location));
	}

	/// <summary>Builds sorted list of &lt;!-- ... --&gt; comment ranges in HTML/Razor text.</summary>
	private static List<(int Start, int End)> FindHtmlCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		int i = 0;
		while (i < text.Length - 3)
		{
			if (text[i] == '<' && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
			{
				int start = i;
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

	/// <summary>Builds sorted list of @* ... *@ comment ranges in Razor text.</summary>
	private static List<(int Start, int End)> FindRazorCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		int i = 0;
		while (i < text.Length - 1)
		{
			if (text[i] == '@' && text[i + 1] == '*')
			{
				int start = i;
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

	/// <summary>
	/// Checks if a character position falls within any comment range.
	/// Ranges are sorted by start position; linear scan with early exit.
	/// </summary>
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
