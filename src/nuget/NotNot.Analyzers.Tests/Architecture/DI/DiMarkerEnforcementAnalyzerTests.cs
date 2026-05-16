using System.IO;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture.DI;

namespace NotNot.Analyzers.Tests.Architecture.DI;

/// <summary>
/// Tests for <see cref="DiMarkerEnforcementAnalyzer"/> — NN_DI_001 (lifetime mismatch),
/// NN_DI_002 (redundant registration), NN_DI_003 (passthrough factory),
/// NN_DI_004 (marker + IHostedService), plus cross-cutting bypass behavior.
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
		foreach (var asm in refs)
		{
			test.TestState.AdditionalReferences.Add(asm);
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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

namespace SomeOtherLib
{
    public interface IDiSingletonService { } // SHADOW — different namespace, NOT the real marker.
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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

		[Fact]
		public async Task MultipleRegistrationsSameType_EachEvaluatedIndependently()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Dep1 : IDiSingletonService { }
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

		[Fact]
		public async Task NonPassthroughFactory_MethodCallBody_NoDiagnostic()
		{
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service
{
    public static Service Create() => new Service();
}

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        sc.AddSingleton<Service>(sp => Service.Create());
    }
}";
			await VerifyAsync(source);
		}

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
		public async Task PassthroughFactory_NoArgsConstructor_FiresDI003_FullyReplaceableByAutoRegistration()
		{
			// Zero-arg `new Service()` IS classified as a passthrough by design (Wave 1 M1
			// review disposition). The lambda body provides ZERO information that
			// auto-registration on the marker interface couldn't already supply — adding the
			// marker is fully equivalent. NN_DI_003 is Info severity and the suggestion
			// ("add IDi{L}Service marker, remove the factory") is correct and actionable.
			//
			// Helper mechanics: `objectCreation.Arguments.Length == 0` makes the
			// every-arg-is-GetRequiredService foreach a no-op, IsPassthroughFactory returns
			// true with concreteType=Service. Concrete is unmarked, not third-party, not
			// bypassed → NN_DI_003 fires.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

public class Service { }

public class Bootstrap
{
    public static void Configure(IServiceCollection sc)
    {
        {|#0:sc.AddSingleton<Service>(sp => new Service())|};
    }
}";
			await VerifyAsync(source, DI003("Singleton", "Service"));
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
		public async Task NoMarkerSymbolsInCompilation_ShortCircuits_NoDiagnostics()
		{
			// This is a structural test: even with `AddSingleton<X>()` calls present,
			// if the marker interfaces don't resolve (no reference to NotNot.Bcl.Core),
			// the compilation-start gate returns before registering operation handlers.
			//
			// We can't actually REMOVE the NotNot.Bcl.Core reference in this fixture (the test
			// helper adds it unconditionally), so we instead exercise the equivalent: a source
			// with no types that implement markers and no DI registrations involving marker-bearing
			// types — verifies the analyzer is well-behaved on inert compilations.
			var source = @"
using Microsoft.Extensions.DependencyInjection;

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
	}
}
