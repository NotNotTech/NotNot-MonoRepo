using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.ABMCS;

namespace NotNot.BlazorAnalyzers.Tests.ABMCS;

/// <summary>
/// Tests for <see cref="AbmcsConcreteApiResponseAnalyzer"/> (<c>NN_ABMCS_009</c>) — concrete
/// <c>Refit.ApiResponse&lt;T&gt;</c> referenced at the transport boundary. Verifies
/// positive/negative/bypass pathways for the rule that requires <c>IApiResponse&lt;T&gt;</c>
/// over the concrete class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Framework stubs</b>: Refit's <c>ApiResponse&lt;T&gt;</c> + <c>IApiResponse&lt;T&gt;</c>
/// are stubbed in the <c>Refit</c> namespace so the analyzer's semantic resolution succeeds
/// without pulling in the actual Refit assembly. The shapes match Refit's public surface only
/// at the level needed for the analyzer to identify them (no behavioral parity required).
/// </para>
/// </remarks>
public class AbmcsConcreteApiResponseAnalyzerTests
{
	private const string SharedAssemblyName = "TestProject.Shared";
	private const string ServerAssemblyName = "TestProject.Server";

	/// <summary>
	/// Stub for Refit's <c>ApiResponse&lt;T&gt;</c> (concrete class) and <c>IApiResponse&lt;T&gt;</c>
	/// (canonical interface). The analyzer matches the open-generic full name
	/// <c>Refit.ApiResponse&lt;TResponse&gt;</c> against
	/// <see cref="ITypeSymbol.OriginalDefinition"/> — so the stub's type parameter name must
	/// match (<c>TResponse</c>).
	/// </summary>
	private const string RefitApiResponseStub = @"
namespace Refit
{
    public interface IApiResponse<out TResponse> { }

    public class ApiResponse<TResponse> : IApiResponse<TResponse>
    {
        public TResponse? Content { get; set; }
    }
}
";

	/// <summary>
	/// Stub for the canonical legacy bypass attribute. Matched by simple name OR fully-qualified
	/// name per <c>LdddAnalyzerHelpers.HasLdddBypassAttribute</c>.
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
	/// Stub for the canonical ABMCS-vocabulary bypass attribute
	/// (<c>NotNot.Bcl.Diagnostics.FeatureBypassAttribute</c>). The dual-attribute matching in
	/// <c>LdddAnalyzerHelpers.HasLdddBypassAttribute</c> treats <c>[FeatureBypass]</c> as
	/// equivalent to <c>[LdddBypass]</c> — both short-circuit the rule.
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

