using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for BlazorDisposalCatchAnalyzer (NNB009).
/// Verifies detection of verbose catch blocks for cancel exceptions in DisposeAsync.
/// </summary>
public class BlazorDisposalCatchAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<BlazorDisposalCatchAnalyzer, DefaultVerifier>
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

    private static DiagnosticResult DisposalCatchDiagnostic(string callText)
    {
        return new DiagnosticResult(BlazorDisposalCatchAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(callText);
    }

    /// <summary>
    /// Test 1: Empty catch (JSDisconnectedException) in DisposeAsync - REPORT NNB009
    /// </summary>
    [Fact]
    public async Task EmptyJsDisconnectedExceptionCatch_InDisposeAsync_ReportsDiagnostic()
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException)
            {
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 2: Empty catch (OperationCanceledException) in DisposeAsync - REPORT NNB009
    /// </summary>
    [Fact]
    public async Task EmptyOperationCanceledExceptionCatch_InDisposeAsync_ReportsDiagnostic()
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (OperationCanceledException)
            {
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 3: Empty catch (TaskCanceledException) in DisposeAsync - REPORT NNB009
    /// </summary>
    [Fact]
    public async Task EmptyTaskCanceledExceptionCatch_InDisposeAsync_ReportsDiagnostic()
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (TaskCanceledException)
            {
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 4: Catch block also catches ObjectDisposedException - NO DIAGNOSTIC (R2.2)
    /// </summary>
    [Fact]
    public async Task CatchAlsoCatchesObjectDisposedException_NoDiagnostic()
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
            }
            catch (JSDisconnectedException)
            {
            }
            catch (ObjectDisposedException)
            {
                // Different semantics - workflow bug, not cancel
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 5: Catch block also catches JSException - NO DIAGNOSTIC (R2.2)
    /// </summary>
    [Fact]
    public async Task CatchAlsoCatchesJSException_NoDiagnostic()
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
            }
            catch (JSDisconnectedException)
            {
            }
            catch (JSException)
            {
                // Different semantics
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 6: Multi-statement try block - REPORT NNB009 (diagnosis only, no fix)
    /// </summary>
    [Fact]
    public async Task MultiStatementTryBlock_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            {|#0:try
            {
                _disposed = true;
                await _module.InvokeVoidAsync(""cleanup"");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 7: Catch with non-trivial logic - NO DIAGNOSTIC
    /// </summary>
    [Fact]
    public async Task CatchWithNonTrivialLogic_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _cleanupFailed;

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException)
            {
                _cleanupFailed = true;
                // Has actual logic - not just eating exception
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 8: _WaitIgnoreCancel not available - REPORT NNB009 (no fix offered, but still reports)
    /// This test verifies the analyzer reports even when the fix isn't available.
    /// </summary>
    [Fact]
    public async Task WaitIgnoreCancelNotAvailable_ReportsDiagnostic()
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException)
            {
                // _WaitIgnoreCancel() not available but still verbose
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 9: Multi-catch: two cancel exceptions (JSDisconnectedException + OperationCanceledException) - REPORT NNB009
    /// Note: TaskCanceledException is a subclass of OperationCanceledException, so catching both is a compile error.
    /// OperationCanceledException covers both.
    /// </summary>
    [Fact]
    public async Task MultiCatch_TwoCancelExceptions_ReportsDiagnostic()
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException)
            {
            }
            catch (OperationCanceledException)
            {
                // Covers TaskCanceledException too (subclass)
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 10: Partial set: two of three exceptions - REPORT NNB009
    /// </summary>
    [Fact]
    public async Task PartialSet_TwoOfThreeExceptions_ReportsDiagnostic()
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException)
            {
            }
            catch (OperationCanceledException)
            {
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 11: Logging-only catch body (Debug.WriteLine) - REPORT NNB009
    /// </summary>
    [Fact]
    public async Task LoggingOnlyCatchBody_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Diagnostics;
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
            {|#0:try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException)
            {
                Debug.WriteLine(""Circuit disconnected during cleanup"");
            }|}
        }
    }
}";

        await VerifyAnalyzerAsync(source, DisposalCatchDiagnostic("_module.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 12: Catch with when filter (non-trivial) - NO DIAGNOSTIC
    /// </summary>
    [Fact]
    public async Task CatchWithNonTrivialWhenFilter_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _expectedDisconnect;

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync(""cleanup"");
            }
            catch (JSDisconnectedException) when (_expectedDisconnect)
            {
                // Conditional handling - intentional
            }
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }
}
