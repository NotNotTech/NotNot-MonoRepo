using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture;
using Xunit;

namespace NotNot.Analyzers.Tests.Architecture;

/// <summary>
/// Tests for DirectMaybeReturnCodeFixProvider
/// Verifies that the code fix correctly simplifies redundant Maybe reconstruction
/// </summary>
public class DirectMaybeReturnCodeFixTests
{
	private static async Task VerifyCodeFixAsync(string source, string fixedSource)
	{
		var test = new CSharpCodeFixTest<DirectMaybeReturnAnalyzer, DirectMaybeReturnCodeFixProvider, DefaultVerifier>
		{
			TestCode = source,
			FixedCode = fixedSource
		};

		// Add Maybe type stub for testing
		var maybeStub = @"
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
}";

		test.TestState.Sources.Add(maybeStub);
		test.FixedState.Sources.Add(maybeStub);

		await test.RunAsync();
	}

	[Fact]
	public async Task FixRedundantMaybeReconstruction_NonGeneric()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|NN_A002:if (!result.IsSuccess)
            return result.Problem!;|}

        return new Maybe();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		string fixedSource = @"
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

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixRedundantMaybeReconstruction_Generic()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<string>> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|NN_A002:if (!result.IsSuccess)
            return result.Problem!;|}

        return Maybe<string>.Success(result.Value);
    }

    private Task<Maybe<string>> GetMaybeAsync() => Task.FromResult(Maybe<string>.Success(""test""));
}";

		string fixedSource = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<string>> TestMethod()
    {
        var result = await GetMaybeAsync();
        return result;
    }

    private Task<Maybe<string>> GetMaybeAsync() => Task.FromResult(Maybe<string>.Success(""test""));
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixRedundantMaybeReconstruction_WithBlock()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|NN_A002:if (!result.IsSuccess)
        {
            return result.Problem!;
        }|}

        return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		string fixedSource = @"
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

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithEqualsFalsePattern()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod()
    {
        var result = await GetMaybeAsync();
        {|NN_A002:if (result.IsSuccess == false)
            return result.Problem!;|}

        return new Maybe();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		string fixedSource = @"
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

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixNestedIfStatement()
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
            {|NN_A002:if (!result.IsSuccess)
                return result.Problem!;|}

            return new Maybe();
        }
        
        return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		string fixedSource = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe> TestMethod(bool condition)
    {
        if (condition)
        {
            var result = await GetMaybeAsync();
            return result;
        }
        
        return Maybe.SuccessResult();
    }

    private Task<Maybe> GetMaybeAsync() => Task.FromResult(Maybe.SuccessResult());
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithDifferentVariableName()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<int>> TestMethod()
    {
        var response = await GetMaybeAsync();
        {|NN_A002:if (!response.IsSuccess)
            return response.Problem!;|}

        return Maybe<int>.Success(response.Value);
    }

    private Task<Maybe<int>> GetMaybeAsync() => Task.FromResult(Maybe<int>.Success(42));
}";

		string fixedSource = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<int>> TestMethod()
    {
        var response = await GetMaybeAsync();
        return response;
    }

    private Task<Maybe<int>> GetMaybeAsync() => Task.FromResult(Maybe<int>.Success(42));
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixSynchronousMethod()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe TestMethod()
    {
        var result = GetMaybe();
        {|NN_A002:if (!result.IsSuccess)
            return result.Problem!;|}

        return new Maybe();
    }

    private Maybe GetMaybe() => Maybe.SuccessResult();
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe TestMethod()
    {
        var result = GetMaybe();
        return result;
    }

    private Maybe GetMaybe() => Maybe.SuccessResult();
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}
}
