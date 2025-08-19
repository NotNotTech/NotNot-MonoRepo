using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability;

/// <summary>
/// Tests for ToMaybeExceptionCodeFixProvider
/// Verifies that the code fix correctly adds _ToMaybe() to async operations for exception handling
/// </summary>
public class ToMaybeExceptionCodeFixTests
{
	private static async Task VerifyCodeFixAsync(string source, string fixedSource)
	{
		var test = new CSharpCodeFixTest<ToMaybeExceptionAnalyzer, ToMaybeExceptionCodeFixProvider, DefaultVerifier>
		{
			TestCode = source,
			FixedCode = fixedSource
		};

		// Add ASP.NET Core and Maybe stubs
		var stubs = @"
using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Mvc
{
    public class ControllerBase { }
    public class Controller : ControllerBase { }
    
    [AttributeUsage(AttributeTargets.Class)]
    public class ApiControllerAttribute : Attribute { }
    
    public interface IActionResult { }
    public class ActionResult : IActionResult { }
    public class ActionResult<T> : ActionResult 
    {
        public static implicit operator ActionResult<T>(T value) => new ActionResult<T>();
    }
}

namespace NotNot
{
    public struct Maybe
    {
        public bool IsSuccess { get; }
        public Problem? Problem { get; }
        public static Maybe SuccessResult() => default;
        public static Maybe Error(Problem problem) => default;
    }

    public struct Maybe<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public Problem? Problem { get; }
        public static Maybe<T> Success(T value) => default;
        public static Maybe<T> Error(Problem problem) => default;
        public static implicit operator Maybe<T>(T value) => Success(value);
    }

    public class Problem
    {
        public string Title { get; set; }
    }
}

public static class MaybeExtensions
{
    public static async Task<NotNot.Maybe<T>> _ToMaybe<T>(this Task<T> task) => default;
    public static async Task<NotNot.Maybe> _ToMaybe(this Task task) => default;
    public static async ValueTask<NotNot.Maybe<T>> _ToMaybe<T>(this ValueTask<T> task) => default;
    public static async Task<NotNot.Maybe<T>> _ToMaybe<T>(this System.Runtime.CompilerServices.ConfiguredTaskAwaitable<T> task) => default;
    public static async Task<NotNot.Maybe> _ToMaybe(this System.Runtime.CompilerServices.ConfiguredTaskAwaitable task) => default;
    public static async ValueTask<NotNot.Maybe<T>> _ToMaybe<T>(this System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<T> task) => default;
    public static async ValueTask<NotNot.Maybe> _ToMaybe(this System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable task) => default;
}";

		test.TestState.Sources.Add(stubs);
		test.FixedState.Sources.Add(stubs);

