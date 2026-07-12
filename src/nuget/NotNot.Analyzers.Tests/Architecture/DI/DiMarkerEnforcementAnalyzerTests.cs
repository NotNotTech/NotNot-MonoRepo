using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture.DI;

namespace NotNot.Analyzers.Tests.Architecture.DI;

/// <summary>
/// Tests for <see cref="DiMarkerEnforcementAnalyzer"/> — NN_DI_001 (lifetime mismatch),
/// NN_DI_002 (redundant registration), NN_DI_003 (passthrough factory),
/// NN_DI_004 (marker + IHostedService), NN_DI_005 (missing marker — auto-registration candidate),
/// NN_DI_006 (hosted service with a required delegate ctor parameter), NN_DI_007 (explicit
/// registration of a scan-eligible hosted service), plus cross-cutting bypass
/// behavior.
/// </summary>
/// <remarks>
/// <para>
/// Test sources reference the real <c>IDi{Singleton,Scoped,Transient}Service</c> markers from
/// <c>NotNot.Bcl.Core</c> (global namespace per the canonical declaration) and the real
/// <c>NotNot.Bcl.Diagnostics.AutoDiBypassAttribute</c>. Microsoft DI extension methods
/// (<c>AddSingleton</c>/<c>AddScoped</c>/<c>AddTransient</c> + their <c>TryAdd*</c> variants)
/// come from <c>Microsoft.Extensions.Hosting</c>'s transitive package graph.
/// </para>
/// <para>
/// Each test fixture builds a self-contained C# source string that compiles cleanly, sets
/// <see cref="CSharpAnalyzerTest{TAnalyzer,TVerifier}.CompilerDiagnostics"/> to
/// <see cref="CompilerDiagnostics.None"/> to ignore unrelated compiler warnings, and uses the
/// <c>{|#N:expr|}</c> markup convention for diagnostic-location assertions (same pattern as
/// <c>MaybeReturnContractAnalyzerTests</c> in this project).
/// </para>
/// </remarks>
public class DiMarkerEnforcementAnalyzerTests
{
	// ── Shared infrastructure ────────────────────────────────────────────────

	/// <summary>
	/// Custom <see cref="ReferenceAssemblies"/> targeting net10.0. The framework's built-in
	/// <c>ReferenceAssemblies.Net.Net80</c>/<c>.Net90</c> pin <c>System.Runtime</c> at v8/v9; the
	/// additional-references we add (resolved at test-runtime from <c>NotNot.Bcl.Core</c>'s
	/// transitive graph) reference <c>System.Runtime</c> v10 — which produces CS1705 cross-version
	/// binding errors that prevent <c>IServiceCollection</c> / <c>AddSingleton&lt;T&gt;()</c> from
	/// resolving inside the test compilation. Pinning the ref-pack to net10 fixes this.
	/// </summary>
	private static readonly ReferenceAssemblies Net100ReferenceAssemblies = new ReferenceAssemblies(
		"net10.0",
		new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
		Path.Combine("ref", "net10.0"));

	private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
		=> await VerifyWithReferencesAsync(source, [], expected);

