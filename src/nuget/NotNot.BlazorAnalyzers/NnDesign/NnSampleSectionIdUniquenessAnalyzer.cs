using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB047 — every <c>&lt;NnSampleSection Id="..."&gt;</c> static-literal <c>Id</c> must be UNIQUE across the
/// compilation. <c>NnSampleSection</c> renders its <c>Id</c> parameter as an HTML <c>id</c>
/// (<c>&lt;NnPaper id="@Id"&gt;</c>); the <c>/samples</c> Table-of-Contents and browser tests anchor on it
/// via <c>document.getElementById</c>, which returns only the FIRST matching element. Two sections sharing
/// an <c>Id</c> therefore make every anchor / ToC navigation to that id silently mis-target the first
/// occurrence.
/// <para>
/// <b>Cross-file uniqueness.</b> Aggregates every static-literal <c>NnSampleSection</c> <c>Id</c> value
/// across all <c>.razor</c> AdditionalFiles in the compilation and reports each section participating in a
/// duplicate set. A duplicate spanning two files fires in BOTH. Dynamic Id values (<c>Id="@expr"</c>) are
/// not statically comparable and are skipped. Scans <c>.razor</c> only (not <c>.razor.cs</c> / <c>.cs</c> /
/// <c>.razor.css</c>); skips matches inside HTML (<c>&lt;!-- --&gt;</c>) and Razor (<c>@* *@</c>) comments.
/// </para>
/// <para>
/// <b>No path exemptions.</b> Unlike the consumer-policy NnDesign analyzers (NNB044 etc.), NNB047 does NOT
/// exempt <c>NnDesignSamples/**</c> or <c>NnDesignSamplesPage.razor</c> — that is precisely where
/// <c>NnSampleSection</c> is used. It keys on the COMPONENT, not on a consumer path. Escape hatches: the
/// shared <c>[assembly: NnDesignBypass]</c> marker, the <c>NnDesignPolicyAnalyzerEnabled=false</c>
/// kill-switch, and per-rule <c>dotnet_diagnostic.NNB047.severity</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnSampleSectionIdUniquenessAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic ID for a duplicate <c>NnSampleSection</c> <c>Id</c>.</summary>
	public const string DiagnosticId = "NNB047";

	private const string Category = "NnDesign";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	private static readonly LocalizableString Title =
		"Duplicate NnSampleSection Id — HTML ids must be unique";

	private static readonly LocalizableString MessageFormat =
		"NnSampleSection Id '{0}' is not unique — it is declared on {1} sample sections. A duplicate HTML "
		+ "id makes document.getElementById('{0}') resolve only the FIRST element, so Table-of-Contents / "
		+ "anchor navigation silently scrolls to the wrong section. Fix: give each NnSampleSection a "
		+ "distinct Id. Suppress only if intentional via [assembly: NnDesignBypass], "
		+ "<NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled>, or "
		+ "dotnet_diagnostic.NNB047.severity. (NNB047)";

	private static readonly LocalizableString Description =
		"NnSampleSection renders its Id parameter as an HTML id (<NnPaper id=...>). The /samples Table of "
		+ "Contents and browser tests anchor on that id via document.getElementById, which returns only the "
		+ "first matching element — so two NnSampleSection sharing an Id make anchor/ToC navigation "
		+ "mis-target the first occurrence. NNB047 aggregates all static-literal NnSampleSection Id values "
		+ "across .razor markup (AdditionalFiles) and reports every section in a duplicate set. Dynamic Id "
		+ "values (Id=\"@expr\") are not statically comparable and are skipped. Scans .razor only (not "
		+ ".razor.cs / .cs / .razor.css); skips matches inside HTML (<!-- -->) and Razor (@* *@) comments. "
		+ "Unlike the consumer-policy NnDesign analyzers, NNB047 does NOT exempt NnDesignSamples/** — it "
		+ "keys on the component, which is exactly where it lives. Escape hatches: [assembly: NnDesignBypass], "
		+ "the <NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled> kill-switch, or "
		+ "dotnet_diagnostic.NNB047.severity. Severity = Error — a duplicate HTML id is a correctness "
		+ "defect. Full guidance + examples: README #nnb047.";

	/// <summary>NNB047 descriptor — Error severity, NnDesign category.</summary>
	public static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: HelpBase + "nnb047",  // lowercase anchor — GitHub slugifies
		customTags: WellKnownDiagnosticTags.CompilationEnd);

	/// <summary>
	/// Matches an <c>&lt;NnSampleSection ... Id="literal" ...&gt;</c> opening tag and captures the
	/// <c>Id="..."</c> attribute span (<c>idattr</c>) + its value (<c>id</c>). <c>[^&gt;]*?</c> stays
	/// within the tag (a negated class includes newlines but excludes <c>&gt;</c>), so a multi-line opening
	/// tag (Id + XRaySource on separate lines) is covered, yet the scan never crosses the tag's closing
	/// <c>&gt;</c>. A tag with no Id attribute simply does not match.
	/// </summary>
	private static readonly Regex SampleSectionId = new(
		@"<NnSampleSection\b[^>]*?(?<idattr>\bId\s*=\s*""(?<id>[^""]*)"")",
		RegexOptions.Compiled);

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

	private static void AnalyzeCompilation(CompilationAnalysisContext context)
	{
		// Shared kill-switch + assembly bypass with the rest of the NnDesign policy rules.
		if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
			return;
		if (HasNnDesignBypassAssemblyAttribute(context.Compilation))
			return;

		// Uniqueness is compilation-scoped: aggregate every occurrence across ALL .razor AdditionalFiles,
		// THEN report each id whose occurrence count > 1. A single CompilationAction sees every file.
		var occurrences = new Dictionary<string, List<Location>>(StringComparer.Ordinal);

		foreach (var file in context.Options.AdditionalFiles)
		{
			context.CancellationToken.ThrowIfCancellationRequested();
			CollectFromFile(context, file, occurrences);
		}

		foreach (var pair in occurrences)
		{
			var locations = pair.Value;
			if (locations.Count < 2)
				continue;

			foreach (var location in locations)
				context.ReportDiagnostic(Diagnostic.Create(Rule, location, pair.Key, locations.Count));
		}
	}

	private static void CollectFromFile(
		CompilationAnalysisContext context,
		AdditionalText file,
		Dictionary<string, List<Location>> occurrences)
	{
		var path = file.Path;
		if (string.IsNullOrEmpty(path))
			return;

		// NnSampleSection usages live in .razor markup. (.razor.cs / .cs build markup as strings;
		// .razor.css / .css carry selectors, not component tags.)
		if (!path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
			|| path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();
		var htmlComments = FindHtmlCommentRanges(text);
		var razorComments = FindRazorCommentRanges(text);

		foreach (Match m in SampleSectionId.Matches(text))
		{
			if (IsInComment(htmlComments, m.Index) || IsInComment(razorComments, m.Index))
				continue;

			var id = m.Groups["id"].Value;

			// A dynamic value (contains a Razor expression) is not statically comparable → skip.
			if (id.IndexOf('@') >= 0)
				continue;

			// An empty Id renders no html id (Blazor omits null/empty attribute values) → no collision.
			if (string.IsNullOrEmpty(id))
				continue;

			var idAttr = m.Groups["idattr"];
			var location = CreateLocation(path, sourceText, idAttr.Index, idAttr.Length);

			if (!occurrences.TryGetValue(id, out var list))
			{
				list = new List<Location>();
				occurrences[id] = list;
			}
			list.Add(location);
		}
	}

	private static Location CreateLocation(string filePath, SourceText src, int position, int length)
	{
		var start = src.Lines.GetLinePosition(position);
		var end = src.Lines.GetLinePosition(position + length);
		return Location.Create(filePath, new TextSpan(position, length), new LinePositionSpan(start, end));
	}

	private static bool IsOptedOut(AnalyzerConfigOptions options)
	{
		return options.TryGetValue("build_property.NnDesignPolicyAnalyzerEnabled", out var value)
			&& string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Returns true when the compilation's assembly carries an <c>[assembly: NnDesignBypass]</c> marker
	/// (matched by simple attribute name). Mirrors <see cref="NnDesignInlineStyleAnalyzer"/>.
	/// </summary>
	private static bool HasNnDesignBypassAssemblyAttribute(Compilation compilation)
	{
		if (compilation?.Assembly is not IAssemblySymbol assembly)
			return false;

		foreach (var attribute in assembly.GetAttributes())
		{
			if (string.Equals(attribute.AttributeClass?.Name, nameof(NnDesignBypassAttribute), StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	// ── Comment range detection (mirror NnDesignInlineStyleAnalyzer) ──────────

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
