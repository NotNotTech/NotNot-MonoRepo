using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.Rendering;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Rendering;

/// <summary>
/// Tests for <see cref="CadenceUnconditionalRenderAnalyzer"/> (<c>NNB042</c>). Verifies that a
/// <c>System.Threading.Timer</c> callback or a <c>PeriodicTimer.WaitForNextTickAsync</c> loop
/// body on a <c>ComponentBase</c>-derived type that invokes <c>StateHasChanged</c>
/// UNCONDITIONALLY fires the rule, while equality-guarded / <c>NnLiveValue</c>-delegated /
/// no-<c>StateHasChanged</c> callbacks stay silent, and assembly-bypass / path-exemption
/// suppress the rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>Assembly-name gate</b>: the analyzer's <c>ShouldAnalyzeCompilation</c> only fires inside
/// <c>.Shared</c> / <c>.Client</c> assemblies (where Blazor components live). The
/// <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/> default project name is
/// <c>TestProject</c>, which would FAIL the gate, so each positive / negative fixture applies
/// a <see cref="SolutionTransform"/> renaming the assembly to <c>TestProject.Client</c>. The
/// bypass-suppression fixture deliberately keeps the <c>.Client</c> name so the bypass
/// attribute (not the assembly gate) is what suppresses.
/// </para>
/// <para>
/// <b>Type stubs</b>: <see cref="BlazorAnalyzerTestHelper.BlazorTypeStubs"/> supplies the
/// <c>ComponentBase</c> / <c>InvokeAsync</c> shape; this file's <see cref="CadenceStubs"/>
/// constant adds the <c>System.Threading.Timer</c> / <c>PeriodicTimer</c> /
/// <c>EqualityComparer&lt;T&gt;</c> stubs the cadence fixtures need.
/// </para>
/// </remarks>
public class CadenceUnconditionalRenderAnalyzerTests
{
	// ── Type stubs the cadence fixtures need (beyond the shared BlazorTypeStubs) ──────────
	private const string CadenceStubs = @"
namespace System.Threading
{
    public delegate void TimerCallback(object? state);

    public sealed class Timer : IDisposable
    {
        public Timer(TimerCallback callback) { }
        public Timer(TimerCallback callback, object? state, int dueTime, int period) { }
        public Timer(TimerCallback callback, object? state, System.TimeSpan dueTime, System.TimeSpan period) { }
        public void Dispose() { }
    }

    public sealed class PeriodicTimer : IDisposable
    {
        public PeriodicTimer(System.TimeSpan period) { }
        public System.Threading.Tasks.ValueTask<bool> WaitForNextTickAsync(System.Threading.CancellationToken cancellationToken = default) => default;
        public void Dispose() { }
    }
}
";

	/// <summary>
	/// Stub for the canonical <see cref="CadenceRenderBypassAttribute"/>. The analyzer matches
	/// by simple name OR fully-qualified name; the canonical
	/// <c>NotNot.BlazorAnalyzers.Rendering</c> location exercises the fully-qualified match path.
	/// </summary>
	private const string BypassAttributeStub = @"
namespace NotNot.BlazorAnalyzers.Rendering
{
    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class CadenceRenderBypassAttribute : System.Attribute { }
}
";

	/// <summary>
	/// Renames the test project's assembly so its identity ends with <c>.Client</c>, satisfying
	/// the analyzer's <c>IsSharedOrClientAssembly</c> compilation gate. Without this the default
	/// <c>TestProject</c> assembly name fails the gate and NO diagnostic ever fires.
	/// </summary>
	private static Solution RenameToClientAssembly(Solution solution, ProjectId projectId)
	{
		var project = solution.GetProject(projectId);
		return project == null
			? solution
			: solution.WithProjectAssemblyName(projectId, "TestProject.Client");
	}

	/// <summary>Trivial placeholder TestCode for path-anchored fixtures whose real source is added via <c>TestState.Sources</c> with a specific path.</summary>
	private const string Placeholder = "class Placeholder { }";

