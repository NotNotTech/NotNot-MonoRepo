using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for <see cref="BoolDefaultFalseAnalyzer"/> (NN_C003).
/// </summary>
/// <remarks>
/// <para>
/// Bypass tests use a test-source-local declaration of
/// <c>NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute</c> rather than referencing
/// <c>NotNot.Bcl.Core</c>. The analyzer matches by fully-qualified name, which is
/// namespace+name (assembly-independent), so a locally-declared attribute in the right
/// namespace is semantically equivalent to the real one for matching purposes. This keeps
/// the test harness simple — no custom ReferenceAssemblies / PackageIdentity setup needed.
/// </para>
/// <para>
/// <b>Test design</b>: For property and field tests, the "fires" cases use <c>public</c> or
/// <c>internal</c> visibility (which are enforced by the rule). Private/protected/private-
/// protected are auto-exempt and covered separately under the "accessibility cutoff" cases.
/// Parameters are enforced regardless of method visibility — even a parameter on a private
/// method still fires.
/// </para>
/// </remarks>
public class BoolDefaultFalseAnalyzerTests
{
	/// <summary>
	/// Shared declaration of the bypass attribute included in every bypass-test source
	/// string so the analyzer's FQN-strict match succeeds without referencing
	/// NotNot.Bcl.Core.
	/// </summary>
	private const string BypassAttributeDecl = @"
namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false)]
    internal sealed class CodeStyleBypassAttribute : System.Attribute {}
}
";

	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<BoolDefaultFalseAnalyzer, DefaultVerifier>
		{
			TestCode = source,
			// Net8.0 reference assemblies expose `IsExternalInit` and the full record/init
			// support surface that several tests exercise (init-only properties, positional
			// records). Without this, default netstandard2.0 references trigger CS0518.
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Expect(int markupSpan, string kind, string memberName)
	{
		return new DiagnosticResult(BoolDefaultFalseAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(markupSpan)
			.WithArguments(kind, memberName);
	}

	#region Parameter detection — always fires regardless of method/type visibility

	[Fact]
	public async Task MethodParameter_DefaultTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void M(bool flag = {|#0:true|}) { }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "parameter", "flag"));
	}

	[Fact]
	public async Task MethodParameter_DefaultFalse_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void M(bool flag = false) { }
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MethodParameter_NoDefault_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void M(bool flag) { }
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ConstructorParameter_DefaultTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public TestClass(bool flag = {|#0:true|}) { }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "constructor parameter", "flag"));
	}

	[Fact]
	public async Task RecordPositionalParameter_DefaultTrue_FiresDiagnostic()
	{
		string source = @"
public record TestRecord(bool Flag = {|#0:true|});
";
		await VerifyAnalyzerAsync(source, Expect(0, "record positional parameter", "Flag"));
	}

	[Fact]
	public async Task PrimaryConstructorParameter_DefaultTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass(bool flag = {|#0:true|})
{
}";
		await VerifyAnalyzerAsync(source, Expect(0, "primary-constructor parameter", "flag"));
	}

	[Fact]
	public async Task NullableBoolParameter_DefaultTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void M(bool? flag = {|#0:true|}) { }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "parameter", "flag"));
	}

	[Fact]
	public async Task NullableBoolParameter_DefaultNull_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void M(bool? flag = null) { }
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NonBoolParameter_DefaultTrue_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void M(int x = 1) { }
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MultipleParameters_OneDefaultTrue_FiresOnce()
	{
		string source = @"
public class TestClass
{
    public void M(string name = ""x"", bool flag = {|#0:true|}) { }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "parameter", "flag"));
	}

	[Fact]
	public async Task PrivateMethod_BoolParameterDefaultTrue_FiresDiagnostic()
	{
		// Parameters ignore method visibility — even a parameter on a private method fires.
		// The convention applies to the call-site contract, which exists even for private callers.
		string source = @"
public class TestClass
{
    private void M(bool flag = {|#0:true|}) { }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "parameter", "flag"));
	}

	[Fact]
	public async Task PrivateClassPublicMethod_BoolParameterDefaultTrue_FiresDiagnostic()
	{
		// Even if the enclosing class is private (nested), parameters still fire.
		string source = @"
public class OuterClass
{
    private class Inner
    {
        public void M(bool flag = {|#0:true|}) { }
    }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "parameter", "flag"));
	}

	#endregion

	#region Property detection — public/internal fire; private/protected exempt

	[Fact]
	public async Task PublicAutoProperty_InitializerTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public bool Flag { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	[Fact]
	public async Task PublicAutoProperty_InitializerFalse_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    public bool Flag { get; set; } = false;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task PublicAutoProperty_NoInitializer_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    public bool Flag { get; set; }
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task InternalAutoProperty_InitializerTrue_FiresDiagnostic()
	{
		// internal is enforced — visible to whole assembly, broad enough to matter.
		string source = @"
public class TestClass
{
    internal bool Flag { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	[Fact]
	public async Task ProtectedInternalAutoProperty_InitializerTrue_FiresDiagnostic()
	{
		// protected internal is enforced — the "internal" half makes it visible to whole assembly.
		string source = @"
public class TestClass
{
    protected internal bool Flag { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	[Fact]
	public async Task PrivateAutoProperty_InitializerTrue_NoDiagnostic()
	{
		// private = implementation detail, no external consumer surface → exempt.
		string source = @"
public class TestClass
{
    private bool Flag { get; set; } = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ProtectedAutoProperty_InitializerTrue_NoDiagnostic()
	{
		// protected = visible only to derived classes (authored deliberately) → exempt.
		string source = @"
public class TestClass
{
    protected bool Flag { get; set; } = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task PrivateProtectedAutoProperty_InitializerTrue_NoDiagnostic()
	{
		// private protected = MORE restrictive than protected (same-assembly only) → exempt.
		string source = @"
public class TestClass
{
    private protected bool Flag { get; set; } = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task PublicInitOnlyProperty_InitializerTrue_FiresDiagnostic()
	{
		// Option A: { get; init; } is NOT readonly-equivalent — init is still consumer-facing
		// via object-initializer syntax, so the silent-drift concern applies.
		string source = @"
public class TestClass
{
    public bool Flag { get; init; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	[Fact]
	public async Task PublicGetOnlyProperty_InitializerTrue_FiresDiagnostic()
	{
		// Get-only property still fires. The consumer-facing read surface shows `true` by
		// default; readonly-equivalent does not exempt, only restricted visibility does.
		string source = @"
public class TestClass
{
    public bool Flag { get; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	[Fact]
	public async Task PublicExpressionBodiedProperty_True_NoDiagnostic()
	{
		// Computed property, no initializer surface — out of scope. The body returns true,
		// but that's a computation, not a default-state declaration.
		string source = @"
public class TestClass
{
    public bool Flag => true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task PublicNullableBoolProperty_InitializerTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public bool? Flag { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	[Fact]
	public async Task PrivateSetterProperty_InitializerTrue_FiresDiagnostic()
	{
		// { get; private set; } has a set accessor (just restricted access). The PROPERTY is
		// public, so consumers see the default-true. The private-set just constrains
		// post-construction mutation but doesn't change the consumer-facing default surface.
		string source = @"
public class TestClass
{
    public bool Flag { get; private set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Flag"));
	}

	#endregion

	#region Field detection — public/internal fire; private/protected exempt

	[Fact]
	public async Task PublicField_InitializerTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public bool _flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "_flag"));
	}

	[Fact]
	public async Task InternalField_InitializerTrue_FiresDiagnostic()
	{
		// internal is enforced — visible to whole assembly.
		string source = @"
public class TestClass
{
    internal bool _flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "_flag"));
	}

	[Fact]
	public async Task ProtectedInternalField_InitializerTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    protected internal bool _flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "_flag"));
	}

	[Fact]
	public async Task PrivateField_InitializerTrue_NoDiagnostic()
	{
		// private field = implementation detail → exempt.
		string source = @"
public class TestClass
{
    private bool _flag = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ProtectedField_InitializerTrue_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    protected bool _flag = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task PrivateProtectedField_InitializerTrue_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    private protected bool _flag = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NoModifierField_InitializerTrue_NoDiagnostic()
	{
		// No modifier on a class field = implicit private → exempt.
		string source = @"
public class TestClass
{
    bool _flag = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task InternalField_InitializerFalse_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    internal bool _flag = false;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task InternalField_NoInitializer_NoDiagnostic()
	{
		string source = @"
public class TestClass
{
    internal bool _flag;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ConstField_True_NoDiagnostic()
	{
		// `const bool X = true` is a declared value, not a default state — out of scope
		// regardless of accessibility.
		string source = @"
public class TestClass
{
    public const bool Flag = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task StaticReadonlyField_True_FiresDiagnostic()
	{
		// `static readonly` does NOT exempt — readonly is no longer an exemption rule. The
		// only exemptions for fields are restricted visibility (private/protected/private
		// protected) or `const` (which is a compile-time value, not a default state).
		string source = @"
public class TestClass
{
    public static readonly bool Flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "Flag"));
	}

	[Fact]
	public async Task PublicReadonlyInstanceField_True_FiresDiagnostic()
	{
		// Readonly does NOT exempt — only restricted visibility does. Public readonly with
		// default `true` still flags because the consumer-facing read surface shows `true`.
		string source = @"
public class TestClass
{
    public readonly bool Flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "Flag"));
	}

	[Fact]
	public async Task InternalReadonlyInstanceField_True_FiresDiagnostic()
	{
		// `internal readonly` — readonly does not exempt; internal is broad enough that the
		// consumer-facing concern applies across the assembly.
		string source = @"
public class TestClass
{
    internal readonly bool Flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "Flag"));
	}

	[Fact]
	public async Task PublicNullableBoolField_InitializerTrue_FiresDiagnostic()
	{
		string source = @"
public class TestClass
{
    public bool? _flag = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "_flag"));
	}

	[Fact]
	public async Task MultipleFieldsInOneDeclaration_OneTrue_FiresOnce()
	{
		string source = @"
public class TestClass
{
    internal bool _a = false, _b = {|#0:true|}, _c = false;
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "_b"));
	}

	#endregion

	#region Out-of-scope (negative coverage)

	[Fact]
	public async Task LocalVariable_True_NoDiagnostic()
	{
		// Locals aren't a "default" surface — out of scope.
		string source = @"
public class TestClass
{
    public void M()
    {
        bool flag = true;
        _ = flag;
    }
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NonLiteralDefault_NoDiagnostic()
	{
		// Non-literal expression — even though the constant evaluates to true, we only fire
		// on a literal `true`. This is intentional — see analyzer remarks.
		string source = @"
public class TestClass
{
    public const bool _const = true;
    public bool _flag = _const;
}";
		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Bypass attribute

	[Fact]
	public async Task AssemblyBypass_SuppressesAllDiagnostics()
	{
		// All members use accessibility that would normally fire (public/internal) — so the
		// suppression is genuinely caused by the assembly bypass attribute, not by visibility.
		// `[assembly:]` attributes must precede every other element (CS1730), so we cannot
		// use the shared `BypassAttributeDecl` prefix here — inline the attribute namespace
		// AFTER the assembly attribute instead.
		string source = @"
[assembly: NotNot.Bcl.Diagnostics.CodeStyleBypass]

namespace NotNot.Bcl.Diagnostics
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false)]
    internal sealed class CodeStyleBypassAttribute : System.Attribute {}
}

public class TestClass
{
    public void M(bool flag = true) { }
    public bool Prop { get; set; } = true;
    internal bool _field = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task TypeBypass_SuppressesMembersInThatType()
	{
		string source = BypassAttributeDecl + @"

[NotNot.Bcl.Diagnostics.CodeStyleBypass]
public class BypassedClass
{
    public void M(bool flag = true) { }
    public bool Prop { get; set; } = true;
    internal bool _field = true;
}";
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task TypeBypass_DoesNotAffectOtherTypes()
	{
		// One class is bypassed; a sibling class is NOT — the sibling's violation still fires.
		string source = BypassAttributeDecl + @"

[NotNot.Bcl.Diagnostics.CodeStyleBypass]
public class BypassedClass
{
    public bool BypassedProp { get; set; } = true;
}

public class NotBypassedClass
{
    public bool LiveProp { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "LiveProp"));
	}

	[Fact]
	public async Task PropertyMemberBypass_SuppressesOnlyThatProperty()
	{
		string source = BypassAttributeDecl + @"

public class TestClass
{
    [NotNot.Bcl.Diagnostics.CodeStyleBypass]
    public bool BypassedProp { get; set; } = true;

    public bool LiveProp { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "LiveProp"));
	}

	[Fact]
	public async Task FieldMemberBypass_SuppressesOnlyThatField()
	{
		// Both fields are internal (which would fire) — so the suppression is genuinely
		// caused by the bypass attribute, not by accessibility-exempt status.
		string source = BypassAttributeDecl + @"

public class TestClass
{
    [NotNot.Bcl.Diagnostics.CodeStyleBypass]
    internal bool _bypassedField = true;

    internal bool _liveField = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "field", "_liveField"));
	}

	[Fact]
	public async Task ParameterBypass_SuppressesOnlyThatParameter()
	{
		string source = BypassAttributeDecl + @"

public class TestClass
{
    public void M([NotNot.Bcl.Diagnostics.CodeStyleBypass] bool bypassed = true, bool live = {|#0:true|}) { }
}";
		await VerifyAnalyzerAsync(source, Expect(0, "parameter", "live"));
	}

	[Fact]
	public async Task BypassFromUnrelatedNamespace_DoesNotSuppressDiagnostics()
	{
		// FQN-strict lock-in: the bypass attribute MUST be in NotNot.Bcl.Diagnostics. An
		// attribute with the same simple name in a different namespace does NOT bypass —
		// preventing silent rule-disable via naming collision. Mirrors the AutoDiBypass H2
		// review-finding lock-in.
		string source = @"
namespace Some.Other.Namespace
{
    [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false)]
    internal sealed class CodeStyleBypassAttribute : System.Attribute {}
}

[Some.Other.Namespace.CodeStyleBypass]
public class TestClass
{
    public bool Prop { get; set; } = {|#0:true|};
}";
		await VerifyAnalyzerAsync(source, Expect(0, "property", "Prop"));
	}

	#endregion
}
