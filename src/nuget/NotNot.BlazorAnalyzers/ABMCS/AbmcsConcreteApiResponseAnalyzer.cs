using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.BlazorAnalyzers.LiteDDD;

namespace NotNot.BlazorAnalyzers.ABMCS;

/// <summary>
/// ABMCS — <c>NN_ABMCS_009</c>: concrete <c>Refit.ApiResponse&lt;T&gt;</c> referenced at the
/// transport boundary. The canonical form per
/// <c>docs/protocols/abmcs-architecture.VowAgent.md §12.1b</c> is the interface
/// <c>Refit.IApiResponse&lt;T&gt;</c>, NOT the concrete <c>ApiResponse&lt;T&gt;</c> class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection mechanism</b>:
/// <list type="bullet">
///   <item>
///     <description>
///     Fires on <see cref="SyntaxKind.IdentifierName"/> AND <see cref="SyntaxKind.GenericName"/>
///     events (both are <see cref="SimpleNameSyntax"/> subtypes) when the resolved symbol's type
///     full-name is <c>Refit.ApiResponse</c> (open-generic; constructed forms like
///     <c>Refit.ApiResponse&lt;OrderDto&gt;</c> are <see cref="GenericNameSyntax"/> nodes and
///     match via <see cref="ITypeSymbol.OriginalDefinition"/>). Registering on both kinds is
///     required because Roslyn does NOT fire <c>IdentifierName</c> for the identifier portion of
///     a constructed generic — the generic name is a distinct node kind.
///     </description>
///   </item>
///   <item>
///     <description>
///     Gates on Shared/Client assembly suffix per
///     <see cref="LdddAnalyzerHelpers.IsSharedOrClientAssembly(IAssemblySymbol)"/> — the
///     concrete class is permitted in Server-side IApi-mapping helpers where the result is
///     materialized; it is the Shared/Client transport surface that must use the interface.
///     </description>
///   </item>
///   <item>
///     <description>
///     Short-circuits on assembly-level <c>[Feature(FeatureRole.Bypass)]</c> / <c>[LdddBypass]</c> via
///     <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/>.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Why interface over concrete</b>: <c>IApiResponse&lt;T&gt;</c> is the abstraction Refit
/// returns from generated proxies AND the form the controller mapping accepts. The concrete
/// <c>ApiResponse&lt;T&gt;</c> class couples consumers to a specific implementation, makes
/// testing harder (no straightforward fake), and signals to readers that the transport-tier
/// envelope discipline has slipped. See
/// <c>docs/protocols/abmcs-architecture.VowAgent.md §12.5</c> "What NOT to use" section.
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Warning"/>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbmcsConcreteApiResponseAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "ABMCS.Transport";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	/// <summary>Open-generic full name of the concrete <c>Refit.ApiResponse&lt;T&gt;</c> class (target of the warning).</summary>
	private const string ConcreteApiResponseFullName = "Refit.ApiResponse<TResponse>";

	/// <summary>Simple name (defensive secondary match).</summary>
	private const string ConcreteApiResponseSimpleName = "ApiResponse";

	/// <summary>Open-generic full name of the canonical <c>Refit.IApiResponse&lt;T&gt;</c> interface (recommended replacement).</summary>
	private const string InterfaceApiResponseFullName = "Refit.IApiResponse<TResponse>";

	/// <summary>Diagnostic ID for concrete <c>Refit.ApiResponse&lt;T&gt;</c> at the transport boundary.</summary>
	public const string ABMCS009_DiagnosticId = "NN_ABMCS_009";

	private static readonly LocalizableString ABMCS009_Title =
		"Concrete Refit.ApiResponse<T> used — prefer IApiResponse<T>";

	private static readonly LocalizableString ABMCS009_MessageFormat =
		"Type 'Refit.ApiResponse<{0}>' (concrete class) is referenced. Use the interface "
		+ "'Refit.IApiResponse<{0}>' at transport boundaries per ABMCS error-handling discipline. "
		+ "(NN_ABMCS_009)";

	private static readonly LocalizableString ABMCS009_Description =
		"ABMCS principle (per docs/protocols/abmcs-architecture.VowAgent.md §12.6): transport-tier "
		+ "contracts return Refit.IApiResponse<T>, not the concrete Refit.ApiResponse<T> class. "
		+ "The interface is the abstraction Refit's generated proxies produce AND the form "
		+ "controller mappings accept — coupling consumers to the concrete class makes testing "
		+ "harder (no straightforward fake) and signals slipped envelope discipline. "
		+ "Bypass via [assembly: Feature(FeatureRole.Bypass)] for sample/playground assemblies, or "
		+ "[Feature(FeatureRole.Bypass)] on the class/method for narrow carve-outs (e.g. internal helper that "
		+ "must materialize the concrete class).";

	/// <summary>NN_ABMCS_009 descriptor — Warning, ABMCS.Transport category.</summary>
	public static readonly DiagnosticDescriptor ABMCS009_Rule = new(
		ABMCS009_DiagnosticId,
		ABMCS009_Title,
		ABMCS009_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: ABMCS009_Description,
		helpLinkUri: HelpBase + "nn_abmcs_009");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(ABMCS009_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// Register on BOTH SyntaxKind.IdentifierName (plain identifiers like
		// `Refit.ApiResponse` used as an unbound open-generic-typeof) AND SyntaxKind.GenericName
		// (constructed-generic forms like `ApiResponse<OrderDto>` — the canonical real-world
		// target). Roslyn fires distinct events for each kind; a single registration on
		// IdentifierName misses the GenericName case entirely (the failure mode the analyzer was
		// previously exhibiting — see fix iter 1 root cause).
		context.RegisterSyntaxNodeAction(AnalyzeSimpleName, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
	}

	private static bool ShouldAnalyzeCompilation(Compilation compilation)
	{
		var assembly = compilation?.Assembly;
		if (assembly == null)
			return false;

		// Assembly-level opt-out — short-circuit before any rule fires.
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(assembly))
			return false;

		// Rule fires only inside Shared/Client compilations — Server-side ApiResponse<T>
		// materialization is permitted (it is the controller-mapping site that produces it).
		return LdddAnalyzerHelpers.IsSharedOrClientAssembly(assembly);
	}

	private static void AnalyzeSimpleName(SyntaxNodeAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
		if (LdddAnalyzerHelpers.IsExceptedPath(syntaxTreePath))
			return;

		// Handler is registered for SyntaxKind.IdentifierName AND SyntaxKind.GenericName — both
		// derive from SimpleNameSyntax. The semantic resolution + symbol-matching logic below is
		// agnostic to which concrete kind fired the event.
		var node = (SimpleNameSyntax)context.Node;

		// Cheap structural pre-filter — only fire on type-syntax positions (mirrors
		// NN_LDDD_001's optimization). Helper accepts SimpleNameSyntax so both identifier-name
		// and generic-name nodes flow through the same filter.
		if (!LdddAnalyzerHelpers.IsLikelyTypeOrNamespaceReference(node))
			return;

		var symbolInfo = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken);
		var symbol = symbolInfo.Symbol;
		if (symbol is not INamedTypeSymbol typeSymbol)
			return;

		// Match by open-generic full name OR simple name + Refit namespace.
		var original = typeSymbol.OriginalDefinition ?? typeSymbol;
		var fullName = StripGlobalPrefix(original.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

		// Open-generic match: "Refit.ApiResponse<TResponse>".
		var matched = string.Equals(fullName, ConcreteApiResponseFullName, StringComparison.Ordinal);

		// Defensive simple-name + namespace match — covers cases where Refit's open-generic
		// type-parameter naming diverges (e.g. compiler emits "<T>" vs "<TResponse>").
		if (!matched
			&& string.Equals(typeSymbol.Name, ConcreteApiResponseSimpleName, StringComparison.Ordinal)
			&& typeSymbol.ContainingNamespace != null
			&& string.Equals(
				typeSymbol.ContainingNamespace.ToDisplayString(),
				"Refit",
				StringComparison.Ordinal)
			// Exclude the interface IApiResponse — same "ApiResponse" suffix but different symbol kind.
			&& typeSymbol.TypeKind == TypeKind.Class)
		{
			matched = true;
		}

		if (!matched)
			return;

		// Honor symbol-level [Feature(FeatureRole.Bypass)] / [LdddBypass] on the enclosing declaration
		// (class, interface, or method) so the type-scope override hatches still work.
		var enclosingSymbol = context.SemanticModel.GetEnclosingSymbol(node.SpanStart, context.CancellationToken);
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(enclosingSymbol))
			return;

		// Extract the type-argument display name for the diagnostic message ("{0}" arg).
		var typeArgDisplay = ExtractTypeArgumentDisplay(typeSymbol);

		// Diagnostic location = JUST the identifier token (not the full `ApiResponse<OrderDto>`
		// span). For IdentifierNameSyntax, node.GetLocation() and node.Identifier.GetLocation()
		// are equivalent; for GenericNameSyntax, node.GetLocation() spans the entire generic
		// including `<TypeArgs>` while node.Identifier.GetLocation() spans only `ApiResponse`.
		// The narrower span is the user-friendly anchor (matches how Roslyn highlights the
		// offending type name in IDE squiggles).
		context.ReportDiagnostic(Diagnostic.Create(
			ABMCS009_Rule,
			node.Identifier.GetLocation(),
			typeArgDisplay));
	}

	/// <summary>
	/// Returns the display name of the first type argument of a constructed <c>ApiResponse&lt;T&gt;</c>
	/// reference (for the diagnostic message), or <c>"T"</c> when the reference is an open-generic
	/// (no constructed type arguments).
	/// </summary>
	private static string ExtractTypeArgumentDisplay(INamedTypeSymbol typeSymbol)
	{
		if (typeSymbol.IsGenericType && !typeSymbol.IsUnboundGenericType && typeSymbol.TypeArguments.Length > 0)
		{
			return typeSymbol.TypeArguments[0].ToDisplayString();
		}
		return "T";
	}

	private static string StripGlobalPrefix(string fullName)
	{
		if (string.IsNullOrEmpty(fullName))
			return fullName;
		return fullName.StartsWith("global::", StringComparison.Ordinal)
			? fullName.Substring("global::".Length)
			: fullName;
	}
}
