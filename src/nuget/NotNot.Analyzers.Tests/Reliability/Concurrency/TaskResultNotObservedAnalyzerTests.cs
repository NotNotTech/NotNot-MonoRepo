using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;
using NotNot.Analyzers.Tests.TestHelpers;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability.Concurrency;

/// <summary>
/// Comprehensive tests for TaskResultNotObservedAnalyzer (NN_R002)
/// Verifies that Task<T> results are properly observed after being awaited
/// </summary>
public class TaskResultNotObservedAnalyzerTests
{
	/// <summary>
	/// Analyzer verifier shortcut
	/// </summary>
	private static readonly CSharpAnalyzerTest<TaskResultNotObservedAnalyzer, DefaultVerifier> TestVerifier = new();

	private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<TaskResultNotObservedAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};
		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	[Fact]
	public async Task TaskResultNotObserved_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:await GetBoolAsync()|};  // Should trigger NN_R002 - result not observed
    }
    
    private async Task<bool> GetBoolAsync() => await Task.FromResult(true);
}";

		var expected = AnalyzerTestHelper.TaskResultNotObservedDiagnostic(8, 9, "GetBoolAsync()");

		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task TaskResultAssignedToVariable_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        var result = await GetBoolAsync(); // Result observed - should not trigger
        Console.WriteLine(result);
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultReturned_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task<bool> TestMethod()
    {
        return await GetBoolAsync(); // Result returned - should not trigger
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultUsedInExpression_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        if (await GetBoolAsync()) // Result used in condition - should not trigger
        {
            Console.WriteLine(""Success"");
        }
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultUsedInMethodCall_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        Console.WriteLine(await GetBoolAsync()); // Result used as argument - should not trigger
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task VoidTask_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        await DoWorkAsync(); // Void Task - should not trigger
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultUsedInArithmeticExpression_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        var sum = await GetIntAsync() + 10; // Result used in arithmetic - should not trigger
        Console.WriteLine(sum);
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task MultipleTaskResults_SomeNotObserved_ShouldReportOnlyUnobserved()
	{
		string source = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        var observed = await GetIntAsync();  // Result observed - should not trigger
        {|#0:await GetBoolAsync()|};         // Result not observed - should trigger NN_R002
        Console.WriteLine(observed);
    }
    
    private async Task<int> GetIntAsync() => await Task.FromResult(42);
    private async Task<bool> GetBoolAsync() => await Task.FromResult(true);
}";

		var expected = AnalyzerTestHelper.TaskResultNotObservedDiagnostic(10, 9, "GetBoolAsync()");

		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task TaskResultUsedInStringInterpolation_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        var message = $""Result: {await GetIntAsync()}""; // Result used in interpolation - should not trigger
        Console.WriteLine(message);
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultUsedInTernaryOperator_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        var message = await GetBoolAsync() ? ""True"" : ""False""; // Result used in ternary - should not trigger
        Console.WriteLine(message);
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ValueTaskResultNotObserved_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:await GetStringValueTaskAsync()|};  // Should trigger NN_R002 - ValueTask result not observed
    }
    
    private ValueTask<string> GetStringValueTaskAsync() => new ValueTask<string>(""test"");
}";

		var expected = AnalyzerTestHelper.TaskResultNotObservedDiagnostic(8, 9, "GetStringValueTaskAsync()");

		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task TaskResultAssignedToDiscard_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        _ = await GetBoolAsync(); // Explicitly discarded - should not trigger
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultInComplexExpression_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        var result = await GetBoolAsync() && await GetBoolAsync(); // Both results used - should not trigger
        Console.WriteLine(result);
    }");

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultUsedInLinq_ShouldNotReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using System.Linq;

public class TestClass
{
    public async Task TestMethod()
    {
        var numbers = new[] { 1, 2, 3 };
        var threshold = await GetIntAsync(); // Result used in LINQ - should not trigger
        var results = numbers.Where(x => x > threshold);
    }
    
    private async Task<int> GetIntAsync() => await Task.FromResult(42);
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskResultInAsyncLocalFunction_ShouldWork()
	{
		string source = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        async Task<bool> LocalFunction()
        {
            {|#0:await GetBoolAsync()|};  // Should trigger NN_R002 in local function
            return true;
        }
        
        var result = await LocalFunction();
        Console.WriteLine(result);
    }
    
    private async Task<bool> GetBoolAsync() => await Task.FromResult(true);
}";

		var expected = AnalyzerTestHelper.TaskResultNotObservedDiagnostic(11, 13, "GetBoolAsync()");

		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task TaskResultInLambda_ShouldWork()
	{
		string source = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        Func<Task> lambda = async () =>
        {
            {|#0:await GetBoolAsync()|};  // Should trigger NN_R002 in lambda
        };
        
        await lambda();
    }
    
    private async Task<bool> GetBoolAsync() => await Task.FromResult(true);
}";

		var expected = AnalyzerTestHelper.TaskResultNotObservedDiagnostic(11, 13, "GetBoolAsync()");

		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task TaskResultCastedToObject_ShouldNotReportDiagnostic()
	{
		string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        object result = await GetBoolAsync(); // Result cast to object - should not trigger
        Console.WriteLine(result);
    }");

		await VerifyAsync(source);
	}
}
