using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Conventions;
using Xunit;

namespace NotNot.Analyzers.Tests.Conventions;

/// <summary>
/// Tests for RefVarNamingCodeFixProvider.
/// Verifies that code fixes correctly add r_ prefix and rename all references.
/// </summary>
public class RefVarNamingCodeFixTests
{
	private static async Task VerifyCodeFixAsync(string source, string fixedSource)
	{
		var test = new CSharpCodeFixTest<RefVarNamingAnalyzer, RefVarNamingCodeFixProvider, DefaultVerifier>
		{
			TestCode = source,
			FixedCode = fixedSource
		};

		await test.RunAsync();
	}

	[Fact]
	public async Task SimpleRename_ShouldAddPrefix()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var {|NN_C001:value|} = ref array[0];
        value = 5;
    }
}";

		string fixedSource = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var r_value = ref array[0];
        r_value = 5;
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task MultipleReferences_ShouldRenameAll()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var {|NN_C001:entity|} = ref array[0];
        entity = 100;
        Process(ref entity);
    }

    void Process(ref int value) { }
}";

		string fixedSource = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref var r_entity = ref array[0];
        r_entity = 100;
        Process(ref r_entity);
    }

    void Process(ref int value) { }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task ForeachRefVar_ShouldAddPrefix()
	{
		string source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Span<int> span = stackalloc int[] { 1, 2, 3 };
        foreach (ref var {|NN_C001:item|} in span)
        {
            item++;
        }
    }
}";

		string fixedSource = @"
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

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task RefReadonlyVar_ShouldAddPrefix()
	{
		string source = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref readonly var {|NN_C001:value|} = ref array[0];
        int copy = value;
    }
}";

		string fixedSource = @"
public class TestClass
{
    public void TestMethod()
    {
        int[] array = { 1, 2, 3 };
        ref readonly var r_value = ref array[0];
        int copy = r_value;
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}
}
