using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NotNot.Analyzers.Architecture.DI;

/// <summary>
/// Dependency-injection marker-enforcement analyzer hosting six diagnostic rules
/// (<c>NN_DI_001</c> through <c>NN_DI_006</c>) for the project's
/// <c>IDi{Singleton,Scoped,Transient}Service</c> marker-interface auto-registration convention.
/// Mirrors the pattern of <c>NotNot.BlazorAnalyzers.LiteDDD.LdddAssemblyFenceAnalyzer</c> —
/// single analyzer class with regions per rule, shared compilation-start gate, helper extraction
/// to <see cref="DiAnalyzerHelpers"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rule overview</b>:
/// <list type="number">
///   <item><description>
///     <b>NN_DI_001</b> — Lifetime mismatch with marker interface (Error). Fires on
///     <c>Add{L}&lt;T&gt;()</c> or <c>TryAdd{L}&lt;T&gt;()</c> where <c>T</c> implements
///     <c>IDi{L'}Service</c> with <c>L != L'</c>. Lifetime conflicts are correctness bugs
///     regardless of TryAdd vs Add semantics — both branches fire.
///   </description></item>
///   <item><description>
///     <b>NN_DI_002</b> — Redundant explicit registration (Warning). Fires on
///     <c>Add{L}&lt;T&gt;()</c> where <c>T</c> implements <c>IDi{L}Service</c> (matched lifetime,
///     so auto-registration would already handle it). Skipped when the registration is an
///     interface-bridge factory (<see cref="DiAnalyzerHelpers.IsInterfaceBridgeFactory"/>),
///     a <c>TryAdd*</c> variant (deliberate conditional registration), or the type is
///     third-party (<see cref="DiAnalyzerHelpers.IsThirdPartyType"/>).
///   </description></item>
///   <item><description>
///     <b>NN_DI_003</b> — Passthrough factory eligible for auto-registration (Info). Fires on
///     <c>Add{L}&lt;T&gt;(sp =&gt; new T(sp.GetRequiredService&lt;...&gt;(), ...))</c> when
///     <c>T</c> is project-defined (not third-party) and does not already implement a marker
///     interface (otherwise NN_DI_002 covers it). Suggests adding the appropriate
///     <c>IDi{L}Service</c> marker and removing the factory lambda.
///   </description></item>
///   <item><description>
///     <b>NN_DI_004</b> — Marker interface combined with <c>IHostedService</c> (Error). Fires on
///     a class symbol implementing both a marker interface and
///     <c>Microsoft.Extensions.Hosting.IHostedService</c> — the two registration paths conflict
///     (marker → auto-register as service; <c>IHostedService</c> → register via
///     <c>AddHostedService</c>; both can produce duplicate instances or surprise singleton-vs-scoped
///     lifetimes). <b>Third-party-type carve-out unnecessary</b>: <see cref="AnalyzeNamedType"/>
///     is registered as a <c>SymbolAction</c> over <c>SymbolKind.NamedType</c>, which only fires
///     on types DECLARED in the current compilation; types imported from referenced assemblies
///     (third-party, framework) never reach the analyzer here (Wave 1 H3-demoted review finding).
///   </description></item>
///   <item><description>
///     <b>NN_DI_005</b> — Missing marker for project-internal candidate (Info). Fires on
///     <c>Add{L}&lt;T&gt;()</c> or <c>Add{L}&lt;TService, TImpl&gt;()</c> where the implementation
///     type is project-internal, NOT a <c>TryAdd*</c> variant, NOT an interface-bridge factory,
///     NOT third-party, NOT <c>[AutoDiBypass]</c>'d, is a non-abstract class, and lacks any
///     <c>IDi{L}Service</c> marker. Suggests adding the marker interface and removing the
///     explicit registration. NN_DI_005 is the inverse of NN_DI_002 and the residual case of
///     NN_DI_003 (NN_DI_003 handles passthrough-factory shapes; NN_DI_005 handles every other
///     no-marker registration shape).
///   </description></item>
///   <item><description>
///     <b>NN_DI_006</b> — Hosted service with a required delegate constructor parameter (Error).
///     Fires on a concrete (non-abstract) <c>IHostedService</c> (e.g. <c>BackgroundService</c>)
///     whose single public instance constructor has a REQUIRED (<c>!HasExplicitDefaultValue</c>)
///     delegate-typed parameter (<c>Func&lt;&gt;</c>/<c>Action&lt;&gt;</c>/custom delegate). The
///     NotNot Scrutor <c>AsSelfWithInterfaces</c> auto-registration over <c>IHostedService</c>
///     uses constructor injection; DI never registers a delegate, so the concrete registration is
///     unconstructible and crashes host startup (Dev <c>ValidateOnBuild</c> at <c>builder.Build()</c>
///     / Prod <c>Host.StartAsync</c>) BEFORE any port binds. Category Reliability/Error.
///     <b>Marker-INDEPENDENT</b>: fires whether or not the type carries an <c>IDi{L}Service</c>
///     marker — it analyzes ctor-unconstructibility, not marker presence. Gap-filling third rule of
///     the hosted-service matrix (NN_DI_004 = marker+IHostedService conflict; NN_DI_005 =
///     marker-absence; NN_DI_006 = ctor-unconstructibility). Skips zero / more-than-one public
///     instance ctors (ambiguous MS.DI greedy-resolve / <c>[ActivatorUtilitiesConstructor]</c> —
///     conservative near-zero-FP bias for an Error rule).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Bypass mechanisms</b>:
/// <list type="bullet">
///   <item><description>
///     <c>[assembly: AutoDiBypass]</c> — compilation-level short-circuit; no rule fires.
///   </description></item>
///   <item><description>
///     <c>[AutoDiBypass]</c> on a class — type-scope bypass; rules NN_DI_001/002/003/005 (when their
///     target type is the bypassed type) and NN_DI_004/006 (when applied to the analyzed class) skip
///     (the shared type-scope <c>[AutoDiBypass]</c> guard in <see cref="AnalyzeNamedType"/> returns
///     before both the NN_DI_006 and NN_DI_004 branches).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Wave 1 scope limitation — registered-type-only inspection</b>: All five rules inspect the
/// REGISTERED service type's marker (the generic-arg of <c>Add{L}&lt;T&gt;()</c>), NOT the
/// constructed inner type returned by a factory body. A factory body that constructs a wrong-
/// lifetime concrete with <c>new ConcreteImpl(...)</c> beneath an interface-typed registration —
/// e.g. <c>AddTransient&lt;IService&gt;(sp =&gt; new SingletonImpl(...))</c> where
/// <c>SingletonImpl : IDiSingletonService</c> — will NOT fire NN_DI_001 even though the
/// registration creates wrong-lifetime instances. The interface (<c>IService</c>) carries no
/// marker, so <c>TryGetExpectedLifetime</c> returns false on the registered type and the rule
/// short-circuits. Walking factory bodies to inspect the inner concrete is deferred to a future
/// wave — the lock-in test
/// <c>InterfaceBridgeFactory_WithInnerSingletonImpl_DoesNotFireYet_Wave1ScopeLimitation</c>
/// pins this behavior so a future enhancement that flips the assertion is detectable as a
/// behavioral change rather than a silent regression.
/// </para>
/// <para>
/// <b>NN_DI_003 / NN_DI_005 mutual exclusion via control flow</b>: NN_DI_005's predicate is a
/// superset of NN_DI_003's (both fire on unmarked, non-third-party, non-TryAdd registrations).
/// NN_DI_003 is the more-specific diagnostic (passthrough factory shape); when it fires, the
/// developer's actionable suggestion is identical to NN_DI_005's. To prevent double-firing on
/// the same registration, NN_DI_003's emission block ends with an explicit <c>return;</c> —
/// control never reaches NN_DI_005's branch when NN_DI_003 has already reported. This is the
/// canonical pattern (NN_DI_001's lifetime-mismatch block also returns to suppress NN_DI_002/003).
/// </para>
/// <para>
/// <b>Heuristic-tuning decisions for NN_DI_003</b> (Info severity, lowest FP cost — see
/// VibeConsider §3.5):
/// <list type="bullet">
///   <item><description>
///     <b>Already-marked types fire NN_DI_002, not NN_DI_003</b>. If the concrete type returned by
///     the passthrough factory already implements a marker, the rule that better describes the
///     situation is "redundant registration" (NN_DI_002), not "eligible for auto-registration"
///     (NN_DI_003). Reduces double-firing on the same invocation.
///   </description></item>
///   <item><description>
///     <b>Third-party types are skipped</b>. NN_DI_003 suggests "add a marker interface" — for
///     types the user doesn't author, that suggestion is impossible to act on.
///   </description></item>
///   <item><description>
///     <b>TryAdd* variants are skipped</b>. <c>TryAddSingleton&lt;X&gt;(sp =&gt; new X(...))</c> is
///     deliberate conditional registration; the user is explicitly opting out of unconditional
///     auto-registration.
///   </description></item>
///   <item><description>
///     <b>Open-generic registrations are skipped</b>. Marker-interface auto-registration applies
///     to closed types; open-generic patterns (<c>AddSingleton(typeof(IFoo&lt;&gt;), typeof(Foo&lt;&gt;))</c>)
///     have a different registration model entirely.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DiMarkerEnforcementAnalyzer : DiagnosticAnalyzer
{
	// ── Categories ────────────────────────────────────────────────────────────

	private const string ReliabilityCategory = "Reliability";
	private const string DesignCategory = "Design";

	// ── Microsoft.Extensions.DependencyInjection namespace prefix ─────────────

	/// <summary>
	/// Namespace prefix used to recognize <c>IServiceCollection</c> extension methods. The DI
	/// extension methods live in <c>Microsoft.Extensions.DependencyInjection</c> and
	/// <c>Microsoft.Extensions.DependencyInjection.Extensions</c>; matching by namespace prefix
	/// avoids false positives where a third-party type happens to expose an <c>AddSingleton</c>
	/// method.
	/// </summary>
	private const string MsDiNamespacePrefix = "Microsoft.Extensions.DependencyInjection";

	/// <summary>
	/// Fully-qualified name of <c>Microsoft.Extensions.Hosting.IHostedService</c>. Compared against
	/// stripped-<c>global::</c> display string per the convention in <see cref="DiAnalyzerHelpers"/>.
	/// </summary>
	private const string HostedServiceFullName = "Microsoft.Extensions.Hosting.IHostedService";

	// ── NN_DI_001 — Lifetime mismatch with marker interface ──────────────────

	/// <summary>Diagnostic ID for DI lifetime mismatch with marker interface.</summary>
	public const string DI001_DiagnosticId = "NN_DI_001";

	private static readonly LocalizableString DI001_Title =
		"DI lifetime mismatch with marker interface";

	private static readonly LocalizableString DI001_MessageFormat =
		"Type '{0}' is registered as {1} but implements marker '{2}' which requires lifetime '{3}'. "
		+ "Either change the registration to 'Add{3}<{0}>()' or remove the marker interface and "
		+ "register with the intended lifetime. (NN_DI_001)";

	private static readonly LocalizableString DI001_Description =
		"The project's auto-registration system inspects IDi{Singleton,Scoped,Transient}Service "
		+ "marker interfaces to determine lifetime. An explicit Add{L}<T>() registration with "
		+ "lifetime L that differs from the type's marker is a correctness bug — depending on which "
		+ "registration wins, either auto-registration or the explicit call will produce instances "
		+ "with the wrong lifetime. Apply [AutoDiBypass] on the type if the divergence is intentional.";

	/// <summary>NN_DI_001 descriptor — Error severity.</summary>
	public static readonly DiagnosticDescriptor DI001_Rule = new(
		DI001_DiagnosticId,
		DI001_Title,
		DI001_MessageFormat,
		ReliabilityCategory,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: DI001_Description,
		helpLinkUri: DiAnalyzerHelpers.HelpBase + "nn_di_001");

	// ── NN_DI_002 — Redundant explicit DI registration ───────────────────────

	/// <summary>Diagnostic ID for redundant explicit DI registration.</summary>
	public const string DI002_DiagnosticId = "NN_DI_002";

	private static readonly LocalizableString DI002_Title =
		"Redundant explicit DI registration";

	private static readonly LocalizableString DI002_MessageFormat =
		"Type '{0}' implements marker '{1}' and is already auto-registered with lifetime '{2}' — "
		+ "the explicit 'Add{2}<{0}>()' call is redundant. Remove the call, or apply [AutoDiBypass] "
		+ "on the type if the explicit registration is intentional (e.g. multiple service contracts, "
		+ "custom decoration). (NN_DI_002)";

	private static readonly LocalizableString DI002_Description =
		"When a type implements an IDi{L}Service marker interface, the project's auto-registration "
		+ "system already wires it up with lifetime L. An explicit Add{L}<T>() call with the matching "
		+ "lifetime duplicates that registration. The DI container's last-registration-wins or "
		+ "duplicate-resolves-as-IEnumerable<T> semantics may cause subtle bugs.";

	/// <summary>NN_DI_002 descriptor — Warning severity.</summary>
	public static readonly DiagnosticDescriptor DI002_Rule = new(
		DI002_DiagnosticId,
		DI002_Title,
		DI002_MessageFormat,
		DesignCategory,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: DI002_Description,
		helpLinkUri: DiAnalyzerHelpers.HelpBase + "nn_di_002");

	// ── NN_DI_003 — Passthrough DI factory replaceable with marker interface ──

	/// <summary>Diagnostic ID for passthrough DI factory eligible for marker-interface auto-registration.</summary>
	public const string DI003_DiagnosticId = "NN_DI_003";

	private static readonly LocalizableString DI003_Title =
		"Passthrough DI factory replaceable with marker interface";

	private static readonly LocalizableString DI003_MessageFormat =
		"Factory registration 'Add{0}<{1}>(sp => new {1}(...))' is a passthrough that resolves all "
		+ "constructor arguments via IServiceProvider — add the 'IDi{0}Service' marker interface to "
		+ "'{1}' and remove the factory lambda; auto-registration will handle the same wiring. "
		+ "(NN_DI_003)";

	private static readonly LocalizableString DI003_Description =
		"A factory lambda whose body is exactly 'new T(sp.GetRequiredService<...>(), ...)' is "
		+ "behaviorally equivalent to the auto-registration the marker interface provides. Migrating "
		+ "from explicit factory to marker interface reduces composition-root surface area and makes "
		+ "the registration intent declarative. Apply [AutoDiBypass] if the factory exists for a "
		+ "reason not captured by this analyzer (e.g. side effects in construction, multiple service "
		+ "contracts).";

	/// <summary>NN_DI_003 descriptor — Info severity (highest FP risk per VibeConsider §3.5).</summary>
	public static readonly DiagnosticDescriptor DI003_Rule = new(
		DI003_DiagnosticId,
		DI003_Title,
		DI003_MessageFormat,
		DesignCategory,
		DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		description: DI003_Description,
		helpLinkUri: DiAnalyzerHelpers.HelpBase + "nn_di_003");

	// ── NN_DI_004 — DI service marker conflicts with IHostedService ──────────

	/// <summary>Diagnostic ID for DI service marker combined with IHostedService.</summary>
	public const string DI004_DiagnosticId = "NN_DI_004";

	private static readonly LocalizableString DI004_Title =
		"DI service marker conflicts with IHostedService";

	private static readonly LocalizableString DI004_MessageFormat =
		"Type '{0}' implements both '{1}' (DI service marker, lifetime '{2}') and "
		+ "'Microsoft.Extensions.Hosting.IHostedService'. The two registration paths conflict — "
		+ "auto-registration via the marker plus AddHostedService<T>() produces duplicate instances "
		+ "or surprise lifetime mismatches. Remove the marker interface (use AddHostedService<T>() "
		+ "exclusively) or remove IHostedService (and register the type's lifecycle hooks differently). "
		+ "(NN_DI_004)";

	private static readonly LocalizableString DI004_Description =
		"IHostedService implementations are wired into the generic-host startup pipeline via "
		+ "AddHostedService<T>(), which adds the type as a singleton AND enumerates StartAsync hooks. "
		+ "When the same type also carries an IDi{L}Service marker, the auto-registration system "
		+ "wires it up as an independent service registration, defeating the singleton contract and "
		+ "potentially producing two instances. This is almost always a developer mistake.";

	/// <summary>NN_DI_004 descriptor — Error severity.</summary>
	public static readonly DiagnosticDescriptor DI004_Rule = new(
		DI004_DiagnosticId,
		DI004_Title,
		DI004_MessageFormat,
		ReliabilityCategory,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: DI004_Description,
		helpLinkUri: DiAnalyzerHelpers.HelpBase + "nn_di_004");

	// ── NN_DI_005 — Missing marker for project-internal candidate ────────────

	/// <summary>Diagnostic ID for missing marker on project-internal auto-registration candidate.</summary>
	public const string DI005_DiagnosticId = "NN_DI_005";

	private static readonly LocalizableString DI005_Title =
		"Missing IDi{L}Service marker — class is auto-registration candidate";

	private static readonly LocalizableString DI005_MessageFormat =
		"Class '{0}' is registered manually as '{1}' but could implement IDi{1}Service for auto-registration";

	private static readonly LocalizableString DI005_Description =
		"When a project-internal class is registered explicitly via Add{L}<T>() without implementing "
		+ "the matching IDi{L}Service marker, the analyzer suggests migrating to marker-based "
		+ "auto-registration. Reduces composition-root surface area and makes the registration "
		+ "declarative. Apply [AutoDiBypass] if the explicit registration exists for a reason not "
		+ "captured by this analyzer (e.g. ordered registration with other rules, multiple service "
		+ "contracts, side effects). The rule severity can be tuned via .editorconfig with "
		+ "'dotnet_diagnostic.NN_DI_005.severity = {warning|error|silent|none}'.";

	/// <summary>NN_DI_005 descriptor — Info severity (mirrors NN_DI_003 FP-risk-aware shipping profile).</summary>
	public static readonly DiagnosticDescriptor DI005_Rule = new(
		DI005_DiagnosticId,
		DI005_Title,
		DI005_MessageFormat,
		DesignCategory,
		DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		description: DI005_Description,
		helpLinkUri: DiAnalyzerHelpers.HelpBase + "nn_di_005");

	// ── NN_DI_006 — Hosted service required delegate ctor param DI can't provide ─

	/// <summary>Diagnostic ID for a hosted service whose required constructor delegate parameter DI cannot provide.</summary>
	public const string DI006_DiagnosticId = "NN_DI_006";

	private static readonly LocalizableString DI006_Title =
		"Hosted service has a required delegate constructor parameter dependency injection cannot provide";

	private static readonly LocalizableString DI006_MessageFormat =
		"Hosted service '{0}' has a required constructor parameter '{1}' of delegate type '{2}' that "
		+ "dependency injection cannot provide — auto-registration (NotNot Scrutor AsSelfWithInterfaces "
		+ "over IHostedService) will fail to construct it and crash host startup before the port binds. "
		+ "Fix (preferred first): (1) replace the delegate with a DI-registered abstraction — define a "
		+ "small interface implemented by the providing service and inject that (e.g. ISessionReapPurger "
		+ "implemented by VowSessionService); (2) only if a null delegate is a SAFE no-op for the "
		+ "auto-registered instance, make the parameter optional with a default ('{2}? {1} = null') — NOT "
		+ "if absence silently disables required behavior; (3) [AutoDiBypass] on '{0}' or "
		+ "'#pragma warning disable NN_DI_006' ONLY if '{0}' is provably never auto-registered/DI-constructed. "
		+ "(NN_DI_006)";

	private static readonly LocalizableString DI006_Description =
		"The auto-registration convention registers every IHostedService AsSelfWithInterfaces with "
		+ "constructor injection; delegate types (Func<>/Action<>) are never DI-registered, so a REQUIRED "
		+ "delegate ctor parameter makes the concrete registration unconstructible and crashes boot. An "
		+ "OPTIONAL delegate parameter (with a default) is safe — DI passes null. Prefer a registered "
		+ "interface abstraction over a raw delegate so the dependency is declarative and resolvable. "
		+ "Apply [AutoDiBypass] on the type, or #pragma/.editorconfig severity, only when the type is "
		+ "genuinely hand-constructed and never reaches the auto-registration scan.";

	/// <summary>NN_DI_006 descriptor — Error severity.</summary>
	public static readonly DiagnosticDescriptor DI006_Rule = new(
		DI006_DiagnosticId,
		DI006_Title,
		DI006_MessageFormat,
		ReliabilityCategory,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: DI006_Description,
		helpLinkUri: DiAnalyzerHelpers.HelpBase + "nn_di_006");

	// ── DiagnosticAnalyzer overrides ──────────────────────────────────────────

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(DI001_Rule, DI002_Rule, DI003_Rule, DI004_Rule, DI005_Rule, DI006_Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		// Compilation-start gate handles assembly-level [AutoDiBypass] short-circuit AND
		// avoids registering operation actions in compilations that don't reference NotNot.Bcl.Core
		// (where the marker interfaces don't resolve).
		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	// ── Compilation-start gate ────────────────────────────────────────────────

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		var compilation = context.Compilation;

		// Assembly-level bypass — entire compilation opts out of the rule family.
		if (DiAnalyzerHelpers.HasAutoDiBypassAttribute(compilation.Assembly))
		{
			return;
		}

		// Probe whether the marker interfaces resolve in this compilation. Per the GLOBAL-namespace
		// note in DiAnalyzerHelpers, the markers are declared without a namespace directive — they
		// resolve as global symbols. `GetTypeByMetadataName` accepts metadata-format names; for a
		// type with no namespace, the metadata name IS the simple type name.
		var hasAnyMarker =
			compilation.GetTypeByMetadataName(DiAnalyzerHelpers.DiSingletonServiceFullName) != null
			|| compilation.GetTypeByMetadataName(DiAnalyzerHelpers.DiScopedServiceFullName) != null
			|| compilation.GetTypeByMetadataName(DiAnalyzerHelpers.DiTransientServiceFullName) != null;

		if (!hasAnyMarker)
		{
			// Compilation doesn't reference NotNot.Bcl.Core — nothing to enforce.
			return;
		}

		// Register invocation-level rules (NN_DI_001/002/003/005).
		context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);

		// Register symbol-level rule (NN_DI_004).
		context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
	}

	/// <summary>
	/// Returns true when <paramref name="type"/> implements ANY of the three
	/// <c>IDi{Singleton,Scoped,Transient}Service</c> markers. Unlike
	/// <c>DiAnalyzerHelpers.TryGetExpectedLifetime</c>, this helper returns true for multi-marker
	/// classes too — the user IS using the marker pattern even when their type carries multiple
	/// markers. Used by NN_DI_005's "type already uses the pattern" carve-out so that the rule
	/// stays silent on classes the developer has already opted in (single-marker correct case is
	/// covered by <c>hasMarker</c>; multi-marker case must be explicitly excluded since
	/// <c>TryGetExpectedLifetime</c> returns <c>false</c> for it).
	/// </summary>
	private static bool TypeImplementsAnyMarker(INamedTypeSymbol? type)
	{
		if (type == null)
		{
			return false;
		}
		return DiAnalyzerHelpers.ImplementsMarkerInterface(type, DiAnalyzerHelpers.DiSingletonServiceFullName)
			|| DiAnalyzerHelpers.ImplementsMarkerInterface(type, DiAnalyzerHelpers.DiScopedServiceFullName)
			|| DiAnalyzerHelpers.ImplementsMarkerInterface(type, DiAnalyzerHelpers.DiTransientServiceFullName);
	}

	// ── Invocation-level rules (NN_DI_001 / NN_DI_002 / NN_DI_003 / NN_DI_005) ─

	private static void AnalyzeInvocation(OperationAnalysisContext context)
	{
		var invocation = (IInvocationOperation)context.Operation;
		var targetMethod = invocation.TargetMethod;

		// Cheap method-name pre-filter — mirrors the IsLikelyTypeOrNamespaceReference optimization
		// in LdddAnalyzerHelpers. Most invocations in a compilation are NOT DI registrations.
		if (!TryClassifyLifetimeMethod(targetMethod, out var calledLifetime, out var isTryAdd))
		{
			return;
		}

		// Containing-namespace check — confirms this is a Microsoft.Extensions.DependencyInjection
		// extension method rather than a user-defined `AddSingleton` on something unrelated.
		if (!IsInMsDiNamespace(targetMethod.ContainingType))
		{
			return;
		}

		// Resolve the service-type and implementation-type from the call's type arguments.
		// `Add{L}<T>()`              → service=impl=T
		// `Add{L}<TService, TImpl>()` → service=TService, impl=TImpl
		// Any other shape (non-generic `Add{L}(typeof(...), typeof(...))` or open generics) → skip.
		if (!TryResolveGenericTypes(targetMethod, out var implType))
		{
			return;
		}

		// Type-level [AutoDiBypass] on the implementation type — skip the entire registration.
		if (DiAnalyzerHelpers.HasAutoDiBypassAttribute(implType))
		{
			return;
		}

		var hasMarker = DiAnalyzerHelpers.TryGetExpectedLifetime(implType, out var expectedLifetime, out var markerFqn);

		// ── NN_DI_001 — Lifetime mismatch ─────────────────────────────────────
		if (hasMarker
			&& !string.Equals(expectedLifetime, calledLifetime, StringComparison.Ordinal))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				DI001_Rule,
				invocation.Syntax.GetLocation(),
				implType.ToDisplayString(),
				calledLifetime,
				markerFqn,
				expectedLifetime));
			// Don't continue to NN_DI_002/003 — the registration is already broken, no point
			// double-firing on the same invocation.
			return;
		}

		// ── NN_DI_002 — Redundant explicit registration ───────────────────────
		// Matched-lifetime registration of a marker-bearing type. Skip when:
		//   - TryAdd*: deliberate conditional registration.
		//   - Interface-bridge factory: deliberate interface-to-impl bridge.
		//   - Third-party type: the user can't add a marker to a type they don't own (this is
		//     the only way to register it).
		if (hasMarker
			&& string.Equals(expectedLifetime, calledLifetime, StringComparison.Ordinal)
			&& !isTryAdd
			&& !DiAnalyzerHelpers.IsInterfaceBridgeFactory(invocation)
			&& !DiAnalyzerHelpers.IsThirdPartyType(implType))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				DI002_Rule,
				invocation.Syntax.GetLocation(),
				implType.ToDisplayString(),
				markerFqn,
				calledLifetime));
			return;
		}

		// ── NN_DI_003 — Passthrough factory ───────────────────────────────────
		// Fires when:
		//   - Factory body is `new T(sp.GetRequiredService<...>(), ...)` (helper validates this).
		//   - Concrete type is NOT third-party (we can't suggest adding a marker to a type the
		//     user doesn't own).
		//   - Concrete type does NOT already implement a marker (would have hit NN_DI_002 above).
		//   - Not a TryAdd* (deliberate conditional registration).
		// Perf (Wave 1 M5 review): when concreteType is reference-equal to implType (the common case
		// for `Add{L}<T>(sp => new T(...))`), reuse the cached `hasMarker` flag instead of issuing a
		// second `TryGetExpectedLifetime` walk on the same type.
		if (!isTryAdd
			&& !hasMarker
			&& DiAnalyzerHelpers.IsPassthroughFactory(invocation, out var concreteType)
			&& concreteType != null
			&& !DiAnalyzerHelpers.HasAutoDiBypassAttribute(concreteType)
			&& !DiAnalyzerHelpers.IsThirdPartyType(concreteType)
			&& !ConcreteTypeHasMarker(concreteType, implType, hasMarker))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				DI003_Rule,
				invocation.Syntax.GetLocation(),
				calledLifetime,
				concreteType.ToDisplayString()));
			// Return after NN_DI_003 emission — NN_DI_005's predicate is a superset and would
			// double-fire on the same registration. NN_DI_005 is the residual "missing marker"
			// case for non-passthrough-factory shapes (see class-header remarks for full rationale).
			return;
		}

		// ── NN_DI_005 — Missing marker for project-internal candidate ─────────
		// Fires when the registration is an Add{L}<T>() / Add{L}<TService, TImpl>() shape with an
		// unmarked, project-internal, non-abstract class as the implementation. Skip when:
		//   - TryAdd*: deliberate conditional registration.
		//   - Third-party: user doesn't own the type, can't add a marker.
		//   - Interface-bridge factory: deliberate interface→impl bridge using GetRequiredService.
		//   - Type is abstract / interface / non-class: can't be auto-registered as concrete.
		//   - Type implements ANY marker (even multi-marker): suggestion "add a marker" is
		//     meaningless when the class already opts into the pattern; multi-marker ambiguity is
		//     surfaced by other rules / not by NN_DI_005. The `!TypeImplementsAnyMarker` predicate
		//     catches the multi-marker case which `!hasMarker` misses (because
		//     `TryGetExpectedLifetime` returns false on multi-marker — leaving the predicate true).
		// Mutual exclusion with NN_DI_001/002/003: NN_DI_001 and NN_DI_002 short-circuit via their
		// own `return;` when fired; NN_DI_003 short-circuits via the `return;` immediately above.
		// `hasMarker` is the cached result from line ~347 (Wave 1 F2 perf reuse — no recomputation).
		if (!isTryAdd
			&& !hasMarker
			&& !TypeImplementsAnyMarker(implType)
			&& implType.TypeKind == TypeKind.Class
			&& !implType.IsAbstract
			&& !DiAnalyzerHelpers.IsThirdPartyType(implType)
			&& !DiAnalyzerHelpers.IsInterfaceBridgeFactory(invocation))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				DI005_Rule,
				invocation.Syntax.GetLocation(),
				implType.ToDisplayString(),
				calledLifetime));
		}
	}

	/// <summary>
	/// Determines whether <paramref name="concreteType"/> carries a marker interface, reusing the
	/// cached <paramref name="implTypeHasMarker"/> result when <paramref name="concreteType"/> is
	/// semantically equal to <paramref name="implType"/>. Saves one <c>AllInterfaces</c> walk on the
	/// common <c>Add{L}&lt;T&gt;(sp =&gt; new T(...))</c> shape (Wave 1 M5 review perf fix).
	/// </summary>
	private static bool ConcreteTypeHasMarker(
		INamedTypeSymbol concreteType,
		INamedTypeSymbol? implType,
		bool implTypeHasMarker)
	{
		if (implType != null && SymbolEqualityComparer.Default.Equals(concreteType, implType))
		{
			return implTypeHasMarker;
		}
		return DiAnalyzerHelpers.TryGetExpectedLifetime(concreteType, out _, out _);
	}

	// ── Symbol-level rule (NN_DI_004) ─────────────────────────────────────────

	/// <summary>
	/// Analyzes a named-type symbol declaration for NN_DI_004 — marker + IHostedService conflict.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Third-party-type carve-out unnecessary</b> (Wave 1 H3-demoted review finding): this method
	/// is registered via <c>RegisterSymbolAction</c> over <see cref="SymbolKind.NamedType"/>, which
	/// only fires for types DECLARED in the current compilation. Types imported from referenced
	/// assemblies (third-party packages, framework libraries) are not surfaced to the symbol-action
	/// pipeline, so an explicit <c>IsThirdPartyType</c> guard would be dead code. The
	/// <c>NN_DI_002</c>/<c>NN_DI_003</c>/<c>NN_DI_005</c> branches DO need that guard because they
	/// fire on invocation operations that can reference third-party types via their generic arguments.
	/// </para>
	/// </remarks>
	private static void AnalyzeNamedType(SymbolAnalysisContext context)
	{
		var namedType = (INamedTypeSymbol)context.Symbol;

		// Skip interfaces, abstract types, and structs — IHostedService is only meaningfully
		// implemented by concrete classes.
		if (namedType.TypeKind != TypeKind.Class || namedType.IsAbstract)
		{
			return;
		}

		// Type-level [AutoDiBypass] on the class.
		if (DiAnalyzerHelpers.HasAutoDiBypassAttribute(namedType))
		{
			return;
		}

		// ── NN_DI_006 — Hosted service with a required delegate ctor parameter ──
		// Marker-INDEPENDENT: fires on ctor-unconstructibility regardless of marker presence, so it
		// must run BEFORE the NN_DI_004 marker-gate `return` below (the proven crash type carries no
		// IDi*Service marker). A concrete IHostedService whose single public constructor requires a
		// delegate-typed parameter is unconstructible under the NotNot Scrutor AsSelfWithInterfaces
		// auto-registration (DI never supplies a Func<>/Action<>), crashing host startup before the
		// port binds. NN_DI_004 and NN_DI_006 fire on disjoint conditions — a type hitting both is two
		// distinct defects, so no mutual-exclusion return.
		if (ImplementsHostedService(namedType)
			&& TryGetSinglePublicInstanceConstructor(namedType, out var di006Ctor))
		{
			foreach (var param in di006Ctor.Parameters)
			{
				if (!param.HasExplicitDefaultValue && param.Type.TypeKind == TypeKind.Delegate)
				{
					var di006Location = namedType.Locations.Length > 0
						? namedType.Locations[0]
						: Location.None;

					context.ReportDiagnostic(Diagnostic.Create(
						DI006_Rule,
						di006Location,
						namedType.ToDisplayString(),
						param.Name,
						param.Type.ToDisplayString()));
					break;
				}
			}
		}

		// Marker check — short-circuits if no marker present.
		if (!DiAnalyzerHelpers.TryGetExpectedLifetime(namedType, out var lifetime, out var markerFqn))
		{
			return;
		}

		// IHostedService check — walks `AllInterfaces` per the helper's convention.
		if (!ImplementsHostedService(namedType))
		{
			return;
		}

		// Report on the class declaration location.
		var location = namedType.Locations.Length > 0
			? namedType.Locations[0]
			: Location.None;

		context.ReportDiagnostic(Diagnostic.Create(
			DI004_Rule,
			location,
			namedType.ToDisplayString(),
			markerFqn,
			lifetime));
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Attempts to classify <paramref name="method"/> as one of
	/// <c>{AddSingleton,AddScoped,AddTransient,TryAddSingleton,TryAddScoped,TryAddTransient}</c>.
	/// Returns true when matched, with <paramref name="lifetime"/> set to
	/// <c>"Singleton"</c>/<c>"Scoped"</c>/<c>"Transient"</c> and <paramref name="isTryAdd"/>
	/// indicating whether the method name has the <c>TryAdd</c> prefix.
	/// </summary>
	/// <remarks>
	/// Match is by method name only at this stage — the containing-namespace check (in
	/// <see cref="IsInMsDiNamespace"/>) further validates the method is genuinely an MS.DI
	/// extension. Combined with the cheap pre-filter character of this check, the analyzer
	/// avoids paying for semantic resolution on the vast majority of invocations.
	/// </remarks>
	private static bool TryClassifyLifetimeMethod(IMethodSymbol method, out string lifetime, out bool isTryAdd)
	{
		lifetime = string.Empty;
		isTryAdd = false;

		// Wave 1+2 explicitly do not analyze keyed registrations (AddKeyedSingleton/Scoped/Transient
		// + TryAddKeyed*) — different registration model that the marker-interface convention doesn't
		// address. May revisit in a future wave.
		switch (method.Name)
		{
			case "AddSingleton": lifetime = "Singleton"; return true;
			case "AddScoped": lifetime = "Scoped"; return true;
			case "AddTransient": lifetime = "Transient"; return true;
			case "TryAddSingleton": lifetime = "Singleton"; isTryAdd = true; return true;
			case "TryAddScoped": lifetime = "Scoped"; isTryAdd = true; return true;
			case "TryAddTransient": lifetime = "Transient"; isTryAdd = true; return true;
			default: return false;
		}
	}

	/// <summary>
	/// Returns true when <paramref name="type"/>'s containing namespace begins with
	/// <c>Microsoft.Extensions.DependencyInjection</c>. The MS.DI extension methods live in either
	/// <c>Microsoft.Extensions.DependencyInjection</c> (ServiceCollectionServiceExtensions) or
	/// <c>Microsoft.Extensions.DependencyInjection.Extensions</c>
	/// (ServiceCollectionDescriptorExtensions) — both are covered by the prefix match.
	/// </summary>
	private static bool IsInMsDiNamespace(INamedTypeSymbol? type)
	{
		var ns = type?.ContainingNamespace;
		if (ns == null || ns.IsGlobalNamespace)
		{
			return false;
		}

		var displayName = ns.ToDisplayString();
		return displayName.StartsWith(MsDiNamespacePrefix, StringComparison.Ordinal);
	}

	/// <summary>
	/// Resolves the implementation-type of a generic <c>Add{L}&lt;...&gt;()</c> registration.
	/// Returns true and sets <paramref name="implType"/> when the call shape is supported:
	/// <list type="bullet">
	///   <item><description><c>Add{L}&lt;T&gt;()</c> → <c>implType = T</c></description></item>
	///   <item><description><c>Add{L}&lt;TService, TImpl&gt;()</c> → <c>implType = TImpl</c></description></item>
	/// </list>
	/// Returns false for non-generic shapes (<c>Add{L}(typeof(...), typeof(...))</c>) and for
	/// open-generic registrations (any type argument that is an <see cref="ITypeParameterSymbol"/>).
	/// </summary>
	/// <remarks>
	/// Non-generic and open-generic shapes are out of scope per VibeConsider §3.6 — they use a
	/// different registration model that the marker-interface convention doesn't address.
	/// </remarks>
	private static bool TryResolveGenericTypes(IMethodSymbol method, out INamedTypeSymbol? implType)
	{
		implType = null;

		var typeArgs = method.TypeArguments;
		if (typeArgs.Length == 0 || typeArgs.Length > 2)
		{
			// Non-generic overloads (e.g. `Add(typeof(IService), typeof(Impl))`) or higher-arity
			// shapes we don't recognize. Skip.
			return false;
		}

		// Reject open-generic registrations — any type-argument that's a type-parameter symbol
		// means we're inside an open generic context (e.g. an extension method `AddMyService<T>()`
		// that forwards to `AddSingleton<T>()`). We can't reason about marker interfaces on a
		// type parameter.
		foreach (var arg in typeArgs)
		{
			if (arg is ITypeParameterSymbol)
			{
				return false;
			}
		}

		// `Add{L}<T>()`: TypeArguments[0] is the impl.
		// `Add{L}<TService, TImpl>()`: TypeArguments[1] is the impl.
		var resolved = typeArgs[typeArgs.Length - 1] as INamedTypeSymbol;
		if (resolved == null)
		{
			return false;
		}

		implType = resolved;
		return true;
	}

	/// <summary>
	/// Returns true when <paramref name="type"/> implements
	/// <c>Microsoft.Extensions.Hosting.IHostedService</c> (directly, via inheritance, or via any
	/// base type). Walks <see cref="INamedTypeSymbol.AllInterfaces"/> and matches by fully-qualified
	/// name with the <c>global::</c> prefix stripped — same convention used by
	/// <see cref="DiAnalyzerHelpers.ImplementsMarkerInterface"/>.
	/// </summary>
	private static bool ImplementsHostedService(INamedTypeSymbol type)
	{
		foreach (var iface in type.AllInterfaces)
		{
			var display = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			var stripped = display.StartsWith("global::", StringComparison.Ordinal)
				? display.Substring("global::".Length)
				: display;

			if (string.Equals(stripped, HostedServiceFullName, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns true and the single public instance constructor of <paramref name="type"/> when EXACTLY
	/// one exists; false (with <paramref name="constructor"/> null) for zero or more-than-one public
	/// instance constructors. Multi-ctor disambiguation is undecidable here (MS.DI greedy resolution /
	/// <c>[ActivatorUtilitiesConstructor]</c>), so NN_DI_006 conservatively skips ambiguous types —
	/// the near-zero-false-positive bias appropriate for an Error-severity rule.
	/// </summary>
	private static bool TryGetSinglePublicInstanceConstructor(INamedTypeSymbol type, out IMethodSymbol constructor)
	{
		constructor = null!;
		foreach (var ctor in type.InstanceConstructors)
		{
			if (ctor.DeclaredAccessibility != Accessibility.Public)
			{
				continue;
			}
			if (constructor != null)
			{
				// More than one public instance constructor — ambiguous, skip.
				constructor = null!;
				return false;
			}
			constructor = ctor;
		}
		return constructor != null;
	}
}
