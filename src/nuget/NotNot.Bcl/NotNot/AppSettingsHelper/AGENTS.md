# NotNot.AppSettingsHelper Runtime Library

## VIBEGUIDE

### Package Overview
- **Location**: `NotNot.Bcl` package, `NotNot.AppSettingsHelper` namespace
- **Target Framework**: `net10.0`
- **Purpose**: Runtime support for AppSettings load/save/auto-save operations

### Critical Architecture

#### Component Separation
| Component | Target | Purpose |
|-----------|--------|---------|
| `NotNot.AppSettings` | netstandard2.0 | Source generator (compile-time) |
| `NotNot.Bcl/NotNot/AppSettingsHelper` | net10.0 | Runtime manager (run-time) |

This separation exists because source generators (netstandard2.0) cannot use File I/O operations.

#### Dual Workflow Support

The library supports **two distinct workflows** for settings change detection:

| Workflow | Settings Type | Change Detection | Use Case |
|----------|---------------|------------------|----------|
| **A: Source-Generated** | Generated `AppSettings` class | `ISettingsChangeAware` callback | File-based JSON schemas, nested structures |
| **B: POCO/Interface** | Interface + POCO class | `SettingsProxy<T>` (DispatchProxy) | localStorage, code-defined settings |

**Critical**: Both workflows use `SettingsManager<T>` but with different TSettings constraints and access patterns.

#### Storage Abstraction
The library supports pluggable storage backends via `IUserSettingsStorageProvider`:

| Provider | Location | Use Case |
|----------|----------|----------|
| `FileUserSettingsStorageProvider` | `NotNot.Bcl` | Desktop/server apps with file system access |
| `LocalStorageStorageProvider` | `NotNot.BlazorComponents` | Blazor apps using browser localStorage |
| Custom | Your project | Cloud storage, IndexedDB, etc. |

### Namespace Design
- **Runtime Library**: `NotNot.AppSettingsHelper` - contains `SettingsManager<T>`, `SettingsProxy<T>`, `JsonSettingsUtils`, `ISettingsChangeAware`
- **Generated Classes**: `{RootNamespace}.AppSettingsGen` - contains `AppSettings`, nested types, `AppSettingsBinder`
- **Generated Interfaces**: `{RootNamespace}.AppSettingsGen.Interfaces` - contains `IAppSettings`, nested interfaces (for DispatchProxy workflow)

The namespaces are intentionally different to avoid conflicts when both are used together.

### Key Design Decisions

1. **ISettingsChangeAware Interface** (Workflow A)
   - Marked `[EditorBrowsable(Never)]` - internal implementation detail
   - Uses underscore-prefixed method `_SetChangeCallback` to avoid conflicts
   - Implemented by source-generated settings classes

2. **SettingsProxy<T>** (Workflow B)
   - Uses `System.Reflection.DispatchProxy` to intercept property setters
   - **Flat structure only** - nested properties not detected (throws at creation)
   - Interface must be `public` (DispatchProxy runtime constraint)

3. **Single-Mode Access**
   - Access via `Settings` (Workflow A) OR `Proxy` (Workflow B), not both
   - Accessing `Proxy` clears `ISettingsChangeAware` callback to prevent double notification
   - `Proxy` throws if TSettings is not an interface

4. **Factory Pattern**
   - Constructor accepts `Func<TSettings>` factory
   - Captures concrete type from factory for JSON deserialization of interfaces
   - Default parameterless constructor uses `Activator.CreateInstance<T>`

5. **Debounced Auto-Save**
   - Default 500ms debounce interval
   - Uses `Task.Delay` pattern (not `System.Threading.Timer`)
   - Errors reported via `OnAutoSaveError` callback

6. **Diff-Based Save**
   - Only changed values written to user settings file
   - Base settings remain in original files
   - Deletion semantics: null sentinel removes keys

7. **Thread Safety**
   - Lock covers serialization during save
   - `_suppressNotifications` prevents cascading during load

---

# VIBECACHE

**LastCommitHash**: TBD (dual-workflow refactor)
**Timestamp**: 2026-01-08

## Primary Resources
- [`SettingsManager.cs`](./SettingsManager.cs) - Main manager class with factory pattern
- [`SettingsProxy.cs`](./SettingsProxy.cs) - DispatchProxy-based change interceptor
- [`JsonSettingsUtils.cs`](./JsonSettingsUtils.cs) - Diff/merge utilities
- [`ISettingsChangeAware.cs`](./ISettingsChangeAware.cs) - Change tracking interface (Workflow A)
- [`IUserSettingsStorageProvider.cs`](./ISettingsStorageProvider.cs) - Storage abstraction interface
- [`FileUserSettingsStorageProvider.cs`](./FileStorageProvider.cs) - File system storage implementation

## Related Topics
- [`../../../NotNot.AppSettings/AGENTS.md`](../../../NotNot.AppSettings/AGENTS.md) - Source generator
- [`../../../NotNot.AppSettings.Tests/`](../../../NotNot.AppSettings.Tests/) - Test suite

