using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.KeyConventions;

/// <summary>
/// Detects @key directives in Razor files that use member-access expressions not ending
/// in a recognized identity property (.Id, .Key, .Guid, .UniqueId).
/// <para>
/// In Blazor, @key values must be unique among sibling elements. Using non-identity
/// properties (e.g., .CursorOffset, .Index) risks duplicate keys when the collection
/// contains items from multiple sources or when the property isn't a true unique identifier.
/// This causes <c>InvalidOperationException: More than one sibling has the same key value</c>
/// at render time.
/// </para>
/// <para>
/// The fix is to use a property that is guaranteed unique per item (.Id) or an explicit
/// composite key expression via @key="@($"...")" which is exempt from this rule.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ForeachKeyConventionAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "KeyConventions";
	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	// ── Identity property names (case-insensitive) ──────────────────────

	private static readonly HashSet<string> IdentityPropertyNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"Id", "Key", "Guid", "UniqueId", "UniqueKey", "Identifier"
	};

	// ── Diagnostic Descriptor ───────────────────────────────────────────

	/// <summary>NNB040: @key should use an identity property (.Id) or explicit composite key.</summary>
	public static readonly DiagnosticDescriptor RuleKeyNotIdentity = new(
		"NNB040",
		"@key should use an identity property (.Id) or explicit composite key",
		"@key uses '.{0}' which may not be unique across sibling elements. "
			+ "Use a property like .Id or an explicit composite key @key=\"@($\"...\")\" to prevent duplicate key crashes. (NNB040).",
		Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
		description: "Blazor @key values must be unique among sibling elements. Using non-identity "
			+ "properties (e.g., .CursorOffset, .Index, .Offset) risks duplicate keys when items come "
			+ "from multiple sources. Use .Id, .Key, .Guid, or build an explicit composite key with "
			+ "@key=\"@($\"{item.Id}_{item.Source}\")\".",
		helpLinkUri: HelpBase + "NNB040");

	// ── DiagnosticAnalyzer overrides ────────────────────────────────────

	/// <summary>The single NNB040 rule (non-identity <c>@key</c> value) this analyzer reports.</summary>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(RuleKeyNotIdentity);

	/// <summary>Registers a compilation-end action that scans <c>.razor</c> <c>AdditionalText</c> files
	/// for <c>@key</c> attributes bound to non-identity member accesses.</summary>
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

		// Only scan .razor files (not .razor.css)
		if (path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase))
			return;
		if (!path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();

		var htmlComments = FindHtmlCommentRanges(text);
		var razorComments = FindRazorCommentRanges(text);

		CheckKeyDirectives(context, file, sourceText, text, htmlComments, razorComments);
	}

	// ── Detection logic ─────────────────────────────────────────────────

	private static void CheckKeyDirectives(
		CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
		List<(int Start, int End)> htmlComments, List<(int Start, int End)> razorComments)
	{
		const string marker = "@key=\"";
		int pos = 0;

		while ((pos = text.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
		{
			// Skip matches inside comments
			if (IsInComment(htmlComments, pos) || IsInComment(razorComments, pos))
			{
				pos += marker.Length;
				continue;
			}

			int valueStart = pos + marker.Length;
			int valueEnd = FindClosingQuote(text, valueStart);
			if (valueEnd < 0)
			{
				pos += marker.Length;
				continue;
			}

			var keyExpr = text.Substring(valueStart, valueEnd - valueStart).Trim();

			// Skip interpolated string expressions: @($"...") or @($@"...") — intentional composite keys
			if (keyExpr.StartsWith("@($", StringComparison.Ordinal) ||
				keyExpr.StartsWith("@\"", StringComparison.Ordinal))
			{
				pos = valueEnd + 1;
				continue;
			}

			// For simple @(expr) — unwrap and analyze the inner expression
			if (keyExpr.StartsWith("@(", StringComparison.Ordinal) && keyExpr.EndsWith(")", StringComparison.Ordinal))
			{
				keyExpr = keyExpr.Substring(2, keyExpr.Length - 3);
			}

			// Analyze the key expression for non-identity member access
			var nonIdentityProp = FindNonIdentityProperty(keyExpr);
			if (nonIdentityProp != null)
			{
				Report(ctx, file.Path, src, pos, valueEnd - pos + 1, RuleKeyNotIdentity, nonIdentityProp);
			}

			pos = valueEnd + 1;
		}
	}

	/// <summary>
	/// Extracts the last property name from a member-access expression and checks
	/// if it's a recognized identity property.
	/// Returns the non-identity property name, or null if the expression is acceptable.
	/// </summary>
	private static string? FindNonIdentityProperty(string expr)
	{
		// Simple identifier (no dot) — likely a field/variable, not a member access chain
		if (expr.IndexOf('.') < 0)
			return null;

		// Strip leading @ if present (Razor expression shorthand)
		if (expr.StartsWith("@", StringComparison.Ordinal))
			expr = expr.Substring(1);

		// Find the last property in the chain: "evt.Computed.CursorOffset" → "CursorOffset"
		// Handle indexer access: "group.Events[0].Computed.CursorOffset" → "CursorOffset"
		var lastDot = expr.LastIndexOf('.');
		if (lastDot < 0 || lastDot >= expr.Length - 1)
			return null;

		var lastProp = expr.Substring(lastDot + 1).Trim();

		// Strip trailing parentheses for method calls like .GetHashCode()
		var parenIdx = lastProp.IndexOf('(');
		if (parenIdx >= 0)
		{
			// Method call terminal — not a property access. The rule targets accidental
			// non-identity *properties* (e.g., .CursorOffset), not method calls on identity
			// properties (e.g., .Id.ToString()). Skip method call terminals.
			return null;
		}

		if (string.IsNullOrEmpty(lastProp))
			return null;

		// Check against identity property names
		if (IdentityPropertyNames.Contains(lastProp))
			return null;

		return lastProp;
	}

	/// <summary>
	/// Finds the closing quote for an attribute value, handling nested Razor expressions.
	/// Tracks parenthesis depth for @(...) and brace depth for @{...}.
	/// </summary>
	private static int FindClosingQuote(string text, int start)
	{
		int depth = 0;
		bool inRazorExpr = false;

		for (int i = start; i < text.Length; i++)
		{
			char c = text[i];

			if (inRazorExpr)
			{
				if (c == '(' || c == '{') depth++;
				else if (c == ')' || c == '}')
				{
					depth--;
					if (depth <= 0) inRazorExpr = false;
				}
				continue;
			}

			if (c == '@' && i + 1 < text.Length && (text[i + 1] == '(' || text[i + 1] == '{'))
			{
				inRazorExpr = true;
				depth = 1;
				i++; // skip the opening paren/brace
				continue;
			}

			if (c == '"')
				return i;
		}

		return -1;
	}

	// ── Helpers (shared pattern from BannedMudButtonAnalyzer) ────────────

	private static bool IsOptedOut(CompilationAnalysisContext context)
	{
		return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
				   .TryGetValue("build_property.KeyConventionAnalyzerEnabled", out var value) &&
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
