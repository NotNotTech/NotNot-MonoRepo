# NotNot.AppSettings

Automatically create strongly typed C# settings objects from AppSettings.json. Uses Source Generators.

Includes a simple deserialization helper for when you are using Dependency Injection, or not.

For the full appsettings normalization recipe used by VOW and the example projects, see
[`docs/appsettings-normalization.md`](../../../../../docs/appsettings-normalization.md). It covers
host-vs-typed splits, client whitelisting, and review grep patterns for keeping `appsettings.json`
out of the typed graph.

## Further Reading

In addition to this ReadMe (the user-facing quick-start and feature reference), two companion
documents live alongside the package source:

- [**ARCHITECTURE.md**](./ARCHITECTURE.md) — Internal architecture: how the source generator pipeline
  works, the shared-source `JsonMergeCore` pattern between the build-time generator and the
  runtime `NotNot.Bcl.Core` library, build-time vs runtime parity, type inference rules, MSBuild
  integration, and the design decisions log. **Read this if** you want to understand *how* the
  package works internally — useful for contributors and advanced consumers.
- [**MIGRATION.md**](./MIGRATION.md) — Practical migration guide for upgrading existing projects:
  single-file → multi-file, `LoadDirect*` → `NotNot.Storage.SimpleStorageManager<T>` (from
  `NotNot.Bcl.Core`), common errors after upgrade (CS0234, CS0618, generator-empty,
  build-vs-runtime drift), merge behavior reference, and a step-by-step checklist. **Read this if**
  you have an existing project on an older version and need to know what changes.

## Package & Namespace Map

If you're new and wondering what to install / which `using` directive you need, this table is the at-a-glance answer. The build-time generator and the runtime persistence primitives live in **two separate packages** and **three separate namespaces** — install one NuGet package (`NotNot.AppSettings`) and the runtime package (`NotNot.Bcl.Core`) is resolved transitively.

| Goal | Package (NuGet) | Namespace (`using`) | Key types |
|---|---|---|---|
| Source-generate strongly-typed settings from `appsettings*.json` | `NotNot.AppSettings` | `{YourRoot}.AppSettingsGen`<br/>`{YourRoot}.AppSettingsGen.Interfaces` | `AppSettings` (root POCO), `IAppSettings`, `AppSettingsBinder` |
| Read merged settings via DI (`IConfiguration`-bound) | `NotNot.AppSettings` | `{YourRoot}.AppSettingsGen` | `AppSettingsBinder` (DI ctor), `IAppSettingsBinder`, `IConfiguration._AppSettings()` ext |
| Read merged settings without DI (`[Obsolete]` facade) | `NotNot.AppSettings` (+ transitive `NotNot.Bcl.Core` for the merge core) | `{YourRoot}.AppSettingsGen` | `AppSettingsBinder.LoadDirect()` and 3 sibling overloads — all `[Obsolete]`, steered toward `SimpleStorageManager<T>` |
| **Persist runtime settings** (auto-save, reload, reset, atomic writes) | `NotNot.Bcl.Core` (transitive via `NotNot.AppSettings`) | `NotNot.Storage` | `SimpleStorageManager<T>` (lifecycle), `IStorageAdapter` (strategy), `FileStorageAdapter` (built-in), `EphemeralMemoryStorageAdapter` (tests), `NoneStorageAdapter` (no-op), `SimpleStorageOptions` (debounce config) |
| Layered JSON merge utilities (compose your own pipeline) | `NotNot.Bcl.Core` (transitive) | `NotNot.AppSettingsHelper` | `JsonSettingsUtils.MergeJson`, `JsonSettingsUtils.MergeStreamsAsync`, `JsonSettingsUtils.Deserialize<T>`, `ISettingsChangeAware` (implemented by source-generated POCOs) |

**Install** (typical): `dotnet add package NotNot.AppSettings` — `NotNot.Bcl.Core` resolves automatically.

