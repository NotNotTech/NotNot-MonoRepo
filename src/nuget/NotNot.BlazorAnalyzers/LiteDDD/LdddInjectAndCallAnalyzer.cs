using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.BlazorAnalyzers.Lifecycle;

namespace NotNot.BlazorAnalyzers.LiteDDD;

/// <summary>
/// LiteDDD Wave 2 — component-boundary rules <c>NN_LDDD_003</c> and <c>NN_LDDD_005</c>.
/// Single analyzer class with two complementary detection pathways unified under a single
/// <see cref="DiagnosticAnalyzer"/> (parallels
/// <see cref="LdddAssemblyFenceAnalyzer"/> Wave 1 and the
/// <see cref="NnDesign.NnDesignMudBlazorPolicyAnalyzer"/> hybrid pattern):
/// <list type="number">
///   <item><description>
///     <b>NN_LDDD_003</b> — Blazor component <c>[Inject]</c>s a type that is neither
///     <c>[LdddDataService]</c>-marked nor in the framework allow-list. Fires per-property on
///     <see cref="SymbolKind.NamedType"/>; resolves <c>[Inject]</c> attributes via simple-name
///     match against <c>Microsoft.AspNetCore.Components.InjectAttribute</c>; marker check is
///     simple-name OR fully-qualified against <c>NotNot.Bcl.Diagnostics.LdddDataServiceAttribute</c>;
///     framework allow-list is the curated set documented on
///     <see cref="LDDD003_Rule"/> (NavigationManager, ILogger, IJSRuntime, IConfiguration,
///     IStringLocalizer, IDialogService, IHttpClientFactory, HttpClient, ISnackbar,
///     AuthenticationStateProvider, HubConnection — full list in
///     <see cref="FrameworkAllowList"/>).
///   </description></item>
///   <item><description>
///     <b>NN_LDDD_005</b> — direct method invocation inside a Blazor component on a receiver
///     whose type is <c>[LdddDomainService]</c>-marked. Fires per-invocation on
///     <see cref="SyntaxKind.InvocationExpression"/>; property access (<see cref="MemberAccessExpressionSyntax"/>
///     not under an <see cref="InvocationExpressionSyntax"/>) is intentionally ignored — domain
///     services must be called via a Refit <c>[LdddDataService]</c> proxy, never invoked
///     directly from presentation-layer components.
///   </description></item>
/// </list>
/// Both rules gate on the current compilation's assembly identity ending in <c>.Shared</c>
/// or <c>.Client</c> (<see cref="LdddAnalyzerHelpers.IsSharedOrClientAssembly(IAssemblySymbol)"/>),
/// short-circuit on <c>[assembly: LdddBypass]</c>, and additionally honor type-scope
/// <c>[LdddBypass]</c> via <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(ISymbol)"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bypass mechanisms</b>: same hierarchy as <see cref="LdddAssemblyFenceAnalyzer"/> plus
/// type-scope suppression — apply <c>[LdddBypass]</c> on the offending partial class to
/// suppress the diagnostic for that class's members and methods.
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Warning"/> for both rules — escalates to
/// error via <c>.editorconfig</c> in CI builds.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LdddInjectAndCallAnalyzer : DiagnosticAnalyzer
{
	private const string Category = "LiteDDD.ComponentBoundary";

	private const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

	// ── NN_LDDD_003 — Injected service not marked [LdddDataService] ────────────

	/// <summary>Diagnostic ID for <c>[Inject]</c> of a non-<c>[LdddDataService]</c>, non-framework type.</summary>
	public const string LDDD003_DiagnosticId = "NN_LDDD_003";

	private static readonly LocalizableString LDDD003_Title =
		"Injected service not marked [LdddDataService]";

	private static readonly LocalizableString LDDD003_MessageFormat =
		"Type '{0}' is injected into Blazor component '{1}' but is not marked [LdddDataService]. "
		+ "Services injected into Blazor components from Shared/Client assemblies must be Refit "
		+ "DataServices or framework infrastructure. Mark the service interface with "
		+ "[LdddDataService] or add the type to the framework allow-list. (NN_LDDD_003)";

	private static readonly LocalizableString LDDD003_Description =
		"LiteDDD principle: the Blazor component layer composes presentation from a narrow set of "
		+ "wire-level Refit DataServices plus a curated framework allow-list. Injecting an arbitrary "
		+ "domain-service or helper type into a ComponentBase-derived class blurs the LiteDDD "
		+ "Shared/Client boundary and risks pulling domain compute into the page. Mark the injected "
		+ "interface with [NotNot.Bcl.Diagnostics.LdddDataServiceAttribute] when it is a Refit "
		+ "contract, or add the type to the framework allow-list curated by this analyzer. "
		+ "Allow-list (full-name match; generics matched on OriginalDefinition): "
		+ "Microsoft.AspNetCore.Components.NavigationManager, "
		+ "Microsoft.Extensions.Logging.ILogger, Microsoft.Extensions.Logging.ILogger<T>, "
		+ "Microsoft.JSInterop.IJSRuntime, Microsoft.Extensions.Configuration.IConfiguration, "
		+ "Microsoft.Extensions.Localization.IStringLocalizer, "
		+ "Microsoft.Extensions.Localization.IStringLocalizer<T>, "
		+ "MudBlazor.IDialogService, System.Net.Http.IHttpClientFactory, "
		+ "System.Net.Http.HttpClient, MudBlazor.ISnackbar, "
		+ "Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, "
		+ "Microsoft.AspNetCore.SignalR.Client.HubConnection. "
		+ "Bypass via [assembly: LdddBypass] for whole-assembly opt-out, or [LdddBypass] on the "
		+ "ComponentBase-derived class for type-scope suppression.";

	/// <summary>NN_LDDD_003 descriptor — Warning, LiteDDD.ComponentBoundary category.</summary>
	public static readonly DiagnosticDescriptor LDDD003_Rule = new(
		LDDD003_DiagnosticId,
		LDDD003_Title,
		LDDD003_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: LDDD003_Description,
		helpLinkUri: HelpBase + "nn_lddd_003");

	// ── NN_LDDD_005 — Direct call to [LdddDomainService] from Blazor component ──

	/// <summary>Diagnostic ID for direct method invocation on a <c>[LdddDomainService]</c>-marked receiver inside a Blazor component.</summary>
	public const string LDDD005_DiagnosticId = "NN_LDDD_005";

	private static readonly LocalizableString LDDD005_Title =
		"Direct call to [LdddDomainService] from Blazor component";

	private static readonly LocalizableString LDDD005_MessageFormat =
		"Method '{0}' on [LdddDomainService] type '{1}' is invoked from Blazor component '{2}'. "
		+ "Domain services must be called via a Refit DataService proxy, not directly from "
		+ "components. (NN_LDDD_005)";

	private static readonly LocalizableString LDDD005_Description =
		"LiteDDD principle: domain compute lives behind the Server boundary and is invoked from "
		+ "the client via Refit [LdddDataService] interfaces. A direct instance-method call on a "
		+ "[LdddDomainService]-marked receiver from a ComponentBase-derived class bypasses the "
		+ "wire boundary and leaks server-domain coupling into the presentation layer. Route the "
		+ "call through a Refit DataService proxy in Shared/Features/*/Contracts/ instead. "
		+ "Bypass via [assembly: LdddBypass] for whole-assembly opt-out, or [LdddBypass] on the "
		+ "ComponentBase-derived class for type-scope suppression. Property access on a "
		+ "[LdddDomainService] receiver is NOT diagnosed — only method invocations are.";

	/// <summary>NN_LDDD_005 descriptor — Warning, LiteDDD.ComponentBoundary category.</summary>
	public static readonly DiagnosticDescriptor LDDD005_Rule = new(
		LDDD005_DiagnosticId,
		LDDD005_Title,
		LDDD005_MessageFormat,
		Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: LDDD005_Description,
		helpLinkUri: HelpBase + "nn_lddd_005");

	// ── Framework allow-list (NN_LDDD_003 — R4) ───────────────────────────────

	/// <summary>
	/// Curated framework-type allow-list for <c>NN_LDDD_003</c>. Names are fully-qualified
	/// (no <c>global::</c> prefix); generic types use the unconstructed display form
	/// (e.g. <c>Microsoft.Extensions.Logging.ILogger&lt;T&gt;</c>). Match is done against
	/// <see cref="ITypeSymbol.OriginalDefinition"/> with
	/// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> (global prefix stripped). The
	/// list is intentionally narrow — new entries are PR-able as conventions evolve. See the
	/// XML doc on <see cref="LDDD003_Rule"/> for the rationale per entry.
	/// </summary>
	private static readonly HashSet<string> FrameworkAllowList = new(StringComparer.Ordinal)
	{
		// Blazor framework infrastructure
		"Microsoft.AspNetCore.Components.NavigationManager",
		"Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider",

		// Logging
		"Microsoft.Extensions.Logging.ILogger",
		"Microsoft.Extensions.Logging.ILogger<T>",

		// JS interop
		"Microsoft.JSInterop.IJSRuntime",

		// Configuration + Localization
		"Microsoft.Extensions.Configuration.IConfiguration",
		"Microsoft.Extensions.Localization.IStringLocalizer",
		"Microsoft.Extensions.Localization.IStringLocalizer<T>",

		// HTTP
		"System.Net.Http.IHttpClientFactory",
		"System.Net.Http.HttpClient",

		// MudBlazor UI infrastructure (sibling primitive layer)
		"MudBlazor.IDialogService",
		"MudBlazor.ISnackbar",

		// SignalR (HubConnection per VibeTDD §3 Group e-Wave2 spec)
		"Microsoft.AspNetCore.SignalR.Client.HubConnection",
	};

	// ── DiagnosticAnalyzer overrides ──────────────────────────────────────────

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(LDDD003_Rule, LDDD005_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSymbolAction(AnalyzeNamedTypeForInject, SymbolKind.NamedType);
		context.RegisterSyntaxNodeAction(AnalyzeInvocationForDomainCall, SyntaxKind.InvocationExpression);
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

	// ── NN_LDDD_003 — [Inject] of non-[LdddDataService] type ──────────────────

	private static void AnalyzeNamedTypeForInject(SymbolAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		if (context.Symbol is not INamedTypeSymbol typeSymbol)
			return;

		if (typeSymbol.TypeKind != TypeKind.Class)
			return;

		if (!BlazorLifecycleHelpers.IsBlazorComponent(typeSymbol))
			return;

		// Type-scope bypass — [LdddBypass] on the component class (or any containing scope).
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(typeSymbol))
			return;

		// Path-bucket exemption check — based on the first declaring syntax location.
		var firstDeclaringRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
		if (firstDeclaringRef != null
			&& LdddAnalyzerHelpers.IsExceptedPath(firstDeclaringRef.SyntaxTree.FilePath ?? string.Empty))
			return;

		foreach (var member in typeSymbol.GetMembers())
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			if (member is not IPropertySymbol property)
				continue;

			if (!HasInjectAttribute(property))
				continue;

			var injectedType = property.Type;
			if (injectedType == null)
				continue;

			if (IsLdddDataServiceMarked(injectedType))
				continue;

			if (IsFrameworkAllowed(injectedType))
				continue;

			var location = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken).GetLocation()
				?? property.Locations.FirstOrDefault();
			if (location == null)
				continue;

			context.ReportDiagnostic(Diagnostic.Create(
				LDDD003_Rule,
				location,
				injectedType.ToDisplayString(),
				typeSymbol.Name));
		}
	}

	private static bool HasInjectAttribute(IPropertySymbol property)
	{
		foreach (var attribute in property.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass == null)
				continue;

			// Match Microsoft.AspNetCore.Components.InjectAttribute by simple-name + namespace.
			if (string.Equals(attrClass.Name, "InjectAttribute", StringComparison.Ordinal)
				&& string.Equals(
					attrClass.ContainingNamespace?.ToDisplayString(),
					"Microsoft.AspNetCore.Components",
					StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsLdddDataServiceMarked(ITypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass == null)
				continue;

			if (string.Equals(attrClass.Name, LdddAnalyzerHelpers.LdddDataServiceAttributeSimpleName, StringComparison.Ordinal))
				return true;
			if (string.Equals(attrClass.ToDisplayString(), LdddAnalyzerHelpers.LdddDataServiceAttributeFullName, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static bool IsFrameworkAllowed(ITypeSymbol type)
	{
		// Generic types: match against the unconstructed (OriginalDefinition) display form so
		// that ILogger<MyComponent> and IStringLocalizer<MyResources> match their open-generic
		// allow-list entries (ILogger<T>, IStringLocalizer<T>).
		var original = type.OriginalDefinition ?? type;
		var displayName = StripGlobalPrefix(original.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

		if (FrameworkAllowList.Contains(displayName))
			return true;

		// Non-generic types: also try the type's own display string in case OriginalDefinition
		// returns a different shape (defensive — typically redundant).
		var fallbackDisplay = StripGlobalPrefix(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		return FrameworkAllowList.Contains(fallbackDisplay);
	}

	private static string StripGlobalPrefix(string fullName)
	{
		if (string.IsNullOrEmpty(fullName))
			return fullName;
		return fullName.StartsWith("global::", StringComparison.Ordinal)
			? fullName.Substring("global::".Length)
			: fullName;
	}

	// ── NN_LDDD_005 — invocation on [LdddDomainService] receiver ──────────────

	private static void AnalyzeInvocationForDomainCall(SyntaxNodeAnalysisContext context)
	{
		if (!ShouldAnalyzeCompilation(context.Compilation))
			return;

		var syntaxTreePath = context.Node.SyntaxTree.FilePath ?? string.Empty;
		if (LdddAnalyzerHelpers.IsExceptedPath(syntaxTreePath))
			return;

		var invocation = (InvocationExpressionSyntax)context.Node;

		// V1 scope: only diagnose explicit member access (`receiver.Method(...)`).
		// Implicit `this`-call (`Method(...)` with `this` receiver) and static calls are out of scope.
		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			return;

		// Resolve the receiver's type (the symbol on the LEFT side of the dot).
		var receiverTypeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken);
		if (receiverTypeInfo.Type is not INamedTypeSymbol receiverType)
			return;

		if (!IsLdddDomainServiceMarked(receiverType))
			return;

		// Resolve the enclosing class — must be ComponentBase-derived.
		var enclosingClassDecl = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (enclosingClassDecl == null)
			return;

		var enclosingType = context.SemanticModel.GetDeclaredSymbol(enclosingClassDecl, context.CancellationToken);
		if (enclosingType == null)
			return;

		if (!BlazorLifecycleHelpers.IsBlazorComponent(enclosingType))
			return;

		// Type-scope bypass — [LdddBypass] on the enclosing component class or any containing scope.
		if (LdddAnalyzerHelpers.HasLdddBypassAttribute(enclosingType))
			return;

		var methodName = memberAccess.Name.Identifier.ValueText;
		context.ReportDiagnostic(Diagnostic.Create(
			LDDD005_Rule,
			invocation.GetLocation(),
			methodName,
			receiverType.ToDisplayString(),
			enclosingType.Name));
	}

	/// <summary>
	/// Returns true when <paramref name="type"/> (or any base type up its inheritance chain)
	/// carries the <c>[LdddDomainService]</c> marker. Matched by simple-name OR fully-qualified
	/// name to support both the canonical attribute reference and the local-copy fallback
	/// pattern (mirrors <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/>).
	/// Walks <see cref="INamedTypeSymbol.BaseType"/> with explicit depth bound (defends against
	/// pathological inheritance graphs).
	/// </summary>
	private static bool IsLdddDomainServiceMarked(INamedTypeSymbol type)
	{
		const int MaxDepth = 100;
		var current = type;
		var depth = 0;
		while (current != null && depth < MaxDepth)
		{
			foreach (var attribute in current.GetAttributes())
			{
				var attrClass = attribute.AttributeClass;
				if (attrClass == null)
					continue;

				if (string.Equals(attrClass.Name, LdddAnalyzerHelpers.LdddDomainServiceAttributeSimpleName, StringComparison.Ordinal))
					return true;
				if (string.Equals(attrClass.ToDisplayString(), LdddAnalyzerHelpers.LdddDomainServiceAttributeFullName, StringComparison.Ordinal))
					return true;
			}
			current = current.BaseType;
			depth++;
		}
		return false;
	}
}
