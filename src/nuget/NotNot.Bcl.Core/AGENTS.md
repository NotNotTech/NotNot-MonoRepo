# VIBEGUIDE

## Core Library Philosophy
- Pure .NET library with NO web framework dependencies
- Foundation for Maybe<T> pattern and core utilities
- Must remain framework-agnostic for broad compatibility
- Used by both web and non-web applications

## Critical Architectural Boundaries
- **NO Microsoft.Extensions.* dependencies** - Those belong in NotNot.Bcl
- **NO ASP.NET Core dependencies** - Optional ASP.NET support is in NotNot.Bcl
- **NO IResult or HttpContext usage** - Web utilities belong in NotNot.Bcl
- Type detection by name/string matching only when needed for framework types

## Important usage patterns

### Maybe<T> Pattern
- `Maybe.cs` - Core Maybe monad implementation
- `MaybeJsonConverter` - JSON serialization support
- `Problem.cs` - Structured error representation
- Detection of IResult types by name (not reference) to avoid ASP.NET dependency

### Pooled Arrays/Spans Pattern: Mem<T>, RefMem<T>, and SpanGuard<T>
- `Mem.cs` - Memory abstractions for different allocation strategies
- **Mem<T>**: Pooled/wrapped memory for heap/pooled scenarios
  - Very low GC pressure: just a small tracking object (array is pooled)
  - Fast reliable Span<T> access and conversion to/from read-only mode (ReadMem<T>)
  - Flexible backing stores: array, list, memory, pooled
  - **Use for**: general-purpose, cross-method lifetime, async-compatible
- **RefMem<T>**: Unified API for both stack-allocated Span<T> and Mem<T> (NEW)
  - ref struct that wraps either Span<T> or Mem<T>
  - Span mode: zero GC pressure (pure stack allocation)
  - Mem mode: delegates all operations to wrapped Mem<T>
  - Provides consistent API regardless of backing storage
  - **Use for**: hot-path code that switches between stack/heap allocation
  - **Limitations in Span mode**: Cannot use Clone(), Map(), BatchMap(), DangerousGetArray(), AsReadMem()
- **SpanGuard<T>**: Pooled array with auto-return for "stackalloc" semantics
  - No GC pressure: pooled array automatically freed on Dispose
  - **Use for**: method-lifetime arrays with `using` pattern 


### Serialization
- `SerializationHelper.cs` - JSON configuration
- Custom converters for Maybe<T> types
- Framework-agnostic serialization patterns

### OpenGenericMethodExecutor
- Reflection utility for creating strongly-typed delegates from generic methods
- Supports both instance and static generic methods
- **Instance Methods**: Use `CreateInstanceInvoker<TDelegate>` or `CreateExactInstanceInvoker<TDelegate>`
  - Delegate first parameter must accept the declaring type instance
  - Example: `Action<MyClass, string>` for instance method `void Foo<T>(T value)`
- **Static Methods**: Use `CreateStaticInvoker<TDelegate>` or `CreateExactStaticInvoker<TDelegate>`
  - Delegate parameters match method parameters directly (no instance parameter)
  - Example: `Action<string>` for static method `static void Bar<T>(T value)`
- **Exact-Match Semantics**: No fuzzy matching, exact type equality required
- **Caching Support**: Use `GetCachedInstanceInvoker` or `GetCachedStaticInvoker` for repeated invocations
- **Search-based**: Name-based lookup with binding flags control
- **Direct MethodInfo**: `CreateExact*Invoker` bypasses search for known methods
- See [OpenGenericMethodExecutor.cs](./NotNot/Advanced/OpenGenericMethodExecutor.cs) and [OpenGenericMethodExecutor_Static.cs](./NotNot/Advanced/OpenGenericMethodExecutor_Static.cs)

### Event Utilities (NotNot namespace)

Thread-safe event registration and invocation with automatic cleanup of expired handlers.

#### Sync Events (WeakReference storage)
- **`Event<TEventArgs>`**: Standard event pattern with sender + EventArgs
- **`ActionEvent<TArgs>`**: Lightweight - no sender parameter
- **`ActionEventSpan<TArgs>`**: For high-perf scenarios passing `Span<TArgs>`

WeakReference storage allows handlers to be garbage collected without explicit unsubscription.

```csharp
var changed = new ActionEvent<string>();
changed.Handler += (msg) => Console.WriteLine(msg);
changed.Raise("Hello");  // Invokes all live handlers
```

#### Async Events (Strong reference storage)
- **`AsyncActionEvent<TArgs>`**: Async version of ActionEvent
- **`AsyncEvent<TEventArgs>`**: Async version with sender parameter