	private static async Task VerifyWithReferencesAsync(
		string source,
		IEnumerable<MetadataReference> additionalReferences,
		params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<DiMarkerEnforcementAnalyzer, DefaultVerifier>
		{
			TestCode = source,
			ReferenceAssemblies = Net100ReferenceAssemblies,
		};

		// Dedupe by Assembly identity — IDiSingletonService and AutoDiBypassAttribute live in the
		// same NotNot.Bcl.Core assembly; the MS DI extension-method classes and IServiceCollection
		// may share assemblies as well. Microsoft.CodeAnalysis.Testing rejects duplicates.
		var refs = new System.Collections.Generic.HashSet<System.Reflection.Assembly>
		{
			typeof(IDiSingletonService).Assembly,                                                       // NotNot.Bcl.Core (markers + AutoDiBypassAttribute)
			typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly,               // Microsoft.Extensions.DependencyInjection.Abstractions
			typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly,// Microsoft.Extensions.DependencyInjection
			typeof(Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions).Assembly, // (same .Extensions assembly)
			typeof(Microsoft.Extensions.Hosting.IHostedService).Assembly,                               // Microsoft.Extensions.Hosting.Abstractions
		};

		// Cross-assembly fixtures: sibling metadata references built by CompileMetadataReference are compiled
		// against TRUSTED_PLATFORM_ASSEMBLIES, where System.Object lives in System.Private.CoreLib — but the
		// consumer compilation uses Microsoft.NETCore.App.Ref (Object in System.Runtime). Without corelib a
		// referenced sibling type triggers CS0012 ("type 'Object' is defined in an assembly that is not
		// referenced"), which makes the whole AddHostedService<Sibling>() call an OperationKind.Invalid
		// operation the analyzer never sees — silently defeating NN_DI_007 on marked-sibling fixtures.
		// Added ONLY when sibling references are present: injecting System.Private.CoreLib into a pure
		// ref-assembly (in-compilation-only) compilation creates a dual-corelib duplicate-Object situation
		// that breaks factory-overload binding in those fixtures.
		var siblingReferences = new System.Collections.Generic.List<MetadataReference>(additionalReferences);
		if (siblingReferences.Count > 0)
		{
			refs.Add(typeof(object).Assembly);                                                          // System.Private.CoreLib (aligns with sibling TPA-built metadata)
		}

		foreach (var asm in refs)
		{
			test.TestState.AdditionalReferences.Add(asm);
		}
		foreach (var reference in siblingReferences)
		{
			test.TestState.AdditionalReferences.Add(reference);
		}

		// Compiler diagnostics suppressed — fixture source-strings are minimal and may emit
		// CS warnings unrelated to the analyzer under test (mirrors MaybeReturnContractAnalyzerTests).
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	private static MetadataReference CompileMetadataReference(string assemblyName, string source)
	{
		// Include NotNot.Bcl.Core alongside the platform + Hosting assemblies so sibling sources can
		// reference [assembly: AutoDiScanAssembly] / [assembly: AutoDiBypass] and the IDi*Service markers
		// with zero compiler errors (emitResult.Success must hold).
		var referencePaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Append(typeof(Microsoft.Extensions.Hosting.IHostedService).Assembly.Location)
			.Append(typeof(NotNot.Bcl.Diagnostics.AutoDiScanAssemblyAttribute).Assembly.Location)
			.Distinct(StringComparer.OrdinalIgnoreCase);
		var compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			referencePaths.Select(path => MetadataReference.CreateFromFile(path)),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using var stream = new MemoryStream();
		var emitResult = compilation.Emit(stream);
		Assert.True(
			emitResult.Success,
			string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
		return MetadataReference.CreateFromImage(stream.ToArray());
	}

	private static DiagnosticResult DI001(string implType, string calledLifetime, string markerFqn, string expectedLifetime, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI001_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(markup)
			.WithArguments(implType, calledLifetime, markerFqn, expectedLifetime);

	private static DiagnosticResult DI002(string implType, string markerFqn, string lifetime, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI002_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
			.WithLocation(markup)
			.WithArguments(implType, markerFqn, lifetime);

	private static DiagnosticResult DI003(string lifetime, string concreteType, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI003_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
			.WithLocation(markup)
			.WithArguments(lifetime, concreteType);

	private static DiagnosticResult DI004(string implType, string markerFqn, string lifetime, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI004_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(markup)
			.WithArguments(implType, markerFqn, lifetime);

	private static DiagnosticResult DI005(string implType, string lifetime, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI005_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
			.WithLocation(markup)
			.WithArguments(implType, lifetime);

	private static DiagnosticResult DI006(string hostedType, string paramName, string delegateType, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI006_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(markup)
			.WithArguments(hostedType, paramName, delegateType);

	private static DiagnosticResult DI007(string hostedType, int markup = 0) =>
		new DiagnosticResult(DiMarkerEnforcementAnalyzer.DI007_DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(markup)
			.WithArguments(hostedType);

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_001 — Lifetime mismatch with marker interface (Error)
	// ══════════════════════════════════════════════════════════════════════════

	public class NN_DI_001_LifetimeMismatch
	{
		[Fact]
		public async Task AddTransient_OnSingletonMarker_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddTransient<Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Transient", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task AddSingleton_OnScopedMarker_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service : IDiScopedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Singleton", "IDiScopedService", "Scoped"));
		}

		[Fact]
		public async Task AddScoped_OnTransientMarker_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service : IDiTransientService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddScoped<Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Scoped", "IDiTransientService", "Transient"));
		}

		[Fact]
		public async Task TryAddTransient_OnSingletonMarker_Fires_TryAddDoesNotSuppressLifetimeCheck()
		{
			// Per Group C's heuristic: lifetime mismatch is a correctness bug regardless of
			// Add vs TryAdd semantics — TryAdd is "conditional registration", not "less wrong".
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.TryAddTransient<Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Transient", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task AddSingleton_OnSingletonMarker_NoDiagnostic_LifetimeMatches()
		{
			// Lifetime matches — NN_DI_001 silent (NN_DI_002 would fire instead; covered separately).
			// To keep this test focused on NN_DI_001, we mark the type [AutoDiBypass] so NN_DI_002 also stays quiet.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddTransient_OnUnmarkedType_NoDiagnostic_OutsideEnforcementZone()
		{
			// Wave 2 fixture update (Cluster A): `[AutoDiBypass]` added to silence NN_DI_005, which
			// fires on every unmarked project-internal class in Wave 2. Test intent preserved —
			// NN_DI_001 (the rule under test in this nested class) must stay silent on an unmarked
			// type (no marker → no lifetime to mismatch). The bypass attribute documents intent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddTransient_OnSingletonMarker_WithBypassAttribute_NoDiagnostic()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddSingleton_ThirdPartyType_NameClashWithMarker_NoDiagnostic_SymbolKeyed()
		{
			// A locally-declared interface with the same simple name as the marker but in a
			// different (non-global) namespace MUST NOT trigger the analyzer — the analyzer
			// keys off symbol identity / FQN, not the simple string.
			//
			// Wave 2 fixture update (Cluster A): `[AutoDiBypass]` added to `SomeOtherLib.Service`
			// to silence Wave 2's new NN_DI_005 (which fires on every unmarked project-internal
			// class). The test intent — verifying NN_DI_001 keys off SYMBOL identity, not name
			// match — is preserved: `Service` carries a shadow `IDiSingletonService` that the
			// analyzer correctly ignores, and the bypass attribute prevents NN_DI_005 from
			// surfacing the unrelated "missing marker" advisory on the test fixture.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

namespace SomeOtherLib
{
    public interface IDiSingletonService { } // SHADOW — different namespace, NOT the real marker.

    [AutoDiBypass]
    public class Service : IDiSingletonService { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<SomeOtherLib.Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddSingletonTwoArg_ChecksImplementationType()
		{
			// `AddSingleton<TService, TImpl>()` overload — analyzer reads TImpl's marker.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public interface IService { }
public class Service : IService, IDiScopedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<IService, Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Singleton", "IDiScopedService", "Scoped"));
		}

		[Fact]
		public async Task MultipleMarkersOnType_NoDiagnostic_TryGetExpectedLifetimeReturnsFalse()
		{
			// Per DiAnalyzerHelpers.TryGetExpectedLifetime: hits != 1 returns false. The analyzer
			// stays silent on multi-marker types — a different rule (if any) should surface that.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service : IDiSingletonService, IDiScopedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task InterfaceBridgeFactory_WithInnerSingletonImplFactoryBody_DoesNotFireYet_Wave1ScopeLimitation()
		{
			// Wave 1 documented scope limitation (H1 review finding): the analyzer inspects only
			// the REGISTERED service type's marker, not the constructed inner type in a factory
			// body. Here `IService` carries no marker (so TryGetExpectedLifetime returns false on
			// the registered type) and the analyzer short-circuits — even though `SingletonImpl`
			// IS marked IDiSingletonService and is being constructed Transient (a lifetime
			// mismatch in spirit).
			//
			// A future Wave 2 enhancement could walk the factory body to inspect the inner
			// concrete; if that lands, this test's assertion flips from 0 diagnostics to
			// expecting DI001 against `SingletonImpl`. Test name should be renamed at that time.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public interface IService { }
public class SingletonImpl : IService, IDiSingletonService
{
    public SingletonImpl() { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<IService>(sp => new SingletonImpl());
    }
}";
			await VerifyAsync(source);
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_002 — Redundant explicit DI registration (Warning)
	// ══════════════════════════════════════════════════════════════════════════

	public class NN_DI_002_RedundantRegistration
	{
		[Fact]
		public async Task AddSingleton_OnSingletonMarker_Fires_LifetimeMatchesMarker()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>()|};
    }
}";
			await VerifyAsync(source, DI002("Service", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task AddScoped_OnScopedMarker_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiScopedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddScoped<Service>()|};
    }
}";
			await VerifyAsync(source, DI002("Service", "IDiScopedService", "Scoped"));
		}

		[Fact]
		public async Task AddTransient_OnTransientMarker_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiTransientService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddTransient<Service>()|};
    }
}";
			await VerifyAsync(source, DI002("Service", "IDiTransientService", "Transient"));
		}

		[Fact]
		public async Task AddSingleton_OnSingletonMarker_TrivialFactoryWithoutDIArgs_Fires()
		{
			// Factory body `new Service()` with no DI-resolved args — not a passthrough, not an
			// interface-bridge. Still redundant: the marker already auto-registers Service.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service())|};
    }
}";
			await VerifyAsync(source, DI002("Service", "IDiSingletonService", "Singleton"));
		}

		// ── Interface-bridge carve-out (CRITICAL — ~40-site pattern in real Program.cs) ──

		[Fact]
		public async Task InterfaceBridgeFactory_AddSingleton_NoDiagnostic()
		{
			// `AddSingleton<IFoo>(sp => sp.GetRequiredService<FooUseCases>())` — concrete is
			// already registered via marker; this registration just bridges interface→impl.
			// Per DiAnalyzerHelpers.IsInterfaceBridgeFactory, this is a deliberate pattern.
			// Note: `IFoo` is the *registered service* type here (no marker on the interface),
			// so the implType the analyzer sees is `IFoo` (an interface). For NN_DI_002 to even
			// be a candidate, implType would need to carry a marker — interfaces don't carry
			// markers directly in this carve-out test, so we instead exercise the case where the
			// REGISTERED concrete type carries a marker.
			//
			// True carve-out shape: AddSingleton<FooUseCases>(sp => sp.GetRequiredService<FooUseCases>())
			// when FooUseCases : IDiSingletonService — NN_DI_002 candidate that the
			// IsInterfaceBridgeFactory skip neutralizes.
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present so the NN_DI_002
			// scan-marker gate (implInScanAssembly) does NOT silence — the interface-bridge carve-out
			// is what actually suppresses the diagnostic, so this test genuinely exercises
			// IsInterfaceBridgeFactory rather than short-circuiting at the gate.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class FooUseCases : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<FooUseCases>(sp => sp.GetRequiredService<FooUseCases>());
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task InterfaceBridgeFactory_AddScoped_NoDiagnostic()
		{
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present so the interface-bridge
			// carve-out — not the scan-marker gate — is what suppresses NN_DI_002.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiScopedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddScoped<Service>(sp => sp.GetRequiredService<Service>());
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task InterfaceBridgeFactory_UsingGetService_NoDiagnostic()
		{
			// GetService (non-Required) also classifies as interface-bridge per the helper.
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present so the interface-bridge
			// carve-out — not the scan-marker gate — is what suppresses NN_DI_002.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => sp.GetService<Service>()!);
    }
}";
			await VerifyAsync(source);
		}

		// ── TryAdd carve-out (deliberate conditional registration) ──

		[Fact]
		public async Task TryAddSingleton_OnSingletonMarker_NoDiagnostic()
		{
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present so the TryAdd carve-out
			// (!isTryAdd) — not the scan-marker gate — is what suppresses NN_DI_002.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.TryAddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		// ── Keyed-services carve-out (Wave 1 M6 review — out-of-scope by design) ──

		[Fact]
		public async Task AddKeyedSingleton_OnSingletonMarker_NoDiagnostic_KeyedNotInScope()
		{
			// Wave 1 M6 review finding: keyed-service registrations (`AddKeyedSingleton<T>("k")` etc.)
			// are .NET 8+ DI primitives that share the `Add` prefix but include `Keyed` in the name.
			// The analyzer's `TryClassifyLifetimeMethod` only matches the non-keyed variants
			// (`AddSingleton`, `AddScoped`, `AddTransient`, `TryAdd*`). Keyed registrations silently
			// bypass all five rules — intentional, since the keyed registration model is a different
			// shape than the marker-interface convention addresses. This test locks the current
			// boundary so a future refactor that adds `AddKeyed*` to the classifier doesn't silently
			// flip the FP profile.
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present so the silence is
			// attributable to TryClassifyLifetimeMethod rejecting `AddKeyedSingleton` (the classifier
			// boundary under test) rather than the scan-marker gate. With the marker, if a future
			// refactor added `AddKeyed*` to the classifier, NN_DI_002 WOULD fire here — pinning the
			// boundary against exactly that regression.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddKeyedSingleton<Service>(""mykey"");
    }
}";
			await VerifyAsync(source);
		}

		// ── Bypass / scope-limit cases ──

		[Fact]
		public async Task BypassAttribute_OnConcreteType_NoDiagnostic()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task UnmarkedType_NoDiagnostic_OutsideEnforcementZone()
		{
			// Wave 2 fixture update (Cluster A): `[AutoDiBypass]` added to silence NN_DI_005, which
			// fires on every unmarked project-internal class in Wave 2. Test intent preserved —
			// NN_DI_002 (the rule under test in this nested class) must stay silent on an unmarked
			// type (no marker → not a "redundant explicit" candidate). The bypass attribute
			// documents intent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task MultipleRegistrationsSameType_EachEvaluatedIndependently()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>()|};
        {|#1:sc.AddSingleton<Service>()|};
    }
}";
			await VerifyAsync(
				source,
				DI002("Service", "IDiSingletonService", "Singleton", markup: 0),
				DI002("Service", "IDiSingletonService", "Singleton", markup: 1));
		}

		[Fact]
		public async Task InterfaceBridgeFactory_WithCastWrappingGetService_StillSuppressed()
		{
			// M3 review fix: factory body `(Service)sp.GetRequiredService<Service>()` wraps the
			// resolver call in a cast operator. Before the fix, IsInterfaceBridgeFactory's
			// `is IInvocationOperation` pattern-match failed against the IConversionOperation
			// and the carve-out didn't fire — NN_DI_002 would have incorrectly flagged the
			// registration as redundant. UnwrapConversions strips the cast, the inner
			// IInvocationOperation surfaces as a GetRequiredService call, the carve-out fires,
			// and NN_DI_002 stays silent.
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present so the cast-unwrap +
			// interface-bridge carve-out — not the scan-marker gate — is what suppresses NN_DI_002.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => (Service)sp.GetRequiredService<Service>());
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddSingleton_OnSingletonMarker_UnmarkedAssembly_Silent()
		{
			// F1 scan-marker gate (negative): identical to AddSingleton_OnSingletonMarker_Fires but
			// WITHOUT [assembly: AutoDiScanAssembly]. In an unmarked assembly the type is never
			// auto-registered, so the explicit AddSingleton is NOT redundant — it is the only
			// registration. NN_DI_002 must stay silent (advising removal would unregister the type).
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_003 — Passthrough factory eligible for marker auto-registration (Info)
	// ══════════════════════════════════════════════════════════════════════════

	public class NN_DI_003_PassthroughFactory
	{
		[Fact]
		public async Task PassthroughFactory_TwoDIArgs_Fires_OnUnmarkedConcrete()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Dep1 : IDiSingletonService { }
public class Dep2 : IDiSingletonService { }
public class Service
{
    public Service(Dep1 a, Dep2 b) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep1>(), sp.GetRequiredService<Dep2>()))|};
    }
}";
			await VerifyAsync(source, DI003("Singleton", "Service"));
		}

