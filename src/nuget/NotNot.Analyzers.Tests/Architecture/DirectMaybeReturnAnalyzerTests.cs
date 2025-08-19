using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture;
using Xunit;

namespace NotNot.Analyzers.Tests.Architecture;

/// <summary>
/// Tests for DirectMaybeReturnAnalyzer (NN_A002)
/// Verifies detection of redundant Maybe reconstruction patterns
/// </summary>
public class DirectMaybeReturnAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<DirectMaybeReturnAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};

		// Add Maybe type stub for testing
		test.TestState.Sources.Add(@"
using System;

namespace NotNot
{
    public struct Maybe
    {
        public bool IsSuccess { get; }
        public Problem? Problem { get; }
        public static Maybe SuccessResult() => default;
        public static Maybe Error(Problem problem) => default;
        public Maybe(Problem problem) { IsSuccess = false; Problem = problem; }
        public static implicit operator Maybe(Problem problem) => new Maybe(problem);
    }

    public struct Maybe<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public Problem? Problem { get; }
        public static Maybe<T> Success(T value) => default;
        public static Maybe<T> Error(Problem problem) => default;
        public static implicit operator Maybe<T>(Problem problem) => Error(problem);
    }

    public class Problem
    {
        public string Title { get; set; }
        public static Problem FromEx(Exception ex) => new Problem();
    }
}");

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	private static DiagnosticResult DirectReturnDiagnostic(int line, int column, string methodName)
	{
		return new DiagnosticResult(DirectMaybeReturnAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			 .WithLocation(line, column)
			 .WithArguments(methodName);
	}

	[Fact]
	public async Task RedundantMaybeReconstruction_NonGeneric_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|#0:if (!result.IsSuccess)
            return result.Problem!;|}

        return new Maybe();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(10, 9, "TestMethod"));
	}

	[Fact]
	public async Task RedundantMaybeReconstruction_Generic_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<string>> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|#0:if (!result.IsSuccess)
            return result.Problem!;|}

        return Maybe<string>.Success(result.Value);
    }

    private Task<Maybe<string>> GetMaybeAsync() => Task.FromResult(Maybe<string>.Success(""test""));
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(10, 9, "TestMethod"));
	}

	[Fact]
	public async Task RedundantMaybeReconstruction_WithBlock_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|#0:if (!result.IsSuccess)
        {
            return result.Problem!;
        }|}

        return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(10, 9, "TestMethod"));
	}

	[Fact]
	public async Task DirectMaybeReturn_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        return result;
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ValidTransformation_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<int>> TestMethod()
    {
        var result = await GetMaybeAsync();
        if (!result.IsSuccess)
            return result.Problem!;

        // Performing transformation, not just returning value
        var transformed = result.Value * 2;
        return Maybe<int>.Success(transformed);
    }

    private Task<Maybe<int>> GetMaybeAsync() => Task.FromResult(Maybe<int>.Success(5));
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NonMaybeReturnType_ShouldNotAnalyze()
	{
		string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public int TestMethod()
    {
        var result = GetValue();
        if (result < 0)
            return -1;

        return result;
    }

    private int GetValue() => 42;
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MaybeWithElseClause_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        if (!result.IsSuccess)
            return result.Problem!;
        else
            return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NestedIfStatements_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod(bool condition)
    {
        if (condition)
        {
            var result = await GetMaybeAsync();
            {|#0:if (!result.IsSuccess)
                return result.Problem!;|}

            return new Maybe();
        }
        
        return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(12, 13, "TestMethod"));
	}

	[Fact]
	public async Task EqualsFalsePattern_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|#0:if (result.IsSuccess == false)
            return result.Problem!;|}

        return new Maybe();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(10, 9, "TestMethod"));
	}

	[Fact]
	public async Task SynchronousMethod_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe TestMethod()
    {
        var result = GetMaybe();
        {|#0:if (!result.IsSuccess)
            return result.Problem!;|}

        return new Maybe();
    }

    private Maybe GetMaybe() => Maybe.SuccessResult();
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(9, 9, "TestMethod"));
	}

	[Fact]
	public async Task MultipleRedundantPatterns_ShouldReportMultipleDiagnostics()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod1()
    {
        var result = await GetMaybeAsync();
        {|#0:if (!result.IsSuccess)
            return result.Problem!;|}

        return new Maybe();
    }

    public async Task<Maybe> TestMethod2()
    {
        var result = await GetMaybeAsync();
        {|#1:if (!result.IsSuccess)
            return result.Problem!;|}

        return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source,
			 DirectReturnDiagnostic(10, 9, "TestMethod1"),
			 DirectReturnDiagnostic(19, 9, "TestMethod2"));
	}

	[Fact]
	public async Task ValueTask_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async ValueTask<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|#0:if (!result.IsSuccess)
            return result.Problem!;|}

        return new Maybe();
    }

    private ValueTask<Maybe> GetMaybeAsync() => new ValueTask<Maybe>(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(10, 9, "TestMethod"));
	}

	[Fact]
	public async Task ExpressionBodiedMethod_ShouldNotAnalyze()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public Task<Maybe> TestMethod() => GetMaybeAsync();

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task DifferentVariableNames_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<int>> TestMethod()
    {
        var response = await GetMaybeAsync();
        {|#0:if (!response.IsSuccess)
            return response.Problem!;|}

        return Maybe<int>.Success(response.Value);
    }

    private Task<Maybe<int>> GetMaybeAsync() => Task.FromResult(Maybe<int>.Success(42));
}";

		await VerifyAnalyzerAsync(source, DirectReturnDiagnostic(10, 9, "TestMethod"));
	}
}
