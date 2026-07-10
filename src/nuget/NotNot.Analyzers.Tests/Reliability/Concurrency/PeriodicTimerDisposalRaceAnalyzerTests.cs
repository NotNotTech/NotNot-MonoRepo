using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability.Concurrency;

/// <summary>
/// Tests for <see cref="PeriodicTimerDisposalRaceAnalyzer"/> (NN_R009).
/// </summary>
/// <remarks>
/// <para>
/// REQ-1 (POSITIVE): a field <c>PeriodicTimer</c> that is BOTH polled via <c>WaitForNextTickAsync</c> AND
/// disposed inside <c>DisposeAsync()</c> fires NN_R009 at the field declaration, with arg {0} = the field name.
/// </para>
/// <para>
/// REQ-2 (NEGATIVE — all silent): (a) a <c>using var</c> LOCAL PeriodicTimer (disposed by scope after the loop
/// — the safe pattern; a local is never a field/property member), (b) the canonical fix — a
/// <c>Task.Delay(interval, token)</c> loop with no PeriodicTimer at all, (c) a field PeriodicTimer waited-on but
/// NEVER disposed (condition (c) unmet), and (d) a field PeriodicTimer disposed but NEVER waited-on (condition
/// (b) unmet).
/// </para>
/// <para>
/// <c>System.Threading.PeriodicTimer</c> is BCL, resolved from the net80 reference assemblies — no local stub
/// is required, so fixtures compile directly.
/// </para>
/// </remarks>
public class PeriodicTimerDisposalRaceAnalyzerTests
{
	private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<PeriodicTimerDisposalRaceAnalyzer, DefaultVerifier>
		{
			TestCode = source,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		// Only the analyzer diagnostics are under test — ignore nullable/unused compiler diagnostics.
		test.CompilerDiagnostics = CompilerDiagnostics.None;

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult Diagnostic(int line, int column, string fieldName)
		=> new DiagnosticResult(PeriodicTimerDisposalRaceAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(fieldName);

	// ---- REQ-1: POSITIVE — field PeriodicTimer polled + disposed in a disposal method ----

	[Fact]
	public async Task FieldTimer_PolledAndDisposedInDisposeAsync_Fires()
	{
		// The `_timer` field-declaration identifier is on line 8, column 36
		// (4-space indent + "private " + "readonly " + "PeriodicTimer " = column 36).
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller : System.IAsyncDisposable
{
    private readonly PeriodicTimer _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    public async Task RunAsync(CancellationToken ct)
    {
        while (await _timer.WaitForNextTickAsync(ct)) { }
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await Task.CompletedTask;
    }
}";

		await VerifyAsync(source, Diagnostic(8, 36, "_timer"));
	}

	// ---- REQ-2: NEGATIVES — all silent ----

	[Fact]
	public async Task UsingVarLocalTimer_Silent()
	{
		// (a) A `using var` LOCAL PeriodicTimer, disposed by its scope AFTER the loop exits — the safe pattern.
		// A local is never a field/property member, so it is never collected → never flagged.
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller
{
    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct)) { }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task TaskDelayLoop_NoTimer_Silent()
	{
		// (b) The canonical fix — a per-iteration Task.Delay loop with no PeriodicTimer at all.
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller : System.IAsyncDisposable
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task FieldTimer_WaitedButNeverDisposed_Silent()
	{
		// (c) A field PeriodicTimer waited-on but NEVER disposed — condition (c) unmet.
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller
{
    private readonly PeriodicTimer _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    public async Task RunAsync(CancellationToken ct)
    {
        while (await _timer.WaitForNextTickAsync(ct)) { }
    }
}";

		await VerifyAsync(source);
	}

	[Fact]
	public async Task FieldTimer_DisposedButNeverWaited_Silent()
	{
		// (d) A field PeriodicTimer disposed in DisposeAsync but NEVER waited-on — condition (b) unmet.
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller : System.IAsyncDisposable
{
    private readonly PeriodicTimer _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await Task.CompletedTask;
    }
}";

		await VerifyAsync(source);
	}

	// ---- AC-2 (independent tester): token-agnostic production-shape POSITIVE + local-boundary NEGATIVE ----

	[Fact]
	public async Task FieldTimer_TokenPassedCancelBeforeDispose_Fires()
	{
		// The EXACT real production-bug shape NN_R009 exists to catch — the one the mis-enumeration ("passes a
		// token") that hid 3 real sites would have MISSED. A field PeriodicTimer is polled WITH a CancellationToken
		// argument (_stopCts.Token) AND disposed in DisposeAsync AFTER _stopCts.Cancel() + await _loopTask, inside
		// an OperationCanceledException-only catch. The rule is TOKEN-AGNOSTIC: it fires whether or not a token is
		// passed to WaitForNextTickAsync. The `_timer` field identifier is on line 8, column 36 (identical prefix
		// to the REQ-1 positive: 4-space indent + "private readonly PeriodicTimer " = column 36).
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller : System.IAsyncDisposable
{
    private readonly PeriodicTimer _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();
    private Task _loopTask = Task.CompletedTask;

    public void Start() => _loopTask = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_stopCts.Token)) { }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stopCts.Cancel();
        try
        {
            await _loopTask;
        }
        catch (OperationCanceledException) { }
        _timer.Dispose();
        _stopCts.Dispose();
    }
}";

		await VerifyAsync(source, Diagnostic(8, 36, "_timer"));
	}

	[Fact]
	public async Task UsingVarLocalTimer_WithCtsFieldCancelledInDispose_Silent()
	{
		// Boundary NEGATIVE: a `using var` LOCAL PeriodicTimer polled with a CancellationTokenSource FIELD's token
		// (_stopCts.Token), with _stopCts.Cancel() invoked inside DisposeAsync. The timer is a LOCAL (never a
		// field/property member), so it is never collected → NN_R009 must stay silent even though a CTS field is
		// cancelled in a disposal method and the polled token comes from a field. Proves the field-vs-local
		// boundary holds under the exact token-passing shape of the positive above.
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;

public class Poller : System.IAsyncDisposable
{
    private readonly CancellationTokenSource _stopCts = new CancellationTokenSource();

    public async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(_stopCts.Token)) { }
    }

    public ValueTask DisposeAsync()
    {
        _stopCts.Cancel();
        return ValueTask.CompletedTask;
    }
}";

		await VerifyAsync(source);
	}
}
