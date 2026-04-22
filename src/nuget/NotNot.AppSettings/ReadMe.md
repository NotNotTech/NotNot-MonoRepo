# NotNot.AppSettings

Automatically create strongly typed C# settings objects from AppSettings.json. Uses Source Generators.

Includes a simple deserialization helper for when you are using Dependency Injection, or not.

## Table of Contents

- [NotNot.AppSettings](#notnotappsettings)
	- [Table of Contents](#table-of-contents)
	- [Getting Started](#getting-started)
	- [How it works](#how-it-works)
	- [Multi-File Merging](#multi-file-merging)
	- [Example](#example)
	- [State Management with AppSettingsManager](#state-management-with-appsettingsmanager)
	- [Storage Providers](#storage-providers)
		- [ISettingsStorageProvider](#isettingsstorageprovider)
		- [FileStorageProvider](#filestorageprovider)
		- [LocalStorageStorageProvider (Blazor)](#localstoragestorageprovider-blazor)
		- [Custom Storage Providers](#custom-storage-providers)
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

When multiple `appsettings*.json` files are present, they are merged by a unified JSON merge core (`JsonMergeCore`) that is shared-source between the compile-time source generator and the `NotNot.Bcl.Core` runtime. The same semantics apply at build-time (for schema generation) and at runtime (for `AppSettingsManager<T>.LoadAsync` and the `[Obsolete]` `LoadDirect*` facades).

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

**`LoadDirect*` is `[Obsolete]`**: the emitted `LoadDirect`, `LoadDirectFromText`, `LoadDirectFromTexts`, and `LoadDirectFromStreams` methods are now `[Obsolete]` facades that route through the same unified merge core. New code should use `AppSettingsManager<T>.LoadAsync()` (see "State Management with AppSettingsManager" below) or the standard `IConfiguration` DI binding.

**Transitive dependency note**: because `LoadDirect*` delegates to `NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync` (which lives in `NotNot.Bcl.Core`), any consumer project that invokes `LoadDirect*` must transitively reference `NotNot.Bcl.Core`. Consumers that use only the DI-constructor path (`new AppSettingsBinder(IConfiguration)`) or `AppSettingsManager<T>` directly are unaffected. This is a deliberate trade-off of unifying compile-time and runtime merge contracts; the `[Obsolete]` attribute steers new consumers toward `AppSettingsManager<T>` anyway.

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

## State Management with AppSettingsManager

For applications requiring **runtime settings persistence**, **auto-save**, and **reset to defaults**, use `AppSettingsManager<T>` from the `NotNot.Bcl` package:

```csharp
using NotNot.AppSettingsHelper;

// Load settings
var manager = new AppSettingsManager<AppSettings>();
await manager.LoadAsync(default, "appsettings.json", "appsettings.Development.json");

// Enable auto-save (changes saved after 500ms debounce)
manager.EnableAutoSave();

// Modify settings - automatically persisted to appsettings.user.json
manager.Settings.Window.X = 100;
manager.Settings.Window.Y = 200;

// Manual operations
await manager.SaveAsync();                // Immediate save
await manager.ReloadAsync();              // Reload from disk
await manager.ResetToDefaultsAsync();     // Delete user file, reload defaults

// Cleanup
await manager.DisposeAsync();
```

**Note:** Requires reference to `NotNot.Bcl` package (`Install-Package NotNot.Bcl`).

### Load Workflows

| Method | Save-Capable | Use Case |
|--------|--------------|----------|
| `LoadAsync(ct, params paths)` | Yes | File-based settings with layered merge |
| `LoadAsync(streams, ct)` | Yes | Stream-based settings |
| `LoadFromConfigurationAsync(config, basePath, ct)` | Yes | IConfiguration with save support |
| `LoadFromConfiguration(config)` | No | Read-only mode |

### Auto-Save Configuration

```csharp
// Custom debounce interval
manager.EnableAutoSave(TimeSpan.FromSeconds(2));

// Error handling
manager.OnAutoSaveError = ex => Console.WriteLine($"Auto-save failed: {ex}");

// Disable auto-save (optionally save pending changes)
await manager.DisableAutoSaveAsync(saveNow: true);
```

### User Settings File

Changes are persisted to a separate user file (default: `appsettings.user.json`). Only changed values are stored (diff-based).

```csharp
manager.UserSettingsPath = "config/user-settings.json";
```

### Reset Operations

| Method | Behavior |
|--------|----------|
| `Clear()` | Reset to base settings in memory (triggers auto-save) |
| `ResetToDefaultsAsync()` | Delete user file, reload from base files |
| `ReloadAsync()` | Discard memory changes, reload from disk |

### Known Limitations

- **Array element mutations**: In-place changes like `items[0] = x` are NOT tracked. Reassign the entire array instead.
- **Stream-based load**: Cannot distinguish base from user layers for reset operations.

## Storage Providers

For platforms without file system access (Blazor, sandboxed environments), use storage providers:

### ISettingsStorageProvider

The `ISettingsStorageProvider` interface abstracts settings persistence:

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

Built-in file system storage provider for desktop/server apps:

```csharp
using NotNot.AppSettingsHelper;

// Create storage provider for a specific file
var storage = new FileStorageProvider("/path/to/settings.json");

// Load settings using the storage provider
var manager = new AppSettingsManager<AppSettings>();
await manager.LoadFromStorageAsync(storage);

// Enable auto-save - changes persisted to the storage provider
manager.EnableAutoSave();

// Modify settings
manager.Settings.Theme = "dark";

// Cleanup
await manager.DisposeAsync();
```

**Features:**
- Automatic directory creation on first write
- Graceful handling of locked/inaccessible files
- Thread-safe for debounced auto-save

### LocalStorageStorageProvider (Blazor)

For Blazor apps, use `LocalStorageStorageProvider` from `NotNot.BlazorComponents`:

```csharp
// In Blazor component or service
@inject IJSRuntime JSRuntime

var storage = new LocalStorageStorageProvider(JSRuntime, "app-settings");
var manager = new AppSettingsManager<AppSettings>();
await manager.LoadFromStorageAsync(storage);
manager.EnableAutoSave();
```

### Custom Storage Providers

Implement `ISettingsStorageProvider` for custom backends (cloud, IndexedDB, etc.):

```csharp
public class IndexedDbStorageProvider : ISettingsStorageProvider
{
    private readonly IJSRuntime _jsRuntime;
    private readonly string _storeName;

    public IndexedDbStorageProvider(IJSRuntime jsRuntime, string storeName)
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
            return null; // Use defaults
        }
    }

    public async ValueTask WriteAsync(string json, CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbSet", ct, _storeName, json);
    }

    public async ValueTask DeleteAsync(CancellationToken ct = default)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDbDelete", ct, _storeName);
    }

    public async ValueTask<bool> ExistsAsync(CancellationToken ct = default)
    {
        return await _jsRuntime.InvokeAsync<bool>("indexedDbExists", ct, _storeName);
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
	- **NEW**: `AppSettingsManager<T>` for runtime settings persistence, auto-save, and reset operations
	- **NEW**: `JsonSettingsUtils` for diff-based save (only changed values persisted)
	- **NEW**: `ISettingsChangeAware` interface for change tracking (source-generated)
	- Breaking: Generated properties now use backing fields for change detection
	- Runtime features require `NotNot.Bcl` package reference
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
