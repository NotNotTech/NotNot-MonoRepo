using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for RefVarNamingAnalyzer (NN_C001).
/// Verifies detection of ref var declarations without r_ prefix.
/// </summary>
public class RefVarNamingAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<RefVarNamingAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	private static DiagnosticResult RefVarNamingDiagnostic(int line, int column, string varName, string suggestedName)
	{
		return new DiagnosticResult(RefVarNamingAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(varName, suggestedName);
	}

	#region Basic Ref Var Tests

	[Fact]
	public async Task NonCompliantRefVar_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var {|#0:value|} = ref array[0];
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(7, 17, "value", "r_value"));
	}

	[Fact]
	public async Task CompliantRefVar_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var r_value = ref array[0];
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task RegularVar_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        var value = 42;
        var r_value = 43; // OK even with prefix - not ref
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task RefWithExplicitType_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref int value = ref array[0]; // Out of scope - not var
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Ref Readonly Tests

	[Fact]
	public async Task NonCompliantRefReadonlyVar_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref readonly var {|#0:value|} = ref array[0];
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(7, 26, "value", "r_value"));
	}

	[Fact]
	public async Task CompliantRefReadonlyVar_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref readonly var r_value = ref array[0];
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	#endregion

	#region Foreach Ref Var Tests

	[Fact]
	public async Task NonCompliantForeachRefVar_ShouldReportDiagnostic()
	{
		string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Span<int> span = stackalloc int[] { 1, 2, 3 };
        foreach (ref var {|#0:item|} in span)
        {
            item++;
        }
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(9, 26, "item", "r_item"));
	}

	[Fact]
	public async Task CompliantForeachRefVar_ShouldNotReportDiagnostic()
	{
		string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Span<int> span = stackalloc int[] { 1, 2, 3 };
        foreach (ref var r_item in span)
        {
            r_item++;
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ForeachRefReadonlyVar_ShouldReportDiagnostic()
	{
		string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Span<int> span = stackalloc int[] { 1, 2, 3 };
        foreach (ref readonly var {|#0:item|} in span)
        {
            Console.WriteLine(item);
        }
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(9, 35, "item", "r_item"));
	}

	#endregion

	#region Case Sensitivity Tests

	[Fact]
	public async Task UppercaseRPrefix_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var {|#0:R_foo|} = ref array[0]; // Uppercase R not allowed
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(7, 17, "R_foo", "r_R_foo")); // Note: fix prepends r_, doesn't replace
	}

	[Fact]
	public async Task LowercasePrefixWithPascalCase_ShouldNotReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var r_Foo = ref array[0]; // Lowercase prefix, PascalCase name - OK
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task WrongPrefix_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var {|#0:Ref_foo|} = ref array[0];
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(7, 17, "Ref_foo", "r_Ref_foo"));
	}

	#endregion

	#region Local Function Tests

	[Fact]
	public async Task LocalFunctionRefVar_ShouldReportDiagnostic()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };

        void ProcessRef()
        {
            ref var {|#0:value|} = ref array[0];
        }

        ProcessRef();
    }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(10, 21, "value", "r_value"));
	}

	#endregion

	#region Multiple References Tests

	[Fact]
	public async Task MultipleReferences_AllShouldBeRenamed()
	{
		// This test validates that the analyzer detects the issue
		// The code fix test will validate renaming behavior
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var {|#0:entity|} = ref array[0];
        entity = 100;
        Process(ref entity);
    }

    void Process(ref int value) { }
}";

		await VerifyAnalyzerAsync(source,
			RefVarNamingDiagnostic(7, 17, "entity", "r_entity"));
	}

	#endregion
}
