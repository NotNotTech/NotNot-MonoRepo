using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for <see cref="NnAppSettingsServerOnlyReadAnalyzer"/> (NN_C005).
/// </summary>
/// <remarks>
/// <para>
/// Fixtures hand-author BOTH generated trees (NN_C004-test pattern — no real generator run): a FULL
/// <c>AppSettings</c> tree carrying <c>[GeneratedCode("NotNot.AppSettings", ...)]</c> with a ServerOnly
/// <c>ServerKey</c> plus a whitelisted <c>ClientKey</c>, AND a parallel <c>_ClientAppSettings</c> mirror
/// that EXCLUDES <c>ServerKey</c> but INCLUDES <c>ClientKey</c> — mirroring the real generator's pruning
/// (a ServerOnly key is simply absent from <c>_ClientAppSettings</c>).
/// </para>
/// <para>
/// The analyzer's <c>.Shared</c>/<c>.Client</c>/<c>.Server</c> suffix gate reads
/// <c>compilation.Assembly.Identity.Name</c>, so each case renames the test compilation via
/// <see cref="CSharpAnalyzerTest{TAnalyzer,TVerifier}.SolutionTransforms"/> (inlined
/// <see cref="RenameAssembly"/>, the BlazorAnalyzers.Tests idiom). The generated-type NAMESPACE
/// (<c>App.AppSettingsGen</c>) is independent of the assembly name.
/// </para>
/// </remarks>
public class NnAppSettingsServerOnlyReadAnalyzerTests
{
	private const string SharedAssemblyName = "App.Shared";
	private const string ServerAssemblyName = "App.Server";

	/// <summary>
	/// Shared declaration of the full <c>AppSettings</c> tree (with a ServerOnly + a whitelisted key), the
	/// pruned <c>_ClientAppSettings</c> mirror, and a non-AppSettings type. Included in every fixture source.
	/// </summary>
	private const string SettingsDecl = @"
namespace App.AppSettingsGen
{
    [System.Runtime.CompilerServices.CompilerGenerated]
    [System.CodeDom.Compiler.GeneratedCode(""NotNot.AppSettings"", ""1.2.3"")]
    public partial class AppSettings
    {
        public double? ClientKey { get; set; }
        public double? ServerKey { get; set; }
    }

    // The pruned client snapshot: ClientKey is whitelisted (present), ServerKey is ServerOnly (absent).
    [System.Runtime.CompilerServices.CompilerGenerated]
    [System.CodeDom.Compiler.GeneratedCode(""NotNot.AppSettings"", ""1.2.3"")]
    public partial class _ClientAppSettings
    {
        public double? ClientKey { get; set; }
    }
}

public class NotSettings
{
    public int? Prop { get; set; }
}
";

	private static async Task VerifyAsync(string consumerBody, string assemblyName, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NnAppSettingsServerOnlyReadAnalyzer, DefaultVerifier>
		{
			TestCode = SettingsDecl + consumerBody,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		// Ignore compiler diagnostics (nullable-context, unused-value) — only analyzer diagnostics are tested.
		test.CompilerDiagnostics = CompilerDiagnostics.None;
		test.SolutionTransforms.Add(RenameAssembly(assemblyName));

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Expect(int markupSpan, string keyPath, string assemblyName)
	{
		return new DiagnosticResult(NnAppSettingsServerOnlyReadAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(markupSpan)
			.WithArguments(keyPath, assemblyName);
	}

	/// <summary>
	/// Renames the primary test project so the analyzer's assembly-suffix reachability gate sees a
	/// <c>.Shared</c> / <c>.Server</c> identity instead of the default <c>TestProject</c>.
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

	#region Positive — fires at the read

	[Fact]
	public async Task ServerOnlyRead_InSharedAssembly_FiresAtRead()
	{
		// ServerKey is absent from _ClientAppSettings (ServerOnly) and read from a *.Shared assembly → fires.
		string consumer = @"
public class Consumer
{
    private App.AppSettingsGen.AppSettings s = new App.AppSettingsGen.AppSettings();
    public void M()
    {
        var v = s.{|#0:ServerKey|};
        _ = v;
    }
}";
		await VerifyAsync(consumer, SharedAssemblyName, Expect(0, "ServerKey", SharedAssemblyName));
	}

	#endregion

	#region Negative — silent (zero false positives)

	[Fact]
	public async Task ClientWhitelistedRead_InSharedAssembly_NoDiagnostic()
	{
		// ClientKey is present in _ClientAppSettings (whitelisted) → safe even in a *.Shared assembly.
		string consumer = @"
public class Consumer
{
    private App.AppSettingsGen.AppSettings s = new App.AppSettingsGen.AppSettings();
    public void M()
    {
        var v = s.ClientKey;
        _ = v;
    }
}";
		await VerifyAsync(consumer, SharedAssemblyName);
	}

	[Fact]
	public async Task ServerOnlyRead_InServerAssembly_NoDiagnostic()
	{
		// Same ServerOnly read, but the consuming assembly is *.Server → exempt (the gate requires
		// .Shared/.Client). ServerOnly reads are legal server-side.
		string consumer = @"
public class Consumer
{
    private App.AppSettingsGen.AppSettings s = new App.AppSettingsGen.AppSettings();
    public void M()
    {
        var v = s.ServerKey;
        _ = v;
    }
}";
		await VerifyAsync(consumer, ServerAssemblyName);
	}

	[Fact]
	public async Task NonAppSettingsRead_InSharedAssembly_NoDiagnostic()
	{
		// Ordinary (non-NotNot.AppSettings) member read → never flagged, even in a *.Shared assembly.
		string consumer = @"
public class Consumer
{
    private NotSettings o = new NotSettings();
    public void M()
    {
        var v = o.Prop;
        _ = v;
    }
}";
		await VerifyAsync(consumer, SharedAssemblyName);
	}

	#endregion
}
