using NotNot.BlazorAnalyzers.Parameters;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Parameters;

/// <summary>
/// Tests for UnknownComponentParameterAnalyzer (NNB010).
/// Verifies detection of unknown parameters passed to Blazor components.
/// </summary>
public class UnknownComponentParameterAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<UnknownComponentParameterAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        // Add Blazor type stubs including RenderTreeBuilder
        test.TestState.Sources.Add(RazorGeneratedCodeStubs);

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    private static DiagnosticResult UnknownParameterDiagnostic(string paramName, string componentName, int markerIndex = 0)
    {
        return new DiagnosticResult(UnknownComponentParameterAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(markerIndex)
            .WithArguments(paramName, componentName);
    }

    /// <summary>
    /// Type stubs simulating generated Razor code and Blazor component infrastructure.
    /// </summary>
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

namespace Microsoft.AspNetCore.Components.Web
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;

    public class PageTitle : ComponentBase
    {
        [Parameter]
        public RenderFragment? ChildContent { get; set; }
    }
}
";

    /// <summary>
    /// Test component with no attribute splatting.
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
        public string? Variant { get; set; }

        [Parameter]
        public RenderFragment? ChildContent { get; set; }
    }
}
";

    /// <summary>
    /// Test component WITH attribute splatting (CaptureUnmatchedValues = true).
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
    public async Task UnknownParameter_OnComponentWithoutSplatting_ReportsDiagnostic()
    {
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
            __builder.AddComponentParameter(1, {|#0:""unknownattr""|}, ""test-value"");
            __builder.CloseComponent();
        }
    }
}
";

        await VerifyAnalyzerAsync(source, UnknownParameterDiagnostic("unknownattr", "MudButton"));
    }

    [Fact]
    public async Task KnownParameter_OnComponent_NoDiagnostic()
    {
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
            // Using string literal for known parameter - still valid
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
    public async Task UnknownParameter_OnComponentWithSplatting_NoDiagnostic()
    {
        // NNB010 skips components with CaptureUnmatchedValues = true.
        // The stricter NNB011 handles those cases.
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
            __builder.AddComponentParameter(2, ""aria-label"", ""Close button"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostic from NNB010 - splatted components are handled by NNB011
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DataXcs_ExemptedAttribute_NoDiagnostic()
    {
        // data-xcs is hardcoded as exempt (XRay instrumentation)
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
    public async Task AriaAttributes_ExemptedByPrefix_NoDiagnostic()
    {
        // All aria-* attributes are exempted by prefix
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
            __builder.AddComponentParameter(1, ""aria-label"", ""test"");
            __builder.AddComponentParameter(2, ""aria-hidden"", ""true"");
            __builder.AddComponentParameter(3, ""aria-describedby"", ""desc"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostics - all aria-* attributes are exempted
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task CommonHtmlAttributes_Exempted_NoDiagnostic()
    {
        // Common HTML passthrough attributes are exempted
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
            __builder.AddComponentParameter(1, ""title"", ""tooltip"");
            __builder.AddComponentParameter(2, ""role"", ""button"");
            __builder.AddComponentParameter(3, ""tabindex"", ""0"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostics - common HTML attributes are exempted
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NameOfExpression_NoDiagnostic()
    {
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
            // nameof expression - compiler validates this
            __builder.AddComponentParameter(1, nameof(MudButton.Color), ""primary"");
            __builder.CloseComponent();
        }
    }
}
";

        // No diagnostic - nameof expressions are validated by the compiler
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task MultipleUnknownParameters_ReportsMultipleDiagnostics()
    {
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
            __builder.AddComponentParameter(1, {|#0:""unknownattr""|}, ""test-value"");
            __builder.AddComponentParameter(2, ""Color"", ""primary""); // This one is valid
            __builder.CloseComponent();

            __builder.OpenComponent<MudButton>(4);
            __builder.AddComponentParameter(5, {|#1:""foobar""|}, ""Button"");
            __builder.CloseComponent();
        }
    }
}
";

        await VerifyAnalyzerAsync(source,
            UnknownParameterDiagnostic("unknownattr", "MudButton", 0),
            UnknownParameterDiagnostic("foobar", "MudButton", 1));
    }

    [Fact]
    public async Task AddAttribute_NotAddComponentParameter_NoDiagnostic()
    {
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
            // AddAttribute (not AddComponentParameter) is used for things like ChildContent
            __builder.AddAttribute(1, ""ChildContent"", (RenderFragment)(b => { }));
            __builder.CloseComponent();
        }
    }
}
";

        // AddAttribute is not analyzed - only AddComponentParameter
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task HtmlElement_NotComponent_NoDiagnostic()
    {
        var source = @"
namespace TestApp
{
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;

    public class TestPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder __builder)
        {
            // HTML elements use OpenElement, not OpenComponent
            __builder.OpenElement(0, ""div"");
            __builder.AddAttribute(1, ""data-xcs"", ""test-value"");
            __builder.CloseElement();
        }
    }
}
";

        // HTML elements are not analyzed - only components
        await VerifyAnalyzerAsync(source);
    }
}
