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
```

## Why These Rules?

### Memory Leaks from Undisposed References

`DotNetObjectReference<T>` and `IJSObjectReference` hold references that prevent garbage collection. Failing to dispose them leads to memory leaks that accumulate over component lifecycle.

### JSDisconnectedException Crashes

When a Blazor Server circuit disconnects (e.g., user navigates away), JS interop calls throw `JSDisconnectedException`. If not caught in `DisposeAsync`, this can crash the component disposal chain.

## Related Analyzers

- **Meziantou.Analyzer**: MA0119 warns about JSRuntime in OnInitialized (we complement this)
- **Microsoft.AspNetCore.Components.Analyzers**: BL0005/BL0006 for parameter mutations

## License

[MPL-2.0](LICENSE.md)
