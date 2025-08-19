using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture;
using Xunit;

namespace NotNot.Analyzers.Tests.Architecture;

/// <summary>
/// Tests for MaybeReturnContractAnalyzer (NN_A001)
/// Verifies that controller actions in core feature namespaces return Maybe or Maybe&lt;T&gt;
/// </summary>
public class MaybeReturnContractAnalyzerTests
{
	private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<MaybeReturnContractAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};
		// Add ASP.NET Core references for controller types
		test.TestState.AdditionalReferences.Add(typeof(Microsoft.AspNetCore.Mvc.ControllerBase).Assembly);
		test.TestState.AdditionalReferences.Add(typeof(Microsoft.AspNetCore.Mvc.IActionResult).Assembly);

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}
		await test.RunAsync();
	}

	private static DiagnosticResult MaybeReturnDiagnostic(int line, int column, string methodName, string returnType)
	{
		return new DiagnosticResult(MaybeReturnContractAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(methodName, returnType);
	}

	[Fact]
	public async Task ControllerReturningIActionResult_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        public {|#0:IActionResult|} BadMethod()
        {
            return Ok();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(9, 30, "BadMethod", "IActionResult");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task ControllerReturningStatusCodeResult_ShouldReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        public {|#0:StatusCodeResult|} HeartbeatTemp()
        {
            return StatusCode(200);
        }
    }
}";

		var expected = MaybeReturnDiagnostic(8, 33, "HeartbeatTemp", "StatusCodeResult");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task ControllerReturningTaskIActionResult_ShouldReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.EzAccess.Api
{
    public class GroupController : ControllerBase
    {
        public async {|#0:Task<IActionResult>|} GetGroup()
        {
            await Task.Delay(1);
            return Ok();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(9, 42, "GetGroup", "Task<IActionResult>");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task ControllerReturningMaybe_ShouldNotReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class Maybe { }
    
    public class TestController : ControllerBase
    {
        public Maybe GoodMethod()
        {
            return new Maybe();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ControllerReturningMaybeGeneric_ShouldNotReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class Maybe<T> { }
    public class UserDto { }
    
    public class TestController : ControllerBase
    {
        public Maybe<UserDto> GetUser()
        {
            return new Maybe<UserDto>();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ControllerReturningTaskMaybe_ShouldNotReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.NewAudit.Api
{
    public class Maybe { }
    
    public class AuditController : ControllerBase
    {
        public async Task<Maybe> GetAuditLog()
        {
            await Task.Delay(1);
            return new Maybe();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ControllerReturningValueTaskMaybe_ShouldNotReportDiagnostic()
	{
		string source = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Localization.Api
{
    public class Maybe<T> { }
    
    public class LocalizationController : ControllerBase
    {
        public async ValueTask<Maybe<string>> GetTranslation()
        {
            await Task.Delay(1);
            return new Maybe<string>();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task PrivateMethod_ShouldNotReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        private IActionResult PrivateMethod()
        {
            return Ok();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ProtectedMethod_ShouldNotReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        protected IActionResult ProtectedMethod()
        {
            return Ok();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ControllerOutsideCoreNamespace_ShouldNotReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace SomeOtherNamespace
{
    public class TestController : ControllerBase
    {
        public IActionResult Method()
        {
            return Ok();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task NonControllerClass_ShouldNotReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class NotAController
    {
        public IActionResult Method()
        {
            return new OkResult();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ApiControllerAttribute_ShouldBeRecognized()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    [ApiController]
    public class AccountEndpoints
    {
        public {|#0:IActionResult|} GetAccount()
        {
            return new OkResult();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(9, 30, "GetAccount", "IActionResult");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task PersistenceApiNamespace_ShouldBeIncluded()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Persistence.Api
{
    public class PersistenceController : ControllerBase
    {
        public {|#0:IActionResult|} GetData()
        {
            return Ok();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(8, 30, "GetData", "IActionResult");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task MultipleReturnStatements_ShouldReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        public {|#0:IActionResult|} ConditionalMethod(bool condition)
        {
            if (condition)
                return Ok();
            else
                return BadRequest();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(8, 30, "ConditionalMethod", "IActionResult");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task ExpressionBodiedMember_ShouldReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        public {|#0:IActionResult|} GetStatus() => Ok();
    }
}";

		var expected = MaybeReturnDiagnostic(8, 30, "GetStatus", "IActionResult");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task NestedMaybeGeneric_ShouldNotReportDiagnostic()
	{
		string source = @"
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class Maybe<T> { }
    
    public class TestController : ControllerBase
    {
        public Maybe<List<string>> GetList()
        {
            return new Maybe<List<string>>();
        }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task ObjectReturn_ShouldReportDiagnostic()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public class TestController : ControllerBase
    {
        public {|#0:object|} GetObjectResult()
        {
            return new object();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(8, 23, "GetObjectResult", "object");
		await VerifyAsync(source, expected);
	}

	[Fact]
	public async Task InheritedController_ShouldBeRecognized()
	{
		string source = @"
using Microsoft.AspNetCore.Mvc;

namespace Cleartrix.Cloud.Feature.Account.Api
{
    public abstract class BaseController : ControllerBase { }
    
    public class DerivedController : BaseController
    {
        public {|#0:IActionResult|} GetData()
        {
            return Ok();
        }
    }
}";

		var expected = MaybeReturnDiagnostic(10, 30, "GetData", "IActionResult");
		await VerifyAsync(source, expected);
	}
}
