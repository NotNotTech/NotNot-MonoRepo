using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB022 — End-using applications must not reference MudBlazor directly: use Nn* wrappers from
/// <c>NotNot.BlazorDesign.NnDesign</c>. Hybrid analyzer with two pathways unified under a single
/// <see cref="DiagnosticDescriptor"/>:
/// <list type="number">
///   <item>Text-scan pathway (<c>RegisterCompilationAction</c>) — operates on <c>.razor</c> and
///         <c>.razor.cs</c> files registered as AdditionalFiles by the package's <c>.props</c>.</item>
///   <item>Semantic pathway (<c>RegisterSyntaxNodeAction</c>) — operates on plain <c>.cs</c> files
///         via Roslyn syntax + symbol resolution; flags <c>using MudBlazor;</c> directives and
///         identifier references whose containing namespace starts with <c>MudBlazor</c>.</item>
/// </list>
/// Both pathways share the <see cref="IsExceptedPath"/> helper for path-bucket exemptions and
/// honor the per-file <c>nnb022:allow-mudblazor</c> opt-out marker. Severity = Error (the consumer is
/// verified <c>Mud*</c>-clean modulo the enumerated carve-outs, so any new direct reference breaks the build).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnDesignMudBlazorPolicyAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic ID for direct MudBlazor references.</summary>
	public const string DiagnosticId = "NNB022";

	private const string Category = "BannedComponents";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	private static readonly LocalizableString Title =
		"Direct MudBlazor reference forbidden — use Nn* wrapper from NotNot.BlazorDesign";

	private static readonly LocalizableString MessageFormat =
		"End-using applications must not reference MudBlazor directly: '{0}' is a primitive component, "
		+ "not a brand-language component. Use the Nn* wrapper from NotNot.BlazorDesign.NnDesign. "
		+ "If no Nn* equivalent exists, file an NnDesign-gap — do NOT reach into MudBlazor at the call site. "
		+ "ONE TRUE WAY per UX concern. (NNB022)";

	private static readonly LocalizableString Description =
		"MudBlazor is the general-purpose primitive layer wrapped internally by NnDesign. "
		+ "Consumer Blazor projects compose UI exclusively from Nn* components — direct Mud* references "
		+ "in consumer code force every call site to repeat brand boilerplate, multiply places where the "
		+ "brand drifts, and create N-1 places where a single bug fix must be repeated. "
		+ "Exceptions (path-based allow-list): NotNot.BlazorDesign internals; root provider wiring "
		+ "(App.razor, Program.cs, VowMudLocalizer.cs); NnDesignSamples/** pages; Pages/Samples/** "
		+ "consumer sample/demo pages; explicit dev/test/legacy "
		+ "harness pages (BlazorTermTestHarness, BlazorTermHarmonizedHarness, BlazorTermDiffTest, "
		+ "BlazorTermTabTest, BlazorTermTest, HarnessDummyTab, HarnessTerminalWrapper, RichEditExamplePage, "
		+ "NnDesignSamplesPage, DashboardLegacy, TestInputs). Per-file opt-out for documented call-site "
		+ "cases: @* nnb022:allow-mudblazor: <reason> *@ (Razor) or // nnb022:allow-mudblazor: <reason> (C#). "
		+ "Assembly-level opt-out for sibling primitive layers and the NnDesign wrapper layer itself: apply "
		+ "[assembly: NotNot.BlazorAnalyzers.NnDesign.NnDesignBypass] — the analyzer matches by simple "
		+ "attribute name, so consumer assemblies may either reference NotNot.BlazorAnalyzers types directly "
		+ "or declare a local internal copy of NnDesignBypassAttribute with the same name. Use when the "
		+ "entire assembly is intentionally exempt (e.g., NotNot.BlazorComponents is a sibling primitive "
		+ "layer alongside MudBlazor — direct MudBlazor consumption is intentional architecture because "
		+ "the assembly does NOT reference NotNot.BlazorDesign). See NnDesignBypassAttribute XmlDoc for "
		+ "guidance on choosing among the four exemption mechanisms. "
		+ "Authority: NotNot.BlazorDesign/AGENTS.md → Consumer Policy; protocols/nndesign.md ENFORCEMENT.";

	/// <summary>NNB022 descriptor — Error severity, BannedComponents category.</summary>
	public static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		Title,
		MessageFormat,
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: HelpBase + "nnb022",  // lowercase anchor — GitHub slugifies (SME M3)
		customTags: WellKnownDiagnosticTags.CompilationEnd);

	// ── Regex patterns ────────────────────────────────────────────────────

	/// <summary>Markup detection — <c>.razor</c> only. NO <c>I?</c> prefix (interface names don't appear as markup).</summary>
	private static readonly Regex MudTagRegex = new(
		@"<Mud[A-Z]\w*",
		RegexOptions.Compiled);

	/// <summary>C# identifier detection — <c>.razor.cs</c> only. INCLUDES <c>I?</c> for IMud* interface names.</summary>
	private static readonly Regex MudIdentifierRegex = new(
		@"\b(I?Mud[A-Z]\w*)\b",
		RegexOptions.Compiled);

	/// <summary>Razor <c>@using</c> directive — line-anchored.</summary>
	private static readonly Regex RazorUsingMudBlazorRegex = new(
		@"^\s*@using\s+MudBlazor\s*;?\s*$",
		RegexOptions.Compiled | RegexOptions.Multiline);

	/// <summary>C# <c>using</c> directive — line-anchored to bare <c>using MudBlazor;</c>.</summary>
	private static readonly Regex CsUsingMudBlazorRegex = new(
		@"^\s*using\s+MudBlazor\s*;",
		RegexOptions.Compiled | RegexOptions.Multiline);

	// ── Exception buckets ────────────────────────────────────────────────

	/// <summary>File-name allow-list (per SME M5 — explicit names, not patterns).</summary>
	private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
	{
		// Root provider wiring
		"Program.cs",
		"App.razor",
		"VowMudLocalizer.cs",

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

		// Text-scan pathway (.razor + .razor.cs via AdditionalFiles)
		context.RegisterCompilationAction(AnalyzeCompilation);

		// Semantic pathway (plain .cs via SyntaxTree)
		context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
		context.RegisterSyntaxNodeAction(AnalyzeIdentifierName, SyntaxKind.IdentifierName);
	}

	// ── Text-scan pathway ─────────────────────────────────────────────────

	private static void AnalyzeCompilation(CompilationAnalysisContext context)
	{
		if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
			return;

		// Assembly-level opt-out — sibling primitive layers (NotNot.BlazorComponents) and the wrapper
		// layer itself (NotNot.BlazorDesign) declare [assembly: NnDesignBypass] to exempt the whole
		// compilation. Matches by simple attribute name; consumer may declare a local internal copy.
		if (HasNnDesignBypassAssemblyAttribute(context.Compilation))
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

		// File-extension dispatch
		var isRazorCs = path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);
		var isRazor = !isRazorCs
			&& !path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase)
			&& path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

		if (!isRazor && !isRazorCs)
			return;

		// Path-bucket exception
		if (IsExceptedPath(path))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();

		// Per-file opt-out marker
		if (HasOptOutComment(text))
			return;

		if (isRazor)
		{
			AnalyzeRazorText(context, file.Path, sourceText, text);
		}
		else
		{
			AnalyzeRazorCsText(context, file.Path, sourceText, text);
		}
	}

	private static void AnalyzeRazorText(
		CompilationAnalysisContext context, string filePath, SourceText sourceText, string text)
	{
		var htmlComments = FindHtmlCommentRanges(text);
		var razorComments = FindRazorCommentRanges(text);

		// Markup tag detection — `<Mud[A-Z]\w*`
		foreach (Match match in MudTagRegex.Matches(text))
		{
			if (IsInComment(htmlComments, match.Index) || IsInComment(razorComments, match.Index))
				continue;

			// Skip false-positive: a `<MudXxx` that's actually inside a string literal in @code is rare
			// and not worth the cost of body-aware scanning here. Identifier text after `<` is the type name.
			var displayName = match.Value.TrimStart('<');
			Report(context, filePath, sourceText, match.Index, match.Length, displayName);
		}

		// `@using MudBlazor;` detection — only report if file also contains a markup or body Mud* reference
		// (avoids flagging bare imports of MudBlazor namespace in transient pages).
		foreach (Match usingMatch in RazorUsingMudBlazorRegex.Matches(text))
		{
			if (IsInComment(htmlComments, usingMatch.Index) || IsInComment(razorComments, usingMatch.Index))
				continue;

			// Co-occurrence guard: only report if file contains another Mud* reference outside this line.
			// Practical check: find any `<Mud[A-Z]` (already reported above) OR any body `\bMud[A-Z]\w*\b`
			// outside the @using line itself.
			if (FileContainsMudReferenceOutside(text, usingMatch.Index, usingMatch.Length))
			{
				Report(context, filePath, sourceText, usingMatch.Index, usingMatch.Length, "@using MudBlazor");
			}
		}
	}

	private static void AnalyzeRazorCsText(
		CompilationAnalysisContext context, string filePath, SourceText sourceText, string text)
	{
		var csComments = FindCSharpCommentRanges(text);

		// `using MudBlazor;` directive — strict line-anchored. Always reported (no co-occurrence guard for
		// .razor.cs partial-class consumers — the using indicates feature consumption per Q-Impl-1).
		var usingMatches = new List<Match>();
		foreach (Match m in CsUsingMudBlazorRegex.Matches(text))
		{
			if (IsInComment(csComments, m.Index))
				continue;
			usingMatches.Add(m);
			Report(context, filePath, sourceText, m.Index, m.Length, "using MudBlazor");
		}

		// Body identifier scan — `\b(I?Mud[A-Z]\w*)\b`. Skip:
		// (a) matches inside C# comments
		// (b) matches inside the already-reported using lines
		// (c) the bare token "MudBlazor" itself (it's the namespace, not a type — captured because the regex
		//     matches M-u-d, then 'B' uppercase, then `lazor`).
		foreach (Match match in MudIdentifierRegex.Matches(text))
		{
			if (IsInComment(csComments, match.Index))
				continue;
			if (match.Value == "MudBlazor")
				continue;
			if (IsWithinAnyMatch(usingMatches, match.Index))
				continue;

			Report(context, filePath, sourceText, match.Index, match.Length, match.Value);
		}
	}

	// ── Semantic pathway ──────────────────────────────────────────────────

	private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
	{
		if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
			return;

		// Assembly-level opt-out — short-circuits before per-file path filtering.
		if (HasNnDesignBypassAssemblyAttribute(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;

		// Skip .razor.cs files (text-scan pathway handles them).
		if (syntaxTreePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
			return;
		// Skip .razor files generated to .g.cs — Razor-generated code has unrelated .g.cs syntax trees;
		// avoid double-reporting on text-scan ground truth.
		if (syntaxTreePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
			return;

		if (IsExceptedPath(syntaxTreePath))
			return;

		var node = (UsingDirectiveSyntax)context.Node;
		if (node.Name == null)
			return;

		// Resolve the namespace via SemanticModel
		var symbolInfo = context.SemanticModel.GetSymbolInfo(node.Name, context.CancellationToken);
		var nsSymbol = symbolInfo.Symbol as INamespaceSymbol
			?? symbolInfo.CandidateSymbols.OfType<INamespaceSymbol>().FirstOrDefault();

		if (nsSymbol == null)
			return;

		if (!IsMudBlazorNamespace(nsSymbol))
			return;

		// Per-file opt-out (whole-source scan)
		if (HasOptOutCommentInSource(context.Node.SyntaxTree))
			return;

		var displayText = node.Name.ToString();
		context.ReportDiagnostic(Diagnostic.Create(
			Rule, node.GetLocation(), $"using {displayText}"));
	}

	private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context)
	{
		if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
			return;

		// Assembly-level opt-out — short-circuits before per-file path filtering.
		if (HasNnDesignBypassAssemblyAttribute(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
		if (syntaxTreePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
			return;
		if (syntaxTreePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
			return;

		if (IsExceptedPath(syntaxTreePath))
			return;

		var node = (IdentifierNameSyntax)context.Node;
		var name = node.Identifier.ValueText;

		// Skip identifiers within UsingDirectiveSyntax — already handled by AnalyzeUsingDirective.
		if (IsWithinUsingDirective(node))
			return;

		// Skip declaration sites (namespace/type/method declarations declaring an entity, NOT consuming
		// a MudBlazor type). Examples: `namespace MudBlazor { ... }` declares the MudBlazor namespace.
		if (IsDeclarationSite(node))
			return;

		// Cheap structural pre-filter: skip if NOT under a NameSyntax / TypeSyntax / MemberAccess context.
		// IdentifierName fires on millions of tokens; restrict to type/namespace/member-access positions.
		if (!IsLikelyTypeOrNamespaceReference(node))
			return;

		// Symbol resolution: deliberate Q1.B trade-off — closing the plain-`.cs` consumer-code gap (per the
		// rev2 plan §6) requires resolving every IdentifierName that survives the structural pre-filter to its
		// containing namespace. The structural pre-filter (IsLikelyTypeOrNamespaceReference) eliminates the
		// hot path of local-variable / member-name reads; what remains is the type-position / namespace-position
		// subset where SemanticModel.GetSymbolInfo is justified. PEER_REVIEW H-1 (perf concern) accepted this
		// as the documented cost-of-correctness. Future optimization: short-circuit on identifier text starting
		// with anything but 'M' / 'I' before the symbol resolve, if profiling shows hotspot pressure.
		var symbolInfo = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken);
		var symbol = symbolInfo.Symbol
			?? symbolInfo.CandidateSymbols.FirstOrDefault();

		if (symbol == null)
			return;

		// Type symbol or namespace symbol — check containing namespace chain.
		var ns = symbol switch
		{
			INamespaceSymbol nsSym => nsSym,
			ITypeSymbol typeSym => typeSym.ContainingNamespace,
			_ => symbol.ContainingNamespace,
		};
		if (ns == null)
			return;

		if (!IsMudBlazorNamespace(ns))
			return;

		// Per-file opt-out
		if (HasOptOutCommentInSource(context.Node.SyntaxTree))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), name));
	}

	// ── Shared helpers ────────────────────────────────────────────────────

	private static bool IsExceptedPath(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return false;

		var p = filePath.Replace('\\', '/');

		// Folder-pattern exceptions (case-insensitive Contains)
		if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0 ||
			p.IndexOf("/NotNot.BlazorDesign.Desktop/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		// Consumer sample/demo pages under Pages/Samples/** — pedagogical / side-by-side surface that
		// MAY reference Mud* primitives directly (same carve-out the NN_RM_001 / NN_ABMCS_* suites apply).
		if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		// File-name allow-list
		var fileName = Path.GetFileName(p);
		return AllowedFileNames.Contains(fileName);
	}

	private static bool HasOptOutComment(string fileText)
	{
		if (string.IsNullOrEmpty(fileText))
			return false;
		return fileText.IndexOf("nnb022:allow-mudblazor", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool HasOptOutCommentInSource(SyntaxTree tree)
	{
		var src = tree.GetText().ToString();
		return HasOptOutComment(src);
	}

	private static bool IsOptedOut(AnalyzerConfigOptions options)
	{
		return options.TryGetValue("build_property.NnDesignPolicyAnalyzerEnabled", out var value)
			&& string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Returns true when the compilation's assembly carries an
	/// <c>[assembly: NnDesignBypass]</c> marker (matched by simple attribute name).
	/// Match is by <c>AttributeClass.Name</c> only — consumer assemblies may either
	/// reference <see cref="NnDesignBypassAttribute"/> directly or declare a local
	/// <c>internal sealed class NnDesignBypassAttribute : Attribute</c> with the same
	/// name; both are treated equivalently. This avoids forcing the analyzer's types
	/// to be exposed via <c>ReferenceOutputAssembly="true"</c> on the consumer's
	/// analyzer-only ProjectReference. Cost: O(N) over assembly attributes (typically N&lt;10).
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

	private static bool IsMudBlazorNamespace(INamespaceSymbol ns)
	{
		// Walk to root namespace; require root segment to equal "MudBlazor".
		var current = ns;
		string? rootName = null;
		while (current != null && !current.IsGlobalNamespace)
		{
			rootName = current.Name;
			current = current.ContainingNamespace;
		}
		return string.Equals(rootName, "MudBlazor", StringComparison.Ordinal);
	}

	private static bool IsWithinUsingDirective(SyntaxNode node)
	{
		var parent = node.Parent;
		while (parent != null)
		{
			if (parent is UsingDirectiveSyntax)
				return true;
			parent = parent.Parent;
		}
		return false;
	}

	/// <summary>
	/// Returns true if the identifier node is part of a DECLARATION site rather than a usage:
	/// namespace declarations, type declarations (class/struct/enum/interface), method declarations,
	/// parameter declarations, etc. Declaration-site identifiers introduce names — they don't consume
	/// MudBlazor types.
	/// </summary>
	private static bool IsDeclarationSite(IdentifierNameSyntax node)
	{
		// NamespaceDeclarationSyntax.Name — `namespace MudBlazor { ... }`, `namespace MudBlazor.Services { ... }`,
		// or `namespace MudBlazor;` (file-scoped). Walk up the qualified-name chain regardless of Left/Right
		// position so that multi-segment namespace declarations correctly anchor BOTH the LEFT-side identifier
		// (e.g. `MudBlazor` in `MudBlazor.Services`) AND the RIGHT-side identifier (`Services`) to the outer
		// NamespaceDeclarationSyntax check below. Restricting to `qns.Right == current` would leave the LEFT
		// identifier stuck mid-chain — its parent QNS would never reach the namespace decl, returning false
		// and causing a spurious NNB022 fire on the LEFT segment of multi-segment namespace declarations.
		SyntaxNode current = node;
		while (current.Parent is QualifiedNameSyntax qns)
		{
			current = qns;
		}
		// `current` is now the top of any qualified-name chain (or the original node).
		var parent = current.Parent;
		if (parent is BaseNamespaceDeclarationSyntax nsDecl && nsDecl.Name == current)
			return true;
		return false;
	}

	/// <summary>
	/// Cheap structural pre-filter: returns true if the identifier appears in a position likely to
	/// reference a type or namespace (member access target, qualified-name component, type-syntax position
	/// like return type / parameter / cast / typeof / nameof). Filters out the millions of local-variable
	/// reads that <c>IdentifierName</c> fires on every other token.
	/// </summary>
	private static bool IsLikelyTypeOrNamespaceReference(IdentifierNameSyntax node)
	{
		var parent = node.Parent;

		// SKIP inner positions of compound names — report only on the OUTERMOST identifier
		// to avoid noise (e.g. `Color.Primary` produces ONE diagnostic on `Color`, not two).
		// (a) `MudBlazor.Color` qualified-name: `Color` IdentifierName has parent QualifiedNameSyntax
		//     with `Right == node`. The OUTER name (`MudBlazor.Color`) is itself a QualifiedNameSyntax —
		//     it's not an IdentifierNameSyntax, so the outer node won't fire IdentifierName actions.
		//     But `MudBlazor` (Left) WILL fire — and that's the anchor we report.
		// (b) `Color.Primary` member-access: `Primary` IdentifierName has parent MemberAccessExpression
		//     with `Name == node`. The `Color` (Expression) WILL fire — that's our anchor.
		if (parent is QualifiedNameSyntax qns && qns.Right == node)
			return false;
		if (parent is MemberAccessExpressionSyntax memberAccessRight && memberAccessRight.Name == node)
			return false;

		// TypeSyntax base catches QualifiedNameSyntax (Left position is reached above), ArrayTypeSyntax,
		// GenericNameSyntax, NullableTypeSyntax, etc. — any name/type position. Use a single TypeSyntax
		// check first.
		if (parent is TypeSyntax)
			return true;
		switch (parent)
		{
			// `MudBlazor.Color.Primary` (member access — Expression position only, Name handled above)
			case MemberAccessExpressionSyntax mae when mae.Expression == node:
			// Type-arg list `Foo<MudBlazor.Color>`
			case TypeArgumentListSyntax:
			// Object creation: `new MudColor(...)`
			case ObjectCreationExpressionSyntax oce when oce.Type == node:
			// typeof / nameof / cast
			case TypeOfExpressionSyntax:
			case CastExpressionSyntax cast when cast.Type == node:
			// using directive (already filtered above, but defensive)
			case UsingDirectiveSyntax:
			// Method/property declarations using a return-type identifier
			case MethodDeclarationSyntax md when md.ReturnType == node:
			case PropertyDeclarationSyntax pd when pd.Type == node:
			case FieldDeclarationSyntax:
			case VariableDeclarationSyntax vd when vd.Type == node:
			case ParameterSyntax ps when ps.Type == node:
				return true;
			default:
				return false;
		}
	}

	private static bool IsWithinAnyMatch(List<Match> matches, int position)
	{
		foreach (var m in matches)
		{
			if (position >= m.Index && position < m.Index + m.Length)
				return true;
		}
		return false;
	}

	private static bool FileContainsMudReferenceOutside(string text, int excludeStart, int excludeLength)
	{
		var excludeEnd = excludeStart + excludeLength;
		// Look for `<Mud[A-Z]` markup — anywhere outside the excluded range.
		foreach (Match m in MudTagRegex.Matches(text))
		{
			if (m.Index < excludeStart || m.Index >= excludeEnd)
				return true;
		}
		// Look for body identifier — bare `Mud[A-Z]\w*` or `IMud[A-Z]\w*` outside the range AND not "MudBlazor".
		foreach (Match m in MudIdentifierRegex.Matches(text))
		{
			if (m.Value == "MudBlazor")
				continue;
			if (m.Index < excludeStart || m.Index >= excludeEnd)
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

	// ── Comment range detection ───────────────────────────────────────────

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

	/// <summary>
	/// Builds sorted list of C# comment ranges: <c>// ... \n</c>, <c>/* ... */</c>.
	/// String literals (regular, verbatim, interpolated) are skipped to avoid false-matching
	/// comment markers that appear inside strings.
	/// </summary>
	private static List<(int Start, int End)> FindCSharpCommentRanges(string text)
	{
		var ranges = new List<(int Start, int End)>();
		var i = 0;
		while (i < text.Length)
		{
			var ch = text[i];

			// Verbatim string literal: @"..."
			if (ch == '@' && i + 1 < text.Length && text[i + 1] == '"')
			{
				i += 2;
				while (i < text.Length)
				{
					if (text[i] == '"')
					{
						// Doubled "" inside verbatim string = escaped quote, continue.
						if (i + 1 < text.Length && text[i + 1] == '"')
						{
							i += 2;
							continue;
						}
						i++;
						break;
					}
					i++;
				}
				continue;
			}

			// Regular or interpolated string literal: "..." or $"..."
			if (ch == '"' || (ch == '$' && i + 1 < text.Length && text[i + 1] == '"'))
			{
				if (ch == '$') i++; // skip $
				i++; // skip opening "
				while (i < text.Length)
				{
					if (text[i] == '\\' && i + 1 < text.Length)
					{
						i += 2;
						continue;
					}
					if (text[i] == '"')
					{
						i++;
						break;
					}
					if (text[i] == '\n')
					{
						break; // unterminated string at EOL
					}
					i++;
				}
				continue;
			}

			// Char literal: '...'
			if (ch == '\'')
			{
				i++;
				while (i < text.Length)
				{
					if (text[i] == '\\' && i + 1 < text.Length)
					{
						i += 2;
						continue;
					}
					if (text[i] == '\'')
					{
						i++;
						break;
					}
					i++;
				}
				continue;
			}

			// Single-line comment: // ... \n
			if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
			{
				var start = i;
				i += 2;
				while (i < text.Length && text[i] != '\n')
					i++;
				ranges.Add((start, i));
				continue;
			}

			// Block comment: /* ... */
			if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*')
			{
				var start = i;
				i += 2;
				while (i < text.Length - 1)
				{
					if (text[i] == '*' && text[i + 1] == '/')
					{
						i += 2;
						break;
					}
					i++;
				}
				ranges.Add((start, i));
				continue;
			}

			i++;
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
