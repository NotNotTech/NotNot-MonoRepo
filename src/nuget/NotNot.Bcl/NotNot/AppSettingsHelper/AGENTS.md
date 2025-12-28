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

#### Storage Abstraction
The library supports pluggable storage backends via `ISettingsStorageProvider`:

| Provider | Location | Use Case |
|----------|----------|----------|
| `FileStorageProvider` | `NotNot.Bcl` | Desktop/server apps with file system access |
| `LocalStorageStorageProvider` | `NotNot.BlazorComponents` | Blazor apps using browser localStorage |
| Custom | Your project | Cloud storage, IndexedDB, etc. |

### Namespace Design
- **Runtime Library**: `NotNot.AppSettingsHelper` - contains `AppSettingsManager<T>`, `JsonSettingsUtils`, `ISettingsChangeAware`
- **Generated Code**: `{RootNamespace}.AppSettingsGen` - contains `AppSettings`, nested types, `AppSettingsBinder`

The namespaces are intentionally different to avoid conflicts when both are used together.

### Key Design Decisions

1. **ISettingsChangeAware Interface**
   - Marked `[EditorBrowsable(Never)]` - internal implementation detail
   - Uses underscore-prefixed method `_SetChangeCallback` to avoid conflicts
   - Implemented by source-generated settings classes

2. **Debounced Auto-Save**
   - Default 500ms debounce interval
   - Uses `Task.Delay` pattern (not `System.Threading.Timer`)
   - Errors reported via `OnAutoSaveError` callback

3. **Diff-Based Save**
   - Only changed values written to user settings file
   - Base settings remain in original files
   - Deletion semantics: null sentinel removes keys

4. **Thread Safety**
   - Lock covers serialization during save
   - `_suppressNotifications` prevents cascading during load

---

# VIBECACHE

**LastCommitHash**: 7f13062
**Timestamp**: 2025-12-28 12:00

## Primary Resources
- [`AppSettingsManager.cs`](./AppSettingsManager.cs) - Main manager class (~600 lines)
- [`JsonSettingsUtils.cs`](./JsonSettingsUtils.cs) - Diff/merge utilities (189 lines)
- [`ISettingsChangeAware.cs`](./ISettingsChangeAware.cs) - Change tracking interface (32 lines)
- [`ISettingsStorageProvider.cs`](./ISettingsStorageProvider.cs) - Storage abstraction interface (47 lines)
- [`FileStorageProvider.cs`](./FileStorageProvider.cs) - File system storage implementation (86 lines)

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

---

## Key Components

### AppSettingsManager<TSettings>
**File**: [`AppSettingsManager.cs`](./AppSettingsManager.cs)

Primary manager for settings lifecycle.

```csharp
public sealed class AppSettingsManager<TSettings> : IDisposable, IAsyncDisposable
    where TSettings : class, new()
```

#### Load Workflows
| Method | Save-Capable | Use Case |
|--------|--------------|----------|
| `LoadAsync(ct, params paths)` | Yes | File-based settings with `UserSettingsPath` |
| `LoadAsync(streams, ct)` | Yes | Stream-based settings |
| `LoadFromStorageAsync(provider, ct)` | Yes | **Storage-provider-based persistence** |
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

### ISettingsStorageProvider
**File**: [`ISettingsStorageProvider.cs`](./ISettingsStorageProvider.cs)

Async storage abstraction for pluggable persistence backends. Each provider instance represents a single settings "document" (e.g., one file or one localStorage key).

```csharp
public interface ISettingsStorageProvider
{
    ValueTask<string?> ReadAsync(CancellationToken ct = default);
    ValueTask WriteAsync(string json, CancellationToken ct = default);
    ValueTask DeleteAsync(CancellationToken ct = default);
    ValueTask<bool> ExistsAsync(CancellationToken ct = default);
}
```

### FileStorageProvider
**File**: [`FileStorageProvider.cs`](./FileStorageProvider.cs)

File system implementation of `ISettingsStorageProvider`. Thread-safe for use with `AppSettingsManager` debounced auto-save.

```csharp
public sealed class FileStorageProvider : ISettingsStorageProvider
{
    public FileStorageProvider(string filePath);
    public string FilePath { get; }
}
```

**Features:**
- Directory creation handled automatically on first write
- IOException caught on read (returns null, caller uses defaults)
- Simple, synchronous file operations wrapped in async interface

---

## Usage Examples

### Basic Load/Save
```csharp
using NotNot.AppSettingsHelper;

var manager = new AppSettingsManager<AppSettings>();
await manager.LoadAsync(default, "appsettings.json", "appsettings.Development.json");

// Modify settings
manager.Settings.Window.X = 100;

// Manual save
await manager.SaveAsync();
```

### Auto-Save Pattern
```csharp
var manager = new AppSettingsManager<AppSettings>
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

### Storage Provider Pattern
Use `LoadFromStorageAsync` for custom storage backends:

```csharp
using NotNot.AppSettingsHelper;

// File-based persistence (desktop/server apps)
var storage = new FileStorageProvider("/path/to/settings.json");
var manager = new AppSettingsManager<AppSettings>();
await manager.LoadFromStorageAsync(storage);
manager.EnableAutoSave();

// Modify settings - auto-saved to the storage provider
manager.Settings.Window.X = 100;

// Cleanup
await manager.DisposeAsync();
```

### Custom Storage Provider
Implement `ISettingsStorageProvider` for custom backends:

```csharp
public class CloudStorageProvider : ISettingsStorageProvider
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

## Known Limitations

### P1 Issues (from review)
1. ~~**Dirty state timing**~~: FIXED (335d38b) - `_isDirty` now cleared AFTER successful write
2. **Array mutation**: In-place array changes (`items[0] = x`) not tracked - reassign array instead
3. **IConfiguration persistence**: Non-file config sources (env vars) may persist to user file

### Documented Behaviors
- Top-level deletion is ambiguous (returns null = same as "no changes")
- Nested deletion works correctly via null sentinel
- All 40 tests passing
