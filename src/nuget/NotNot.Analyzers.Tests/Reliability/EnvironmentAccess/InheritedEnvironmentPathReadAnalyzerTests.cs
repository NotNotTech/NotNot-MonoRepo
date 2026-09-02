using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.EnvironmentAccess;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability.EnvironmentAccess;

/// <summary>
/// Tests for <see cref="InheritedEnvironmentPathReadAnalyzer"/> (NN_R011).
/// </summary>
/// <remarks>
/// <para>
/// POSITIVE: a read of the INHERITED <c>PATH</c> — the one-argument overload, or the two-argument overload
/// with an explicit <c>EnvironmentVariableTarget.Process</c> — in a type NOT marked
/// <c>[SystemPathOwner]</c>. Message arg {0} is the enclosing type name.
/// </para>
/// <para>
/// NEGATIVE, and this is where the rule earns its keep: the <c>Machine</c> and <c>User</c> targets read the
/// PERSISTENT store and are the FIX this rule steers callers toward. Flagging them would invert the rule
/// and condemn the corrected code, so those two cases are the load-bearing cells of this matrix rather
/// than routine coverage. The marked-owner case is the third: the owner legitimately falls back to the
/// inherited value when the persistent store is unreadable.
/// </para>
/// <para>
/// Fixtures declare a test-source-local <c>NotNot.Bcl.Diagnostics.SystemPathOwnerAttribute</c> rather than
/// referencing NotNot.Bcl.Core. The analyzer matches the marker by FULLY-QUALIFIED NAME, so a stub in the
/// same namespace is semantically identical for matching purposes — and it keeps these tests off the
/// cross-version reference-assembly problem that forces the DI-marker suite onto a custom net10 ref pack.
/// <c>System.Environment</c> and <c>EnvironmentVariableTarget</c> resolve from the net80 reference
/// assemblies.
/// </para>
/// <para>
/// STRUCTURE, and it is load-bearing: the marker stub and the fixture are added as TWO SEPARATE
/// compilation sources, never concatenated into one. Concatenation put every fixture's own <c>using</c>
/// directives AFTER <see cref="Decl"/>'s namespace declaration — CS1529, which
/// <c>CompilerDiagnostics.None</c> silently hides — so those imports were dead and fixtures survived only
/// on <c>Decl</c>'s file-level <c>using System;</c> leaking to them. That defect had already bitten once:
/// an owner fixture's dead import left the attribute bound to an ErrorType and the exemption never fired,
/// which reads exactly like an analyzer bug. As separate sources each fixture is a well-formed file and
/// owns its imports. Do NOT "simplify" this back to <c>Decl + consumerBody</c>.
/// </para>
/// </remarks>
public class InheritedEnvironmentPathReadAnalyzerTests
{
	/// <summary>Test-source-local marker stub, compiled as its own source file alongside each fixture.</summary>
	private const string Decl = @"
using System;

namespace NotNot.Bcl.Diagnostics
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SystemPathOwnerAttribute : Attribute { }
}
";

	private static async Task VerifyAsync(string consumerBody, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<InheritedEnvironmentPathReadAnalyzer, DefaultVerifier>
		{
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		test.TestState.Sources.Add(Decl);
		test.TestState.Sources.Add(consumerBody);
		// Only analyzer diagnostics are under test — ignore nullable/unused compiler diagnostics.
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Expect(string enclosingTypeName)
		=> new DiagnosticResult(InheritedEnvironmentPathReadAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(0)
			.WithArguments(enclosingTypeName);

	// ── POSITIVE ─────────────────────────────────────────────────────────────

	/// <summary>
	/// P1 — the canonical violation: the shape deleted from <c>CodexAppServerProcess.ResolveExecutable</c>
	/// in commit 73c31315, which walked the inherited PATH with no fallback and made every Codex session
	/// launch fail before spawning anything.
	/// </summary>
	[Fact]
	public async Task InheritedPathRead_InUnmarkedType_Fires()
	{
		await VerifyAsync(@"
using System;
using System.IO;

public static class PathWalker
{
    public static string? Resolve(string executable)
    {
        var pathValue = {|#0:Environment.GetEnvironmentVariable(""PATH"")|} ?? """";
        foreach (var dir in pathValue.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
", Expect("PathWalker"));
	}

	/// <summary>P2 — an explicit Process target is the same inherited read, spelled out.</summary>
	[Fact]
	public async Task ExplicitProcessTarget_InUnmarkedType_Fires()
	{
		await VerifyAsync(@"
using System;

public static class ProcessTargetReader
{
    public static string? Read()
        => {|#0:Environment.GetEnvironmentVariable(""PATH"", EnvironmentVariableTarget.Process)|};
}
", Expect("ProcessTargetReader"));
	}

	/// <summary>The variable name is matched case-insensitively, as the OS treats it.</summary>
	[Fact]
	public async Task LowercaseVariableName_Fires()
	{
		await VerifyAsync(@"
using System;

public static class LowercaseReader
{
    public static string? Read() => {|#0:Environment.GetEnvironmentVariable(""Path"")|};
}
", Expect("LowercaseReader"));
	}

	/// <summary>
	/// P4 — <c>using static System.Environment;</c> makes the call unqualified. It binds to the SAME method
	/// with the SAME runtime behavior, so qualification alone must not decide whether the rule fires.
	/// Independent review caught this as a metamorphic hole: the rule was silent here while flagging the
	/// byte-equivalent qualified call.
	/// </summary>
	[Fact]
	public async Task StaticImportedCall_InUnmarkedType_Fires()
	{
		await VerifyAsync(@"
using static System.Environment;

public static class StaticImportReader
{
    public static string? Read() => {|#0:GetEnvironmentVariable(""PATH"")|};
}
", Expect("StaticImportReader"));
	}

	/// <summary>
	/// P5 — named arguments written out of order. Reading arguments by SYNTAX POSITION inspected the target
	/// as if it were the variable name and went silent; arguments are resolved through their parameter.
	/// </summary>
	[Fact]
	public async Task ReorderedNamedArguments_InUnmarkedType_Fires()
	{
		await VerifyAsync(@"
using System;

public static class NamedArgReader
{
    public static string? Read()
        => {|#0:Environment.GetEnvironmentVariable(
               target: EnvironmentVariableTarget.Process, variable: ""PATH"")|};
}
", Expect("NamedArgReader"));
	}

	/// <summary>
	/// P6 — the marker's STRICTNESS half: a same-named attribute in an unrelated namespace must NOT exempt.
	/// Without this fixture the namespace comparison in <c>IsInsideOwnerMarkedType</c> could be deleted and
	/// the suite would stay green, leaving the rule silently disableable by a naming collision.
	/// </summary>
	[Fact]
	public async Task ImpostorMarkerInUnrelatedNamespace_DoesNotExempt_Fires()
	{
		await VerifyAsync(@"
using System;

namespace Impostor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SystemPathOwnerAttribute : Attribute { }

    [Impostor.SystemPathOwner]
    public static class NotTheRealOwner
    {
        public static string? Read() => {|#0:Environment.GetEnvironmentVariable(""PATH"")|};
    }
}
", Expect("NotTheRealOwner"));
	}

	// ── NEGATIVE — the two load-bearing cells ────────────────────────────────

	/// <summary>
	/// N1 — the Machine target reads the PERSISTENT store and is current even when this process's copy is
	/// stale. It is the remedy the rule recommends; flagging it would invert the rule.
	/// </summary>
	[Fact]
	public async Task MachineTarget_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class MachineReader
{
    public static string? Read()
        => Environment.GetEnvironmentVariable(""PATH"", EnvironmentVariableTarget.Machine);
}
");
	}

	/// <summary>N2 — the User target, same reasoning as the Machine target.</summary>
	[Fact]
	public async Task UserTarget_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class UserReader
{
    public static string? Read()
        => Environment.GetEnvironmentVariable(""PATH"", EnvironmentVariableTarget.User);
}
");
	}

	/// <summary>
	/// N3 — the sanctioned owner. Its inherited read is a deliberate soft fallback for a host that denies
	/// the persistent store: a stale path still resolves machine-installed tools, an empty one resolves
	/// nothing.
	/// </summary>
	[Fact]
	public async Task InheritedRead_InsideMarkedOwner_IsSilent()
	{
		await VerifyAsync(@"
[NotNot.Bcl.Diagnostics.SystemPathOwner]
public static class SystemPathProvider
{
    public static string GetSystemPath()
    {
        var processPath = Environment.GetEnvironmentVariable(""PATH"") ?? """";
        var machine = Environment.GetEnvironmentVariable(""PATH"", EnvironmentVariableTarget.Machine);
        return string.IsNullOrWhiteSpace(machine) ? processPath : machine;
    }
}
");
	}

	/// <summary>A helper nested inside the owner inherits the owner's exemption.</summary>
	[Fact]
	public async Task InheritedRead_InNestedTypeOfMarkedOwner_IsSilent()
	{
		await VerifyAsync(@"
[NotNot.Bcl.Diagnostics.SystemPathOwner]
public static class OuterOwner
{
    private static class Inner
    {
        public static string? Read() => Environment.GetEnvironmentVariable(""PATH"");
    }
}
");
	}

	// ── NEGATIVE — scope and precision ───────────────────────────────────────

	/// <summary>
	/// N4 — PATHEXT is deliberately out of scope: the executable-extension list is effectively immutable,
	/// so the staleness hazard does not apply and flagging it would produce findings with no defect behind
	/// them.
	/// </summary>
	[Fact]
	public async Task PathExt_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class ExtReader
{
    public static string? Read() => Environment.GetEnvironmentVariable(""PATHEXT"");
}
");
	}

	/// <summary>N5 — an unrelated variable.</summary>
	[Fact]
	public async Task UnrelatedVariable_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class HomeReader
{
    public static string? Read() => Environment.GetEnvironmentVariable(""HOME"");
}
");
	}

	/// <summary>N6 — a non-constant name cannot be proven to be PATH, so the rule stays silent.</summary>
	[Fact]
	public async Task NonConstantVariableName_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class DynamicReader
{
    public static string? Read(string variableName) => Environment.GetEnvironmentVariable(variableName);
}
");
	}

	/// <summary>N7 — a same-named method on an unrelated type must never match.</summary>
	[Fact]
	public async Task SameNamedMethodOnUnrelatedType_IsSilent()
	{
		await VerifyAsync(@"
public static class Environment
{
    public static string? GetEnvironmentVariable(string name) => null;
}

public static class Spoofed
{
    public static string? Read() => Environment.GetEnvironmentVariable(""PATH"");
}
");
	}

	/// <summary>N8 — <c>nameof</c> dereferences nothing at runtime.</summary>
	[Fact]
	public async Task NameOf_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class NameOfUser
{
    public static string Read() => nameof(Environment.GetEnvironmentVariable);
}
");
	}

	/// <summary>The documented last-tier escape still works.</summary>
	[Fact]
	public async Task PragmaSuppressed_IsSilent()
	{
		await VerifyAsync(@"
using System;

public static class SuppressedReader
{
    public static string? Read()
    {
#pragma warning disable NN_R011
        return Environment.GetEnvironmentVariable(""PATH"");
#pragma warning restore NN_R011
    }
}
");
	}
}
