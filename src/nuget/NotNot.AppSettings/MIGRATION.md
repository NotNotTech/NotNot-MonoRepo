# NotNot.AppSettings — Migration Guide

**Audience**: Developers upgrading from older versions of NotNot.AppSettings, or migrating between API surfaces (`LoadDirect*` → `SimpleStorageManager<T>`, single-file → multi-file).

**Companion docs**:
- [`ReadMe.md`](./ReadMe.md) — installation + usage + examples.
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — internal design + pipeline.

---

## "I want to..." — Quick Index

| If you want to... | Read |
|---|---|
| Add a second `appsettings.{Environment}.json` file | [Single-File → Multi-File](#single-file--multi-file) |
| Stop seeing `[Obsolete]` warnings on `LoadDirect()` | [LoadDirect* → SimpleStorageManager](#loaddirect--simplestoragemanagert) |
| Resolve `CS0234: NotNot.Bcl.Core not found` | [Common Errors](#common-errors-after-upgrade) |
| Persist runtime settings changes back to disk | [Adopting SimpleStorageManager](#adopting-simplestoragemanagert) |
| Understand the merge semantics for layered files | [Merge Behavior Reference](#merge-behavior-reference) |
| Replace runtime `IConfiguration.Bind` with strongly-typed loading | [LoadDirect* → SimpleStorageManager](#loaddirect--simplestoragemanagert) |

---

## Single-File → Multi-File

If your project currently has just `appsettings.json` and you want to layer a `appsettings.Production.json` on top, you don't need to change ANY C# code. Just add the file:

```
MyProject/
├── appsettings.json              ← already there
├── appsettings.Production.json   ← add this
└── Program.cs
```

The package's `.props` auto-glob (`<AdditionalFiles Include="appsettings*.json" />`) picks up both files automatically. The source generator merges them at build time per the [merge semantics](#merge-behavior-reference) and produces a single `_AppSettings` class reflecting the unified shape.

### What you DON'T need to do

- ❌ No manual `<AdditionalFiles>` declarations in your csproj (auto-glob handles it).
- ❌ No code changes — `_AppSettings` shape comes from the merged JSON.
- ❌ No new package references.

### What you DO need to verify

- ✅ Files have `CopyToOutputDirectory="Always"` if any runtime path reads them (e.g. `LoadDirect()` reads from disk at runtime).
- ✅ Environment variable (`ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT`) is set if you want `appsettings.{Env}.json` selectively loaded by `LoadDirect()` — the runtime file resolution respects this convention.

### Verification

After build, inspect the generated `obj/Generated/.../AppSettings.g.cs` — it should contain properties for keys from BOTH files, unified per the merge rules.

---

## `LoadDirect*` → `SimpleStorageManager<T>`

The four `LoadDirect*` overloads are now `[Obsolete]` facades that route through the same unified merge core but show a compile warning steering you toward `NotNot.Storage.SimpleStorageManager<T>` (from `NotNot.Bcl.Core`). They will continue to work — the deprecation is a soft migration signal, not a hard removal.

### Conceptual difference

`LoadDirect*` is a **one-shot read**: it merges your `appsettings*.json` files into a strongly-typed object and returns it. There is no save, no change tracking, no reload.

`SimpleStorageManager<T>` is a **lifecycle-managed persistence wrapper**: it owns a `T` instance, persists mutations to a backing store via an `IStorageAdapter`, and supports auto-save (debounced), reload, and reset-to-defaults. It is intentionally general — `T` can be any POCO with a parameterless constructor, not just AppSettings.

For most apps, the migration is NOT a 1-to-1 replacement. You typically use BOTH:

- `AppSettingsBinder.LoadDirect()` (or the DI binding) → build-time merged **defaults** from `appsettings*.json`
- `SimpleStorageManager<AppSettings>` with a `FileStorageAdapter` pointing at a separate user-overrides file (e.g. `appsettings.user.json`) → runtime mutations layered on top of defaults

### Why migrate at all?

| `LoadDirect*` | `SimpleStorageManager<T>` |
|---|---|
| Read-only loading | Load + auto-save + reload + reset-to-defaults |
| No change tracking | `Update(Action<T>)` triggers debounced auto-save |
| Sync-over-async (uses `.GetAwaiter().GetResult()`) | Genuinely async (`InitializeAsync`, `FlushAsync`) |
| Requires `NotNot.Bcl.Core` transitive | Same runtime dep, but cleaner adapter abstraction |
| Frozen API surface | Active development surface |

### Migration matrix

#### Case 1: `LoadDirect()` — file-based loading, no persistence

If you only need to read settings (no save/reload), the simplest migration is to **suppress the warning** rather than restructure your code. `LoadDirect*` will continue to work indefinitely as a facade.

**Before**:
```csharp
using MyApp.AppSettingsGen;

var settings = AppSettingsBinder.LoadDirect();
Console.WriteLine(settings.Database.ConnectionString);
```

**After (option A — accept the facade)**:
```csharp
using MyApp.AppSettingsGen;

#pragma warning disable CS0618
var settings = AppSettingsBinder.LoadDirect();
#pragma warning restore CS0618
Console.WriteLine(settings.Database.ConnectionString);
```

**After (option B — DI-binding, no Obsolete)**:
```csharp
using Microsoft.Extensions.Configuration;
using MyApp.AppSettingsGen;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json", optional: true)
    .Build();
var settings = new AppSettingsBinder(config).AppSettings;
Console.WriteLine(settings.Database.ConnectionString);
```

**After (option C — full SimpleStorageManager for persistence)**: see [Adopting SimpleStorageManager](#adopting-simplestoragemanagert) below.

#### Case 2: `LoadDirectFromText(string)` — single inline JSON

**Before**:
```csharp
var json = """{ "Theme": "dark" }""";
var settings = AppSettingsBinder.LoadDirectFromText(json);
```

**After (with SimpleStorageManager — only useful if you want save/reload)**:
```csharp
// Required package: NotNot.Bcl.Core (auto-resolved transitively when NotNot.AppSettings is installed)
using NotNot.Storage;            // SimpleStorageManager, EphemeralMemoryStorageAdapter
using MyApp.AppSettingsGen;      // your generated AppSettings type

// EphemeralMemoryStorageAdapter is the built-in in-memory adapter (NotNot.Storage namespace).
// Seed it with the inline JSON via WriteAsync before InitializeAsync, OR construct an
// AppSettings instance and pass it as initialData.
var adapter = new EphemeralMemoryStorageAdapter();
await adapter.WriteAsync("""{ "Theme": "dark" }""");
var manager = new SimpleStorageManager<AppSettings>(adapter);
await manager.InitializeAsync();
var theme = manager.Data.Theme;
```

If you ONLY want a one-shot bind from a string, suppress the `LoadDirectFromText` warning and keep your code — `SimpleStorageManager` is overkill for that case.

> **Note**: `NotNot.Storage` ships three built-in adapters: `FileStorageAdapter` (production), `EphemeralMemoryStorageAdapter` (tests / scratch), `NoneStorageAdapter` (no-op). All implement `IStorageAdapter` (3 methods: `ReadAsync`, `WriteAsync`, `DeleteAsync`). Implement your own for browser localStorage, cloud, IndexedDB, etc.

#### Case 3: `LoadDirectFromTexts(params string[])` — multiple inline JSONs

This case ONLY makes sense for one-shot merging of N JSON strings. There is no direct `SimpleStorageManager` equivalent because the manager is built around a single backing store.

**Recommendation**: keep `LoadDirectFromTexts` and suppress the warning. The facade is preserved exactly because some scenarios (test scaffolding, dynamic JSON sources) genuinely need it.

```csharp
#pragma warning disable CS0618
var settings = AppSettingsBinder.LoadDirectFromTexts(baseJson, overrideJson);
#pragma warning restore CS0618
```

#### Case 4: `LoadDirectFromStreams(List<Stream>)` — already stream-based

Same as Case 3 — `SimpleStorageManager` works with a single adapter, not a list of streams. Keep `LoadDirectFromStreams` for one-shot merge scenarios.

```csharp
#pragma warning disable CS0618
var settings = AppSettingsBinder.LoadDirectFromStreams(myStreams);
#pragma warning restore CS0618
```

If you really want to fold N streams into a `SimpleStorageManager<T>`, do the merge yourself first via `JsonSettingsUtils.MergeStreamsAsync` and feed the result into a `SimpleStorageManager` constructor that takes seeded defaults:

```csharp
using NotNot.AppSettingsHelper;
using System.Text.Json;

var merged = await JsonSettingsUtils.MergeStreamsAsync(myStreams);
var defaults = JsonSerializer.Deserialize<AppSettings>(merged?.ToJsonString() ?? "{}");
var adapter = new FileStorageAdapter("appsettings.user.json");
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);
await manager.InitializeAsync();
```

### Behavior parity guarantee

`LoadDirect*` and `SimpleStorageManager<T>.InitializeAsync` BOTH route through `JsonSettingsUtils.MergeStreamsAsync` for the merge step (since the iter1 fix). For identical inputs, the merge output is byte-identical. The differences are purely lifecycle:

- `LoadDirect*` returns the bound POCO and exits — no further state.
- `SimpleStorageManager` retains the POCO under its `Data` property, tracks mutations via `Update(...)`, and persists to its `IStorageAdapter` (debounced).

---

## Adopting `SimpleStorageManager<T>`

If you want the full lifecycle (load + auto-save + reset), here's the canonical pattern for AppSettings consumers.

> **Required**:
> - **Package**: `NotNot.Bcl.Core` (auto-resolved transitively when you install `NotNot.AppSettings` via NuGet — no explicit `<PackageReference>` needed for typical consumers)
> - **Namespace**: `NotNot.Storage` (for `SimpleStorageManager<T>`, `IStorageAdapter`, `FileStorageAdapter`)
> - **Optional namespace**: `NotNot.AppSettingsHelper` (only if you call `JsonSettingsUtils.MergeJson` / `MergeStreamsAsync` directly — the canonical pattern below does not)

```csharp
using NotNot.Storage;            // SimpleStorageManager, FileStorageAdapter
using MyApp.AppSettingsGen;      // your generated AppSettings + AppSettingsBinder

// 1. Get build-time defaults from the source-generated AppSettings
var defaults = AppSettingsBinder.LoadDirect();   // OR: bind via IConfiguration

// 2. Choose where runtime overrides persist
var adapter = new FileStorageAdapter("appsettings.user.json");
//   Or use a built-in factory:
//   FileStorageAdapter.OsAppDataLocal("MyApp", "settings.json")
//   FileStorageAdapter.OsExeDir("settings.json")
//   FileStorageAdapter.UserHome(".myapp.json")

// 3. Construct the manager with seeded defaults
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);

// 4. Initialize (loads existing user file if present, deep-merged onto defaults)
await manager.InitializeAsync();

// 5. Read settings
Console.WriteLine(manager.Data.Database.ConnectionString);

// 6. Mutate via Update — this triggers the debounced auto-save
manager.Update(d => d.UI.Theme = "dark");

// 7. Manual operations
await manager.FlushAsync();             // immediate write (skip debounce)
await manager.ReloadAsync();            // discard memory changes, reload from adapter
await manager.ResetToDefaultsAsync();   // restore seeded defaults + flush

// 8. Subscribe to events (optional)
manager.OnDataChanged += () => Console.WriteLine("Data changed");
manager.OnDataLoaded  += () => Console.WriteLine("Data loaded");
manager.OnError       += ex => Console.Error.WriteLine($"Storage error: {ex.Message}");

// 9. Cleanup — flushes pending writes
await manager.DisposeAsync();
```

### Save semantics — adapter-driven

The manager writes the ENTIRE serialized `Data` POCO to its adapter on save. The base `appsettings*.json` files are **never** modified — runtime overrides land in whatever file the `FileStorageAdapter` points at (e.g. `appsettings.user.json`).

On reload, the manager:
1. Reads the adapter's stored JSON
2. Deep-merges it onto the seeded defaults via `JsonSettingsUtils.MergeJson` (RFC-7396 semantics)
3. Deserializes the merged result into a fresh `T` instance

This means: removing the user file via `ResetToDefaultsAsync` (or deleting it manually) reverts to the seeded defaults from your build-time `appsettings*.json` schema.

### Direct mutation is NOT tracked

```csharp
manager.Data.UI.Theme = "dark";  // ❌ NOT auto-saved (direct mutation, no tracking)
manager.Update(d => d.UI.Theme = "dark");  // ✅ Auto-saved (Update wraps in lock + debounce)
```

If you must mutate directly (e.g. binding to UI), call `await manager.FlushAsync()` afterward.

### DI integration

For ASP.NET Core / generic-host scenarios:

```csharp
builder.Services.AddSingleton<SimpleStorageManager<AppSettings>>(sp =>
{
    var defaults = AppSettingsBinder.LoadDirect();
    var adapter = FileStorageAdapter.OsAppDataLocal("MyApp", "settings.json");
    var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);
    manager.InitializeAsync().GetAwaiter().GetResult();
    return manager;
});
```

The `.GetAwaiter().GetResult()` blocks once at startup — runtime usage is fully async after that.

### Custom storage adapters

`IStorageAdapter` has 3 methods:

```csharp
public interface IStorageAdapter
{
    ValueTask<string?> ReadAsync(CancellationToken ct = default);
    ValueTask WriteAsync(string data, CancellationToken ct = default);
    ValueTask DeleteAsync(CancellationToken ct = default);
}
```

Implement this interface for browser localStorage, cloud storage, encrypted file, in-memory test seam, etc. The manager only sees JSON strings — it doesn't care what the adapter is doing under the hood.

---

## Common Errors After Upgrade

### `CS0234: The type or namespace name 'Bcl' does not exist in the namespace 'NotNot'`

**Cause**: You're calling `LoadDirect*` (which now requires `NotNot.Bcl.Core` at runtime), but your project doesn't reference `NotNot.Bcl.Core` directly.

**Resolution**: Add the package reference. NuGet should auto-resolve this for fresh installs of recent versions (the `.nupkg` declares `NotNot.Bcl.Core` as a transitive dep), but older lockfiles may need explicit:

```xml
<ItemGroup>
    <PackageReference Include="NotNot.Bcl.Core" Version="*" />
</ItemGroup>
```

(Match `Version` to whatever version your `NotNot.AppSettings` resolves.)

### `CS0618: 'AppSettingsBinder.LoadDirect()' is obsolete: 'Use NotNot.Storage.SimpleStorageManager<T> for runtime settings load/save. ...'`

**Cause**: You're calling a `LoadDirect*` method which is now `[Obsolete]`.

**Resolution options** (in order of preference):
1. If you need persistence (save/reload): migrate to `SimpleStorageManager<T>` per [Adopting SimpleStorageManager](#adopting-simplestoragemanagert).
2. If you only need read-once binding: switch to direct `IConfiguration` binding (Case 1, option B above) — no Obsolete warning, same result.
3. Suppress the warning at the specific call site if `LoadDirect*` is genuinely the right tool (test scaffolding, simple console app):
   ```csharp
   #pragma warning disable CS0618
   var settings = AppSettingsBinder.LoadDirect();
   #pragma warning restore CS0618
   ```
4. Suppress project-wide via `<NoWarn>$(NoWarn);CS0618</NoWarn>` — **NOT RECOMMENDED**, hides legitimate other deprecations.

### Generator output is empty / `_AppSettings` doesn't exist

**Cause**: One of:
1. The package wasn't installed correctly (no `.props`/`.targets` auto-import).
2. You're consuming via `<ProjectReference OutputItemType="Analyzer">` without explicit `<Import>` of the `.props`/`.targets` files (see [`ReadMe.md` § Local Development](./ReadMe.md#local-development-reference-csproj-not-nuget)).
3. No `appsettings*.json` files exist in the project root (so AdditionalFiles is empty).
4. `<NotNot_AppSettings_AutoGlob>false</NotNot_AppSettings_AutoGlob>` is set somewhere (disables auto-glob; you must declare `<AdditionalFiles>` manually).

**Diagnosis**: Inspect `obj/Generated/.../` after build. If empty, check the build log for "Processing source generation: rootNamespace=..." — its absence means the generator never received any files.

### Settings differ between build-time and runtime

**Cause**: Pre-iter1 versions had divergent `JsonDocumentOptions` between the generator and runtime — the generator allowed trailing commas + comments, runtime did not. JSON valid at build-time would fail at runtime.

**Resolution**: Upgrade to the latest version where `JsonMergeCore.DefaultDocumentOptions` unifies both parse sites. If you previously had to remove trailing commas to make runtime work, you can re-add them.

### `JsonException: ... root must be an object`

**Cause** (post-iter2): `MergeStreamsAsync` and `JsonMergeCore.MergeAll` now throw on non-object root JSON (e.g. `[1, 2, 3]` at the top level), where pre-iter2 versions silently dropped or reset.

**Resolution**: Wrap the array in an object root, e.g. `{ "items": [1, 2, 3] }`. This was always the intent — top-level non-objects don't make sense for settings schemas.

### Test project: `CS0433: The type 'JsonMergeCore' exists in both 'NotNot.AppSettings' and 'NotNot.Bcl.Core'`

**Cause**: `JsonMergeCore` is shared-source — compiled into BOTH assemblies under the same FQN. If your test project references both via `ProjectReference` (or one analyzer + one library reference), you'll hit this.

**Resolution**: Use MSBuild `Aliases="..."` on the conflicting `ProjectReference`, then `extern alias` in your test files:

```xml
<!-- In test csproj -->
<ProjectReference Include="..\NotNot.AppSettings\NotNot.AppSettings.csproj"
                  Aliases="appsettings_gen" />
```

```csharp
// In test file
extern alias appsettings_gen;
using JsonMergeCore = appsettings_gen::NotNot.AppSettingsInternal.JsonMergeCore;
```

Working example in [`../NotNot.AppSettings.Tests/NotNot.AppSettings.Tests.csproj`](../NotNot.AppSettings.Tests/NotNot.AppSettings.Tests.csproj).

---

## Merge Behavior Reference

When multiple `appsettings*.json` files are present, they merge per these rules. **The same rules apply at compile-time AND runtime** — no surprises.

### Order

Files are sorted by **ordinal path ascending** before merge. `appsettings.Development.json` < `appsettings.Production.json` < `appsettings.Staging.json` lexically. **Last path wins** for overlapping keys.

This is deterministic across OS / filesystem ordering quirks. If you want a specific override order, name your files accordingly (e.g. prefix with `01-`, `02-` for explicit ordering).

### Merge rules (RFC-7396-ish)

| Earlier file | Later file | Result |
|---|---|---|
| `{ "key": "v1" }` | `{ "key": "v2" }` | `{ "key": "v2" }` (override) |
| `{ "key": "v1" }` | `{ "other": "v2" }` | `{ "key": "v1", "other": "v2" }` (union) |
| `{ "obj": { "a": 1 } }` | `{ "obj": { "b": 2 } }` | `{ "obj": { "a": 1, "b": 2 } }` (deep merge) |
| `{ "arr": [1, 2] }` | `{ "arr": [3] }` | `{ "arr": [3] }` (REPLACE — not concat) |
| `{ "key": "v1" }` | `{ "key": null }` | `{}` (DELETE — RFC-7396 null-as-removal) |

### Why arrays REPLACE (not CONCAT)

CONCAT made the merge non-idempotent (running merge twice produced different results from running it once). REPLACE matches the runtime `JsonSettingsUtils.MergeJson` contract. Pre-iter1 versions had divergent behavior — the generator concatenated, the runtime replaced. Iter1 unified both on REPLACE.

### Why `null` literals DELETE

This is the standard RFC-7396 JSON Merge Patch convention. It enables a layered file to explicitly REMOVE a key inherited from an earlier file. If you genuinely want a `null` value, you can't express it in this merge scheme — but typed settings shouldn't have null values anyway (the strong typing already makes optional values nullable).

---

## Migration Checklist

Use this checklist when upgrading an existing project:

- [ ] **Verify build succeeds** after package upgrade — generator may produce additional warnings (`CS0618` on `LoadDirect*` call sites).
- [ ] **Add `NotNot.Bcl.Core` package reference** if missing (newer versions auto-resolve via transitive dep, but older lockfiles may need explicit).
- [ ] **Inspect generated `_AppSettings`** for any unexpected new properties from layered `appsettings*.json` files (auto-glob is on by default).
- [ ] **For `CS0618` on `LoadDirect*`**: decide per call site — migrate to `SimpleStorageManager<T>` (if you want persistence), switch to `IConfiguration` binding (if read-once is enough), or suppress the warning (if `LoadDirect*` is genuinely the right tool).
- [ ] **Test multi-environment scenarios** if you have multiple `appsettings*.json` files (Development/Production/Staging).
- [ ] **Test trailing-commas + comments** in your `appsettings*.json` files if you'd worked around pre-iter1 strict parsing — they should now work at both build-time and runtime.
- [ ] **Audit non-object root JSON** — pre-iter2 silent-drop is now a fail-fast `JsonException`. Wrap arrays in `{ "items": [...] }` if needed.
- [ ] **Choose a storage adapter** if migrating to `SimpleStorageManager<T>` — `FileStorageAdapter` for filesystem, custom `IStorageAdapter` for browser localStorage / cloud / etc.

---

## Further Reading

- [`ReadMe.md`](./ReadMe.md) — Full installation, examples, troubleshooting.
- [`ARCHITECTURE.md`](./ARCHITECTURE.md) — Internal pipeline, shared-source pattern, MSBuild integration.
- [`AGENTS.md`](./AGENTS.md) — Terse machine-readable reference.
