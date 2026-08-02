using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Analyzer forbidding a read of a <c>NotNot.AppSettings</c>-generated ServerOnly settings key from a
/// client-reachable (<c>.Shared</c> / <c>.Client</c>) compilation — the read returns <c>null</c> on the
/// WASM client at runtime (NN_C005).
/// </summary>
/// <remarks>
/// <para>
/// <b>Rationale</b>: <c>NotNot.AppSettings</c> prunes <c>ServerOnly</c> keys from the generated client
/// snapshot (the <c>_ClientAppSettings</c> mirror tree). A <c>.Shared</c> Razor Class Library executes in
/// BOTH the server and the WASM client; <c>.Client</c> is client-only. Reading a ServerOnly key from that
/// client-reachable code therefore yields <c>null</c> on the client and — with <c>.Require()</c> — throws at
/// bootstrap. This is the read-reachability companion to NN_C004 (which bans the write-side
/// <c>?? default</c>): orthogonal axes, gap-free.
/// </para>
/// <para>
/// <b>Detection</b>: fires on a member-access (<c>settings.Key</c> or <c>settings?.Key</c>) where
/// <list type="number">
///   <item><description>
///     the current compilation's assembly identity name ends with <c>.Shared</c> or <c>.Client</c>
///     (Server assemblies are exempt — ServerOnly reads are legal there); AND
///   </description></item>
///   <item><description>
///     the accessed member resolves to a property/field on a <c>NotNot.AppSettings</c>-generated FULL-tree
///     type (containing type carries <c>[GeneratedCode("NotNot.AppSettings", ...)]</c> and lives in the
///     <c>.AppSettingsGen</c> full <c>AppSettings</c> tree — NOT the <c>_ClientAppSettings</c> mirror); AND
///   </description></item>
///   <item><description>
///     the corresponding path is ABSENT from the generated <c>_ClientAppSettings</c> mirror (the key was
///     pruned ⇒ ServerOnly).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Silent (out of scope)</b>: reads of client-whitelisted keys (present in <c>_ClientAppSettings</c>);
/// reads from a <c>.Server</c> assembly; reads of the <c>_ClientAppSettings</c> mirror itself (already
/// pruned ⇒ safe); reads of non-<c>NotNot.AppSettings</c> members; compilations whose <c>_ClientAppSettings</c>
/// mirror is not present (conservative — cannot prove ServerOnly); member references inside
/// <c>nameof(...)</c> (no runtime dereference); member access inside generated <c>*.g.cs</c> files (skipped
/// via <see cref="GeneratedCodeAnalysisFlags.None"/>).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnAppSettingsServerOnlyReadAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NN_C005";

	// Category string matches NN_C004 (AppSettingsCodeDefaultAnalyzer) exactly so the NN_C family shares one
	// category bucket for category-based .editorconfig + release-tracking.
	private const string Category = "CodeStyle";

	/// <summary>
	/// The tool name emitted as the first <c>[GeneratedCode]</c> constructor argument on every
	/// <c>NotNot.AppSettings</c>-generated settings class (see <c>NotNot.AppSettings/AppSettingsGen.cs</c>).
	/// </summary>
	private const string AppSettingsToolName = "NotNot.AppSettings";

	private const string GeneratedCodeAttributeFullName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

	// NotNot.AppSettings namespace/type conventions (see NotNot.AppSettings/AppSettingsGen.cs):
	//   full   tree root:  {Root}.AppSettingsGen.AppSettings           subtree ns: {Root}.AppSettingsGen._AppSettings{...}
	//   client tree root:  {Root}.AppSettingsGen._ClientAppSettings    subtree ns: {Root}.AppSettingsGen._ClientAppSettingsTypes{...}
	// The full and client subtrees share identical type names + identical namespace suffix after the
	// subtree-root segment, so a full-tree type maps to its client counterpart by swapping that segment.
	private const string GenNamespaceSegment = "AppSettingsGen";
	private const string FullRootTypeName = "AppSettings";
	private const string ClientRootTypeName = "_ClientAppSettings";
	private const string FullSubtreeSegment = "_AppSettings";
	private const string ClientSubtreeSegment = "_ClientAppSettingsTypes";

	private static readonly LocalizableString Title =
		"ServerOnly AppSettings key read from client-reachable code";

	private static readonly LocalizableString MessageFormat =
		"Appsettings key '{0}' is ServerOnly (not client-whitelisted) but is read from '{1}', which runs on "
		+ "the WASM client — it will be null at runtime. Decide: (1) if the client genuinely needs this value, "
		+ "whitelist it as 'ClientRead' (or 'ClientWriteLocal'/'ClientWriteServer') under "
		+ "NotNotAppSettings:whitelist in appsettings*.json; (2) if it is server-only/secret, move this read to "
		+ "a .Server-side type — do NOT whitelist server-only/secret config (that exposes it to the client); "
		+ "(3) #pragma warning disable NN_C005 only if this code path provably never runs on the client.";

	private static readonly LocalizableString Description =
		".Shared is a Razor Class Library that runs in BOTH the server and the WASM client; .Client is "
		+ "client-only. NotNot.AppSettings prunes ServerOnly keys from the generated client snapshot "
		+ "(_ClientAppSettings), so reading one from client-reachable code yields null at runtime — and with "
		+ ".Require() throws at bootstrap, the same failure as the NN_C004 companion. NN_C005 is the "
		+ "read-reachability companion to NN_C004's write-default rule. The fix is a DECISION, not a reflex: "
		+ "whitelist ONLY if the client genuinely needs the value, otherwise move the read server-side — never "
		+ "whitelist a server-only secret just to silence the diagnostic.";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: Title,
		messageFormat: MessageFormat,
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#nn_c005",
		customTags: new[] { "CodeStyle", "Conventions" });

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();

		// None => never invoked for nodes in generated code (the generated *.g.cs settings files
		// themselves read their own ServerOnly members; that is not a client-reachability smell).
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

		context.RegisterCompilationStartAction(static start =>
		{
			// Assembly-suffix reachability gate (same fence NN_LDDD_001 uses): only .Shared / .Client
			// compilations run on the client. Server assemblies are exempt.
			if (!IsSharedOrClientAssembly(start.Compilation.Assembly))
				return;

			start.RegisterSyntaxNodeAction(
				AnalyzeMemberAccess,
				SyntaxKind.SimpleMemberAccessExpression,
				SyntaxKind.MemberBindingExpression);
		});
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeMemberAccess");

		// Both `settings.Key` (member access) and `settings?.Key` (member binding) carry a `.Name`.
		var nameNode = context.Node switch
		{
			MemberAccessExpressionSyntax ma => ma.Name,
			MemberBindingExpressionSyntax mb => mb.Name,
			_ => null,
		};
		if (nameNode is null)
			return;

		// `nameof(settings.Key)` does not dereference the value at runtime → never null-crashes → exempt.
		if (IsInsideNameOf(context.Node))
			return;

		var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
		if (symbol is not IPropertySymbol and not IFieldSymbol)
			return;

		var containingType = symbol.ContainingType;
		if (containingType is null || !IsAppSettingsGeneratedType(containingType))
			return;

		var ns = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
		var parts = ns.Split('.');
		var genIndex = Array.IndexOf(parts, GenNamespaceSegment);
		if (genIndex < 0)
			return; // not in the .AppSettingsGen tree
		var genNs = string.Join(".", parts.Take(genIndex + 1));

		// The _ClientAppSettings mirror tree is already the pruned/safe view — reads of it never crash.
		if (string.Equals(containingType.Name, ClientRootTypeName, StringComparison.Ordinal))
			return;
		if (ns.IndexOf("." + ClientSubtreeSegment, StringComparison.Ordinal) >= 0)
			return;

		var compilation = context.SemanticModel.Compilation;

		// Locate the client mirror root: its presence proves the client snapshot is part of THIS
		// compilation. Absent → conservatively silent (cannot prove the key is ServerOnly here).
		var clientRoot = compilation.GetTypeByMetadataName(genNs + "." + ClientRootTypeName);
		if (clientRoot is null)
			return;

		var counterpart = ResolveClientCounterpart(compilation, containingType, ns, genNs, clientRoot);

		var whitelisted = counterpart is not null
			&& counterpart.GetMembers(symbol.Name).Any(m => m is IPropertySymbol or IFieldSymbol);
		if (whitelisted)
			return; // present in the client mirror → client-whitelisted → safe

		// Absent from the client mirror → ServerOnly → report at the accessed-member identifier.
		var keyPath = ComputeKeyPath(symbol, containingType, ns, genNs);
		var assemblyName = compilation.Assembly.Identity.Name;
		context.ReportDiagnostic(Diagnostic.Create(Rule, nameNode.GetLocation(), keyPath, assemblyName));
	}

	/// <summary>
	/// Maps a FULL-tree generated type to its <c>_ClientAppSettings</c> mirror counterpart by swapping the
	/// subtree-root namespace segment (<c>_AppSettings</c> → <c>_ClientAppSettingsTypes</c>), or the root
	/// (<c>AppSettings</c> → <c>_ClientAppSettings</c>). Returns <c>null</c> when no counterpart exists (the
	/// whole section was pruned ⇒ ServerOnly).
	/// </summary>
	private static INamedTypeSymbol? ResolveClientCounterpart(
		Compilation compilation, INamedTypeSymbol fullType, string ns, string genNs, INamedTypeSymbol clientRoot)
	{
		if (string.Equals(fullType.Name, FullRootTypeName, StringComparison.Ordinal)
			&& string.Equals(ns, genNs, StringComparison.Ordinal))
		{
			return clientRoot;
		}

		var fullSubtreePrefix = genNs + "." + FullSubtreeSegment;
		if (!ns.StartsWith(fullSubtreePrefix, StringComparison.Ordinal))
			return null; // unexpected shape → treat as no counterpart (conservative-silent via clientRoot gate)

		var suffix = ns.Substring(fullSubtreePrefix.Length); // "" or "._Sessions..."
		var clientNs = genNs + "." + ClientSubtreeSegment + suffix;
		return compilation.GetTypeByMetadataName(clientNs + "." + fullType.Name);
	}

	/// <summary>
	/// Builds a human-readable dotted key path (e.g. <c>Sessions.SecretToken</c>) from the generated
	/// type-namespace chain plus the member name. Root-level scalars return just the member name.
	/// </summary>
	private static string ComputeKeyPath(ISymbol member, INamedTypeSymbol containingType, string ns, string genNs)
	{
		if (string.Equals(containingType.Name, FullRootTypeName, StringComparison.Ordinal)
			&& string.Equals(ns, genNs, StringComparison.Ordinal))
		{
			return member.Name;
		}

		var segments = new List<string>();
		var fullSubtreePrefix = genNs + "." + FullSubtreeSegment;
		if (ns.StartsWith(fullSubtreePrefix, StringComparison.Ordinal))
		{
			var suffix = ns.Substring(fullSubtreePrefix.Length); // "" or "._Sessions._Window"
			foreach (var seg in suffix.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
				segments.Add(seg.TrimStart('_'));
		}

		segments.Add(containingType.Name);
		segments.Add(member.Name);
		return string.Join(".", segments);
	}

	/// <summary>
	/// True when <paramref name="node"/> is nested inside a <c>nameof(...)</c> invocation (bounded walk).
	/// </summary>
	private static bool IsInsideNameOf(SyntaxNode node)
	{
		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
				return true;

			// Bound the walk at statement / member-declaration boundaries.
			if (current is StatementSyntax or MemberDeclarationSyntax)
				break;
		}

		return false;
	}

	/// <summary>
	/// True iff the assembly identity name ends with <c>.Shared</c> or <c>.Client</c> (<see cref="StringComparison.Ordinal"/>).
	/// Inlined equivalent of <c>NotNot.BlazorAnalyzers.LiteDDD.LdddAnalyzerHelpers.IsSharedOrClientAssembly</c>
	/// (intentionally no cross-package dependency on BlazorAnalyzers).
	/// </summary>
	private static bool IsSharedOrClientAssembly(IAssemblySymbol? assembly)
	{
		if (assembly is null)
			return false;

		var name = assembly.Identity.Name;
		return name.EndsWith(".Shared", StringComparison.Ordinal)
			|| name.EndsWith(".Client", StringComparison.Ordinal);
	}

	/// <summary>
	/// True iff <paramref name="type"/> carries <c>[System.CodeDom.Compiler.GeneratedCode("NotNot.AppSettings", ...)]</c>.
	/// Matches on the attribute FQN + the tool-name constructor argument — robust to namespace/version (same
	/// recognition predicate as NN_C004's <c>AppSettingsCodeDefaultAnalyzer</c>).
	/// </summary>
	private static bool IsAppSettingsGeneratedType(INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass is null)
				continue;

			var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fullName.StartsWith("global::", StringComparison.Ordinal))
				fullName = fullName.Substring("global::".Length);

			if (!string.Equals(fullName, GeneratedCodeAttributeFullName, StringComparison.Ordinal))
				continue;

			var ctorArgs = attribute.ConstructorArguments;
			if (ctorArgs.Length >= 1
				&& ctorArgs[0].Value is string toolName
				&& string.Equals(toolName, AppSettingsToolName, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