		await test.RunAsync();
	}

	[Fact]
	public async Task FixSimpleReturn_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData()
    {
        return await GetDataInternal();
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData()
    {
        return await GetDataInternal().ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixExpressionBody_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public {|NN_R004:Task<string>|} GetData() => GetDataInternal();

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public Task<NotNot.Maybe<string>> GetData() => GetDataInternal().ConfigureAwait(false)._ToMaybe();

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithConfigureAwait_OnlyAddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData()
    {
        return await GetDataInternal().ConfigureAwait(false);
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData()
    {
        return await GetDataInternal().ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixMultipleReturns_AddsToMaybeToAll()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData(bool condition)
    {
        if (condition)
            return await GetDataInternal();
            
        return await GetAlternativeData();
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
    private Task<string> GetAlternativeData() => Task.FromResult(""alternative"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData(bool condition)
    {
        if (condition)
            return await GetDataInternal().ConfigureAwait(false)._ToMaybe();
            
        return await GetAlternativeData().ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
    private Task<string> GetAlternativeData() => Task.FromResult(""alternative"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixValueTask_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:ValueTask<int>|} GetCount()
    {
        return await GetCountInternal();
    }

    private ValueTask<int> GetCountInternal() => new ValueTask<int>(42);
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async ValueTask<NotNot.Maybe<int>> GetCount()
    {
        return await GetCountInternal().ConfigureAwait(false)._ToMaybe();
    }

    private ValueTask<int> GetCountInternal() => new ValueTask<int>(42);
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixTaskFromResult_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public {|NN_R004:Task<string>|} GetData()
    {
        return Task.FromResult(""data"");
    }
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public Task<NotNot.Maybe<string>> GetData()
    {
        return Task.FromResult(""data"").ConfigureAwait(false)._ToMaybe();
    }
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixComplexExpression_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData(bool useCache)
    {
        return await (useCache ? GetCachedData() : GetFreshData());
    }

    private Task<string> GetCachedData() => Task.FromResult(""cached"");
    private Task<string> GetFreshData() => Task.FromResult(""fresh"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData(bool useCache)
    {
        return await (useCache ? GetCachedData() : GetFreshData()).ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetCachedData() => Task.FromResult(""cached"");
    private Task<string> GetFreshData() => Task.FromResult(""fresh"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithLocalVariable_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData()
    {
        var task = GetDataInternal();
        return await task;
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData()
    {
        var task = GetDataInternal();
        return await task.ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixNestedReturn_AddsToMaybe()
	{
		string source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData(bool condition)
    {
        if (condition)
        {
            if (DateTime.Now.Second % 2 == 0)
            {
                return await GetEvenData();
            }
            return await GetOddData();
        }
        return await GetDefaultData();
    }

    private Task<string> GetEvenData() => Task.FromResult(""even"");
    private Task<string> GetOddData() => Task.FromResult(""odd"");
    private Task<string> GetDefaultData() => Task.FromResult(""default"");
}";

		string fixedSource = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData(bool condition)
    {
        if (condition)
        {
            if (DateTime.Now.Second % 2 == 0)
            {
                return await GetEvenData().ConfigureAwait(false)._ToMaybe();
            }
            return await GetOddData().ConfigureAwait(false)._ToMaybe();
        }
        return await GetDefaultData().ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetEvenData() => Task.FromResult(""even"");
    private Task<string> GetOddData() => Task.FromResult(""odd"");
    private Task<string> GetDefaultData() => Task.FromResult(""default"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task SkipNonAsyncReturn_OnlyFixAsyncOnes()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData(bool useAsync)
    {
        if (useAsync)
            return await GetAsyncData();
        
        return ""sync-data"";
    }

    private Task<string> GetAsyncData() => Task.FromResult(""async-data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData(bool useAsync)
    {
        if (useAsync)
            return await GetAsyncData().ConfigureAwait(false)._ToMaybe();
        
        return ""sync-data"";
    }

    private Task<string> GetAsyncData() => Task.FromResult(""async-data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixActionResult_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<ActionResult<string>>|} GetData()
    {
        return await GetDataInternal();
    }

    private Task<ActionResult<string>> GetDataInternal() 
        => Task.FromResult<ActionResult<string>>(""data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<ActionResult<string>>> GetData()
    {
        return await GetDataInternal().ConfigureAwait(false)._ToMaybe();
    }

    private Task<ActionResult<string>> GetDataInternal() 
        => Task.FromResult<ActionResult<string>>(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithCancellationToken_AddsToMaybe()
	{
		string source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData(CancellationToken ct)
    {
        return await GetDataInternal(ct);
    }

    private Task<string> GetDataInternal(CancellationToken ct) 
        => Task.FromResult(""data"");
}";

		string fixedSource = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData(CancellationToken ct)
    {
        return await GetDataInternal(ct).ConfigureAwait(false)._ToMaybe();
    }

    private Task<string> GetDataInternal(CancellationToken ct) 
        => Task.FromResult(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixGenericMethod_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<T>|} GetData<T>() where T : new()
    {
        return await GetDataInternal<T>();
    }

    private Task<T> GetDataInternal<T>() where T : new()
        => Task.FromResult(new T());
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<T>> GetData<T>() where T : new()
    {
        return await GetDataInternal<T>().ConfigureAwait(false)._ToMaybe();
    }

    private Task<T> GetDataInternal<T>() where T : new()
        => Task.FromResult(new T());
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}

	[Fact]
	public async Task FixWithAwaitInCondition_AddsToMaybe()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|NN_R004:Task<string>|} GetData()
    {
        if (await CheckCondition())
            return await GetDataInternal();
        
        return ""default"";
    }

    private Task<bool> CheckCondition() => Task.FromResult(true);
    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		string fixedSource = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<NotNot.Maybe<string>> GetData()
    {
        if (await CheckCondition())
            return await GetDataInternal().ConfigureAwait(false)._ToMaybe();
        
        return ""default"";
    }

    private Task<bool> CheckCondition() => Task.FromResult(true);
    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyCodeFixAsync(source, fixedSource);
	}
}
