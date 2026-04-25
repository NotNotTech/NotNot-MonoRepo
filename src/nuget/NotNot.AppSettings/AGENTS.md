# NotNot.AppSettings Source Generator

## VIBEGUIDE

### Package Overview
- **NuGet Package**: `NotNot.AppSettings`
- **Target Framework**: `netstandard2.0` (required for source generators)
- **Purpose**: Automatically generate strongly-typed C# settings classes from `appsettings*.json` files

### Critical Architecture Constraints
1. **No File I/O**: Source generators cannot use `System.IO.File` (RS1035 analyzer restriction)
   - Uses `AdditionalTextsProvider` to read JSON files as `SourceText`
   - JSON files must be marked as `<AdditionalFiles>` in consuming project

2. **netstandard2.0 Requirement**: Source generators must target netstandard2.0
   - Cannot use modern .NET APIs directly
   - Runtime functionality lives in separate library (`NotNot.Bcl`)

3. **Generated Code Implements ISettingsChangeAware**
   - Interface defined in `NotNot.Bcl.Core` runtime library (`NotNot.AppSettingsHelper` namespace)
   - Enables change tracking for auto-save functionality
   - Projects using save features must reference `NotNot.Bcl.Core` (auto-resolved transitively when `NotNot.AppSettings` NuGet package is installed)

### Key Design Decisions
- **All JSON Numbers → `double`**: The generator maps ALL JSON numeric values to `double` regardless of whether they are whole numbers. Consumers needing `int`, `long`, etc. must cast explicitly (e.g. `(int)settings.Value`). This avoids type mismatch bugs downstream — JSON has no integer/float distinction, and `double` is the natural C# representation.
- **Backing Fields**: Generated properties use private backing fields (not auto-properties) to enable change detection
- **Recursive Callback Propagation**: Nested settings objects propagate change callbacks automatically
- **Internal by Default**: Generated classes are `internal` unless `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` is set
- **Namespace Convention**: Classes in `{RootNamespace}.AppSettingsGen`, interfaces in `{RootNamespace}.AppSettingsGen.Interfaces`
- **Automatic Interface Generation**: Interfaces are ALWAYS generated alongside classes (enables DispatchProxy workflow)

### Generated Files
| File | Purpose |
|------|---------|
| `_BinderShims.g.cs` | `AppSettingsBinder`, `IAppSettingsBinder`, `LoadDirect()` methods |
| `{Namespace}.AppSettings.g.cs` | Root settings class (implements `IAppSettings`) |
| `{Namespace}.Interfaces.IAppSettings.g.cs` | Root settings interface |
| `{Namespace}._{ClassName}.{PropertyName}.g.cs` | Nested settings classes |
| `{Namespace}.Interfaces.I{PropertyName}.g.cs` | Nested settings interfaces |

---

# VIBECACHE

**LastCommitHash**: 7f13062
**Timestamp**: 2025-12-28 12:00

## Primary Resources
- [`AppSettingsGen.cs`](./AppSettingsGen.cs) - Main source generator implementation (721 lines)
- [`AppSettingsGenConfig.cs`](./AppSettingsGenConfig.cs) - Configuration model
- [`JsonMerger.cs`](./JsonMerger.cs) - JSON file merging utility
- [`ReadMe.md`](./ReadMe.md) - NuGet package documentation

## Related Topics
- [`../NotNot.Bcl/NotNot/AppSettingsHelper/AGENTS.md`](../NotNot.Bcl/NotNot/AppSettingsHelper/AGENTS.md) - Runtime manager and utilities (namespace `NotNot.AppSettingsHelper`)
- [`../NotNot.AppSettings.Tests/`](../NotNot.AppSettings.Tests/) - Test suite (40 tests)

## Child Topics
None - this is a leaf package.

## E2E Scenario Summary
1. **Developer adds NuGet package** → builds project → uses generated `AppSettings` class
2. **Developer uses DI** → registers `AppSettingsBinder` → injects settings
3. **Developer uses non-DI** → calls `AppSettingsBinder.LoadDirect()` → gets settings
4. **Developer enables auto-save** → references `NotNot.Bcl` → uses `AppSettingsManager<T>`
5. **Developer uses storage provider** → `FileUserSettingsStorageProvider` or custom `IUserSettingsStorageProvider` → `RegisterUserStorageAndTryLoad()`
6. **Developer uses DispatchProxy workflow** → `AppSettingsManager<IAppSettings>` (generated interface) → access via `.Proxy`

## Key Components

