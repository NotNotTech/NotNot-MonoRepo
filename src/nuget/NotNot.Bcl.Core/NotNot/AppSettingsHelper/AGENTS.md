# NotNot.AppSettingsHelper Runtime Library

Runtime utilities co-used by the `NotNot.AppSettings` source generator (build-time merge) and runtime persistence consumers. Located in `NotNot.Bcl.Core` package, namespace `NotNot.AppSettingsHelper`.

This folder contains JSON merge/diff utilities + change-detection interface only. Persistence (`SimpleStorageManager<T>` + `IStorageAdapter`) lives in the sibling [`../Storage/`](../Storage/) folder under namespace `NotNot.Storage`.

## Folder Contents

| File | Purpose |
|------|---------|
| `JsonSettingsUtils.cs` | Diff/merge utilities: `MergeJson(a, b)`, `MergeStreamsAsync(streams)`, `Deserialize<T>(node, options)`. Wraps the shared-source `JsonMergeCore` (compiled into both `NotNot.AppSettings` generator and this assembly via `<Compile Include>` link) for runtime callers. |
| `ISettingsChangeAware.cs` | Interface implemented by source-generated settings classes (via `[EditorBrowsable(Never)]`-marked `_SetChangeCallback`) for change-notification propagation. Used by `SimpleStorageManager<T>` consumers that want push-style change events. |
| `AppSettingsHelper.VibeDetails.md` | API docs, usage examples, migration notes (may be partially outdated post-R1.1 — verify against source). |

## Two-Component Split

| Assembly | Target | Role |
|---|---|---|
| `NotNot.AppSettings` | netstandard2.0 | Source generator (compile-time): merges `appsettings*.json` via `JsonMergeCore`, emits strongly-typed `_AppSettings` class. |
| `NotNot.Bcl.Core` (this folder) | net10.0 | Runtime: `JsonSettingsUtils` exposes the merge to runtime callers; `ISettingsChangeAware` is the change-detection contract; `NotNot.Storage` provides the actual persistence. |

Separation exists because Roslyn source generators MUST target netstandard2.0 (cross-SDK compatibility — they're loaded by the Roslyn compiler host). Modern .NET APIs (System.Text.Json features, IHostedService, file I/O) live here at net10.0.

## RFC-7396-ish Merge Semantics

`JsonSettingsUtils.MergeJson` and `JsonSettingsUtils.MergeStreamsAsync` apply:

| Situation | Behavior |
|---|---|
| Two objects with overlapping keys | Later wins for scalar values; deep-merge for nested objects. |
| Two objects with overlapping array values | **Later array REPLACES earlier** (NOT concatenated). |
| Later file has `"key": null` | Key is **DELETED** from output (RFC-7396 null-as-removal). |
| Non-object root in `MergeStreamsAsync`/`MergeAll` | Throws `JsonException` (fail-fast since iter2). |

Same semantics apply at compile-time (in `NotNot.AppSettings/JsonMerger.cs` → `JsonMergeCore.MergeAll`) and at runtime here. The shared-source `JsonMergeCore` is the single source of truth.

## Change-Detection Pattern (`ISettingsChangeAware`)

Source-generated settings classes implement this interface so that mutating a property triggers a callback the manager can use to schedule a debounced save. Generated code emits backing-field setters that compare-then-assign and invoke the callback only on actual change:

```csharp
private string? _theme;
public string? Theme
{
    get => _theme;
    set
    {
        if (!Equals(_theme, value))
        {
            _theme = value;
            (value as ISettingsChangeAware)?._SetChangeCallback(_onChanged);
            _onChanged?.Invoke();
        }
    }
}
```

Nested settings objects propagate the callback recursively — one mutation anywhere in the tree triggers a single debounced save at the root.

## Composing With `NotNot.Storage`

For runtime persistence, compose `SimpleStorageManager<T>` (in [`../Storage/`](../Storage/)) over an `IStorageAdapter`. Example using a generated AppSettings type:

```csharp
using NotNot.Storage;
using MyApp.AppSettingsGen;

var defaults = AppSettingsBinder.LoadDirect();   // build-time merged defaults
var adapter = new FileStorageAdapter("appsettings.user.json");
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);
await manager.InitializeAsync();
manager.Update(d => d.Theme = "dark");   // auto-saved (debounced)
await manager.DisposeAsync();
```

`SimpleStorageManager<T>` is intentionally generic — it works with any POCO, not just AppSettings. The AppSettings angle is just the most common use case.

## Known Limitations

- **Array element mutation** (`items[0] = x`) is NOT tracked by `ISettingsChangeAware`. Reassign the array, or call `manager.Update(d => d.Items[0] = x)` to force dirty-tracking.
- **Direct `manager.Data` mutation** bypasses dirty-tracking. Use `manager.Update(Action<T>)` or call `manager.FlushAsync()` afterward.
- **`SimpleStorageManager<TInterface>` via DispatchProxy** — supported pattern but requires an additional proxy layer wrapping the manager. The interface itself must be `public` (DispatchProxy constraint).

## Related Topics

- [`../../../NotNot.AppSettings/AGENTS.md`](../../../NotNot.AppSettings/AGENTS.md) — Source generator (build-time).
- [`../../../NotNot.AppSettings/ARCHITECTURE.md`](../../../NotNot.AppSettings/ARCHITECTURE.md) — Two-component design + shared-source pattern + MSBuild integration.
- [`../../../NotNot.AppSettings/MIGRATION.md`](../../../NotNot.AppSettings/MIGRATION.md) — Migration paths from `LoadDirect*` to `SimpleStorageManager<T>`.
- [`../Storage/`](../Storage/) — `SimpleStorageManager<T>` + `IStorageAdapter` + adapters.
