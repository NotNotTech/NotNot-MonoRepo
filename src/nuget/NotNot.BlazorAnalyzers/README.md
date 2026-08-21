# NotNot.BlazorAnalyzers

**Blazor-specific Roslyn analyzers for lifecycle management and best practices.**

[![NuGet](https://img.shields.io/nuget/v/NotNot.BlazorAnalyzers.svg)](https://www.nuget.org/packages/NotNot.BlazorAnalyzers/)
[![License: MPL-2.0](https://img.shields.io/badge/License-MPL--2.0-blue.svg)](https://opensource.org/licenses/MPL-2.0)

## Overview

NotNot.BlazorAnalyzers provides Roslyn analyzers specifically designed for Blazor component development. These analyzers help catch common lifecycle management issues that can lead to memory leaks, disposed object access, and circuit disconnection errors.

## Installation

```bash
dotnet add package NotNot.BlazorAnalyzers
```

Or in your `.csproj`:

```xml
<PackageReference Include="NotNot.BlazorAnalyzers" Version="*" PrivateAssets="all" />
```

## Analyzer Rules

<a id="nnb022"></a>
### NNB022: Direct MudBlazor reference forbidden — use Nn* wrapper

**Severity:** Error
**Category:** BannedComponents
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy · `protocols/nndesign.md` ENFORCEMENT section

End-using applications must not reference MudBlazor directly. NNB022 is the build-time enforcement of the NnDesign policy: MudBlazor is the general-purpose primitive layer wrapped internally by `NotNot.BlazorDesign.NnDesign`. Consumer Blazor projects compose UI exclusively from `Nn*` components — the wrappers carry brand defaults, dense margins, opinionated dropdown widths, and any cross-cutting fixes that must apply uniformly. Direct `Mud*` references in consumer code force every call site to repeat brand boilerplate, multiply places where the brand drifts, and create N-1 places where a single bug fix must be repeated.

**Hybrid analyzer with three detection pathways**:

1. **`<Mud*>` markup tags in `.razor` files** — text-scan via AdditionalFiles (e.g. `<MudButton>`, `<MudCard>`, `<MudIconButtonGroup />`). All tags matching `<Mud[A-Z]\w*` fire uniformly — no skip-list.
2. **`using MudBlazor;` directives + `Mud*` / `IMud*` identifiers in `.razor.cs` code-behind files** — text-scan via AdditionalFiles. Bare `using MudBlazor;` reports independently (presumed feature consumption per partial-class semantics).
3. **Plain `.cs` files** — Roslyn semantic analysis via `SyntaxNodeAction` on `UsingDirective` and `IdentifierName`. Symbol resolution walks the namespace chain to root; reports when the root namespace equals `MudBlazor`.

```razor
@* ❌ Direct MudBlazor reference — NNB022 fires *@
<MudButton Color="Color.Primary">Click</MudButton>

@* ✅ Use the Nn* wrapper from NotNot.BlazorDesign *@
<NnButton OnClick="HandleClick">Click</NnButton>
```

```csharp
// ❌ NNB022 fires on `using MudBlazor;` in plain .cs file
using MudBlazor;
public static class StatusFormatHelper
{
    public static Color GetStatusColor() => Color.Primary; // also fires on Color refs
}
```

**Path-based exception buckets** (analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| NotNot.BlazorDesign internals | Any file under `**/NotNot.BlazorDesign/**` (the wrappers consume MudBlazor internally — that's their job) |
| NnDesignSamples folder | Any file under `**/NnDesignSamples/**` (sample/demo pages exhibiting both wrappers and primitives) |
| Pages/Samples folder | Any file under `**/Pages/Samples/**` (consumer sample/demo + dev-harness pages; same carve-out the NN_RM_001 / NN_ABMCS_* suites apply) |
| Root provider wiring | `App.razor`, `Program.cs`, `VowMudLocalizer.cs` (matched by file name) |
| Dev/test/legacy harness pages | Explicit allow-list: `BlazorTermTestHarness.{Wasm,Server}.razor`, `BlazorTermHarmonizedHarness.{Wasm,Server}.razor`, `BlazorTermDiffTest.razor`, `BlazorTermTabTest.razor`, `BlazorTermTest.razor`, `HarnessDummyTab.razor`, `HarnessTerminalWrapper.razor`, `RichEditExamplePage.razor`, `NnDesignSamplesPage.razor`, `DashboardLegacy.razor`, `TestInputs.razor` |

**Per-file opt-out** (for documented call-site cases — e.g. transitional retentions awaiting a Tier 2 / Tier 3 wrapper):

```razor
@* nnb022:allow-mudblazor: pending NnMenu wrapper *@
<MudMenu>
    <MudMenuItem>One</MudMenuItem>
</MudMenu>
```

```csharp
// nnb022:allow-mudblazor: deliberate adapter extending MudLocalizer API
using MudBlazor;
```

The marker can appear anywhere in the file. The text after the second colon is documentation only — the analyzer does not parse it. Code review enforces a non-empty rationale.

**Project-wide kill-switch** (rare; prefer per-file opt-out):

```xml
<PropertyGroup>
  <NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled>
</PropertyGroup>
```

**Default severity is Error** — the consumer (VOW) is verified `Mud*`-clean modulo the enumerated carve-outs, so any new direct `Mud*` reference in non-exempt consumer code breaks the build. To soften locally (rare), override via `.editorconfig`:

```ini
[*.{razor,cs}]
dotnet_diagnostic.NNB022.severity = warning
```

<a id="nn_lddd_001"></a>
### NN_LDDD_001: Server-Assembly Type Referenced in Shared/Client Code

**Severity:** Warning
**Category:** LiteDDD.AssemblyFence

Detects when a `.Shared` or `.Client` assembly references a type defined in a `.Server` assembly. The LiteDDD architecture treats `.Server` assemblies as the home of domain logic, database entities, and application services; Shared/Client must communicate via Refit data-service interfaces and DTOs, never via direct type references.

The analyzer hooks a `SyntaxNodeAction` on `IdentifierName`, resolves the symbol via the semantic model, walks to the symbol's containing assembly, and checks whether the assembly identity name ends in `.Server`. Self-references (same compilation) are ignored.

```csharp
// In Novaleaf.VibeOverwatch.Shared:

// ❌ NN_LDDD_001 fires — type lives in Novaleaf.VibeOverwatch.Server
using Novaleaf.VibeOverwatch.Server.Features.SessionManagement.AppLogic;
public class SomeShared { private SessionUseCases _svc; }

// ✅ Inject a Refit interface defined in Shared/Features/*/Contracts/ instead
public class SomeShared { [Inject] private IVowSessionDataService Svc { get; set; } = null!; }
```

**Fix:** Move the shared type to `Shared/Features/{Feature}/Contracts/` or define a DTO in Shared. If the type is genuinely server-side, route through a Refit data-service interface marked with `[LdddDataService]`. For sibling primitive libraries that have no notion of LiteDDD layering, apply `[assembly: LdddBypass]`.

<a id="nn_lddd_002"></a>
### NN_LDDD_002: Server-Namespace Using Directive in Shared/Client Code

**Severity:** Warning
**Category:** LiteDDD.AssemblyFence

Detects `using` directives that import a server-side namespace into a `.Shared` or `.Client` assembly. Even when no symbol from the namespace is referenced, the import creates a dependency direction that violates the assembly fence.

Hybrid detection across three pathways:

1. **Plain `.cs` files** — Roslyn semantic analysis via `SyntaxNodeAction` on `UsingDirective`. The namespace symbol is resolved semantically, and the analyzer checks whether the namespace contains a `.Server.` mid-segment or ends in `.Server`. A text-pattern fallback applies when the namespace fails to resolve (e.g., when the Server assembly is not referenced by the Shared compilation).
2. **`.razor.cs` code-behind files** — text-scan via AdditionalFiles using a line-anchored regex on `^\s*@?using\s+([\w\.]+\.Server(?:\.[\w\.]+)?)\s*;?\s*$`.
3. **`.razor` markup files** — same regex applied to `@using` directives.

```razor
@* In Novaleaf.VibeOverwatch.Shared/Components/Pages/SomePage.razor *@

@* ❌ NN_LDDD_002 fires *@
@using Novaleaf.VibeOverwatch.Server.Features.SessionManagement.AppLogic

@* ✅ Import the Shared contracts namespace instead *@
@using Novaleaf.VibeOverwatch.Features.SessionManagement.Contracts
```

**Fix:** Remove the import and route through a Shared contracts namespace. If shared types are needed, relocate them to `Shared/Features/{Feature}/Contracts/`. Bypass via `[assembly: LdddBypass]` for sibling primitive libraries.

<a id="nn_lddd_003"></a>
### NN_LDDD_003: Injected Service Not Marked [LdddDataService]

**Severity:** Warning
**Category:** LiteDDD.ComponentBoundary

Detects properties marked with `[Inject]` on `ComponentBase`-derived classes when the injected type lacks the `[LdddDataService]` marker AND is not in the framework allow-list. The LiteDDD principle is that the Blazor component layer composes presentation from a narrow set of wire-level Refit data-services plus a curated set of framework infrastructure types.

A `SymbolAction` on `NamedType` iterates members carrying the `[Inject]` attribute (matched by simple-name + namespace `Microsoft.AspNetCore.Components`). For each injected type, the analyzer checks the `[LdddDataService]` marker (simple-name OR fully-qualified-name) and the framework allow-list. The allow-list is matched against `OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` with the `global::` prefix stripped, so generic types match their open-generic allow-list entries.

**Framework allow-list** (built into the analyzer, evolves via PR):

- `Microsoft.AspNetCore.Components.NavigationManager`
- `Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider`
- `Microsoft.Extensions.Logging.ILogger`, `Microsoft.Extensions.Logging.ILogger<T>`
- `Microsoft.JSInterop.IJSRuntime`
- `Microsoft.Extensions.Configuration.IConfiguration`
- `Microsoft.Extensions.Localization.IStringLocalizer`, `Microsoft.Extensions.Localization.IStringLocalizer<T>`
- `System.Net.Http.IHttpClientFactory`, `System.Net.Http.HttpClient`
- `MudBlazor.IDialogService`, `MudBlazor.ISnackbar`
- `Microsoft.AspNetCore.SignalR.Client.HubConnection`

```csharp
public class MyPage : ComponentBase
{
    // ❌ NN_LDDD_003 fires — IMyHelper has no marker and is not in the allow-list
    [Inject] public IMyHelper Helper { get; set; } = null!;

    // ✅ No diagnostic — IVowSessionDataService is marked [LdddDataService]
    [Inject] public IVowSessionDataService SessionSvc { get; set; } = null!;

    // ✅ No diagnostic — framework allow-list
    [Inject] public NavigationManager Nav { get; set; } = null!;
}
```

**Fix:** Mark the injected service interface with `[NotNot.Bcl.Diagnostics.LdddDataService]` if it is a Refit data-service contract. Otherwise add the type to the framework allow-list in `LdddInjectAndCallAnalyzer.FrameworkAllowList`. For class-scope suppression, apply `[LdddBypass]` to the component class; for whole-assembly opt-out, apply `[assembly: LdddBypass]`.

<a id="nn_lddd_004"></a>
### NN_LDDD_004: Entity Framework DbContext Referenced in Shared/Client Code

**Severity:** Warning
**Category:** LiteDDD.AssemblyFence

Detects fields, properties, or parameters typed as `Microsoft.EntityFrameworkCore.DbContext` (or any subclass) inside a `.Shared` or `.Client` assembly. Persistence concerns live behind the Server boundary — leaking the ORM into the presentation layer violates LiteDDD layering.

A `SymbolAction` on `Field` and `Property` symbols plus a `SyntaxNodeAction` on `Parameter` syntax nodes walks the inheritance chain via `INamedTypeSymbol.BaseType` checking for the fully-qualified type `Microsoft.EntityFrameworkCore.DbContext`. Type-name-only matching is intentionally avoided — custom types named `DbContext` in other namespaces would false-positive.

```csharp
// In Novaleaf.VibeOverwatch.Shared:

// ❌ NN_LDDD_004 fires
public class SomeShared
{
    private VowDbContext _db;  // VowDbContext : DbContext
}

// ✅ Route through a Refit data-service interface instead
public class SomeShared
{
    [Inject] private IVowSessionDataService SessionSvc { get; set; } = null!;
}
```

**Fix:** Move the DbContext access into the Server project. Expose the needed data via a Refit data-service interface marked with `[LdddDataService]` in `Shared/Features/{Feature}/Contracts/`, and inject the interface from the component. For class-scope suppression, apply `[LdddBypass]` to the containing type.

<a id="nn_lddd_005"></a>
### NN_LDDD_005: Direct Call to [LdddDomainService] from Blazor Component

**Severity:** Warning
**Category:** LiteDDD.ComponentBoundary

Detects direct method invocations on a receiver whose type is marked `[LdddDomainService]`, when the call site is inside a `ComponentBase`-derived class. Domain services represent server-side compute that must be invoked via a Refit data-service proxy — calling them directly from a component bypasses the wire boundary.

A `SyntaxNodeAction` on `InvocationExpression` matches calls of the form `receiver.Method(...)` (only explicit member access — implicit `this`-calls and static calls are out of scope for V1). The receiver's type is resolved via the semantic model, walked through the inheritance chain (bounded at 100 levels) checking for `[LdddDomainService]` by simple-name OR fully-qualified-name. The enclosing class must be `ComponentBase`-derived. Property access on a domain-service receiver is intentionally NOT diagnosed — only method invocations are.

```csharp
public class MyPage : ComponentBase
{
    private readonly PathResolver _resolver = new();  // PathResolver carries [LdddDomainService]

    private void Compute()
    {
        // ❌ NN_LDDD_005 fires
        var result = _resolver.Resolve(input);
    }
}

// ✅ Inject an IEzLinkDetectionService (Refit, marked [LdddDataService]) and await
//    a server endpoint that internally calls PathResolver.
```

**Fix:** Move the domain compute behind a Refit data-service endpoint. Define the call as a method on the appropriate `[LdddDataService]`-marked interface in `Shared/Features/{Feature}/Contracts/`, implement the server-side delegation in `Server/Features/{Feature}/AppLogic/`, and call the proxied interface from the component. For class-scope suppression, apply `[LdddBypass]` to the component class.

### NN_LDDD_* — Bypass Mechanisms and Path Exemptions

The NN_LDDD_* suite shares a layered bypass model:

- **Whole-assembly bypass** — `[assembly: NotNot.Bcl.Diagnostics.LdddBypass]` (or a local internal copy declared as `[assembly: LdddBypass]`) short-circuits all five rules. Used by sibling primitive libraries (e.g., `NotNot.BlazorComponents`, `NotNot.BlazorDesign`) that have no notion of LiteDDD layering.
- **Type-scope bypass (Wave 2 rules only)** — `[LdddBypass]` on a class or method suppresses `NN_LDDD_003` and `NN_LDDD_005` for that scope. The Wave 1 assembly-fence rules (`NN_LDDD_001`, `NN_LDDD_002`, `NN_LDDD_004`) ignore type-scope `[LdddBypass]` — use the assembly-level form for Wave 1 carve-outs.
- **Path exemption** — Files under `Pages/Samples/**` are exempt from all five rules. This parallels the NNB022 carve-out: pedagogical sample pages may reference Server-side types and call domain services directly for demonstration purposes.

The attribute markers (`LdddDataServiceAttribute`, `LdddDomainServiceAttribute`, `LdddBypassAttribute`) live in `NotNot.Bcl.Core` under the `NotNot.Bcl.Diagnostics` namespace. The analyzer matches the attributes by simple name as well as fully-qualified name, so consumers may declare local internal copies to avoid taking a runtime reference on the analyzer assembly.

**Severity escalation** — escalate via `.editorconfig` once the LiteDDD migration backlog reaches zero:

```ini
[*.{razor,cs}]
dotnet_diagnostic.NN_LDDD_001.severity = error
dotnet_diagnostic.NN_LDDD_002.severity = error
dotnet_diagnostic.NN_LDDD_003.severity = error
dotnet_diagnostic.NN_LDDD_004.severity = error
dotnet_diagnostic.NN_LDDD_005.severity = error
```

<a id="nn_rm_001"></a>
### NN_RM_001: Per-page `@rendermode` directive forbidden (Pattern 2 reflection is canonical)

**Severity:** Warning
**Category:** RenderMode
**Authority:** Consumer-app render-mode architecture (e.g., `Novaleaf.VibeOverwatch/BlazorArchitecture.vibeKnowledge.md`) — applies whenever a consumer adopts **Pattern 2 reflection** at `App.razor` as the single render-mode declaration site.

Detects `@rendermode` directives in `.razor` files. Pattern 2 reflection at `App.razor` (typically a getter binding `HttpContext.GetEndpoint()?.Metadata.GetMetadata<RenderModeAttribute>()?.Mode` to `<HeadOutlet>` + `<Routes>`) reads per-route render-mode metadata via endpoint reflection. Per-page `@rendermode @(...)` directives re-introduce the layout-tier LCA reconciliation defect documented at [`dotnet/aspnetcore#52768`](https://github.com/dotnet/aspnetcore/issues/52768): stacked WASM directives downgrade to `"type":"auto"` boundary markers, silently breaking JS interop reach (popovers/tooltips/dropdowns).

The analyzer registers a `CompilationAction` that scans `AdditionalFiles` for `.razor` files using the multiline regex `^\s*@rendermode\b` (word-boundary; catches any directive value — canonical `InteractiveWebAssemblyRenderMode` literal, `InteractiveServer`, `InteractiveAuto`, or layout-tier placements). The diagnostic location anchors at the directive's line/column via `SourceText.Lines.GetLinePosition` plus a whitespace-skip-to-`@` step.

```razor
@page "/some-route"

@* ❌ NN_RM_001 fires — per-page directive re-introduces LCA defect *@
@rendermode @(new InteractiveWebAssemblyRenderMode(prerender: true))

@* ❌ NN_RM_001 fires — any directive value triggers the rule *@
@rendermode InteractiveServer

@* ✅ Override via attribute form — flows through endpoint metadata without LCA risk *@
@attribute [RenderModeInteractiveServer]
```

**Path-based exception buckets** (analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| Pages/Samples | Any file path containing `Pages/Samples/` (pedagogical / side-by-side sample pages) |
| Pages/NnDesignSamples | Any file path containing `Pages/NnDesignSamples/` (NnDesign component demo pages) |

**Bypass mechanism** — whole-assembly opt-out for sibling libraries that don't adopt Pattern 2 reflection:

```csharp
[assembly: NotNot.BlazorAnalyzers.RenderMode.NnRmBypass]
```

Apply this to assemblies whose render-mode architecture predates or doesn't use single-declaration-site Pattern 2 reflection. Per-page or per-class bypass is intentionally NOT supported — the consumer assembly either adopts Pattern 2 reflection wholesale or opts out wholesale. This keeps the bypass surface minimal and prevents per-page bypass markers from accumulating as silent contradictions of the architectural invariant.

**Known limitations** (BY-DESIGN trade-offs pinned as tests):

- Text-scan analyzer (per Microsoft.CodeAnalysis `AdditionalFiles` precedent) lacks Razor lexical context, so a `@rendermode` directive inside a Razor block comment (e.g., `@* @rendermode ... *@`) WILL fire the diagnostic. LOW user impact — block-commented directives are vanishingly rare in practice; the trade-off favors text-scan simplicity over a full Razor lexer dependency.
- Word-boundary regex prevents false positives on identifier-char continuations (e.g., `@rendermodeXyz` does not match), but the analyzer does not validate that post-directive text is well-formed Razor.

**Severity escalation** — escalate via `.editorconfig` once the consumer assembly reaches zero per-page directives:

```ini
[*.razor]
dotnet_diagnostic.NN_RM_001.severity = error
```

<a id="nn_nnd_async_002"></a>
### NN_NND_ASYNC_002: NnAsyncBoundComponentBase subclass lifecycle override must call base

**Severity:** Warning
**Category:** NnDesign Convention

Complement to NN_NND_ASYNC_001 (which enforces "a sync handler must NOT observe async mixin state"). NN_NND_ASYNC_002 enforces the converse contract on the inheritance layer: a subclass of `NotNot.BlazorDesign.NnDesign.AsyncBound.NnAsyncBoundComponentBase<TValue>` that overrides a lifecycle method **the base itself implements** must call `base.{Method}(...)`.

`NnAsyncBoundComponentBase<TValue>` performs essential per-lifecycle bookkeeping: `OnInitialized` constructs the `Behavior` save machine and seeds the `_lastCommitted` self-coerce-echo sidecar; `OnParametersSet` refreshes `_lastCommitted` from genuine external parameter changes; `Dispose` disposes the `Behavior`. An override that omits the base call silently freezes that work — `_lastCommitted` never refreshes (stale self-coerce-echo discriminator) or the `Behavior` is never constructed (no save machine). **Authority:** the trio-rollout P2 finding (commit `293aaf72`) — `NnChipSetSingle`/`NnChipSetMulti` shipped this latent override-without-base until peer review caught it.

**Scope — only the three real-bookkeeping lifecycle methods.** The analyzer fires ONLY on overrides of `OnInitialized`, `OnParametersSet`, and `Dispose` — the base members whose impl does real per-lifecycle work. `NnAsyncBoundComponentBase` also declares many no-op **policy seams** (`IsEmpty`, `Clamp`, `ApplyRevertedValue`, `NotifyValueChanged`, `GetEffectivePreFlight`, `DefaultAllowNull`, `EmptyNonNullValue`, `DefaultInteractionMode`) authored `=> default` / `{ }` **to be replaced**; overriding one WITHOUT calling base is CORRECT (calling base is pointless, and for `IsEmpty` it would reintroduce the `=> false` default → a latent bug). The analyzer does NOT fire on these seams.

**Why semantic, not syntactic.** A plain `ComponentBase` subclass overriding `OnParametersSet` without `base` is fine (ComponentBase's impl is a no-op). The defect is specific to `NnAsyncBoundComponentBase`, whose three lifecycle methods carry real work. Detection requires BOTH: (1) the override's method **name** is one of `OnInitialized` / `OnParametersSet` / `Dispose` (partitioning the real-work lifecycle from the no-op seams above); AND (2) the override's `OverriddenMethod` chain contains a link whose `ContainingType.OriginalDefinition` is `NnAsyncBoundComponentBase<TValue>` and is non-abstract (the base supplies a real impl). Together they auto-exclude the base's no-op policy seams, plain `ComponentBase` overrides, methods the base does not implement (e.g. `OnAfterRender`), and the Razor-compiler-generated `BuildRenderTree` override (whose base is `ComponentBase`).

**Generated-code analysis.** The most common defect form lives in `.razor` `@code` blocks, which compile to Razor-generated C# (`.g.cs`). Unlike NN_NND_ASYNC_001, this analyzer registers with `GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics` so an override-without-base inside component `@code` is caught. The precise base-chain check above is the false-positive guard against the Razor-emitted `BuildRenderTree` that generated-code analysis also surfaces.

> **Accepted limitation:** the `Analyze` flag surfaces _all_ generated code, not only Razor `.g.cs`. A non-Razor source generator emitting an `NnAsyncBoundComponentBase` subclass with a base-omitting lifecycle override would also fire (and the consumer can suppress only via `#pragma` / `.editorconfig`, since generated source is not hand-editable). This is accepted: no such generator exists, and a `.razor.g.cs` path-gate would be fragile filepath coupling. The Razor-synthesized-member footgun (`BuildRenderTree` / `SetParametersAsync`) is now doubly guarded: neither is in the lifecycle-name whitelist, so even if `NnAsyncBoundComponentBase` ever overrode one the analyzer would stay silent on every generated component. An invariant comment on the base's lifecycle region still documents the guidance.

```csharp
// ❌ NN_NND_ASYNC_002 fires — override omits base.OnParametersSet()
public class NnWidget : NnAsyncBoundComponentBase<int>
{
    protected override void OnParametersSet()
    {
        // _lastCommitted never refreshes — stale self-coerce-echo discriminator.
        RecomputeLayout();
    }
}

// ✅ No diagnostic — base call present (conventionally the first statement)
public class NnWidget : NnAsyncBoundComponentBase<int>
{
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        RecomputeLayout();
    }
}
```

The base call may appear anywhere in the body (the rule is presence-based, not first-statement) and an expression-bodied override (`=> base.OnParametersSet()`) satisfies it. Detected lifecycle methods are exactly those `NnAsyncBoundComponentBase` itself implements: `OnInitialized`, `OnParametersSet`, and `Dispose`.

**Suppression** — only when the base bookkeeping is genuinely undesired (rare):

```csharp
#pragma warning disable NN_NND_ASYNC_002 // Documented reason: base bookkeeping intentionally skipped
protected override void OnParametersSet() { /* ... */ }
#pragma warning restore NN_NND_ASYNC_002
```

Or project/folder-wide via `.editorconfig`:

```ini
[*.{cs,razor}]
dotnet_diagnostic.NN_NND_ASYNC_002.severity = warning
```

<a id="nnb002"></a>
### NNB002: JS Reference Disposal Required

**Severity:** Error
**Category:** Lifecycle

Detects `DotNetObjectReference<T>` or `IJSObjectReference` fields in Blazor components that are not properly disposed.

```csharp
// ❌ Missing disposal - NNB002 fires
@code {
    private IJSObjectReference? _module;
    private DotNetObjectReference<MyComponent>? _dotNetRef;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./my-module.js");
            _dotNetRef = DotNetObjectReference.Create(this);
        }
    }
    // No IAsyncDisposable implementation!
}

// ✅ Properly disposed
@implements IAsyncDisposable

@code {
    private IJSObjectReference? _module;
    private DotNetObjectReference<MyComponent>? _dotNetRef;

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
        _dotNetRef?.Dispose();
    }
}
```

<a id="nnb007"></a>
### NNB007: JSDisconnectedException Not Caught

**Severity:** Warning
**Category:** Reliability

Detects JS interop calls in `DisposeAsync` methods that don't handle `JSDisconnectedException`.

```csharp
// ❌ Missing exception handling - NNB007 fires
public async ValueTask DisposeAsync()
{
    if (_module is not null)
    {
        await _module.InvokeVoidAsync("cleanup"); // Can throw if circuit disconnected!
        await _module.DisposeAsync();
    }
}

// ✅ Properly handled with try-catch
public async ValueTask DisposeAsync()
{
    if (_module is not null)
    {
        try
        {
            await _module.InvokeVoidAsync("cleanup");
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Circuit already disconnected - expected during navigation
        }
    }
}

// ✅ Properly handled with _WaitIgnoreCancel() extension (from NotNot.Bcl.Core)
public async ValueTask DisposeAsync()
{
    if (_module is not null)
    {
        await _module.InvokeVoidAsync("cleanup")._WaitIgnoreCancel();
    }
    // For nullable IJSObjectReference disposal:
    await (_module?.DisposeAsync())._WaitIgnoreCancelOrNull();
    _module = null;
}
```

The analyzer recognizes these safe wrapper extension methods (from NotNot.Bcl.Core):

| Method | Catches |
|--------|---------|
| `_WaitIgnoreCancel()` | `TaskCanceledException`, `OperationCanceledException`, `JSDisconnectedException` |
| `_WaitIgnoreCancelOrNull()` | Same as above, plus handles `null` ValueTask |

These provide a concise alternative to verbose try-catch blocks.

<a id="nnb008"></a>
### NNB008: JsonException from JS Interop - Fix JavaScript Instead

**Severity:** Error
**Category:** Reliability

Detects catching and swallowing `JsonException` from JS interop calls. When `InvokeAsync<T>` fails with `JsonException`, it typically indicates a bug in the JavaScript function (null reference, undefined property). The correct fix is to improve the JavaScript with defensive coding, not mask the error in C#.

```csharp
// ❌ WRONG - masks JS bug, NNB008 fires
try
{
    var result = await _module.InvokeAsync<string>("parseJson", data);
}
catch (JsonException)
{
    // Silently swallowing JS error - the JS function has a bug!
}

// ✅ CORRECT - fix the JavaScript function instead
// In your .js file, add defensive coding:
// const element = container?.querySelector('.selector');
// if (!element) {
//     console.warn('[module] element not found');
//     return null; // Graceful return instead of throwing
// }
```

**Why ERROR severity?** Per the FAIL_FAST principle, masking bugs leads to harder-to-debug issues later. When JS interop throws `JsonException`, the JavaScript function needs fixing - catching it in C# hides the root cause.

**Known Limitations:**
- Skips try blocks also containing `System.Text.Json` API calls (e.g., `JsonSerializer.Deserialize`) to avoid false positives
- Applies to all classes, not just Blazor components (JsonException masking is an anti-pattern everywhere)

<a id="nnb010"></a>
### NNB010: Unknown Component Parameter

**Severity:** Error
**Category:** Parameters

Detects unknown parameters passed to Blazor components that don't support attribute splatting. This catches typos in parameter names and prevents runtime errors from invalid attributes.

```razor
@* ❌ Unknown parameter - NNB010 fires *@
<MudButton unknownparam="value" Color="Color.Primary">Click</MudButton>
@* If MudButton doesn't have [Parameter(CaptureUnmatchedValues = true)],
   unknown parameters will cause a runtime exception *@

@* ❌ Typo in parameter name - NNB010 fires *@
<MudButton Colr="Color.Primary">Click</MudButton>
@* "Colr" instead of "Color" - caught at compile time! *@

@* ✅ Common HTML attributes are exempt - no error *@
<MudButton aria-label="Close" title="Close button" @onclick="HandleClick">
    Click
</MudButton>
```

**How it works:**
1. Analyzes generated Razor code (`.g.cs` files)
2. Tracks `OpenComponent<T>` calls to identify component types
3. Detects `AddComponentParameter(N, "string-literal", value)` calls (vs `nameof()`)
4. Validates parameter names against the component's `[Parameter]` properties
5. Skips components with `CaptureUnmatchedValues` (those are handled by NNB011)

**Note:** HTML elements (`<div>`, `<span>`, etc.) are NOT analyzed - only Blazor components.

**Exempted Attributes:**

| Pattern | Examples | Rationale |
|---------|----------|-----------|
| `aria-*` | `aria-label`, `aria-hidden` | Accessibility attributes |
| `data-*` | `data-xcs`, `data-testid` | HTML5 custom data attributes |
| `on*` | `onclick`, `onchange` | Event handlers |
| Common HTML | `title`, `role`, `tabindex`, `id`, `style` | Standard HTML passthrough |

**Suppression:** To allow specific unknown attributes, use `.editorconfig`:
```ini
[*.razor]
dotnet_diagnostic.NNB010.severity = none  # Disable entirely
# Or use #pragma warning disable NNB010 for specific cases
```

<a id="nnb011"></a>
### NNB011: Unknown Parameter on Splatted Component (Strict Mode)

**Severity:** Error
**Category:** Parameters

Strict sibling to NNB010. Detects unknown parameters on components that HAVE `[Parameter(CaptureUnmatchedValues = true)]`. While splatting allows these at runtime, this catches typos and enforces explicit parameter usage for non-standard attributes.

```razor
@* ❌ NNB011 fires - unknown param on splatted component *@
<MudButton customattr="value">Click</MudButton>
@* MudButton has splatting, so this works at runtime,
   but NNB011 flags it to catch potential typos *@

@* ✅ No error - common HTML attributes are exempted *@
<MudButton aria-label="Close" title="Tooltip" @onclick="HandleClick">
    Click
</MudButton>

@* ✅ No error - known parameter *@
<MudButton Color="Color.Primary">Click</MudButton>
```

**Exempted Attributes:** Same as NNB010 (see table above).

**When to disable NNB011:**
- When using third-party components with splatting for custom attributes
- For components that intentionally accept arbitrary props

```ini
[*.razor]
dotnet_diagnostic.NNB011.severity = none  # Allow non-standard splatted attributes
```

<a id="nnb012"></a>
### NNB012: Incomplete JS Interop Cancellation Handling

**Severity:** Error
**Category:** Reliability

Detects catch blocks that handle `JSDisconnectedException` but are missing `TaskCanceledException` or `OperationCanceledException`. When a Blazor Server circuit disconnects, JS interop calls can throw *either* exception depending on timing — handling one without the other leaves a gap that crashes the circuit.

```csharp
// ❌ Missing TaskCanceledException - NNB012 fires
try
{
    await _module.InvokeVoidAsync("cleanup");
    await _module.DisposeAsync();
}
catch (Exception ex) when (ex is JSDisconnectedException or JSException or ObjectDisposedException)
{
    // TaskCanceledException escapes when call is in-flight during disconnect!
}

// ✅ Complete handling
try
{
    await _module.InvokeVoidAsync("cleanup");
    await _module.DisposeAsync();
}
catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or JSException or ObjectDisposedException)
{
    // Both disconnect paths covered
}
```

**Why two exception types?**
- `JSDisconnectedException` — thrown when the runtime *knows* the circuit is gone *before* the call
- `TaskCanceledException` — thrown when a call is *already in-flight* and the `CancellationToken` fires

**Scope:** Any try-catch block (not just `DisposeAsync`, not just Blazor components). If you're catching `JSDisconnectedException`, you should also catch `TaskCanceledException`.

**When NNB012 does NOT fire:**
- Catch includes `TaskCanceledException` or `OperationCanceledException` (parent class)
- Blanket `catch (Exception)` or bare `catch` (catches everything)
- No `JSDisconnectedException` in any catch clause (NNB007's responsibility)
- All JS interop calls in the try block use `_WaitIgnoreCancel()` (safe wrapper handles both)

<a id="nnb013"></a>
### NNB013: Missing JSDisconnectedException in JS Interop Catch

**Severity:** Error
**Category:** Reliability

The converse of NNB012. Detects try-catch blocks containing JS interop calls that catch `TaskCanceledException`/`OperationCanceledException` but are missing `JSDisconnectedException`.

```csharp
// ❌ Missing JSDisconnectedException - NNB013 fires
try
{
    await _module.InvokeVoidAsync("setItem", data);
}
catch (JSException ex) { Log(ex); }
catch (OperationCanceledException)
{
    // Has cancellation handling, but JSDisconnectedException escapes!
}
catch (ObjectDisposedException ex) { Log(ex); }

// ✅ Complete handling
try
{
    await _module.InvokeVoidAsync("setItem", data);
}
catch (JSException ex) { Log(ex); }
catch (OperationCanceledException) { }
catch (JSDisconnectedException) { }
catch (ObjectDisposedException ex) { Log(ex); }
```

**Why a separate rule from NNB012?** NNB013 requires a **JS-interop gate** — it only fires when the try block contains actual JS interop calls (`InvokeAsync`, `InvokeVoidAsync`, etc.). This is necessary because `OperationCanceledException` is commonly caught for non-JS-interop cancellation (e.g., `CancellationToken`, `Task.Delay`). Without the gate, NNB013 would produce false positives on all cancellation handling.

**Relationship to NNB007:** NNB007 checks for missing `JSDisconnectedException` but is scoped to `DisposeAsync` in Blazor components only. NNB013 applies to **any method in any class** — filling the coverage gap for non-disposal JS interop call sites.

**When NNB013 does NOT fire:**
- Catch includes `JSDisconnectedException` (directly or in `when` filter)
- No JS interop calls in the try block (JS-interop gate)
- All JS interop calls use `_WaitIgnoreCancel()` (safe wrapper)
- Blanket `catch (Exception)` or bare `catch` (catches everything)
- No cancellation exception in any catch clause (NNB012's direction, not NNB013's)

<a id="nnb009"></a>
### NNB009: Verbose Disposal Exception Catching

**Severity:** Warning
**Category:** Lifecycle

Detects verbose catch blocks for cancel exceptions in `DisposeAsync` methods. When you catch `JSDisconnectedException`, `OperationCanceledException`, or `TaskCanceledException` separately with empty handlers, consider using `_WaitIgnoreCancel()` for cleaner, more maintainable code.

```csharp
// ❌ VERBOSE - NNB009 fires
public async ValueTask DisposeAsync()
{
    try
    {
        await _module.InvokeVoidAsync("cleanup");
    }
    catch (JSDisconnectedException) { }
    catch (OperationCanceledException) { }
    catch (TaskCanceledException) { }  // Easy to miss one!
}

// ✅ CONCISE - use _WaitIgnoreCancel() from NotNot.Bcl.Core
public async ValueTask DisposeAsync()
{
    await _module.InvokeVoidAsync("cleanup")._WaitIgnoreCancel();
}
```

**Why WARNING severity?** This is a style/maintainability improvement, not a logic bug. The verbose pattern works but is error-prone (easy to forget one exception type) and violates DRY.

**When NNB009 does NOT fire:**
- Catch blocks with actual logic (not just swallowing)
- Patterns also catching `ObjectDisposedException` or `JSException` (different semantics)
- Catch clauses with non-trivial `when` filters (intentional conditional handling)

**Known Limitations:**
- v1 scope: `DisposeAsync` methods in Blazor components only
- Code fix not yet implemented (coming in v2)

<a id="nnb045"></a>
### NNB045: Transition-guard field claimed after `await` in async lifecycle

**Severity:** Warning
**Category:** Lifecycle

Detects a parameter-transition guard `if (x != _field)` whose block assigns `_field = …` lexically AFTER the first `await`, inside an `async` Blazor lifecycle override (`OnParametersSetAsync` primary; `OnInitializedAsync` / `OnAfterRenderAsync` secondary) of a `ComponentBase`-derived type.

Blazor re-invokes async lifecycle methods (especially `OnParametersSetAsync`, on every parameter change) and renders the component tree at each `await` suspension. A guard that claims `_field` only after an `await` leaves it **unclaimed across the await window**: a re-entrant pass re-runs the side-effecting block (duplicate work), and any child component whose post-render restore (e.g. persisted expand-state) lands during the window is **clobbered** by the resumed continuation's late reset.

```csharp
// ❌ NNB045 fires — guard field claimed AFTER the awaits
public class UserInputsPanel : ComponentBase
{
    private string? _currentSessionId;
    [Parameter] public string? newId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            await _cts.CancelAsync();
            await Unregister(_currentSessionId);
            _currentSessionId = newId;   // ← unclaimed across the awaits
        }
    }
}

// ✅ claim-then-await — claim synchronously, then do async teardown
public class UserInputsPanel : ComponentBase
{
    private string? _currentSessionId;
    [Parameter] public string? newId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (newId != _currentSessionId)
        {
            var previous = _currentSessionId;
            _currentSessionId = newId;   // ← claimed BEFORE any await
            await _cts.CancelAsync();
            await Unregister(previous);
        }
    }
}
```

**The fix** is to hoist the `_field` claim — and the block's synchronous state resets — above the first `await`, so the first render emitted at the await already reflects the new transition's state. The canonical reference is the claim-then-await pattern in `VowSessionMetaPanel.OnParametersSetAsync`.

**Seed order** when hoisting state resets: `persisted -> snapshot -> declared default`. A declared-default reset that runs after an await is the residual D2 class (a child's persisted restore landing during the await window is overwritten by the late default) — D2 is undecidable without annotation and is NOT enforced; NNB045 enforces the precise, sound D1 point (the guard-field claim itself), which in every observed instance co-occurred with the late resets.

**Detection** is semantic for two facts (the containing type derives from `ComponentBase`, and the guard-field symbol compared in the `!=` equals the field assigned after the await); ordering is syntactic (span comparison).

**When NNB045 does NOT fire:**
- Claim appears BEFORE the first `await` in the block (the claim-then-await fix).
- The guard block has no assignment to the compared field.
- The enclosing type is not a `ComponentBase`-derived component (a coincidentally-named `OnParametersSetAsync` elsewhere).
- The assignment lives inside a nested lambda or local function after the await (deferred execution — not a synchronous claim).

**Suppression** — only when awaiting before the claim is genuinely required (rare):

```csharp
#pragma warning disable NNB045 // Documented reason: await must precede the claim here
_currentSessionId = newId;
#pragma warning restore NNB045
```

Or project/folder-wide via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.NNB045.severity = error
```

**Default severity is Error** — promoted from the initial Warning rollout after proving quiet across a full solution build (zero violations tree-wide). To relax to a non-breaking warning in a specific project:

```ini
[*.cs]
dotnet_diagnostic.NNB045.severity = warning
```

<a id="nnb046"></a>
### NNB046: XRay path-marker set drifted from the canonical SSOT

**Severity:** Warning
**Category:** Conventions / Architecture (SSOT enforcement)

The XRay single-segment folder-marker set `{ /Features/, /Pages/, /Shared/, /Layout/, /Components/ }` has multiple mirrors that MUST carry the same membership or XRay source resolution silently breaks (the proven root cause of the prior XRay drift defect):

- **`XRayPathMarkers.SingleSegment`** — the C# SSOT (in `NotNot.BlazorAnalyzers`), consumed by `XRayMetadataGenerator.ComputeRelativePath`'s relative-path FALLBACK.
- **`XRayHelper._fallbackMarkers`** — a bare-init `string[]` FIELD (slashed) in `NotNot.BlazorComponents.XRay`.
- **`XRayEndpoints.fallbackPrefixes`** — a collection-expression `string[]` method-LOCAL (slash-less) in `NotNot.BlazorComponents.XRay`.
- **`xray-path-helpers.js::SINGLE_SEGMENT_MARKERS`** — the JS SSOT (`xray-interop.js::appPathMarkers` is a DERIVED `.map()` of it, not a separate source).

The runtime C# mirrors live in a different assembly that cannot reference the analyzer const (the analyzer is referenced analyzer-only, `ReferenceOutputAssembly=false`), so NNB046 enforces their equality at compile time. It covers BOTH a field mirror and a method-local mirror, and guards **two drift axes** — both break XRay resolution:

1. **Segment membership** — slashes normalized away, the segment **SET** compared (membership, not order). A dropped / added / renamed segment fires.
2. **Per-site slash convention** — the slash form is load-bearing and differs per site: `_fallbackMarkers` requires **leading+trailing** slashes (`"/Features/"` — its `IndexOf(marker)` + `Substring(idx + 1)` fallback consumer depends on the leading slash; `XRayMetadataGenerator` carries the identical dependency), while `fallbackPrefixes` requires **trailing-only** (`"Features/"` — its `prefix + file` → `Path.Combine` consumer must NOT carry a leading slash). A copy with the right segments but the WRONG slash form for that site silently breaks resolution, so after the membership check passes NNB046 ALSO verifies the slash form expected at the matched mirror name.

A `CollectionExpressionSyntax` containing a **spread** (`[..other]`) makes the literal set un-knowable → NNB046 bails (preserving the never-false-positive guarantee).

```csharp
// ❌ NNB046 fires — segment-set drift (in the XRay namespace, a known mirror name)
namespace NotNot.BlazorComponents.XRay;
internal static class Mirror
{
    // /Layout/ dropped → segment set drifted from the canonical 5
    private static readonly string[] _fallbackMarkers =
        { "/Features/", "/Pages/", "/Shared/", "/Components/" };
}

// ❌ NNB046 fires — slash-convention drift (correct segments, WRONG form for the site)
private static readonly string[] _fallbackMarkers =
    { "Features/", "Pages/", "Shared/", "Layout/", "Components/" }; // field requires LEADING+TRAILING slashes
string[] fallbackPrefixes = ["/Features/", "/Pages/", "/Shared/", "/Layout/", "/Components/"]; // local requires TRAILING-ONLY

// ✅ allowed — in-sync segments AND the per-site slash form
private static readonly string[] _fallbackMarkers =
    { "/Features/", "/Pages/", "/Shared/", "/Layout/", "/Components/" }; // slashed field — silent
string[] fallbackPrefixes = ["Features/", "Pages/", "Shared/", "Layout/", "Components/"]; // slash-less local — silent
```

**Fix order:**
1. Update the drifted declaration to the canonical segment set in ITS required slash convention (`_fallbackMarkers` ⇒ leading+trailing; `fallbackPrefixes` ⇒ trailing-only); OR
2. If the set legitimately changed, update `XRayPathMarkers.SingleSegment` + every mirror (`XRayMetadataGenerator` via the const, `XRayHelper._fallbackMarkers`, `XRayEndpoints.fallbackPrefixes`, and the JS SSOT `xray-path-helpers.js::SINGLE_SEGMENT_MARKERS`) + the JS drift test together; OR
3. `#pragma warning disable NNB046` only if the array is genuinely NOT an XRay marker mirror.

**When NNB046 does NOT fire:**
- The declaration is in-sync with the canonical set (after slash normalization) AND carries the per-site slash form expected at its mirror name.
- The declaration is outside an XRay namespace (namespace is not `NotNot.BlazorComponents.XRay` and does not end in `.XRay`).
- The declaration name is not a known mirror name (`_fallbackMarkers` / `fallbackPrefixes`).
- The initializer is not a constant `string[]` literal, or a collection expression uses a spread element (a dynamic / spread / non-string array cannot be proven drifted).

Project/folder-wide via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.NNB046.severity = warning
```

<a id="nnb041"></a>
### NNB041: ReplaceUrlStateAsync requires adjacent component-state mutation

**Severity:** Warning
**Category:** Navigation

Detects `NavigationManagerExtensions.ReplaceUrlStateAsync(...)` invocations inside Blazor component methods when no synchronous UI-state mutation precedes the call within the same method body. `ReplaceUrlStateAsync` writes the browser URL via `history.pushState`/`replaceState` through JS interop — unlike `NavigationManager.NavigateTo`, it does NOT drive a Blazor router navigation. `[SupplyParameterFromQuery]`-bound parameters retain their previous value and `OnParametersSetAsync` does not fire. Any component event handler calling this helper must have already updated its own UI state synchronously before the call.

This analyzer turns the documented xml-doc contract at `NavigationManagerExtensions.cs:103-117` into a compile-time invariant. The regression class was introduced at commit `407350ad` — 2 of 14 migrated URL updates had no adjacent state mutation and silently relied on the eliminated re-binding cascade, producing a no-op click with zero error signal.

```csharp
public class VowDashboard : ComponentBase
{
    private object? _selectedSession;
    private NavigationManager _nav = null!;

    // ❌ NNB041 fires — no preceding state mutation
    private async Task HandleSessionClick(string sessionId)
    {
        await _nav.ReplaceUrlStateAsync("/session/" + sessionId);
    }

    // ✅ Mode (a): assign the backing state field before the URL update
    private async Task HandleSessionClickFixed(object session)
    {
        _selectedSession = session;
        await _nav.ReplaceUrlStateAsync("/session/" + session);
    }

    // ✅ Mode (b): call a helper that bundles state + URL atomically
    private async Task HandleActivate(object session)
    {
        ActivateSessionAndUpdateUrl(session);
        await _nav.ReplaceUrlStateAsync("/session/123");
    }

    // ✅ Mode (c): cleanup — set _selectedSession = null before URL update
    private async Task HandleCloseSession()
    {
        _selectedSession = null;
        await _nav.ReplaceUrlStateAsync("/");
    }

    private void ActivateSessionAndUpdateUrl(object s) { _selectedSession = s; }
}
```

**Compliance modes** (any of these preceding the call in the same method body satisfies the contract):

| Mode | Pattern | Example |
|------|---------|---------|
| (a) | `_selectedSession = <non-null>;` | Assign the backing state field |
| (b) | Call to `ActivateSession*` / `SelectSession*` / `OpenTerminalTab*` / `EnsureSessionVisibleInSidebar*` | Use a bundled helper |
| (c) | `_selectedSession = null;` or `SelectSession(null)` | Cleanup/close flow |

**Path-based exception buckets** (analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| Pages/Samples | Any file path containing `/Pages/Samples/` |
| Pages/NnDesignSamples | Any file path containing `/Pages/NnDesignSamples/` |

**Suppression** — for the rare genuinely-URL-only update:

```csharp
#pragma warning disable NNB041 // Documented reason: URL-only update, no state dependency
await _nav.ReplaceUrlStateAsync("/settings");
#pragma warning restore NNB041
```

Or project/folder-wide via `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.NNB041.severity = none
```

No dedicated bypass attribute is provided — `#pragma` / `.editorconfig` is the escape mechanism. This is intentional: NNB041 is a per-call-site correctness rule, and a whole-assembly opt-out would defeat the invariant it enforces.

<a id="nnb043"></a>
### NNB043: Raw Tier-B appearance param exposed on an Nn* component (policy table)

**Severity:** Error
**Category:** NnDesign
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy (Tier A/B) · `NnDesignLayoutContract.VowSpec` SPEC-001..003

The producer-side enforcer of the NnDesign manifesto's Tier-B/appearance-channel discipline ("enforced or it doesn't exist"). A raw appearance-laundering passthrough param (`string *Style` / `*Class`) exposed on an `Nn*` component is the entropy channel a consumer would use to bypass the prescriptive "ONE TRUE WAY" contract. NNB043 flags such params on the design system's OWN components.

**Scans the PRODUCER (no producer-path exemption).** Unlike NNB022 / NNB_CSS008 (which exempt `**/NotNot.BlazorDesign/**` because they police *consumer* usage), NNB043 deliberately analyzes the `NotNot.BlazorDesign` assembly's own `Nn*`-component parameter declarations — the manifesto demands the rule flag the design system's own raw appearance params. The producer references this analyzer via `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`, so NNB043 fires during the producer build.

**Symbol-based + explicit deny/allow policy table** (NOT a string/regex heuristic): fires on `SymbolKind.NamedType`, iterates each type's public `[Parameter]`-marked properties, and matches by resolved **type** (`string`) + **name** shape (ends in `Style`/`Class`). A matched param FIRES unless its `(component, param)` pair is a documented **Tier-A allow-row**. The detector list is extensible to future non-string Tier-B rows (`Variant` / `Margin` / `Elevation`) as contracts formalize — NNB043 is the single Tier-B-exposure owner, not a one-off "no strings" rule.

```csharp
// ❌ NNB043 fires — raw string appearance passthrough on an Nn* component
public class NnWidget : NnComponentBase
{
    [Parameter] public string? BodyStyle { get; set; }   // appearance laundering via Style=
    [Parameter] public string? BodyClass { get; set; }   // …and via Class=
}

// ✅ No diagnostic — semantic param replaces the raw passthrough
public class NnWidget : NnComponentBase
{
    [Parameter] public NnDensity Density { get; set; }       // semantic density axis
    [Parameter] public NnBodyLayout BodyLayout { get; set; } // structural body-layout token
}
```

**Tier-A allow-rows** (documented brand-permitted-variation channels — never flagged; seeded per `NnDesignLayoutContract.VowSpec`):

| Component | Param(s) | Rationale (VowSpec) |
|-----------|----------|---------------------|
| `NnComponentBase` | `Style`, `Class` | SPEC-003 — base root-element escape hatch, inherited by every wrapper |
| `NnDialog` | `TitleClass`, `ContentClass`, `ActionsClass` | SPEC-002 — per-region class hooks for genuinely-unbounded dialog layout |
| `NnAppBar` | `ToolBarClass` | SPEC-002 — structural inner-toolbar class the base `Class` cannot reach |

The allow-rows are class-only channels — their raw `*Style` twins are NOT exposed; any OTHER public `string`-typed `*Style`/`*Class` param on an `Nn*` component is a DENY.

**Per-symbol opt-out** (documented genuine brand-permitted-variation pending a policy-table allow-row):

```csharp
/// <summary>nnb043:allow-tierb: pending Tier-A classification for the new region hook</summary>
[Parameter] public string? RegionClass { get; set; }
```

**Project-wide kill-switch** (rare):

```xml
<PropertyGroup>
  <NnDesignTierBExposureAnalyzerEnabled>false</NnDesignTierBExposureAnalyzerEnabled>
</PropertyGroup>
```

**Default severity is Error** — the producer is verified appearance-laundering-clean (fires ZERO on the real producer), so any NEW raw Tier-B exposure breaks the build. To soften locally (rare), override via `.editorconfig`:

```ini
[*.{razor,cs}]
dotnet_diagnostic.NNB043.severity = warning
```

<a id="nnb044"></a>
### NNB044: Static-literal inline `style=` in consumer markup (value-shape allowlist)

**Severity:** Error
**Category:** NnDesign
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy · Appendix A NNB044

The consumer-side counterpart to [NNB043](#nnb043) (producer-side Tier-B exposure). A STATIC-LITERAL inline `style=` / `Style=` attribute in consumer `.razor` markup is "appearance laundered through a literal" — the hard-coded padding / display / layout that the NnDesign semantic params (`Density` / `BodyLayout`) are meant to absorb. NNB044 enforces the Slice-3 migration scope: each fire is a static-literal inline style to migrate to a semantic param or a scoped CSS class.

**Value-shape allowlist (the core discriminator).** Inline style fires ONLY when its value is a STATIC LITERAL — no Razor expression. A DYNAMIC value (any `@`-bound / interpolated / expression value) is ALLOWED: a sparkline height, a computed color, a per-row width are legitimately dynamic and cannot be a static semantic param. The allowlist keys on the VALUE shape, not the attribute name.

```razor
@* ❌ NNB044 fires — static-literal inline style (appearance laundered through a literal) *@
<div style="padding: 4px 8px">…</div>
<div style="display:flex">…</div>

@* ✅ dynamic value — @-bound / interpolated / expression → legitimately computed, NO warning *@
<div style="@_panelStyle">…</div>
<div style="background:@color">…</div>
<div style="height:@(h)px">…</div>
<div style=@_expr>…</div>

@* ✅ semantic param replaces the static literal (the migration target) *@
<NnContentSection Density="Compact">…</NnContentSection>
```

**Scan target:** `.razor` markup only (not `.razor.cs` / `.cs` / `.css`). Skips matches inside HTML (`<!-- -->`) and Razor (`@* *@`) comments. A longer attribute ending in `style` (e.g. a `BodyStyle=` component param) does NOT match — the leading-boundary guard isolates the standalone `style` attribute (the `BodyStyle` producer param is [NNB043](#nnb043)'s concern).

**Path-based exception buckets** (analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| NnDesign producer / wrapper internals | Any file under `**/NotNot.BlazorDesign/**` |
| Samples | `**/NnDesignSamples/**`, `**/Pages/Samples/**` |
| Root-bootstrap + dev/test harness | `App.razor`, `BlazorTermTest*.razor`, `BlazorTermHarmonizedHarness.*`, `HarnessDummyTab.razor`, `HarnessTerminalWrapper.razor`, `RichEditExamplePage.razor`, `NnDesignSamplesPage.razor`, `DashboardLegacy.razor`, `TestInputs.razor` (file-name allow-list) |

**Per-file opt-out**: `@* nnb044:allow-inline-style: <reason> *@`. **Assembly opt-out**: `[assembly: NotNot.BlazorAnalyzers.NnDesign.NnDesignBypass]`. **Kill-switch** (shared with the NnDesign policy suite): `<NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled>`.

**Default severity is Error** — the Slice-3 static-literal conformity sweep cleared the consumer surface (fires ZERO across the consumer markup), so any NEW static-literal inline style breaks the build. To soften locally (rare), override via `.editorconfig`:

```ini
[*.razor]
dotnet_diagnostic.NNB044.severity = warning
```

<a id="nnb047"></a>
### NNB047: Duplicate `NnSampleSection` Id (HTML ids must be unique)

**Severity:** Error
**Category:** NnDesign
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy

`NnSampleSection` renders its `Id` parameter as an HTML `id` (`<NnPaper id="@Id">`). The `/samples` Table of Contents and browser tests anchor on that id via `document.getElementById`, which returns only the **first** matching element. Two `NnSampleSection` sharing an `Id` therefore make every anchor / ToC navigation to that id silently mis-target the first occurrence (the motivating defect: a duplicated `Id="sample-27"` sent the "NnSelect" ToC entry to the wrong section). NNB047 aggregates every static-literal `NnSampleSection` `Id` across `.razor` markup and reports each section in a duplicate set.

```razor
@* ❌ NNB047 fires on BOTH — they share Id="sample-x" (getElementById resolves only the first) *@
<NnSampleSection Id="sample-x" XRaySource="@XRayHelper.Source()">…</NnSampleSection>
…
<NnSampleSection Id="sample-x" XRaySource="@XRayHelper.Source()">…</NnSampleSection>

@* ✅ each section has a distinct Id *@
<NnSampleSection Id="sample-x" XRaySource="@XRayHelper.Source()">…</NnSampleSection>
<NnSampleSection Id="sample-y" XRaySource="@XRayHelper.Source()">…</NnSampleSection>

@* ✅ dynamic Id (Id="@expr") is not statically comparable → skipped *@
<NnSampleSection Id="@_id" XRaySource="@XRayHelper.Source()">…</NnSampleSection>
```

**Scan target:** `.razor` markup only (not `.razor.cs` / `.cs` / `.razor.css`). Uniqueness is **compilation-scoped** — a duplicate spanning two files fires in both. Skips matches inside HTML (`<!-- -->`) and Razor (`@* *@`) comments. Dynamic `Id="@expr"` values are not statically comparable and are skipped.

**No path exemptions.** Unlike the consumer-policy NnDesign analyzers ([NNB044](#nnb044) etc.), NNB047 does **not** exempt `NnDesignSamples/**` or `NnDesignSamplesPage.razor` — that is precisely where `NnSampleSection` is used. It keys on the component, not on a consumer path.

**Assembly opt-out**: `[assembly: NotNot.BlazorAnalyzers.NnDesign.NnDesignBypass]`. **Kill-switch** (shared with the NnDesign policy suite): `<NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled>`.

**Default severity is Error** — a duplicate HTML id is a correctness defect (silent mis-navigation). To soften locally (rare), override via `.editorconfig`:

```ini
[*.razor]
dotnet_diagnostic.NNB047.severity = warning
```

<a id="nnb_css009"></a>
### NNB_CSS009: Do not reach into MudBlazor internal `.mud-*` classes

**Severity:** Error
**Category:** CssModernization
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy · `NnDesignLayoutContract.VowSpec` SPEC-008

Sibling of [NNB_CSS008](#NNB_CSS008) (which guards the `.nns-*` namespace). MudBlazor is the primitive layer wrapped internally by NnDesign; its `.mud-*` classes are private implementation detail two layers down from the consumer. A consumer that restyles one (e.g. `::deep .mud-tab { min-width:80px }` or a bare `.mud-tabs-header { min-height:unset }`) couples to MudBlazor's private DOM through the NnDesign wrapper and breaks when either layer's DOM changes. The sanctioned alternative is the NnDesign wrapper's published contract (declarative-`NnTabs` trigger-sizing default + `FillPanels` / per-panel `data-nn-fill`, `NnContentSection` params).

**Same text-scan host + react-to/reach-into discriminator as NNB_CSS008**: the styled SUBJECT is the rightmost compound selector. A `.mud-*` class in subject position is a reach-in (warns). The same `.mud-*` as an ancestor / state gate on a non-mud subject is "react-to" (sanctioned).

```css
/* ❌ NNB_CSS009 fires — subject is a .mud-* class */
::deep .mud-tab { min-width: 80px; }
.mud-tabs-header { min-height: unset; }

/* ✅ react-to: .mud-* as ancestor/state gate on a non-mud subject — NO warning */
.mud-tabs-active .vow-panel { display: block; }

/* ✅ own element — NO warning */
.vow-metatabs-content { overflow: auto; }
```

**Allow-list: EMPTY** — unlike `.nns-*` (which publishes `.nns-chord-revealed` as a public contract), MudBlazor exposes no public `.mud-*` styling contract to the consumer. Every non-exempt `.mud-*` subject is a reach-in.

**Path-based exception buckets** (analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| NnDesign producer / wrapper internals | Any file under `**/NotNot.BlazorDesign/**` (the wrapper legitimately restyles `.mud-*` — that's its job) |
| Samples | `**/NnDesignSamples/**`, `**/Pages/Samples/**` |
| Samples / global theme CSS | `nn-design-samples.css`, `app.css` (file-name allow-list) |

**Per-file opt-out**: `/* nnb_css009:allow-reachin: <reason> */`. **Kill-switch** (shared with the CSS suite): `<CssAnalyzerEnabled>false</CssAnalyzerEnabled>`.

**Default severity is Error** — the consumer tree is verified `.mud-*`-reach-in-clean (fires ZERO across the consumer CSS), so any NEW reach-in breaks the build. To soften locally (rare), override via `.editorconfig`:

```ini
[*.css]
dotnet_diagnostic.NNB_CSS009.severity = warning
```

<a id="NNB_CSS010"></a><a id="nnb_css010"></a>
### NNB_CSS010: Avoid viewport-relative units in consumer code

**Severity:** Error
**Category:** CssModernization
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy · NnDesign layout contract

Viewport-relative units (`vh` `vw` `vmin` `vmax` and the dynamic variants `dvh` `svh` `lvh` `dvw` `svw` `lvw`) size against the WINDOW, not the space the layout granted. Inside the app shell — which reserves navbar + statusbar chrome — a viewport-sized box overflows the pane clip line: content paints "under the status bar" and is silently clipped. Fires property-agnostically (height / min-height / max-height / width / margin / inset / …) on any declaration value in CSS (global + `.razor.css`) and any quoted attribute value in `.razor` markup — the unit in a bounded context is the smell, not a specific property.

**Real incidents (2026-06-10, runtime-proven)**:
- `UserInputsPanel.razor` had `MaxHeight="60vh"` on an `NnContentSection` — the section painted 100% below the pane clip line. Fix: `Fill="true"` (tier 1).
- `DebugSessionInfoPage.razor.css` had `height:100vh` on the page-host class — 64px overflow under shell chrome. Fix: `height:100%` (tier 2, counterfactual-proven).

```css
/* ❌ NNB_CSS010 fires — viewport units in consumer CSS (global or .razor.css) */
.vow-host { height: 100vh; }
.x { min-height: 60vh; }

/* ✅ container-bounded sizing — NO warning */
.vow-host { height: 100%; }
.x { max-height: calc(100% - 8px); }
```

```razor
@* ❌ NNB_CSS010 fires — static viewport unit in a quoted attribute value *@
<NnContentSection MaxHeight="60vh">…</NnContentSection>

@* ✅ dynamic value (Razor expression in the attribute value) — legitimately computed, NO warning *@
<div style="height:@(h)vh">…</div>

@* ✅ prose/doc text outside an attribute value — NO warning *@
<p>Use <code>"50vw"</code> only for viewport-owned surfaces.</p>
```

**Fix tiers (cheapest-correct-first)**:
1. Section inside an `NnContentSectionGroup` that should take the remaining height → `Fill="true"`.
2. Box filling a bounded parent → `height:100%` or `data-nn-fill="container"`.
3. Genuine viewport-owned surface (portal dialog/overlay, standalone page outside the shell) → keep the unit and add a per-file `nnb_css010:allow-viewport-unit` comment stating why. Viewport-anchored portals are CORRECTLY viewport-relative — they get the allow-comment, not silence.
4. Only if truly intended → `dotnet_diagnostic.NNB_CSS010.severity` in `.editorconfig`.

**Dual-emit with [NNB044](#nnb044)**: a static `style="height:60vh"` literal fires BOTH NNB044 (static-literal inline style — unit-agnostic pattern, razor-compiled surface) and NNB_CSS010 (viewport unit — value-unit rule, css files + razor attribute text). Two genuinely co-present smells; migrating the inline style to a semantic param resolves both.

**Path-based exception buckets** (conform to [NNB_CSS009](#nnb_css009)'s set; analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| NnDesign producer / wrapper internals | Any file under `**/NotNot.BlazorDesign/**` (owns the sanctioned viewport sizing: `nns-app-shell` 100vh, `.nns-popup` 90vw/85vh, `data-nn-fill="viewport"`) |
| Samples | `**/NnDesignSamples/**`, `**/Pages/Samples/**` |
| Samples / global theme CSS | `nn-design-samples.css`, `app.css` (file-name allow-list) |

**Per-file opt-out**: `/* nnb_css010:allow-viewport-unit: <reason> */` (any comment form — the marker is matched anywhere in the file). **Kill-switch** (shared with the CSS suite): `<CssAnalyzerEnabled>false</CssAnalyzerEnabled>`.

**Default severity is Error** — project convention for layout-contract rules (NNB_CSS006/008/009 precedent): a new viewport unit in consumer bounded context is the exact regression class behind the two incidents above. To soften locally (rare), override via `.editorconfig`:

```ini
[*.{css,razor}]
dotnet_diagnostic.NNB_CSS010.severity = warning
```

<a id="NNB_CSS011"></a><a id="nnb_css011"></a>
### NNB_CSS011: Do not apply consumer CSS to an `Nn*` element (even via a consumer-owned class)

**Severity:** Warning (interim ratchet → Error once the consumer surface is verified clean)
**Category:** CssModernization
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer CSS — No Consumer Styling of `Nn*` · Appendix A NNB_CSS011

Closes the loophole [NNB_CSS008](#NNB_CSS008) / [NNB_CSS009](#nnb_css009) leave open. Those ban only the internal-namespace reach-in (the styled SUBJECT being a `.nns-*`/`.mud-*` class). They do NOT fire on `::deep .my-class { … }` where `.my-class` is a **consumer-authored** class that is BOUND to an `Nn*` component root. The declaration still lands on an `Nn*` element — e.g. `::deep .vow-metaside-actions-section { min-height: 80px }` paired with `<NnContentSection Class="vow-metaside-actions-section">`, a section-root floor that silently defeats the component's collapse. Tier A/B govern Nn* *parameters*; this governs consumer *CSS*. Property-agnostic: a `min-height`/`margin` layout declaration counts exactly as a `color`/`background` one.

**Two CSS-delivery surfaces, one rule.** NNB_CSS011 catches the same consumer-CSS-on-`Nn*` violation regardless of WHERE the CSS text lives:

1. **Scoped `.razor.css` `::deep` rule** — a `::deep .my-class { … }` selector in a consumer `.razor.css` whose class binds to an `Nn*` root in the paired `.razor`. The `::deep` is the necessary condition here — it is what pierces the scoped boundary into the child component's DOM (own-element styling omits `::deep`).
2. **Inline `.razor` `<style>` block** — a plain (non-`::deep`) class selector inside an inline `<style>…</style>` block authored directly in a `.razor`, whose class binds to an `Nn*` via a static-literal `Class="…"` in the SAME `.razor`. The inline path requires NO `::deep`: an inline `<style>` is unscoped (it leaks **globally**), so `::deep` would be inert — the plain class selector IS the violation, and arguably worse than the scoped form.

**Detection is cross-file / intra-file (`VIABLE_COSTLY`).** For the scoped surface the analyzer reads the PAIRED `.razor` (same path, `.css` stripped); for the inline surface it scans each `<style>` block against the SAME `.razor`'s markup. Both build a class→`Nn*` binding map from `Nn*` open-tags carrying a LITERAL `Class="… class …"` (the subject class as a space-separated token); match (and the component is not gray-zone) → it warns at the CSS selector subject inside the rule / block.

```css
/* ❌ NNB_CSS011 fires (scoped) — ::deep subject is a consumer class bound to an Nn* root in the paired .razor */
::deep .my-section { min-height: 80px; }

/* ✅ own element (no ::deep) — NO warning */
.my-section { min-height: 80px; }

/* ✅ .nns-* / .mud-* subject — NOT this rule (CSS008 / CSS009 own those vectors) */
::deep .nns-content-section-body { max-height: 40vh; }
```

```razor
@* ❌ NNB_CSS011 fires (inline) — a plain class selector in an inline <style> block, bound to an Nn* root *@
<style>.sidebar { width: 220px; }</style>
<NnSidebarPanel Class="sidebar" />

@* ✅ inline <style> styling a PLAIN element (not an Nn*) — NO warning *@
<style>.page-root { display: flex; }</style>
<div class="page-root">…</div>
```

**Relocating the CSS between the two deliveries is NOT a fix.** Moving a `::deep .my-class` rule out of `Foo.razor.css` into an inline `<style>` in `Foo.razor` (or vice-versa) does not resolve the violation — both surfaces land consumer CSS on the `Nn*` element, and the inline form additionally globalizes it. One DiagnosticId owns both deliveries precisely so the relocation loophole cannot reopen.

**Sanctioned alternative** (never an exemption from the ban): sizing/fill → a Layout Contract mode (`Fill` / `MaxHeight` / `data-nns-fill`); positioning → the axis-spacer mode; any other need → a producer default or a Tier-A param. Resolve an existing fire by DELETE-first triage: (1) DELETE if the rule restates a correct default (fix the default once); (2) expose a Tier-A param for genuine per-instance variation; (3) per-file opt-out only if truly intended.

**Documented false-negatives** (accepted; never guessed-around): dynamic `Class="@expr"` on the `Nn*` tag (not literal-matchable — applies to BOTH deliveries); `@media`/`@supports`-nested rules (the selector walk skips top-level `@`-blocks, so a rule nested inside `@media { … }` is invisible on both deliveries — the inline surface makes responsive `@media` blocks common, so this is the most likely inline miss); inline `Style=` ATTRIBUTE on the `Nn*` tag (owned by [NNB044](#nnb044) — distinct from an inline `<style>` BLOCK, which NNB_CSS011 DOES catch; this rule is CSS-selector-only, so it never double-emits on inline `Style=` attributes); a subject class bound to the `Nn*` in a different, non-paired `.razor`.

**Path-based exception buckets** (conform to [NNB_CSS009](#nnb_css009)'s set; analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| NnDesign producer / wrapper internals | Any file under `**/NotNot.BlazorDesign/**` |
| Samples | `**/NnDesignSamples/**`, `**/Pages/Samples/**` |
| Samples / global theme CSS | `nn-design-samples.css`, `app.css` (file-name allow-list) |
| Gray-zone consumer-local `Nn*` | `NnBlazorTermTab`, `NnBlazorTermTabFooter`, `NnPerfMonitorPanel` (retain the `Nn*` prefix as branding markers but live in the consumer assembly — styling them is consumer-local, not a design-system reach-in) |

The exception buckets above apply identically to BOTH deliveries — the inline `.razor` `<style>` path uses the SAME path buckets, gray-zone component list, per-file opt-out marker, and kill-switch (the opt-out marker is matched anywhere in the `.razor`, including inside the `<style>` block or a `@* … *@` comment). Commented-out (`@* … *@` / `<!-- … -->`) `<style>`+`<Nn* Class>` pairs are stripped before extraction, so dead markup never false-positives.

**Per-file opt-out**: `/* nnb_css011:allow-reachin: <reason> */` (or a `@* nnb_css011:allow-reachin: <reason> *@` Razor comment on the inline path). **Kill-switch** (shared with the CSS suite): `<CssAnalyzerEnabled>false</CssAnalyzerEnabled>`.

**Default severity is Warning** — interim ratchet. The step-3 consumer-CSS sweep left a documented `TODO(NnDesign-sweep)` tail of `::deep`-on-`Nn*` rules; Warning surfaces that tail as a worklist without breaking the build. Escalates to Error once the consumer surface is verified `::deep`-on-`Nn*`-clean (the doctrine end-state). To soften locally:

```ini
[*.css]
dotnet_diagnostic.NNB_CSS011.severity = suggestion
```

<a id="NNB_CSS012"></a><a id="nnb_css012"></a>
### NNB_CSS012: `font-size` literal duplicates an `--nns-font-size-*` design token

**Severity:** Error
**Category:** CssModernization
**Authority:** The NnDesign token system (producer manifesto) — `nn-design.css` `:root` `--nns-font-size-*` scale is the SSOT.

A different axis from the reach-in family ([NNB_CSS008](#NNB_CSS008) / [NNB_CSS009](#nnb_css009) / [NNB_CSS011](#NNB_CSS011)): those police the styled SUBJECT of a selector; NNB_CSS012 policies the VALUE of a `font-size:` declaration. A literal `font-size: 0.875rem` (or its px equivalent `14px`) silently restates the `--nns-font-size-md` token — when the token ladder is re-tuned the literal stays frozen and detaches from the scale. The cheapest correct fix is token substitution: `font-size: var(--nns-font-size-md)`.

**Token values are the SSOT parsed from `nn-design.css`.** The analyzer finds the canonical `nn-design.css` among the AdditionalFiles and parses its `:root` `--nns-font-size-*` declarations into a flag-set = each rem value + its px equivalent (rem × 16, root = 16px): `sm` = 0.75rem / 12px, `md` = 0.875rem / 14px, `lg` = 1rem / 16px. If `nn-design.css` is not registered as an AdditionalFile (a consumer build), the token-map is empty and the analyzer is inert — this scopes NNB_CSS012 to the producer build that owns the scale.

**Exemption posture is INVERTED vs the reach-in family** (precedent [NNB043](#nnb043) "scans the PRODUCER, no producer-path exemption"). NNB_CSS012 deliberately does NOT call the producer-path exemption ([NNB_CSS008](#NNB_CSS008) et al. exempt `**/NotNot.BlazorDesign/**` because they police *consumer* usage) — the producer CSS is exactly where the tokens and their literal-duplicating `font-size` declarations live, so it MUST be scanned. Only the vendor-file skip, the shared `CssAnalyzerEnabled=false` kill-switch, and the per-file `nnb_css012:allow-fontsize-literal` marker apply.

```css
/* ❌ NNB_CSS012 fires — rem literal equals design token --nns-font-size-md */
.label { font-size: 0.875rem; }

/* ❌ NNB_CSS012 fires — px equivalent of --nns-font-size-md (14px == 0.875rem × 16) */
.label { font-size: 14px; }

/* ✅ use the token — NO warning */
.label { font-size: var(--nns-font-size-md); }

/* ✅ off-ladder value with no matching token — NO warning */
.caption { font-size: 0.8125rem; }

/* ✅ the token DEFINITION itself — NO warning (excluded by construction) */
:root { --nns-font-size-md: 0.875rem; }

/* ✅ intentional icon-glyph size with a same-line marker — NO warning */
.icon { font-size: 16px; /* nnb_css012:allow-fontsize-literal: icon glyph */ }
```

**Documented non-goals / false-negatives** (accepted): the `--nns-font-size-*:` token definitions and `var(--nns-font-size-*)` references are excluded by construction (the `(?<![\w-])` lookbehind on the `font-size` match fails on the preceding `-`); inline `style="font-size:…"` in `.razor` markup is owned by [NNB044](#nnb044) (an explicit non-goal here); off-ladder values with no matching token never fire.

**Per-file opt-out**: `/* nnb_css012:allow-fontsize-literal: <reason> */`. **Kill-switch** (shared with the CSS suite): `<CssAnalyzerEnabled>false</CssAnalyzerEnabled>`. To soften locally:

```ini
[*.css]
dotnet_diagnostic.NNB_CSS012.severity = suggestion
```

<a id="NNB_CSS013"></a><a id="nnb_css013"></a>
### NNB_CSS013: bare `var(--nns-*)` reference to an unknown design token

**Severity:** Error
**Category:** CssModernization
**Authority:** The NnDesign token system (producer manifesto) — the `@property` + `:root` `--nns-*` declarations in `nn-design.css` / `nn-colors.css` are the SSOT for the valid token set.

A bare `var(--nns-typo)` (no fallback) to a token declared in NO authority CSS silently resolves to nothing: no compiler error, no browser error, no fallback. That single, high-value, least-visible failure is the one bug this rule guards — the authority files' own internal wiring is exactly this bare form (e.g. `nn-design.css` `var(--nns-switch-accent)`), so a typo there is invisible until the affected pixel renders empty. The cheapest correct fix is to correct the token name, or add a fallback: `var(--nns-typo, <value>)`.

**The valid `--nns-*` set is harvested from the AUTHORITY sources, two-pass.** Pass 1 builds the valid set from the authority CSS files (`nn-design.css` / `nn-colors.css` — both the `@property --nns-x { … }` registration and the `:root` `--nns-x:` value forms) UNIONED with producer `.razor` / `.razor.cs` C#-string token declarations (e.g. `--nns-switch-accent`, declared inline in producer markup yet referenced bare in the authority CSS). Component-scoped `.css` / `.razor.css` are deliberately NOT declaration sources — a stray local must not launder a mis-typed token into the valid set. Pass 2 then checks every bare reference on the CSS surfaces (`.css` / `.razor.css`) against that global set; two passes are mandatory because a reference may precede its declaration file in AdditionalFiles order.

**Authority-presence gate (producer-scoped MVP).** The analyzer is INERT (zero diagnostics) unless at least one authority CSS file (`nn-design.css` / `nn-colors.css`) is registered as an AdditionalFile. Authority CSS is producer-resident (`NotNot.BlazorDesign/wwwroot/`) and a ProjectReference / PackageReference does not import an RCL's `wwwroot` CSS, so the gate holds true ONLY in the producer build — exactly where every reference is producer-resident and the C#-string harvest is safe. Consumer-side coverage (distributing the valid set with the analyzer package so consumer builds validate too) is the ratified fast-follow, NOT this increment.

**Bare-only, by construction (the bare-vs-fallback rule).** Only a BARE reference — trailing `)` — fires; it genuinely resolves to nothing. A FALLBACK-GUARDED reference `var(--nns-x, <value>)` — trailing `,` — resolves to its fallback and is intentionally SKIPPED: it is categorically outside the "resolves to nothing" target bug. Interpolated `var(--nns-color-{role})` (a C#-string-interpolated token, not literal-checkable) and malformed tokens are likewise skipped as documented false-negatives (malformed-token grammar is the future naming rule's job).

**Exemption posture is producer-INCLUSIVE (like [NNB_CSS012](#NNB_CSS012), unlike the reach-in family).** Token-validity is universal — a bare typo'd `var(--nns-*)` is a bug in the producer authority files and samples too, and those files are exactly where bare references live. So the reference-check deliberately does NOT call the producer-path exemption (`IsExceptedPath`). Only the vendor-file skip (`*.min.css`), the shared `CssAnalyzerEnabled=false` kill-switch, and the per-file `nnb_css013:allow-unknown-token` marker apply.

```css
/* ❌ NNB_CSS013 fires (Error) — bare var() to a token declared in no authority CSS */
.switch { accent-color: var(--nns-swich-accent); }

/* ✅ fallback-guarded — resolves to the fallback, NOT flagged */
.switch { accent-color: var(--nns-swich-accent, currentColor); }

/* ✅ known token declared in nn-design.css / nn-colors.css — NO warning */
.switch { accent-color: var(--nns-switch-accent); }

/* ✅ interpolated / dynamic (C#-interpolated) token — skipped (not literal-checkable) */
.chip { color: var(--nns-color-{role}); }

/* ✅ runtime-injected token the analyzer cannot see — same-line marker */
.host { color: var(--nns-tenant-brand); /* nnb_css013:allow-unknown-token: injected at runtime */ }
```

**Severity is interim Warning → target Error.** An unknown bare reference is a hard correctness defect (peer of [NNB_CSS012](#NNB_CSS012), Error end-state). It ships at interim **Warning**; the ratchet to **Error** is a later build-gated step, contingent on a proven-clean authority-present producer build (zero residual bare hits).

**Per-file opt-out**: `/* nnb_css013:allow-unknown-token: <reason> */`. **Kill-switch** (shared with the CSS suite): `<CssAnalyzerEnabled>false</CssAnalyzerEnabled>`. To soften locally:

```ini
[*.css]
dotnet_diagnostic.NNB_CSS013.severity = suggestion
```

### NNB048: Design-system component calls application-owned JavaScript

**Severity:** Error

The design system is referenced BY applications and cannot reference them back, so a global JS identifier is an inverted dependency: it resolves in the one host that happens to define that script and throws in every other, where the surrounding `catch` turns the failure into a silently missing feature with nothing in the build to say so.

This is not hypothetical. A sidebar in the package called `vowShared.initSidebarResize` — defined in the consuming application — for a long time. Nothing failed loudly, because the only symptom was "resize does nothing" for a consumer that did not exist yet.

```csharp
// ❌ NNB048 fires — root global belongs to the host application
await JS.InvokeVoidAsync("vowShared.initSidebarResize", options);

// ❌ NNB048 fires — importing a module from outside the package
await JS.InvokeAsync<IJSObjectReference>("import", "./js/app-helpers.js");

// ✅ the preferred shape — module ships WITH the package
var module = await JS.InvokeAsync<IJSObjectReference>(
    "import", "./_content/NotNot.BlazorDesign/nnSidebarResize.js");
await module.InvokeVoidAsync("attachResize", pane, handle, dotNetRef, options);

// ✅ platform global — addresses the browser, not any application
await JS.InvokeAsync<string>("localStorage.getItem", key);

// ✅ package-owned global — registered by this package's own classic scripts
await JS.InvokeVoidAsync("nnDesign.pageState.attach", dotNetRef);
```

**Classification is by ROOT global** (the text before the first dot), which is what makes the rule sound rather than merely strict:

| Root | Verdict | Why |
|------|---------|-----|
| Browser-provided (`localStorage`, `navigator`, `eval`, …) | allowed | addresses the platform; no application involved |
| `nn`-prefixed (`nnDesign`, `nnToast`, `nnBrowserNotification`) | allowed | registered by scripts that ship WITH the package |
| anything else (`vowShared`, `myApp`, …) | **reported** | can only have come from the consuming application |

Calls made ON an `IJSObjectReference` are unrestricted — the module path was already validated at its import, so the reference is trusted.

**Detection** resolves the receiver for BOTH the instance and the reduced-extension call shape (`JS.InvokeVoidAsync(...)` compiles to the latter, so handling only the former would miss every real call site).

**When NNB048 does NOT fire:**
- The root global is browser-provided or `nn`-prefixed (table above).
- The receiver is an `IJSObjectReference` rather than `IJSRuntime`.
- The interop identifier is computed rather than constant — undecidable, and never reported.
- The `import` module path is not a constant, so it cannot be checked.
- The calling code is outside the `NotNot.BlazorDesign` namespace tree.

A platform global missing from the allow-list should be **added to `BrowserNativeRoots`**, not suppressed — the list is the rule's soundness boundary and is meant to be extended.

```csharp
#pragma warning disable NNB048 // Documented reason: <why this host global is unavoidable here>
await JS.InvokeVoidAsync("someHostGlobal.method", arg);
#pragma warning restore NNB048
```

### NNB049: Per-instance accent fill emitted without its paired on-color

**Severity:** Error

A consumer picks the accent colour, so the ink drawn on it cannot be a constant. The palette feeding these pickers includes pure black, which means any fixed foreground — a role token, a theme default, a literal — eventually paints a colour on itself at 1:1 contrast and the text disappears.

This is the defect class that passes everything else. The offending declaration was a legitimate, correctly spelled token on a component that satisfied every other analyzer. Nothing was malformed; the value was simply derived from the wrong thing — a per-ROLE constant standing in for a per-INSTANCE computation.

```csharp
// ❌ NNB049 fires — emits the fill, never derives the ink
internal string? AccentStyle(TNode node) =>
    $"--nns-hierarchy-project-guide-color:{NormalizeAccentColor(node)}";

// ✅ fill and its on-color emitted together, both from the same value
internal string? AccentStyle(TNode node)
{
    var color = NormalizeAccentColor(node);
    var onColor = ContrastCalculator.PickOnColor(color);
    return $"--nns-hierarchy-project-guide-color:{color};--nns-hierarchy-project-on-accent:{onColor}";
}
```

**Scope, stated honestly.** The rule keys on the `-guide-color` convention (how the design system names a per-instance FILL) and requires the same method to call the on-color helper. It does NOT infer whether a colour is painted as a background — that is not statically decidable. It is therefore a **regression guard over fill properties following the convention**, not a general contrast proof. A stroke/frame accent (named `-color`, carrying no text) is correctly outside its reach.

**When NNB049 does NOT fire:**
- The method calls the on-color helper anywhere in its body.
- No string literal in the method contains `-guide-color`.
- The declaring type is outside the `NotNot.BlazorDesign` namespace tree.

```csharp
#pragma warning disable NNB049 // Documented reason: reads the property, does not paint it
var current = ReadCustomProperty("--nns-hierarchy-project-guide-color");
#pragma warning restore NNB049
```

<a id="nnb050"></a>
### NNB050: Modal-content subject rendered directly in its primary sample section

**Severity:** Error

**Category:** NnDesign

The producer catalog is the authority for which sample entries demonstrate modal content. A catalog
property marked with `[NnSampleModalContent(typeof(T))]` must keep the primary `NnSampleSection` focused
on its committed projection and launch action. Rendering `<T>` directly in that matching section dumps the
dialog content into the sample page instead of teaching the modal interaction.

```csharp
// Producer classification — the analyzer reads this relation; it does not maintain a second type map.
[NnSampleModalContent(typeof(NnColorPicker))]
public static NnSampleCatalogEntry NnColorPicker { get; } = ...;
```

```razor
@* ❌ NNB050 fires — the marked primary section directly renders its modal-content subject *@
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <NnColorPicker @bind-Value="_hex" />
</NnSampleSection>

@* ✅ committed projection + trigger in the primary section *@
<NnSampleSection Entry="@NnSampleCatalog.NnColorPicker">
    <code>@_hex</code>
    <NnButton OnClick="OpenPicker">Edit</NnButton>
</NnSampleSection>

@* ✅ the marked subject belongs in a companion dialog file *@
<NnColorPicker @bind-Value="_workingHex" />
```

**Fix tiers (cheapest-correct-first):**

1. Keep the committed value or swatch and its launch action in the marked primary section; move `<T>`
   into a companion dialog component with a detached working copy.
2. If the catalog relation is intentionally not modal content, remove or correct the producer marker.
3. Only for an intentional, documented exception, use the shared NnDesign bypass or configure
   `dotnet_diagnostic.NNB050.severity` in `.editorconfig`.

**When NNB050 does NOT fire:**

- The subject is in a companion file or outside the matching primary `NnSampleSection`.
- The section is unmarked, including cross-cutting uses such as an `NnColorPicker` inside an `NnSaveState`
  sample.
- The primary section contains a trigger, committed projection, ordinary control, self-modalizing host,
  or non-modal popup rather than the marked subject.
- The opening tag is in an HTML/Razor comment, or the entry expression is not the supported direct form
  `Entry="@NnSampleCatalog.Property"`.

**Static boundary.** The analyzer resolves the marked catalog property and scans its primary section for
direct subject opening tags, including tags nested under Razor conditionals. It does not infer whether an
arbitrary callback opens a dialog, whether Apply/Cancel preserve the transaction, or whether the dialog has
the required geometry. Those behaviors require runtime/browser evidence. A future modal-content entry is
unprotected until its producer property receives the marker; names and paths are deliberately not used as
classification heuristics.

**Escape hatches:** the shared `[assembly: NnDesignBypass]` marker, the
`<NnDesignPolicyAnalyzerEnabled>false</NnDesignPolicyAnalyzerEnabled>` build-property kill-switch, or a
per-rule `dotnet_diagnostic.NNB050.severity` configuration. Use the broad bypass/kill-switch only when the
whole assembly or build is intentionally outside the NnDesign policy; a local catalog correction is the
preferred fix.

## Analyzer ID Registry (tested guard)

The documented diagnostic IDs MUST match the implemented `DiagnosticDescriptor`s. This registry is the single source; the per-ID sections above point at it. A registry test (`AnalyzerIdRegistryTests`) asserts every ID below resolves to exactly one analyzer's `SupportedDiagnostics` descriptor; duplicate IDs are checked separately, but the registry does not assert reverse coverage of every implemented descriptor, so prose IDs cannot drift from code (the failure mode that left "Planned: NNB022" stale while NNB022 was live).

| ID | Analyzer type | Severity | Scan target |
|----|---------------|----------|-------------|
| NNB022 | `NnDesignMudBlazorPolicyAnalyzer` | Error | Consumer `Mud*` markup / usings / identifiers |
| NNB043 | `NnDesignTierBExposureAnalyzer` | Error | Producer `Nn*` component params |
| NNB044 | `NnDesignInlineStyleAnalyzer` | Error | Consumer `.razor` static-literal inline `style=` |
| NNB047 | `NnSampleSectionIdUniquenessAnalyzer` | Error | `.razor` `<NnSampleSection Id>` cross-file uniqueness |
| NNB048 | `NnDesignJsPackageBoundaryAnalyzer` | Error | Producer JS interop identifier (constant only) |
| NNB049 | `NnDesignAccentOnColorPairingAnalyzer` | Error | Producer method emitting a `-guide-color` fill property |
| NNB050 | `NnModalSampleInliningAnalyzer` | Error | Marked primary `.razor` section directly rendering its catalog-designated modal subject |
| NNB_CSS008 | `CssNnReachInAnalyzer` | Error | Consumer scoped CSS (`.nns-*` reach-in) |
| NNB_CSS009 | `CssMudReachInAnalyzer` | Error | Consumer scoped CSS (`.mud-*` reach-in) |
| NNB_CSS010 | `CssModernizationAnalyzer` | Error | Consumer CSS + `.razor` attr values (viewport units) |
| NNB_CSS011 | `CssNnConsumerClassAnalyzer` | Warning | Consumer CSS class bound to an `Nn*` root via TWO deliveries: scoped `::deep` in `.razor.css` (cross-file `.razor.css`↔`.razor`) AND an inline `<style>` block in a `.razor` (intra-file) |
| NNB_CSS012 | `CssFontSizeTokenAnalyzer` | Error | `font-size` literal (rem / px) in `.css` / `.razor.css` duplicating an `--nns-font-size-*` token value |
| NNB_CSS013 | `CssNnTokenValidityAnalyzer` | Error | Unknown `--nns-*` token reference (bare, producer-scoped) |

## Configuration

Configure rules via `.editorconfig`:

```ini
[*.cs]
# Configure rule severity
dotnet_diagnostic.NNB002.severity = error
dotnet_diagnostic.NNB007.severity = warning
dotnet_diagnostic.NNB008.severity = error
dotnet_diagnostic.NNB009.severity = warning
dotnet_diagnostic.NNB010.severity = error
dotnet_diagnostic.NNB011.severity = error
dotnet_diagnostic.NNB012.severity = error
dotnet_diagnostic.NNB013.severity = error

# Async lifecycle transition-guard ordering (default: error)
dotnet_diagnostic.NNB045.severity = error

# XRay path-marker SSOT drift guard (default: warning)
dotnet_diagnostic.NNB046.severity = warning

# LiteDDD boundary rules (default: warning)
dotnet_diagnostic.NN_LDDD_001.severity = warning
dotnet_diagnostic.NN_LDDD_002.severity = warning
dotnet_diagnostic.NN_LDDD_003.severity = warning
dotnet_diagnostic.NN_LDDD_004.severity = warning
dotnet_diagnostic.NN_LDDD_005.severity = warning

# Render mode enforcement — Pattern 2 reflection canonicalization (default: warning)
dotnet_diagnostic.NN_RM_001.severity = warning

# Navigation contract enforcement (default: warning)
dotnet_diagnostic.NNB041.severity = warning
```

## Why These Rules?

### Memory Leaks from Undisposed References

`DotNetObjectReference<T>` and `IJSObjectReference` hold references that prevent garbage collection. Failing to dispose them leads to memory leaks that accumulate over component lifecycle.

### Circuit Disconnect Exception Pair

When a Blazor Server circuit disconnects, JS interop calls throw one of **two** exceptions depending on timing:
- `JSDisconnectedException` — runtime knows the circuit is gone *before* the call
- `TaskCanceledException` — call is *already in-flight*, `CancellationToken` fires

Handling one without the other leaves a gap. NNB007 ensures `JSDisconnectedException` is caught in `DisposeAsync`. NNB012 ensures `TaskCanceledException` is also caught when `JSDisconnectedException` is. NNB013 ensures the reverse — `JSDisconnectedException` is caught when cancellation is handled for JS interop calls.

## Related Analyzers

- **Meziantou.Analyzer**: MA0119 warns about JSRuntime in OnInitialized (we complement this)
- **Microsoft.AspNetCore.Components.Analyzers**: BL0005/BL0006 for parameter mutations

## License

[MPL-2.0](LICENSE.md)
