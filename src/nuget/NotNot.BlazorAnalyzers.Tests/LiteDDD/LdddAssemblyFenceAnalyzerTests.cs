using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.LiteDDD;

namespace NotNot.BlazorAnalyzers.Tests.LiteDDD;

/// <summary>
/// Tests for <see cref="LdddAssemblyFenceAnalyzer"/> (NN_LDDD_001 / NN_LDDD_002 / NN_LDDD_004).
/// Verifies the three Wave-1 assembly-fence rules across positive, negative, bypass-attribute,
/// and path-exemption pathways. Mirrors the
/// <see cref="NnDesign.NnDesignMudBlazorPolicyAnalyzerTests"/> pattern but adds two critical
/// extensions:
/// <list type="number">
///   <item><description>
///     <b>Assembly-name override</b> via
///     <see cref="CSharpAnalyzerTest{TAnalyzer,TVerifier}.SolutionTransforms"/> — the analyzer's
///     <c>.Shared</c> / <c>.Client</c> / <c>.Server</c> suffix gates require renaming the test
///     compilation away from the default <c>TestProject</c>.
///   </description></item>
///   <item><description>
///     <b>Sibling .Server project</b> for NN_LDDD_001 — true cross-assembly references require
///     a second project in the test solution, also wired via <c>SolutionTransforms</c>.
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Source-addition pattern</b> (avoid double-compile errors): Microsoft.CodeAnalysis.Testing
/// implicitly registers <see cref="AnalyzerTest{TVerifier}.TestCode"/> as
/// <c>/0/Test0.cs</c>. Tests that need a SPECIFIC path (e.g. path-exemption
/// <c>Pages/Samples/**</c>, or to anchor line numbers in field-declarator spans) must leave
/// <c>TestCode</c> as a trivial placeholder and add the real source via
/// <see cref="SolutionState.Sources"/> with the target path. Setting BOTH the same content
/// causes CS0101/CS0111/CS0579 duplicate-type/member/attribute errors AND duplicate
/// diagnostics.
/// </para>
/// </remarks>
public class LdddAssemblyFenceAnalyzerTests
{
	// ── Test infrastructure ─────────────────────────────────────────────────────

	/// <summary>
	/// Default Shared-assembly name used by tests where the consumer must be classified Shared
	/// (positive/bypass/exemption tests for NN_LDDD_001/002, and NN_LDDD_004 positive tests).
	/// </summary>
	private const string SharedAssemblyName = "TestProject.Shared";

	/// <summary>
	/// Server-assembly name used by NN_LDDD_004 negative — fields/properties typed as DbContext
	/// inside a <c>.Server</c> assembly must NOT fire the diagnostic. Also the name of the
	/// sibling cross-assembly project for NN_LDDD_001 positive.
	/// </summary>
	private const string ServerAssemblyName = "TestProject.Server";

	/// <summary>
	/// Trivial placeholder used as <see cref="AnalyzerTest{TVerifier}.TestCode"/> when the real
	/// source under test is added via <see cref="SolutionState.Sources"/>.
	/// </summary>
	private const string Placeholder = "class Placeholder { }";

	/// <summary>
	/// Stub for <see cref="Microsoft.EntityFrameworkCore.DbContext"/>. Added to
	/// <c>TestState.Sources</c> for NN_LDDD_004 tests so the analyzer's full-name match
	/// (<c>Microsoft.EntityFrameworkCore.DbContext</c>) succeeds at semantic resolution.
	/// </summary>
	private const string DbContextStub = @"
namespace Microsoft.EntityFrameworkCore
{
    public abstract class DbContext { }
}
";

	/// <summary>
	/// Stub for the canonical bypass attribute. Matched by EITHER simple name
	/// (<c>LdddBypassAttribute</c>) OR fully-qualified name
	/// (<c>NotNot.Bcl.Diagnostics.LdddBypassAttribute</c>) per
	/// <see cref="LdddAnalyzerHelpers.HasLdddBypassAttribute(IAssemblySymbol)"/>.
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
	/// Source for a sample server-side type referenced by NN_LDDD_001 positive tests. Declared
	/// in the <c>Novaleaf.VibeOverwatch.Server.Foo</c> namespace so the analyzer recognizes the
	/// reference as server-side via the SECOND project's <c>.Server</c> assembly suffix.
	/// </summary>
	private const string ServerNamespaceTypeStub = @"
namespace Novaleaf.VibeOverwatch.Server.Foo
{
    public class SomeType { }
}
";

