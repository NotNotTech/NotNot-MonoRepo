using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for JsReferenceDisposalAnalyzer (NNB002).
/// Verifies detection of JS reference fields without proper disposal.
/// </summary>
public class JsReferenceDisposalAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<JsReferenceDisposalAnalyzer, DefaultVerifier>
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

    private static DiagnosticResult JsReferenceDisposalDiagnostic(string fieldName, string typeName)
    {
        return new DiagnosticResult(JsReferenceDisposalAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments(fieldName, typeName);
    }

    [Fact]
    public async Task IJSObjectReference_WithoutIAsyncDisposable_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? {|#0:_module|};

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // _module = await JS.InvokeAsync<IJSObjectReference>(""import"", ""./module.js"");
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsReferenceDisposalDiagnostic("_module", "IJSObjectReference"));
    }

    [Fact]
    public async Task DotNetObjectReference_WithoutDisposal_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private DotNetObjectReference<TestComponent>? {|#0:_dotNetRef|};

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference<TestComponent>.Create(this);
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsReferenceDisposalDiagnostic("_dotNetRef", "DotNetObjectReference<TestComponent>"));
    }

    [Fact]
    public async Task IJSObjectReference_WithIAsyncDisposable_AndDisposed_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // _module = await JS.InvokeAsync<IJSObjectReference>(""import"", ""./module.js"");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DotNetObjectReference_WithIDisposable_AndDisposed_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IDisposable
{
    private DotNetObjectReference<TestComponent>? _dotNetRef;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference<TestComponent>.Create(this);
        }
    }

    public void Dispose()
    {
        _dotNetRef?.Dispose();
    }
}";

        // DotNetObjectReference has sync Dispose, so IDisposable is acceptable
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task MultipleJsReferences_OnlyUndisposedReported()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module1;
    private IJSObjectReference? {|#0:_module2|};

    public async ValueTask DisposeAsync()
    {
        if (_module1 is not null)
        {
            await _module1.DisposeAsync();
        }
        // _module2 is NOT disposed!
    }
}";

        await VerifyAnalyzerAsync(source, JsReferenceDisposalDiagnostic("_module2", "IJSObjectReference"));
    }

    [Fact]
    public async Task NonBlazorComponent_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

// Not a Blazor component - no ComponentBase inheritance
public class RegularClass
{
    private IJSObjectReference? _module;
}";

        // Should not report - this is not a Blazor component
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task IJSObjectReference_WithConditionalDispose_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask DisposeAsync()
    {
        // Using conditional access pattern
        if (_module != null)
        {
            await _module.DisposeAsync();
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task IJSObjectReference_WithInvokeVoidAsyncDispose_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _editor;

    public async ValueTask DisposeAsync()
    {
        // Using InvokeVoidAsync(""dispose"") pattern - common for JS wrapper cleanup
        if (_editor is not null)
        {
            await _editor.InvokeVoidAsync(""dispose"");
        }
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}";

        // Both _editor (disposed via InvokeVoidAsync) and _module (disposed via DisposeAsync) should be OK
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task IJSObjectReference_WithInvokeVoidAsyncCleanup_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _viewer;

    public async ValueTask DisposeAsync()
    {
        // Using InvokeVoidAsync(""cleanup"") pattern
        if (_viewer is not null)
        {
            await _viewer.InvokeVoidAsync(""cleanup"");
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }
}
