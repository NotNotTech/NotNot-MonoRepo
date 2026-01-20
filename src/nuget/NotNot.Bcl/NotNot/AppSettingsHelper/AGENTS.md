# NotNot.AppSettingsHelper Runtime Library

Runtime support for AppSettings load/save/auto-save operations. Located in `NotNot.Bcl` package.

## Architecture

| Component | Target | Purpose |
|-----------|--------|---------|
| `NotNot.AppSettings` | netstandard2.0 | Source generator (compile-time) |
| `NotNot.Bcl/NotNot/AppSettingsHelper` | net10.0 | Runtime manager (run-time) |

Separation exists because source generators (netstandard2.0) cannot use File I/O.

## Dual Workflow Support

| Workflow | Settings Type | Change Detection | Use Case |
|----------|---------------|------------------|----------|
| **A: Source-Generated** | Generated `AppSettings` class | `ISettingsChangeAware` callback | File-based JSON schemas, nested structures |
| **B: POCO/Interface** | Interface + POCO class | `SettingsProxy<T>` (DispatchProxy) | localStorage, code-defined settings |

Access via `Settings` (Workflow A) OR `Proxy` (Workflow B), not both.

## Storage Providers

| Provider | Location | Use Case |
|----------|----------|----------|
| `FileUserSettingsStorageProvider` | `NotNot.Bcl` | Desktop/server apps |
| `LocalStorageStorageProvider` | `NotNot.BlazorComponents` | Blazor localStorage |

## Key Design Decisions

1. **ISettingsChangeAware**: Marked `[EditorBrowsable(Never)]`, uses `_SetChangeCallback` to avoid conflicts
2. **SettingsProxy<T>**: Flat structure only - nested properties throw at creation
3. **Debounced Auto-Save**: 500ms default, `Task.Delay` pattern
4. **Diff-Based Save**: Only changed values written; null sentinel removes keys

## Critical Files

| File | Purpose |
|------|---------|
| `SettingsManager.cs` | Main manager with factory pattern |
| `SettingsProxy.cs` | DispatchProxy-based change interceptor |
| `JsonSettingsUtils.cs` | Diff/merge utilities |
| `IUserSettingsStorageProvider.cs` | Storage abstraction interface |
| `FileUserSettingsStorageProvider.cs` | File system implementation |

## Known Issues

- **Array mutation**: In-place changes (`items[0] = x`) not tracked - reassign array instead
- **Workflow B flat-only**: Nested reference-type properties throw at Proxy creation
- **Interface visibility**: TSettings interface must be `public` (DispatchProxy constraint)

## Details

- [AppSettingsHelper.VibeDetails.md](./AppSettingsHelper.VibeDetails.md) - API docs, usage examples, migration guide

## Related Topics

- [`../../../NotNot.AppSettings/AGENTS.md`](../../../NotNot.AppSettings/AGENTS.md) - Source generator