	/// <summary>
	/// Source for a Contracts-namespaced type referenced by NN_LDDD_001 negative tests. Declared
	/// in a sibling project named <c>TestProject.Contracts</c> (no <c>.Server</c> suffix) so
	/// neither the namespace nor the containing-assembly check matches the server pattern.
	/// </summary>
	private const string ContractsNamespaceTypeStub = @"
namespace Novaleaf.VibeOverwatch.Features.Contracts
{
    public class SomeContract { }
}
";

	/// <summary>
	/// Returns a solution-transform delegate that renames the primary project to
	/// <paramref name="assemblyName"/>. Use this when no cross-assembly reference is needed.
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

	/// <summary>
	/// Returns a solution-transform delegate that:
	/// <list type="number">
	///   <item><description>Renames the primary project to <paramref name="primaryAssemblyName"/>.</description></item>
	///   <item><description>Adds a sibling project named <paramref name="siblingAssemblyName"/> with
	///     <paramref name="siblingSource"/> as a document.</description></item>
	///   <item><description>Wires a <see cref="ProjectReference"/> from primary → sibling so the
	///     primary compilation can <c>using</c>/reference types declared in the sibling.</description></item>
	/// </list>
	/// </summary>
	private static Func<Solution, ProjectId, Solution> AddSiblingProject(
		string primaryAssemblyName,
		string siblingAssemblyName,
		string siblingSource,
		string siblingFileName)
	{
		return (solution, projectId) =>
		{
			// Rename primary.
			solution = solution.WithProjectAssemblyName(projectId, primaryAssemblyName);

			// Create sibling project.
			var siblingProjectId = ProjectId.CreateNewId();
			solution = solution.AddProject(
				siblingProjectId,
				siblingAssemblyName,
				siblingAssemblyName,
				LanguageNames.CSharp);

			// Mirror metadata references + compilation options from primary so sibling compiles.
			var primaryProject = solution.GetProject(projectId)!;
			foreach (var metaRef in primaryProject.MetadataReferences)
			{
				solution = solution.AddMetadataReference(siblingProjectId, metaRef);
			}
			solution = solution.WithProjectCompilationOptions(
				siblingProjectId,
				primaryProject.CompilationOptions!);

			// Add sibling source.
			var docId = DocumentId.CreateNewId(siblingProjectId);
			solution = solution.AddDocument(docId, siblingFileName, siblingSource);

			// Wire primary → sibling reference.
			return solution.AddProjectReference(projectId, new ProjectReference(siblingProjectId));
		};
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// NN_LDDD_001 — Server-assembly type referenced in Shared/Client code
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE — Shared assembly references a type defined in a sibling <c>.Server</c> assembly.
	/// The analyzer's semantic pathway resolves the IdentifierName to the cross-assembly type,
	/// walks to <c>ContainingAssembly.Identity.Name</c>, recognizes the <c>.Server</c> suffix,
	/// and fires NN_LDDD_001.
	/// </summary>
	/// <remarks>
	/// Uses fully-qualified type access (no <c>using</c>) so NN_LDDD_002 does NOT also fire on
	/// the using-directive. The single expected diagnostic anchors on the outermost
	/// IdentifierName of the qualified-name chain.
	/// </remarks>
	[Fact]
	public async Task NN_LDDD_001_FiresOnServerAssemblyTypeReference()
	{
		// Use fully-qualified access (no using directive) so ONLY NN_LDDD_001 fires.
		// The analyzer's IsLikelyTypeOrNamespaceReference reports on the OUTERMOST identifier
		// in the qualified-name chain — for `Novaleaf.VibeOverwatch.Server.Foo.SomeType`, that
		// is the LEFT-most segment `Novaleaf` (line 5).
		var consumerSource = @"namespace TestProject.Shared
{
    public class ConsumerClass
    {
        public Novaleaf.VibeOverwatch.Server.Foo.SomeType DoStuff() => null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};

		test.SolutionTransforms.Add(AddSiblingProject(
			primaryAssemblyName: SharedAssemblyName,
			siblingAssemblyName: ServerAssemblyName,
			siblingSource: ServerNamespaceTypeStub,
			siblingFileName: "SomeType.cs"));

		// Outermost IdentifierName `Novaleaf` — line 5, after "        public " (16 leading chars).
		// `Novaleaf` length 8 → col 16 → col 24.
		//
		// The first arg is the rendered display name of the resolved symbol — for the LEFTMOST
		// identifier in `Novaleaf.VibeOverwatch.Server.Foo.SomeType`, symbol resolution returns
		// the NAMESPACE symbol `Novaleaf` (not the type at the end of the chain). The analyzer
		// reports `symbol.ToDisplayString()` (= `"Novaleaf"`) because the namespace symbol does
		// not flow through the IFieldSymbol/IPropertySymbol/IMethodSymbol switch path that
		// extracts the containing type.
		//
		// This is BY DESIGN of the analyzer — namespace symbols whose containing assembly is
		// `.Server` produce a NN_LDDD_001 diagnostic, since the namespace itself is the leak
		// surface (subsequent `.VibeOverwatch.Server.Foo.SomeType` resolution all happens
		// through that namespace). The diagnostic still correctly identifies the cross-assembly
		// boundary via the containing-assembly arg.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddAssemblyFenceAnalyzer.LDDD001_Rule)
				.WithSpan("/0/Test0.cs", 5, 16, 5, 24)
				.WithArguments(
					"Novaleaf",
					ServerAssemblyName,
					"Shared",
					SharedAssemblyName));

		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — Shared assembly references a type defined in a sibling
	/// <c>TestProject.Contracts</c> assembly (NO <c>.Server</c> suffix). Both the namespace
	/// pattern check and the containing-assembly suffix check fail → no diagnostic.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_001_DoesNotFireOnNonServerNamespace()
	{
		var consumerSource = @"namespace TestProject.Shared
{
    public class ConsumerClass
    {
        public Novaleaf.VibeOverwatch.Features.Contracts.SomeContract DoStuff() => null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};

		test.SolutionTransforms.Add(AddSiblingProject(
			primaryAssemblyName: SharedAssemblyName,
			siblingAssemblyName: "TestProject.Contracts",
			siblingSource: ContractsNamespaceTypeStub,
			siblingFileName: "SomeContract.cs"));

		// Expect ZERO diagnostics — neither namespace nor assembly match the .Server pattern.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS — same violation as the positive test, but the consuming assembly carries
	/// <c>[assembly: LdddBypass]</c>. The compilation-level short-circuit in
	/// <see cref="LdddAssemblyFenceAnalyzer.ShouldAnalyzeCompilation"/> suppresses all rules.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_001_DoesNotFireWhenAssemblyHasLdddBypass()
	{
		var consumerSource = @"[assembly: NotNot.Bcl.Diagnostics.LdddBypass]
namespace TestProject.Shared
{
    public class ConsumerClass
    {
        public Novaleaf.VibeOverwatch.Server.Foo.SomeType DoStuff() => null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};

		// Add the bypass attribute declaration as a separate source so the consumer-source's
		// `[assembly: ...]` reference resolves.
		test.TestState.Sources.Add(BypassAttributeStub);

		test.SolutionTransforms.Add(AddSiblingProject(
			primaryAssemblyName: SharedAssemblyName,
			siblingAssemblyName: ServerAssemblyName,
			siblingSource: ServerNamespaceTypeStub,
			siblingFileName: "SomeType.cs"));

		// Expect ZERO diagnostics — assembly-level [LdddBypass] short-circuits all rules.
		await test.RunAsync();
	}

	/// <summary>
	/// PATH-EXEMPTION — the violation lives in a file whose path contains
	/// <c>/Pages/Samples/</c>, which <see cref="LdddAnalyzerHelpers.IsExceptedPath"/> exempts
	/// per the AGENTS.md pedagogical-samples carve-out. No diagnostic fires.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_001_DoesNotFireInPagesSamplesPath()
	{
		var consumerSource = @"namespace TestProject.Shared
{
    public class FooSample
    {
        public Novaleaf.VibeOverwatch.Server.Foo.SomeType DoStuff() => null!;
    }
}
";
		// Path-based exemption requires the source to be at a SPECIFIC path. Use placeholder
		// for TestCode and add the real source via TestState.Sources with the target path.
		const string consumerPath = "/TestProject.Shared/Pages/Samples/Foo.razor.cs";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.Sources.Add((consumerPath, consumerSource));

		test.SolutionTransforms.Add(AddSiblingProject(
			primaryAssemblyName: SharedAssemblyName,
			siblingAssemblyName: ServerAssemblyName,
			siblingSource: ServerNamespaceTypeStub,
			siblingFileName: "SomeType.cs"));

		// Expect ZERO diagnostics — Pages/Samples/** is exempt per IsExceptedPath.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// NN_LDDD_002 — Server-namespace using directive in Shared/Client code
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE (Razor) — <c>.razor</c> AdditionalFile contains
	/// <c>@using Novaleaf.VibeOverwatch.Server.Features.X</c>. The text-scan pathway over
	/// AdditionalFiles fires NN_LDDD_002 anchored at the matched line.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_002_FiresOnRazorAtUsingServerNamespace()
	{
		var razorContent = "@using Novaleaf.VibeOverwatch.Server.Features.X";
		const string razorPath = "/TestProject.Shared/Components/Foo.razor";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// ServerUsingRegex matches the whole directive: line 1, cols 1 → 1+length.
		// `@using Novaleaf.VibeOverwatch.Server.Features.X` = 47 chars → end col 48.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddAssemblyFenceAnalyzer.LDDD002_Rule)
				.WithSpan(razorPath, 1, 1, 1, 48)
				.WithArguments(
					"using Novaleaf.VibeOverwatch.Server.Features.X",
					"Shared",
					SharedAssemblyName));

		await test.RunAsync();
	}

	/// <summary>
	/// POSITIVE (plain .cs) — plain <c>.cs</c> source in a <c>.Shared</c> assembly contains
	/// <c>using Novaleaf.VibeOverwatch.Server.Features.X;</c>. The semantic pathway
	/// (<c>AnalyzeUsingDirective</c>) fires NN_LDDD_002 with fallback text-pattern matching
	/// since the namespace doesn't actually exist in the compilation.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_002_FiresOnPlainCsUsingServerNamespace()
	{
		var consumerSource = @"using Novaleaf.VibeOverwatch.Server.Features.X;
namespace TestProject.Shared
{
    public class ConsumerClass { }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// The semantic pathway reports at the full UsingDirective span. The framework names the
		// TestCode file `/0/Test0.cs`.
		// Line 1: `using Novaleaf.VibeOverwatch.Server.Features.X;` = 47 chars → col 1 → col 48.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddAssemblyFenceAnalyzer.LDDD002_Rule)
				.WithSpan("/0/Test0.cs", 1, 1, 1, 48)
				.WithArguments(
					"using Novaleaf.VibeOverwatch.Server.Features.X",
					"Shared",
					SharedAssemblyName));

		// Suppress compiler diagnostics for the unresolved namespace — the analyzer's fallback
		// text-pattern still recognizes the using as server-side.
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — <c>.razor</c> AdditionalFile contains <c>@using NotNot.Bcl.Core.X</c>. The
	/// namespace has no <c>.Server.</c> segment and does not end in <c>.Server</c>, so the
	/// regex never matches → no diagnostic.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_002_DoesNotFireOnNonServerNamespace()
	{
		var razorContent = "@using NotNot.Bcl.Core.X";
		const string razorPath = "/TestProject.Shared/Components/Foo.razor";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS — same Razor violation as the positive Razor test, but the consuming assembly
	/// carries <c>[assembly: LdddBypass]</c>. The compilation-level short-circuit suppresses
	/// the rule.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_002_DoesNotFireWhenAssemblyHasLdddBypass()
	{
		var razorContent = "@using Novaleaf.VibeOverwatch.Server.Features.X";
		const string razorPath = "/TestProject.Shared/Components/Foo.razor";

		// Bypass attribute applied via a .cs source file in the same compilation. TestCode is
		// the consumer source containing `[assembly: ...]`; bypass-attribute declaration is a
		// separate source so the attribute reference resolves.
		var consumerSource = @"[assembly: NotNot.Bcl.Diagnostics.LdddBypass]
namespace TestProject.Shared { public class Marker { } }
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(BypassAttributeStub);
		test.TestState.AdditionalFiles.Add((razorPath, razorContent));
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// Expect ZERO diagnostics — [assembly: LdddBypass] short-circuits all rules.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// NN_LDDD_004 — Entity Framework DbContext referenced in Shared/Client code
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// POSITIVE (field) — <c>.Shared</c>-assembly class declares
	/// <c>private VowDbContext _db;</c> where <c>VowDbContext : DbContext</c>. The
	/// <c>AnalyzeFieldOrProperty</c> symbol-action resolves the field's type, walks to
	/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> via
	/// <see cref="LdddAnalyzerHelpers.IsEntityFrameworkDbContext"/>, and fires NN_LDDD_004.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_004_FiresOnDbContextFieldInSharedAssembly()
	{
		var consumerSource = @"using Microsoft.EntityFrameworkCore;
namespace TestProject.Shared
{
    public class VowDbContext : DbContext { }
    public class ConsumerClass
    {
        private VowDbContext _db = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(DbContextStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// The analyzer reports at the field-declarator's syntax location:
		// Line 7: `        private VowDbContext _db = null!;`
		// DeclaringSyntaxReferences returns the VariableDeclarator → `_db = null!`.
		// Span: line 7, col 30 → col 41 (per analyzer's actual output observed in prior run).
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddAssemblyFenceAnalyzer.LDDD004_Rule)
				.WithSpan("/0/Test0.cs", 7, 30, 7, 41)
				.WithArguments(
					"Field _db",
					"TestProject.Shared.VowDbContext",
					"Shared",
					SharedAssemblyName));

		// The unused-private-field analyzer fires CS0169 on `_db`. Allow it.
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		await test.RunAsync();
	}

	/// <summary>
	/// POSITIVE (property) — same as the field test, but with a property declaration. The
	/// <c>AnalyzeFieldOrProperty</c> action handles both kinds uniformly.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_004_FiresOnDbContextPropertyInSharedAssembly()
	{
		var consumerSource = @"using Microsoft.EntityFrameworkCore;
namespace TestProject.Shared
{
    public class VowDbContext : DbContext { }
    public class ConsumerClass
    {
        public VowDbContext Db { get; set; } = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(DbContextStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));

		// DeclaringSyntaxReferences for a property returns the full PropertyDeclarationSyntax.
		// Line 7: `        public VowDbContext Db { get; set; } = null!;`
		// Property declaration spans col 9 ("public" start) → col 54 (end of `= null!;`).
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(LdddAssemblyFenceAnalyzer.LDDD004_Rule)
				.WithSpan("/0/Test0.cs", 7, 9, 7, 54)
				.WithArguments(
					"Property Db",
					"TestProject.Shared.VowDbContext",
					"Shared",
					SharedAssemblyName));

		await test.RunAsync();
	}

	/// <summary>
	/// NEGATIVE — same field declaration as the positive test, but the consuming assembly is
	/// named <c>TestProject.Server</c>. The
	/// <see cref="LdddAssemblyFenceAnalyzer.ShouldAnalyzeCompilation"/> gate requires
	/// <c>.Shared</c> or <c>.Client</c> suffix, so Server assemblies bypass all rules
	/// (DbContext usage is expected and legal on the server side).
	/// </summary>
	[Fact]
	public async Task NN_LDDD_004_DoesNotFireOnDbContextFieldInServerAssembly()
	{
		var consumerSource = @"using Microsoft.EntityFrameworkCore;
namespace TestProject.Server
{
    public class VowDbContext : DbContext { }
    public class ConsumerClass
    {
        private VowDbContext _db = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(DbContextStub);
		test.SolutionTransforms.Add(RenameAssembly(ServerAssemblyName));
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		// Expect ZERO diagnostics — Server-assembly consumer is not gated.
		await test.RunAsync();
	}

	/// <summary>
	/// BYPASS — same positive violation, but the assembly carries
	/// <c>[assembly: LdddBypass]</c>. Short-circuits the rule.
	/// </summary>
	[Fact]
	public async Task NN_LDDD_004_DoesNotFireWhenAssemblyHasLdddBypass()
	{
		var consumerSource = @"using Microsoft.EntityFrameworkCore;
[assembly: NotNot.Bcl.Diagnostics.LdddBypass]
namespace TestProject.Shared
{
    public class VowDbContext : DbContext { }
    public class ConsumerClass
    {
        private VowDbContext _db = null!;
    }
}
";

		var test = new CSharpAnalyzerTest<LdddAssemblyFenceAnalyzer, DefaultVerifier>
		{
			TestCode = consumerSource,
		};
		test.TestState.Sources.Add(DbContextStub);
		test.TestState.Sources.Add(BypassAttributeStub);
		test.SolutionTransforms.Add(RenameAssembly(SharedAssemblyName));
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		// Expect ZERO diagnostics — [assembly: LdddBypass] short-circuits.
		await test.RunAsync();
	}
}
