using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for ServiceProviderInDisposalAnalyzer (NNB015).
/// Verifies detection of service resolution calls in Dispose/DisposeAsync methods.
/// </summary>
public class ServiceProviderInDisposalAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ServiceProviderInDisposalAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        // Add Blazor type stubs (includes IServiceProvider and DI extension stubs)
        test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    private static DiagnosticResult ServiceProviderDiagnostic(string callText, string methodName)
    {
        return new DiagnosticResult(ServiceProviderInDisposalAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments(callText, methodName);
    }

    [Fact]
    public async Task GetService_InDisposeAsync_Reports()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class SomeService { }

public class MyComponent : IAsyncDisposable
{
    private IServiceProvider _sp;

    public async ValueTask DisposeAsync()
    {
        var svc = {|#0:_sp.GetService<SomeService>()|};
    }
}";

        await VerifyAnalyzerAsync(source, ServiceProviderDiagnostic("_sp.GetService<SomeService>()", "DisposeAsync"));
    }

    [Fact]
    public async Task GetRequiredService_InDispose_Reports()
    {
        var source = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public class SomeService { }

public class MyComponent : IDisposable
{
    private IServiceProvider _sp;

    public void Dispose()
    {
        var svc = {|#0:_sp.GetRequiredService<SomeService>()|};
    }
}";

        await VerifyAnalyzerAsync(source, ServiceProviderDiagnostic("_sp.GetRequiredService<SomeService>()", "Dispose"));
    }

    [Fact]
    public async Task GetService_InOnInitializedAsync_NoReport()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

public class SomeService { }

public class MyComponent : ComponentBase, IAsyncDisposable
{
    private IServiceProvider _sp;

    protected override async Task OnInitializedAsync()
    {
        var svc = _sp.GetService<SomeService>();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() { }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task GetService_InRandomMethod_NoReport()
    {
        var source = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public class SomeService { }

public class MyComponent : IDisposable
{
    private IServiceProvider _sp;

    public void DoWork()
    {
        var svc = _sp.GetService<SomeService>();
    }

    public void Dispose() { }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task CustomGetService_NotServiceProvider_NoReport()
    {
        var source = @"
using System;

public class SomeService { }

public class CustomLocator
{
    public T GetService<T>() => default;
}

public class MyComponent : IDisposable
{
    private CustomLocator _locator;

    public void Dispose()
    {
        var svc = _locator.GetService<SomeService>();
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task CachedField_InDisposeAsync_NoReport()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class SomeService
{
    public void Cleanup() { }
}

public class MyComponent : IAsyncDisposable
{
    private IServiceProvider _sp;
    private SomeService _cached;

    public MyComponent()
    {
        _cached = _sp.GetService<SomeService>();
    }

    public async ValueTask DisposeAsync()
    {
        _cached.Cleanup();
        await Task.CompletedTask;
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NonDisposableClass_NoReport()
    {
        var source = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public class SomeService { }

public class NotDisposable
{
    private IServiceProvider _sp;

    public void Dispose()
    {
        var svc = _sp.GetService<SomeService>();
    }
}";

        await VerifyAnalyzerAsync(source);
    }
}
