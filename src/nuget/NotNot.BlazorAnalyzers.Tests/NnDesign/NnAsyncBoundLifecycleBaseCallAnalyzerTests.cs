using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Tests for <see cref="NnAsyncBoundLifecycleBaseCallAnalyzer"/> (NN_NND_ASYNC_002) — an
/// <c>NnAsyncBoundComponentBase&lt;TValue&gt;</c> subclass that overrides a lifecycle method the base
/// itself implements must call <c>base.{Method}(...)</c>.
/// <list type="number">
///   <item>P1 — plain <c>.cs</c> subclass override of <c>OnParametersSet</c> without base → REPORT.</item>
///   <item>P2 — the SAME override in a <c>// &lt;auto-generated/&gt;</c> tree (simulating <c>.razor</c>
///     <c>@code</c>) → STILL REPORT. Load-bearing proof of the <c>GeneratedCodeAnalysisFlags.Analyze</c>
///     divergence from the NN_NND_ASYNC_001 sibling.</item>
///   <item>P3 — <c>Dispose</c> override without <c>base.Dispose()</c> → REPORT.</item>
///   <item>N1 — override WITH <c>base.OnParametersSet()</c> → SILENT.</item>
///   <item>N2 — plain <c>ComponentBase</c> subclass override (NOT NnAsyncBoundComponentBase) → SILENT.</item>
///   <item>N3 — override of a method the base does NOT implement (<c>OnAfterRender</c>) → SILENT.</item>
///   <item>N4 — generated <c>.g.cs</c> Razor-emitted <c>BuildRenderTree</c> override (base =
///     ComponentBase) → SILENT. Load-bearing FP guard for the <c>Analyze</c> flag.</item>
///   <item>P4 — <c>OnInitialized</c> override without base → REPORT (highest-consequence: base
///     constructs the Behavior save machine).</item>
///   <item>P5 — TRANSITIVE 2-level chain <c>Leaf : IntermediateChained :
///     NnAsyncBoundComponentBase&lt;int&gt;</c>, leaf overrides <c>OnParametersSet</c> without base
///     → REPORT. Load-bearing proof the chain-walk loop resolves the deepest link.</item>
///   <item>N5 — same transitive chain, leaf WITH base → SILENT.</item>
///   <item>N6 — TRANSITIVE FP guard: <c>Leaf : IntermediateNewVirtual : ...</c>, leaf overrides the
///     intermediate's OWN new virtual without base → SILENT.</item>
///   <item>N7 — <c>OnInitialized</c> override WITH base → SILENT.</item>
///   <item>N8a/b/c — base-call FORM robustness (expression-bodied <c>=&gt; base.X()</c>; base call
///     inside <c>if</c>; base call inside <c>try</c>) → SILENT. Pins README's presence-based
///     promise. (An <c>await base.OnInitializedAsync()</c> fixture is N/A: the base implements only
///     SYNC lifecycle methods, so no async override resolves a qualifying link.)</item>
///   <item>N9 — latent-footgun boundary (NOW DOUBLE-GUARDED): a base-stub variant where the base
///     ITSELF overrides <c>BuildRenderTree</c> → SILENT, because <c>BuildRenderTree</c> is not a
///     whitelisted lifecycle name. Pins that the name whitelist neutralizes the Razor-synthesized-
///     member footgun independently of the base chain.</item>
///   <item>S1–S5 (SEAM SILENCE — the 37→0 over-fire fix) — a subclass overriding a base no-op POLICY
///     SEAM (<c>IsEmpty</c> / <c>Clamp</c> / <c>ApplyRevertedValue</c> / <c>NotifyValueChanged</c> /
///     <c>GetEffectivePreFlight</c>) WITHOUT calling base → SILENT. The seams exist to be replaced;
///     base-omitting overrides are correct (calling <c>IsEmpty</c> base reintroduces its <c>false</c>
///     default → latent bug). The name whitelist rejects them before the base-chain walk.</item>
/// </list>
/// </summary>
public class NnAsyncBoundLifecycleBaseCallAnalyzerTests
{
    // ── Base-class stubs (compilation-only — minimal lifecycle surface mirroring the real base) ──
    // ComponentBase provides no-op OnInitialized/OnParametersSet + virtual BuildRenderTree.
    // NnAsyncBoundComponentBase<T> provides NON-abstract OnInitialized/OnParametersSet/Dispose that
    // call into ComponentBase — the "base does real work" shape the analyzer keys on — PLUS the no-op
    // POLICY SEAMS (IsEmpty / Clamp / ApplyRevertedValue / NotifyValueChanged / GetEffectivePreFlight)
    // authored `=> default` / `{ }`. An override of a seam that omits base is CORRECT (the seam exists
    // to be replaced), so the analyzer must be SILENT on it — pinned by the S-series fixtures.
    private const string BaseStubs = @"
using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components.Rendering
{
    public sealed class RenderTreeBuilder { }
}

namespace Microsoft.AspNetCore.Components
{
    public abstract class ComponentBase
    {
        protected virtual void OnInitialized() { }
        protected virtual void OnParametersSet() { }
        protected virtual void OnAfterRender(bool firstRender) { }
        protected virtual void BuildRenderTree(Rendering.RenderTreeBuilder builder) { }
    }
}

namespace NotNot.BlazorDesign.NnDesign.AsyncBound
{
    using Microsoft.AspNetCore.Components;

