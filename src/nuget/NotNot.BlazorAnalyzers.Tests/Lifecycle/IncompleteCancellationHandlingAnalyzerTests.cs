using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for IncompleteCancellationHandlingAnalyzer (NNB012).
/// Verifies detection of catch blocks handling JSDisconnectedException but missing
/// TaskCanceledException/OperationCanceledException.
/// </summary>
public class IncompleteCancellationHandlingAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<IncompleteCancellationHandlingAnalyzer, DefaultVerifier>
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

	private static DiagnosticResult IncompleteCancellationDiagnostic()
	{
		return new DiagnosticResult(IncompleteCancellationHandlingAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(0);
	}

	// ──────────────────────────────────────────────────────────
	// POSITIVE CASES — should fire NNB012
	// ──────────────────────────────────────────────────────────

	[Fact]
	public async Task WhenFilter_HasJsDisconnected_MissingTaskCanceled_ReportsDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        {|#0:catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, IncompleteCancellationDiagnostic());
	}

	[Fact]
	public async Task DirectCatch_JsDisconnectedOnly_ReportsDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        {|#0:catch (JSDisconnectedException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, IncompleteCancellationDiagnostic());
	}

	[Fact]
	public async Task MultipleSeparateCatches_JsDisconnectedButNoCancellation_ReportsDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        {|#0:catch (JSDisconnectedException)
        {
        }|}
        catch (JSException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source, IncompleteCancellationDiagnostic());
	}

	[Fact]
	public async Task NotInDisposeAsync_StillReportsDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    public async Task FocusAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""focus"");
        }
        {|#0:catch (Exception ex) when (ex is JSDisconnectedException or JSException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, IncompleteCancellationDiagnostic());
	}

	[Fact]
	public async Task NonBlazorComponent_StillReportsDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

public class RegularService : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        {|#0:catch (JSDisconnectedException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, IncompleteCancellationDiagnostic());
	}

	[Fact]
	public async Task WhenFilter_NegatedTaskCanceled_StillReportsDiagnostic()
	{
		// 'not TaskCanceledException' does NOT handle it — semantic analysis catches this
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        {|#0:catch (Exception ex) when (ex is JSDisconnectedException or not TaskCanceledException)
        {
        }|}
    }
}";

		await VerifyAnalyzerAsync(source, IncompleteCancellationDiagnostic());
	}

	[Fact]
	public async Task WhenFilter_MixedPositiveAndNegative_PositiveWins_NoDiagnostic()
	{
		// TaskCanceledException is positively matched in one branch AND negated in another.
		// The positive match should win — TaskCanceledException IS handled.
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or not OperationCanceledException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	// ──────────────────────────────────────────────────────────
	// NEGATIVE CASES — should NOT fire NNB012
	// ──────────────────────────────────────────────────────────

	[Fact]
	public async Task WhenFilter_HasBothJsDisconnectedAndTaskCanceled_NoDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or JSException or ObjectDisposedException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task WhenFilter_HasOperationCanceledException_NoDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException or JSException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task SeparateCatches_BothJsDisconnectedAndTaskCanceled_NoDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (TaskCanceledException)
        {
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
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
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
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NoCatchForJsDisconnected_NoDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (ObjectDisposedException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task NoTryCatch_NoDiagnostic()
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
        await _module!.InvokeVoidAsync(""cleanup"");
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task JsDisconnectedCatch_PlusBlanketCatch_WithCancellation_NoDiagnostic()
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (Exception)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	[Fact]
	public async Task SafeWrappedCalls_NoDiagnostic()
	{
		// _WaitIgnoreCancel() handles both JSDisconnectedException and TaskCanceledException
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"")._WaitIgnoreCancel();
        }
        catch (JSDisconnectedException)
        {
            // Outer catch is redundant but safe — _WaitIgnoreCancel handles it
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
	public async Task LogicalOrFilter_HasBothTypes_NoDiagnostic()
	{
		// Tests the || pattern: catch (Exception ex) when (ex is A || ex is B)
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
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        catch (Exception ex) when (ex is JSDisconnectedException || ex is TaskCanceledException)
        {
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}
}
