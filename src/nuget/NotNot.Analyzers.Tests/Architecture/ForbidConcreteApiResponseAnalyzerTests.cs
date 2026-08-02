using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture;

namespace NotNot.Analyzers.Tests.Architecture;

/// <summary>
/// Tests for ForbidConcreteApiResponseAnalyzer (NN_A003).
/// Verifies that concrete Refit.ApiResponse&lt;T&gt; usage is flagged while
/// IApiResponse&lt;T&gt; (interface) usage is allowed.
/// </summary>
public class ForbidConcreteApiResponseAnalyzerTests
{
    private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ForbidConcreteApiResponseAnalyzer, DefaultVerifier>
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
        return new DiagnosticResult(ForbidConcreteApiResponseAnalyzer.DiagnosticId,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(line, column);
    }

    [Fact]
    public async Task VariableDeclaration_ConcreteApiResponse_ShouldReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    void Method()
    {
        Refit.ApiResponse<string> response = null;
    }
}";
        await VerifyAsync(source, Diagnostic(13, 9));
    }

    [Fact]
    public async Task ParameterType_ConcreteApiResponse_ShouldReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    void Method(Refit.ApiResponse<int> param) { }
}";
        await VerifyAsync(source, Diagnostic(11, 17));
    }

    [Fact]
    public async Task ReturnType_ConcreteApiResponse_ShouldReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    Refit.ApiResponse<string> Method() => null;
}";
        await VerifyAsync(source, Diagnostic(11, 5));
    }

    [Fact]
    public async Task PropertyType_ConcreteApiResponse_ShouldReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    Refit.ApiResponse<bool> Prop { get; set; }
}";
        await VerifyAsync(source, Diagnostic(11, 5));
    }

    [Fact]
    public async Task FieldType_ConcreteApiResponse_ShouldReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    Refit.ApiResponse<int> _field;
}";
        await VerifyAsync(source, Diagnostic(11, 5));
    }

    // --- Negative cases: IApiResponse (interface) should NOT be flagged ---

    [Fact]
    public async Task VariableDeclaration_IApiResponse_ShouldNotReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    void Method()
    {
        Refit.IApiResponse<string> response = null;
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParameterType_IApiResponse_ShouldNotReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    void Method(Refit.IApiResponse<int> param) { }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task ReturnType_IApiResponse_ShouldNotReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    Refit.IApiResponse<string> Method() => null;
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task NonGenericIApiResponse_ShouldNotReport()
    {
        var source = @"
namespace Refit
{
    public interface IApiResponse { }
    public interface IApiResponse<out T> : IApiResponse { }
    public class ApiResponse<T> : IApiResponse<T> { }
}

class TestClass
{
    void Method(Refit.IApiResponse param) { }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task UnrelatedType_ShouldNotReport()
    {
        var source = @"
class TestClass
{
    void Method()
    {
        string x = null;
        int y = 0;
    }
}";
        await VerifyAsync(source);
    }
}
