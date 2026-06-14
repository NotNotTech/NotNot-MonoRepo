using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for <see cref="AppSettingsCodeDefaultAnalyzer"/> (NN_C004).
/// </summary>
/// <remarks>
/// <para>
/// Fixtures declare a test-source-local settings type carrying
/// <c>[System.CodeDom.Compiler.GeneratedCode("NotNot.AppSettings", ...)]</c> rather than referencing the
/// real NotNot.AppSettings generator output. The analyzer matches on the attribute FQN + the tool-name
/// constructor argument, so a locally-declared marked type is semantically equivalent to a generated one
/// for matching purposes — keeping the harness reference-free (mirrors the NN_C003 bypass-attribute
/// approach).
/// </para>
/// <para>
/// Every generated settings property is nullable <c>T?</c> and all JSON numbers map to <c>double</c>, so the
/// fixtures model <c>double?</c> options that consumers cast (<c>(int?)</c>) before applying the smell —
/// matching the real exemplars (<c>VowTranscriptEventCache.cs</c>, <c>PtyPolicyResolver.cs</c>).
/// </para>
/// </remarks>
public class AppSettingsCodeDefaultAnalyzerTests
{
	/// <summary>
	/// Shared declaration of a NotNot.AppSettings-shaped (marked) settings type plus a canonical-default
	/// holder, included in every fixture source.
	/// </summary>
	private const string SettingsDecl = @"
namespace Gen
{
    [System.Runtime.CompilerServices.CompilerGenerated]
    [System.CodeDom.Compiler.GeneratedCode(""NotNot.AppSettings"", ""1.2.3"")]
    public partial class VowSessions
    {
        public double? ProjectionMruCapacity { get; set; }
        public double? ProjectionIdleTtlMinutes { get; set; }
        public string? Name { get; set; }
        public Nested? Sessions { get; set; }
    }

    [System.Runtime.CompilerServices.CompilerGenerated]
    [System.CodeDom.Compiler.GeneratedCode(""NotNot.AppSettings"", ""1.2.3"")]
    public partial class Nested
    {
        public double? MaxOpenPty { get; set; }
    }
}

public static class Defaults
{
    public const int MaxOpenPty = 30;
    public static readonly int StaticFallback = 7;
}

public class NotSettings
{
    public int? Prop { get; set; }
}
";

	private static async Task VerifyAsync(string consumerBody, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<AppSettingsCodeDefaultAnalyzer, DefaultVerifier>
		{
			TestCode = SettingsDecl + consumerBody,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		// Ignore compiler diagnostics (nullable-context warnings, unused-value warnings) — only the
		// analyzer diagnostics are under test.
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Expect(int markupSpan, string memberName)
	{
		return new DiagnosticResult(AppSettingsCodeDefaultAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(markupSpan)
			.WithArguments(memberName);
	}

	#region Positive — fires at the ?? operator

	[Fact]
	public async Task CastNumericLiteralDefault_FiresAtOperator()
	{
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var mruCapacity = (int?)s.ProjectionMruCapacity {|#0:??|} 4;
        _ = mruCapacity;
    }
}";
		await VerifyAsync(consumer, Expect(0, "ProjectionMruCapacity"));
	}

	[Fact]
	public async Task CastDoubleLiteralDefault_FiresAtOperator()
	{
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var idleTtl = (double?)s.ProjectionIdleTtlMinutes {|#0:??|} 10.0;
        _ = idleTtl;
    }
}";
		await VerifyAsync(consumer, Expect(0, "ProjectionIdleTtlMinutes"));
	}

	[Fact]
	public async Task ConditionalAccessConstFieldDefault_FiresAtOperator()
	{
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var max = (int?)s.Sessions?.MaxOpenPty {|#0:??|} Defaults.MaxOpenPty;
        _ = max;
    }
}";
		await VerifyAsync(consumer, Expect(0, "MaxOpenPty"));
	}

