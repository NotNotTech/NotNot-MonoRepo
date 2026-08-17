using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB050 — a producer-designated modal-content subject must not be rendered directly in the
/// matching primary <c>NnSampleSection</c>. The producer catalog owns the classification through
/// <c>NnSampleModalContentAttribute(Type)</c>; this rule only guards the direct Razor opening-tag
/// shape and does not infer trigger callbacks, dialog transactions, or geometry.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnModalSampleInliningAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic ID for direct modal-content inlining in a marked sample section.</summary>
	public const string DiagnosticId = "NNB050";

	private const string Category = "NnDesign";
	private const string CatalogMetadataName = "NotNot.BlazorDesign.NnDesign.NnSampleCatalog";
	private const string MarkerNamespace = "NotNot.BlazorDesign.NnDesign";
	private const string MarkerName = "NnSampleModalContentAttribute";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	private static readonly LocalizableString Title =
		"Modal-content subject rendered directly in its primary sample section";

	private static readonly LocalizableString MessageFormat =
		"Sample '{0}' directly renders modal-content subject <{1}>. 1) Show the committed value plus a "
		+ "trigger and move <{1}> into a companion dialog. 2) If this catalog relation is intentionally not "
		+ "modal content, remove or correct the producer marker. 3) For an intentional exception, use the "
		+ "documented NnDesign bypass or dotnet_diagnostic.NNB050.severity configuration. (NNB050)";

	private static readonly LocalizableString Description =
		"A producer-designated modal-content sample must demonstrate the launch interaction from its primary "
		+ "NnSampleSection instead of dumping the dialog subject inline. The analyzer reports only direct subject "
		+ "opening tags in a marked section; it does not prove that an arbitrary trigger opens a dialog, that "
		+ "Apply/Cancel are correct, or that the modal is expanded. Move the subject to a companion dialog and "
		+ "keep the primary section focused on committed value projection and its launch action. Use the final "
		+ "suppression tier only when the catalog classification is intentionally inapplicable.";

	/// <summary>NNB050 descriptor — Error severity, NnDesign category.</summary>
	public static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: HelpBase + "nnb050",
		customTags: WellKnownDiagnosticTags.CompilationEnd);

	private static readonly Regex SectionOpening = new(
		@"<NnSampleSection\b(?<attrs>[^>]*)>",
		RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly Regex SectionClosing = new(
		@"</NnSampleSection\s*>",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex EntryAttribute = new(
		@"\bEntry\s*=\s*(?<quote>[""'])@NnSampleCatalog\.(?<property>[A-Za-z_][A-Za-z0-9_]*)\k<quote>",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
		// Shared NnDesign escape hatches. A standard per-rule severity setting remains owned by
		// Roslyn; this analyzer only handles the shared build-property and assembly bypass.
		if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions)
			|| HasNnDesignBypassAssemblyAttribute(context.Compilation))
			return;

		foreach (var file in context.Options.AdditionalFiles)
		{
			context.CancellationToken.ThrowIfCancellationRequested();
			AnalyzeFile(context, file);
		}
	}

	private static void AnalyzeFile(CompilationAnalysisContext context, AdditionalText file)
	{
		var filePath = file.Path;
		if (string.IsNullOrEmpty(filePath)
			|| !filePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
			|| filePath.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();
		var htmlComments = FindHtmlCommentRanges(text);
		var razorComments = FindRazorCommentRanges(text);

		foreach (Match section in SectionOpening.Matches(text))
		{
			if (IsInComment(htmlComments, section.Index) || IsInComment(razorComments, section.Index))
				continue;

			var entry = EntryAttribute.Match(section.Groups["attrs"].Value);
			if (!entry.Success)
				continue;

			var propertyName = entry.Groups["property"].Value;
			if (!TryGetModalSubject(context.Compilation, propertyName, out var subjectName))
				continue;

			var contentStart = section.Index + section.Length;
			var contentEnd = FindSectionEnd(text, contentStart, htmlComments, razorComments);
			if (contentEnd <= contentStart)
				continue;

			AnalyzeSection(
				context,
				filePath,
				sourceText,
				text,
				contentStart,
				contentEnd,
				subjectName,
				propertyName,
				htmlComments,
				razorComments);
		}
	}

	private static void AnalyzeSection(
		CompilationAnalysisContext context,
		string filePath,
		SourceText sourceText,
		string text,
		int contentStart,
		int contentEnd,
		string subjectName,
		string propertyName,
		List<(int Start, int End)> htmlComments,
		List<(int Start, int End)> razorComments)
	{
		var subjectOpening = new Regex(
			$"<{Regex.Escape(subjectName)}\\b[^>]*>",
			RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

		foreach (Match subject in subjectOpening.Matches(text, contentStart))
		{
			if (subject.Index >= contentEnd)
				break;
			if (IsInComment(htmlComments, subject.Index) || IsInComment(razorComments, subject.Index))
				continue;

			var location = CreateLocation(filePath, sourceText, subject.Index, subject.Length);
			context.ReportDiagnostic(Diagnostic.Create(Rule, location, propertyName, subjectName));
		}
	}

	private static int FindSectionEnd(
		string text,
		int contentStart,
		List<(int Start, int End)> htmlComments,
		List<(int Start, int End)> razorComments)
	{
		foreach (Match closing in SectionClosing.Matches(text, contentStart))
		{
			if (!IsInComment(htmlComments, closing.Index) && !IsInComment(razorComments, closing.Index))
				return closing.Index;
		}

		return -1;
	}

	private static bool TryGetModalSubject(Compilation compilation, string propertyName, out string subjectName)
	{
		subjectName = string.Empty;
		var catalog = compilation.GetTypeByMetadataName(CatalogMetadataName);
		if (catalog == null)
			return false;

		foreach (var member in catalog.GetMembers(propertyName))
		{
			if (member is not IPropertySymbol property)
				continue;

			foreach (var attribute in property.GetAttributes())
			{
				var attributeClass = attribute.AttributeClass;
				if (attributeClass == null
					|| !string.Equals(attributeClass.Name, MarkerName, StringComparison.Ordinal)
					|| !string.Equals(
						attributeClass.ContainingNamespace?.ToDisplayString(),
						MarkerNamespace,
						StringComparison.Ordinal)
					|| attribute.ConstructorArguments.Length != 1)
					continue;

				var subjectArgument = attribute.ConstructorArguments[0];
				if (subjectArgument.Kind != TypedConstantKind.Type
					|| subjectArgument.Value is not INamedTypeSymbol subjectType)
					continue;

				subjectName = subjectType.Name;
				return !string.IsNullOrEmpty(subjectName);
			}
		}

		return false;
	}

	private static Location CreateLocation(string filePath, SourceText sourceText, int position, int length)
	{
		var start = sourceText.Lines.GetLinePosition(position);
		var end = sourceText.Lines.GetLinePosition(position + length);
		return Location.Create(
			filePath,
			new TextSpan(position, length),
			new LinePositionSpan(start, end));
	}

	private static bool IsOptedOut(AnalyzerConfigOptions options)
	{
		return options.TryGetValue("build_property.NnDesignPolicyAnalyzerEnabled", out var value)
			&& string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
	}

	private static bool HasNnDesignBypassAssemblyAttribute(Compilation compilation)
	{
		foreach (var attribute in compilation.Assembly.GetAttributes())
		{
			if (string.Equals(attribute.AttributeClass?.Name, nameof(NnDesignBypassAttribute), StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	private static List<(int Start, int End)> FindHtmlCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		var index = 0;
		while (index < text.Length - 3)
		{
			if (text[index] == '<' && text[index + 1] == '!' && text[index + 2] == '-' && text[index + 3] == '-')
			{
				var start = index;
				index += 4;
				while (index < text.Length - 2)
				{
					if (text[index] == '-' && text[index + 1] == '-' && text[index + 2] == '>')
					{
						index += 3;
						break;
					}
					index++;
				}
				ranges.Add((start, index));
			}
			else
			{
				index++;
			}
		}

		return ranges;
	}

	private static List<(int Start, int End)> FindRazorCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		var index = 0;
		while (index < text.Length - 1)
		{
			if (text[index] == '@' && text[index + 1] == '*')
			{
				var start = index;
				index += 2;
				while (index < text.Length - 1)
				{
					if (text[index] == '*' && text[index + 1] == '@')
					{
						index += 2;
						break;
					}
					index++;
				}
				ranges.Add((start, index));
			}
			else
			{
				index++;
			}
		}

		return ranges;
	}

	private static bool IsInComment(List<(int Start, int End)> ranges, int position)
	{
		foreach (var (start, end) in ranges)
		{
			if (position >= start && position < end)
				return true;
			if (start > position)
				break;
		}

		return false;
	}
}
