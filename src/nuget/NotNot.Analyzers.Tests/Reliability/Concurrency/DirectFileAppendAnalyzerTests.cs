using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability.Concurrency;

/// <summary>
/// Tests for <see cref="DirectFileAppendAnalyzer"/> (NN_R008).
/// </summary>
/// <remarks>
/// <para>
/// REQ-1 (POSITIVE): a direct <c>File.AppendAllText(finalPath, "x")</c> in an ordinary type fires at the
/// invocation, with arg {0} = the API simple name.
/// </para>
/// <para>
/// REQ-2 (NEGATIVE — all silent): (a) the sanctioned <c>AtomicFileWriter.AppendWithRetry</c> helper call,
/// (b) a <c>File.AppendAllText</c> INSIDE a type declared as <c>NotNot.Storage.AtomicFileWriter</c>
/// (the containing-type exemption — its <c>AppendWithRetry</c> calls <c>File.AppendAllText</c> internally),
/// (c) a <c>#pragma warning disable NN_R008</c>-wrapped append, (d) a direct <c>File.WriteAllText</c>
/// (out of scope — full write, not append), and (e) a <c>File.Move(temp, final, overwrite: true)</c>
/// (NN_R007's domain, not NN_R008).
/// </para>
/// <para>
/// Fixtures declare a test-source-local <c>AtomicFileWriter</c> stub so the negative-(a) case resolves
/// without referencing NotNot.Bcl.Core — the analyzer never inspects that helper (it is not a
/// <c>System.IO.File</c> member), so a local stub is semantically equivalent for matching.
/// <c>System.IO.File</c> is BCL, resolved from the net80 reference assemblies.
/// </para>
/// </remarks>
public class DirectFileAppendAnalyzerTests
{
	/// <summary>Test-source-local sanctioned-helper stub, included in every fixture (11 content lines).</summary>
	private const string Decl = @"
using System;
using System.IO;

namespace NotNot.Storage
{
    public static class AtomicFileWriter
    {
        public static void AppendWithRetry(string finalPath, string contents) { }
    }
}
";

	private static async Task VerifyAsync(string consumerBody, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<DirectFileAppendAnalyzer, DefaultVerifier>
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

	/// <summary>
	/// Runs a fixture WITHOUT the shared <see cref="Decl"/> stub prefix — for the containing-type
	/// exemption case, whose fixture itself declares <c>NotNot.Storage.AtomicFileWriter</c> (prefixing the
	/// stub would duplicate that type). Asserts NO analyzer diagnostic.
	/// </summary>
	private static async Task VerifyStandaloneAsync(string fixture)
	{
		var test = new CSharpAnalyzerTest<DirectFileAppendAnalyzer, DefaultVerifier>
		{
			TestCode = fixture,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		test.CompilerDiagnostics = CompilerDiagnostics.None;
		await test.RunAsync();
	}

	private static DiagnosticResult Diagnostic(int line, int column, string apiName)
		=> new DiagnosticResult(DirectFileAppendAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(apiName);

	// ---- REQ-1: POSITIVE — fires at the direct append ----

	[Fact]
	public async Task DirectAppend_OrdinaryType_Fires()
	{
		// Decl spans 11 content lines (1-11); the consumer string's own leading newline is line 12, so the
		// consumer's `using System.IO;` is absolute line 13. File.AppendAllText is the 7th consumer line
		// after its leading blank → absolute line 19, column 9 (4-space indent inside the method).
		var consumer = @"
using System.IO;

public class Repo
{
    public void Log(string finalPath)
    {
        File.AppendAllText(finalPath, ""x"");
    }
}";

		await VerifyAsync(consumer, Diagnostic(19, 9, "AppendAllText"));
	}

	// ---- REQ-2: NEGATIVES — all silent ----

	[Fact]
	public async Task SanctionedHelper_AppendWithRetry_Silent()
	{
		// (a) The sanctioned helper call — not a System.IO.File member, never matched.
		var consumer = @"
using NotNot.Storage;

public class Repo
{
    public void Log(string finalPath)
    {
        AtomicFileWriter.AppendWithRetry(finalPath, ""x"");
    }
}";

		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task InsideAtomicFileWriter_ContainingTypeExemption_Silent()
	{
		// (b) A File.AppendAllText declared INSIDE NotNot.Storage.AtomicFileWriter — the sanctioned impl;
		// exempted by containing-type. This fixture is a STANDALONE compilation (NOT prefixed by the shared
		// Decl stub, which already owns the NotNot.Storage.AtomicFileWriter type name) so its ONLY type IS
		// the exempt type — the append inside it must stay silent.
		var fixture = @"
using System.IO;

namespace NotNot.Storage
{
    public static class AtomicFileWriter
    {
        public static void AppendWithRetry(string finalPath, string contents)
        {
            File.AppendAllText(finalPath, contents);
        }
    }
}";

		await VerifyStandaloneAsync(fixture);
	}

	[Fact]
	public async Task PragmaSuppressed_Append_Silent()
	{
		// (c) A #pragma warning disable NN_R008-wrapped append — suppressed at the call.
		var consumer = @"
using System.IO;

public class Repo
{
    public void Log(string finalPath)
    {
#pragma warning disable NN_R008
        File.AppendAllText(finalPath, ""x"");
#pragma warning restore NN_R008
    }
}";

		await VerifyAsync(consumer);
	}

	[Fact]
	public async Task DirectFinalWrite_NotAppend_Silent()
	{
		// (d) A direct full-file write — not an append (out of scope).
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

	[Fact]
	public async Task FileMoveOverwrite_NN_R007Domain_Silent()
	{
		// (e) File.Move(temp, final, overwrite: true) — NN_R007's temp-then-rename domain, not NN_R008.
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

		await VerifyAsync(consumer);
	}
}
