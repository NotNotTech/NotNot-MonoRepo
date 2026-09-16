using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for <see cref="VacuousTestAssertionAnalyzer"/> (NN_C006).
/// </summary>
/// <remarks>
/// <para>
/// ORGANISING KEY: the suite is keyed by DISTINGUISHING PREDICATE LINE, not by case. The criterion is
/// mutation detection — deleting any one distinguishing line must fail at least one test here — so a case
/// that merely "looks covered" while one of its arms stays unpinned is the defect this organisation
/// prevents. Each fixture's xmldoc names the line it kills (L1..L19, matching the Plan's line→fixture
/// matrix).
/// </para>
/// <para>
/// STRUCTURE, and it is load-bearing: <see cref="Stub"/> and the fixture are added as TWO SEPARATE
/// compilation sources, never concatenated into one. Concatenation puts the fixture's own <c>using</c>
/// directives AFTER the stub's namespace declaration — CS1529, which <c>CompilerDiagnostics.None</c>
/// silently hides — leaving those imports dead and degrading a positive fixture into a false negative that
/// reads exactly like an analyzer bug. That already happened once during NN_R011 development. As separate
/// sources each file is well-formed and owns its imports. Do NOT "simplify" this into one
/// <c>TestCode = Stub + fixture</c> string, and do NOT route it through
/// <c>AnalyzerTestHelper.WrapInClass</c>, which concatenates.
/// </para>
/// <para>
/// <see cref="StubAndFixtureCompileCleanly_AsTwoSources"/> is the mutation detector for that hazard: it
/// re-runs one pair with compiler ERRORS verified, so a malformed stub or a dead import fails loudly
/// instead of silently making every fixture pass for the wrong reason.
/// </para>
/// <para>
/// WHY CASE-C RECEIVERS COME FROM A REFERENCE ASSEMBLY: Case C's third condition compares the
/// <c>typeof(T)</c> operand's containing assembly against the compilation's own assembly. A stubbed
/// "foreign" type lives IN the fixture compilation, so it would make C1/C2 silently not fire — the same
/// false-negative class as the CS1529 trap. Positives therefore use <c>System.Text.StringBuilder</c> from
/// the Net80 reference assemblies, and <see cref="OwnAssemblyReflection_IsSilent"/>'s own-assembly type is
/// declared in the fixture source. The rule never verifies the named member EXISTS, so no real non-public
/// member name is needed.
/// </para>
/// </remarks>
public class VacuousTestAssertionAnalyzerTests
{
	/// <summary>
	/// Test-source-local stub, compiled as its own source file alongside every fixture. Three groups:
	/// (1) test attributes, so the scope gate can fire — the gate matches an attribute's SIMPLE NAME suffix
	/// with no namespace requirement, which is what lets one rule serve xunit/NUnit/MSTest, so a local stub
	/// is semantically identical for matching purposes; (2) an <c>Assert</c> surface, because
	/// <c>Xunit.Assert</c> is not in the Net80 reference assemblies and Cases A/B gate on a containing type
	/// whose simple name is <c>Assert</c>; (3) a <c>NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute</c> stub,
	/// so remediation tier 4's member-scope promise is testable.
	/// <para>
	/// A REDUCED <c>Assert</c> surface is itself a false-negative mechanism: a shape the real library exposes
	/// but the stub does not cannot be exercised at all. That is how the whole unconditional-marker class
	/// (<c>Fail</c>/<c>Skip</c>) stayed invisible until review, and how the generic <c>Equal&lt;T&gt;</c>
	/// overload hid a gate defect. When adding a fixture, check the surface exists here first.
	/// </para>
	/// </summary>
	private const string Stub = @"
using System;
using System.Collections.Generic;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class FactAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TheoryAttribute : Attribute { }

