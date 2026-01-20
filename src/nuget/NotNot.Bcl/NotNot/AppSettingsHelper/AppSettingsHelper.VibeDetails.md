# AppSettingsHelper - Detailed Documentation

Extended API documentation, usage examples, and migration guides extracted from AGENTS.md.

## Key Components

### SettingsManager<TSettings>

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

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISettingsChangeAware
{
    void _SetChangeCallback(Action? callback);
}
```

### IUserSettingsStorageProvider

Async storage abstraction for USER settings persistence.

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

```csharp
public class FileUserSettingsStorageProvider : IUserSettingsStorageProvider
{
    public FileUserSettingsStorageProvider(string filePath);
    public string FilePath { get; }
}
```

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

### Custom Storage Provider

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
        try { return await _client.GetStringAsync(_endpoint, ct); }
        catch (HttpRequestException) { return null; }
    }

    public async ValueTask WriteAsync(string json, CancellationToken ct = default)
        => await _client.PutAsync(_endpoint, new StringContent(json), ct);

    public async ValueTask DeleteAsync(CancellationToken ct = default)
        => await _client.DeleteAsync(_endpoint, ct);

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

```diff
- public class MyProvider : ISettingsStorageProvider
+ public class MyProvider : IUserSettingsStorageProvider
```

### From `LoadFromStorageAsync` to `RegisterUserStorageAndTryLoad`

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

### P1 Issues
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
