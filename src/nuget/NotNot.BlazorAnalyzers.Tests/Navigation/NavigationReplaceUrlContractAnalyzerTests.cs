using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.Navigation;

namespace NotNot.BlazorAnalyzers.Tests.Navigation;

/// <summary>
/// Tests for <see cref="NavigationReplaceUrlContractAnalyzer"/> (NNB041) — the adjacent-state-mutation
/// contract for <c>NavigationManagerExtensions.ReplaceUrlStateAsync</c>. Verifies positive/negative
/// fixtures per the recipe Phase 0e table (F1-F7).
/// <para>
/// Each fixture locally declares a minimal <c>NavigationManagerExtensions</c> static class with a
/// <c>ReplaceUrlStateAsync</c> extension stub plus a <c>ComponentBase</c>-derived component — the
/// test compilation does not reference VOW, so the symbol must be locally present for the analyzer's
/// SemanticModel by-name resolution.
/// </para>
/// </summary>
public class NavigationReplaceUrlContractAnalyzerTests
{
	// ── Test infrastructure ─────────────────────────────────────────────────────

	/// <summary>
	/// Stub for <c>Microsoft.AspNetCore.Components.ComponentBase</c> — the analyzer's
	/// <see cref="Lifecycle.BlazorLifecycleHelpers.IsBlazorComponent"/> matches by simple-name +
	/// containing-namespace.
	/// </summary>
	private const string BlazorComponentsStub = @"
using System.Threading.Tasks;
namespace Microsoft.AspNetCore.Components
{
    public abstract class ComponentBase { }
    public sealed class NavigationManager { }
}
";

	/// <summary>
	/// Stub for <c>NavigationManagerExtensions</c> — the class defined in the VOW consumer that
	/// the analyzer matches by simple-name strings. Must declare a <c>ReplaceUrlStateAsync</c>
	/// extension method on <c>NavigationManager</c> so the SemanticModel resolves it.
	/// </summary>
	private const string NavigationExtensionsStub = @"
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
namespace TestProject.Shared.Helpers
{
    public static class NavigationManagerExtensions
    {
        public static Task ReplaceUrlStateAsync(this NavigationManager nav, string url, string? title = null)
            => Task.CompletedTask;
    }
}
";