    public static class Assert
    {
        public static void Equal(object expected, object actual) { }
        public static void Equal<T>(T expected, T actual) { }
        public static void NotEqual(object expected, object actual) { }
        public static void StrictEqual(object expected, object actual) { }
        public static void Same(object expected, object actual) { }
        public static void True(bool condition) { }
        public static void True(bool condition, string userMessage) { }
        public static void False(bool condition) { }
        public static void False(bool condition, string userMessage) { }
        public static void NotNull(object value) { }
        public static void All<T>(IEnumerable<T> collection, Action<T> action) { }
        public static void Contains<T>(T expected, IEnumerable<T> collection) { }
        public static void Fail(string message) { }
        public static void Skip(string reason) { }
    }
}

namespace NotNot.Bcl.Diagnostics
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    public sealed class CodeStyleBypassAttribute : Attribute { }
}
";

	/// <summary>
	/// Two-source composition. <c>CompilerDiagnostics.None</c> keeps nullable/unused compiler noise out of
	/// the analyzer-only assertion; <see cref="StubAndFixtureCompileCleanly_AsTwoSources"/> is what keeps
	/// that setting from hiding a malformed fixture.
	/// </summary>
	private static async Task VerifyAsync(string fixture, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<VacuousTestAssertionAnalyzer, DefaultVerifier>
		{
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		test.TestState.Sources.Add(Stub);
		test.TestState.Sources.Add(fixture);
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Expect(string caseName, string symbolName)
		=> new DiagnosticResult(VacuousTestAssertionAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
			.WithLocation(0)
			.WithArguments(caseName, symbolName);

	private const string ConstantTautology = "constant-tautology";
	private const string EnumShapePin = "enum-shape-pin";
	private const string ForeignNonPublicReflection = "foreign-nonpublic-reflection";

	// ── HARNESS GUARD (P4 mutation detector) ─────────────────────────────────

	/// <summary>
	/// Re-runs one stub+fixture pair with compiler ERRORS verified. <c>CompilerDiagnostics.None</c> is
	/// required by every other test here and is exactly what hid CS1529 during NN_R011 development, so
	/// without this test a malformed stub or a dead <c>using</c> would degrade every positive fixture into a
	/// silent false negative rather than failing.
	/// </summary>
	[Fact]
	public async Task StubAndFixtureCompileCleanly_AsTwoSources()
	{
		var test = new CSharpAnalyzerTest<VacuousTestAssertionAnalyzer, DefaultVerifier>
		{
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		test.TestState.Sources.Add(Stub);
		test.TestState.Sources.Add(@"
using Xunit;

public class Foo { }
public class Bar { }

public class GuardTests
{
    [Fact]
    public void TypesDiffer()
    {
        {|#0:Assert.NotEqual(typeof(Foo), typeof(Bar))|};
    }
}
");
		// Compiler ERRORS are verified (not merely ignored): CS1529 and any unbound name fail here.
		test.CompilerDiagnostics = CompilerDiagnostics.Errors;
		test.ExpectedDiagnostics.Add(Expect(ConstantTautology, "TypesDiffer"));

		await test.RunAsync();
	}

	// ── POSITIVE: Case A — constant-tautology ────────────────────────────────

	/// <summary>A1 — pins L6 (the <c>typeof(...)</c> arm). Two type identities compared: the outcome is
	/// fixed by declaration, and no code under test runs.</summary>
	[Fact]
	public async Task TypeofOperands_InTestClass_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }
public class Bar { }

public class ShapeTests
{
    [Fact]
    public void TypesDiffer()
    {
        {|#0:Assert.NotEqual(typeof(Foo), typeof(Bar))|};
    }
}
", Expect(ConstantTautology, "TypesDiffer"));
	}

	/// <summary>A2 — pins L8 (the declaration-metadata member arm: a member on <c>System.Type</c> /
	/// <c>MemberInfo</c> whose receiver is itself behavior-independent).</summary>
	[Fact]
	public async Task TypeofDeclarationMetadata_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }

public class ShapeTests
{
    [Fact]
    public void FooIsPublic()
    {
        {|#0:Assert.True(typeof(Foo).IsPublic)|};
    }
}
", Expect(ConstantTautology, "FooIsPublic"));
	}

	/// <summary>
	/// A3 — pins L5 (the <c>GetConstantValue</c> arm). This is the shape of
	/// <c>PtyAbiContractTests.DenialReasons_MirrorTheContractsAuthorityExactly</c>: an all-constant
	/// cross-assembly ABI mirror. It FIRES by design and is a KEEP under the message's tier 1 — the finding
	/// is a candidate, and that file's xmldoc names the consumer it protects.
	/// </summary>
	[Fact]
	public async Task AllConstantOperands_AbiMirrorShape_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public enum WireStatus { PoolExhausted = 3 }
public enum DenialReason { PoolExhausted = 3 }

public class AbiTests
{
    [Fact]
    public void DenialReasonsMirror()
    {
        {|#0:Assert.Equal(DenialReason.PoolExhausted, (int)WireStatus.PoolExhausted)|};
    }
}
", Expect(ConstantTautology, "DenialReasonsMirror"));
	}

	/// <summary>A4 — pins L7 (the <c>nameof(...)</c> arm). Deliberate divergence from NN_C005, which
	/// EXEMPTS <c>nameof</c>: the purposes differ (null-dereference reachability versus behavioral
	/// vacuity), so treating it as behavior-INDEPENDENT here is not a family inconsistency.</summary>
	[Fact]
	public async Task NameofOperand_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }

public class ShapeTests
{
    [Fact]
    public void NameIsStable()
    {
        {|#0:Assert.Equal(""Foo"", nameof(Foo))|};
    }
}
", Expect(ConstantTautology, "NameIsStable"));
	}

	/// <summary>
	/// A5 — pins L13 for the <c>Assert</c> gate. <c>using static Xunit.Assert;</c> turns the call into an
	/// <c>IdentifierNameSyntax</c>; because the gate reads the RESOLVED SYMBOL's containing type, mere
	/// qualification is structurally incapable of deciding whether the rule fires. This is the confirmed
	/// NN_R011 metamorphic hole, pinned here before it can reappear.
	/// </summary>
	[Fact]
	public async Task StaticallyImportedAssert_Fires()
	{
		await VerifyAsync(@"
using static Xunit.Assert;
using Xunit;

public class Foo { }
public class Bar { }

public class ShapeTests
{
    [Fact]
    public void TypesDiffer()
    {
        {|#0:Equal(typeof(Foo), typeof(Bar))|};
    }
}
", Expect(ConstantTautology, "TypesDiffer"));
	}

	// ── POSITIVE: Case B — enum-shape-pin ────────────────────────────────────

	/// <summary>B1 — pins L11 (an argument tree containing a <c>GetNames</c>/<c>GetValues</c>
	/// invocation).</summary>
	[Fact]
	public async Task EnumGetNamesPin_Fires()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public enum Shade { A, B }

public class ShapeTests
{
    [Fact]
    public void NamesArePinned()
    {
        {|#0:Assert.Equal(new[] { ""A"", ""B"" }, Enum.GetNames(typeof(Shade)))|};
    }
}
", Expect(EnumShapePin, "NamesArePinned"));
	}

	/// <summary>
	/// B2 — pins L11 through the generic overload nested inside a member access. This is the shape of
	/// <c>PtyAbiContractTests.Statuses_PreserveExistingNumbersAndAppendEffectsPending</c>; it fires as a
	/// candidate, and the message routes such a reader to a consumer-citation KEEP rather than a deletion.
	/// Also proves the Case A → Case B dispatch order: <c>Enum.GetValues&lt;E&gt;().Length</c> has an
	/// invocation receiver, so Case A cannot match it and cannot produce a second diagnostic here.
	/// </summary>
	[Fact]
	public async Task EnumGetValuesLengthPin_Fires()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public enum Shade { A, B, C }

public class ShapeTests
{
    [Fact]
    public void CountIsPinned()
    {
        {|#0:Assert.Equal(3, Enum.GetValues<Shade>().Length)|};
    }
}
", Expect(EnumShapePin, "CountIsPinned"));
	}

	/// <summary>B3 — pins L13 for Case B's inner scan: <c>using static System.Enum;</c> makes
	/// <c>GetNames(...)</c> an <c>IdentifierNameSyntax</c>, and the scan resolves every descendant
	/// invocation's SYMBOL rather than requiring member-access syntax.</summary>
	[Fact]
	public async Task StaticallyImportedEnumGetNames_Fires()
	{
		await VerifyAsync(@"
using static System.Enum;
using Xunit;

public enum Shade { A, B }

public class ShapeTests
{
    [Fact]
    public void NamesArePinned()
    {
        {|#0:Assert.Equal(new[] { ""A"", ""B"" }, GetNames(typeof(Shade)))|};
    }
}
", Expect(EnumShapePin, "NamesArePinned"));
	}

	// ── POSITIVE: Case C — foreign-nonpublic-reflection ──────────────────────

	/// <summary>
	/// C1 — pins L2 (scope-gate BREADTH) as well as Case C's own conditions: the reflection lives in a
	/// NON-attributed <c>private static</c> helper, so a gate restricted to attributed methods would miss
	/// it. Reported at the reflection invocation, which is inside no assertion at all.
	/// </summary>
	[Fact]
	public async Task ForeignNonPublicReflection_InPrivateHelper_Fires()
	{
		await VerifyAsync(@"
using System.Reflection;
using System.Text;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesHiddenMember()
    {
        Assert.NotNull(InvokeShouldRender());
    }

    private static MethodInfo InvokeShouldRender()
        => {|#0:typeof(StringBuilder).GetMethod(""Hidden"", BindingFlags.Instance | BindingFlags.NonPublic)|};
}
", Expect(ForeignNonPublicReflection, "InvokeShouldRender"));
	}

	/// <summary>
	/// C2 — pins L14. Reordered named arguments bind the same overload, but <c>arguments[0]</c> is then the
	/// FLAGS, not the name. Resolution goes through
	/// <c>AnalyzerArgumentBinding.FindArgumentForParameter</c>, never syntax position; reading by position
	/// was a confirmed NN_R011 defect.
	/// </summary>
	[Fact]
	public async Task ReorderedNamedArguments_Fires()
	{
		await VerifyAsync(@"
using System.Reflection;
using System.Text;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesHiddenMember()
    {
        Assert.NotNull({|#0:typeof(StringBuilder).GetMethod(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic, name: ""Hidden"")|});
    }
}
", Expect(ForeignNonPublicReflection, "ProbesHiddenMember"));
	}

	// ── NEGATIVE ─────────────────────────────────────────────────────────────

	/// <summary>N1 — pins L4 (Case A's all-quantifier over arguments): one behavior-dependent operand is
	/// enough to make the assertion meaningful.</summary>
	[Fact]
	public async Task BehaviorDependentOperand_IsSilent()
	{
		await VerifyAsync(@"
using System.Collections.Generic;
using Xunit;

public class ShapeTests
{
    [Fact]
    public void CountIsSeven()
    {
        var list = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
        Assert.Equal(7, list.Count);
    }
}
");
	}

	/// <summary>N2 — pins L4 from the other side: the operand is a call into the code under test.</summary>
	[Fact]
	public async Task MethodUnderTestOperand_IsSilent()
	{
		await VerifyAsync(@"
using Xunit;

public static class Subject
{
    public static int Compute(int input) => input * 2;
}

public class ShapeTests
{
    [Fact]
    public void ComputeDoubles()
    {
        var expected = 4;
        Assert.Equal(expected, Subject.Compute(2));
    }
}
");
	}

	/// <summary>
	/// N3 — pins L10 (Case B's assertion-method allowlist). <c>Assert.All</c> uses enum reflection as a
	/// per-member ENUMERATION SOURCE for a behavioral assertion rather than pinning shape, so flagging it
	/// would condemn the correct pattern the rule steers toward.
	/// </summary>
	[Fact]
	public async Task EnumReflectionAsEnumerationSource_IsSilent()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public enum Shade { A, B }

public class ShapeTests
{
    private static string Map(Shade shade) => shade.ToString();

    [Fact]
    public void EveryShadeMaps()
    {
        Assert.All(Enum.GetValues<Shade>(), v => Assert.NotNull(Map(v)));
    }
}
");
	}

	/// <summary>N4 — the composite negative observed beside the real locus: a RUNTIME receiver (L16) and
	/// <c>Public</c> rather than <c>NonPublic</c> flags (L15). Both conditions fail independently.</summary>
	[Fact]
	public async Task RuntimeReceiverPublicFlags_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using Xunit;

public class ShapeTests
{
    [Fact]
    public void ReadsPublicProperty()
    {
        object obj = this;
        var name = ""Name"";
        Assert.NotNull(obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public));
    }
}
");
	}

	/// <summary>N5 — pins L1 (the scope gate). All three shapes in a class that declares no
	/// test-attributed method produce ZERO diagnostics.</summary>
	[Fact]
	public async Task AllThreeShapes_InNonTestClass_IsSilent()
	{
		await VerifyAsync(@"
using System;
using System.Reflection;
using System.Text;
using Xunit;

public class Foo { }
public class Bar { }
public enum Shade { A, B, C }

public class NotATestClass
{
    public void ConstantTautology()
    {
        Assert.NotEqual(typeof(Foo), typeof(Bar));
    }

    public void EnumShapePin()
    {
        Assert.Equal(3, Enum.GetValues<Shade>().Length);
    }

    public void ForeignReflection()
    {
        Assert.NotNull(typeof(StringBuilder).GetMethod(""Hidden"", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
");
	}

	/// <summary>N6 — pins L17 (Case C's containing-assembly ownership comparison). The reflected type is
	/// declared IN the fixture source, so the compilation owns it — testing your own internals is
	/// ordinary.</summary>
	[Fact]
	public async Task OwnAssemblyReflection_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using Xunit;

public class OwnType { }

public class ReflectionTests
{
    [Fact]
    public void ProbesOwnMember()
    {
        Assert.NotNull(typeof(OwnType).GetMethod(""Hidden"", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
");
	}

	/// <summary>N7 — pins L3 (the <c>Assert</c> containing-type gate). A non-<c>Assert</c> assertion
	/// surface is a DECLARED scope boundary: FluentAssertions, Shouldly, and raw <c>Debug.Assert</c> are
	/// deliberately out of scope, not an oversight.</summary>
	[Fact]
	public async Task NonAssertAssertionSurface_IsSilent()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }
public class Bar { }

public static class Verify
{
    public static void Equal(object expected, object actual) { }
}

public class ShapeTests
{
    [Fact]
    public void TypesDiffer()
    {
        Verify.Equal(typeof(Foo), typeof(Bar));
    }
}
");
	}

	/// <summary>N8 — pins L9 (the member-access arm's RECEIVER condition). <c>obj.GetType()</c> is a
	/// runtime value, so the metadata read through it is behavior-dependent.</summary>
	[Fact]
	public async Task RuntimeReceiverDeclarationMetadata_IsSilent()
	{
		await VerifyAsync(@"
using Xunit;

public class ShapeTests
{
    [Fact]
    public void ReceiverIsRuntime()
    {
        object obj = this;
        Assert.True(obj.GetType().IsPublic);
    }
}
");
	}

	/// <summary>N9 — pins L12 (Case B's <c>System.Enum</c> containing-type check). A same-named helper on
	/// an unrelated type must not match.</summary>
	[Fact]
	public async Task SameNamedNonEnumHelper_IsSilent()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public enum Shade { A }

public static class MyEnumHelper
{
    public static string[] GetNames(Type type) => new[] { ""A"" };
}

public class ShapeTests
{
    [Fact]
    public void NamesArePinned()
    {
        Assert.Equal(new[] { ""A"" }, MyEnumHelper.GetNames(typeof(Shade)));
    }
}
");
	}

	/// <summary>N10 — pins L14's constant requirement: a non-constant member name cannot be proven, so the
	/// rule stays silent rather than guessing.</summary>
	[Fact]
	public async Task NonConstantMemberName_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using System.Text;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesComputedMember()
    {
        Probe(""Hidden"");
    }

    private static MethodInfo Probe(string memberName)
        => typeof(StringBuilder).GetMethod(memberName, BindingFlags.Instance | BindingFlags.NonPublic);
}
");
	}

	/// <summary>N11 — pins L15 in isolation: the receiver IS a foreign <c>typeof</c>, only the
	/// <c>NonPublic</c> flag is absent.</summary>
	[Fact]
	public async Task ForeignPublicReflection_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using System.Text;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesVisibleMember()
    {
        Assert.NotNull(typeof(StringBuilder).GetMethod(""Visible"", BindingFlags.Instance | BindingFlags.Public));
    }
}
");
	}

	/// <summary>N12 — pins L16 in isolation: the flags ARE <c>NonPublic</c>, only the <c>typeof</c>
	/// receiver is absent.</summary>
	[Fact]
	public async Task RuntimeReceiverNonPublicReflection_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesHiddenMemberAtRuntime()
    {
        object obj = this;
        Assert.NotNull(obj.GetType().GetMethod(""Hidden"", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
");
	}

	/// <summary>
	/// N13 — pins the ZERO-ARGUMENT prefilter, NOT Case C's method-name set. It was originally filed against
	/// matrix row L18, and that claim was affirmatively false: <c>GetInterfaces()</c> has no arguments, so it
	/// exits at the arity prefilter long before the name set is consulted, and it has neither a <c>name</c>
	/// nor a <c>bindingAttr</c> parameter even without that exit. The true isolating negative for the name
	/// set is <see cref="ForeignNonPublicNestedTypeLookup_IsSilent"/>.
	/// </summary>
	[Fact]
	public async Task NonMemberLookupReflection_IsSilent()
	{
		await VerifyAsync(@"
using System.Text;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesInterfaces()
    {
        Assert.NotNull(typeof(StringBuilder).GetInterfaces());
    }
}
");
	}

	/// <summary>
	/// N14 — pins L19 (the member-scope <c>CodeStyleBypass</c> walk). The locked message promises
	/// <c>[NotNot.Bcl.Diagnostics.CodeStyleBypass]</c> ON THE MEMBER as tier 4; the assembly-level
	/// short-circuit alone would not honour that promise. A <c>#pragma</c> fixture is deliberately omitted:
	/// pragma suppression is a compiler mechanism with no analyzer-side code path, so it carries no
	/// marginal detection value.
	/// </summary>
	[Fact]
	public async Task MemberScopeCodeStyleBypass_IsSilent()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }
public class Bar { }

public class ShapeTests
{
    [Fact]
    [NotNot.Bcl.Diagnostics.CodeStyleBypass]
    public void TypesDiffer()
    {
        Assert.NotEqual(typeof(Foo), typeof(Bar));
    }
}
");
	}

	// ── F1: unconditional-outcome markers stay silent ─────────────────────────

	/// <summary>
	/// N15 — <c>Assert.Fail(string)</c> is all-constant by construction but its fixed outcome is FAILURE, so
	/// it is a negative-path DETECTOR, not ceremony. Reporting it is the one shape where this rule could
	/// cause the harm it exists to prevent: a local control marker names no consumer, so tier 1 cannot apply
	/// and the ladder routes the reader to tier 3 — deleting it converts a test that FAILS when the expected
	/// exception is not thrown into one that PASSES.
	/// </summary>
	[Fact]
	public async Task UnconditionalFailMarker_IsSilent()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public class ShapeTests
{
    [Fact]
    public void ThrowsOnBadInput()
    {
        try
        {
            throw new ArgumentException(""bad"");
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.Fail(""expected ArgumentException"");
    }
}
");
	}

	/// <summary>N16 — <c>Assert.Skip(string)</c>, same mechanism: the outcome is unconditional by
	/// design.</summary>
	[Fact]
	public async Task UnconditionalSkipMarker_IsSilent()
	{
		await VerifyAsync(@"
using Xunit;

public class ShapeTests
{
    [Fact]
    public void NotSupportedHere()
    {
        Assert.Skip(""not supported on this platform"");
    }
}
");
	}

	/// <summary>
	/// N17 — the version-independent arm of the same finding, and the one that does NOT rest on a method-name
	/// set: <c>Assert.True(false, …)</c> and <c>Assert.False(true, …)</c> have a COMPARED argument that is a
	/// constant, so the all-quantifier is satisfied — but the fixed outcome is FAILURE. Vacuity means a fixed
	/// outcome of PASS; both must stay silent.
	/// </summary>
	[Fact]
	public async Task ConstantForcedFailureMarkers_IsSilent()
	{
		await VerifyAsync(@"
using Xunit;

public class ShapeTests
{
    [Fact]
    public void UnreachableBranches()
    {
        Assert.True(false, ""unreachable branch"");
        Assert.False(true, ""also unreachable"");
    }
}
");
	}

	// ── F2: Case B searches COMPARED arguments only ───────────────────────────

	/// <summary>
	/// N18 — the enum accessor appears ONLY in the failure-message parameter, so it pins nothing: the verdict
	/// rests entirely on <c>Subject.IsReady()</c>. Pins the compared/carried split for Case B. Without it,
	/// Case B fires on an assertion whose argument tree merely CONTAINS an enum accessor, which is what made
	/// the declared "enumeration source is silent" boundary key on the method name rather than on the
	/// property the promise is about.
	/// </summary>
	[Fact]
	public async Task EnumAccessorInCarriedMessage_IsSilent()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public enum Shade { A, B }

public static class Subject
{
    public static bool IsReady() => true;
}

public class ShapeTests
{
    [Fact]
    public void ReadyWithEnumMessage()
    {
        Assert.True(Subject.IsReady(), string.Join("","", Enum.GetNames<Shade>()));
    }
}
");
	}

	/// <summary>
	/// N19 — pins Case B's ACCESSOR-NAME set, a different line from the assertion-method allowlist that N3
	/// pins and from the containing-type check that N9 pins. <c>Enum.IsDefined</c> is a <c>System.Enum</c>
	/// call inside an allowlisted assertion; only the accessor-name set keeps it silent, so deleting that
	/// set widens Case B to every <c>System.Enum</c> invocation.
	/// </summary>
	[Fact]
	public async Task EnumNonAccessorCall_IsSilent()
	{
		await VerifyAsync(@"
using System;
using Xunit;

public enum Shade { A, B, C, D }

public class ShapeTests
{
    [Fact]
    public void ValueIsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(Shade), 3));
    }
}
");
	}

	// ── F3: bare GENERIC call under `using static` ────────────────────────────

	/// <summary>
	/// A6 — pins the outer gate's node-shape breadth for the GENERIC spelling.
	/// <c>GenericNameSyntax</c> is a SIBLING of <c>IdentifierNameSyntax</c> under <c>SimpleNameSyntax</c>,
	/// not a subtype, so a gate written against the narrower type excluded <c>Equal&lt;int&gt;(1, 1)</c>
	/// before any symbol resolution while the qualified spelling fired — a direct recurrence of the
	/// confirmed NN_R011 qualification-decides hole that A5 could not expose, since A5 exercises only the
	/// bare NON-generic call.
	/// </summary>
	[Fact]
	public async Task BareGenericStaticImportedAssert_Fires()
	{
		await VerifyAsync(@"
using static Xunit.Assert;
using Xunit;

public class GenericShapeTests
{
    [Fact]
    public void ConstantsEqual()
    {
        {|#0:Equal<int>(1, 1)|};
    }
}
", Expect(ConstantTautology, "ConstantsEqual"));
	}

	// ── F4: scope gate decides on the TYPE, not one declaration ───────────────

	/// <summary>
	/// A7 — the assertion sits in a <c>partial</c> part that declares no test-attributed method of its own.
	/// The locked specification scopes membership to the TYPE ("any method in that TYPE"), and a type symbol
	/// merges partial parts; a gate reading one <c>TypeDeclarationSyntax</c>'s members went silent here, and
	/// moving the same helper into the attributed part would have made it fire.
	/// </summary>
	[Fact]
	public async Task PartialTestClassPart_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }
public class Bar { }

public partial class PartialShapeTests
{
    [Fact]
    public void Marker()
    {
        Helper();
    }
}

public partial class PartialShapeTests
{
    private static void Helper()
    {
        {|#0:Assert.NotEqual(typeof(Foo), typeof(Bar))|};
    }
}
", Expect(ConstantTautology, "Helper"));
	}

	/// <summary>A8 — the same divergence through INHERITANCE: the derived test class declares no attributed
	/// method, it inherits them. The type symbol's base chain covers it.</summary>
	[Fact]
	public async Task InheritedTestAttributedBase_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }
public class Bar { }

public class ShapeTestBase
{
    [Fact]
    public void Marker()
    {
    }
}

public class DerivedShapeTests : ShapeTestBase
{
    public void Helper()
    {
        {|#0:Assert.NotEqual(typeof(Foo), typeof(Bar))|};
    }
}
", Expect(ConstantTautology, "Helper"));
	}

	/// <summary>A9 — pins the attribute-name normalization. <c>[FactAttribute]</c> is the same attribute as
	/// <c>[Fact]</c>, but <c>"FactAttribute".EndsWith("Fact")</c> is FALSE, so without the suffix strip the
	/// whole rule goes dark for a file written that way.</summary>
	[Fact]
	public async Task AttributeSuffixSpelling_Fires()
	{
		await VerifyAsync(@"
using Xunit;

public class Foo { }
public class Bar { }

public class SuffixShapeTests
{
    [FactAttribute]
    public void TypesDiffer()
    {
        {|#0:Assert.NotEqual(typeof(Foo), typeof(Bar))|};
    }
}
", Expect(ConstantTautology, "TypesDiffer"));
	}

	// ── F5: the true isolating negatives for two Case-C lines ─────────────────

	/// <summary>
	/// N20 — the TRUE isolating negative for Case C's method-name set, replacing the false L18/N13 claim.
	/// Every other Case-C condition holds: a foreign <c>typeof</c> receiver, a constant member name, and
	/// <c>NonPublic</c> flags. It is silent for exactly one reason — <c>GetNestedType</c> is outside the
	/// member-lookup name set — so deleting that set makes this fixture fire.
	/// </summary>
	[Fact]
	public async Task ForeignNonPublicNestedTypeLookup_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using System.Text;
using Xunit;

public class ReflectionTests
{
    [Fact]
    public void ProbesNestedType()
    {
        Assert.NotNull(typeof(StringBuilder).GetNestedType(""Hidden"", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
");
	}

	/// <summary>
	/// N21 — pins Case C's <c>System.Type</c> containing-type check, which is the sole mechanism behind
	/// declared scope-boundary entry 6 (a member named <c>GetMethod</c> on something that is not
	/// <c>System.Type</c> is not reflection over declaration metadata).
	/// </summary>
	[Fact]
	public async Task NonSystemTypeMemberLookup_IsSilent()
	{
		await VerifyAsync(@"
using System.Reflection;
using Xunit;

public static class FakeType
{
    public static MethodInfo GetMethod(string name, BindingFlags bindingAttr) => null;
}

public class ReflectionTests
{
    [Fact]
    public void ProbesFakeType()
    {
        Assert.NotNull(FakeType.GetMethod(""Hidden"", BindingFlags.Instance | BindingFlags.NonPublic));
    }
}
");
	}
}
