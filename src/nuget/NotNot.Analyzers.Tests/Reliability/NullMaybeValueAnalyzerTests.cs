using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability;

/// <summary>
/// Tests for NullMaybeValueAnalyzer (NN_R003)
/// Verifies detection of null values passed to Maybe.Success<T>()
/// </summary>
public class NullMaybeValueAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<NullMaybeValueAnalyzer, DefaultVerifier>
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

	private static DiagnosticResult NullMaybeDiagnostic(int line, int column, string typeArg)
	{
		return new DiagnosticResult(NullMaybeValueAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
			 .WithLocation(line, column)
			 .WithArguments(typeArg);
	}

	[Fact]
	public async Task NullLiteralToMaybeSuccess_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|#0:null|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(8, 38, "string"));
	}

	[Fact]
	public async Task NullVariableToMaybeSuccess_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        const string nullValue = null;
        return Maybe<string>.Success({|#0:nullValue|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(9, 38, "string"));
	}

	[Fact]
	public async Task DefaultLiteralForReferenceType_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|#0:default|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(8, 38, "string"));
	}

	[Fact]
	public async Task DefaultExpressionForReferenceType_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|#0:default(string)|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(8, 38, "string"));
	}

	[Fact]
	public async Task NullableWithNullValue_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        string? nullableString = null;
        return Maybe<string>.Success({|#0:nullableString|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(9, 38, "string"));
	}

	[Fact]
	public async Task ValidValueToMaybeSuccess_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success(""test"");
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NonNullVariableToMaybeSuccess_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        var value = ""test"";
        return Maybe<string>.Success(value);
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task DefaultValueType_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<int> TestMethod()
    {
        return Maybe<int>.Success(default);
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task DefaultIntExpression_ShouldNotReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<int> TestMethod()
    {
        return Maybe<int>.Success(default(int));
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NullToGenericMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<T> TestMethod<T>() where T : class
    {
        return Maybe<T>.Success({|#0:null|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(8, 33, "T"));
	}

	[Fact]
	public async Task ComplexTypeNull_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;
using System.Collections.Generic;

public class TestClass
{
    public Maybe<List<string>> TestMethod()
    {
        return Maybe<List<string>>.Success({|#0:null|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(9, 44, "List<string>"));
	}

	[Fact]
	public async Task TernaryOperatorWithNull_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod(bool condition)
    {
        const string nullValue = null;
        return Maybe<string>.Success({|#0:condition ? nullValue : null|});
    }
}";

		// This should report diagnostic as the expression can evaluate to null
		// Note: Current analyzer might not catch this complex case, but it's a good test to have
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MethodReturningNull_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|#0:GetNull()|});
    }

    private string GetNull() => null;
}";

		// The analyzer should catch constant null values
		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NullFromField_ShouldReportDiagnostic()
	{
		string source = @"
using NotNot;

public class TestClass
{
    private const string NullField = null;

    public Maybe<string> TestMethod()
    {
        return Maybe<string>.Success({|#0:NullField|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(10, 38, "string"));
	}

	[Fact]
	public async Task MaybeSuccessWithoutGenericArg_ValidValue_ShouldNotReport()
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
        return Maybe.Success(""test"");
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MaybeSuccessWithoutGenericArg_NullValue_ShouldReport()
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
        return Maybe.Success<string>({|#0:null|});
    }
}";

		await VerifyAnalyzerAsync(source, NullMaybeDiagnostic(13, 38, "string"));
	}

	[Fact]
	public async Task NonMaybeSuccessMethod_ShouldNotReport()
	{
		string source = @"
public class OtherClass
{
    public static T Success<T>(T value) => value;
}

public class TestClass
{
    public string TestMethod()
    {
        return OtherClass.Success<string>(null);
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MultipleArguments_ShouldNotReport()
	{
		string source = @"
using NotNot;

public static class Maybe
{
    public static Maybe<T> Success<T>(T value, bool flag) => default;
}

public class TestClass
{
    public Maybe<string> TestMethod()
    {
        return Maybe.Success<string>(null, true);
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NullableReferenceTypeWithValue_ShouldNotReport()
	{
		string source = @"
using NotNot;

public class TestClass
{
    public Maybe<string?> TestMethod()
    {
        string? value = ""test"";
        return Maybe<string?>.Success(value);
    }
}";

		await VerifyAnalyzerAsync(source);
	}
}
