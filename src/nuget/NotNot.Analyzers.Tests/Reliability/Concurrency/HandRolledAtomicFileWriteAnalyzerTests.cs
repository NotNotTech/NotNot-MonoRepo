using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability.Concurrency;

/// <summary>
/// Tests for <see cref="HandRolledAtomicFileWriteAnalyzer"/> (NN_R007).
/// </summary>
/// <remarks>
/// <para>
/// REQ-1 (POSITIVE): a deterministic <c>filePath + ".tmp"</c> temp + <c>File.WriteAllText</c> +
/// <c>File.Move(temp, final, overwrite: true)</c> fires at the <c>File.Move</c> invocation.
/// </para>
/// <para>
/// REQ-2 (NEGATIVE — all silent): (a) the sanctioned <c>AtomicFileWriter.WriteAtomic</c> helper call,
/// (b) a Guid-unique temp + Move (the LOAD-BEARING precision case — must not fire on AtomicFileWriter's
/// own unique-temp pattern), (c) a plain <c>File.Move(a, b)</c> with no overwrite, and (d) a direct
/// <c>File.WriteAllText(final, ...)</c> with no temp + Move.
/// </para>
/// <para>
/// Fixtures declare a test-source-local <c>AtomicFileWriter</c> stub so the negative-(a) case resolves
/// without referencing NotNot.Bcl.Core — the analyzer never inspects that helper (its arg0 is a method
/// call, not a deterministic concat), so a local stub is semantically equivalent for matching.
/// <c>System.IO.File</c> is BCL, resolved from the net80 reference assemblies.
/// </para>
/// </remarks>
public class HandRolledAtomicFileWriteAnalyzerTests
{
	/// <summary>Test-source-local sanctioned-helper stub + a deterministic-source method, included in every fixture.</summary>
	private const string Decl = @"
using System;
using System.IO;

namespace NotNot.Storage
{
    public static class AtomicFileWriter
    {
        public static void WriteAtomic(string finalPath, string contents) { }
    }
}
";

	private static async Task VerifyAsync(string consumerBody, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<HandRolledAtomicFileWriteAnalyzer, DefaultVerifier>
		{
			TestCode = Decl + consumerBody,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		// Only the analyzer diagnostics are under test — ignore nullable/unused compiler diagnostics.
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Diagnostic(int line, int column, string tempText)
		=> new DiagnosticResult(HandRolledAtomicFileWriteAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(tempText);

	// ---- REQ-1: POSITIVE — fires at the File.Move ----

	[Fact]
	public async Task DeterministicTemp_WriteThenOverwriteRename_Fires()
	{
		// Decl spans 11 content lines (1-11); the consumer string's own leading newline is line 12, so the
		// consumer's `using System.IO;` is absolute line 13. File.Move (8th line of the consumer body after
		// its leading blank) lands on absolute line 21, column 9 (4-space indent inside the method).
		var consumer = @"
using System.IO;

public class Repo
{
    public void Save(string filePath, string json)
    {
        var tempPath = filePath + "".tmp"";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }
}";

		await VerifyAsync(consumer, Diagnostic(21, 9, "tempPath"));
	}

	[Fact]
	public async Task DeterministicTemp_PositionalOverwriteTrue_Fires()
	{
		// Positional `true` (not `overwrite:`-named) third arg still fires.
		var consumer = @"
using System.IO;

public class Repo
{
    public void Save(string filePath, string json)
    {
        var tempPath = filePath + "".tmp"";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, true);
    }
}";

		await VerifyAsync(consumer, Diagnostic(21, 9, "tempPath"));
	}

	[Fact]
	public async Task InlineDeterministicConcat_Fires()
	{
		// arg0 is an inline deterministic concat (no resolvable local) — still matched.
		var consumer = @"
using System.IO;

public class Repo
{
    public void Save(string filePath, string json)
    {
        File.WriteAllText(filePath + "".tmp"", json);
        File.Move(filePath + "".tmp"", filePath, overwrite: true);
    }
}";

		// No `var tempPath` line in this consumer, so File.Move lands on absolute line 20, column 9.
		await VerifyAsync(consumer, Diagnostic(20, 9, @"filePath + "".tmp"""));
	}

	// ---- REQ-2: NEGATIVES — all silent ----

	[Fact]
	public async Task SanctionedHelper_AtomicFileWriter_Silent()
	{
		// (a) The sanctioned helper call — no File.Move at all.
		var consumer = @"
using NotNot.Storage;

public class Repo
{
    public void Save(string filePath, string json)
    {
        AtomicFileWriter.WriteAtomic(filePath, json);
    }
}";

		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task GuidUniqueTemp_WriteThenMove_Silent()
	{
		// (b) LOAD-BEARING precision case: a Guid component makes the temp unique, not deterministic —
		// this is exactly AtomicFileWriter's own pattern; it must NOT fire.
		var consumer = @"
using System;
using System.IO;

public class Repo
{
    public void Save(string filePath, string json)
    {
        var tempPath = filePath + ""."" + Guid.NewGuid().ToString(""N"") + "".tmp"";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }
}";

		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task PlainMove_NoOverwrite_Silent()
	{
		// (c) A plain rename — no overwrite, not the temp-then-rename idiom.
		var consumer = @"
using System.IO;

public class Repo
{
    public void Rename(string a, string b)
    {
        File.Move(a, b);
    }
}";

		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task DirectFinalWrite_NoTemp_Silent()
	{
		// (d) A direct final write — no temp + Move (a different, out-of-scope concern).
		var consumer = @"
using System.IO;

public class Repo
{
    public void Save(string finalPath, string json)
    {
        File.WriteAllText(finalPath, json);
    }
}";

		await VerifyAsync(consumer);
	}
}
