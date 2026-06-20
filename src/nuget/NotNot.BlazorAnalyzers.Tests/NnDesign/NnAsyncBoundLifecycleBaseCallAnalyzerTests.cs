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
///   <item>N9 — RED regression / latent-footgun boundary: a base-stub variant where the base ITSELF
///     overrides <c>BuildRenderTree</c> → generated <c>BuildRenderTree</c> override FALSE-POSITIVES.
///     Asserts the FP, documenting why the base must never override a Razor-synthesized member.</item>
/// </list>
/// </summary>
public class NnAsyncBoundLifecycleBaseCallAnalyzerTests
{
    // ── Base-class stubs (compilation-only — minimal lifecycle surface mirroring the real base) ──
    // ComponentBase provides no-op OnInitialized/OnParametersSet + virtual BuildRenderTree.
    // NnAsyncBoundComponentBase<T> provides NON-abstract OnInitialized/OnParametersSet/Dispose that
    // call into ComponentBase — exactly the "base does real work" shape the analyzer keys on.
    private const string BaseStubs = @"
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
        public virtual void Dispose() { }
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
    // N9 (RED REGRESSION — LATENT FOOTGUN BOUNDARY TEST) — uses a base-stub variant where
    // NnAsyncBoundComponentBase ITSELF overrides BuildRenderTree (the invariant the real base
    // MUST NOT violate). A generated component's Razor-emitted BuildRenderTree override WOULD then
    // resolve a qualifying NnAsyncBoundComponentBase link → the analyzer FALSE-POSITIVES. This
    // fixture ASSERTS that FP, turning the soundness invariant into an executable boundary test:
    // if a future base edit overrides BuildRenderTree, THIS fixture's expected-diagnostic makes
    // the consequence loud (it documents WHY the invariant comment exists). The real base does NOT
    // override BuildRenderTree (N4 pins the safe shape); this fixture pins the dangerous one.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Test_N9_BaseOverridesBuildRenderTree_FalsePositiveBoundary_Reports()
    {
        var source = @"// <auto-generated/>
using Microsoft.AspNetCore.Components.Rendering;
using NotNot.BlazorDesign.NnDesign.AsyncBound;

namespace NotNot.BlazorDesign.NnDesign;

public class GeneratedComponent : NnAsyncBoundComponentBase<int>
{
    protected override void {|#0:BuildRenderTree|}(RenderTreeBuilder builder)
    {
        // Razor-emitted override with no base call. Because the (test-only) base ALSO overrides
        // BuildRenderTree non-abstractly, the chain now contains a qualifying link → the analyzer
        // fires. This is the documented latent footgun, asserted here as a boundary test.
    }
}
";
        var path = "/TestProject/Components/GeneratedComponentN9.razor.g.cs";
        await VerifyWithExtraSourcesAsync(
            source, path, BaseStubsWithBuildRenderTreeOverride, Array.Empty<string>(),
            ExpectedAtMarker("BuildRenderTree"));
    }
}