### Source Generator Entry Point
- **Class**: [`AppSettingsGen`](./AppSettingsGen.cs#L21) (inherits `IncrementalGenerator` from SGF)
- **Trigger**: `appsettings*.json` files via `AdditionalTextsProvider`
- **Output**: Generated C# source files

### Generated Code Pattern
```csharp
// Generated property with change detection
private string? _connectionString;
public string? ConnectionString
{
    get => _connectionString;
    set
    {
        if (!Equals(_connectionString, value))
        {
            _connectionString = value;
            (value as ISettingsChangeAware)?._SetChangeCallback(_onChanged);
            _onChanged?.Invoke();
        }
    }
}
```

### Configuration Options
| MSBuild Property | Effect |
|------------------|--------|
| `NotNot_AppSettings_GenPublic` | Set to `true` for public generated classes |

---

## Developer Usage

### Basic Setup
1. Install `NotNot.AppSettings` NuGet package
2. Add one or more `appsettings*.json` files at the project root. The package's `build/NotNot.AppSettings.props` auto-globs them as `<AdditionalFiles Include="appsettings*.json" />` — no manual declaration required. Set `CopyToOutputDirectory="Always"` on the files themselves if runtime file-read paths (e.g. `LoadDirect`) rely on them being present next to the built binary.
3. Build project
4. Use generated `{RootNamespace}.AppSettingsGen.AppSettings` class

### Usage Patterns

#### Non-DI (Standalone)
```csharp
var settings = AppSettingsBinder.LoadDirect();
Console.WriteLine(settings.Database.ConnectionString);
```

#### DI (ASP.NET Core)
```csharp
builder.Services.AddSingleton<IAppSettingsBinder, AppSettingsBinder>();
var app = builder.Build();
var settings = app.Services.GetRequiredService<IAppSettingsBinder>().AppSettings;
```

#### Full Mode (with Save Support — `SimpleStorageManager<T>`)
```csharp
// Requires NotNot.Bcl.Core reference (auto-resolved transitively).
using NotNot.Storage;

var defaults = AppSettingsBinder.LoadDirect();   // Build-time merged defaults.
var adapter = new FileStorageAdapter("appsettings.user.json");
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);
await manager.InitializeAsync();
manager.Update(d => d.Window.X = 100);   // Auto-saved (default 500ms debounce).
```

#### Storage Adapter Mode (custom backends)
```csharp
// IStorageAdapter has 3 methods: ReadAsync, WriteAsync, DeleteAsync.
// FileStorageAdapter is built-in. Implement IStorageAdapter for browser localStorage / cloud / IndexedDB / etc.
var adapter = new FileStorageAdapter("/path/to/settings.json");
var defaults = AppSettingsBinder.LoadDirect();
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);
await manager.InitializeAsync();
manager.Update(d => d.Theme = "dark");   // Auto-saved via adapter.
```

#### DispatchProxy Mode (using generated interface)
```csharp
// Use generated interface for DispatchProxy-based change detection.
// Namespace: {RootNamespace}.AppSettingsGen.Interfaces
using MyApp.AppSettingsGen.Interfaces;
using NotNot.Storage;

var adapter = new FileStorageAdapter("/path/to/settings.json");
// Construct via DispatchProxy to intercept property setters on IAppSettings.
// (Implementation pattern: wrap a SimpleStorageManager<AppSettings> and project
//  its Data through a DispatchProxy<IAppSettings> that triggers manager.Update on set.)
```

`SimpleStorageManager<T>` itself is generic over any `T : class, new()` and does not require an interface. The DispatchProxy pattern is an optional layer for scenarios where you want runtime-intercepted property change detection without recompiling the POCO with `ISettingsChangeAware` plumbing.

See [`../NotNot.Bcl.Core/NotNot/AppSettingsHelper/AGENTS.md`](../NotNot.Bcl.Core/NotNot/AppSettingsHelper/AGENTS.md) for `JsonSettingsUtils` + `ISettingsChangeAware` documentation, and [`../NotNot.Bcl.Core/NotNot/Storage/`](../NotNot.Bcl.Core/NotNot/Storage/) for `SimpleStorageManager<T>` + `IStorageAdapter` + `FileStorageAdapter` source.

---

## Implementation Notes

### JSON Merging
- Multiple `appsettings*.json` files are merged via a unified JSON merge core (`JsonMergeCore`, shared-source between the generator and the `NotNot.Bcl.Core` runtime).
  - **Order**: Files are sorted by ordinal path ascending; last path wins for overlapping keys (deterministic — no filesystem-dependent ordering).
  - **Objects**: Deep-merged recursively (nested keys preserved from both sides unless overwritten).
  - **Arrays**: REPLACED wholesale (not concatenated) — the later file's array supersedes the earlier file's array. (This is the post-Option-C contract; older docs may mention concatenation.)
  - **null literals**: A `null` value in a later file DELETES the key from the merged output (RFC-7396 merge-patch semantics).
  - The merged JSON is the single authoritative input to the source generator.
- Compile-time merge (generator) and runtime merge (`JsonSettingsUtils.MergeStreamsAsync` / `SimpleStorageManager<T>` reload path / `LoadDirect*` facades) share the same `JsonMergeCore` — same semantics, same edge cases.

### Type Inference
| JSON Type | C# Type |
|-----------|---------|
| `"string"` | `string` |
| `123` | `double` |
| `true/false` | `bool` |
| `null` | `object` |
| `{}` | Generated nested class |
| `[]` | Array of inferred element type |

### Known Limitations
- Array element changes are NOT tracked for auto-save
- Mixed-type arrays become `object[]`
- Top-level deletion semantics are ambiguous