**Why strong references for async?** WeakReference + async is architecturally problematic - GC can collect handlers during the await window between `TryGetTarget` and completion.

```csharp
var dataReady = new AsyncActionEvent<byte[]>();
dataReady.Handler += async (data) => await ProcessAsync(data);
await dataReady.RaiseAsync(buffer, ct);  // Sequential await with backpressure
dataReady.Handler -= myHandler;  // MUST unsubscribe explicitly
```

**Key differences**:
| Feature | Sync (Event/ActionEvent) | Async (AsyncActionEvent/AsyncEvent) |
|---------|-------------------------|-------------------------------------|
| Storage | WeakReference | Strong reference |
| Cleanup | Automatic (GC) | Manual (must unsubscribe) |
| Backpressure | None | Sequential await |
| CancellationToken | N/A | Optional (checked between handlers) |
| Exception handling | Per-handler (continues) | First exception stops chain |

**Exception semantics**: Async variants stop invocation on first exception (bubbles to caller). If resilient "fire-all-despite-errors" semantics are needed, callers must wrap handlers individually.

### Concurrency Utilities (NotNot.Concurrency namespace)

#### Debouncer
- **Trailing-edge debouncer** using `Timer.Change` pattern
- Executes ONCE after activity stops (quiet period)
- Zero exceptions, zero allocations per `Trigger()` call
- Thread-safe with lock synchronization
- **Use for**: UI event debouncing, search-as-you-type, resize handlers

```csharp
var debouncer = new Debouncer(
    async () => await SaveAsync(),
    delayMs: 300);

debouncer.Trigger();  // Rapid calls → single execution after 300ms quiet
debouncer.Dispose();  // Cleanup
```

**Important**: Timer callbacks run on ThreadPool. For UI frameworks, marshal to the appropriate context:
- Blazor: `InvokeAsync(() => StateHasChanged())`
- WPF: `Dispatcher.Invoke(() => ...)`

#### KeyedThrottler
- **Key-based throttle/rate-limiter** ensuring minimum time BETWEEN executions
- Multiple rapid calls → multiple executions spaced by `MinDelay`
- Supports parallelism control via `MinimumParallel` and `ParallelGrowthMultiplier`
- **Use for**: API rate limiting, service-level coordination

```csharp
var throttler = new KeyedThrottler { MinDelay = TimeSpan.FromSeconds(1) };

// These will execute with ~1 second between them
await throttler.EventuallyOnce("api-call", () => CallApiAsync());
```

**Key difference**: Debouncer = fire once after quiet. KeyedThrottler = fire repeatedly with spacing.

### Extensions
- Pure .NET extension methods only.
- conventions:
	- follow same `zz_Extensions_{type}` naming convention for new extensions.
   - always prefix our extension methods with `_` to visibly signal this is our extension method.
- No Microsoft.Extensions.* dependent extensions (those go in NotNot.Bcl)
- Focus on general-purpose utilities

# VIBECACHE

**LastCommitHash**: Unknown
**Timestamp**: 2025-08-07 03:30:00

## Primary Resources
- [System.Text.Json Documentation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json)
- [.NET Base Class Library](https://learn.microsoft.com/en-us/dotnet/api/)

## Related Topics
- [../NotNot.Bcl/AGENTS.md](../NotNot.Bcl/AGENTS.md) - Modern cross-platform utilities with Microsoft.Extensions.* support

## Dependency Rules
1. **Allowed**: System.* namespaces, pure .NET libraries
2. **Forbidden**: Microsoft.AspNetCore.*, Microsoft.Extensions.* (belong in NotNot.Bcl)
3. **Exception**: Can detect framework types by fully-qualified name strings

## IResult Handling Strategy
When Maybe<T> encounters IResult types during deserialization:
- Detect by type name: `Microsoft.AspNetCore.Http.HttpResults.*`
- Throw clear exception guiding to use Maybe or Maybe<OperationResult>
- Cannot reference IResult directly - only string-based detection

## Why This Separation Matters

**NotNot.Bcl.Core** (this package) can be used in:
- Console applications with minimal dependencies
- Desktop applications (WPF, WinForms, MAUI)
- Libraries that cannot reference Microsoft.Extensions.*
- Embedded or constrained scenarios

**NotNot.Bcl** extends Core for modern environments:
- Apps using IHostBuilder, IServiceCollection, modern DI
- Console apps, services, web apps, desktop apps with modern hosting
- Optional ASP.NET Core utilities when needed

This separation ensures:
- Minimal-dependency scenarios can use Core alone
- Modern apps get full Microsoft.Extensions.* integration
- Clear architectural boundaries