    public abstract class NnAsyncBoundComponentBase<TValue> : ComponentBase, IDisposable
    {
        // Real-bookkeeping lifecycle (whitelisted — override-without-base is a defect).
        protected override void OnInitialized() { base.OnInitialized(); }
        protected override void OnParametersSet() { base.OnParametersSet(); }
        public virtual void Dispose() { }

        // No-op POLICY SEAMS (NOT whitelisted — override-without-base is CORRECT).
        protected virtual bool IsEmpty(TValue value) => false;
        protected virtual TValue Clamp(TValue value) => value;
        protected virtual void ApplyRevertedValue(TValue value) { }
        protected virtual Task NotifyValueChanged(TValue value) => Task.CompletedTask;
        protected virtual System.Func<TValue, Task>? GetEffectivePreFlight() => null;
    }
}
";

    // ── Intermediate-base stubs (transitive-chain fixtures) ───────────────────────────────
    // IntermediateChained correctly chains base on OnParametersSet — a leaf overriding the SAME
    // method without base must STILL fire (the chain-walk loop resolves the deepest link to the
    // NnAsyncBoundComponentBase impl two levels up). Proves OverridesNonAbstractBaseMethod walks
    // the FULL chain, not just the immediate override.
    private const string IntermediateChainedStub = @"
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign.Intermediates
{
    public abstract class IntermediateChained : NnAsyncBoundComponentBase<int>
    {
        protected override void OnParametersSet() { base.OnParametersSet(); }
    }
}
";

    // IntermediateNewVirtual introduces a NEW virtual method that NnAsyncBoundComponentBase does
    // NOT implement. A leaf overriding THAT method without base → SILENT (no qualifying
    // NnAsyncBoundComponentBase link in the override chain — the FP guard holds at the intermediate
    // layer, proving the analyzer keys on the base's own impls, not any ancestor's).
    private const string IntermediateNewVirtualStub = @"
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign.Intermediates
{
    public abstract class IntermediateNewVirtual : NnAsyncBoundComponentBase<int>
    {
        protected virtual void OnLayoutComputed() { }
    }
}
";

    // BaseStubs variant whose NnAsyncBoundComponentBase ALSO overrides BuildRenderTree (the
    // latent-footgun shape). Used by the RED regression fixture N9 to prove that IF the base ever
    // overrode a Razor-synthesized member, the generated BuildRenderTree override WOULD false-
    // positive — turning the soundness invariant into an executable boundary test.
    private const string BaseStubsWithBuildRenderTreeOverride = @"
using System;

namespace Microsoft.AspNetCore.Components.Rendering
{
    public sealed class RenderTreeBuilder { }
}

namespace Microsoft.AspNetCore.Components
{
    public abstract class ComponentBase
    {
        protected virtual void OnInitialized() { }
        protected virtual void OnParametersSet() { }
        protected virtual void OnAfterRender(bool firstRender) { }
        protected virtual void BuildRenderTree(Rendering.RenderTreeBuilder builder) { }
    }
}

namespace NotNot.BlazorDesign.NnDesign.AsyncBound
{
    using Microsoft.AspNetCore.Components;