	[Fact]
	public async Task StaticReadonlyFieldDefault_FiresAtOperator()
	{
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var v = (int?)s.ProjectionMruCapacity {|#0:??|} Defaults.StaticFallback;
        _ = v;
    }
}";
		await VerifyAsync(consumer, Expect(0, "ProjectionMruCapacity"));
	}

	[Fact]
	public async Task StringLiteralDefault_FiresAtOperator()
	{
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var name = s.Name {|#0:??|} ""fallback"";
        _ = name;
    }
}";
		await VerifyAsync(consumer, Expect(0, "Name"));
	}

	[Fact]
	public async Task NonEmptyStringLiteralDefault_FiresAtOperator()
	{
		// A non-empty string literal is a genuine code-side default → still fires.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var name = s.Name {|#0:??|} ""vs-running"";
        _ = name;
    }
}";
		await VerifyAsync(consumer, Expect(0, "Name"));
	}

	#endregion

	#region Negative — silent (zero false positives)

	[Fact]
	public async Task EmptyStringLiteralRight_IsNullNormalization_NoDiagnostic()
	{
		// `?? ""` coalesces null to a blank string (null-NORMALIZATION), not a config default → silent.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var name = s.Name ?? """";
        _ = name;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task WhitespaceStringLiteralRight_IsNullNormalization_NoDiagnostic()
	{
		// `?? "   "` is whitespace-only → null-NORMALIZATION, not a config default → silent.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var name = s.Name ?? ""   "";
        _ = name;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task StringEmptyFieldRight_IsNullNormalization_NoDiagnostic()
	{
		// `?? string.Empty` is the field form of `?? ""` → null-NORMALIZATION, not a config default → silent.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var name = s.Name ?? string.Empty;
        _ = name;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task ThrowExpression_IsPermittedRequiredPattern_NoDiagnostic()
	{
		// `?? throw` is the fail-loud required-value pattern the rule must PERMIT.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var v = (int?)s.ProjectionMruCapacity ?? throw new System.InvalidOperationException(""required"");
        _ = v;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task LocalVariableLeft_NoDiagnostic()
	{
		// LHS is a local, not a settings option.
		string consumer = @"
public class Consumer
{
    public void M()
    {
        int? local = null;
        var v = local ?? 4;
        _ = v;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task NonSettingsTypeMemberLeft_NoDiagnostic()
	{
		// Containing type lacks [GeneratedCode(""NotNot.AppSettings"")].
		string consumer = @"
public class Consumer
{
    private NotSettings o = new NotSettings();
    public void M()
    {
        var v = o.Prop ?? 4;
        _ = v;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task MethodCallRight_NoDiagnostic()
	{
		// Computed default (method invocation) → cannot live in static JSON → out of scope.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    private int Compute() => 1;
    public void M()
    {
        var v = (int?)s.ProjectionMruCapacity ?? Compute();
        _ = v;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task PropertyReadRight_NoDiagnostic()
	{
		// Environment-derived property read → computed → out of scope (only const/static-readonly FIELDS fire).
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var v = (int?)s.ProjectionMruCapacity ?? System.Environment.ProcessorCount;
        _ = v;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task NullLiteralRight_NoDiagnostic()
	{
		// `?? null` is not a default VALUE.
		string consumer = @"
public class Consumer
{
    private Gen.VowSessions s = new Gen.VowSessions();
    public void M()
    {
        var v = s.Name ?? null;
        _ = v;
    }
}";
		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task InsideGeneratedCode_NoDiagnostic()
	{
		// The `??` smell lives INSIDE a [GeneratedCode] type → skipped by ConfigureGeneratedCodeAnalysis(None),
		// mirroring the real *.g.cs settings files.
		string consumer = @"
namespace GenConsumerNs
{
    [System.CodeDom.Compiler.GeneratedCode(""NotNot.AppSettings"", ""1.2.3"")]
    public partial class GenConsumer
    {
        private Gen.VowSessions s = new Gen.VowSessions();
        public void M()
        {
            var v = (int?)s.ProjectionMruCapacity ?? 4;
            _ = v;
        }
    }
}";
		await VerifyAsync(consumer);
	}

	#endregion
}
