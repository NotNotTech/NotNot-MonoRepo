using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for RefPrefixMustBeRefCodeFixProvider.
/// Verifies that code fixes correctly remove r_ prefix from non-ref variables.
/// </summary>
public class RefPrefixMustBeRefCodeFixTests
{
	private static async Task VerifyCodeFixAsync(string source, string fixedSource)
	{
		var test = new CSharpCodeFixTest<RefPrefixMustBeRefAnalyzer, RefPrefixMustBeRefCodeFixProvider, DefaultVerifier>
		{
			TestCode = source,
			FixedCode = fixedSource
		};

		await test.RunAsync();
	}

	[Fact]
	public async Task RemovePrefixFromLocalVariable()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        var {|NN_C002:r_tile|} = 22;
        r_tile++;
    }
}";

		string fixedSource = @"
public class TestClass
{
    public void TestMethod()
    {
        var tile = 22;
        tile++;
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task RemovePrefixFromForeach()
	{
		string source = @"
using System.Collections.Generic;

public class TestClass
{
    public void TestMethod()
    {
        var items = new List<int> { 1, 2, 3 };
        foreach (var {|NN_C002:r_item|} in items)
        {
            System.Console.WriteLine(r_item);
        }
    }
}";

		string fixedSource = @"
using System.Collections.Generic;

public class TestClass
{
    public void TestMethod()
    {
        var items = new List<int> { 1, 2, 3 };
        foreach (var item in items)
        {
            System.Console.WriteLine(item);
        }
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task RemovePrefixFromParameter()
	{
		string source = @"
public class TestClass
{
    public void TestMethod(int {|NN_C002:r_value|})
    {
        System.Console.WriteLine(r_value);
    }
}";

		string fixedSource = @"
public class TestClass
{
    public void TestMethod(int value)
    {
        System.Console.WriteLine(value);
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task RemovePrefixUpdatesAllReferences()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        var {|NN_C002:r_count|} = 0;
        r_count++;
        r_count += 5;
        System.Console.WriteLine(r_count);
    }
}";

		string fixedSource = @"
public class TestClass
{
    public void TestMethod()
    {
        var count = 0;
        count++;
        count += 5;
        System.Console.WriteLine(count);
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}
}
