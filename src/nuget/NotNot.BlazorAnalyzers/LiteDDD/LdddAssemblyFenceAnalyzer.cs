using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.LiteDDD;

/// <summary>
/// LiteDDD Wave 1 — assembly-fence rules <c>NN_LDDD_001</c>, <c>NN_LDDD_002</c>, <c>NN_LDDD_004</c>.
/// Hybrid analyzer with three detection pathways unified under one analyzer class (parallels
/// <see cref="NnDesign.NnDesignMudBlazorPolicyAnalyzer"/>):
/// <list type="number">
///   <item><description>
///     <b>NN_LDDD_001</b> — Server-assembly type referenced in Shared/Client code.
///     <c>RegisterSyntaxNodeAction</c> on <see cref="SyntaxKind.IdentifierName"/> + semantic
///     symbol resolution → walk to <see cref="ITypeSymbol.ContainingAssembly"/> → check
///     <c>.Server</c> suffix via
///     <see cref="LdddAnalyzerHelpers.IsServerAssembly(IAssemblySymbol)"/>.
///   </description></item>
///   <item><description>
///     <b>NN_LDDD_002</b> — Server-namespace using directive in Shared/Client code. Hybrid:
///     text-scan over <c>.razor</c>/<c>.razor.cs</c> AdditionalFiles + semantic resolution on
///     <see cref="SyntaxKind.UsingDirective"/> for plain <c>.cs</c>.
///   </description></item>
///   <item><description>
///     <b>NN_LDDD_004</b> — Entity Framework <c>DbContext</c> referenced in Shared/Client code.
///     <see cref="SymbolKind.Field"/> / <see cref="SymbolKind.Property"/> /
///     <see cref="SyntaxKind.Parameter"/> with full-name match against
///     <c>Microsoft.EntityFrameworkCore.DbContext</c>.
///   </description></item>
/// </list>
/// All three rules gate on the current compilation's assembly identity ending in <c>.Shared</c>
/// or <c>.Client</c> (<see cref="LdddAnalyzerHelpers.IsSharedOrClientAssembly(IAssemblySymbol)"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bypass mechanisms</b> (use the smallest mechanism that fits):
/// <list type="bullet">
///   <item><description>
///     <c>[assembly: LdddBypass]</c> — assembly-level short-circuit for sibling primitive
///     layers (<c>NotNot.BlazorComponents</c>, <c>NotNot.BlazorDesign</c>) or carve-out cases.
///   </description></item>
///   <item><description>
///     <c>Pages/Samples/**</c> — pedagogical samples exempt by AGENTS.md policy.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Warning"/> — escalates to error via
/// <c>.editorconfig</c> in CI builds.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LdddAssemblyFenceAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "LiteDDD.AssemblyFence";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	// ── NN_LDDD_001 — Server-assembly type referenced in Shared/Client code ─────

	/// <summary>Diagnostic ID for server-assembly type references in Shared/Client code.</summary>
	public const string LDDD001_DiagnosticId = "NN_LDDD_001";

	private static readonly LocalizableString LDDD001_Title =
		"Server-assembly type referenced in Shared/Client code";

	private static readonly LocalizableString LDDD001_MessageFormat =
		"Type '{0}' is defined in server assembly '{1}' but referenced from {2} assembly '{3}'. "
		+ "Shared/Client code must not reference Server-assembly types directly — route through a "
		+ "Refit data-service interface in Shared/Features/*/Contracts/ or define a DTO in Shared. "
		+ "(NN_LDDD_001)";

	private static readonly LocalizableString LDDD001_Description =
		"LiteDDD principle: the client is a light presentation layer. Server-assembly types "
		+ "(assemblies whose identity ends in .Server) represent domain logic, database entities, "
		+ "or application-services and must not cross the Server/Shared boundary directly. "
		+ "Communicate via Refit interfaces (marked with [LdddDataService]) and DTOs in the Shared "
		+ "project. Apply [assembly: LdddBypass] for sibling primitive layers or carve-out cases.";

	/// <summary>NN_LDDD_001 descriptor — Warning, LiteDDD.AssemblyFence category.</summary>
	public static readonly DiagnosticDescriptor LDDD001_Rule = new(
		LDDD001_DiagnosticId,
		LDDD001_Title,
		LDDD001_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: LDDD001_Description,
		helpLinkUri: HelpBase + "nn_lddd_001");

	// ── NN_LDDD_002 — Server-namespace using directive in Shared/Client code ────

	/// <summary>Diagnostic ID for server-namespace using directives in Shared/Client code.</summary>
	public const string LDDD002_DiagnosticId = "NN_LDDD_002";

	private static readonly LocalizableString LDDD002_Title =
		"Server-namespace using directive in Shared/Client code";

	private static readonly LocalizableString LDDD002_MessageFormat =
		"Using directive '{0}' imports a server-side namespace into {1} assembly '{2}'. "
		+ "Remove the import and route through Shared contracts/DTOs instead. (NN_LDDD_002)";

	private static readonly LocalizableString LDDD002_Description =
		"LiteDDD principle: Shared/Client code must not import namespaces from Server assemblies. "
		+ "Even if no symbol is referenced, the import creates a dependency direction that violates "
		+ "the assembly fence. Move shared types to a Shared/Features/*/Contracts/ folder or define "
		+ "DTOs that flow across the wire.";

	/// <summary>NN_LDDD_002 descriptor — Warning, LiteDDD.AssemblyFence category.</summary>
	public static readonly DiagnosticDescriptor LDDD002_Rule = new(
		LDDD002_DiagnosticId,
		LDDD002_Title,
		LDDD002_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: LDDD002_Description,
		helpLinkUri: HelpBase + "nn_lddd_002");

	// ── NN_LDDD_004 — Entity Framework DbContext referenced in Shared/Client code ────

	/// <summary>Diagnostic ID for direct EF DbContext usage in Shared/Client code.</summary>
	public const string LDDD004_DiagnosticId = "NN_LDDD_004";

	private static readonly LocalizableString LDDD004_Title =
		"Entity Framework DbContext referenced in Shared/Client code";

	private static readonly LocalizableString LDDD004_MessageFormat =
		"Member '{0}' has Entity Framework DbContext type '{1}' inside {2} assembly '{3}'. "
		+ "DbContext is a server-side persistence concern — Shared/Client must communicate via "
		+ "Refit data-service interfaces and DTOs, never via direct ORM types. (NN_LDDD_004)";

	private static readonly LocalizableString LDDD004_Description =
		"LiteDDD principle: persistence concerns live behind the Server boundary. Even a field, "
		+ "property, or parameter typed as DbContext (or a subclass) in Shared/Client code leaks "
		+ "the ORM into the presentation layer. Route data access through Refit data-service "
		+ "interfaces marked with [LdddDataService].";

	/// <summary>NN_LDDD_004 descriptor — Warning, LiteDDD.AssemblyFence category.</summary>
	public static readonly DiagnosticDescriptor LDDD004_Rule = new(
		LDDD004_DiagnosticId,
		LDDD004_Title,
		LDDD004_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: LDDD004_Description,
		helpLinkUri: HelpBase + "nn_lddd_004");

	// ── NN_LDDD_002 text-scan regex (for .razor / .razor.cs AdditionalFiles) ────

	/// <summary>
	/// Matches <c>@using Foo.Server.Bar;</c> (Razor) or <c>using Foo.Server.Bar;</c> (C#).
	/// Anchored to line-start (multiline). The <c>.Server.</c> segment is required as a
	/// segment — namespaces containing <c>.Server.</c> mid-token still match because the
	/// regex requires segment-prefix <c>\b</c> word-boundaries on either side of the dotted
	/// chain, but the practical risk (custom namespaces with <c>.Server.</c> non-suffix
	/// segments) is intentionally accepted at Wave 1 per VibeConsider §5.5 — Wave 1
	/// AdditionalFile text-scan is regex-only; the plain-<c>.cs</c> semantic pathway
	/// covers the precise case.
	/// </summary>
	private static readonly Regex ServerUsingRegex = new(
		@"^\s*@?using\s+([\w\.]+\.Server(?:\.[\w\.]+)?)\s*;?\s*$",
		RegexOptions.Compiled | RegexOptions.Multiline);

	// ── DiagnosticAnalyzer overrides ──────────────────────────────────────────

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(LDDD001_Rule, LDDD002_Rule, LDDD004_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// Text-scan pathway (NN_LDDD_002 over .razor / .razor.cs AdditionalFiles)
		context.RegisterCompilationAction(AnalyzeCompilation);

		// Semantic pathways
		context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
		context.RegisterSyntaxNodeAction(AnalyzeIdentifierName, SyntaxKind.IdentifierName);
		context.RegisterSymbolAction(AnalyzeFieldOrProperty, SymbolKind.Field, SymbolKind.Property);
		context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
	}

	// ── Compilation gate ──────────────────────────────────────────────────────

	private static bool ShouldAnalyzeCompilation(Compilation compilation)
	{
		var assembly = compilation?.Assembly;
		if (assembly == null)
			return false;

		// Assembly-level opt-out — short-circuit before any rule fires.
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(assembly))
			return false;

		// Rules fire only inside Shared/Client compilations.
		return LdddAnalyzerHelpers.IsSharedOrClientAssembly(assembly);
	}

	// ── Text-scan pathway (NN_LDDD_002 over AdditionalFiles) ──────────────────

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

		// Dispatch — only .razor / .razor.cs participate in the text-scan pathway.
		var isRazorCs = path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);
		var isRazor = !isRazorCs
			&& !path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase)
			&& path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

		if (!isRazor && !isRazorCs)
			return;

		if (LdddAnalyzerHelpers.IsExceptedPath(path))
			return;

		var sourceText = file.GetText(context.CancellationToken);
		if (sourceText == null || sourceText.Length == 0)
			return;

		var text = sourceText.ToString();
		var compilationAssemblyName = context.Compilation.Assembly.Identity.Name;
		var compilationAssemblyKind = ClassifyAssemblyKind(compilationAssemblyName);

		foreach (Match match in ServerUsingRegex.Matches(text))
		{
			var namespacePart = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
			var displayName = ("@" + namespacePart).Replace("@", string.Empty);

			var location = Location.Create(
				path,
				new TextSpan(match.Index, match.Length),
				new LinePositionSpan(
					sourceText.Lines.GetLinePosition(match.Index),
					sourceText.Lines.GetLinePosition(match.Index + match.Length)));

			context.ReportDiagnostic(Diagnostic.Create(
				LDDD002_Rule,
				location,
				$"using {namespacePart}",
				compilationAssemblyKind,
				compilationAssemblyName));
		}
	}

	// ── Semantic pathway — NN_LDDD_002 plain .cs ──────────────────────────────

	private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;

		// AdditionalFile pathway covers .razor / .razor.cs / .razor.g.cs — skip here.
		if (syntaxTreePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
			return;
		if (syntaxTreePath.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase))
			return;

		if (LdddAnalyzerHelpers.IsExceptedPath(syntaxTreePath))
			return;

		var node = (UsingDirectiveSyntax)context.Node;
		if (node.Name == null)
			return;

		var symbolInfo = context.SemanticModel.GetSymbolInfo(node.Name, context.CancellationToken);
		var nsSymbol = symbolInfo.Symbol as INamespaceSymbol
			?? symbolInfo.CandidateSymbols.OfType<INamespaceSymbol>().FirstOrDefault();

		if (nsSymbol == null)
		{
			// Fall back to text-pattern check on the directive's name text — covers cases where
			// the namespace doesn't resolve (e.g. the Server assembly isn't referenced by the
			// Shared compilation, but the using directive is syntactically present).
			var directiveText = node.Name.ToString();
			if (LooksLikeServerNamespace(directiveText))
			{
				ReportLddd002(context, node, $"using {directiveText}");
			}
			return;
		}

		if (!IsServerNamespace(nsSymbol))
			return;

		ReportLddd002(context, node, $"using {node.Name}");
	}

	private static void ReportLddd002(SyntaxNodeAnalysisContext context, UsingDirectiveSyntax node, string displayName)
	{
		var compilationAssemblyName = context.Compilation.Assembly.Identity.Name;
		var compilationAssemblyKind = ClassifyAssemblyKind(compilationAssemblyName);

		context.ReportDiagnostic(Diagnostic.Create(
			LDDD002_Rule,
			node.GetLocation(),
			displayName,
			compilationAssemblyKind,
			compilationAssemblyName));
	}

	// ── Semantic pathway — NN_LDDD_001 IdentifierName ─────────────────────────

	private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
		if (LdddAnalyzerHelpers.IsExceptedPath(syntaxTreePath))
			return;

		var node = (IdentifierNameSyntax)context.Node;

		// Skip identifiers within UsingDirective — those are NN_LDDD_002's job.
		if (IsWithinUsingDirective(node))
			return;

		// Cheap structural pre-filter (mirrors NnDesignMudBlazorPolicyAnalyzer optimization).
		if (!LdddAnalyzerHelpers.IsLikelyTypeOrNamespaceReference(node))
			return;

		var symbolInfo = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken);
		var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
		if (symbol == null)
			return;

		// Resolve to the containing type symbol when the symbol is a member (method/property/etc.).
		var typeSymbol = symbol switch
		{
			ITypeSymbol t => t,
			IMethodSymbol m => m.ContainingType as ITypeSymbol,
			IPropertySymbol p => p.ContainingType as ITypeSymbol,
			IFieldSymbol f => f.ContainingType as ITypeSymbol,
			IEventSymbol e => e.ContainingType as ITypeSymbol,
			_ => null,
		};

		var containingAssembly = typeSymbol?.ContainingAssembly ?? symbol.ContainingAssembly;
		if (containingAssembly == null)
			return;

		// Don't flag self-references (same compilation).
		if (SymbolEqualityComparer.Default.Equals(containingAssembly, context.Compilation.Assembly))
			return;

		if (!LdddAnalyzerHelpers.IsServerAssembly(containingAssembly))
			return;

		var displayName = typeSymbol?.ToDisplayString() ?? symbol.ToDisplayString();
		var compilationAssemblyName = context.Compilation.Assembly.Identity.Name;
		var compilationAssemblyKind = ClassifyAssemblyKind(compilationAssemblyName);

		context.ReportDiagnostic(Diagnostic.Create(
			LDDD001_Rule,
			node.GetLocation(),
			displayName,
			containingAssembly.Identity.Name,
			compilationAssemblyKind,
			compilationAssemblyName));
	}

	// ── Semantic pathway — NN_LDDD_004 fields / properties / parameters ──────

	private static void AnalyzeFieldOrProperty(SymbolAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		ITypeSymbol? memberType;
		string memberName;
		string memberKind;
		switch (context.Symbol)
		{
			case IFieldSymbol field:
				memberType = field.Type;
				memberName = field.Name;
				memberKind = "Field";
				break;
			case IPropertySymbol property:
				memberType = property.Type;
				memberName = property.Name;
				memberKind = "Property";
				break;
			default:
				return;
		}

		if (!LdddAnalyzerHelpers.IsEntityFrameworkDbContext(memberType))
			return;

		var declaringSyntax = context.Symbol.DeclaringSyntaxReferences.FirstOrDefault();
		if (declaringSyntax == null)
			return;

		var syntaxTreePath = declaringSyntax.SyntaxTree.FilePath ?? string.Empty;
		if (LdddAnalyzerHelpers.IsExceptedPath(syntaxTreePath))
			return;

		// Suppress if the containing type has [LdddBypass].
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(context.Symbol.ContainingType))
			return;

		var location = declaringSyntax.GetSyntax(context.CancellationToken).GetLocation();
		var compilationAssemblyName = context.Compilation.Assembly.Identity.Name;
		var compilationAssemblyKind = ClassifyAssemblyKind(compilationAssemblyName);

		context.ReportDiagnostic(Diagnostic.Create(
			LDDD004_Rule,
			location,
			$"{memberKind} {memberName}",
			memberType!.ToDisplayString(),
			compilationAssemblyKind,
			compilationAssemblyName));
	}

	private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
		if (LdddAnalyzerHelpers.IsExceptedPath(syntaxTreePath))
			return;

		var parameter = (ParameterSyntax)context.Node;
		if (parameter.Type == null)
			return;

		var typeInfo = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken);
		var paramType = typeInfo.Type;
		if (!LdddAnalyzerHelpers.IsEntityFrameworkDbContext(paramType))
			return;

		// Suppress if the enclosing type has [LdddBypass].
		var enclosingSymbol = context.SemanticModel.GetEnclosingSymbol(parameter.SpanStart, context.CancellationToken);
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(enclosingSymbol))
			return;

		var paramName = parameter.Identifier.ValueText;
		var compilationAssemblyName = context.Compilation.Assembly.Identity.Name;
		var compilationAssemblyKind = ClassifyAssemblyKind(compilationAssemblyName);

		context.ReportDiagnostic(Diagnostic.Create(
			LDDD004_Rule,
			parameter.GetLocation(),
			$"Parameter {paramName}",
			paramType!.ToDisplayString(),
			compilationAssemblyKind,
			compilationAssemblyName));
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns true when the namespace symbol's root ends in <c>.Server</c>, OR the namespace's
	/// containing assembly identity ends in <c>.Server</c>. The dual check covers both the
	/// "Server suffix at namespace level" (e.g. <c>Foo.Server.Bar</c>) and the "Server suffix at
	/// assembly level" (e.g. namespace doesn't have <c>Server</c> but the assembly does — rare
	/// but legitimate for refactored projects).
	/// </summary>
	private static bool IsServerNamespace(INamespaceSymbol ns)
	{
		var displayName = ns.ToDisplayString();
		if (LooksLikeServerNamespace(displayName))
			return true;

		// Fallback: namespace's containing assembly is a Server assembly.
		var containingAssembly = ns.ContainingAssembly;
		return LdddAnalyzerHelpers.IsServerAssembly(containingAssembly);
	}

	/// <summary>
	/// Text-pattern check for namespace strings that look like server-side namespaces.
	/// Matches when the dotted-namespace chain contains a <c>.Server.</c> mid-segment or
	/// terminates in <c>.Server</c>.
	/// </summary>
	private static bool LooksLikeServerNamespace(string ns)
	{
		if (string.IsNullOrEmpty(ns))
			return false;

		// Match either ".Server." (mid-chain) or ending ".Server".
		return ns.IndexOf(".Server.", StringComparison.Ordinal) >= 0
			|| ns.EndsWith(".Server", StringComparison.Ordinal);
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
	/// Returns a human-friendly classification of the assembly's role
	/// (<c>"Shared"</c> / <c>"Client"</c>) based on identity-name suffix.
	/// Used purely for diagnostic message formatting.
	/// </summary>
	private static string ClassifyAssemblyKind(string assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
			return "consumer";
		if (assemblyName.EndsWith(".Shared", StringComparison.Ordinal))
			return "Shared";
		if (assemblyName.EndsWith(".Client", StringComparison.Ordinal))
			return "Client";
		return "consumer";
	}
}
