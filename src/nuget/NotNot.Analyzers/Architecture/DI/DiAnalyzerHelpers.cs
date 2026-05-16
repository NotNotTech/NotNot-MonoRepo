using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace NotNot.Analyzers.Architecture.DI;

/// <summary>
/// Shared helpers for the DI marker-enforcement analyzer family
/// (<c>NN_DI_001</c> through <c>NN_DI_005</c>). Mirrors the helper-extraction strategy
/// used by <c>NotNot.BlazorAnalyzers.LiteDDD.LdddAnalyzerHelpers</c> (sibling project).
/// </summary>
/// <remarks>
/// <para>
/// FQN constants for <c>IDiSingletonService</c> / <c>IDiScopedService</c> / <c>IDiTransientService</c>
/// are intentionally NOT prefixed with a containing namespace — the canonical declarations in
/// <c>NotNot.Bcl.Core/NotNot/DI/_DIServiceMarkers.cs</c> sit in the GLOBAL namespace
/// (no <c>namespace</c> directive in the source file, and <c>NotNot.Bcl.Core.csproj</c> sets
/// <c>&lt;RootNamespace&gt;&lt;/RootNamespace&gt;</c> to empty). Roslyn emits these symbols via
/// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> as <c>global::IDiSingletonService</c>;
/// the comparison helper <see cref="ImplementsMarkerInterface(INamedTypeSymbol, string)"/>
/// strips the <c>global::</c> prefix before comparing against these constants.
/// </para>
/// <para>
/// Bypass detection (<see cref="HasAutoDiBypassAttribute(IAssemblySymbol)"/> /
/// <see cref="HasAutoDiBypassAttribute(ISymbol)"/>) matches by fully-qualified name ONLY —
/// consumers MUST reference the canonical <c>NotNot.Bcl.Diagnostics.AutoDiBypassAttribute</c>
/// from <c>NotNot.Bcl.Core</c>. Wave 1 H2 review finding REMOVED the simple-name fallback that
/// the <c>[LdddBypass]</c> precedent allows: <c>AutoDiBypassAttribute</c> is brand-new and has
/// no consumer-side namespace precedent, so the simple-name match would risk silent rule
/// disabling by an unrelated local attribute. Bypass attributes are <em>not</em> inherited
/// (mirrors <c>[LdddBypass]</c> with <c>Inherited=false</c>).
/// </para>
/// </remarks>
internal static class DiAnalyzerHelpers
{
	/// <summary>
	/// Documentation URL prefix for the DI analyzer family (mirrors the BlazorAnalyzers convention).
	/// </summary>
	internal const string HelpBase =
		"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers#";

	/// <summary>
	/// Fully-qualified name of the <c>IDiSingletonService</c> marker interface.
	/// Declared in <c>NotNot.Bcl.Core/NotNot/DI/_DIServiceMarkers.cs</c> at GLOBAL namespace scope
	/// (the source file has no <c>namespace</c> directive and the csproj's <c>RootNamespace</c> is empty).
	/// </summary>
	internal const string DiSingletonServiceFullName = "IDiSingletonService";

	/// <summary>
	/// Fully-qualified name of the <c>IDiScopedService</c> marker interface. See remarks on
	/// <see cref="DiSingletonServiceFullName"/> for global-namespace rationale.
	/// </summary>
	internal const string DiScopedServiceFullName = "IDiScopedService";

	/// <summary>
	/// Fully-qualified name of the <c>IDiTransientService</c> marker interface. See remarks on
	/// <see cref="DiSingletonServiceFullName"/> for global-namespace rationale.
	/// </summary>
	internal const string DiTransientServiceFullName = "IDiTransientService";

	/// <summary>
	/// Fully-qualified name of the canonical <c>[AutoDiBypass]</c> attribute. Lives in
	/// <c>NotNot.Bcl.Core/NotNot/Diagnostics/AutoDiBypassAttribute.cs</c> (Group A — sibling implementer).
	/// Match is FQN-only (Wave 1 H2 review finding) — locally-declared bypass attributes in
	/// unrelated namespaces are NOT honored, eliminating the silent-rule-disabling risk a
	/// simple-name match would create.
	/// </summary>
	internal const string AutoDiBypassAttributeFullName = "NotNot.Bcl.Diagnostics.AutoDiBypassAttribute";

