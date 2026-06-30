
# NotNot.Analyzers

**Modern C# code analyzers for reliability and best practices in .NET applications.**

[![NuGet](https://img.shields.io/nuget/v/NotNot.Analyzers.svg)](https://www.nuget.org/packages/NotNot.Analyzers/)
[![License: MPL-2.0](https://img.shields.io/badge/License-MPL--2.0-blue.svg)](https://opensource.org/licenses/MPL-2.0)

## 🚀 Features

- **Automatic Code Fixes** - Get instant suggestions to fix common async/await issues
- **Context-Aware Analysis** - Smart detection based on your code's context (UI, library, test code)
- **Performance Optimized** - Concurrent execution with minimal overhead
- **Configurable Rules** - Customize severity and behavior via EditorConfig
- **Modern IDE Integration** - Works seamlessly with Visual Studio, VS Code, and Rider

## 📦 Installation

### Package Manager Console
```powershell
Install-Package NotNot.Analyzers
```

### .NET CLI
```bash
dotnet add package NotNot.Analyzers
```

### PackageReference
```xml
<PackageReference Include="NotNot.Analyzers" Version="latest" PrivateAssets="all" />
```

> **Note:** Use `PrivateAssets="all"` to prevent the analyzer from being transitively referenced.

## 🔍 Analyzer Rules

### NN_R001: Task should be awaited, assigned, or returned
**Severity:** Error  
**Category:** Reliability

Detects fire-and-forget task patterns that can lead to unhandled exceptions.

```csharp
// ❌ Problematic
DoWorkAsync(); // Fire-and-forget

// ✅ Fixed automatically
await DoWorkAsync();        // Option 1: Await
_ = DoWorkAsync();          // Option 2: Explicit discard
var task = DoWorkAsync();   // Option 3: Store for later
return DoWorkAsync();       // Option 4: Return from method
```

### NN_R002: Task<T> result should be observed
**Severity:** Error  
**Category:** Reliability

Ensures that Task<T> results are properly observed when awaited.

```csharp
// ❌ Problematic
await GetDataAsync(); // Result ignored

// ✅ Fixed automatically
var data = await GetDataAsync();    // Option 1: Use result
_ = await GetDataAsync();           // Option 2: Explicit discard
return await GetDataAsync();        // Option 3: Return result
```

### NN_C001: ref var declarations must use r_ prefix
**Severity:** Error
**Category:** Naming
**Automatic Fix:** ✅ Adds r_ prefix and renames all references

Enforces r_ prefix naming convention for ref var declarations to make reference semantics visible.

```csharp
// ❌ Problematic
ref var cell = ref board.RefCell(point);
cell.Value = 5; // Hard to see this mutates the board!

// ✅ Fixed automatically (Ctrl+. in IDE)
ref var r_cell = ref board.RefCell(point);
r_cell.Value = 5; // Clear mutation intent

// Also applies to foreach
foreach (ref var r_item in span) { r_item++; } // OK
foreach (ref var item in span) { item++; } // Error → Auto-fix to r_item

// And ref readonly
ref readonly var r_value = ref array[0]; // OK
ref readonly var value = ref array[0]; // Error → Auto-fix to r_value
```

**Code Fix Behavior:**
- Automatically adds `r_` prefix to variable name
- Renames all references in scope using Roslyn Renamer API
- Handles collision detection (generates r_cell1 if r_cell exists)
- Available via Ctrl+. or lightbulb in IDE
- Supports "Fix All in Document/Project/Solution"

**Why this matters:**
- Ref variables directly mutate underlying data structures
- The r_ prefix makes mutation danger immediately visible
- Complements existing conventions: h_ for handles, p_ for pointers
- Critical for ECS architectures with heavy ref usage

### NN_C002: Variables with r_ prefix must be declared with ref
**Severity:** Error
**Category:** Naming
**Automatic Fix:** ✅ Removes r_ prefix and renames all references

Prevents misuse of the r_ prefix on non-ref variables (bidirectional enforcement with NN_C001).

```csharp
// ❌ Problematic
var r_tile = 22; // r_ prefix but not ref - confusing!
void Method(int r_value) { } // Parameter with r_ but not ref

// ✅ Fixed automatically (Ctrl+. in IDE)
var tile = 22; // Auto-fix removes misleading prefix
void Method(int value) { } // Auto-fix removes prefix from parameter

// ✅ OK - ref-like parameters allowed
void Method(ref int r_value) { } // ref parameter - OK
void Method(out int r_result) { } // out parameter - OK
void Method(in int r_input) { } // in parameter - OK
```

**Code Fix Behavior:**
- Automatically removes `r_` prefix from variable name
- Renames all references in scope using Roslyn Renamer API
- Handles collision detection automatically
- Available via Ctrl+. or lightbulb in IDE
- Supports "Fix All in Document/Project/Solution"

**Why this matters:**
- Prevents accidental misuse of r_ convention
- Ensures r_ prefix reliably indicates reference semantics
- Catches copy-paste errors where ref keyword removed but name not updated
- Bidirectional enforcement: ref vars need r_, r_ vars must be ref


### NOTNOT001: Destructor must be protected with try/catch
**Severity:** Error
**Category:** Reliability
**Automatic Fix:** ✅ Wraps destructor body in try/catch

Ensures destructors (finalizers) wrap their logic in try/catch blocks to prevent unhandled exceptions from crashing the application.

```csharp
// ❌ Problematic
~MyClass()
{
    // Finalizers should never throw
    Dispose();
}

// ✅ Fixed automatically (Ctrl+. in IDE)
~MyClass()
{
    try
    {
        // Finalizers should never throw
        Dispose();
    }
    catch (Exception ex)
    {
        ex.__RethrowUnlessAppShutdownOrRelease();
    }
}
```

**Code Fix Behavior:**
- Automatically wraps entire destructor body in try/catch block
- Uses `__RethrowUnlessAppShutdownOrRelease()` to suppress exceptions during app shutdown or release builds
- Allows exceptions to propagate during debug builds for easier debugging
- Available via Ctrl+. or lightbulb in IDE
- Supports "Fix All in Document/Project/Solution"

**Why this matters:**
- Unhandled exceptions in finalizers can cause fatal crashes
- Exceptions in destructors can cause process termination
- `__RethrowUnlessAppShutdownOrRelease()` balances safety and debuggability
- Critical for resource management and application stability

### NN_R005: Catch block must rethrow general exception
**Severity:** Error  
**Category:** Reliability

Catch blocks catching `Exception`, `SystemException`, or bare `catch` must rethrow. Specific exception types can be swallowed.

```csharp
// ❌ Problematic
catch (Exception ex) { Log(ex); }  // Logs but doesn't rethrow

// ✅ Fixed
catch (Exception ex) { __.DebugAssertOnce(ex); return fallback; }
catch (Exception ex) { throw; }
```

### NN_R006: Empty catch block silently swallows exception
**Severity:** Error  
**Category:** Reliability

Detects catch blocks with zero statements that silently swallow exceptions — any exception type. Complementary to NN_R005 which targets general exception types without rethrow.

**Concurrency note**: Pre-checks (`File.Exists()`, `dict.ContainsKey()`) have TOCTOU races in concurrent scenarios. Atomic operations (`FileMode.CreateNew`, `ConcurrentDictionary.TryAdd`, DB unique constraints) legitimately need try/catch. The catch block is justified — but it still must not be empty.

```csharp
// ❌ Problematic — empty catch silently swallows
catch (IOException)
{
    // File already exists (cross-process race) — skip
}

// ✅ Option 1: Avoid exceptions (non-concurrent only — TOCTOU race if concurrent)
if (File.Exists(path)) return;

// ✅ Option 2: Debug assertion (good for atomic race-condition catches)
catch (IOException ex)
{
    __.DebugAssertOnce(ex);  // Visible in DEBUG, logged once, graceful in RELEASE
}

// ✅ Option 3: Logging (good for expected concurrent conflicts)
catch (IOException ex)
{
    _logger.LogDebug(ex, "Atomic write conflict — file already exists");
}
```

<a id="NN_R007"></a>
### NN_R007: Hand-rolled atomic file write is not concurrency-safe
**Severity:** Error
**Category:** Reliability

Detects a hand-rolled atomic file write — a write to a **deterministic** `.tmp` path followed by `File.Move(temp, final, overwrite: true)` — that lacks the unique-temp + per-path-serialization concurrency guard. Two correctness mechanisms are **both** required; removing either re-opens a race:

1. **Unique temp name per writer** — on Windows the default `FileShare.Read` on a *shared* `.tmp` open makes the second same-path writer throw `ERROR_SHARING_VIOLATION`.
2. **Per-final-path serialization of the entire write+rename** — concurrent `MoveFileEx`/`MOVEFILE_REPLACE_EXISTING` calls racing **one** destination are not mutually safe; the losing rename throws.

```csharp
// ❌ Flagged (fires at the File.Move) — deterministic temp + overwrite-rename, no concurrency guard
var tempPath = filePath + ".tmp";          // deterministic concat ending in ".tmp"
File.WriteAllText(tempPath, json);
File.Move(tempPath, filePath, overwrite: true);

// ✅ Option 1 (preferred): the sanctioned helper — unique temp + per-path lock + bounded retry
NotNot.Storage.AtomicFileWriter.WriteAtomic(filePath, json);
NotNot.Storage.AtomicFileWriter.WriteAtomic(filePath, lines);          // IEnumerable<string> overload (mirrors File.WriteAllLines)
await NotNot.Storage.AtomicFileWriter.WriteAtomicAsync(filePath, json);

// ✅ Option 2: if AtomicFileWriter is unavailable — UNIQUE temp AND serialize the write+rename per final path
var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp"; // unique → silent
//   ... PLUS a per-final-path lock around the WriteAllText + Move pair (unique name alone is insufficient)

// ✅ Allowed (silent): no overwrite, or a direct final write
File.Move(a, b);                            // not the temp-then-rename idiom
File.WriteAllText(finalPath, json);         // no temp + Move — different concern
```

**Flagged**: `File.Move(temp, final, overwrite: true)` (the receiver resolves to `System.IO.File`; the third argument is the literal `true`, named `overwrite:` or positional) where `temp` resolves — to its single-method local declaration **or** inline — to a **deterministic** `+ ".tmp"` concat with **no** `Guid`/`Random`/`Ticks`/`GetRandomFileName`/`GetTempFileName` identifier anywhere in the concat tree.

**Allowed**: `NotNot.Storage.AtomicFileWriter.WriteAtomic`/`WriteAtomicAsync` (its temp is minted via a method call, never a literal-bearing deterministic concat — not matched); a Guid/Random/Ticks-bearing **unique** temp (not deterministic); a plain `File.Move(a, b)` with no `overwrite: true`; a direct `File.WriteAllText(finalPath, ...)` with no temp + Move; a `temp` sourced from a method call (e.g. `MakeTempPath(x)`).

**Preferred fix** (in order):
1. Use `NotNot.Storage.AtomicFileWriter.WriteAtomic`/`WriteAtomicAsync` (unique temp + per-path lock + bounded retry). A `WriteAtomic(string finalPath, IEnumerable<string> lines, Encoding? = null)` overload mirrors `File.WriteAllLines`.
2. If `AtomicFileWriter` is unavailable here, use a unique temp (`finalPath + Guid + ".tmp"`) **and** serialize the write+rename per final path — unique-name alone is insufficient.
3. `#pragma warning disable NN_R007` / `.editorconfig` severity override only if the write is provably single-threaded **and** single-process for the file's lifetime.

Complementary to NN_R001/NN_R002 (Task-concurrency) and NN_R005/NN_R006 (catch-reliability) on a disjoint axis — NN_R007 is the file-I/O concurrency-reliability rule.

<a id="NN_C004"></a>
### NN_C004: No code-side default for AppSettings options
**Severity:** Error
**Category:** CodeStyle

Forbids a code-side default on a `NotNot.AppSettings`-generated settings option — i.e. `{settingsOption} ?? {literal/const}`. The generator makes `appsettings*.json` the **single source of truth** for settings defaults (every generated property is nullable `T?`). A code-side `?? <default>` is a *second, drifting copy* of a default that already lives in the JSON, and it **silently masks a missing/null config value** that should fail loud at startup rather than limp on a hardcoded fallback. Keep one default location (the JSON) and prefer fail-loud over fail-quiet.

```csharp
// ❌ Flagged (fires at the ?? operator) — settings.* is a [GeneratedCode("NotNot.AppSettings")] option
var mruCapacity   = (int?)settings.ProjectionMruCapacity ?? 4;          // numeric literal
var idleTtl       = (double?)settings.ProjectionIdleTtlMinutes ?? 10.0; // double literal
var maxOpen       = (int?)settings.Sessions?.MaxOpenPty ?? Options.DefaultMaxOpenPty; // const (or constant-init static-readonly) field
var name          = settings.Name ?? "vs-running";                     // non-empty string literal
settings.ProjectionMruCapacity ??= 4;                                  // ??= coalesce-assignment also writes a default

// ✅ Allowed
var required      = (int?)settings.ProjectionMruCapacity ?? throw new InvalidOperationException("required"); // fail loud
var computed      = (int?)settings.ProjectionMruCapacity ?? Environment.ProcessorCount; // computed — can't live in JSON
var computedField = (int?)settings.ProjectionMruCapacity ?? Options.ComputedDefault;    // static-readonly w/ computed initializer — out of scope
var fromFactory   = (int?)settings.ProjectionMruCapacity ?? Compute();  // method call — out of scope
var normalized    = settings.Name ?? "";                               // empty/whitespace string — null-normalization
var normalized2   = settings.Name ?? string.Empty;                     // string.Empty — null-normalization
var local         = someLocal ?? 4;                                    // not a settings option
```

**Flagged**: `{member} ?? {default}` **or** `{member} ??= {default}` where the member's containing type carries `[System.CodeDom.Compiler.GeneratedCode("NotNot.AppSettings", ...)]` AND the right operand is a default-VALUE shape (a NON-EMPTY string literal, a numeric/bool/char literal — optionally a leading unary `-`/`+` on a numeric literal — a `const` field, or a `static readonly` field whose initializer is itself a compile-time constant). Casts, parentheses, and conditional-access on the left operand are stripped before resolving the member.

**Allowed**: `?? throw` (the permitted required-no-default pattern); a non-settings left operand; a computed right operand (a method call, a property read such as `Environment.ProcessorCount`, or a `static readonly` field with a computed initializer — none can live in static JSON); an empty/whitespace-only string literal (`?? ""`, `?? "   "`) or `System.String.Empty` (`?? string.Empty`) — null-normalization, not a config default; `?? null` / `?? default`; and coalesce expressions inside generated `*.g.cs` files.

**Preferred fix** (in order):
1. Set the default in `appsettings*.json` and read the typed property directly (remove the `?? default`).
2. If REQUIRED with no sensible default, fail loud with `?? throw`.
3. If genuinely optional, branch on null as a real state.
4. `#pragma warning disable NN_C004` / `.editorconfig` severity override only when a code-side default is genuinely intended.

Complementary to NN_C003 (BoolDefaultFalse) on a disjoint axis: C003 = bool param default-false at *declaration*; C004 = no code-side default substitution at *consumption*. They never co-fire.

<a id="nn_c005"></a>
### NN_C005: No ServerOnly AppSettings read from client-reachable code
**Severity:** Error
**Category:** CodeStyle

Forbids reading a `NotNot.AppSettings`-generated **ServerOnly** key from a `.Shared`/`.Client` (WASM-client-reachable) compilation. The generator prunes ServerOnly keys from the generated client snapshot (`_ClientAppSettings`), so the read is `null` on the client at runtime — and with `.Require()` **throws at bootstrap** (the NN_C004 reachability failure class). NN_C005 is the read-reachability companion to NN_C004's write-default rule.

`.Shared` is a Razor Class Library that runs in **both** the server and the WASM client; `.Client` is client-only. Whitelisting is a **decision, not a reflex** — never whitelist a server-only secret just to silence the diagnostic, because that ships it to the client.

```csharp
// Generated by NotNot.AppSettings (appsettings*.json + NotNotAppSettings:whitelist):
//   ClientKey → ClientRead (present in _ClientAppSettings)
//   ServerKey → ServerOnly (pruned from _ClientAppSettings)
//   var settings = app.Configuration.AppSettingsGen()...;   // the FULL AppSettings tree

// ❌ Flagged (fires at the read) — inside a .Shared / .Client assembly:
var token = settings.{|ServerKey|};                 // ServerOnly → null on the WASM client at runtime

// ✅ Allowed:
var name  = settings.ClientKey;                      // client-whitelisted (present in _ClientAppSettings)
var key   = nameof(settings.ServerKey);              // no runtime dereference
// ... the same `settings.ServerKey` read inside a .Server assembly — ServerOnly reads are legal server-side
```

**Flagged** (at the accessed-member identifier): `{settings}.{Key}` (or `{settings}?.{Key}`) where the containing type is a `NotNot.AppSettings`-generated FULL-tree type (`[GeneratedCode("NotNot.AppSettings", ...)]`, in the `.AppSettingsGen` `AppSettings` tree — not the `_ClientAppSettings` mirror) AND the corresponding path is absent from the generated `_ClientAppSettings` mirror (⇒ ServerOnly) AND the current assembly name ends with `.Shared` or `.Client`.

**Allowed**: a read of a client-whitelisted key (present in `_ClientAppSettings`); the same read from a `.Server` assembly; a read of a non-`[GeneratedCode("NotNot.AppSettings")]` member; a read of the `_ClientAppSettings` mirror itself; `nameof(...)` (no runtime dereference); member access inside generated `*.g.cs` files; and compilations whose `_ClientAppSettings` mirror is not present (conservative — cannot prove ServerOnly).

**Preferred fix** (the fix is a *decision*, not a reflex):
1. If the client genuinely needs the value, whitelist it as `ClientRead` (or `ClientWriteLocal`/`ClientWriteServer`) under `NotNotAppSettings:whitelist` in `appsettings*.json`.
2. If it is server-only/secret, move the read to a `.Server`-side type. **Do NOT whitelist server-only/secret config** — that exposes it to the client.
3. `#pragma warning disable NN_C005` / `.editorconfig` severity override only if the code path provably never runs on the client.

Complementary to NN_C004 (AppSettingsCodeDefault) on a disjoint axis: C004 = write-side `?? default` ban at *consumption*; C005 = read-side ServerOnly-reachability ban. They never co-fire.

## ⚙️ Configuration

Configure rules using `.editorconfig`:

```ini
[*.cs]
# Configure rule severity
dotnet_diagnostic.NN_R001.severity = error
dotnet_diagnostic.NN_R002.severity = error
dotnet_diagnostic.NN_C001.severity = error
dotnet_diagnostic.NN_C002.severity = error
dotnet_diagnostic.NOTNOT001.severity = error
dotnet_diagnostic.NN_R005.severity = error
dotnet_diagnostic.NN_R006.severity = error
dotnet_diagnostic.NN_R007.severity = error
dotnet_diagnostic.NN_C004.severity = error
dotnet_diagnostic.NN_C005.severity = error

# Disable specific rules
dotnet_diagnostic.NN_R003.severity = none

# Disable for generated code
[*.Generated.cs]
dotnet_diagnostic.NN_C001.severity = none

# Category-based configuration
dotnet_analyzer_diagnostic.category-reliability.severity = error
dotnet_analyzer_diagnostic.category-performance.severity = warning
```

### Advanced Configuration

```ini
# Performance settings
notNot_analyzers_enable_concurrent_execution = true
notNot_analyzers_skip_generated_code = true

# Context-aware features
notNot_analyzers_ui_context_detection = true
notNot_analyzers_library_code_detection = true
```

## 🎯 Smart Suppressions

The analyzers include intelligent suppression for common scenarios:

- **Test Methods** - Automatic suppression of NN_R001 in test methods where fire-and-forget is often intentional
- **Event Handlers** - Reduced warnings for UI event handlers where blocking patterns are common
- **Cleanup Code** - Suppression of NN_R002 in `Dispose` and cleanup methods

## 🛠️ IDE Integration

### Visual Studio
- Automatic code fixes appear in the Quick Actions menu (Ctrl+.)
- Bulk fix available for entire projects
- Integration with Error List and Solution Explorer

### VS Code
- Works with C# extension
- Fixes available through Code Actions
- Integrated with Problems panel

### JetBrains Rider
- Full IntelliSense integration
- Context Actions for automatic fixes
- Inspection results in Problems view

## 📊 Performance Monitoring

Built-in performance tracking helps identify analyzer overhead:

```csharp
// Access performance metrics (in debug builds)
var metrics = AnalyzerPerformanceTracker.GetAllMetrics();
foreach (var metric in metrics)
{
    Console.WriteLine($"{metric.AnalyzerId}: {metric.AverageTime.TotalMilliseconds:F2}ms avg");
}
```

## 🔧 Troubleshooting

### Common Issues

**1. Analyzer not running**
- Ensure `PrivateAssets="all"` is set in PackageReference
- Restart IDE after installation
- Check that analysis is enabled in IDE settings

**2. Too many warnings**
- Configure severity levels in `.editorconfig`
- Use bulk suppression for legacy code
- Consider gradual adoption with `severity = suggestion`

**3. Performance issues**
- Enable concurrent execution: `notNot_analyzers_enable_concurrent_execution = true`
- Skip generated code: `notNot_analyzers_skip_generated_code = true`
- Monitor performance with built-in tracking

### Debugging

Enable verbose logging in `.editorconfig`:
```ini
notNot_analyzers_verbose_logging = true
```

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

### Development Setup
1. Clone the repository
2. Open in Visual Studio 2022 or later
3. Build the solution
4. Run tests with `dotnet test`

### Creating Custom Rules
```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MyCustomAnalyzer : DiagnosticAnalyzer
{
    // Implement your custom logic
}
```

## 📄 License

This project is licensed under the [Mozilla Public License 2.0](LICENSE.md).

## 🔗 Links

- [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers)
- [Issue Tracker](https://github.com/NotNotTech/NotNot-MonoRepo/issues)
- [Release Notes](CHANGELOG.md)
- [NuGet Package](https://www.nuget.org/packages/NotNot.Analyzers/)

## 🏆 Why NotNot.Analyzers?

- **Battle-tested** - Used in production applications
- **Modern** - Built with latest .NET analyzer APIs
- **Intelligent** - Context-aware analysis reduces false positives
- **Fast** - Optimized for large codebases
- **Configurable** - Adapt to your team's coding standards
- **Educational** - Detailed error messages help developers learn best practices

---

**Made with ❤️ by the NotNot team**



## Direct ProjectReference Usage

If referencing this project directly (not the nuget package) be sure to add ` OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the `.csproj` reference, like:

```xml
<ProjectReference Include="..\lib\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Diagnostic Anchor Reservations

The following anchor IDs are reserved for the DI marker-enforcement analyzer rule family
(`NotNot.Analyzers/Architecture/DI/DiMarkerEnforcementAnalyzer.cs`). Per-rule documentation
pages with these anchors are pending — referenced by `HelpLinkUri` on each diagnostic descriptor.

<a id="nn_di_001"></a>
### NN_DI_001 — DI lifetime mismatch with marker interface

Fires on `Add{L}<T>()` / `TryAdd{L}<T>()` where `T` implements `IDi{L'}Service` with `L != L'`.
Severity: **Error**. Documentation page pending.

<a id="nn_di_002"></a>
### NN_DI_002 — Redundant explicit DI registration

Fires on `Add{L}<T>()` where `T` already implements `IDi{L}Service` (matched lifetime — auto-registration
would handle the same wiring). Severity: **Warning**. Documentation page pending.

<a id="nn_di_003"></a>
### NN_DI_003 — Passthrough DI factory replaceable with marker interface

Fires on `Add{L}<T>(sp => new T(sp.GetRequiredService<...>(), ...))` when `T` is project-defined and
does not already implement a marker interface. Severity: **Info**. Documentation page pending.

<a id="nn_di_004"></a>
### NN_DI_004 — DI service marker conflicts with IHostedService

Fires on a class symbol implementing both a marker interface and
`Microsoft.Extensions.Hosting.IHostedService`. Severity: **Error**. Documentation page pending.

<a id="nn_di_005"></a>
### NN_DI_005 — Missing IDi{L}Service marker — class is auto-registration candidate

Fires on `Add{L}<T>()` / `Add{L}<TService, TImpl>()` where the implementation type is project-internal,
non-abstract, lacks any `IDi{L}Service` marker, and isn't covered by an existing carve-out
(`TryAdd*`, third-party, interface-bridge factory, `[AutoDiBypass]`). Severity: **Info**.
Documentation page pending.

<a id="nn_di_006"></a>
### NN_DI_006 — Hosted service has a required delegate constructor parameter DI cannot provide
**Severity:** Error
**Category:** Reliability

Fires on a concrete `IHostedService` (directly, or via `BackgroundService`/a base) with EXACTLY one
public instance constructor that has a **required** parameter of delegate type (`Func<>`/`Action<>`/a
custom delegate). The NotNot Scrutor convention
(`AddClasses(AssignableTo<IHostedService>()).AsSelfWithInterfaces()`) auto-registers every hosted
service with constructor injection; delegate types are never DI-registered, so a required delegate
parameter makes the `AsSelf` concrete registration **unconstructible** — the host crashes at
`builder.Build()` (Dev `ValidateOnBuild`) or `Host.StartAsync` (Prod) **before any port binds**. Marker-
independent: NN_DI_006 fires whether or not the type carries an `IDi{L}Service` marker (the proven crash
type carries none). It is the gap-filling third rule of the hosted-service matrix (NN_DI_004 =
marker+IHostedService conflict; NN_DI_005 = marker-absence; NN_DI_006 = ctor-unconstructibility).

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

// ❌ Flagged (fires at the class declaration) — required delegate ctor param, unconstructible under auto-registration
public class CrashObserver : BackgroundService
{
    public CrashObserver(Func<string, CancellationToken, Task> purge) { /* ... */ }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}

// ✅ Option 1 (preferred): inject a DI-registered abstraction instead of a raw delegate
public interface ISessionReapPurger { Task PurgeAsync(string id, CancellationToken ct); }

public class ReapObserver : BackgroundService
{
    public ReapObserver(ISessionReapPurger purger) { /* ... */ }   // DI resolves the registered interface
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}

// ✅ Option 2 (only if a null delegate is a SAFE no-op): make the parameter optional with a default
public class OptionalObserver : BackgroundService
{
    public OptionalObserver(Func<string, CancellationToken, Task>? purge = null) { /* DI passes null */ }
    protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}

// ✅ Allowed (silent): a plain class (not IHostedService) — never reaches the auto-registration scan
public class ManualFactoryConsumer
{
    public ManualFactoryConsumer(Func<int> f) { /* constructed by a hand-written factory */ }
}
```

**Flagged**: a concrete (non-abstract) class implementing `Microsoft.Extensions.Hosting.IHostedService`
with EXACTLY one public instance constructor whose parameter list contains a parameter that has no
explicit default value AND whose type is `TypeKind.Delegate`.

**Allowed**: an optional-default delegate parameter (`Func<...>? f = null` — DI passes null); a required
non-delegate parameter (a registered interface abstraction); a non-`IHostedService` class with a
delegate ctor param (manual-factory pattern); an abstract `IHostedService` (not auto-registered as
concrete); a type with zero or more-than-one public instance constructors (ambiguous — MS.DI
greedy-resolution / `[ActivatorUtilitiesConstructor]` undecidable, conservative skip); and a type/assembly
carrying `[AutoDiBypass]`.

**Preferred fix** (the fix is a *decision*, not a reflex):
1. Replace the delegate with a DI-registered abstraction — define a small interface implemented by the
   providing service (e.g. `ISessionReapPurger` implemented by `VowSessionService`) and inject that.
2. ONLY if a null delegate is a SAFE no-op for the auto-registered instance, make the parameter optional
   with a default (`Func<...>? f = null`). NOT if absence silently disables required behavior.
3. `[AutoDiBypass]` on the type, or `#pragma warning disable NN_DI_006` / `.editorconfig`
   `dotnet_diagnostic.NN_DI_006.severity = none`, ONLY if the type is provably never
   auto-registered / DI-constructed.