**Best-practice runtime composition** (`appsettings*.json` defaults + per-user overrides + auto-save):
```csharp
using NotNot.Storage;            // SimpleStorageManager, FileStorageAdapter
using MyApp.AppSettingsGen;      // your generated AppSettings + AppSettingsBinder

var defaults = AppSettingsBinder.LoadDirect();                       // build-time merged defaults
var adapter  = FileStorageAdapter.OsAppDataLocal("MyApp", "settings.json");
var manager  = new SimpleStorageManager<AppSettings>(adapter, defaults);
await manager.InitializeAsync();
manager.Update(d => d.Theme = "dark");                               // debounced auto-save
```

See [State Management with SimpleStorageManager](#state-management-with-simplestoragemanager) for the full lifecycle, [Storage Adapters](#storage-adapters) for the adapter strategy interface, and [MIGRATION.md](./MIGRATION.md) if you're upgrading from `LoadDirect*`.

## Table of Contents

- [NotNot.AppSettings](#notnotappsettings)
	- [Further Reading](#further-reading)
	- [Package & Namespace Map](#package--namespace-map)
	- [Table of Contents](#table-of-contents)
	- [Getting Started](#getting-started)
	- [How it works](#how-it-works)
	- [Multi-File Merging](#multi-file-merging)
	- [Example](#example)
	- [State Management with SimpleStorageManager](#state-management-with-simplestoragemanager)
	- [Storage Adapters](#storage-adapters)
		- [IStorageAdapter](#istorageadapter)
		- [FileStorageAdapter](#filestorageadapter)
		- [Browser localStorage (Blazor)](#browser-localstorage-blazor)
		- [Custom Storage Adapters](#custom-storage-adapters)
	- [Troubleshooting / Tips](#troubleshooting--tips)
		- [How to access the `AppSettings` class from external code?](#how-to-access-the-appsettings-class-from-external-code)
		- [How to extend the generated `AppSettings` class?](#how-to-extend-the-generated-appsettings-class)
		- [Some settings not being loaded (value is `NULL`). Or:  My `appSettings.Development.json` file is not loaded](#some-settings-not-being-loaded-value-is-null-or--my-appsettingsdevelopmentjson-file-is-not-loaded)
		- [Intellisense not working for `AppSettings` class](#intellisense-not-working-for-appsettings-class)
		- [Why are some of my nodes typed as `object`?](#why-are-some-of-my-nodes-typed-as-object)
		- [Tip: Backup generated code in your git repository](#tip-backup-generated-code-in-your-git-repository)
	- [Contribute](#contribute)
		- [Local Development (Reference `.csproj`, not Nuget)](#local-development-reference-csproj-not-nuget)
		- [Nuget](#nuget)
	- [Acknowledgments](#acknowledgments)
	- [License: MPL-2.0](#license-mpl-20)
	- [Notable Changes](#notable-changes)



## Getting Started

1) Add an `appsettings.json` file to your project *(make sure it's copied to the output)*.
2) **[Install this nuget package `NotNot.AppSettings`](https://www.nuget.org/packages/NotNot.AppSettings)**.
3) Build your project
4) Use the generated `AppSettings` class in your code. (See the example section below).

## How it works

During your project's build process, NotNot.AppSettings will parse the  `appsettings*.json` in your project's root folder.  These files are all merged into a single schema. Using source-generators it then creates a set of csharp classes that matches each node in the json hierarchy.

After building your project, an `AppSettings` class contains the strongly-typed definitions,
and an `AppSettingsBinder` helper/loader util will be found under the `{YourProjectRootNamespace}.AppSettingsGen` namespace.

## Multi-File Merging

When multiple `appsettings*.json` files are present, they are merged by a unified JSON merge core (`JsonMergeCore`) that is shared-source between the compile-time source generator and the `NotNot.Bcl.Core` runtime. The same semantics apply at build-time (for schema generation) and at runtime (for `NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync`, the `NotNot.Storage.SimpleStorageManager<T>` reload path, and the `[Obsolete]` `LoadDirect*` facades).

**Merge semantics (RFC-7396-ish):**

- **Deterministic order**: files sorted by ordinal path ascending; **last path wins** for overlapping keys. No filesystem-dependent ordering.
- **Objects**: deep-merged recursively. Nested keys from both files are preserved unless explicitly overwritten.
- **Arrays**: **REPLACED wholesale**, not concatenated. The later file's array fully supersedes the earlier file's array.
- **`null` literal**: DELETES the key from the merged output (RFC-7396 merge-patch deletion semantics).

**Example:**

`appsettings.json`:
```json
{
  "Db": { "Host": "localhost", "Port": 5432 },
  "Tags": [ "dev", "local" ]
}
```

`appsettings.Production.json`:
```json
{
  "Db": { "Host": "prod.db", "Retries": 3 },
  "Tags": [ "prod" ],
  "LegacyKey": null
}
```

Merged result:
```json
{
  "Db": { "Host": "prod.db", "Port": 5432, "Retries": 3 },
  "Tags": [ "prod" ]
}
```

Note: `Db` is deep-merged (`Port` preserved, `Host` overwritten, `Retries` added), `Tags` is fully replaced, and `LegacyKey: null` deletes the key entirely.

**`LoadDirect*` is `[Obsolete]`**: the emitted `LoadDirect`, `LoadDirectFromText`, `LoadDirectFromTexts`, and `LoadDirectFromStreams` methods are now `[Obsolete]` facades that route through the same unified merge core. New code that needs runtime persistence (load + auto-save + reload) should use `NotNot.Storage.SimpleStorageManager<T>` (see "State Management with SimpleStorageManager" below). For read-only binding, the standard `IConfiguration` DI path is the simplest option.

**Transitive dependency note**: because `LoadDirect*` delegates to `NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync` (which lives in `NotNot.Bcl.Core`), any consumer project that invokes `LoadDirect*` must transitively reference `NotNot.Bcl.Core`. Consumers that use only the DI-constructor path (`new AppSettingsBinder(IConfiguration)`) or `SimpleStorageManager<T>` directly are unaffected. This is a deliberate trade-off of unifying compile-time and runtime merge contracts; the `[Obsolete]` attribute steers new consumers toward the explicit composition (`SimpleStorageManager<AppSettings>` over a `FileStorageAdapter`) anyway.

## Example

`appsettings.json`

```json
{
  "Hello": {
	"World": "Hello back at you!"
  }
}
```

`Program.cs`

```csharp
using ExampleApp.AppSettingsGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExampleApp;
public class Program
{ 
   public static async Task Main(string[] args)
   {
      {
         Console.WriteLine("NON-DI EXAMPLE");
                  
         var appSettings = ExampleApp.AppSettingsGen.AppSettingsBinder.LoadDirect();
         Console.WriteLine(appSettings.Hello.World);         
      }
      {
         Console.WriteLine("DI EXAMPLE");

         HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
         builder.Services.AddSingleton<IAppSettingsBinder, AppSettingsBinder>();
         var app = builder.Build();
         var appSettings = app.Services.GetRequiredService<IAppSettingsBinder>().AppSettings;
         Console.WriteLine(appSettings.Hello.World);
      }
   }
}
```

*See the **`./NotNot.AppSettings.Example`** folder in the repository for a fully buildable version of this example.*

### New in `v2.0.3`
There's now an IConfiguration extension method to make usage even easier:

```csharp
public class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var appSettings = builder.Configuration._AppSettings();
		Console.WriteLine($"appSettings.AllowedHosts={appSettings.AllowedHosts}");

```

## State Management with SimpleStorageManager

For applications requiring **runtime settings persistence**, **debounced auto-save**, and **reset to defaults**, compose `NotNot.Storage.SimpleStorageManager<T>` (from `NotNot.Bcl.Core`) over an `IStorageAdapter`. `SimpleStorageManager<T>` is a general-purpose POCO persistence wrapper — it works with any `T : class, new()`, not just `AppSettings`.

> **Required**:
> - **Package**: `NotNot.Bcl.Core` (auto-resolved transitively when you install `NotNot.AppSettings` via NuGet — no separate `<PackageReference>` needed for typical consumers)
> - **Namespace**: `NotNot.Storage` (for `SimpleStorageManager<T>`, `IStorageAdapter`, `FileStorageAdapter`)
> - **Namespace**: `{YourRoot}.AppSettingsGen` (for the source-generated `AppSettings` POCO + `AppSettingsBinder.LoadDirect()`)

### Canonical pattern

```csharp
using NotNot.Storage;            // SimpleStorageManager, FileStorageAdapter
using ExampleApp.AppSettingsGen; // ExampleApp's generated AppSettings + AppSettingsBinder

// 1. Build-time defaults from your appsettings*.json schema (one-shot bind).
var defaults = AppSettingsBinder.LoadDirect();

// 2. Choose where runtime overrides persist (separate from your base appsettings*.json).
var adapter = new FileStorageAdapter("appsettings.user.json");
//   Or use a built-in factory:
//   FileStorageAdapter.OsAppDataLocal("MyApp", "settings.json")
//   FileStorageAdapter.OsExeDir("settings.json")
//   FileStorageAdapter.UserHome(".myapp.json")

// 3. Construct the manager with seeded defaults — stored JSON deep-merges onto these.
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);

// 4. Initialize (loads existing user file if present).
await manager.InitializeAsync();

// 5. Read settings — Data is the strongly-typed POCO.
Console.WriteLine(manager.Data.Database.ConnectionString);

// 6. Mutate via Update — triggers debounced auto-save (default 500ms).
manager.Update(d => { d.Window.X = 100; d.Window.Y = 200; });

// 7. Manual operations.
await manager.FlushAsync();             // Immediate write (skip debounce).
await manager.ReloadAsync();            // Discard memory changes, reload from adapter.
await manager.ResetToDefaultsAsync();   // Restore seeded defaults + flush.

// 8. Subscribe to events (optional).
manager.OnDataChanged += () => Console.WriteLine("Data changed.");
manager.OnDataLoaded  += () => Console.WriteLine("Data loaded.");
manager.OnError       += ex => Console.Error.WriteLine($"Storage error: {ex.Message}");

// 9. Cleanup — flushes pending writes.
await manager.DisposeAsync();
```

**Note:** Requires reference to `NotNot.Bcl.Core` package. When installed via the `NotNot.AppSettings` NuGet package, `NotNot.Bcl.Core` is resolved transitively.

### Direct mutation is NOT tracked

Direct property assignment bypasses dirty-tracking and auto-save:

```csharp
manager.Data.UI.Theme = "dark";              // ❌ NOT auto-saved (direct mutation).
manager.Update(d => d.UI.Theme = "dark");    // ✅ Auto-saved.
```

If you must mutate directly (e.g. binding to a UI control), call `await manager.FlushAsync()` afterward.

### Save semantics

The manager writes the ENTIRE serialized `Data` POCO to its adapter on save. The base `appsettings*.json` files are **never** modified — runtime overrides land in whatever file the `FileStorageAdapter` points at (e.g. `appsettings.user.json`).

On reload, the manager:
1. Reads the adapter's stored JSON.
2. Deep-merges it onto the seeded defaults via `JsonSettingsUtils.MergeJson` (RFC-7396 semantics — same as the build-time merge).
3. Deserializes the merged result into a fresh `T` instance.

To reset, call `ResetToDefaultsAsync` (or delete the user file manually).

### Auto-save configuration

Customize debounce intervals via `SimpleStorageOptions`:

```csharp
var options = new SimpleStorageOptions
{
    WriteDebounce = TimeSpan.FromSeconds(2),   // Write debounce window.
    ReadDebounce = TimeSpan.FromMilliseconds(250),  // External-change reload debounce.
};
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults, options);
```

### Reset operations

| Method | Behavior |
|--------|----------|
| `ResetToDefaultsAsync()` | Restore seeded defaults in memory + flush to adapter. |
| `ReloadAsync()` | Discard memory changes, reload from adapter. |
| `NotifyExternalChange()` | Schedule a debounced reload (e.g. when an external process modified the file). |

### Known Limitations

- **Array element mutations**: In-place changes like `items[0] = x` are NOT tracked unless wrapped in `Update(...)`.
- **Bypassing `Update`**: Direct mutation of `manager.Data` does not trigger save. Use `Update` or call `FlushAsync` manually.

## Storage Adapters

`IStorageAdapter` is the strategy interface that backs `SimpleStorageManager<T>`. Built-in implementations cover file-based persistence; custom adapters cover Blazor localStorage, cloud, or any other backend.

### IStorageAdapter

The `IStorageAdapter` interface (in `NotNot.Storage`) abstracts settings persistence:

```csharp
public interface IStorageAdapter
{
    ValueTask<string?> ReadAsync(CancellationToken ct = default);
    ValueTask WriteAsync(string data, CancellationToken ct = default);
    ValueTask DeleteAsync(CancellationToken ct = default);
}
```

A `null` return from `ReadAsync` indicates "no stored data" — `SimpleStorageManager` falls back to seeded defaults (or a fresh `T()` if none seeded).

### FileStorageAdapter

Built-in file system adapter for desktop/server apps. Uses **atomic writes** (temp file + rename) so partial-write crashes don't corrupt the settings file.

```csharp
using NotNot.Storage;

// Direct file path.
var storage = new FileStorageAdapter("/path/to/settings.json");

// Or factory methods for common locations:
var os = FileStorageAdapter.OsAppDataLocal("MyApp", "settings.json");
//   Windows: %LOCALAPPDATA%/MyApp/settings.json
//   Linux/macOS: ~/.local/share/MyApp/settings.json

var exe = FileStorageAdapter.OsExeDir("settings.json");
//   Alongside the running executable.

var home = FileStorageAdapter.UserHome(".myapp.json");
//   User home directory.

var manager = new SimpleStorageManager<AppSettings>(storage, defaults);
await manager.InitializeAsync();
manager.Update(d => d.Theme = "dark");   // Persisted via the adapter.
await manager.DisposeAsync();
```

**Features:**
- Atomic write (temp file + rename) — partial-write safe.
- Automatic directory creation on first write.
- Graceful handling of missing files (returns `null` from `ReadAsync` instead of throwing).

### Browser localStorage (Blazor)

`NotNot.Bcl.Core` does **not** ship a built-in browser localStorage adapter (the package is framework-agnostic and does not reference Blazor / `IJSRuntime`). For Blazor WebAssembly, implement `IStorageAdapter` over `IJSRuntime` yourself — the implementation is small. The example below is a complete reference implementation you can copy into your project.

```csharp
// Reference implementation — copy into your Blazor project (NotNot.Bcl.Core does not ship this).
public sealed class BrowserLocalStorageAdapter : IStorageAdapter, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly string _key;

    public BrowserLocalStorageAdapter(IJSRuntime js, string key) { _js = js; _key = key; }

    public async ValueTask<string?> ReadAsync(CancellationToken ct = default)
        => await _js.InvokeAsync<string?>("localStorage.getItem", ct, _key);

    public async ValueTask WriteAsync(string data, CancellationToken ct = default)
        => await _js.InvokeVoidAsync("localStorage.setItem", ct, _key, data);

    public async ValueTask DeleteAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("localStorage.removeItem", ct, _key);

    public ValueTask DisposeAsync() => default;
}

// Usage:
var adapter = new BrowserLocalStorageAdapter(JSRuntime, "app-settings");
var manager = new SimpleStorageManager<AppSettings>(adapter, defaults);
await manager.InitializeAsync();
```

`SimpleStorageManager.DisposeAsync` will invoke `IAsyncDisposable.DisposeAsync` on the adapter if it implements it — useful for adapters that hold JS interop handles.

### Custom Storage Adapters

Implement `IStorageAdapter` for cloud storage, IndexedDB, encrypted file, in-memory test seam, etc. Three methods, no other contract:

```csharp
public class IndexedDbStorageAdapter : IStorageAdapter
{
    private readonly IJSRuntime _jsRuntime;
    private readonly string _storeName;

    public IndexedDbStorageAdapter(IJSRuntime jsRuntime, string storeName)
    {
        _jsRuntime = jsRuntime;
        _storeName = storeName;
    }

    public async ValueTask<string?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("indexedDbGet", ct, _storeName);
        }
        catch
        {
            return null; // null → SimpleStorageManager falls back to seeded defaults.
        }
    }

    public async ValueTask WriteAsync(string data, CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbSet", ct, _storeName, data);
    }

    public async ValueTask DeleteAsync(CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbDelete", ct, _storeName);
    }
}
```

## Troubleshooting / Tips

### How to access the generated classes from external code?

To prevent namespace collisions with other projects, the generated classes are `internal` by default.
If you need to access it from another project, the best solution is to make a wrapper class in the project that uses the generated code.
Alternatively, you can make the generated code `public`:

- v`2.0.0` and later: you can add the following to your `.csproj` file:
```xml
	<PropertyGroup>
		<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>
	</PropertyGroup>
```
- v`1.x` and earlier: The `AppSettings` class is `public` by default.


### How to extend the generated `AppSettings` class?

You can extend any/all of the generated code by creating a partial class in the same namespace.

### Some settings not being loaded (value is `NULL`). Or:  My `appSettings.Development.json` file is not loaded

Ensure the proper environment variable is set.   For example, The `appSettings.Development.json` file is only loaded when the `ASPNETCORE_ENVIRONMENT` 
or `DOTNET_ENVIORNMENT` environment variable is set to `Development`.

### Intellisense not working for `AppSettings` class

A strongly-typed `AppSettings` (and sub-classes) is recreated every time you build your project.
This may confuse your IDE and you might need to restart it to get intellisense working again.

### Why are some of my nodes typed as `object`?

Under some circumstances, the type of a node's value in `appsettings.json` would be ambiguous, so `object` is used:

- If the value is `null` or `undefined`
- If the value is a POJO/Array/primitive in one appsettings file, and a different one of those three in another.


### Tip: Backup generated code in your git repository

Add this to your `.csproj` to have the code output to `./Generated` and have it be ***ignored*** by your project.
This way you can check it into source control and have a backup of the generated code in case you need to stop using this package.
```xml
<!--output the source generator build files-->
<Target Name="DeleteFolder" BeforeTargets="PreBuildEvent">
	<RemoveDir Directories="$(CompilerGeneratedFilesOutputPath)" />
</Target>	
<PropertyGroup>
	<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
	<CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
<ItemGroup>
	<!--Exclude the output of source generators from the compilation-->
	<Compile Remove="$(CompilerGeneratedFilesOutputPath)/**" />
</ItemGroup>
```

## Contribute

- If you find value from this project, consider sponsoring.

### Local Development (Reference `.csproj`, not Nuget)

- Add ` OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the `<ProjectReference/>`
- **IMPORTANT**: When using Project References, you must manually import the targets file to expose MSBuild properties to the source generator:
```xml
<!-- Import NotNot.AppSettings targets to expose MSBuild properties to source generator -->
<Import Project="path/to/NotNot.AppSettings.targets" />
```
Without this import, properties like `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` will be ignored.
- beware when attempting to update nuget packages, it will likely break the Source Generator.  Default to just leaving them as is, unless you want to spend time troubleshooting sourcegen thrown exceptions.

### Consuming as ProjectReference Analyzer

When consuming `NotNot.AppSettings` via raw `ProjectReference` with `OutputItemType="Analyzer"` (instead of a NuGet package reference — typical in monorepos), you **must** explicitly import the generator's `build/*.props` and `*.targets` files. NuGet packaging applies the auto-import convention for you, but raw `OutputItemType="Analyzer"` ProjectReferences do **not** — this is a NuGet vs MSBuild ergonomics asymmetry, not a bug in this generator.

Without the explicit `<Import>` block, `<AdditionalFiles Include="appsettings*.json" />` AutoGlob is inert and the generator sees no input files (silent — no diagnostic).

**Example pattern** (from `src/example/NotNot.AppSettings.Example/NotNot.AppSettings.Example.csproj`):

```xml
<ItemGroup>
  <ProjectReference
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false"
      Include="..\..\nuget\NotNot.AppSettings\NotNot.AppSettings.csproj" />
</ItemGroup>

<!--
  R1.3 workaround, root cause deferred (analyzer-ProjectReference auto-import is a NuGet-package
  convention; OutputItemType="Analyzer" ProjectReference does not auto-import the referenced
  project's build/*.props|*.targets — must be explicit-imported per consumer).
-->
<Import Project="..\..\nuget\NotNot.AppSettings\NotNot.AppSettings.props" />
<Import Project="..\..\nuget\NotNot.AppSettings\NotNot.AppSettings.targets" />
```

**Consumers using the NuGet package reference path (`<PackageReference Include="NotNot.AppSettings" />`) do not need this** — NuGet handles the `build/*.props|*.targets` auto-import automatically.

### Nuget

- current version is set via `MinVer`, which matches the repo git tags.
- read the repo's `Contrib/` folder for more info.


## Acknowledgments

- This project was inspired by https://github.com/FrodeHus/AppSettingsSourceGenerator which unfortunately did not match my needs in fundamental ways.

## License: MPL-2.0

A summary from [TldrLegal](https://www.tldrlegal.com/license/mozilla-public-license-2-0-mpl-2):

>   MPL is a copyleft license that is easy to comply with. You must make the source code for any of your changes available under MPL, but you can combine the MPL software with proprietary code, as long as you keep the MPL code in separate files. Version 2.0 is, by default, compatible with LGPL and GPL version 2 or greater. You can distribute binaries under a proprietary license, as long as you make the source available under MPL.

**In brief**: You can basically use this project however you want, but all changes to it must be open sourced.

## Notable Changes

- **`3.0.0`** :
	- **NEW**: `NotNot.Storage.SimpleStorageManager<T>` (in `NotNot.Bcl.Core`) for runtime POCO persistence, debounced auto-save, and reset operations.
	- **NEW**: `IStorageAdapter` strategy interface + `FileStorageAdapter` (atomic-write file backend). Implement `IStorageAdapter` for browser localStorage, cloud, or custom backends.
	- **NEW**: `JsonSettingsUtils.MergeJson` for RFC-7396 deep merge (deep-merge objects, REPLACE arrays, null-DELETE keys) used by both build-time and runtime.
	- **NEW**: `ISettingsChangeAware` interface for change tracking (source-generated).
	- **NEW**: Multi-file `appsettings*.json` auto-glob via package's `build/*.props` — multiple files merge into a single `_AppSettings` schema (compile-time + runtime use the same merge core).
	- **DEPRECATED**: `LoadDirect*` overloads on `AppSettingsBinder` are now `[Obsolete]` facades. They still work; new code should use `SimpleStorageManager<T>` for persistence or `IConfiguration` binding for read-only.
	- Breaking: Generated properties now use backing fields for change detection.
	- Runtime features require `NotNot.Bcl.Core` package reference (auto-resolved transitively).
- **`2.0.3`** :
	- New IConfiguration extension method to make usage easier
- **`2.0.2`** :
  - Generated code files now use the `.g.cs` suffix (a7dc012)
  - Added an extension method for faster Dependency Injection acquisition (7fd7f1f)
  - Improved Linux compatibility for finding `appsettings.json` files (a29270f)
- **`2.0.0`** : generated code is now `internal` by default.  Make it public by adding `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` to your `.csproj`
- **`1.2.1`** : improve doc for missing appSettings.json, handle projects with blank default namespace. move to new repository.
- **`1.1.1`** : make the nuget package `<PrivateAsset>` so only the project that directly references it uses it. 
  - (needed for example: test projects)
- **`1.0.0`** : polish and readme tweaks.  **Put a fork in it, it's done!**
- **`0.12.0`** : change appsettings read logic to use "AdditionalFiles" workflow instead of File.IO
- **`0.10.0`** : Initial Release.