	/// <summary>
	/// POSITIVE — Shared-assembly transport contract declares a method returning the CONCRETE
	/// <c>Refit.ApiResponse&lt;OrderDto&gt;</c>. The analyzer's IdentifierName action resolves
	/// the type and fires <c>NN_ABMCS_009</c>.
	/// </summary>
	[Fact]
	public async Task NN_ABMCS_009_Positive_ConcreteApiResponse_FiresDiagnostic()
	{
		var consumerSource = @"using Refit;
using System.Threading.Tasks;
namespace TestProject.Shared
{
    public class OrderDto { public string Id { get; set; } = """"; }

    public interface IOrderDataService
    {
        Task<ApiResponse<OrderDto>> GetAsync(string id);
    }
}
";

		var test = new CSharpAnalyzerTest<AbmcsConcreteApiResponseAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(RefitApiResponseStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// `ApiResponse` identifier at line 9, col 14 (after `        Task<` = 8 spaces + 5 chars = col 14).
		// Length = "ApiResponse" = 11 chars → end col 25.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(AbmcsConcreteApiResponseAnalyzer.ABMCS009_Rule)
				.WithSpan("/0/Test0.cs", 9, 14, 9, 25)
				.WithArguments("TestProject.Shared.OrderDto"));

		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — same shape as the positive test, but returns <c>IApiResponse&lt;OrderDto&gt;</c>
	/// (the canonical interface). The analyzer's open-generic full-name match against
	/// <c>Refit.ApiResponse&lt;TResponse&gt;</c> does not match <c>IApiResponse</c> (which is
	/// the interface, full-name <c>Refit.IApiResponse&lt;TResponse&gt;</c>).
	/// </summary>
	[Fact]
	public async Task NN_ABMCS_009_Negative_InterfaceIApiResponse_NoDiagnostic()
	{
		var consumerSource = @"using Refit;
using System.Threading.Tasks;
namespace TestProject.Shared
{
    public class OrderDto { public string Id { get; set; } = """"; }

    public interface IOrderDataService
    {
        Task<IApiResponse<OrderDto>> GetAsync(string id);
    }
}
";

		var test = new CSharpAnalyzerTest<AbmcsConcreteApiResponseAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(RefitApiResponseStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — IApiResponse is the canonical interface form.
		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — Server-assembly references the concrete <c>ApiResponse&lt;T&gt;</c> (legitimate
	/// usage in controller-mapping code where the result is materialized). The analyzer gates on
	/// <c>.Shared</c> / <c>.Client</c> assembly suffix, so Server assemblies bypass the rule.
	/// </summary>
	[Fact]
	public async Task NN_ABMCS_009_Negative_ServerAssembly_NoDiagnostic()
	{
		var consumerSource = @"using Refit;
using System.Threading.Tasks;
namespace TestProject.Server
{
    public class OrderDto { public string Id { get; set; } = """"; }

    public class OrderUseCases
    {
        public Task<ApiResponse<OrderDto>> GetAsync(string id) => null!;
    }
}
";

		var test = new CSharpAnalyzerTest<AbmcsConcreteApiResponseAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(RefitApiResponseStub);
		test.SolutionTransforms.Add(RenameAssembly(ServerAssemblyName));

		// Expect ZERO diagnostics — Server-assembly consumer is not gated.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS — same positive shape, but the assembly carries
	/// <c>[assembly: LdddBypass]</c>. The compilation-level short-circuit in
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/> suppresses the
	/// rule. Verifies the additive-alias bypass attribute pathway works for the new ABMCS rule.
	/// </summary>
	[Fact]
	public async Task NN_ABMCS_009_Bypass_AssemblyLevelLdddBypass_NoDiagnostic()
	{
		var consumerSource = @"using Refit;
using System.Threading.Tasks;

[assembly: NotNot.Bcl.Diagnostics.LdddBypass]

namespace TestProject.Shared
{
    public class OrderDto { public string Id { get; set; } = """"; }

    public interface IOrderDataService
    {
        Task<ApiResponse<OrderDto>> GetAsync(string id);
    }
}
";

		var test = new CSharpAnalyzerTest<AbmcsConcreteApiResponseAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(RefitApiResponseStub);
		test.TestState.Sources.Add(BypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — [assembly: LdddBypass] short-circuits the rule.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS — same positive shape with <c>[assembly: FeatureBypass]</c> (ABMCS-vocabulary
	/// attribute) instead of the legacy <c>[assembly: LdddBypass]</c>. Verifies the additive-alias
	/// dual-attribute matching in
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/> recognizes the
	/// new attribute as equivalent — Peer F1 work-order item 7 ("new bypass tests verify
	/// [assembly: FeatureBypass] short-circuits both rule families").
	/// </summary>
	[Fact]
	public async Task NN_ABMCS_009_Bypass_AssemblyLevelFeatureBypass_NoDiagnostic()
	{
		var consumerSource = @"using Refit;
using System.Threading.Tasks;

[assembly: NotNot.Bcl.Diagnostics.FeatureBypass]

namespace TestProject.Shared
{
    public class OrderDto { public string Id { get; set; } = """"; }

    public interface IOrderDataService
    {
        Task<ApiResponse<OrderDto>> GetAsync(string id);
    }
}
";

		var test = new CSharpAnalyzerTest<AbmcsConcreteApiResponseAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(RefitApiResponseStub);
		test.TestState.Sources.Add(FeatureBypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — [assembly: FeatureBypass] short-circuits the rule via the
		// dual-attribute simple-name match in HasLdddBypassAttribute.
		await test.RunAsync();
	}
}
