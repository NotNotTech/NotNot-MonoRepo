using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Architecture;

namespace NotNot.Analyzers.Tests.Architecture;

/// <summary>
/// Tests for ForbidApiExceptionCatchAnalyzer (NN_A004).
/// Verifies that catching Refit.ApiException is flagged as a warning while
/// catching other exception types is allowed.
/// </summary>
public class ForbidApiExceptionCatchAnalyzerTests
{
    private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ForbidApiExceptionCatchAnalyzer, DefaultVerifier>
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
        return new DiagnosticResult(ForbidApiExceptionCatchAnalyzer.DiagnosticId,
                Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(line, column);
    }

    [Fact]
    public async Task CatchApiException_ShouldReport()
    {
        var source = @"
using System;

namespace Refit
{
    public class ApiException : Exception { }
}

class TestClass
{
    void Method()
    {
        try { }
        catch (Refit.ApiException ex)
        {
            Console.WriteLine(ex);
        }
    }
}";
        await VerifyAsync(source, Diagnostic(14, 9));
    }

    [Fact]
    public async Task CatchApiExceptionWithoutVariable_ShouldReport()
    {
        var source = @"
using System;

namespace Refit
{
    public class ApiException : Exception { }
}

class TestClass
{
    void Method()
    {
        try { }
        catch (Refit.ApiException)
        {
        }
    }
}";
        await VerifyAsync(source, Diagnostic(14, 9));
    }

    [Fact]
    public async Task CatchDerivedApiException_ShouldReport()
    {
        var source = @"
using System;

namespace Refit
{
    public class ApiException : Exception { }
    public class ValidationApiException : ApiException { }
}

class TestClass
{
    void Method()
    {
        try { }
        catch (Refit.ValidationApiException ex)
        {
            Console.WriteLine(ex);
        }
    }
}";
        await VerifyAsync(source, Diagnostic(15, 9));
    }

    // --- Negative cases ---

    [Fact]
    public async Task CatchException_ShouldNotReport()
    {
        var source = @"
using System;

class TestClass
{
    void Method()
    {
        try { }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task CatchInvalidOperationException_ShouldNotReport()
    {
        var source = @"
using System;

class TestClass
{
    void Method()
    {
        try { }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine(ex);
        }
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task BareCatch_ShouldNotReport()
    {
        var source = @"
class TestClass
{
    void Method()
    {
        try { }
        catch
        {
        }
    }
}";
        await VerifyAsync(source);
    }
}
