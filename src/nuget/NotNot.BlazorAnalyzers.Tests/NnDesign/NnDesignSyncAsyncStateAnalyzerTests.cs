using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests for <see cref="NnDesignSyncAsyncStateAnalyzer"/> (NN_NND_ASYNC_001).
/// Verifies the three syntactic detection patterns plus namespace-scope filter:
/// <list type="number">
///   <item>Pattern 1 — sync method body reads <c>_behavior.IsInFlight</c></item>
///   <item>Pattern 2 — <c>.GetAwaiter().GetResult()</c> on <c>_behavior.NotifyChange</c></item>
///   <item>Pattern 3 — sync lambda passed as <c>ValueChanged</c> captures <c>_behavior</c></item>
///   <item>Filter — same patterns in non-<c>NotNot.BlazorDesign</c> namespace produce zero diagnostics</item>
/// </list>
/// Additional negative tests cover the ALLOWED contexts (async method, computed property,
/// async lambda) that retrofitted components rely on for legitimate render-time reads.
/// </summary>
public class NnDesignSyncAsyncStateAnalyzerTests
{
    // ── NnDesign mixin stub (compilation-only — minimal surface) ─────────────────────────

    private const string MixinStubs = @"
using System;
using System.Threading.Tasks;

namespace NotNot
{
    public readonly struct Maybe<T>
    {
        public bool IsSuccess => true;
        public T? Value => default;
        public static Maybe<T> Success(T value) => default;
    }
}

namespace NotNot.BlazorDesign.NnDesign.AsyncBound
{
    public sealed class NnAsyncBoundBehavior<T>
    {
        public bool IsInFlight { get; private set; }
        public bool InDebounce { get; private set; }
        public bool SpinnerVisible { get; private set; }
        public string? InlineErrorMessage { get; private set; }
        public int? LastError { get; private set; }
        public Task<NotNot.Maybe<T>> NotifyChange(T value) => Task.FromResult(NotNot.Maybe<T>.Success(value));
        public Task<NotNot.Maybe<T>> ExecuteAsync(T value) => Task.FromResult(NotNot.Maybe<T>.Success(value));
    }
}
";

    // ── Test helpers ──────────────────────────────────────────────────────────────────────

