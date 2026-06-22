using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Lifecycle;

/// <summary>
/// Tests for TransitionGuardClaimAfterAwaitAnalyzer (NNB045).
/// Verifies detection of a transition-guard field claimed AFTER an await in an async Blazor
/// lifecycle override, and silence on the claim-before-await / no-claim / non-component /
/// lambda-deferred negatives.
/// </summary>
public class TransitionGuardClaimAfterAwaitAnalyzerTests
{
	private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<TransitionGuardClaimAfterAwaitAnalyzer, DefaultVerifier>
		{
			TestCode = source
		};

		// Add Blazor type stubs (ComponentBase, lifecycle methods, etc.).
		test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);

		if (expected?.Length > 0)
		{
			test.ExpectedDiagnostics.AddRange(expected);
		}

		await test.RunAsync();
	}

	private static DiagnosticResult ClaimAfterAwaitDiagnostic(string lifecycleMethod, string guardField)
	{
		return new DiagnosticResult(TransitionGuardClaimAfterAwaitAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(0)
			.WithArguments(lifecycleMethod, guardField);
	}

	/// <summary>
	/// POSITIVE — pre-fix UserInputsPanel shape: guard field claimed AFTER the awaits. NNB045 fires
	/// at the '_currentSessionId = newId' assignment.
	/// </summary>
	[Fact]
	public async Task ClaimAfterAwait_InOnParametersSetAsync_ReportsDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    private string? _currentSessionId;
    public string? newId;

    protected override async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            await Task.Delay(1);
            {|#0:_currentSessionId = newId|};
        }
    }
}";

		await VerifyAnalyzerAsync(source, ClaimAfterAwaitDiagnostic("OnParametersSetAsync", "_currentSessionId"));
	}

	/// <summary>
	/// NEGATIVE (a) — claim BEFORE the await (the canonical claim-then-await fix). No diagnostic.
	/// </summary>
	[Fact]
	public async Task ClaimBeforeAwait_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    private string? _currentSessionId;
    public string? newId;

    protected override async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            _currentSessionId = newId;
            await Task.Delay(1);
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	/// <summary>
	/// NEGATIVE (b) — guard block has an await but no assignment to the guard field. No diagnostic.
	/// </summary>
	[Fact]
	public async Task NoAssignmentToGuardField_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    private string? _currentSessionId;
    public string? newId;

    protected override async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            await Task.Delay(1);
            await Task.Delay(2);
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	/// <summary>
	/// NEGATIVE (c) — non-ComponentBase class with a method coincidentally named OnParametersSetAsync,
	/// claiming after an await. No diagnostic (semantic component gate excludes it).
	/// </summary>
	[Fact]
	public async Task NonComponentClass_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;

public class NotAComponent
{
    private string? _currentSessionId;
    private string? newId;

    public async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            await Task.Delay(1);
            _currentSessionId = newId;
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	/// <summary>
	/// NEGATIVE (d) — assignment to the guard field inside a lambda after an await. The lambda is a
	/// deferred scope, so the claim is NOT a synchronous claim. No diagnostic.
	/// </summary>
	[Fact]
	public async Task ClaimInsideLambdaAfterAwait_NoDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    private string? _currentSessionId;
    public string? newId;

    private void Schedule(Action callback) => callback();

    protected override async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            await Task.Delay(1);
            Schedule(() => { _currentSessionId = newId; });
        }
    }
}";

		await VerifyAnalyzerAsync(source);
	}

	/// <summary>
	/// Secondary lifecycle override — claim after await in OnInitializedAsync also fires (message
	/// arg {0} reflects the actual lifecycle name).
	/// </summary>
	[Fact]
	public async Task ClaimAfterAwait_InOnInitializedAsync_ReportsDiagnostic()
	{
		var source = @"
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    private string? _currentSessionId;
    public string? newId;

    protected override async Task OnInitializedAsync()
    {
        if (newId != _currentSessionId)
        {
            await Task.Delay(1);
            {|#0:_currentSessionId = newId|};
        }
    }
}";

		await VerifyAnalyzerAsync(source, ClaimAfterAwaitDiagnostic("OnInitializedAsync", "_currentSessionId"));
	}
}
