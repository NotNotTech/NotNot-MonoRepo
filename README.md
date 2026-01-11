# NotNot-MonoRepo

A collection of open source C# NuGet packages for .NET development.

[![License: MPL-2.0](https://img.shields.io/badge/License-MPL%202.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)

---

## Published Packages

### [NotNot.AppSettings](./src/nuget/NotNot.AppSettings/)

**Strongly-Typed `appsettings.json` via Source Generator**

[![NuGet](https://img.shields.io/nuget/v/NotNot.AppSettings.svg)](https://www.nuget.org/packages/NotNot.AppSettings)

Automatically generate strongly-typed C# classes from your `appsettings.json` files at compile time. No runtime reflection, just clean, type-safe configuration access.

```bash
dotnet add package NotNot.AppSettings
```

**Quick Start:**

```json
// appsettings.json
{
  "Database": {
    "ConnectionString": "Server=localhost;Database=MyApp"
  }
}
```

```csharp
// Generated class available after build
var settings = AppSettingsBinder.LoadDirect();
Console.WriteLine(settings.Database.ConnectionString);

// Or with DI
builder.Services.AddSingleton<IAppSettingsBinder, AppSettingsBinder>();
```

**Features:**
- Zero runtime overhead - all parsing happens at compile time
- Merges all `appsettings*.json` files into a unified schema
- Works with or without Dependency Injection
- Supports extending generated classes via partial classes

[Full Documentation →](./src/nuget/NotNot.AppSettings/README.md)

---

## Pre-Release Packages

> These packages are functional but not yet published to NuGet. They may have breaking changes.

### [NotNot.Bcl](./src/nuget/NotNot.Bcl/) & [NotNot.Bcl.Core](./src/nuget/NotNot.Bcl.Core/)

**Opinionated Base Class Library for Professional .NET Development**

A "kitchen sink" utility library with a clean architectural split:

| Package | Purpose | Dependencies |
|---------|---------|--------------|
| `NotNot.Bcl.Core` | Core utilities, Maybe<T> pattern, extensions | Pure .NET (no external deps) |
| `NotNot.Bcl` | Modern cross-platform with DI/hosting integration | Microsoft.Extensions.*, optional ASP.NET Core |
| `NotNot.Platform.Desktop` | Desktop OS integration (file explorer, shell) | Pure .NET |

**Highlights:**
- **Maybe<T> Pattern** - Functional error handling with structured `Problem` types
- **Pooled Memory Abstractions** - `Mem<T>`, `RefMem<T>`, `SpanGuard<T>` for low-GC allocations
- **OpenGenericMethodExecutor** - Reflection utilities for creating delegates from generic methods
- **Extension Methods** - Prefixed with `_` for easy discovery (e.g., `myList._Shuffle()`)

### [NotNot.Mixins](./src/nuget/NotNot.Mixins/)

**Source Generator for Inline Composition/Mixins Pattern**

Port of [InlineComposition](https://github.com/BlackWhiteYoshi/InlineComposition) - compose classes by inlining members from other classes.

```csharp
[InlineComposition<LoggerMixin>]
public partial class MyService { }

// Members from LoggerMixin are inlined into MyService at compile time
```

**Status:** Initial port from InlineComposition v1.5.0. Same-project inlining works; cross-assembly support planned.

### [NotNot.Analyzers](./src/nuget/NotNot.Analyzers/)

**Opinionated Code Analyzers**

Custom Roslyn analyzers for code quality:
- **Banned APIs** - Block usage of problematic APIs
- **Task Awaited on Return** - Enforce async best practices

### [NotNot.GodotNet.SourceGen](./src/nuget/NotNot.GodotNet.SourceGen/)

**Source Generators & Analyzers for Godot Engine (.NET)**

Compile-time safety for Godot C# development:

| Diagnostic | Purpose |
|------------|---------|
| `GODOT001` | Null safety in `_ExitTree()` overrides |
| `GODOT002` | Prohibited API detection (e.g., `MultiMesh.CustomAabb`) |
| `GODOT003` | Exception safety in `Dispose()` methods |

**Features:**
- `[NotNotSceneRoot]` attribute for type-safe scene instantiation
- `_ResPath` class with compile-time asset path constants
- Auto-configured via MSBuild `.props` and `.targets`

> **Note:** Requires the `NotNot.GodotNet` library (currently private). Contact maintainer if interested.

---

## Deprecated Packages

### ~~NotNot.SimStorm~~

*Asynchronous execution framework with node-based architecture. Superseded by SlimGraph in private development.*

### ~~NotNot.Server~~

*Server utilities rolled into `NotNot.Bcl` and `NotNot.Bcl.Core`.*

---

## Examples

Example projects demonstrating package usage:

| Example | Description |
|---------|-------------|
| [NotNot.AppSettings.Example](./src/example/NotNot.AppSettings.Example/) | DI and non-DI usage patterns |
| [NotNot.AppSettings.StandaloneExample](./src/example/NotNot.AppSettings.StandaloneExample/) | Minimal standalone usage |
| [NotNot.Bcl.Example.HelloConsole](./src/example/NotNot.Bcl.Example.HelloConsole/) | Console app with BCL utilities |

---

## Repository Structure

```
NotNot-MonoRepo/
├── src/
│   ├── nuget/                    # NuGet package sources
│   │   ├── NotNot.AppSettings/   # Published
│   │   ├── NotNot.Bcl/           # Pre-release
│   │   ├── NotNot.Bcl.Core/      # Pre-release
│   │   ├── NotNot.Platform.Desktop/  # Pre-release (NEW)
│   │   ├── NotNot.Mixins/        # Pre-release
│   │   ├── NotNot.Analyzers/     # Pre-release
│   │   └── NotNot.GodotNet.SourceGen/  # Pre-release
│   └── example/                  # Example projects
├── contrib/                      # Contributing documentation
├── meta/                         # Package metadata (logos, etc.)
└── vm-scripts/                   # Development VM scripts
```

---

## Why MonoRepo?

- **Unified build/test** - Build breaks are immediately visible across packages
- **Shared infrastructure** - Common MSBuild targets via `CommonSettings.targets`
- **Easy local development** - Debug/Release configurations switch between project references and NuGet packages

### Build Configurations

| Configuration | Behavior |
|---------------|----------|
| `Debug` | Uses local project references for rapid development |
| `Release` | Uses published NuGet packages for validation |

See [creating-nuget-packages.md](./contrib/creating-nuget-packages.md) for detailed workflows.

---

## Contributing

1. **Find or create an issue** describing the change
2. **Fork and create a branch** from `master`
3. **Make changes** following existing code conventions
4. **Test thoroughly** - run affected example projects
5. **Submit PR** with clear description

### Local Development

For rapid iteration, use Debug configuration which references local projects:

```xml
<ProjectReference Include="..\NotNot.AppSettings\NotNot.AppSettings.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Versioning

Packages use [MinVer](https://github.com/adamralph/minver) for semantic versioning:
- Tag format: `{PackageName}-{Major}.{Minor}.{Patch}` (e.g., `NotNot.AppSettings-2.0.3`)
- Tags trigger version updates on next build

---

## License

**[MPL-2.0](./LICENSE)** - Mozilla Public License 2.0

From [TLDRLegal](https://www.tldrlegal.com/license/mozilla-public-license-2-0-mpl-2):

> MPL is a copyleft license that is easy to comply with. You must make the source code for any of your changes available under MPL, but you can combine the MPL software with proprietary code, as long as you keep the MPL code in separate files.

**In brief:** Use freely in commercial projects. Changes to MPL files must be open-sourced; your proprietary code stays proprietary.

> **Note:** Exception: [`NotNot.Mixins`](./src/nuget/NotNot.Mixins/) is [MIT licensed](./src/nuget/NotNot.Mixins/LICENSE) (ported from InlineComposition).

---

## Contact

- **Issues:** [GitHub Issues](https://github.com/NotNotTech/NotNot-MonoRepo/issues)
- **Author:** Novaleaf / Jason Swearingen

---

*Formerly hosted at various locations including https://github.com/jasonswearingen/NotNot.AppSettings*
