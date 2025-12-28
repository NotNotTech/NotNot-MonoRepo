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
   - Interface defined in `NotNot.Bcl` runtime library (`NotNot.AppSettingsHelper` namespace)
   - Enables change tracking for auto-save functionality
   - Projects using save features must reference `NotNot.Bcl`

### Key Design Decisions
- **Backing Fields**: Generated properties use private backing fields (not auto-properties) to enable change detection
- **Recursive Callback Propagation**: Nested settings objects propagate change callbacks automatically
- **Internal by Default**: Generated classes are `internal` unless `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` is set
- **Namespace Convention**: Generated code lives under `{RootNamespace}.AppSettingsGen`

### Generated Files
| File | Purpose |
|------|---------|
| `_BinderShims.g.cs` | `AppSettingsBinder`, `IAppSettingsBinder`, `LoadDirect()` methods |
| `{Namespace}.AppSettings.g.cs` | Root settings class |
| `{Namespace}._{ClassName}.{PropertyName}.g.cs` | Nested settings classes |

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
5. **Developer uses storage provider** → `FileStorageProvider` or custom `ISettingsStorageProvider` → `LoadFromStorageAsync()`

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
2. Add `appsettings.json` to project with `<AdditionalFiles Include="appsettings.json" CopyToOutputDirectory="Always" />`
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

#### Full Mode (with Save Support)
```csharp
// Requires NotNot.Bcl reference
var manager = new AppSettingsManager<AppSettings>();
await manager.LoadAsync(default, "appsettings.json");
manager.EnableAutoSave();
manager.Settings.Window.X = 100; // Auto-saved after 500ms
```

#### Storage Provider Mode
```csharp
// For custom storage backends (file, localStorage, cloud, etc.)
var storage = new FileStorageProvider("/path/to/settings.json");
var manager = new AppSettingsManager<AppSettings>();
await manager.LoadFromStorageAsync(storage);
manager.EnableAutoSave();
manager.Settings.Theme = "dark"; // Auto-saved to storage provider
```

See [`../NotNot.Bcl/NotNot/AppSettingsHelper/AGENTS.md`](../NotNot.Bcl/NotNot/AppSettingsHelper/AGENTS.md) for full storage provider documentation.

---

## Implementation Notes

### JSON Merging
- Multiple `appsettings*.json` files are merged in order
- Later files override earlier values
- Objects merge recursively; arrays concatenate

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