## Child Topics
None - this is a leaf folder.

## E2E Scenario Summary
1. **Load settings from files** → merge layered JSON → deserialize to strongly-typed object
2. **Save changes** → compute diff → merge into user file → persist
3. **Auto-save on change** → debounce → persist only differences
4. **Reload external changes** → re-read files → update in-memory state
5. **Reset to defaults** → delete user file → reload base settings
6. **POCO workflow** → define interface + POCO → access via Proxy → auto-save on setter

---

## Key Components

### SettingsManager<TSettings>
**File**: [`SettingsManager.cs`](./SettingsManager.cs)

Primary manager for settings lifecycle. Supports both source-generated and POCO workflows.

```csharp
public sealed class SettingsManager<TSettings> : IDisposable, IAsyncDisposable
    where TSettings : class
{
    // Factory constructor (required for interface TSettings)
    public SettingsManager(Func<TSettings> factory);

    // Parameterless constructor (uses Activator.CreateInstance)
    public SettingsManager();

    // Workflow A: Source-generated settings with ISettingsChangeAware
    public TSettings Settings { get; }

    // Workflow B: Interface + POCO with DispatchProxy
    public TSettings Proxy { get; }  // Creates proxy on first access
}
```

#### Load Workflows
| Method | Save-Capable | Use Case |
|--------|--------------|----------|
| `LoadAsync(ct, params paths)` | Yes | File-based settings with `UserSettingsPath` |
| `LoadAsync(streams, ct)` | Yes | Stream-based settings |
| `RegisterUserStorageAndTryLoad(provider, ct)` | Yes | **Storage-provider-based persistence** |
| `LoadFromConfigurationAsync(config, basePath, ct)` | Yes | IConfiguration with base file |
| `LoadFromConfiguration(config)` | No | Read-only IConfiguration |

#### Save Operations
| Method | Description |
|--------|-------------|
| `SaveAsync()` | Immediate save, cancels pending auto-save |
| `ReloadAsync()` | Discard in-memory, reload from disk |
| `ResetToDefaultsAsync()` | Delete user file, reload base |
| `Clear()` | Reset to base settings (triggers auto-save) |

#### Auto-Save Control
| Method | Description |
|--------|-------------|
| `EnableAutoSave(debounce?)` | Enable with optional interval |
| `DisableAutoSaveAsync(saveNow?)` | Disable, optionally save pending |

### SettingsProxy<TSettings>
**File**: [`SettingsProxy.cs`](./SettingsProxy.cs)

DispatchProxy-based interceptor for POCO settings (Workflow B).

```csharp
public class SettingsProxy<TSettings> : DispatchProxy
    where TSettings : class
{
    public static TSettings Create(TSettings target, Action onPropertyChanged);
}
```

**Constraints:**
- `TSettings` must be an interface
- Interface must be `public` (DispatchProxy runtime requirement)
- Only top-level properties tracked (flat structure enforced)
- Nested reference types cause `InvalidOperationException` at creation

### JsonSettingsUtils
**File**: [`JsonSettingsUtils.cs`](./JsonSettingsUtils.cs)

Static utilities for JSON diffing and merging.

```csharp
public static class JsonSettingsUtils
{
    // Compute changes between original and current
    public static JsonNode? ComputeDiff(JsonNode? original, JsonNode? current);

    // Apply diff to target (null values = delete key)
    public static JsonNode MergeJson(JsonNode target, JsonNode diff);

    // Merge multiple streams (later overrides earlier)
    public static ValueTask<JsonObject> MergeStreamsAsync(IEnumerable<Stream> streams, CancellationToken ct);
}
```

#### Deletion Semantics
- When `original["key"] = value` but `current["key"]` is missing
- Diff contains `"key": null` (null sentinel)
- Merge removes the key from target

### ISettingsChangeAware
**File**: [`ISettingsChangeAware.cs`](./ISettingsChangeAware.cs)

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISettingsChangeAware
{
    void _SetChangeCallback(Action? callback);
}
```

### IUserSettingsStorageProvider
**File**: [`ISettingsStorageProvider.cs`](./ISettingsStorageProvider.cs)

Async storage abstraction for USER settings persistence. Each provider instance represents a single settings "document" (e.g., one file or one localStorage key).

```csharp
public interface IUserSettingsStorageProvider
{
    ValueTask<string?> ReadAsync(CancellationToken ct = default);
    ValueTask WriteAsync(string json, CancellationToken ct = default);
    ValueTask DeleteAsync(CancellationToken ct = default);
    ValueTask<bool> ExistsAsync(CancellationToken ct = default);
}
```

### FileUserSettingsStorageProvider
**File**: [`FileStorageProvider.cs`](./FileStorageProvider.cs)

File system implementation of `IUserSettingsStorageProvider`. Thread-safe for use with `SettingsManager` debounced auto-save.

```csharp
public class FileUserSettingsStorageProvider : IUserSettingsStorageProvider
{
    public FileUserSettingsStorageProvider(string filePath);
    public string FilePath { get; }
}
```

**Features:**
- Directory creation handled automatically on first write
- IOException caught on read (returns null, caller uses defaults)
- Simple, synchronous file operations wrapped in async interface

---

## Usage Examples

### Workflow A: Source-Generated Settings (Nested Structures)

Use when you have JSON schema files and need nested property support.

```csharp
using NotNot.AppSettingsHelper;

