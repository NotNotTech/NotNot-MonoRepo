using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.LiteDDD;

namespace NotNot.BlazorAnalyzers.Tests.LiteDDD;

/// <summary>
/// Tests for <see cref="LdddInjectAndCallAnalyzer"/> (NN_LDDD_003 / NN_LDDD_005) — the Wave-2
/// component-boundary rules. Verifies positive/negative/framework-allow-list/bypass pathways for
/// <c>[Inject]</c>-of-unmarked-service (NN_LDDD_003) and direct-invocation-of-domain-service
/// (NN_LDDD_005). Mirrors <see cref="LdddAssemblyFenceAnalyzerTests"/> framework setup (assembly
/// rename via <see cref="CSharpAnalyzerTest{TAnalyzer,TVerifier}.SolutionTransforms"/>) plus
/// adds the Blazor + SignalR + framework stubs needed for component-boundary analysis.
/// </summary>
/// <remarks>
/// <para>
/// <b>Framework stubs</b> (added via <see cref="SolutionState.Sources"/>): the analyzer matches
/// <c>Microsoft.AspNetCore.Components.ComponentBase</c>, <c>InjectAttribute</c>, and the
/// framework allow-list (NavigationManager, HubConnection) by name + namespace. The test
/// fixtures declare stub types in the matching namespaces so the analyzer's semantic resolution
/// succeeds without pulling in actual ASP.NET Core / SignalR assemblies. The
/// <see cref="LdddDataServiceAttribute"/> + <see cref="LdddDomainServiceAttribute"/> + bypass
/// stubs cover the marker-attribute matching paths.
/// </para>
/// <para>
/// <b>Diagnostic anchor</b>: for NN_LDDD_003 the analyzer reports at the property
/// declaration's <see cref="ISymbol.DeclaringSyntaxReferences"/> location → covers the entire
/// <c>PropertyDeclarationSyntax</c> span; for NN_LDDD_005 at
/// <see cref="InvocationExpressionSyntax.GetLocation"/> → covers the full invocation
/// expression including receiver + arguments.
/// </para>
/// </remarks>
public class LdddInjectAndCallAnalyzerTests
{
	// ── Test infrastructure ─────────────────────────────────────────────────────

	/// <summary>
	/// Default Shared-assembly name used by tests where the consumer must be classified Shared
	/// (positive/negative/bypass tests — analyzer gates on <c>.Shared</c>/<c>.Client</c> suffix).
	/// </summary>
	private const string SharedAssemblyName = "TestProject.Shared";

	/// <summary>
	/// Trivial placeholder used as <see cref="AnalyzerTest{TVerifier}.TestCode"/> when the real
	/// source under test is added via <see cref="SolutionState.Sources"/>.
	/// </summary>
	private const string Placeholder = "class Placeholder { }";

	/// <summary>
	/// Stub for <see cref="Microsoft.AspNetCore.Components.ComponentBase"/> +
	/// <c>InjectAttribute</c>. <see cref="LdddInjectAndCallAnalyzer"/> /
	/// <see cref="BlazorLifecycleHelpers.IsBlazorComponent"/> matches by simple-name +
	/// containing-namespace, so the stub need not implement real Blazor behavior — only the
	/// type identity has to match.
	/// </summary>
	private const string BlazorComponentsStub = @"
namespace Microsoft.AspNetCore.Components
{
    public abstract class ComponentBase { }

    [System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class InjectAttribute : System.Attribute { }

    public sealed class NavigationManager { }
}
";

	/// <summary>
	/// Stub for <c>Microsoft.AspNetCore.SignalR.Client.HubConnection</c> — entry in the
	/// analyzer's <see cref="LdddInjectAndCallAnalyzer"/> <c>FrameworkAllowList</c>. Matched by
	/// fully-qualified name (stripped of <c>global::</c>), so the stub must live in the exact
	/// namespace.
	/// </summary>
	private const string HubConnectionStub = @"
namespace Microsoft.AspNetCore.SignalR.Client
{
    public sealed class HubConnection { }
}
";