    public abstract class NnAsyncBoundComponentBase<TValue> : ComponentBase, IDisposable
    {
        protected override void OnInitialized() { base.OnInitialized(); }
        protected override void OnParametersSet() { base.OnParametersSet(); }
        // INVARIANT-VIOLATING override (test-only): the real base MUST NOT do this.
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) { base.BuildRenderTree(builder); }
        public virtual void Dispose() { }
    }
}
";

    // ── Test helpers ──────────────────────────────────────────────────────────────────────

    private static async Task VerifyAsync(string source, string filePath, params DiagnosticResult[] expected)
    {
        await VerifyWithExtraSourcesAsync(source, filePath, BaseStubs, Array.Empty<string>(), expected);
    }

    /// <summary>
    /// Verification entrypoint allowing a custom base-stub (e.g. the BuildRenderTree-overriding
    /// invariant-violating variant) plus additional supporting source trees (intermediate bases).
    /// </summary>
    private static async Task VerifyWithExtraSourcesAsync(
        string source,
        string filePath,
        string baseStubs,
        string[] extraSources,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<NnAsyncBoundLifecycleBaseCallAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        test.TestState.Sources.Add((filePath, source));
        test.TestState.Sources.Add(baseStubs);
        foreach (var extra in extraSources)
            test.TestState.Sources.Add(extra);
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Diagnostic at a <c>{|#0:...|}</c> location marker — the framework derives the span; we supply
    /// only the ID, severity, and the <c>{0}</c> method-name argument.
    /// </summary>
    private static DiagnosticResult ExpectedAtMarker(string methodName)
    {
        return new DiagnosticResult(NnAsyncBoundLifecycleBaseCallAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(methodName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P1 — plain .cs subclass override of OnParametersSet without base → REPORT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_P1_OnParametersSetOverrideNoBaseCall_Reports()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void {|#0:OnParametersSet|}()
    {
        // Violation: no base.OnParametersSet() — freezes the _lastCommitted sidecar.
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/TestComponentP1.cs";
        await VerifyAsync(source, path, ExpectedAtMarker("OnParametersSet"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P2 (LOAD-BEARING) — same override inside a `// <auto-generated/>` tree
    // (simulating .razor @code) → STILL REPORT. Proves GeneratedCodeAnalysisFlags.Analyze.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_P2_GeneratedCodeOnParametersSetNoBaseCall_Reports()
    {
        var source = @"// <auto-generated/>
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class GeneratedComponent : NnAsyncBoundComponentBase<int>
{
    protected override void {|#0:OnParametersSet|}()
    {
        // Violation in Razor-generated code — the Analyze flag must surface this.
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/GeneratedComponent.razor.g.cs";
        await VerifyAsync(source, path, ExpectedAtMarker("OnParametersSet"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P3 — Dispose override without base.Dispose() → REPORT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_P3_DisposeOverrideNoBaseCall_Reports()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    public override void {|#0:Dispose|}()
    {
        // Violation: no base.Dispose() — the Behavior is never disposed.
    }
}
";
        var path = "/TestProject/Components/TestComponentP3.cs";
        await VerifyAsync(source, path, ExpectedAtMarker("Dispose"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N1 — override WITH base.OnParametersSet() → SILENT
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N1_OnParametersSetOverrideWithBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/TestComponentN1.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N2 — plain ComponentBase subclass override (NOT NnAsyncBoundComponentBase) → SILENT
    // (ComponentBase's OnParametersSet is a no-op; base-call is optional.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N2_PlainComponentBaseOverrideNoBaseCall_NotReported()
    {
        var source = @"using Microsoft.AspNetCore.Components;

namespace NotNot.BlazorDesign.NnDesign;

public class PlainComponent : ComponentBase
{
    protected override void OnParametersSet()
    {
        // No NnAsyncBoundComponentBase in the override chain — silent.
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/PlainComponentN2.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N3 — override of a method the base does NOT implement (OnAfterRender) → SILENT
    // (No NnAsyncBoundComponentBase link in the OverriddenMethod chain — base-call optional.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N3_BaseUnimplementedMethodOverrideNoBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void OnAfterRender(bool firstRender)
    {
        // NnAsyncBoundComponentBase does NOT override OnAfterRender — the resolved base impl is
        // ComponentBase's, not NnAsyncBoundComponentBase's, so the chain has no qualifying link.
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/TestComponentN3.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N4 (LOAD-BEARING FP GUARD) — generated .g.cs Razor-emitted BuildRenderTree override
    // (base = ComponentBase) → SILENT. The Analyze flag surfaces this method; the precise
    // base-chain check must reject it (its base is ComponentBase, not NnAsyncBoundComponentBase).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N4_GeneratedBuildRenderTreeOverride_NotReported()
    {
        var source = @"// <auto-generated/>
using Microsoft.AspNetCore.Components.Rendering;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class GeneratedComponent : NnAsyncBoundComponentBase<int>
{
    // Razor compiler emits this override; its base is ComponentBase.BuildRenderTree, which
    // NnAsyncBoundComponentBase does NOT override — no qualifying link → silent (no FP).
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Generated render body — no base.BuildRenderTree() call, and that is correct.
    }
}
";
        var path = "/TestProject/Components/GeneratedComponentN4.razor.g.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P4 — OnInitialized override without base.OnInitialized() → REPORT
    // (Highest-consequence: the base OnInitialized CONSTRUCTS the Behavior save machine.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_P4_OnInitializedOverrideNoBaseCall_Reports()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void {|#0:OnInitialized|}()
    {
        // Violation: no base.OnInitialized() — the Behavior save machine is never constructed.
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/TestComponentP4.cs";
        await VerifyAsync(source, path, ExpectedAtMarker("OnInitialized"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // P5 (TRANSITIVE — LOAD-BEARING) — 2-level chain Leaf : IntermediateChained :
    // NnAsyncBoundComponentBase<int>. IntermediateChained chains base correctly; Leaf overrides
    // the SAME OnParametersSet WITHOUT base → MUST REPORT. Proves the chain-walk loop resolves
    // the deepest link two levels up (a regression to immediate-override-only matching ships green
    // without this fixture).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_P5_TransitiveLeafOverrideNoBaseCall_Reports()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.Intermediates;

namespace NotNot.BlazorDesign.NnDesign;

public class LeafComponent : IntermediateChained
{
    protected override void {|#0:OnParametersSet|}()
    {
        // Violation: no base.OnParametersSet(). The base impl lives two levels up
        // (NnAsyncBoundComponentBase), reached via IntermediateChained.
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/LeafComponentP5.cs";
        await VerifyWithExtraSourcesAsync(
            source, path, BaseStubs, new[] { IntermediateChainedStub },
            ExpectedAtMarker("OnParametersSet"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N5 (TRANSITIVE SILENT) — Leaf : IntermediateChained : NnAsyncBoundComponentBase<int>.
    // Leaf overrides OnParametersSet WITH base → SILENT. Proves the transitive chain that DOES
    // call base is correctly accepted (no FP across the intermediate).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N5_TransitiveLeafOverrideWithBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.Intermediates;

namespace NotNot.BlazorDesign.NnDesign;

public class LeafComponent : IntermediateChained
{
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/LeafComponentN5.cs";
        await VerifyWithExtraSourcesAsync(source, path, BaseStubs, new[] { IntermediateChainedStub });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N6 (TRANSITIVE FP GUARD) — Leaf : IntermediateNewVirtual : NnAsyncBoundComponentBase<int>.
    // The intermediate introduces a NEW virtual OnLayoutComputed that NnAsyncBoundComponentBase
    // does NOT implement; Leaf overrides it WITHOUT base → SILENT. Proves the analyzer keys on the
    // base's OWN impls, not any ancestor's virtual — the FP guard holds at the intermediate layer.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N6_TransitiveIntermediateNewVirtualOverride_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.Intermediates;

namespace NotNot.BlazorDesign.NnDesign;

public class LeafComponent : IntermediateNewVirtual
{
    protected override void OnLayoutComputed()
    {
        // OnLayoutComputed is the intermediate's OWN virtual — no NnAsyncBoundComponentBase link
        // in the chain → silent (base-call optional).
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/LeafComponentN6.cs";
        await VerifyWithExtraSourcesAsync(source, path, BaseStubs, new[] { IntermediateNewVirtualStub });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N7 (OnInitialized SILENT) — OnInitialized override WITH base.OnInitialized() → SILENT.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N7_OnInitializedOverrideWithBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var x = 1;
    }
}
";
        var path = "/TestProject/Components/TestComponentN7.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N8 (BASE-CALL FORM ROBUSTNESS) — the README promises expression-bodied + conditional +
    // await forms are recognized. Each sub-fixture is a SILENT case proving presence-based
    // detection (BodyContainsBaseCall) sees the base call regardless of syntactic form (no FP).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N8a_ExpressionBodiedBaseCall_NotReported()
    {
        // Expression-bodied override: `=> base.OnParametersSet()`.
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void OnParametersSet() => base.OnParametersSet();
}
";
        var path = "/TestProject/Components/TestComponentN8a.cs";
        await VerifyAsync(source, path);
    }

    [Fact]
    public async Task Test_N8b_ConditionalBaseCallInsideIf_NotReported()
    {
        // Base call nested inside an `if` block — presence-based descendant scan must find it.
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void OnParametersSet()
    {
        if (1 == 1)
        {
            base.OnParametersSet();
        }
    }
}
";
        var path = "/TestProject/Components/TestComponentN8b.cs";
        await VerifyAsync(source, path);
    }

    [Fact]
    public async Task Test_N8c_BaseCallInsideTry_NotReported()
    {
        // Base call nested inside a `try` block — presence-based descendant scan must find it.
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override void OnParametersSet()
    {
        try
        {
            base.OnParametersSet();
        }
        catch
        {
        }
    }
}
";
        var path = "/TestProject/Components/TestComponentN8c.cs";
        await VerifyAsync(source, path);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // N9 (LATENT FOOTGUN BOUNDARY — NOW DOUBLE-GUARDED) — uses a base-stub variant where
    // NnAsyncBoundComponentBase ITSELF overrides BuildRenderTree (the invariant the real base MUST
    // NOT violate). Under the base-chain-ONLY predicate this false-positived; the NAME WHITELIST now
    // adds a SECOND, independent guard — BuildRenderTree is not one of the three real-bookkeeping
    // lifecycle names (OnInitialized/OnParametersSet/Dispose), so the analyzer is SILENT even when the
    // base overrides it. This fixture pins that the whitelist neutralizes the footgun: a future base
    // edit overriding a Razor-synthesized member no longer false-positives on every generated
    // component (the consequence the invariant comment guards against). N4 pins the safe shape.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N9_BaseOverridesBuildRenderTree_WhitelistGuardsFootgun_NotReported()
    {
        var source = @"// <auto-generated/>
using Microsoft.AspNetCore.Components.Rendering;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class GeneratedComponent : NnAsyncBoundComponentBase<int>
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // Razor-emitted override with no base call. Even though the (test-only) base ALSO overrides
        // BuildRenderTree non-abstractly, BuildRenderTree is NOT a whitelisted lifecycle name, so the
        // analyzer is SILENT — the name whitelist guards the footgun independently of the base chain.
    }
}
";
        var path = "/TestProject/Components/GeneratedComponentN9.razor.g.cs";
        await VerifyWithExtraSourcesAsync(
            source, path, BaseStubsWithBuildRenderTreeOverride, Array.Empty<string>());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // S-series (SEAM SILENCE — the over-fire fix) — a subclass overriding one of the base's no-op
    // POLICY SEAMS WITHOUT calling base → SILENT. These seams are authored `=> default` / `{ }` to be
    // REPLACED; a base-omitting override is CORRECT (for IsEmpty, calling base would reintroduce the
    // `=> false` default → latent bug). Pins the 37→0 false-positive fix: the name whitelist rejects
    // every seam before the base-chain walk. Contrast P1/P3/P4 (lifecycle-without-base → REPORT).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_S1_IsEmptySeamOverrideNoBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override bool IsEmpty(int value) => value == 0;
}
";
        var path = "/TestProject/Components/TestComponentS1.cs";
        await VerifyAsync(source, path);
    }

    [Fact]
    public async Task Test_S2_ClampSeamOverrideNoBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override int Clamp(int value) => value < 0 ? 0 : value;
}
";
        var path = "/TestProject/Components/TestComponentS2.cs";
        await VerifyAsync(source, path);
    }

    [Fact]
    public async Task Test_S3_ApplyRevertedValueSeamOverrideNoBaseCall_NotReported()
    {
        var source = @"using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    private int _value;
    protected override void ApplyRevertedValue(int value)
    {
        // Assigns the control's own bound field — no base call (base is a no-op).
        _value = value;
    }
}
";
        var path = "/TestProject/Components/TestComponentS3.cs";
        await VerifyAsync(source, path);
    }

    [Fact]
    public async Task Test_S4_NotifyValueChangedSeamOverrideNoBaseCall_NotReported()
    {
        var source = @"using System.Threading.Tasks;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override Task NotifyValueChanged(int value) => Task.CompletedTask;
}
";
        var path = "/TestProject/Components/TestComponentS4.cs";
        await VerifyAsync(source, path);
    }

    [Fact]
    public async Task Test_S5_GetEffectivePreFlightSeamOverrideNoBaseCall_NotReported()
    {
        var source = @"using System;
using System.Threading.Tasks;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class TestComponent : NnAsyncBoundComponentBase<int>
{
    protected override Func<int, Task>? GetEffectivePreFlight() => v => Task.CompletedTask;
}
";
        var path = "/TestProject/Components/TestComponentS5.cs";
        await VerifyAsync(source, path);
    }
}