var manager = new SettingsManager<AppSettings>();
await manager.LoadAsync(default, "appsettings.json", "appsettings.Development.json");

// Modify nested settings - ISettingsChangeAware detects all changes
manager.Settings.Window.X = 100;
manager.Settings.Database.ConnectionString = "...";

// Manual save
await manager.SaveAsync();
```

### Workflow B: POCO/Interface Settings (Flat Structures)

Use for localStorage, code-defined settings with flat property structures.

```csharp
using NotNot.AppSettingsHelper;

// 1. Define interface (must be public)
public interface IMySettings
{
    string? Theme { get; set; }
    int? FontSize { get; set; }
}

// 2. Define POCO implementation
public class MySettings : IMySettings
{
    public string? Theme { get; set; }
    public int? FontSize { get; set; }
}

// 3. Create manager with factory
var manager = new SettingsManager<IMySettings>(() => new MySettings());
await manager.RegisterUserStorageAndTryLoad(storageProvider);
manager.EnableAutoSave();

// 4. Access via Proxy - changes auto-detected via DispatchProxy
manager.Proxy.Theme = "dark";     // Triggers auto-save
manager.Proxy.FontSize = 14;      // Triggers auto-save

await manager.DisposeAsync();
```

### Auto-Save Pattern
```csharp
var manager = new SettingsManager<AppSettings>
{
    UserSettingsPath = "appsettings.user.json",
    OnAutoSaveError = ex => Console.WriteLine($"Auto-save failed: {ex}")
};

await manager.LoadAsync(default, "appsettings.json");
manager.EnableAutoSave(TimeSpan.FromMilliseconds(500));

// Changes auto-saved after 500ms debounce
manager.Settings.Window.X = 100;
manager.Settings.Window.Y = 200;

// Cleanup
await manager.DisposeAsync();
```

### Reset to Defaults
```csharp
// Delete user settings and reload base
await manager.ResetToDefaultsAsync();

// Or soft reset (triggers auto-save with base values)
manager.Clear();
```

### Custom Storage Provider
Implement `IUserSettingsStorageProvider` for custom backends:

```csharp
public class CloudStorageProvider : IUserSettingsStorageProvider
{
    private readonly HttpClient _client;
    private readonly string _endpoint;

    public CloudStorageProvider(HttpClient client, string endpoint)
    {
        _client = client;
        _endpoint = endpoint;
    }

    public async ValueTask<string?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            return await _client.GetStringAsync(_endpoint, ct);
        }
        catch (HttpRequestException)
        {
            return null; // Use defaults
        }
    }

    public async ValueTask WriteAsync(string json, CancellationToken ct = default)
    {
        await _client.PutAsync(_endpoint, new StringContent(json), ct);
    }

    public async ValueTask DeleteAsync(CancellationToken ct = default)
    {
        await _client.DeleteAsync(_endpoint, ct);
    }

    public async ValueTask<bool> ExistsAsync(CancellationToken ct = default)
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, _endpoint), ct);
        return response.IsSuccessStatusCode;
    }
}
```

---

## Migration Guide

### From `ISettingsStorageProvider` to `IUserSettingsStorageProvider`

The interface was renamed to clarify it's for USER settings (distinct from base config).

```diff
- public class MyProvider : ISettingsStorageProvider
+ public class MyProvider : IUserSettingsStorageProvider
```

### From `LoadFromStorageAsync` to `RegisterUserStorageAndTryLoad`

The method was renamed to clarify its semantics (registers provider, attempts load).

```diff
- await manager.LoadFromStorageAsync(provider, ct);
+ await manager.RegisterUserStorageAndTryLoad(provider, ct);
```

### From `FileStorageProvider` to `FileUserSettingsStorageProvider`

```diff
- var storage = new FileStorageProvider("/path/to/settings.json");
+ var storage = new FileUserSettingsStorageProvider("/path/to/settings.json");
```

---

## Known Limitations

### P1 Issues (from review)
1. ~~**Dirty state timing**~~: FIXED (335d38b) - `_isDirty` now cleared AFTER successful write
2. **Array mutation**: In-place array changes (`items[0] = x`) not tracked - reassign array instead
3. **IConfiguration persistence**: Non-file config sources (env vars) may persist to user file

### Workflow B Limitations
1. **Flat structures only**: Nested reference-type properties throw at Proxy creation
2. **Interface visibility**: TSettings interface must be `public` (DispatchProxy constraint)
3. **Property setters only**: Method calls not tracked, only `set_*` intercepted

### Documented Behaviors
- Top-level deletion is ambiguous (returns null = same as "no changes")
- Nested deletion works correctly via null sentinel
- All 40 tests passing