	/// <summary>
	/// Stub for <see cref="NotNot.Bcl.Diagnostics.LdddDataServiceAttribute"/>. The analyzer
	/// matches by simple name OR fully-qualified name
	/// (<see cref="LdddAnalyzerHelpers.LdddDataServiceAttributeSimpleName"/> /
	/// <see cref="LdddAnalyzerHelpers.LdddDataServiceAttributeFullName"/>), so consumers may
	/// declare the attribute locally as in production — the canonical
	/// <c>NotNot.Bcl.Diagnostics</c> location is used here to exercise the fully-qualified
	/// match path.
	/// </summary>
	private const string LdddDataServiceAttributeStub = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(
        System.AttributeTargets.Class | System.AttributeTargets.Interface,
        Inherited = true, AllowMultiple = false)]
    public sealed class LdddDataServiceAttribute : System.Attribute { }
}
";

	/// <summary>
	/// Stub for <see cref="NotNot.Bcl.Diagnostics.LdddDomainServiceAttribute"/>. Same matching
	/// rules as the DataService stub above — analyzer accepts simple name or fully-qualified
	/// name.
	/// </summary>
	private const string LdddDomainServiceAttributeStub = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(
        System.AttributeTargets.Class | System.AttributeTargets.Interface,
        Inherited = true, AllowMultiple = false)]
    public sealed class LdddDomainServiceAttribute : System.Attribute { }
}
";

	/// <summary>
	/// Stub for the canonical bypass attribute. Identical to the Wave-1 fixture — the
	/// <see cref="LdddInjectAndCallAnalyzer"/> uses
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/> +
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(ISymbol)"/> for assembly-scope and
	/// symbol-scope short-circuit, both matched by simple name.
	/// </summary>
	private const string BypassAttributeStub = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(
        System.AttributeTargets.Assembly | System.AttributeTargets.Class
        | System.AttributeTargets.Struct | System.AttributeTargets.Interface
        | System.AttributeTargets.Method,
        AllowMultiple = false, Inherited = false)]
    public sealed class LdddBypassAttribute : System.Attribute { }
}
";

	/// <summary>
	/// Returns a solution-transform delegate that renames the primary project to
	/// <paramref name="assemblyName"/>. Wave 2 analyzer gates on <c>.Shared</c>/<c>.Client</c>
	/// suffix; the rename moves the test compilation past that gate.
	/// </summary>
	private static Func<Solution, ProjectId, Solution> RenameAssembly(string assemblyName)
	{
		return (solution, projectId) =>
		{
			var project = solution.GetProject(projectId);
			return project == null
				? solution
				: solution.WithProjectAssemblyName(projectId, assemblyName);
		};
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// NN_LDDD_003 — Injected service not marked [LdddDataService]
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — Shared-assembly Blazor component <c>[Inject]</c>s an interface that lacks the
	/// <c>[LdddDataService]</c> marker AND is not in the framework allow-list. The analyzer's
	/// <see cref="LdddInjectAndCallAnalyzer.AnalyzeNamedTypeForInject"/> symbol-action fires
	/// NN_LDDD_003 at the property declaration.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_003_Positive_UnmarkedServiceInjected_FiresDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
namespace TestProject.Shared
{
    public interface IMyDomainService { void Compute(); }

    public class MyComponent : ComponentBase
    {
        [Inject] public IMyDomainService Svc { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Property declaration `[Inject] public IMyDomainService Svc { get; set; } = null!;`
		// at line 8. DeclaringSyntaxReferences for a property returns the full
		// PropertyDeclarationSyntax span — leading indent (8 spaces) → col 9, body is
		// 59 chars including the `[Inject]` attribute-list prefix, end-exclusive col 68.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddInjectAndCallAnalyzer.LDDD003_Rule)
				.WithSpan("/0/Test0.cs", 8, 9, 8, 68)
				.WithArguments(
					"TestProject.Shared.IMyDomainService",
					"MyComponent"));

		// ABMCS additive alias — NN_ABMCS_003 dual-emit, same span, same args.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddInjectAndCallAnalyzer.ABMCS003_Rule)
				.WithSpan("/0/Test0.cs", 8, 9, 8, 68)
				.WithArguments(
					"TestProject.Shared.IMyDomainService",
					"MyComponent"));

		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — same shape as the positive test, but the injected interface carries
	/// <c>[LdddDataService]</c>. The marker-match short-circuits the per-property check; no
	/// diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_003_Negative_LdddDataServiceMarked_NoDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    [LdddDataService]
    public interface IMyDataSvc { void Get(); }

    public class MyComponent : ComponentBase
    {
        [Inject] public IMyDataSvc Svc { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(LdddDataServiceAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — [LdddDataService] marker satisfies the per-property check.
		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE (framework allow-list) — Blazor component <c>[Inject]</c>s
	/// <c>NavigationManager</c>, which is in
	/// <see cref="LdddInjectAndCallAnalyzer.FrameworkAllowList"/>. No diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_003_Negative_NavigationManager_NoDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
namespace TestProject.Shared
{
    public class MyComponent : ComponentBase
    {
        [Inject] public NavigationManager Nav { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — NavigationManager is in the framework allow-list.
		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE (SignalR allow-list) — Blazor component <c>[Inject]</c>s a
	/// <c>HubConnection</c>, which is the SignalR allow-list entry. No diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_003_Negative_HubConnection_NoDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
namespace TestProject.Shared
{
    public class MyComponent : ComponentBase
    {
        [Inject] public HubConnection Hub { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(HubConnectionStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — HubConnection is in the framework allow-list.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS (assembly-level) — same positive shape, but the consuming assembly carries
	/// <c>[assembly: LdddBypass]</c>. The compilation-level short-circuit in
	/// <see cref="LdddInjectAndCallAnalyzer.ShouldAnalyzeCompilation"/> suppresses both rules.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_003_Bypass_AssemblyLevel_NoDiagnostic()
	{
		// `[assembly: ...]` attributes must appear BEFORE `using` directives (CS1529).
		var consumerSource = @"using Microsoft.AspNetCore.Components;

[assembly: NotNot.Bcl.Diagnostics.LdddBypass]

namespace TestProject.Shared
{
    public interface IMyDomainService { void Compute(); }

    public class MyComponent : ComponentBase
    {
        [Inject] public IMyDomainService Svc { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(BypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — assembly-level [LdddBypass] short-circuits.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS (type-level) — same positive shape, but the partial class carries
	/// <c>[LdddBypass]</c>. The per-symbol bypass check in
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(ISymbol)"/> short-circuits the
	/// rule for the component class's members.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_003_Bypass_TypeLevel_NoDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    public interface IMyDomainService { void Compute(); }

    [LdddBypass]
    public partial class MyComponent : ComponentBase
    {
        [Inject] public IMyDomainService Svc { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(BypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — type-scope [LdddBypass] suppresses the per-class check.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// NN_LDDD_005 — Direct call to [LdddDomainService] from Blazor component
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — ComponentBase-derived class invokes an instance method on a receiver whose
	/// type carries <c>[LdddDomainService]</c>. The analyzer's
	/// <see cref="LdddInjectAndCallAnalyzer.AnalyzeInvocationForDomainCall"/> syntax-action
	/// fires NN_LDDD_005 at the InvocationExpression location.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_005_Positive_CallOnDomainService_FiresDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    [LdddDomainService]
    public class MyDomainService { public int Compute(int x) => x + 1; }

    public class MyComponent : ComponentBase
    {
        private MyDomainService _domainSvc = new MyDomainService();

        public int Run() => _domainSvc.Compute(42);
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(LdddDomainServiceAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Invocation `_domainSvc.Compute(42)` at line 12 — start col 29
		// (after `        public int Run() => ` = 8 spaces + 21 chars `public int Run() => ` = col 29),
		// length = `_domainSvc.Compute(42)` = 22 chars → end col 51.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddInjectAndCallAnalyzer.LDDD005_Rule)
				.WithSpan("/0/Test0.cs", 12, 29, 12, 51)
				.WithArguments(
					"Compute",
					"TestProject.Shared.MyDomainService",
					"MyComponent"));

		// ABMCS additive alias — NN_ABMCS_002 dual-emit (RENUMBERED from _005 to _002 by
		// design). Same span, same args.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddInjectAndCallAnalyzer.ABMCS002_Rule)
				.WithSpan("/0/Test0.cs", 12, 29, 12, 51)
				.WithArguments(
					"Compute",
					"TestProject.Shared.MyDomainService",
					"MyComponent"));

		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — same call site, but the receiver's type has NO <c>[LdddDomainService]</c>
	/// marker. The marker-match short-circuits the per-invocation check; no diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_005_Negative_NoMarker_NoDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
namespace TestProject.Shared
{
    public class MyService { public int Compute(int x) => x + 1; }

    public class MyComponent : ComponentBase
    {
        private MyService _svc = new MyService();

        public int Run() => _svc.Compute(42);
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — receiver type has no [LdddDomainService] marker.
		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — call site is in a plain class (not ComponentBase-derived). The enclosing-type
	/// check requires <see cref="BlazorLifecycleHelpers.IsBlazorComponent"/>; non-components are
	/// out-of-scope.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_005_Negative_NonComponentBase_NoDiagnostic()
	{
		var consumerSource = @"using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    [LdddDomainService]
    public class MyDomainService { public int Compute(int x) => x + 1; }

    public class PlainHelper
    {
        private MyDomainService _domainSvc = new MyDomainService();

        public int Run() => _domainSvc.Compute(42);
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(LdddDomainServiceAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — PlainHelper is not ComponentBase-derived.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS (type-level / class-level) — same positive shape, but the partial component class
	/// carries <c>[LdddBypass]</c>. The per-symbol bypass walk in
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(ISymbol)"/> short-circuits the rule
	/// for invocations inside the class.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_005_Bypass_ClassLevel_NoDiagnostic()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    [LdddDomainService]
    public class MyDomainService { public int Compute(int x) => x + 1; }

    [LdddBypass]
    public partial class MyComponent : ComponentBase
    {
        private MyDomainService _domainSvc = new MyDomainService();

        public int Run() => _domainSvc.Compute(42);
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(LdddDomainServiceAttributeStub);
		test.TestState.Sources.Add(BypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — type-scope [LdddBypass] suppresses the per-class check.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// ABMCS additive-alias attribute recognition tests
	// ═══════════════════════════════════════════════════════════════════════════
	//
	// These tests verify the analyzer recognizes the NEW ABMCS-vocabulary attributes
	// ([FeatureContract], [FeatureServerLogic], [FeatureBypass]) with identical semantics to
	// their legacy [Lddd*] counterparts. The dual-emit pattern means both NN_LDDD_* and
	// NN_ABMCS_* diagnostics fire on the same violation; these tests focus on the new
	// attribute names as inputs (not on the dual-emit outputs).

	/// <summary>
	/// Stub for the canonical <see cref="NotNot.Bcl.Diagnostics.FeatureContractAttribute"/> —
	/// the ABMCS-vocabulary sibling of <see cref="LdddDataServiceAttribute"/>. Analyzer matches
	/// by simple name OR fully-qualified name.
	/// </summary>
	private const string FeatureContractAttributeStub = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(
        System.AttributeTargets.Class | System.AttributeTargets.Interface,
        Inherited = true, AllowMultiple = false)]
    public sealed class FeatureContractAttribute : System.Attribute { }
}
";

	/// <summary>
	/// Stub for the canonical <see cref="NotNot.Bcl.Diagnostics.FeatureServerLogicAttribute"/> —
	/// the ABMCS-vocabulary sibling of <see cref="LdddDomainServiceAttribute"/>.
	/// </summary>
	private const string FeatureServerLogicAttributeStub = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(
        System.AttributeTargets.Class | System.AttributeTargets.Interface,
        Inherited = true, AllowMultiple = false)]
    public sealed class FeatureServerLogicAttribute : System.Attribute { }
}
";

	/// <summary>
	/// Stub for the canonical <see cref="NotNot.Bcl.Diagnostics.FeatureBypassAttribute"/> —
	/// the ABMCS-vocabulary sibling of <see cref="LdddBypassAttribute"/>.
	/// </summary>
	private const string FeatureBypassAttributeStub = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(
        System.AttributeTargets.Assembly | System.AttributeTargets.Class
        | System.AttributeTargets.Struct | System.AttributeTargets.Interface
        | System.AttributeTargets.Method,
        AllowMultiple = false, Inherited = false)]
    public sealed class FeatureBypassAttribute : System.Attribute { }
}
";

	/// <summary>
	/// NEGATIVE — same shape as the NN_LDDD_003 positive test, but the injected interface
	/// carries the NEW ABMCS-vocabulary <c>[FeatureContract]</c> instead of the legacy
	/// <c>[LdddDataService]</c>. The analyzer's additive-alias detection in
	/// <c>IsLdddDataServiceMarked</c> must recognize the new attribute as equivalent — zero
	/// diagnostics fire.
	/// </summary>
	[Fact]
	public async Task Abmcs_FeatureContract_Marker_SuppressesNN_LDDD_003_And_NN_ABMCS_003()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    [FeatureContract]
    public interface IMyDataSvc { void Get(); }

    public class MyComponent : ComponentBase
    {
        [Inject] public IMyDataSvc Svc { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(FeatureContractAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — [FeatureContract] marker satisfies the per-property check
		// equivalently to [LdddDataService], so neither NN_LDDD_003 nor NN_ABMCS_003 fires.
		await test.RunAsync();
	}

	/// <summary>
	/// POSITIVE — same shape as the NN_LDDD_005 positive test, but the receiver's class carries
	/// the NEW ABMCS-vocabulary <c>[FeatureServerLogic]</c> instead of the legacy
	/// <c>[LdddDomainService]</c>. The analyzer's <c>IsLdddDomainServiceMarked</c> additive-
	/// alias detection must trip the rule and dual-emit both NN_LDDD_005 + NN_ABMCS_002.
	/// </summary>
	[Fact]
	public async Task Abmcs_FeatureServerLogic_Marker_FiresNN_LDDD_005_And_NN_ABMCS_002()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;
using NotNot.Bcl.Diagnostics;
namespace TestProject.Shared
{
    [FeatureServerLogic]
    public class MyServerLogic { public int Compute(int x) => x + 1; }

    public class MyComponent : ComponentBase
    {
        private MyServerLogic _svc = new MyServerLogic();

        public int Run() => _svc.Compute(42);
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(FeatureServerLogicAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Invocation `_svc.Compute(42)` at line 12 — start col 29 (`        public int Run() => ` = 28 chars), length = `_svc.Compute(42)` = 16 chars → end col 45.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddInjectAndCallAnalyzer.LDDD005_Rule)
				.WithSpan("/0/Test0.cs", 12, 29, 12, 45)
				.WithArguments(
					"Compute",
					"TestProject.Shared.MyServerLogic",
					"MyComponent"));

		// Dual-emit verification — NN_ABMCS_002 fires alongside the legacy ID.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddInjectAndCallAnalyzer.ABMCS002_Rule)
				.WithSpan("/0/Test0.cs", 12, 29, 12, 45)
				.WithArguments(
					"Compute",
					"TestProject.Shared.MyServerLogic",
					"MyComponent"));

		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS — same NN_LDDD_003 positive shape, but the consuming assembly carries the NEW
	/// ABMCS-vocabulary <c>[assembly: FeatureBypass]</c> instead of the legacy
	/// <c>[assembly: LdddBypass]</c>. The analyzer's
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/> additive-alias
	/// detection must short-circuit both rule families.
	/// </summary>
	[Fact]
	public async Task Abmcs_FeatureBypass_Assembly_ShortCircuitsBothRuleFamilies()
	{
		var consumerSource = @"using Microsoft.AspNetCore.Components;

[assembly: NotNot.Bcl.Diagnostics.FeatureBypass]

namespace TestProject.Shared
{
    public interface IMyDomainService { void Compute(); }

    public class MyComponent : ComponentBase
    {
        [Inject] public IMyDomainService Svc { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddInjectAndCallAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(FeatureBypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — [assembly: FeatureBypass] short-circuits both NN_LDDD_003
		// AND NN_ABMCS_003 (additive-alias matching in HasLdddBypassAttribute).
		await test.RunAsync();
	}
}
