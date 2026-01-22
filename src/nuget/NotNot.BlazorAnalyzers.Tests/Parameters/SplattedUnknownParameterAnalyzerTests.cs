using NotNot.BlazorAnalyzers.Parameters;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Parameters;

/// <summary>
/// Tests for SplattedUnknownParameterAnalyzer (NNB011).
/// Verifies detection of unknown parameters on components WITH attribute splatting.
/// </summary>
public class SplattedUnknownParameterAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<SplattedUnknownParameterAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        test.TestState.Sources.Add(RazorGeneratedCodeStubs);

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    private static DiagnosticResult SplattedUnknownDiagnostic(string paramName, string componentName, int markerIndex = 0)
    {
        return new DiagnosticResult(SplattedUnknownParameterAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(markerIndex)
            .WithArguments(paramName, componentName);
    }

    private const string RazorGeneratedCodeStubs = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components
{
    public interface IComponent { }

    public abstract class ComponentBase : IComponent
    {
        protected virtual void BuildRenderTree(Rendering.RenderTreeBuilder builder) { }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ParameterAttribute : Attribute
    {
        public bool CaptureUnmatchedValues { get; set; }
    }

    public delegate void RenderFragment(Rendering.RenderTreeBuilder builder);
}

namespace Microsoft.AspNetCore.Components.Rendering
{
    public sealed class RenderTreeBuilder
    {
        public void OpenElement(int sequence, string elementName) { }
        public void CloseElement() { }
        public void OpenComponent<TComponent>(int sequence) where TComponent : IComponent { }
        public void CloseComponent() { }
        public void AddAttribute(int sequence, string name, object? value) { }
        public void AddComponentParameter(int sequence, string name, object? value) { }
        public void AddContent(int sequence, string? textContent) { }
        public void AddMarkupContent(int sequence, string? markupContent) { }
    }
}
";

    /// <summary>
    /// Component WITHOUT splatting - NNB011 should NOT report (NNB010 handles these).
    /// </summary>
    private const string TestComponentNoSplatting = @"
namespace TestComponents
{
    using Microsoft.AspNetCore.Components;

    public class MudButton : ComponentBase
    {
        [Parameter]
        public string? Color { get; set; }

        [Parameter]
        public RenderFragment? ChildContent { get; set; }
    }
}
";

    /// <summary>
    /// Component WITH splatting - NNB011 SHOULD report unknown params.
    /// </summary>
    private const string TestComponentWithSplatting = @"
namespace TestComponents
{
    using System.Collections.Generic;
    using Microsoft.AspNetCore.Components;

    public class MudButtonSplatted : ComponentBase
    {
        [Parameter]
        public string? Color { get; set; }

        [Parameter(CaptureUnmatchedValues = true)]
        public Dictionary<string, object>? UserAttributes { get; set; }
    }
}
";

    [Fact]
    public async Task UnknownParameter_OnSplattedComponent_ReportsDiagnostic()
    {
        var source = TestComponentWithSplatting + @"
namespace TestApp
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;
    using TestComponents;

    public class TestPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
            __builder.OpenComponent<MudButtonSplatted>(0);
            __builder.AddComponentParameter(1, {|#0:""aria-label""|}, ""Close button"");
            __builder.CloseComponent();
        }
    }
}
";

        await VerifyAnalyzerAsync(source, SplattedUnknownDiagnostic("aria-label", "MudButtonSplatted"));
    }

    [Fact]
    public async Task UnknownParameter_OnNonSplattedComponent_NoDiagnostic()
    {
        // NNB011 only handles splatted components - NNB010 handles non-splatted
        var source = TestComponentNoSplatting + @"
namespace TestApp
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;
    using TestComponents;

    public class TestPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
            __builder.OpenComponent<MudButton>(0);
            __builder.AddComponentParameter(1, ""aria-label"", ""Close button"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostic from NNB011 - non-splatted components are handled by NNB010
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task KnownParameter_OnSplattedComponent_NoDiagnostic()
    {
        var source = TestComponentWithSplatting + @"
namespace TestApp
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;
    using TestComponents;

    public class TestPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
            __builder.OpenComponent<MudButtonSplatted>(0);
            __builder.AddComponentParameter(1, ""Color"", ""primary"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostic - Color is a valid parameter
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DataXcs_ExemptedEvenOnSplattedComponent_NoDiagnostic()
    {
        // data-xcs is hardcoded as exempt (XRay instrumentation)
        var source = TestComponentWithSplatting + @"
namespace TestApp
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;
    using TestComponents;

    public class TestPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
            __builder.OpenComponent<MudButtonSplatted>(0);
            __builder.AddComponentParameter(1, ""data-xcs"", ""test-value"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostic - data-xcs is exempted
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task MultipleUnknownParameters_OnSplattedComponent_ReportsAll()
    {
        var source = TestComponentWithSplatting + @"
namespace TestApp
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;
    using TestComponents;

    public class TestPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
            __builder.OpenComponent<MudButtonSplatted>(0);
            __builder.AddComponentParameter(1, {|#0:""aria-label""|}, ""Button"");
            __builder.AddComponentParameter(2, ""Color"", ""primary""); // Valid
            __builder.AddComponentParameter(3, ""data-xcs"", ""xyz""); // Exempted
            __builder.CloseComponent();

            __builder.OpenComponent<MudButtonSplatted>(4);
            __builder.AddComponentParameter(5, {|#1:""role""|}, ""button"");
            __builder.CloseComponent();
        }
    }
}
";

        await VerifyAnalyzerAsync(source,
            SplattedUnknownDiagnostic("aria-label", "MudButtonSplatted", 0),
            SplattedUnknownDiagnostic("role", "MudButtonSplatted", 1));
    }
}
