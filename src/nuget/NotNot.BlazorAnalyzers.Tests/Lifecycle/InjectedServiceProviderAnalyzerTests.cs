using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for InjectedServiceProviderAnalyzer (NNB017).
/// Verifies detection of [Inject] IServiceProvider properties in Blazor components.
/// </summary>
public class InjectedServiceProviderAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<InjectedServiceProviderAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        // Add Blazor type stubs
        test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    private static DiagnosticResult InjectedServiceProviderDiagnostic(string propertyName)
    {
        return new DiagnosticResult(InjectedServiceProviderAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(propertyName);
    }

    [Fact]
    public async Task InjectServiceProvider_InComponentBase_Reports()
    {
        var source = @"
using System;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    {|#0:[Inject] private IServiceProvider SP { get; set; }|}
}";

        await VerifyAnalyzerAsync(source, InjectedServiceProviderDiagnostic("SP"));
    }

    [Fact]
    public async Task InjectTypedService_InComponentBase_NoReport()
    {
        var source = @"
using System;
using Microsoft.AspNetCore.Components;

public interface IMyService { }

public class TestComponent : ComponentBase
{
    [Inject] private IMyService Svc { get; set; }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ServiceProviderProperty_WithoutInject_NoReport()
    {
        var source = @"
using System;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    private IServiceProvider SP { get; set; }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task InjectServiceProvider_InNonComponent_NoReport()
    {
        var source = @"
using System;
using Microsoft.AspNetCore.Components;

public class RegularClass
{
    [Inject] public IServiceProvider SP { get; set; }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task InjectServiceProvider_InIndirectComponentBase_Reports()
    {
        var source = @"
using System;
using Microsoft.AspNetCore.Components;

public class MyBaseComponent : ComponentBase { }

public class TestComponent : MyBaseComponent
{
    {|#0:[Inject] private IServiceProvider SP { get; set; }|}
}";

        await VerifyAnalyzerAsync(source, InjectedServiceProviderDiagnostic("SP"));
    }
}
