using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability;

/// <summary>
/// Tests for ToMaybeExceptionAnalyzer (NN_R004)
/// Verifies detection of async methods in controllers that don't use _ToMaybe() for exception handling
/// </summary>
public class ToMaybeExceptionAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<ToMaybeExceptionAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};

		// Add ASP.NET Core stubs
		test.TestState.Sources.Add(@"
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
}");

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	private static DiagnosticResult ToMaybeDiagnostic(int line, int column, string methodName, string returnType)
	{
		return new DiagnosticResult(ToMaybeExceptionAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
			 .WithLocation(line, column)
			 .WithArguments(methodName, returnType);
	}

	[Fact]
	public async Task ControllerMethod_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:Task<string>|} GetData()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetData", "string"));
	}

	[Fact]
	public async Task ControllerMethod_WithToMaybe_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<string> GetData()
    {
        var result = await GetDataInternal()._ToMaybe();
        return result.Value;
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ControllerMethod_ReturningMaybe_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NotNot;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<Maybe<string>> GetData()
    {
        await Task.Delay(100);
        return Maybe<string>.Success(""data"");
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NonPublicMethod_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    private async Task<string> GetData()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NonControllerClass_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;

public class TestService
{
    public async Task<string> GetData()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task ControllerInheritance_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public class TestController : Controller
{
    public async {|#0:Task<string>|} GetData()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(7, 18, "GetData", "string"));
	}

	[Fact]
	public async Task ValueTaskReturn_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:ValueTask<int>|} GetCount()
    {
        await Task.Delay(100);
        return 42;
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetCount", "int"));
	}

	[Fact]
	public async Task ActionResultReturn_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:Task<ActionResult<string>>|} GetData()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetData", "ActionResult<string>"));
	}

	[Fact]
	public async Task ExpressionBody_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public {|#0:Task<string> GetData|}() => GetDataInternal();

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 12, "GetData", "string"));
	}

	[Fact]
	public async Task ExpressionBody_WithToMaybe_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NotNot;

[ApiController]
public class TestController : ControllerBase
{
    public Task<Maybe<string>> GetData() => GetDataInternal()._ToMaybe();

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task MultipleReturnStatements_NoToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:Task<string>|} GetData(bool condition)
    {
        if (condition)
        {
            await Task.Delay(50);
            return ""data1"";
        }
        
        await Task.Delay(100);
        return ""data2"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetData", "string"));
	}

	[Fact]
	public async Task MixedToMaybeUsage_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<string> GetData(bool condition)
    {
        if (condition)
        {
            var result = await GetDataInternal()._ToMaybe();
            return result.Value;
        }
        
        return ""default"";
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task GenericController_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController<T> : ControllerBase
{
    public async {|#0:Task<T>|} GetData()
    {
        await Task.Delay(100);
        return default(T);
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetData", "T"));
	}

	[Fact]
	public async Task SynchronousMethod_ShouldNotReport()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public string GetData()
    {
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task TaskWithoutGeneric_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task DoWork()
    {
        await Task.Delay(100);
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task IActionResultReturn_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:Task<IActionResult>|} GetData()
    {
        await Task.Delay(100);
        return new ActionResult();
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetData", "IActionResult"));
	}

	[Fact]
	public async Task NestedToMaybeCall_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<string> GetData()
    {
        var innerResult = await GetInnerData()._ToMaybe();
        if (!innerResult.IsSuccess)
            return null;
            
        var outerResult = await ProcessData(innerResult.Value)._ToMaybe();
        return outerResult.Value;
    }

    private Task<string> GetInnerData() => Task.FromResult(""inner"");
    private Task<string> ProcessData(string data) => Task.FromResult(data + ""_processed"");
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task InterfaceImplementation_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public interface IDataService
{
    Task<string> GetData();
}

[ApiController]
public class TestController : ControllerBase, IDataService
{
    public async {|#0:Task<string>|} GetData()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(13, 18, "GetData", "string"));
	}

	[Fact]
	public async Task OverriddenMethod_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

public abstract class BaseController : ControllerBase
{
    public abstract Task<string> GetData();
}

[ApiController]
public class TestController : BaseController
{
    public override async {|#0:Task<string> GetData|}()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(13, 27, "GetData", "string"));
	}

	[Fact]
	public async Task LambdaExpression_WithToMaybe_ShouldNotReport()
	{
		string source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<string> GetData()
    {
        Func<Task<string>> dataFunc = async () =>
        {
            await Task.Delay(100);
            return ""data"";
        };

        var result = await dataFunc()._ToMaybe();
        return result.Value;
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task LocalFunction_WithoutToMaybe_InController_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:Task<string>|} GetData()
    {
        return await GetDataLocal();

        async Task<string> GetDataLocal()
        {
            await Task.Delay(100);
            return ""data"";
        }
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 18, "GetData", "string"));
	}

	[Fact]
	public async Task PartialMethod_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public partial class TestController : ControllerBase
{
    public partial Task<string> GetData();
}

public partial class TestController
{
    public async partial {|#0:Task<string> GetData|}()
    {
        await Task.Delay(100);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(13, 26, "GetData", "string"));
	}

	[Fact]
	public async Task ConfigureAwaitWithToMaybe_ShouldNotReport()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NotNot;

[ApiController]
public class TestController : ControllerBase
{
    public async Task<string> GetData()
    {
        var result = await GetDataInternal().ConfigureAwait(false)._ToMaybe();
        return result.Value;
    }

    private Task<string> GetDataInternal() => Task.FromResult(""data"");
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task TaskFromResult_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public {|#0:Task<string> GetData|}()
    {
        return Task.FromResult(""data"");
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(8, 12, "GetData", "string"));
	}

	[Fact]
	public async Task CancellationToken_WithoutToMaybe_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public class TestController : ControllerBase
{
    public async {|#0:Task<string>|} GetData(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return ""data"";
    }
}";

		await VerifyAnalyzerAsync(source, ToMaybeDiagnostic(9, 18, "GetData", "string"));
	}
}