    private static async Task VerifyAsync(string source, string filePath, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<NnDesignSyncAsyncStateAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.Sources.Add((filePath, source));
        test.TestState.Sources.Add(MixinStubs);
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Expected(string filePath, int line, int column, int endLine, int endColumn,
        string enclosingDescription, string stateRef)
    {
        return new DiagnosticResult(NnDesignSyncAsyncStateAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithSpan(filePath, line, column, endLine, endColumn)
            .WithArguments(enclosingDescription, stateRef);
    }

    /// <summary>
    /// Variant for tests that use <c>{|#0:...|}</c> location-marker syntax in the source — the
    /// framework derives the span from the marker, so we only supply diagnostic ID + arguments.
    /// </summary>
    private static DiagnosticResult ExpectedAtMarker(string enclosingDescription, string stateRef)
    {
        return new DiagnosticResult(NnDesignSyncAsyncStateAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(enclosingDescription, stateRef);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern 1 — sync method body reads `_behavior.IsInFlight` → REPORT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_Pattern1_SyncMethodReadsMixinState_Reports()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public void OnSyncHandler()
    {
        if (_behavior != null && {|#0:_behavior.IsInFlight|})
        {
            // Violation: sync method body reads mixin state.
        }
    }
}
";
        var path = "/TestProject/Components/TestComponent.cs";
        // Marker {|#0:...|} wraps the exact `_behavior.IsInFlight` member-access node — the
        // testing framework derives span automatically. Note: `_behavior != null` is parsed
        // as IdentifierNameSyntax `_behavior`, NOT a MemberAccessExpression, so the analyzer
        // does not fire on it.
        await VerifyAsync(source, path,
            ExpectedAtMarker("method 'OnSyncHandler'", "_behavior.IsInFlight"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern 2 — `.GetAwaiter().GetResult()` on `_behavior.NotifyChange` → REPORT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_Pattern2_GetAwaiterGetResultOnNotifyChange_Reports()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public void OnSyncHandler(bool value)
    {
        // Violation: blocking sync wait on mixin async method.
        var result = {|#0:_behavior!.NotifyChange(value).GetAwaiter().GetResult()|};
    }
}
";
        var path = "/TestProject/Components/TestComponent.cs";
        // Marker {|#0:...|} wraps the full GetAwaiter().GetResult() invocation; framework
        // derives span automatically. Receiver uses `!.` (null-forgiving) — the analyzer's
        // TryGetMixinReceiverName helper unwraps PostfixUnaryExpressionSyntax to reach the
        // inner `_behavior` IdentifierName.
        await VerifyAsync(source, path,
            ExpectedAtMarker("method 'OnSyncHandler'",
                "_behavior.NotifyChange().GetAwaiter().GetResult()"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern 3 — sync lambda registered as `ValueChanged` captures `_behavior` → REPORT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_Pattern3_SyncLambdaCapturesBehavior_Reports()
    {
        var source = @"using System;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public Action<bool> ValueChanged { get; set; } = _ => { };

    public void Wire()
    {
        // Violation: sync lambda assigned to a 'ValueChanged'-named field captures _behavior.
        ValueChanged = {|#0:v => { var _ = _behavior; }|};
    }
}
";
        var path = "/TestProject/Components/TestComponent.cs";
        // Marker wraps the sync lambda; framework derives span. The lambda is assigned to a
        // field whose name is 'ValueChanged' (matches SyncCallbackNames) AND captures the
        // mixin field _behavior — both gates fire.
        await VerifyAsync(source, path,
            ExpectedAtMarker("method 'Wire'",
                "sync lambda captures '_behavior'"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Namespace-scope filter — same Pattern 1 violation in consumer namespace → NO diagnostic
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_NamespaceScope_ConsumerCodeNotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace Some.Consumer.Project;

public class ConsumerComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public void OnSyncHandler()
    {
        if (_behavior != null && _behavior.IsInFlight)
        {
            // Same shape as Pattern 1 — but namespace is NOT NotNot.BlazorDesign.*,
            // so the analyzer should NOT report.
        }
    }
}
";
        var path = "/TestProject/Components/ConsumerComponent.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negative — async method reads `_behavior.SpinnerVisible` → NO diagnostic
    // (Retrofitted components do this legitimately inside async handlers.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_AsyncMethodReadsMixinState_NotReported()
    {
        var source = @"using System.Threading.Tasks;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public async Task OnAsyncHandlerAsync()
    {
        await Task.Delay(1);
        if (_behavior != null && _behavior.SpinnerVisible)
        {
            // Allowed: async method body — race window is structured.
        }
    }
}
";
        var path = "/TestProject/Components/TestComponentAsync.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negative — computed property getter reads `_behavior.InDebounce` → NO diagnostic
    // (The R3 retrofit's `EffectiveDisabled` getter pattern.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_ComputedPropertyReadsMixinState_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    private bool EffectiveDisabled => _behavior != null && _behavior.InDebounce;
}
";
        var path = "/TestProject/Components/TestComponentComputed.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negative — Task-returning sync-modifier-less method reads state → NO diagnostic
    // (e.g. `private Task<int> HandlerAsync()` without `async` keyword still allowed.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_TaskReturningMethodReadsMixinState_NotReported()
    {
        var source = @"using System.Threading.Tasks;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public Task<bool> HandlerAsync()
    {
        return Task.FromResult(_behavior != null && _behavior.IsInFlight);
    }
}
";
        var path = "/TestProject/Components/TestComponentTaskReturning.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negative — async lambda registered as ValueChanged captures _behavior → NO diagnostic
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_AsyncLambdaCapturesBehavior_NotReported()
    {
        var source = @"using System;
using System.Threading.Tasks;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent
{
    private NnAsyncBoundBehavior<bool>? _behavior;

    public Func<bool, Task> ValueChanged { get; set; } = _ => Task.CompletedTask;

    public void Wire()
    {
        // Allowed: lambda is async — observing mixin state is fine.
        ValueChanged = async v => { await Task.Delay(1); var _ = _behavior; };
    }
}
";
        var path = "/TestProject/Components/TestComponentAsyncLambda.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negative — dual-mixin NnChipSet pattern: computed-property reads of `_singleBehavior` /
    // `_multiBehavior` in an arrow-bodied `EffectiveReadOnly` getter → NO diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_DualMixinComputedProperty_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;
using System.Collections.Generic;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponentDual
{
    private NnAsyncBoundBehavior<bool>? _singleBehavior;
    private NnAsyncBoundBehavior<IReadOnlyCollection<bool>>? _multiBehavior;

    private bool EffectiveReadOnly =>
        (_singleBehavior != null && _singleBehavior.InDebounce)
        || (_multiBehavior != null && _multiBehavior.SpinnerVisible);
}
";
        var path = "/TestProject/Components/TestComponentDual.cs";
        await VerifyAsync(source, path);
    }
}
