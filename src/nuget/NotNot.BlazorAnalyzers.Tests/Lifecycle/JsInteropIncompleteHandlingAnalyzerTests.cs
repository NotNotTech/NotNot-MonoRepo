using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for JsInteropIncompleteHandlingAnalyzer (NNB013).
/// Verifies detection of JS interop try-catch blocks catching cancellation
/// but missing JSDisconnectedException (converse of NNB012).
/// </summary>
public class JsInteropIncompleteHandlingAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<JsInteropIncompleteHandlingAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};

		test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult MissingJsDisconnectedDiagnostic()
	{
		return new DiagnosticResult(JsInteropIncompleteHandlingAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(0);
	}

	// ──────────────────────────────────────────────────────────
	// POSITIVE CASES — should fire NNB013
	// ──────────────────────────────────────────────────────────

	[Fact]
	public async Task JsInterop_CatchesOperationCanceled_MissingJsDisconnected_ReportsDiagnostic()
	{
		// The BrowserLocalStorageAdapter pattern — catches cancellation but not disconnect
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class StorageService : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task WriteAsync(string data)
    {
        try
        {
            await _module!.InvokeVoidAsync(""setItem"", data);
        }
        catch (JSException)
        {
        }
        {|#0:catch (OperationCanceledException)
        {
        }|}
        catch (ObjectDisposedException)
        {
        }
    }

    public ValueTask DisposeAsync() => default;
}";

		await VerifyAnalyzerAsync(source, MissingJsDisconnectedDiagnostic());
	}

	[Fact]
	public async Task JsInterop_CatchesTaskCanceled_MissingJsDisconnected_ReportsDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    public async Task MeasureAsync()
    {
        try
        {
            await _module!.InvokeAsync<double[]>(""measureDimensions"");
        }
        {|#0:catch (TaskCanceledException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, MissingJsDisconnectedDiagnostic());
	}

	[Fact]
	public async Task JsInterop_WhenFilter_HasCancellation_MissingJsDisconnected_ReportsDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"");
        }
        {|#0:catch (Exception ex) when (ex is OperationCanceledException or JSException or ObjectDisposedException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, MissingJsDisconnectedDiagnostic());
	}

	[Fact]
	public async Task JsInterop_NotInDisposeAsync_ReportsDiagnostic()
	{
		// NNB013 applies to any method, not just DisposeAsync (unlike NNB007)
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await _module!.InvokeVoidAsync(""init"");
        }
        catch (JSException)
        {
        }
        {|#0:catch (OperationCanceledException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, MissingJsDisconnectedDiagnostic());
	}

	// ──────────────────────────────────────────────────────────
	// NEGATIVE CASES — should NOT fire NNB013
	// ──────────────────────────────────────────────────────────

	[Fact]
	public async Task JsInterop_HasBothCancellationAndJsDisconnected_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException or JSException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task JsInterop_SeparateCatches_BothPresent_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NoJsInteropCalls_CatchesCancellation_NoDiagnostic()
	{
		// JS-interop gate: OperationCanceledException for non-JS code is fine
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class RegularService
{
    public async Task DoWorkAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(1000, ct);
        }
        catch (OperationCanceledException)
        {
            // Legitimate non-JS cancellation handling
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task BlanketExceptionCatch_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"");
        }
        catch (Exception)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task BareCatch_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"");
        }
        catch
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task SafeWrappedCalls_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"")._WaitIgnoreCancel();
        }
        catch (OperationCanceledException)
        {
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
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NoCancellationCatch_NoDiagnostic()
	{
		// Only catches JSDisconnectedException — NNB012's concern, not NNB013's
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class MyService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""work"");
        }
        catch (JSDisconnectedException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}
}
