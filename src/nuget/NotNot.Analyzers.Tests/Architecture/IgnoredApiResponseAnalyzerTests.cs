using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture;

namespace NotNot.Analyzers.Tests.Architecture;

/// <summary>
/// Tests for IgnoredApiResponseAnalyzer (NN_A005).
/// Verifies that IApiResponse results from await expressions that are implicitly discarded
/// are flagged, while assigned or explicitly discarded results are allowed.
/// </summary>
public class IgnoredApiResponseAnalyzerTests
{
    private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<IgnoredApiResponseAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };
        test.CompilerDiagnostics = CompilerDiagnostics.None;

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }
        await test.RunAsync();
    }

    private static DiagnosticResult Diagnostic(int line, int column)
    {
        return new DiagnosticResult(IgnoredApiResponseAnalyzer.DiagnosticId,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(line, column);
    }

    [Fact]
    public async Task AwaitedIApiResponse_ImplicitlyDiscarded_ShouldReport()
    {
        var source = @"
using System.Threading.Tasks;

namespace Refit
{
    public interface IApiResponse { bool IsSuccessStatusCode { get; } }
    public interface IApiResponse<out T> : IApiResponse { T Content { get; } }
}

class TestClient
{
    public Task<Refit.IApiResponse<string>> MutateAsync() => Task.FromResult<Refit.IApiResponse<string>>(null);
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        await client.MutateAsync();
    }
}";
        await VerifyAsync(source, Diagnostic(20, 9));
    }

    [Fact]
    public async Task AwaitedIApiResponse_NonGeneric_ImplicitlyDiscarded_ShouldReport()
    {
        var source = @"
using System.Threading.Tasks;

namespace Refit
{
    public interface IApiResponse { bool IsSuccessStatusCode { get; } }
    public interface IApiResponse<out T> : IApiResponse { T Content { get; } }
}

class TestClient
{
    public Task<Refit.IApiResponse> MutateAsync() => Task.FromResult<Refit.IApiResponse>(null);
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        await client.MutateAsync();
    }
}";
        await VerifyAsync(source, Diagnostic(20, 9));
    }

    [Fact]
    public async Task AwaitedApiResponse_Concrete_ImplicitlyDiscarded_ShouldReport()
    {
        var source = @"
using System.Threading.Tasks;

namespace Refit
{
    public interface IApiResponse { bool IsSuccessStatusCode { get; } }
    public interface IApiResponse<out T> : IApiResponse { T Content { get; } }
    public class ApiResponse<T> : IApiResponse<T>
    {
        public bool IsSuccessStatusCode => true;
        public T Content => default(T);
    }
}

class TestClient
{
    public Task<Refit.ApiResponse<string>> MutateAsync() => Task.FromResult<Refit.ApiResponse<string>>(null);
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        await client.MutateAsync();
    }
}";
        // ApiResponse<T> implements IApiResponse<T> which implements IApiResponse
        await VerifyAsync(source, Diagnostic(25, 9));
    }

    // --- Negative cases: assigned or explicitly discarded ---

    [Fact]
    public async Task AwaitedIApiResponse_AssignedToVariable_ShouldNotReport()
    {
        var source = @"
using System.Threading.Tasks;

namespace Refit
{
    public interface IApiResponse { bool IsSuccessStatusCode { get; } }
    public interface IApiResponse<out T> : IApiResponse { T Content { get; } }
}

class TestClient
{
    public Task<Refit.IApiResponse<string>> MutateAsync() => Task.FromResult<Refit.IApiResponse<string>>(null);
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        var result = await client.MutateAsync();
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task AwaitedIApiResponse_ExplicitlyDiscarded_ShouldNotReport()
    {
        var source = @"
using System.Threading.Tasks;

namespace Refit
{
    public interface IApiResponse { bool IsSuccessStatusCode { get; } }
    public interface IApiResponse<out T> : IApiResponse { T Content { get; } }
}

class TestClient
{
    public Task<Refit.IApiResponse<string>> MutateAsync() => Task.FromResult<Refit.IApiResponse<string>>(null);
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        _ = await client.MutateAsync();
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task AwaitedTask_NoApiResponse_ShouldNotReport()
    {
        var source = @"
using System.Threading.Tasks;

class TestClass
{
    async Task Method()
    {
        await Task.Delay(1);
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task AwaitedTaskOfString_NoApiResponse_ShouldNotReport()
    {
        var source = @"
using System.Threading.Tasks;

class TestClient
{
    public Task<string> GetAsync() => Task.FromResult(""hello"");
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        await client.GetAsync();
    }
}";
        // string does not implement IApiResponse, so no diagnostic even though result is discarded
        await VerifyAsync(source);
    }

    [Fact]
    public async Task AwaitedIApiResponse_UsedInIfCondition_ShouldNotReport()
    {
        var source = @"
using System.Threading.Tasks;

namespace Refit
{
    public interface IApiResponse { bool IsSuccessStatusCode { get; } }
    public interface IApiResponse<out T> : IApiResponse { T Content { get; } }
}

class TestClient
{
    public Task<Refit.IApiResponse<string>> MutateAsync() => Task.FromResult<Refit.IApiResponse<string>>(null);
}

class TestClass
{
    async Task Method()
    {
        var client = new TestClient();
        var result = await client.MutateAsync();
        if (result.IsSuccessStatusCode) { }
    }
}";
        await VerifyAsync(source);
    }
}