	/// <summary>
	/// Returns true when the compilation's assembly carries an <c>[assembly: AutoDiBypass]</c>
	/// marker. Matched by fully-qualified name only (<c>NotNot.Bcl.Diagnostics.AutoDiBypassAttribute</c>) —
	/// see <see cref="IsAutoDiBypassAttributeClass"/> for rationale (H2 review finding, Wave 1).
	/// Cost: O(N) over assembly attributes (typically N&lt;10).
	/// </summary>
	internal static bool HasAutoDiBypassAttribute(IAssemblySymbol? assembly)
	{
		if (assembly == null)
		{
			return false;
		}

		foreach (var attribute in assembly.GetAttributes())
		{
			if (IsAutoDiBypassAttributeClass(attribute.AttributeClass))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Symbol-scope bypass check — returns true when the type/method carries an
	/// <c>[AutoDiBypass]</c> attribute on its <em>own</em> attribute list. Does NOT walk
	/// containing symbols (mirrors <c>[LdddBypass]</c> with <c>Inherited=false</c>). For
	/// assembly-level short-circuit, use <see cref="HasAutoDiBypassAttribute(IAssemblySymbol)"/>.
	/// </summary>
	internal static bool HasAutoDiBypassAttribute(ISymbol? symbol)
	{
		if (symbol == null)
		{
			return false;
		}

		foreach (var attribute in symbol.GetAttributes())
		{
			if (IsAutoDiBypassAttributeClass(attribute.AttributeClass))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns true when the named-type symbol implements the given marker interface
	/// (matched by fully-qualified name with <c>global::</c> prefix stripped) either directly,
	/// via inheritance, or via any base type. Walks <see cref="INamedTypeSymbol.AllInterfaces"/>.
	/// </summary>
	/// <param name="type">Type to inspect.</param>
	/// <param name="markerFqn">Fully-qualified name of the marker interface (without <c>global::</c> prefix).</param>
	/// <remarks>
	/// Marker interfaces are inherited by convention — a class deriving from a class that implements
	/// <c>IDiSingletonService</c> is still a singleton from the auto-registration system's POV.
	/// <see cref="INamedTypeSymbol.AllInterfaces"/> already includes inherited interfaces from base
	/// types, so a single check suffices (no manual base-walk required).
	/// </remarks>
	internal static bool ImplementsMarkerInterface(INamedTypeSymbol? type, string markerFqn)
	{
		if (type == null || string.IsNullOrEmpty(markerFqn))
		{
			return false;
		}

		foreach (var iface in type.AllInterfaces)
		{
			if (string.Equals(StripGlobalPrefix(iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
				markerFqn, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns the lifetime ("Singleton" / "Scoped" / "Transient") declared by the type's marker
	/// interface, if-and-only-if the type implements <em>exactly one</em> of the three markers.
	/// Returns false (with <paramref name="lifetime"/> and <paramref name="markerFqn"/> set to empty)
	/// when the type implements zero or more-than-one marker — the latter is a developer mistake
	/// that should be surfaced separately by a higher-level rule rather than picked arbitrarily.
	/// </summary>
	internal static bool TryGetExpectedLifetime(INamedTypeSymbol? type, out string lifetime, out string markerFqn)
	{
		lifetime = string.Empty;
		markerFqn = string.Empty;

		if (type == null)
		{
			return false;
		}

		var hits = 0;
		string foundLifetime = string.Empty;
		string foundMarker = string.Empty;

		if (ImplementsMarkerInterface(type, DiSingletonServiceFullName))
		{
			hits++;
			foundLifetime = "Singleton";
			foundMarker = DiSingletonServiceFullName;
		}
		if (ImplementsMarkerInterface(type, DiScopedServiceFullName))
		{
			hits++;
			foundLifetime = "Scoped";
			foundMarker = DiScopedServiceFullName;
		}
		if (ImplementsMarkerInterface(type, DiTransientServiceFullName))
		{
			hits++;
			foundLifetime = "Transient";
			foundMarker = DiTransientServiceFullName;
		}

		if (hits != 1)
		{
			return false;
		}

		lifetime = foundLifetime;
		markerFqn = foundMarker;
		return true;
	}

	/// <summary>
	/// Returns true when an <c>Add{L}&lt;T&gt;</c> invocation's factory lambda matches the
	/// passthrough shape: lambda body is exactly a single <c>new T(...)</c> expression whose
	/// constructor arguments are <em>all</em> <c>sp.GetRequiredService&lt;...&gt;()</c> calls
	/// (or its <c>GetService</c> sibling), AND the constructor has at least one such argument.
	/// Out-param <paramref name="concreteType"/> is set to the constructed type when the match
	/// succeeds.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Distinguishes the auto-registration-eligible factory shape from non-trivial factories
	/// (I/O, conditionals, caching) which must NOT be flagged. The lambda body must contain
	/// exactly one return value — an <see cref="IObjectCreationOperation"/> — with no other
	/// statements (logging, mutation, branching). The single-statement constraint is
	/// expression-bodied-lambda OR a block-bodied lambda containing exactly one <c>return new T(...)</c>.
	/// </para>
	/// <para>
	/// <b>Wave 2 design tightening</b>: a passthrough factory MUST bridge at least one
	/// DI-resolved dependency. Parameterless constructor bodies (<c>sp =&gt; new T()</c>),
	/// object-initializer bodies (<c>sp =&gt; new T { Prop = x }</c>), and any factory with no
	/// <c>GetRequiredService</c> arguments are NOT classified as passthrough — they have no DI
	/// dependency to "pass through". Such factories fall through to NN_DI_005 (Wave 2's
	/// missing-marker advisory), which correctly handles the broader "non-passthrough, unmarked
	/// project-internal class" case. Wave 1's M1 review disposition (which included zero-arg
	/// as passthrough) is REVERSED by this change — zero-arg `new T()` is structurally identical
	/// to `Add{L}&lt;T&gt;()` (no factory needed), and NN_DI_005 is the more-precise diagnostic
	/// for "unmarked class registered explicitly".
	/// </para>
	/// <para>
	/// Also rejects object-initializer expressions: <c>new T { Prop = value }</c> sets
	/// <see cref="IObjectCreationOperation.Initializer"/> to a non-null
	/// <see cref="IObjectOrCollectionInitializerOperation"/>. The object-initializer carries
	/// non-DI state (property values from the lambda closure or literals) — replacing with a
	/// marker interface would silently drop that state, which is a behavior-changing
	/// suggestion. NN_DI_005 still fires on the unmarked class — the developer reviews and
	/// decides whether the initializer is essential.
	/// </para>
	/// <para>
	/// The cast-unwrap loop at the start of body-extraction (M3 review fix) allows
	/// <c>sp =&gt; (IService)new ServiceImpl(...)</c> to also classify as passthrough — the
	/// surrounding cast is operationally a no-op for the registration semantics.
	/// </para>
	/// </remarks>
	internal static bool IsPassthroughFactory(IInvocationOperation? invocation, out INamedTypeSymbol? concreteType)
	{
		concreteType = null;

		if (invocation == null || invocation.Arguments.Length == 0)
		{
			return false;
		}

		// Find the lambda argument (typically `Add{L}<T>(sp => ...)` has a single Func<IServiceProvider, T> arg).
		var lambda = ExtractFactoryLambda(invocation);
		if (lambda == null)
		{
			return false;
		}

		// Find the single returned expression. Block-bodied lambdas may wrap it in a return statement.
		// Unwrap any wrapping conversions (cast operator on the lambda's return value, e.g.
		// `sp => (IService)new ServiceImpl(...)`) — Wave 1 M3 review fix; matches the existing
		// cast-unwrap loop in IsServiceProviderGetCall.
		var returned = UnwrapConversions(ExtractSingleReturnedOperation(lambda));
		if (returned is not IObjectCreationOperation objectCreation)
		{
			return false;
		}

		// Wave 2 tightening: reject object-initializer bodies (`new T { Prop = x }`). The
		// initializer carries non-DI state — replacing with a marker would silently drop it.
		if (objectCreation.Initializer != null)
		{
			return false;
		}

		// Wave 2 tightening: require AT LEAST ONE DI-resolved constructor argument. Parameterless
		// `new T()` is structurally identical to `Add{L}<T>()` (no factory needed); NN_DI_005 is
		// the more-precise diagnostic for "unmarked class registered explicitly". Replaces Wave 1
		// M1 review disposition (which included zero-arg as passthrough).
		if (objectCreation.Arguments.Length == 0)
		{
			return false;
		}

		// Every constructor argument must be a `sp.GetRequiredService<...>()` (or GetService) call.
		foreach (var arg in objectCreation.Arguments)
		{
			if (!IsServiceProviderGetCall(arg.Value))
			{
				return false;
			}
		}

		concreteType = objectCreation.Type as INamedTypeSymbol;
		return concreteType != null;
	}

	/// <summary>
	/// Returns true when an <c>Add{L}&lt;TInterface&gt;</c> invocation's factory lambda matches the
	/// interface-bridge shape: lambda body is exactly a single
	/// <c>sp.GetRequiredService&lt;TImpl&gt;()</c> call (NOT <c>new TImpl(...)</c>). This is the
	/// carve-out that must NOT flag <c>NN_DI_002</c> (redundant-registration) — the developer is
	/// deliberately bridging an interface registration to an existing concrete registration.
	/// </summary>
	/// <remarks>
	/// Detection contract per <c>VibeConsider §3.1</c>: lambda body is precisely a
	/// <see cref="IInvocationOperation"/> whose method name is <c>GetRequiredService</c> or
	/// <c>GetService</c>. Any deviation (object creation, multiple statements, conditional)
	/// fails the match and the surrounding rule decides whether to flag.
	/// </remarks>
	internal static bool IsInterfaceBridgeFactory(IInvocationOperation? invocation)
	{
		if (invocation == null || invocation.Arguments.Length == 0)
		{
			return false;
		}

		var lambda = ExtractFactoryLambda(invocation);
		if (lambda == null)
		{
			return false;
		}

		// Unwrap wrapping conversions (cast operator on the lambda's return value, e.g.
		// `sp => (IService)sp.GetRequiredService<ServiceImpl>()`) — Wave 1 M3 review fix.
		var returned = UnwrapConversions(ExtractSingleReturnedOperation(lambda));
		return returned is IInvocationOperation innerInvocation
			&& IsServiceProviderGetMethod(innerInvocation.TargetMethod);
	}

	/// <summary>
	/// Returns true when the type's containing assembly matches the third-party-assembly ignore list
	/// (Microsoft.*, System.*, Azure.*, MudBlazor*, Serilog*, netstandard*, NotNot.*). Used by the
	/// analyzer to skip enforcement on registrations of third-party types the user didn't author.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>List divergence from runtime scanIgnore</b> (Wave 1 L1 review clarification): the
	/// runtime Scrutor-scan ignore list in
	/// <c>NotNot.Bcl.Core/NotNot/DI/zz_Extensions_IServiceCollection_DI.cs</c> currently excludes
	/// <c>Microsoft.*</c>, <c>netstandard*</c>, <c>Serilog*</c>, <c>System*</c>, <c>Azure*</c>.
	/// The analyzer's list adds <c>MudBlazor*</c> as defensive — even though the runtime scanIgnore
	/// doesn't include it, user code rarely owns <c>MudBlazor.*</c> namespace types, so treating
	/// them as third-party here avoids surface noise where the developer cannot add a marker
	/// interface anyway. The asymmetry is intentional: the analyzer is allowed to be MORE
	/// conservative (skip more sites) than the runtime — false-negatives in the analyzer are
	/// preferable to false-positives that suggest impossible fixes.
	/// </para>
	/// <para>
	/// <b><c>NotNot.*</c> prefix inclusion</b> (Wave 2 audit finding — Program.cs FP-rate
	/// calibration): the <c>NotNot.*</c> assembly family contains internal-but-pre-marker types
	/// (e.g. <c>NotNot.Bcl.Core/NotNot/SimpleStorageManager&lt;T&gt;</c>) that PRE-DATE the DI
	/// marker family. Consumer code registers these via <c>AddSingleton&lt;SimpleStorageManager&lt;T&gt;&gt;()</c>
	/// at the composition root, but the consuming project doesn't own the type — it cannot add a
	/// marker interface to a sibling-primitive declared in a different package. Without this
	/// carve-out, the empirical FP rate for NN_DI_005 against <c>Novaleaf.VibeOverwatch</c>'s
	/// <c>Program.cs</c> would be 15.4% (4/26 sites); with it, 8.3% (2/24). The <c>NotNot.*</c>
	/// inclusion treats sibling-primitive types as third-party for the purposes of the DI rule
	/// family — they may eventually grow markers (durable migration path), but until then the
	/// analyzer must not suggest impossible fixes.
	/// </para>
	/// </remarks>
	internal static bool IsThirdPartyType(ITypeSymbol? type)
	{
		if (type == null)
		{
			return false;
		}

		var assemblyName = type.ContainingAssembly?.Identity.Name;
		if (string.IsNullOrEmpty(assemblyName))
		{
			return false;
		}

		return assemblyName!.StartsWith("Microsoft.", StringComparison.Ordinal)
			|| assemblyName!.StartsWith("System.", StringComparison.Ordinal)
			|| assemblyName!.StartsWith("Azure.", StringComparison.Ordinal)
			|| assemblyName!.StartsWith("MudBlazor", StringComparison.Ordinal)
			|| assemblyName!.StartsWith("Serilog", StringComparison.Ordinal)
			|| assemblyName!.StartsWith("netstandard", StringComparison.Ordinal)
			|| assemblyName!.StartsWith("NotNot.", StringComparison.Ordinal);
	}

	// ── Internal helpers ───────────────────────────────────────────────────

	/// <summary>
	/// Returns true when <paramref name="attrClass"/> matches the canonical <c>[AutoDiBypass]</c>
	/// attribute by fully-qualified name only (<c>NotNot.Bcl.Diagnostics.AutoDiBypassAttribute</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Simple-name matching was intentionally REMOVED in Wave 1 (H2 review finding). Rationale:
	/// <c>AutoDiBypassAttribute</c> is a brand-new symbol introduced by this PR. Unlike the
	/// <c>[LdddBypass]</c> precedent (which has years of consumer-side precedent and a stable
	/// simple-name expectation), there is no consumer-side namespace precedent for this attribute,
	/// so the standard FQN-only match is safer: a consumer codebase that happens to declare a
	/// local <c>[AutoDiBypass]</c> attribute in an unrelated namespace would otherwise silently
	/// suppress this entire analyzer family, which is HIGH-impact-when-it-happens (silent rule
	/// disabling = production correctness bugs).
	/// </para>
	/// <para>
	/// Consumers MUST reference the canonical attribute from <c>NotNot.Bcl.Core</c>. Locally-
	/// declared bypass attributes (even with the identical simple name) are not honored.
	/// </para>
	/// </remarks>
	private static bool IsAutoDiBypassAttributeClass(INamedTypeSymbol? attrClass)
	{
		if (attrClass == null)
		{
			return false;
		}

		// FQN-only match — strip `global::` prefix per the convention used by
		// ImplementsMarkerInterface, then compare against the canonical name.
		var fullName = StripGlobalPrefix(attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		return string.Equals(fullName, AutoDiBypassAttributeFullName, StringComparison.Ordinal);
	}

	/// <summary>
	/// Strips the <c>global::</c> prefix produced by <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>.
	/// </summary>
	private static string StripGlobalPrefix(string fullyQualified)
	{
		const string GlobalPrefix = "global::";
		return fullyQualified.StartsWith(GlobalPrefix, StringComparison.Ordinal)
			? fullyQualified.Substring(GlobalPrefix.Length)
			: fullyQualified;
	}

	/// <summary>
	/// Extracts the factory lambda argument from an <c>Add{L}&lt;T&gt;(sp =&gt; ...)</c> invocation.
	/// Walks argument operations and any wrapping <see cref="IDelegateCreationOperation"/> or
	/// <see cref="IConversionOperation"/> nodes that Roslyn inserts when a lambda is converted
	/// to a delegate type.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Lambda extraction filters by parameter type (Wave 1 M2 review fix): only arguments whose
	/// parameter is <c>Func&lt;IServiceProvider, T&gt;</c> (or <c>Func&lt;IServiceProvider, object&gt;</c>
	/// / <c>Func&lt;IServiceProvider, object?&gt;</c>) qualify. This prevents binding to a non-factory
	/// lambda when user-defined extension methods sit in the <c>Microsoft.Extensions.DependencyInjection</c>
	/// namespace and expose richer overloads (e.g. an <c>Action&lt;HostBuilderContext, IServiceProvider, T&gt;</c>
	/// parameter alongside a factory delegate).
	/// </para>
	/// <para>
	/// <b>Assumption</b>: the canonical Microsoft <c>Add{L}&lt;T&gt;</c> overloads accept the factory
	/// as <c>Func&lt;IServiceProvider, TService&gt;</c> (1-arg) or
	/// <c>Func&lt;IServiceProvider, TImplementation&gt;</c> (2-arg). When the analyzer is invoked
	/// against a custom DI extension whose factory signature differs, this filter will reject the
	/// extension's lambda — by design.
	/// </para>
	/// </remarks>
	private static IAnonymousFunctionOperation? ExtractFactoryLambda(IInvocationOperation invocation)
	{
		foreach (var arg in invocation.Arguments)
		{
			if (!IsFactoryDelegateParameter(arg.Parameter?.Type))
			{
				continue;
			}

			var lambda = UnwrapToLambda(arg.Value);
			if (lambda != null)
			{
				return lambda;
			}
		}
		return null;
	}

	/// <summary>
	/// Returns true when <paramref name="parameterType"/> matches the canonical MS.DI factory
	/// delegate shape: <c>Func&lt;IServiceProvider, T&gt;</c> for any <c>T</c>. Accepts both
	/// generic <c>T</c> and the open-erased <c>Func&lt;IServiceProvider, object[?]&gt;</c> form.
	/// Wave 1 M2 review fix.
	/// </summary>
	private static bool IsFactoryDelegateParameter(ITypeSymbol? parameterType)
	{
		if (parameterType is not INamedTypeSymbol named
			|| !named.IsGenericType
			|| named.TypeArguments.Length != 2
			|| !string.Equals(named.Name, "Func", StringComparison.Ordinal))
		{
			return false;
		}

		// First type-arg must be IServiceProvider (by FQN, ignoring `global::` prefix).
		var first = named.TypeArguments[0];
		var firstFqn = StripGlobalPrefix(first.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
		return string.Equals(firstFqn, "System.IServiceProvider", StringComparison.Ordinal);
	}

	/// <summary>Unwraps conversion / delegate-creation operations to find a nested anonymous function.</summary>
	private static IAnonymousFunctionOperation? UnwrapToLambda(IOperation? operation)
	{
		var current = operation;
		// Defensive depth bound — operation trees in practice nest at most ~3 layers.
		for (var depth = 0; depth < 8 && current != null; depth++)
		{
			if (current is IAnonymousFunctionOperation lambda)
			{
				return lambda;
			}
			if (current is IDelegateCreationOperation delegateCreation)
			{
				current = delegateCreation.Target;
				continue;
			}
			if (current is IConversionOperation conversion)
			{
				current = conversion.Operand;
				continue;
			}
			break;
		}
		return null;
	}

	/// <summary>
	/// Returns the single value-producing operation inside a lambda body, or null when the body
	/// contains anything other than a single return / expression. Block-bodied lambdas with
	/// statements beyond <c>return X;</c> return null (which disqualifies them from the
	/// passthrough/interface-bridge fast-path).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Detection scope is body-level only</b> (Wave 1 M4 review clarification). A body-level
	/// conditional like <c>sp =&gt; cond ? new A(sp.GetRequiredService&lt;X&gt;()) : new B(...)</c>
	/// is correctly disqualified — the single-statement returned value is an
	/// <see cref="IConditionalOperation"/>, not an <see cref="IObjectCreationOperation"/>, so the
	/// passthrough/interface-bridge pattern-matching fails.
	/// </para>
	/// <para>
	/// <b>Scope-level conditionals are NOT carved out by this helper</b>. A registration site like
	/// <c>if (env.IsDevelopment()) { sc.AddSingleton&lt;X&gt;(sp =&gt; new X(...)); }</c> will still
	/// surface the <c>AddSingleton</c> invocation to <c>AnalyzeInvocation</c> exactly as if the
	/// <c>if</c> wrapper were absent — Roslyn's operation tree positions each invocation independently
	/// and the analyzer does not walk parent statement nodes. Scope-level <c>if</c>/<c>switch</c>
	/// gating is intentionally NOT a carve-out: the developer's intent ("register only under
	/// condition X") doesn't change whether the marker-based path would express the same wiring,
	/// it changes when the registration is added — orthogonal concern. Use <c>[AutoDiBypass]</c> on
	/// the type if a scope-gated explicit registration shouldn't trigger the rule family.
	/// </para>
	/// </remarks>
	private static IOperation? ExtractSingleReturnedOperation(IAnonymousFunctionOperation lambda)
	{
		var body = lambda.Body;
		if (body == null)
		{
			return null;
		}

		// Block-bodied lambda: body is IBlockOperation. We accept exactly one statement which
		// must be either an IReturnOperation or an IExpressionStatementOperation wrapping the value.
		// Expression-bodied lambda: Roslyn still wraps the result in IBlockOperation containing a
		// single IReturnOperation, so unified handling works.
		if (body.Operations.Length != 1)
		{
			return null;
		}

		var only = body.Operations[0];
		if (only is IReturnOperation ret)
		{
			return ret.ReturnedValue;
		}
		if (only is IExpressionStatementOperation expr)
		{
			return expr.Operation;
		}
		return only;
	}

	/// <summary>
	/// Returns true when the operation is an <c>IServiceProvider.GetRequiredService&lt;T&gt;()</c>
	/// or <c>IServiceProvider.GetService&lt;T&gt;()</c> call. Used by the passthrough classifier
	/// to validate that every constructor argument resolves a dependency from the service provider.
	/// </summary>
	private static bool IsServiceProviderGetCall(IOperation? operation)
	{
		// Strip wrapping conversions (covariance / generic erasure inserts conversions).
		var current = UnwrapConversions(operation);

		return current is IInvocationOperation invocation
			&& IsServiceProviderGetMethod(invocation.TargetMethod);
	}

	/// <summary>
	/// Strips wrapping <see cref="IConversionOperation"/> nodes (cast operator, covariance,
	/// nullable widening) up to a bounded depth. Returns the inner operand unchanged when no
	/// conversion wrapping is present. Bounded at depth 4 — operation trees in practice nest at
	/// most ~3 conversion layers (cast + nullable + covariance); the bound is a defensive guard
	/// against malformed semantic models.
	/// </summary>
	/// <remarks>
	/// Used by both <see cref="IsPassthroughFactory"/> and <see cref="IsInterfaceBridgeFactory"/>
	/// to detect factory shapes that wrap their returned value in a cast (e.g.
	/// <c>sp =&gt; (IService)new ServiceImpl(...)</c>) — Wave 1 M3 review fix. Also used by
	/// <see cref="IsServiceProviderGetCall"/> to strip conversions Roslyn inserts on
	/// argument-binding (covariance, generic erasure).
	/// </remarks>
	private static IOperation? UnwrapConversions(IOperation? operation)
	{
		var current = operation;
		for (var depth = 0; depth < 4 && current is IConversionOperation conv; depth++)
		{
			current = conv.Operand;
		}
		return current;
	}

	/// <summary>
	/// Returns true when the method symbol is <c>GetRequiredService</c> or <c>GetService</c>
	/// (either the generic extension on <see cref="IServiceProvider"/> from
	/// <c>Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions</c>, or the
	/// non-generic <c>IServiceProvider.GetService(Type)</c>). Match is by method name only —
	/// the surrounding context (<c>Add{L}&lt;T&gt;</c> registration site) already implies the
	/// caller is wired against DI.
	/// </summary>
	private static bool IsServiceProviderGetMethod(IMethodSymbol? method)
	{
		if (method == null)
		{
			return false;
		}
		return string.Equals(method.Name, "GetRequiredService", StringComparison.Ordinal)
			|| string.Equals(method.Name, "GetService", StringComparison.Ordinal);
	}
}
