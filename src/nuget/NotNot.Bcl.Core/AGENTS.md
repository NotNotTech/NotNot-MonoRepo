# VIBEGUIDE

## Core Library Philosophy
- Lightweight .NET library with NO web framework dependencies
- Foundation for Maybe<T> pattern, core utilities, DI infrastructure, and settings management
- Must remain framework-agnostic for broad compatibility (no ASP.NET Core)
- Used by both web and non-web applications

## Critical Architectural Boundaries
- **Microsoft.Extensions.Hosting/DI/Configuration allowed** - Required for DI infrastructure and AppSettingsHelper
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

```csharp
var changed = new ActionEvent<string>();
changed.Handler += (msg) => Console.WriteLine(msg);
changed.Raise("Hello");  // Invokes all live handlers
```

#### Async Events (WeakReference storage)
- **`AsyncActionEvent<TArgs>`**: Async version of ActionEvent
- **`AsyncEvent<TEventArgs>`**: Async version with sender parameter

```csharp
var dataReady = new AsyncActionEvent<byte[]>();
dataReady.Handler += async (data) => await ProcessAsync(data);
await dataReady.RaiseAsync(buffer, ct);  // Sequential await with backpressure
// No explicit unsubscribe required - cleaned up when subscriber is GC'd
```

**All event types use WeakReference storage** - handlers are automatically cleaned up when the subscriber is garbage collected. Explicit unsubscription is optional but allows deterministic cleanup.

**Key differences**:
| Feature | Sync (Event/ActionEvent) | Async (AsyncActionEvent/AsyncEvent) |
|---------|-------------------------|-------------------------------------|
| Storage | WeakReference | WeakReference |
| Cleanup | Automatic (GC) | Automatic (GC) |
| Backpressure | None | Sequential await |
| CancellationToken | N/A | Optional (checked between handlers) |
| Exception handling | First exception stops chain | First exception stops chain |

**Lambda caution**: Anonymous lambdas not stored elsewhere may be collected before invocation. For reliable delivery, use instance method groups (`myEvent.Handler += this.OnEvent`) or store lambda delegates in subscriber fields.

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
- Focus on general-purpose utilities

### DI Infrastructure (NotNot.DI namespace)
- **DI Service Markers**: `IMsDiService`, `IDiScopedService`, `IDiSingletonService`, `IDiTransientService`, `IDiAutoInitialize`
- **MsDIContainer**: Base class for managing a DI container using Generic Host (`DisposeGuard`-based)
- **Service Registration Extensions**: `zz_Extensions_IServiceCollection_DI.cs` for auto-registration
- **Decoration**: `Advanced/_Decoration.cs` for service decoration patterns

### AppSettingsHelper (NotNot.AppSettingsHelper namespace)
- Build-time + runtime utilities co-used by the `NotNot.AppSettings` source generator (shared-source) and runtime persistence consumers.
- `JsonSettingsUtils`: Diff/merge utilities for layered JSON settings (`MergeJson`, `MergeStreamsAsync`, `Deserialize<T>`). Wraps the shared-source `JsonMergeCore` for runtime callers.
- `ISettingsChangeAware`: Interface implemented by source-generated settings classes for change-notification callbacks.
- See [AppSettingsHelper/AGENTS.md](./NotNot/AppSettingsHelper/AGENTS.md) for detailed docs.

### Storage (NotNot.Storage namespace)
- General-purpose POCO persistence primitives (NOT AppSettings-specific — works with any `T : class, new()`).
- `SimpleStorageManager<T>`: Generic storage manager with seeded-defaults deep-merge, `Update(Action<T>)` mutation tracking, debounced auto-save (`WriteDebounce`), and external-change reload (`ReadDebounce`). Lifecycle: `InitializeAsync` → `Update`/`Data` → `FlushAsync`/`ReloadAsync`/`ResetToDefaultsAsync` → `DisposeAsync`.
- `SimpleStorageOptions`: Configuration (debounce intervals, JSON serializer options).
- `IStorageAdapter`: Strategy interface (`ReadAsync`/`WriteAsync`/`DeleteAsync`) — backs the manager.
- `FileStorageAdapter`: File-system adapter with atomic write (temp-file + rename). Factory methods: `OsAppDataLocal(appName, fileName)`, `OsExeDir(fileName)`, `UserHome(fileName)`.
- `EphemeralMemoryStorageAdapter`: In-memory adapter for tests / scratch scenarios.
- `NoneStorageAdapter`: No-op adapter (read returns null, writes are dropped) for scenarios that want the manager API surface without persistence.

### Note on Mixins/Tags
- `Tags` / `ITags` remain in **NotNot.Bcl** (not here) because `[Inline<Tags>]` source generator requires same-compilation source
- See `CrossAssemblyLimitationTests` in NotNot.Mixins.Tests for details

### Diagnostic Attributes (NotNot.Bcl.Diagnostics namespace)
- `RequireMaybeReturnAttribute`: Marks classes/methods requiring Maybe return pattern
- `MaybeReturnNotRequiredAttribute`: Excludes from Maybe return pattern enforcement
- `DiagnosticSeverity` enum: Severity levels for analyzer rules
- Referenced by string name in `NotNot.Analyzers` Roslyn analyzers

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
2. **Allowed**: Microsoft.Extensions.Hosting, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Configuration (for DI infrastructure and AppSettingsHelper)
3. **Forbidden**: Microsoft.AspNetCore.* (ASP.NET Core web framework belongs in NotNot.Bcl)
4. **Exception**: Can detect framework types by fully-qualified name strings

## IResult Handling Strategy
When Maybe<T> encounters IResult types during deserialization:
- Detect by type name: `Microsoft.AspNetCore.Http.HttpResults.*`
- Throw clear exception guiding to use Maybe or Maybe<OperationResult>
- Cannot reference IResult directly - only string-based detection

## Why This Separation Matters

**NotNot.Bcl.Core** (this package) provides:
- Core patterns (Maybe<T>, pooling, diagnostics, events, concurrency)
- DI infrastructure (service markers, MsDIContainer, auto-registration)
- AppSettingsHelper (JSON diff/merge utilities + ISettingsChangeAware) + NotNot.Storage (`SimpleStorageManager<T>` + `IStorageAdapter` + adapters)
- Mixins/Tags and diagnostic attributes for Roslyn analyzers
- Dependencies limited to Microsoft.Extensions.Hosting/DI/Configuration (no ASP.NET Core)

**NotNot.Bcl** extends Core for web-capable environments:
- ASP.NET Core framework reference (`Microsoft.AspNetCore.App`)
- IResult utilities, HttpContext helpers
- Web-specific patterns and extensions

This separation ensures:
- Console apps, desktop apps, and libraries can use Core without ASP.NET Core overhead
- Web projects get full ASP.NET Core integration via NotNot.Bcl
- Clear architectural boundary: Core = no web framework, Bcl = web-capable