		[Fact]
		public async Task PassthroughFactory_NonDILiteralArg_NoDiagnostic_NotPurePassthrough()
		{
			// Wave 2 fixture update (Cluster A): `[AutoDiBypass]` added to `Service` to silence
			// NN_DI_005, which fires on every unmarked project-internal class in Wave 2. Test
			// intent preserved — NN_DI_003 (the rule under test in this nested class) must stay
			// silent on a factory with literal-string args (not pure passthrough). The bypass
			// attribute keeps the "NN_DI_003 silent on non-passthrough body" contract locked
			// WITHOUT collateral NN_DI_005 firing.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

public class Dep1 : IDiSingletonService { }

[AutoDiBypass]
public class Service
{
    public Service(Dep1 a, string name) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep1>(), ""literal-string""));
    }
}";
			await VerifyAsync(source);
		}

		// Wave 2 Cluster C: `NonPassthroughFactory_MethodCallBody_NoDiagnostic` (was here) was
		// DELETED as fully superseded by the new test
		// `NN_DI_005_MissingMarker.NonPassthroughFactory_MethodCallBody_FiresNN_DI_005` (~line 1314).
		// The two tests had byte-identical fixtures with opposing assertions; the Wave-2-correct
		// assertion (NN_DI_005 fires) lives in the new test under the new rule's nested class.

		[Fact]
		public async Task PassthroughFactory_ThirdPartyConcrete_NoDiagnostic_CannotAddMarker()
		{
			// Concrete is `System.Text.StringBuilder` (System.* → third-party per the ignore list).
			// We can't suggest adding a marker to a type we don't own.
			var source = @"
using System.Text;
using Microsoft.Extensions.DependencyInjection;

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<StringBuilder>(sp => new StringBuilder());
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task TryAddSingleton_Passthrough_NoDiagnostic_DeliberateConditional()
		{
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present + `Service` is unmarked
			// project-internal, so WITHOUT the TryAdd carve-out (!isTryAdd) NN_DI_003 (passthrough) and
			// NN_DI_005 (missing-marker) would fire. The TryAdd guard is what genuinely suppresses both —
			// this now exercises the carve-out rather than short-circuiting at the scan-marker gate.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Dep1 : IDiSingletonService { }
public class Service
{
    public Service(Dep1 a) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.TryAddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep1>()));
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task PassthroughFactory_BypassedConcrete_NoDiagnostic()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

public class Dep1 : IDiSingletonService { }

[AutoDiBypass]
public class Service
{
    public Service(Dep1 a) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep1>()));
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task PassthroughFactory_AlreadyMarkedConcrete_NoDi003_Di002HandlesIt()
		{
			// Concrete already implements IDiSingletonService — NN_DI_002 covers the redundancy,
			// NN_DI_003 stays silent to avoid double-firing on the same invocation.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Dep1 : IDiSingletonService { }
public class Service : IDiSingletonService
{
    public Service(Dep1 a) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep1>()))|};
    }
}";
			await VerifyAsync(source, DI002("Service", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task InterfaceBridgePattern_UnmarkedConcreteFactoryReturn_NoDi003()
		{
			// Interface-bridge shape returning sp.GetRequiredService<...>() — body is NOT an
			// `IObjectCreationOperation`, so IsPassthroughFactory returns false. Test the
			// negative-NN_DI_003 path explicitly (the IsInterfaceBridgeFactory skip only matters
			// for NN_DI_002; NN_DI_003 simply doesn't match the lambda shape).
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present + `Concrete` is unmarked
			// project-internal, so WITHOUT the interface-bridge carve-out NN_DI_005 (missing-marker) would
			// fire. The IsInterfaceBridgeFactory skip in the NN_DI_005 predicate is what genuinely
			// suppresses it — this now exercises the carve-out rather than the scan-marker gate.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Concrete { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Concrete>(sp => sp.GetRequiredService<Concrete>());
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task NonGenericRegistration_OpenGenericShape_NoDiagnostic()
		{
			// `AddSingleton(typeof(IFoo<>), typeof(Foo<>))` — non-generic overload. The analyzer
			// returns false from TryResolveGenericTypes (typeArgs.Length == 0).
			var source = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public interface IFoo<T> { }
public class Foo<T> : IFoo<T> { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton(typeof(IFoo<>), typeof(Foo<>));
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task PassthroughFactory_NoArgsConstructor_FiresNN_DI_005_NotPassthrough()
		{
			// Wave 2 design tightening (Cluster B): zero-arg `new Service()` is NO LONGER
			// classified as a passthrough — `IsPassthroughFactory` requires at least one
			// DI-resolved constructor argument. Rationale: a parameterless `new T()` body has no
			// DI dependency to "pass through"; structurally it is identical to `Add{L}<T>()` (no
			// factory needed). NN_DI_005 (Wave 2 missing-marker advisory) is the more precise
			// diagnostic for "unmarked project-internal class registered explicitly" — fires here
			// because `Service` is unmarked, project-internal, and not third-party.
			//
			// Reverses Wave 1 M1 review disposition (which included zero-arg as passthrough). The
			// Wave 2 NN_DI_005 design covers the broader "unmarked registration" case more
			// precisely; NN_DI_003 narrows to "factory that bridges DI-resolved deps".
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service())|};
    }
}";
			await VerifyAsync(source, DI005("Service", "Singleton"));
		}

		[Fact]
		public async Task PassthroughFactory_WithCastWrappingNewT_StillDetected()
		{
			// M3 review fix: factory body `(IService)new ServiceImpl(...)` wraps the
			// `new` expression in a cast operator. Before the fix, IsPassthroughFactory's
			// `is IObjectCreationOperation` pattern-match failed against the IConversionOperation
			// and no diagnostic fired. UnwrapConversions strips the cast and the inner
			// IObjectCreationOperation surfaces — NN_DI_003 fires.
			//
			// Note: the registered type here is `ServiceImpl` (not the interface) — keeping the
			// cast-detection test independent of H1's interface-registration scope limitation.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Dep1 : IDiSingletonService { }
public interface IService { }
public class ServiceImpl : IService
{
    public ServiceImpl(Dep1 a) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<ServiceImpl>(sp => (ServiceImpl)new ServiceImpl(sp.GetRequiredService<Dep1>()))|};
    }
}";
			await VerifyAsync(source, DI003("Singleton", "ServiceImpl"));
		}

		[Fact]
		public async Task PassthroughFactory_UnmarkedAssembly_Silent()
		{
			// F1 scan-marker gate (negative): identical to PassthroughFactory_TwoDIArgs_Fires_OnUnmarkedConcrete
			// but WITHOUT [assembly: AutoDiScanAssembly]. The concrete's assembly is unmarked, so adding a
			// marker would not enable auto-registration — the NN_DI_003 "add a marker, drop the factory"
			// advice is inert. NN_DI_003 (and the residual NN_DI_005) must stay silent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Dep1 : IDiSingletonService { }
public class Dep2 : IDiSingletonService { }
public class Service
{
    public Service(Dep1 a, Dep2 b) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep1>(), sp.GetRequiredService<Dep2>()));
    }
}";
			await VerifyAsync(source);
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_004 — Marker + IHostedService conflict (Error)
	// ══════════════════════════════════════════════════════════════════════════

	public class NN_DI_004_MarkerWithHostedService
	{
		[Fact]
		public async Task SingletonMarker_PlusIHostedService_Fires()
		{
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class {|#0:HostedWorker|} : IDiSingletonService, IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source, DI004("HostedWorker", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task ScopedMarker_PlusIHostedService_Fires()
		{
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class {|#0:HostedWorker|} : IDiScopedService, IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source, DI004("HostedWorker", "IDiScopedService", "Scoped"));
		}

		[Fact]
		public async Task IHostedServiceAlone_NoDiagnostic()
		{
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class HostedWorker : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task MarkerAlone_WithoutIHostedService_NoDiagnostic()
		{
			var source = @"
public class Service : IDiSingletonService { }";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task BothInterfaces_PlusBypass_NoDiagnostic()
		{
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class HostedWorker : IDiSingletonService, IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task UserDerivedFromHostedServiceBase_AddsMarker_FiresNN_DI_004()
		{
			// Wave 1 H3-demoted review finding: validate the cross-assembly inheritance path.
			// A user-declared base class implements `IHostedService` (in this test fixture's
			// compilation); the derived class adds an `IDi*Service` marker. NN_DI_004 must fire
			// on the derived declaration because `INamedTypeSymbol.AllInterfaces` walks base
			// types — even if `IHostedService` enters via the base class rather than direct
			// implementation, the analyzer's `ImplementsInterface` helper resolves it.
			//
			// This locks the cross-assembly path: a future refactor that switches the helper
			// from `AllInterfaces` to `Interfaces` (direct-only) would silently disable NN_DI_004
			// against derived-from-base-IHostedService user types — this test catches that.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public abstract class MyHostedBase : IHostedService
{
    public abstract Task StartAsync(CancellationToken ct);
    public abstract Task StopAsync(CancellationToken ct);
}

public class {|#0:DerivedWorker|} : MyHostedBase, IDiSingletonService
{
    public override Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public override Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source, DI004("DerivedWorker", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task SingletonMarker_PlusIHostedService_UnmarkedAssembly_Silent()
		{
			// F1 scan-marker gate (negative): identical to SingletonMarker_PlusIHostedService_Fires but
			// WITHOUT [assembly: AutoDiScanAssembly]. The runtime scanner never auto-registers a type in an
			// unmarked assembly, so neither the marker auto-registration path nor the IHostedService
			// auto-registration path exists — there is no two-path conflict. NN_DI_004 must stay silent.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class HostedWorker : IDiSingletonService, IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// Cross-cutting bypass behavior
	// ══════════════════════════════════════════════════════════════════════════

	public class Bypass
	{
		[Fact]
		public async Task ClassLevelBypass_SuppressesAllRules()
		{
			// Single source exercising all four rule shapes against a bypassed type. None should fire.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Worker : IDiSingletonService, IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<Worker>();          // would fire NN_DI_001 without bypass
        sc.AddSingleton<Worker>();          // would fire NN_DI_002 without bypass
        sc.AddSingleton<Worker>(sp => new Worker()); // would fire NN_DI_002 / NN_DI_003 candidates
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AssemblyLevelBypass_ShortCircuitsAllRules()
		{
			// `[assembly: AutoDiBypass]` — entire compilation opts out. The analyzer's
			// compilation-start gate returns early; not a single diagnostic fires.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;

[assembly: AutoDiBypass]

public class Worker : IDiSingletonService, IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

public class Service : IDiScopedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddTransient<Service>();   // would fire NN_DI_001 without assembly bypass
        sc.AddScoped<Service>();      // would fire NN_DI_002 without assembly bypass
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AutoDiBypass_FromUnrelatedNamespace_DoesNotSuppressDiagnostics()
		{
			// H2 review finding: locally-declared `[AutoDiBypass]` attributes in unrelated
			// namespaces must NOT suppress the analyzer family. The simple-name fallback was
			// removed in Wave 1 — only the canonical `NotNot.Bcl.Diagnostics.AutoDiBypassAttribute`
			// is honored.
			//
			// Setup: SomeOtherCompany.AutoDiBypassAttribute is a wholly-unrelated attribute that
			// happens to share the simple name. Service carries it AND IDiSingletonService.
			// Registering Transient is still a lifetime mismatch (DI001) — the unrelated bypass
			// MUST NOT silence the rule.
			var source = @"
using System;
using Microsoft.Extensions.DependencyInjection;

namespace SomeOtherCompany
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AutoDiBypassAttribute : Attribute { }
}

[SomeOtherCompany.AutoDiBypass]
public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddTransient<Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Transient", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task AssemblyBypass_ShortCircuitsAllRules_NoDiagnostics()
		{
			// Compilation-start short-circuit test: `[assembly: AutoDiBypass]` causes the analyzer's
			// `OnCompilationStart` to return BEFORE registering any operation handlers, so all five
			// rules (NN_DI_001/002/003/004/005) stay silent regardless of registration shape.
			//
			// Wave 2 update (Cluster A test #7 — was `NoMarkerSymbolsInCompilation_ShortCircuits_NoDiagnostics`):
			// the original test's premise ("no marker symbols in compilation → analyzer silent") was
			// untestable in practice — the test harness unconditionally adds the `NotNot.Bcl.Core`
			// reference, so `GetTypeByMetadataName` always resolves the markers. Wave 2's NN_DI_005
			// fires on every unmarked project-internal class regardless of whether the source
			// declares any marker-bearing classes — so the original assertion of zero diagnostics on
			// three `Add*<Service>()` calls became invalid. This rewrite exercises the genuine
			// short-circuit path (`[assembly: AutoDiBypass]`) which IS what the test name claims to
			// verify, and keeps the multi-rule-silence assertion intact.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[assembly: AutoDiBypass]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
        sc.AddTransient<Service>();
        sc.AddScoped<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task BypassOnBaseClass_DerivedNotBypassed_FiresNormally()
		{
			// Wave 1 M7 review finding: `[AutoDiBypass]` is declared `Inherited = false`. The
			// helper `HasAutoDiBypassAttribute(ISymbol)` walks `symbol.GetAttributes()` which
			// returns ONLY attributes applied directly to that symbol (does NOT walk base types).
			//
			// Contract under test: `[AutoDiBypass]` on a base class does NOT propagate to derived
			// classes. A future maintenance change that switches to `Inherited = true` or walks
			// `BaseType` in the helper would silently break this contract — this test catches it.
			//
			// Fixture: Base has `[AutoDiBypass]`; Derived implements `IDiSingletonService` and is
			// registered Transient (a lifetime mismatch). NN_DI_001 must fire on the registration
			// because the bypass is NOT inherited from Base to Derived.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public abstract class Base { }

public class Derived : Base, IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddTransient<Derived>()|};
    }
}";
			await VerifyAsync(source, DI001("Derived", "Transient", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task BypassedClass_PlusUnmarkedClass_OnlyNN_DI_005_Fires()
		{
			// Verifies that class-level [AutoDiBypass] is TYPE-SCOPED, not assembly-scoped. The
			// bypassed type emits zero diagnostics; the unbypassed unmarked type sitting next to
			// it in the same source file still fires NN_DI_005. This protects against a refactor
			// that would accidentally elevate type-level bypass to compilation-level short-circuit.
			//
			// Fixture: BypassedService is `[AutoDiBypass]` + unmarked → silent. UnmarkedService is
			// vanilla + registered Singleton → fires NN_DI_005.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

[AutoDiBypass]
public class BypassedService { }

public class UnmarkedService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<BypassedService>();
        {|#0:sc.AddSingleton<UnmarkedService>()|};
    }
}";
			await VerifyAsync(source, DI005("UnmarkedService", "Singleton"));
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_005 — Missing IDi{L}Service marker — auto-registration candidate (Info)
	// ══════════════════════════════════════════════════════════════════════════

	public class NN_DI_005_MissingMarker
	{
		// ── Positive cases (NN_DI_005 fires) ──────────────────────────────────

		[Fact]
		public async Task AddSingleton_OnUnmarkedClass_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>()|};
    }
}";
			await VerifyAsync(source, DI005("Service", "Singleton"));
		}

		[Fact]
		public async Task AddScoped_OnUnmarkedClass_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddScoped<Service>()|};
    }
}";
			await VerifyAsync(source, DI005("Service", "Scoped"));
		}

		[Fact]
		public async Task AddTransient_OnUnmarkedClass_Fires()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddTransient<Service>()|};
    }
}";
			await VerifyAsync(source, DI005("Service", "Transient"));
		}

		[Fact]
		public async Task TwoArgOverload_AddSingletonInterfaceImpl_Fires_OnImpl()
		{
			// DP2 Option α lock-in: `Add{L}<TInterface, TImpl>()` fires NN_DI_005 on `TImpl`.
			// Developer reviews each site and applies [AutoDiBypass] for intentional 2-arg shapes.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public interface IService { }
public class ServiceImpl : IService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<IService, ServiceImpl>()|};
    }
}";
			await VerifyAsync(source, DI005("ServiceImpl", "Singleton"));
		}

		[Fact]
		public async Task ObjectInitializerFactory_FiresNN_DI_005()
		{
			// DP3 lock-in: factory body `new Service { Prop = ... }` has an object initializer.
			// IsPassthroughFactory returns false (initializer != GetRequiredService arg), so
			// NN_DI_003 doesn't fire. NN_DI_005's predicate is satisfied — fires once.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service
{
    public string? Prop { get; set; }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service { Prop = ""value"" })|};
    }
}";
			await VerifyAsync(source, DI005("Service", "Singleton"));
		}

		[Fact]
		public async Task NonPassthroughFactory_MethodCallBody_FiresNN_DI_005()
		{
			// Factory body `Service.Create()` is a static method invocation — NOT an object-
			// creation operation. IsPassthroughFactory returns false (it expects
			// `IObjectCreationOperation`). NN_DI_003 silent. Service is unmarked → NN_DI_005 fires.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service
{
    public static Service Create() => new Service();
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => Service.Create())|};
    }
}";
			await VerifyAsync(source, DI005("Service", "Singleton"));
		}

		[Fact]
		public async Task NotNotPrefixedType_NoNN_DI_005()
		{
			// Cluster D (Wave 2 audit follow-up): the audit at `.vibeOverwatch/memory/20260516-1141-
			// AUDIT-di-program-cs-registrations.VibeAudit.md:160` flagged `IsThirdPartyType`'s missing
			// `NotNot.*` prefix as a mandatory pre-merge change. Without the carve-out, consumer
			// registrations of sibling-primitive types from `NotNot.Bcl.Core` / other `NotNot.*`
			// packages (e.g. `AddSingleton<SimpleStorageOptions>()`) fire NN_DI_005 at the consumer
			// site — but the consumer doesn't own the type and cannot add a marker interface.
			//
			// Fixture: register `NotNot.Storage.SimpleStorageOptions` (a real record-class in
			// `NotNot.Bcl.Core`) directly. The analyzer's `IsThirdPartyType` must recognize the
			// `NotNot.*` assembly prefix and skip NN_DI_005 enforcement.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Storage;

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<SimpleStorageOptions>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task ClosedGeneric_UnmarkedOpenForm_Fires()
		{
			// DP6 lock-in (positive): `AddSingleton<MyGeneric<string>>()` where `MyGeneric<T>` has
			// NO marker. The closed-generic registration of an unmarked open form is a missing-
			// marker candidate. NN_DI_005 fires.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class MyGeneric<T> { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<MyGeneric<string>>()|};
    }
}";
			await VerifyAsync(source, DI005("MyGeneric<string>", "Singleton"));
		}

		[Fact]
		public async Task MultipleUnmarkedRegistrations_EachFires()
		{
			// Independent emissions — two consecutive `AddSingleton<Service>()` lines each fire
			// NN_DI_005 at their own location. Verifies the analyzer doesn't dedupe by
			// implementation type within a compilation unit.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>()|};
        {|#1:sc.AddSingleton<Service>()|};
    }
}";
			await VerifyAsync(
				source,
				DI005("Service", "Singleton", markup: 0),
				DI005("Service", "Singleton", markup: 1));
		}

		// ── Negative cases (NN_DI_005 silent — other rule or carve-out applies) ──

		[Fact]
		public async Task AddSingleton_OnMarkedClass_NoNN_DI_005_NN_DI_002_Covers()
		{
			// hasMarker is true → NN_DI_002 covers the redundancy. NN_DI_005 silent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>()|};
    }
}";
			await VerifyAsync(source, DI002("Service", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task AddTransient_OnMismatchedMarker_NoNN_DI_005_NN_DI_001_Covers()
		{
			// hasMarker true, lifetime mismatched → NN_DI_001 covers. NN_DI_005 silent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddTransient<Service>()|};
    }
}";
			await VerifyAsync(source, DI001("Service", "Transient", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task MissingMarker_WithPassthroughFactory_Fires_NN_DI_003_NotNN_DI_005()
		{
			// F1 lock-in (mutual exclusion): unmarked `Service` registered with a passthrough
			// factory body. NN_DI_003 fires; the `return` after NN_DI_003 emission (Group A's F1
			// fix) prevents NN_DI_005 from double-firing on the same invocation.
			//
			// If Group A forgets the `return`, this test FAILS — VerifyAsync rejects any
			// unexpected NN_DI_005 emission. Direct runtime catch for the F1 bug.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Dep : IDiSingletonService { }
public class Service
{
    public Service(Dep d) { }
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service(sp.GetRequiredService<Dep>()))|};
    }
}";
			await VerifyAsync(source, DI003("Singleton", "Service"));
		}

		[Fact]
		public async Task InterfaceBridgeFactory_UnmarkedConcrete_Silent()
		{
			// DP4 lock-in: `IsInterfaceBridgeFactory` returns true for
			// `sp => sp.GetRequiredService<Service>()`, so the NN_DI_005 predicate skips.
			// All rules silent on this shape.
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present + `Service` is unmarked
			// project-internal, so WITHOUT the interface-bridge carve-out NN_DI_005 would fire. The
			// IsInterfaceBridgeFactory skip is what genuinely suppresses it, not the scan-marker gate.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => sp.GetRequiredService<Service>());
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task ThirdPartyTypeRegistration_NoNN_DI_005()
		{
			// `StringBuilder` lives in System.* — `IsThirdPartyType` returns true. NN_DI_005's
			// predicate skips; the user can't add a marker to a type they don't own.
			var source = @"
using System.Text;
using Microsoft.Extensions.DependencyInjection;

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<StringBuilder>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task TryAdd_OnUnmarkedClass_NoNN_DI_005()
		{
			// TryAdd* is deliberate conditional registration. NN_DI_005's `!isTryAdd` guard
			// prevents firing — consistent with NN_DI_002/003 TryAdd carve-outs.
			//
			// F1 coverage-restoration: [assembly: AutoDiScanAssembly] present + `Service` is unmarked
			// project-internal, so WITHOUT the TryAdd carve-out NN_DI_005 would fire. The `!isTryAdd`
			// guard is what genuinely suppresses it, not the scan-marker gate.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.TryAddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddSingleton_OnAbstractClass_Silent()
		{
			// DP5 lock-in: abstract class can't be auto-registered as concrete. NN_DI_005's
			// `!implType.IsAbstract` predicate skips. Compiler may emit a runtime registration
			// error elsewhere, but the analyzer correctly doesn't suggest adding a marker to an
			// abstract type.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public abstract class AbstractBase { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<AbstractBase>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddSingleton_OnInterface_Silent()
		{
			// DP5 lock-in: interface as T (1-arg overload registering an interface directly).
			// NN_DI_005's `TypeKind == Class` predicate skips. Adding a marker to an interface
			// is meaningless — the auto-registration scanner looks for non-abstract assignable
			// concrete types, not interface declarations.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public interface IService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<IService>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task OpenGenericRegistration_Silent()
		{
			// Non-generic shape (typeof-based registration) is rejected by `TryResolveGenericTypes`
			// (typeArgs.Length == 0). NN_DI_005 doesn't even enter. This mirrors the existing
			// NN_DI_003 `NonGenericRegistration_OpenGenericShape_NoDiagnostic` test at the same
			// boundary.
			var source = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public interface IFoo<T> { }
public class Foo<T> : IFoo<T> { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton(typeof(IFoo<>), typeof(Foo<>));
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task ClosedGeneric_MarkedOpenForm_Silent()
		{
			// DP6 lock-in (negative): closed-generic registration of an open form that DOES
			// implement `IDiSingletonService` — the marker is inherited via `AllInterfaces`
			// (`MyGeneric<string>` inherits from `MyGeneric<T>`'s declared interfaces). NN_DI_002
			// fires for the redundancy. NN_DI_005 silent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class MyGeneric<T> : IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<MyGeneric<string>>()|};
    }
}";
			await VerifyAsync(source, DI002("MyGeneric<string>", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task ClosedGeneric_MarkerOnBaseClass_Silent_NN_DI_005()
		{
			// F7 review addition: marker is on a NON-generic base class; the derived generic
			// inherits the marker via `AllInterfaces` (which traverses base types). NN_DI_002
			// fires for the redundancy. NN_DI_005 silent.
			//
			// This locks the base-class inheritance path for marker resolution — separate from
			// the open-generic inheritance path (`ClosedGeneric_MarkedOpenForm_Silent`).
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class MarkedBase : IDiSingletonService { }
public class MyGeneric<T> : MarkedBase { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<MyGeneric<string>>()|};
    }
}";
			await VerifyAsync(source, DI002("MyGeneric<string>", "IDiSingletonService", "Singleton"));
		}

		[Fact]
		public async Task TwoArgOverload_ImplMarked_Fires_NN_DI_002_NotNN_DI_005()
		{
			// DP2 mutual exclusion: `AddSingleton<IService, ServiceImpl>()` where `ServiceImpl`
			// already carries `IDiSingletonService`. hasMarker true → NN_DI_002 fires.
			// NN_DI_005 silent (its `!hasMarker` predicate fails).
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public interface IService { }
public class ServiceImpl : IService, IDiSingletonService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<IService, ServiceImpl>()|};
    }
}";
			await VerifyAsync(source, DI002("ServiceImpl", "IDiSingletonService", "Singleton"));
		}

		// ── Bypass cases (NN_DI_005 honors [AutoDiBypass]) ────────────────────

		[Fact]
		public async Task BypassedClass_NoNN_DI_005()
		{
			// `[AutoDiBypass]` on a class — NN_DI_005's `!HasAutoDiBypassAttribute(implType)`
			// predicate skips. Developer's explicit intent to keep the registration silenced.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AssemblyBypass_NoNN_DI_005()
		{
			// `[assembly: AutoDiBypass]` — compilation-start gate short-circuits BEFORE any
			// operation handlers run. All five rules silent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

[assembly: AutoDiBypass]

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task BypassedClass_2ArgOverload_NoNN_DI_005()
		{
			// `[AutoDiBypass]` on the TImpl side of a 2-arg overload. NN_DI_005's predicate
			// inspects `implType` (the last type-arg) — bypass takes precedence over the
			// missing-marker check.
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using NotNot.Bcl.Diagnostics;

public interface IService { }

[AutoDiBypass]
public class ServiceImpl : IService { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<IService, ServiceImpl>();
    }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AddSingleton_OnUnmarkedClass_UnmarkedAssembly_Silent()
		{
			// F1 scan-marker gate (negative): identical to AddSingleton_OnUnmarkedClass_Fires but WITHOUT
			// [assembly: AutoDiScanAssembly]. In an unmarked assembly the class is never auto-registered,
			// so "add a marker for auto-registration" is inert advice. NN_DI_005 must stay silent.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>();
    }
}";
			await VerifyAsync(source);
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_006 — Hosted service with a required delegate ctor parameter (Error)
	// ══════════════════════════════════════════════════════════════════════════

	public class NN_DI_006_HostedServiceRequiredDelegateCtor
	{
		[Fact]
		public async Task ConcreteHostedService_RequiredDelegateCtorParam_Fires()
		{
			// F1 positive: a concrete BackgroundService (IHostedService) whose single public ctor
			// requires a Func<> delegate parameter DI cannot provide. Marker-LESS — proves NN_DI_006
			// fires regardless of marker presence (the branch runs BEFORE the NN_DI_004 marker-gate
			// return). If this fails to fire, the detection branch is misplaced after the marker gate.
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class {|#0:CrashObserver|} : BackgroundService
{
    public CrashObserver(Func<string, CancellationToken, Task> purge) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source, DI006(
				"CrashObserver",
				"purge",
				"System.Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task>"));
		}

		[Fact]
		public async Task OptionalDefaultDelegateParam_Silent()
		{
			// N1: the delegate ctor parameter has a default (= null) → HasExplicitDefaultValue is true →
			// DI passes null and the registration is constructible. NN_DI_006 silent (PtySessionManager
			// shape — an optional delegate is a safe no-op).
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class Svc : BackgroundService
{
    public Svc(Func<string, string?>? resolver = null) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task RequiredRegisteredInterfaceParam_Silent()
		{
			// N2: the required ctor parameter is an interface abstraction (not a delegate) →
			// TypeKind != Delegate → DI can resolve it. NN_DI_006 silent. This is the preferred-fix
			// shape the diagnostic message steers toward.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public interface ISomeService { }

public class Svc : BackgroundService
{
    public Svc(ISomeService dep) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task NonHostedServiceClass_RequiredDelegateCtor_Silent()
		{
			// N3: a plain class (not IHostedService) with a required delegate ctor — it never reaches
			// the IHostedService auto-registration scan, so a constructor-injected delegate is a
			// legitimate manual-factory pattern. ImplementsHostedService false → NN_DI_006 silent.
			var source = @"
using System;

public class Plain
{
    public Plain(Func<int> f) { }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AbstractHostedService_RequiredDelegateCtor_Silent()
		{
			// N4: an abstract IHostedService with a required delegate ctor — abstract types are not
			// auto-registered as concrete (the shared concrete/!abstract guard at the method top
			// returns first). NN_DI_006 silent.
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public abstract class Base : BackgroundService
{
    protected Base(Func<int> f) { }
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task BypassedHostedService_RequiredDelegateCtor_Silent()
		{
			// N5: [AutoDiBypass] on the type — the shared type-scope guard returns before the NN_DI_006
			// branch. Silent (developer's explicit opt-out: the type is hand-constructed).
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;

[AutoDiBypass]
public class Svc : BackgroundService
{
    public Svc(Func<int> f) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AssemblyBypass_HostedServiceRequiredDelegateCtor_Silent()
		{
			// N6 (markers-absent intent, realized via the testable short-circuit): VerifyAsync
			// UNCONDITIONALLY references NotNot.Bcl.Core, so GetTypeByMetadataName always resolves the
			// markers and a true markers-absent compilation is not achievable in this harness — the
			// compilation-start marker-gate never short-circuits. Per the harness precedent
			// (AssemblyBypass_NoNN_DI_005), realize REQ-2's compilation-gate-silence intent via the
			// genuine `[assembly: AutoDiBypass]` short-circuit, which returns from OnCompilationStart
			// BEFORE AnalyzeNamedType is ever registered. Fixture mirrors F1 plus the assembly bypass.
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;

[assembly: AutoDiBypass]

public class CrashObserver : BackgroundService
{
    public CrashObserver(Func<string, CancellationToken, Task> purge) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task TwoPublicCtors_OneWithRequiredDelegateParam_Silent()
		{
			// N7 (FP-prevention guard pin): a concrete BackgroundService with TWO public instance
			// constructors — one of which has a required Func<> delegate parameter. NN_DI_006 must stay
			// SILENT because TryGetSinglePublicInstanceConstructor returns false on >1 public ctor
			// (MS.DI greedy-resolve / [ActivatorUtilitiesConstructor] disambiguation is undecidable, so
			// the rule conservatively skips). Without the >1-ctor skip this would be a false positive —
			// this test pins that the guard prevents it.
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class Svc : BackgroundService
{
    public Svc(Func<int> f) { }
    public Svc(int x) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task MarkedHostedService_SingleRequiredDelegateCtor_CoFires_NN_DI_004_And_NN_DI_006()
		{
			// Co-fire (detection step-5 documented): a MARKED IHostedService (carries IDiSingletonService)
			// whose single public ctor has a required Func<> delegate param emits BOTH NN_DI_004 (marker +
			// IHostedService conflict) AND NN_DI_006 (required delegate ctor param). The two rules fire on
			// disjoint conditions over the SAME class-declaration location — NN_DI_006 runs BEFORE the
			// NN_DI_004 marker-gate `return` and is marker-independent, so it does not suppress NN_DI_004,
			// and the marker presence does not suppress NN_DI_006. A single class-name markup serves both
			// (both report on namedType.Locations[0]).
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]

public class {|#0:CrashObserver|} : IDiSingletonService, IHostedService
{
    public CrashObserver(Func<string, CancellationToken, Task> purge) { }
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(
				source,
				DI006(
					"CrashObserver",
					"purge",
					"System.Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task>",
					markup: 0),
				DI004("CrashObserver", "IDiSingletonService", "Singleton", markup: 0));
		}

		[Fact]
		public async Task ConcreteHostedService_RequiredDelegateCtorParam_UnmarkedAssembly_Silent()
		{
			// F1 scan-marker gate (negative): identical to ConcreteHostedService_RequiredDelegateCtorParam_Fires
			// but WITHOUT [assembly: AutoDiScanAssembly]. The unconstructible-ctor crash only happens when the
			// runtime auto-registers the type, which requires a scan-marked assembly. In an unmarked assembly
			// the hosted service is never auto-registered, so no boot crash — NN_DI_006 must stay silent.
			var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class CrashObserver : BackgroundService
{
    public CrashObserver(Func<string, CancellationToken, Task> purge) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}";
			await VerifyAsync(source);
		}
	}

	// ══════════════════════════════════════════════════════════════════════════
	// NN_DI_007 — Explicit hosted-service registration duplicates auto-scan (Error)

	public class NN_DI_007_ExplicitHostedServiceRegistration
	{
		[Fact]
		public async Task DirectRegistration_PublicConcreteHostedService_Fires()
		{
			// The hosted type lives in THIS compilation, which must carry [assembly: AutoDiScanAssembly]
			// for the type to be scan-eligible under the marker-based contract.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => {|#0:services.AddHostedService<Worker>()|};
}";
			await VerifyAsync(source, DI007("Worker"));
		}

		[Fact]
		public async Task FactoryRegistration_PublicConcreteHostedService_Fires()
		{
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
public interface IDependency { }
public sealed class Worker : BackgroundService
{
    public Worker(IDependency dependency) { }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => {|#0:services.AddHostedService(sp => new Worker(sp.GetRequiredService<IDependency>()))|};
}";
			await VerifyAsync(source, DI007("Worker"));
		}

		[Fact]
		public async Task DirectRegistration_UnmarkedAssemblyHostedService_Silent()
		{
			// No [assembly: AutoDiScanAssembly] — the type is not scan-eligible, so the explicit
			// registration is legitimate and must stay silent (inversion from name-inference: an
			// unmarked in-compilation type used to fire because it was the current compilation).
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task MarkedReferencedSiblingHostedService_Fires()
		{
			// The sibling assembly carries [assembly: AutoDiScanAssembly] — it opts INTO the AutoDI
			// scan, so an explicit AddHostedService<SiblingWorker> in the consumer duplicates the scan.
			// Re-keys the former name-inference fixture: eligibility is now the marker, not the name.
			var sibling = CompileMetadataReference(
				"Contoso.MarkedSibling",
				@"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
namespace Contoso.MarkedSibling;
public sealed class SiblingWorker : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}");
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Contoso.MarkedSibling;
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => {|#0:services.AddHostedService<SiblingWorker>()|};
}";
			await VerifyWithReferencesAsync(source, [sibling], DI007("Contoso.MarkedSibling.SiblingWorker"));
		}

		// F4(b) NOTE — cross-assembly FACTORY-overload pin intentionally omitted. The factory overload
		// AddHostedService(sp => ...) requires the lambda to convert to Func<IServiceProvider, T>, but the
		// cross-assembly fixture closure (sibling compiled against TRUSTED_PLATFORM_ASSEMBLIES + the consumer
		// referencing System.Private.CoreLib to resolve the sibling's Object — see VerifyWithReferencesAsync)
		// is a dual-corelib compilation that breaks predefined-type resolution (CS0518 System.Void/Object) and
		// therefore delegate-type recognition (CS1660), so no such fixture can COMPILE. The factory overload's
		// firing is already pinned in-compilation (FactoryRegistration_PublicConcreteHostedService_Fires) and
		// cross-assembly marker detection is pinned via the direct overload (MarkedReferencedSiblingHostedService_Fires);
		// the analyzer treats both overloads identically once IsCanonicalAddHostedService matches and marker
		// detection is overload-independent, so their intersection covers the factory-sibling case.

		[Fact]
		public async Task ShadowScanMarkerInUnrelatedNamespace_DoesNotEnableRule_Silent()
		{
			// F4(a) pin: a locally-declared AutoDiScanAssemblyAttribute in an UNRELATED namespace must NOT
			// be honored (mirrors the bypass twin's AutoDiBypass_FromUnrelatedNamespace test). Only the
			// canonical NotNot.Bcl.Diagnostics.AutoDiScanAssemblyAttribute opts an assembly in — a shadow
			// with the same simple name is a different type, so the sibling stays non-scan-eligible and the
			// explicit registration is legitimate (silent). Detection is FQN-only.
			var sibling = CompileMetadataReference(
				"Contoso.ShadowMarkerSibling",
				@"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
[assembly: SomeOtherCompany.AutoDiScanAssembly]
namespace SomeOtherCompany
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class AutoDiScanAssemblyAttribute : Attribute { }
}
namespace Contoso.ShadowMarkerSibling
{
    public sealed class SiblingWorker : IHostedService
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}");
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Contoso.ShadowMarkerSibling;
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => services.AddHostedService<SiblingWorker>();
}";
			await VerifyWithReferencesAsync(source, [sibling]);
		}

		[Fact]
		public async Task UnmarkedReferencedSiblingHostedService_Silent()
		{
			// No [assembly: AutoDiScanAssembly] on the sibling — it is not scan-eligible, so the explicit
			// registration is legitimate. Formerly a NotNot.*/Novaleaf.* name would have forced a fire;
			// under the marker contract an unmarked assembly (even a would-be-owned one) is silent.
			var sibling = CompileMetadataReference(
				"Contoso.UnmarkedSibling",
				@"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
namespace Contoso.UnmarkedSibling;
public sealed class PackageWorker : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}");
			var source = @"
using Contoso.UnmarkedSibling;
using Microsoft.Extensions.DependencyInjection;
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => services.AddHostedService<PackageWorker>();
}";
			await VerifyWithReferencesAsync(source, [sibling]);
		}

		[Fact]
		public async Task MarkedSiblingWithTypeBypass_Silent()
		{
			// Sibling assembly is scan-marked, but the hosted type carries [AutoDiBypass] — bypass is
			// higher precedence, so the type is excluded from the scan and the explicit registration is
			// the intentional single owner. Must stay silent.
			var sibling = CompileMetadataReference(
				"Contoso.MarkedTypeBypassSibling",
				@"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
namespace Contoso.MarkedTypeBypassSibling;
[AutoDiBypass]
public sealed class SiblingWorker : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}");
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Contoso.MarkedTypeBypassSibling;
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => services.AddHostedService<SiblingWorker>();
}";
			await VerifyWithReferencesAsync(source, [sibling]);
		}

		[Fact]
		public async Task MarkedSiblingWithAssemblyBypass_Silent()
		{
			// Sibling assembly carries BOTH [assembly: AutoDiScanAssembly] and [assembly: AutoDiBypass].
			// AutoDiBypass is higher precedence: the assembly is excluded from the scan, so the explicit
			// registration is legitimate. Must stay silent.
			var sibling = CompileMetadataReference(
				"Contoso.MarkedAssemblyBypassSibling",
				@"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
[assembly: AutoDiBypass]
namespace Contoso.MarkedAssemblyBypassSibling;
public sealed class SiblingWorker : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}");
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Contoso.MarkedAssemblyBypassSibling;
public static class Bootstrap
{
    public static void Configure(IServiceCollection services)
        => services.AddHostedService<SiblingWorker>();
}";
			await VerifyWithReferencesAsync(source, [sibling]);
		}

		[Theory]
		[InlineData("internal sealed class")]
		[InlineData("public abstract class")]
		public async Task NonScanEligibleHostedService_Silent(string declaration)
		{
			// [assembly: AutoDiScanAssembly] present so the marker gate does NOT silence — this isolates
			// the visibility/abstractness exclusion axis (non-public or abstract types are not scanned).
			var source = $@"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
{declaration} Worker : BackgroundService
{{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}}
public static class Bootstrap
{{
    public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
}}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task PublicNestedInInternalType_IsScrutorScanVisible_Fires()
		{
			// F2 runtime-alignment (runtime-verified): a `public` hosted service nested inside an
			// `internal` container is auto-registered by the Scrutor scan — the scan keys on the type's
			// OWN declared accessibility (`Type.IsNestedPublic`), NOT the effective (enclosing-aware)
			// visibility. Proven by NotNot.Bcl.Core.Tests.AutoDiRuntimeBypassTests
			// .AddNotNotDiServices_PublicNestedInInternal_IsScanned. The explicit AddHostedService of this
			// shape therefore duplicates the scan, so NN_DI_007 MUST fire. (Before the F2 fix the analyzer's
			// effective-public predicate wrongly stayed silent here — a false negative.)
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
internal static class Container
{
    public sealed class Worker : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services) => {|#0:services.AddHostedService<Container.Worker>()|};
}";
			await VerifyAsync(source, DI007("Container.Worker"));
		}

		[Fact]
		public async Task TopLevelInternalType_NotScrutorScanVisible_Silent()
		{
			// F2 runtime-alignment (runtime-verified): a TOP-LEVEL `internal` hosted service is NOT
			// auto-registered by the Scrutor public-only scan (its own declared accessibility is not
			// public). Proven by NotNot.Bcl.Core.Tests.AutoDiRuntimeBypassTests
			// .AddNotNotDiServices_TopLevelInternalHostedService_IsNotScanned. The explicit registration is
			// therefore the sole owner — NN_DI_007 stays silent. (Also covered by the
			// `internal sealed class` arm of NonScanEligibleHostedService_Silent; this pins the runtime
			// matrix's negative half alongside its positive twin above.)
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
internal sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task TypeBypass_ExplicitRegistration_Silent()
		{
			// Scan-marked assembly + [AutoDiBypass] on the type — isolates type-bypass precedence over
			// the scan marker (bypass wins, so the explicit registration is the intentional owner).
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
[AutoDiBypass]
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task AssemblyBypass_ExplicitRegistration_Silent()
		{
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiBypass]
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public static class Bootstrap
{
    public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task UnrelatedMethodWithSameName_Silent()
		{
			// Scan-marked assembly so marker-absence is NOT the reason for silence — this isolates the
			// OriginalDefinition method-identity hardening: a same-named AddHostedService<T> on an
			// unrelated type resolves to a different method symbol and must stay silent.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class CustomCollection
{
    public void AddHostedService<T>() where T : class, IHostedService { }
}
public static class Bootstrap
{
    public static void Configure(CustomCollection services) => services.AddHostedService<Worker>();
}";
			await VerifyAsync(source);
		}

		[Fact]
		public async Task SpoofExtensionInMicrosoftDiCustomNamespace_Silent()
		{
			// Scan-marked assembly + a spoof AddHostedService<T> declared inside a
			// Microsoft.Extensions.DependencyInjection.* namespace. The OriginalDefinition identity check
			// resolves it to a symbol absent from the cached canonical set, so it stays silent — proving
			// namespace proximity alone cannot masquerade as the canonical method.
			var source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Custom;
using NotNot.Bcl.Diagnostics;
[assembly: AutoDiScanAssembly]
public sealed class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class FakeCollection { }
namespace Microsoft.Extensions.DependencyInjection.Custom
{
    public static class SpoofHostedServiceExtensions
    {
        public static void AddHostedService<T>(this global::FakeCollection services)
            where T : class, IHostedService { }
    }
}
public static class Bootstrap
{
    public static void Configure(FakeCollection services) => services.AddHostedService<Worker>();
}";
			await VerifyAsync(source);
		}
	}

	// Reflection-based contract tests (Wave 1 I1 review)
	// ══════════════════════════════════════════════════════════════════════════

	public class Reflection_InternalConstants
	{
		[Fact]
		public void MarkerInterfaceConstants_MatchActualSymbols()
		{
			// Wave 1 I1 review finding: the FQN constants in DiAnalyzerHelpers
			// (DiSingletonServiceFullName / DiScopedServiceFullName / DiTransientServiceFullName)
			// MUST match the real symbol display strings from a reference compilation. If a future
			// refactor moves the markers into a namespace (e.g., `namespace NotNot.DI;`), the
			// constants would silently mismatch and `compilation.GetTypeByMetadataName(...)` would
			// return null — the analyzer would silently disable itself on every compilation.
			//
			// This test resolves the internal `DiAnalyzerHelpers` type via reflection (the helpers
			// class is `internal` and the tests project lacks `InternalsVisibleTo`), reads each
			// constant, and asserts a reference compilation can resolve the matching type by
			// metadata name.
			//
			// Reflection access pattern mirrors the source-generator output verification pattern
			// from MEMORY.md: load the analyzer assembly via `typeof(DiMarkerEnforcementAnalyzer)`,
			// resolve the internal helpers class by full name, read the public constants via
			// `BindingFlags.NonPublic | Static`.
			var analyzerAssembly = typeof(DiMarkerEnforcementAnalyzer).Assembly;
			var helpersType = analyzerAssembly.GetType("NotNot.Analyzers.Architecture.DI.DiAnalyzerHelpers");
			Assert.NotNull(helpersType);

			var singletonField = helpersType!.GetField("DiSingletonServiceFullName",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			var scopedField = helpersType.GetField("DiScopedServiceFullName",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			var transientField = helpersType.GetField("DiTransientServiceFullName",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			Assert.NotNull(singletonField);
			Assert.NotNull(scopedField);
			Assert.NotNull(transientField);

			var singletonFqn = (string?)singletonField!.GetValue(null);
			var scopedFqn = (string?)scopedField!.GetValue(null);
			var transientFqn = (string?)transientField!.GetValue(null);
			Assert.False(string.IsNullOrEmpty(singletonFqn));
			Assert.False(string.IsNullOrEmpty(scopedFqn));
			Assert.False(string.IsNullOrEmpty(transientFqn));

			// Verify the constants name actual loaded symbols. The markers live in the GLOBAL
			// namespace (no `namespace` directive in _DIServiceMarkers.cs), so the metadata-name
			// lookup should resolve when the analyzer's compilation-start gate runs.
			//
			// We can't easily build a Roslyn Compilation here without reproducing the entire
			// VerifyAsync setup. Instead, validate the runtime-assembly side: each constant must
			// match an actual loaded interface type's name in NotNot.Bcl.Core.
			var bclAssembly = typeof(IDiSingletonService).Assembly;
			var singletonType = bclAssembly.GetType(singletonFqn!);
			var scopedType = bclAssembly.GetType(scopedFqn!);
			var transientType = bclAssembly.GetType(transientFqn!);
			Assert.NotNull(singletonType);
			Assert.NotNull(scopedType);
			Assert.NotNull(transientType);
			Assert.Equal(typeof(IDiSingletonService), singletonType);
			Assert.Equal(typeof(IDiScopedService), scopedType);
			Assert.Equal(typeof(IDiTransientService), transientType);

			// Wave 2 Cluster E: removed `await Task.CompletedTask;` and `async` keyword. xUnit
			// `[Fact]` accepts both synchronous (`void`) and asynchronous (`async Task`) methods;
			// this test is pure reflection and has no asynchronous work, so synchronous is correct
			// and eliminates the spurious PH_S020 "async method without await" warning.
		}
	}
}