	private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<CadenceUnconditionalRenderAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};

		test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);
		test.TestState.Sources.Add(CadenceStubs);
		test.SolutionTransforms.Add((solution, projectId) => RenameToClientAssembly(solution, projectId));

		if (expected?.Length > 0)
			test.ExpectedDiagnostics.AddRange(expected);

		await test.RunAsync();
	}

	private static DiagnosticResult Nnb042() =>
		new DiagnosticResult(CadenceUnconditionalRenderAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
			.WithLocation(0);

	// ══════════════════════════════════════════════════════════════════════════════════════
	// POSITIVE — should fire NNB042
	// ══════════════════════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimerCallback_InvokeAsyncStateHasChanged_Unconditional_Fires()
	{
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(_ => {|#0:InvokeAsync(StateHasChanged)|}, null, 0, 1000);
    }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source, Nnb042());
	}

	[Fact]
	public async Task TimerCallback_MethodGroup_DirectStateHasChanged_Unconditional_Fires()
	{
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        {|#0:StateHasChanged()|};
    }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source, Nnb042());
	}

	[Fact]
	public async Task PeriodicTimerLoop_DirectStateHasChanged_Unconditional_Fires()
	{
		var source = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase
{
    private readonly PeriodicTimer _pt = new PeriodicTimer(TimeSpan.FromSeconds(1));

    public async Task RunAsync()
    {
        while (await _pt.WaitForNextTickAsync())
        {
            {|#0:StateHasChanged()|};
        }
    }
}";
		await VerifyAsync(source, Nnb042());
	}

	[Fact]
	public async Task TimerCallback_UnrelatedIsPattern_WrappingStateHasChanged_Fires()
	{
		// An `is`-pattern type-test is NOT a dirty-check. A StateHasChanged that merely sits
		// under `if (state is string s)` is unguarded for cadence-render purposes → must FIRE.
		// (Locks the drop of the prior blanket `IsPatternExpressionSyntax: return true`.)
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        if (state is string s)
        {
            {|#0:StateHasChanged()|};
        }
    }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source, Nnb042());
	}

	[Fact]
	public async Task TimerCallback_UnrelatedEqualsCall_NotGatingRender_Fires()
	{
		// `someObj.Equals(x)` is an arbitrary instance call, NOT an EqualityComparer projected
		// dirty-check. A StateHasChanged co-located under such an `if` is unguarded → must FIRE.
		// (Locks the tightening of `Equals` recognition to the EqualityComparer<T> form.)
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;
    private readonly object _marker = new object();

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        if (_marker.Equals(state))
        {
            {|#0:StateHasChanged()|};
        }
    }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source, Nnb042());
	}

	// ══════════════════════════════════════════════════════════════════════════════════════
	// NEGATIVE — should NOT fire NNB042
	// ══════════════════════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task TimerCallback_EqualityGuarded_NoFire()
	{
		var source = @"
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;
    private int _v;

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        int next = Compute();
        if (!EqualityComparer<int>.Default.Equals(_v, next))
        {
            _v = next;
            StateHasChanged();
        }
    }

    private int Compute() => 0;

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source);
	}

	[Fact]
	public async Task TimerCallback_EqualityEarlyReturnGuard_NoFire()
	{
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;
    private int _v;

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        int next = Compute();
        if (_v == next) return;
        _v = next;
        StateHasChanged();
    }

    private int Compute() => 0;

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source);
	}

	[Fact]
	public async Task TimerCallback_NnLiveValueDelegated_NoFire()
	{
		// The cadence is delegated to an NnLiveValue<T> whose recompute kernel IS the dirty-check.
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class NnLiveValue<T>
{
    public void Recompute() { }
}

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;
    private readonly NnLiveValue<int> _live = new NnLiveValue<int>();

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        _live.Recompute();
    }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source);
	}

	[Fact]
	public async Task TimerCallback_NoStateHasChanged_NoFire()
	{
		// Logging-only callback — no StateHasChanged at all → no fire by construction.
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        Log(""tick"");
    }

    private void Log(string m) { }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source);
	}

	[Fact]
	public async Task WhileLoop_NonPeriodicTimerLocalWaitForNextTickAsync_NoFire()
	{
		// A component-local parameterless method coincidentally named WaitForNextTickAsync (NOT a
		// PeriodicTimer) driving a while-loop with StateHasChanged must NOT be mistaken for a
		// PeriodicTimer cadence. A bare receiver-less WaitForNextTickAsync() carries no
		// PeriodicTimer signature → no fire. (Locks removal of the bare name-only fallback.)
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TickComponent : ComponentBase
{
    public async Task RunAsync()
    {
        while (await WaitForNextTickAsync())
        {
            StateHasChanged();
        }
    }

    private Task<bool> WaitForNextTickAsync() => Task.FromResult(false);
}";
		await VerifyAsync(source);
	}

	[Fact]
	public async Task TimerCallback_OnNonComponent_NoFire()
	{
		// Not a ComponentBase — out of scope even with unconditional StateHasChanged-named call.
		var source = @"
using System;
using System.Threading;

public class RegularService : IDisposable
{
    private Timer? _t;

    public RegularService()
    {
        _t = new Timer(OnTick, null, 0, 1000);
    }

    private void OnTick(object? state)
    {
        StateHasChanged();
    }

    private void StateHasChanged() { }

    public void Dispose() => _t?.Dispose();
}";
		await VerifyAsync(source);
	}

	// ══════════════════════════════════════════════════════════════════════════════════════
	// EXEMPTION — assembly bypass + path exemption suppress
	// ══════════════════════════════════════════════════════════════════════════════════════

	[Fact]
	public async Task AssemblyBypass_Suppresses()
	{
		// Same unconditional shape as the positive case, but [assembly: CadenceRenderBypass]
		// short-circuits the compilation gate → no diagnostic.
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

[assembly: NotNot.BlazorAnalyzers.Rendering.CadenceRenderBypass]

public class TickComponent : ComponentBase, IDisposable
{
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(_ => InvokeAsync(StateHasChanged), null, 0, 1000);
    }

    public void Dispose() => _t?.Dispose();
}";

		var test = new CSharpAnalyzerTest<CadenceUnconditionalRenderAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);
		test.TestState.Sources.Add(CadenceStubs);
		test.TestState.Sources.Add(BypassAttributeStub);
		test.SolutionTransforms.Add((solution, projectId) => RenameToClientAssembly(solution, projectId));

		// Expect ZERO diagnostics — [assembly: CadenceRenderBypass] short-circuits the rule.
		await test.RunAsync();
	}

	[Fact]
	public async Task PathExemption_PagesSamples_Suppresses()
	{
		// Same unconditional shape, but the file is anchored under Pages/Samples/** → exempt.
		var source = @"
using System;
using System.Threading;
using Microsoft.AspNetCore.Components;

public class SampleTickComponent : ComponentBase, IDisposable
{
    private Timer? _t;

    protected override void OnInitialized()
    {
        _t = new Timer(_ => InvokeAsync(StateHasChanged), null, 0, 1000);
    }

    public void Dispose() => _t?.Dispose();
}";

		const string samplePath = "/Novaleaf.VibeOverwatch.Shared/Components/Pages/Samples/SampleTickComponent.razor.cs";

		// Path-based exemption requires the source at a SPECIFIC path. Use Placeholder for
		// TestCode and add the real source via TestState.Sources with the target path (mirrors
		// LdddAssemblyFenceAnalyzerTests.NN_LDDD_001_DoesNotFireInPagesSamplesPath).
		var test = new CSharpAnalyzerTest<CadenceUnconditionalRenderAnalyzer, DefaultVerifier>
		{
			TestCode = Placeholder,
		};
		test.TestState.Sources.Add((samplePath, source));
		test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);
		test.TestState.Sources.Add(CadenceStubs);
		test.SolutionTransforms.Add((solution, projectId) => RenameToClientAssembly(solution, projectId));

		// Expect ZERO diagnostics — Pages/Samples/** path is exempt per IsExceptedPath.
		await test.RunAsync();
	}
}
