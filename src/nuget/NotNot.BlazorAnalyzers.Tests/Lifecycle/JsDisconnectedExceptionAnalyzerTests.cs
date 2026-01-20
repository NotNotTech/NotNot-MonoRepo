using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for JsDisconnectedExceptionAnalyzer (NNB007).
/// Verifies detection of unhandled JSDisconnectedException in DisposeAsync.
/// </summary>
public class JsDisconnectedExceptionAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<JsDisconnectedExceptionAnalyzer, DefaultVerifier>
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

    private static DiagnosticResult JsDisconnectedDiagnostic(string callText)
    {
        return new DiagnosticResult(JsDisconnectedExceptionAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(callText);
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithoutTryCatch_ReportsDiagnostic()
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
        if (_module is not null)
        {
            await {|#0:_module.InvokeVoidAsync(""cleanup"")|};
            await _module.DisposeAsync();
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsDisconnectedDiagnostic("_module.InvokeVoidAsync"));
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithJsDisconnectedExceptionCatch_NoDiagnostic()
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
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync(""cleanup"");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Expected during circuit disconnect
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithSpecificThenBlanketCatch_ReportsDiagnostic()
    {
        // R4.1 (2026-01-20): Multi-catch with BOTH JSDisconnectedException AND blanket catch is NOT acceptable.
        // The blanket catch masks bugs even when specific exception is also caught.
        // Analyzer must check ALL catch clauses, not short-circuit on first valid match.
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
        if (_module is not null)
        {
            try
            {
                await {|#0:_module.InvokeVoidAsync(""cleanup"")|};
            }
            catch (JSDisconnectedException)
            {
                // Specific catch - good
            }
            catch (Exception)
            {
                // WRONG: Blanket catch after specific catch still masks other bugs!
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsDisconnectedDiagnostic("_module.InvokeVoidAsync"));
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithExceptionCatch_ReportsDiagnostic()
    {
        // R4.1 (2026-01-20): catch (Exception) blanket catches are NOT acceptable per FAIL_FAST_PRINCIPLE.
        // They mask bugs and should be replaced with specific exception handling or _WaitIgnoreCancel().
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
        if (_module is not null)
        {
            try
            {
                await {|#0:_module.InvokeVoidAsync(""cleanup"")|};
            }
            catch (Exception)
            {
                // WRONG: Blanket catch masks bugs - use _WaitIgnoreCancel() or catch specific exception
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsDisconnectedDiagnostic("_module.InvokeVoidAsync"));
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithBareCatch_ReportsDiagnostic()
    {
        // R4.1 (2026-01-20): Bare catch {} is NOT acceptable per FAIL_FAST_PRINCIPLE.
        // It masks ALL exceptions including logic bugs. Use specific catches or _WaitIgnoreCancel().
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
        if (_module is not null)
        {
            try
            {
                await {|#0:_module.InvokeVoidAsync(""cleanup"")|};
            }
            catch
            {
                // WRONG: Bare catch masks ALL exceptions - use _WaitIgnoreCancel() or specific catches
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsDisconnectedDiagnostic("_module.InvokeVoidAsync"));
    }

    [Fact]
    public async Task JsInteropCall_NotInDisposeAsync_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    // Not DisposeAsync - should not be analyzed
    public async Task CleanupAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync(""cleanup"");
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DisposeAsync_OnlyCallsDisposeAsync_NoDiagnostic()
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
        // Just calling DisposeAsync is safe - it handles disconnection internally
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}";

        // DisposeAsync on IJSObjectReference is designed to handle JSDisconnectedException
        // The analyzer should only warn on InvokeVoidAsync/InvokeAsync calls
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task MultipleJsInteropCalls_OnlyUnhandledReported()
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
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync(""handled"");
            }
            catch (JSDisconnectedException) { }

            // This one is NOT in a try-catch
            await {|#0:_module.InvokeVoidAsync(""unhandled"")|};
        }
    }
}";

        await VerifyAnalyzerAsync(source, JsDisconnectedDiagnostic("_module.InvokeVoidAsync"));
    }

    [Fact]
    public async Task NonBlazorComponent_WithJsInteropInDisposeAsync_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

// Not a Blazor component - NNB007 should NOT trigger
public class RegularService : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            // This would trigger NNB007 if it were a Blazor component
            // But since it's not, no diagnostic should be reported
            await _module.InvokeVoidAsync(""cleanup"");
        }
    }
}";

        // NNB007 only applies to Blazor components
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithWaitIgnoreCancel_NoDiagnostic()
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
        if (_module is not null)
        {
            // _WaitIgnoreCancel() internally handles JSDisconnectedException
            await _module.InvokeVoidAsync(""cleanup"")._WaitIgnoreCancel();
        }
    }
}

public static class WaitIgnoreCancelExtensions
{
    public static async ValueTask _WaitIgnoreCancel(this ValueTask task)
    {
        try { await task; }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
        // Catches JSDisconnectedException by type name
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task JsInteropCall_InDisposeAsync_WithWaitIgnoreCancelOrNull_NoDiagnostic()
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
        // _WaitIgnoreCancelOrNull() handles null ValueTask and JSDisconnectedException
        // Pattern: (_module?.DisposeAsync()) returns ValueTask? when _module is nullable
        await (_module?.InvokeVoidAsync(""cleanup""))._WaitIgnoreCancelOrNull();
    }
}

public static class WaitIgnoreCancelExtensions
{
    public static async ValueTask _WaitIgnoreCancelOrNull(this ValueTask? task)
    {
        if (task.HasValue)
        {
            try { await task.Value; }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }
}
