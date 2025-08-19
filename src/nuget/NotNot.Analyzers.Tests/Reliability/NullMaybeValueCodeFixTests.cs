using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability;

/// <summary>
/// Tests for NullMaybeValueCodeFixProvider
/// Verifies that the code fix correctly replaces null values in Maybe.Success with error result
/// </summary>
public class NullMaybeValueCodeFixTests
{
	private static async Task VerifyCodeFixAsync(string source, string fixedSource)
	{
		var test = new CSharpCodeFixTest<NullMaybeValueAnalyzer, NullMaybeValueCodeFixProvider, DefaultVerifier>
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
	public async Task FixNullLiteral_ReplacesWithError()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|NN_R003:null|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixNullVariable_ReplacesWithError()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        const string nullValue = null;
        return Maybe<string>.Success({|NN_R003:nullValue|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        const string nullValue = null;
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixDefaultLiteral_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|NN_R003:default|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixDefaultExpression_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|NN_R003:default(string)|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixInComplexExpression_ReplacesEntireCall()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        var result = Maybe<string>.Success({|NN_R003:null|});
        return result;
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        var result = Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
        return result;
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixInIfStatement_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        if (condition)
            return Maybe<string>.Success({|NN_R003:null|});
        
        return Maybe<string>.Success(""value"");
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        if (condition)
            return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
        
        return Maybe<string>.Success(""value"");
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixGenericMethod_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<T> TestMethod<T>() where T : class
    {
        return Maybe<T>.Success({|NN_R003:null|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<T> TestMethod<T>() where T : class
    {
        return Maybe<T>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixComplexType_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;
using System.Collections.Generic;

public class TestClass
{
    public Maybe<List<string>> TestMethod()
    {
        return Maybe<List<string>>.Success({|NN_R003:null|});
    }
}";

		string fixedSource = @"
using NotNot;
using System.Collections.Generic;

public class TestClass
{
    public Maybe<List<string>> TestMethod()
    {
        return Maybe<List<string>>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixInTernaryExpression_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        return condition ? Maybe<string>.Success({|NN_R003:null|}) : Maybe<string>.Success(""value"");
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        return condition ? Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" }) : Maybe<string>.Success(""value"");
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixInLambda_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;
using System;

public class TestClass
{
    public void TestMethod()
    {
        Func<Maybe<string>> func = () => Maybe<string>.Success({|NN_R003:null|});
    }
}";

		string fixedSource = @"
using NotNot;
using System;

public class TestClass
{
    public void TestMethod()
    {
        Func<Maybe<string>> func = () => Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixNestedCall_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public void TestMethod()
    {
        ProcessMaybe(Maybe<string>.Success({|NN_R003:null|}));
    }

    private void ProcessMaybe(Maybe<string> maybe) { }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public void TestMethod()
    {
        ProcessMaybe(Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" }));
    }

    private void ProcessMaybe(Maybe<string> maybe) { }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithConstField_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public class TestClass
{
    private const string NullField = null;

    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|NN_R003:NullField|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    private const string NullField = null;

    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixStaticMethodCall_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;

public static class Maybe
{
    public static Maybe<T> Success<T>(T value) => default;
}

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe.Success<string>({|NN_R003:null|});
    }
}";

		string fixedSource = @"
using NotNot;

public static class Maybe
{
    public static Maybe<T> Success<T>(T value) => default;
}

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixInAsyncMethod_ReplacesWithSuccessResult()
	{
		string source = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<string>> TestMethod()
    {
        await Task.Delay(1);
        return Maybe<string>.Success({|NN_R003:null|});
    }
}";

		string fixedSource = @"
using NotNot;
using System.Threading.Tasks;

public class TestClass
{
    public async Task<Maybe<string>> TestMethod()
    {
        await Task.Delay(1);
        return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixMultipleOccurrences_ReplacesAll()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        if (condition)
            return Maybe<string>.Success({|NN_R003:null|});
        else
            return Maybe<string>.Success({|NN_R003:default|});
    }
}";

		string fixedSource = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        if (condition)
            return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
        else
            return Maybe<string>.Error(new Problem { Title = ""Null value not allowed"" });
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}
}
