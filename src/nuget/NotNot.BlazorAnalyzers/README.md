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

**Severity:** Warning
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

**Severity escalation** — escalate via `.editorconfig` once the migration backlog reaches zero:

```ini
[*.{razor,cs}]
dotnet_diagnostic.NNB022.severity = error
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

<a id="nnb_css009"></a>
### NNB_CSS009: Do not reach into MudBlazor internal `.mud-*` classes

**Severity:** Warning
**Category:** CssModernization
**Authority:** [`NotNot.BlazorDesign/AGENTS.md`](https://github.com/NotNotTech/NotNot-MonoRepo) → Consumer Policy · `NnDesignLayoutContract.VowSpec` SPEC-008

Sibling of [NNB_CSS008](#NNB_CSS008) (which guards the `.nn-*` namespace). MudBlazor is the primitive layer wrapped internally by NnDesign; its `.mud-*` classes are private implementation detail two layers down from the consumer. A consumer that restyles one (e.g. `::deep .mud-tab { min-width:80px }` or a bare `.mud-tabs-header { min-height:unset }`) couples to MudBlazor's private DOM through the NnDesign wrapper and breaks when either layer's DOM changes. The sanctioned alternative is the NnDesign wrapper's published contract (declarative-`NnTabs` trigger-sizing default + `FillPanels` / per-panel `data-nn-fill`, `NnContentSection` params).

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

**Allow-list: EMPTY** — unlike `.nn-*` (which publishes `.nn-chord-revealed` + `.nns-*` as a public contract), MudBlazor exposes no public `.mud-*` styling contract to the consumer. Every non-exempt `.mud-*` subject is a reach-in.

**Path-based exception buckets** (analyzer skips diagnostic emission):

| Bucket | Applies to |
|--------|-----------|
| NnDesign producer / wrapper internals | Any file under `**/NotNot.BlazorDesign/**` (the wrapper legitimately restyles `.mud-*` — that's its job) |
| Samples | `**/NnDesignSamples/**`, `**/Pages/Samples/**` |
| Samples / global theme CSS | `nn-design-samples.css`, `app.css` (file-name allow-list) |

**Per-file opt-out**: `/* nnb_css009:allow-reachin: <reason> */`. **Kill-switch** (shared with the CSS suite): `<CssAnalyzerEnabled>false</CssAnalyzerEnabled>`.

**Severity escalation** — escalate via `.editorconfig` once the consumer tree is verified `.mud-*`-reach-in-clean:

```ini
[*.css]
dotnet_diagnostic.NNB_CSS009.severity = error
```

## Analyzer ID Registry (tested guard)

The documented diagnostic IDs MUST match the implemented `DiagnosticDescriptor`s. This registry is the single source; the per-ID sections above point at it. A registry test (`AnalyzerIdRegistryTests`) asserts every ID below resolves to exactly one analyzer's `SupportedDiagnostics` descriptor (and the reverse — no implemented descriptor is unregistered), so prose IDs cannot drift from code (the failure mode that left "Planned: NNB022" stale while NNB022 was live).

| ID | Analyzer type | Severity | Scan target |
|----|---------------|----------|-------------|
| NNB043 | `NnDesignTierBExposureAnalyzer` | Error | Producer `Nn*` component params |
| NNB_CSS008 | `CssNnReachInAnalyzer` | Error | Consumer scoped CSS (`.nn-*` reach-in) |
| NNB_CSS009 | `CssMudReachInAnalyzer` | Warning | Consumer scoped CSS (`.mud-*` reach-in) |

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