	// ═══════════════════════════════════════════════════════════════════════════
	// F1 — POSITIVE: no adjacent mutation (407350ad shape)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F1 — Synthetic reconstruction of the commit-407350ad pre-fix shape. A Blazor component
	/// method calls <c>ReplaceUrlStateAsync</c> with NO preceding synchronous UI-state update.
	/// NNB041 fires at the invocation.
	/// </summary>
	[Fact]
	public async Task F1_NoAdjacentMutation_FiresNNB041()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Components.Pages
{
    public class VowDashboard : ComponentBase
    {
        private NavigationManager _nav = new NavigationManager();

        private async Task HandleSessionClick(string sessionId)
        {
            // No state mutation before the URL update — this is the 407350ad regression.
            await _nav.ReplaceUrlStateAsync(""/session/"" + sessionId);
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(NavigationExtensionsStub);

		// NNB041 fires at the ReplaceUrlStateAsync invocation (the InvocationExpression node,
		// not the await keyword). Argument {0} = enclosing method name.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NavigationReplaceUrlContractAnalyzer.Rule)
				.WithArguments("HandleSessionClick")
				.WithSpan(14, 19, 14, 69));

		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// F2 — NEGATIVE: mode (a) _selectedSession = session; precedes
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F2 — Mode (a): <c>_selectedSession = session;</c> precedes the call. Silent.
	/// </summary>
	[Fact]
	public async Task F2_ModeA_SelectedSessionAssignment_NoDiagnostic()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Components.Pages
{
    public class VowDashboard : ComponentBase
    {
        private NavigationManager _nav = new NavigationManager();
        private object? _selectedSession;

        private async Task HandleSessionClick(object session)
        {
            _selectedSession = session;
            await _nav.ReplaceUrlStateAsync(""/session/123"");
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(NavigationExtensionsStub);

		// Expect ZERO diagnostics — mode (a) assignment satisfies contract.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// F3 — NEGATIVE: mode (b) SelectSession/OpenTerminalTab precedes
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F3 — Mode (b): <c>SelectSession(session)</c> and <c>OpenTerminalTab(...)</c> precede the call. Silent.
	/// </summary>
	[Fact]
	public async Task F3_ModeB_HelperCallPrecedes_NoDiagnostic()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Components.Pages
{
    public class VowDashboard : ComponentBase
    {
        private NavigationManager _nav = new NavigationManager();

        private void SelectSession(object? session) { }
        private void OpenTerminalTab(string id) { }

        private async Task HandleActivateSession(object session, string tabId)
        {
            SelectSession(session);
            OpenTerminalTab(tabId);
            await _nav.ReplaceUrlStateAsync(""/session/123"");
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(NavigationExtensionsStub);

		// Expect ZERO diagnostics — mode (b) helper call satisfies contract.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// F4 — NEGATIVE: mode (c) _selectedSession = null; precedes (cleanup)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F4 — Mode (c): <c>_selectedSession = null;</c> precedes the call (cleanup flow). Silent.
	/// </summary>
	[Fact]
	public async Task F4_ModeC_CleanupNullAssignment_NoDiagnostic()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Components.Pages
{
    public class VowDashboard : ComponentBase
    {
        private NavigationManager _nav = new NavigationManager();
        private object? _selectedSession;

        private async Task HandleCloseSession()
        {
            _selectedSession = null;
            await _nav.ReplaceUrlStateAsync(""/"");
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(NavigationExtensionsStub);

		// Expect ZERO diagnostics — mode (c) cleanup assignment satisfies contract.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// F5 — NEGATIVE: non-component context (plain service class)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F5 — Call from a plain service class (not <c>ComponentBase</c>-derived). Silent (AC1 specificity).
	/// </summary>
	[Fact]
	public async Task F5_NonComponentContext_NoDiagnostic()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Services
{
    public class UrlHelperService
    {
        private NavigationManager _nav = new NavigationManager();

        public async Task UpdateUrl(string url)
        {
            await _nav.ReplaceUrlStateAsync(url);
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(NavigationExtensionsStub);

		// Expect ZERO diagnostics — UrlHelperService is not ComponentBase-derived.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// F6 — NEGATIVE: path-exempt (/Pages/Samples/)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F6 — Same positive shape as F1, but file path is under <c>/Pages/Samples/</c>. Silent (AC8).
	/// The test places the component source under a custom file path containing <c>/Pages/Samples/</c>
	/// so the analyzer's <see cref="NavigationReplaceUrlContractAnalyzer"/> path exemption fires.
	/// </summary>
	[Fact]
	public async Task F6_PathExempt_SamplesFolder_NoDiagnostic()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Components.Pages.Samples
{
    public class SampleDashboard : ComponentBase
    {
        private NavigationManager _nav = new NavigationManager();

        private async Task HandleClick(string sessionId)
        {
            // No state mutation — but this file is under /Pages/Samples/, so exempt.
            await _nav.ReplaceUrlStateAsync(""/session/"" + sessionId);
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			// Placeholder required because TestCode can't be empty; real source is under custom path.
			TestCode = "class Placeholder { }",
		};
		// Add all sources under a path that contains /Pages/Samples/ for the component under test.
		test.TestState.Sources.Add(("C:/Proj/Pages/Samples/SampleDashboard.cs", source));
		test.TestState.Sources.Add(("C:/Proj/BlazorStubs.cs", BlazorComponentsStub));
		test.TestState.Sources.Add(("C:/Proj/NavExtStub.cs", NavigationExtensionsStub));

		// Expect ZERO diagnostics — path exemption applies.
		await test.RunAsync();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// F7 — POSITIVE: message-content assertion
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	/// F7 — Message-content assertion: verifies the diagnostic message contains the key reference
	/// strings <c>407350ad</c>, <c>NavigationManagerExtensions.cs:103-117</c>, and
	/// <c>ActivateSessionAndUpdateUrl</c> (AC5/AC6 traceability).
	/// </summary>
	[Fact]
	public async Task F7_MessageContent_ContainsRegressionReferences()
	{
		var source = @"using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using TestProject.Shared.Helpers;

namespace TestProject.Shared.Components.Pages
{
    public class VowDashboard : ComponentBase
    {
        private NavigationManager _nav = new NavigationManager();

        private async Task HandleClick()
        {
            await _nav.ReplaceUrlStateAsync(""/test"");
        }
    }
}
";

		var test = new CSharpAnalyzerTest<NavigationReplaceUrlContractAnalyzer, DefaultVerifier>
		{
			TestCode = source,
		};
		test.TestState.Sources.Add(BlazorComponentsStub);
		test.TestState.Sources.Add(NavigationExtensionsStub);

		// NNB041 fires at the ReplaceUrlStateAsync invocation. Argument {0} = enclosing method name.
		// Line 13: `            await _nav.ReplaceUrlStateAsync("/test");`
		// InvocationExpression: `_nav.ReplaceUrlStateAsync("/test")` starts col 19, length 33.
		test.ExpectedDiagnostics.Add(
			new DiagnosticResult(NavigationReplaceUrlContractAnalyzer.Rule)
				.WithSpan(13, 19, 13, 53)
				.WithArguments("HandleClick"));

		await test.RunAsync();

		// Additionally verify the MessageFormat string contains the required substrings.
		var messageFormat = NavigationReplaceUrlContractAnalyzer.Rule.MessageFormat.ToString();
		messageFormat.Should().Contain("407350ad",
			"MessageFormat must reference the regression commit");
		messageFormat.Should().Contain("NavigationManagerExtensions.cs:103-117",
			"MessageFormat must reference the contract location");
		messageFormat.Should().Contain("ActivateSessionAndUpdateUrl",
			"MessageFormat must reference the helper method");
	}
}
