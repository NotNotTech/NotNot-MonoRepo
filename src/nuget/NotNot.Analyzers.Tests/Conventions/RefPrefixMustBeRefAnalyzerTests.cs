using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for RefPrefixMustBeRefAnalyzer (NN_C002).
/// Verifies detection of non-ref variables incorrectly using r_ prefix.
/// </summary>
public class RefPrefixMustBeRefAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<RefPrefixMustBeRefAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	private static DiagnosticResult RefPrefixMustBeRefDiagnostic(int line, int column, string varName)
	{
		return new DiagnosticResult(RefPrefixMustBeRefAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(varName);
	}

	#region Basic Variable Tests

	[Fact]
	public async Task NonRefVariableWithRPrefix_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        var {|NN_C002:r_tile|} = 22;
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task RefVariableWithRPrefix_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var r_value = ref array[0]; // Correct - ref with r_ prefix
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task RegularVariableWithoutRPrefix_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        var value = 42; // OK - no r_ prefix, not ref
        var tile = 100; // OK
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Foreach Tests

	[Fact]
	public async Task NonRefForeachWithRPrefix_ShouldReportDiagnostic()
	{
		string source = @"
using System.Collections.Generic;

public class TestClass
{
    public void TestMethod()
    {
        var items = new List<int> { 1, 2, 3 };
        foreach (var {|NN_C002:r_item|} in items) // Not ref - shouldn't use r_
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task RefForeachWithRPrefix_ShouldNotReportDiagnostic()
	{
		string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Span<int> span = stackalloc int[] { 1, 2, 3 };
        foreach (ref var r_item in span) // Correct - ref with r_
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Parameter Tests

	[Fact]
	public async Task NonRefParameterWithRPrefix_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod(int {|NN_C002:r_value|})
    {
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task RefParameterWithRPrefix_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod(ref int r_value) // Correct
    {
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task OutParameterWithRPrefix_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod(out int r_value) // OK - out is ref-like
    {
        r_value = 0;
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task InParameterWithRPrefix_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod(in int r_value) // OK - in is ref readonly
    {
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Field Tests

	[Fact]
	public async Task FieldWithRPrefix_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    private int {|NN_C002:r_value|} = 42; // Fields can't be ref (except ref fields in ref structs)
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Multiple Variables

	[Fact]
	public async Task MultipleNonRefVariablesWithRPrefix_ShouldReportMultipleDiagnostics()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int {|NN_C002:r_a|} = 1, {|NN_C002:r_b|} = 2; // Explicit type allows multiple declarators
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion
}
