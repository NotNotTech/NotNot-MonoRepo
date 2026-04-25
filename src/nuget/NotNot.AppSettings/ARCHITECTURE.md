# NotNot.AppSettings — Architecture

**Audience**: Developers wanting to understand HOW NotNot.AppSettings works internally — the source-generator pipeline, the shared-source pattern with `NotNot.Bcl.Core`, the MSBuild integration, and the rationale behind the design constraints.

**Companion docs**:
- [`ReadMe.md`](./ReadMe.md) — installation + usage + examples (start here if you just want to USE the package).
- [`MIGRATION.md`](./MIGRATION.md) — practical migration paths (single→multi-file, `LoadDirect*` → `SimpleStorageManager<T>`).
- [`AGENTS.md`](./AGENTS.md) — terse machine-readable reference (agent-oriented; less narrative).

---

## Two-Component Design

NotNot.AppSettings ships as **two cooperating assemblies**:

| Assembly | Target Framework | Role |
|---|---|---|
| `NotNot.AppSettings` | `netstandard2.0` | **Compile-time** Roslyn source generator. Reads `appsettings*.json` files at build time, merges them, and emits a strongly-typed `_AppSettings` class + binder helpers as C# source code. |
| `NotNot.Bcl.Core` | `net10.0` | **Runtime** library. Provides `JsonSettingsUtils` (`NotNot.AppSettingsHelper` namespace), `SimpleStorageManager<T>` + `IStorageAdapter` + `FileStorageAdapter` (`NotNot.Storage` namespace), and the `JsonMergeCore` merge semantics used by both build-time AND runtime code paths. |

The split is mandatory because:
1. **Roslyn analyzers MUST target `netstandard2.0`** — they are loaded by the Roslyn compiler host (which is `netstandard2.0`-bound for cross-SDK compatibility). The generator cannot use modern .NET APIs directly.
2. **Modern APIs (System.Text.Json features, IHostedService, etc.) need a real runtime target** — those live in `NotNot.Bcl.Core` (`net10.0`).
3. **Merge semantics MUST be identical at compile-time and runtime** — otherwise consumer JSON that parses at build-time could fail at runtime (or vice versa). This drives the **shared-source pattern** below.

---

## Source Generator Pipeline

The compile-time data flow:

```
                   MSBuild auto-glob          AdditionalTextsProvider
appsettings*.json ───────────────────► <AdditionalFiles> ───────────► AppSettingsGen
   (consumer's                          (.props at build/)             (Roslyn IIncrementalGenerator
    project root)                                                       via SourceGenerator.Foundations)
                                                                                │
                                                                                ▼
                                                                       JsonMerger.MergeJsonFiles
                                                                       (sort by ordinal path,
                                                                        fold via JsonMergeCore.MergeAll)
                                                                                │
                                                                                ▼
                                                                       AppSettingsGen.GenerateSourceFiles
                                                                       (walk JSON tree → emit
                                                                        partial classes + interfaces)
                                                                                │
                                                                                ▼
                                                                       Emitted .g.cs files
                                                                       (added to consumer's compilation)
```

### Pipeline Stages

1. **MSBuild auto-glob** ([`build/NotNot.AppSettings.props`](./build/NotNot.AppSettings.props), [`build/NotNot.AppSettings.targets`](./build/NotNot.AppSettings.targets))
   - The `.targets` file injects `<AdditionalFiles Include="appsettings*.json" />` into consumer projects when `NotNot_AppSettings_AutoGlob = true` (the default).
   - Consumers can opt out by setting `<NotNot_AppSettings_AutoGlob>false</NotNot_AppSettings_AutoGlob>` and declaring `<AdditionalFiles>` manually.
   - The auto-glob is package-level only — when consuming via raw `<ProjectReference OutputItemType="Analyzer">`, you must explicitly `<Import>` the `.props` and `.targets` files (see ReadMe.md "Consuming as ProjectReference Analyzer" section).

2. **`AdditionalTextsProvider`** ([`AppSettingsGen.cs:67`](./AppSettingsGen.cs))
   - Roslyn provides each `<AdditionalFiles>` entry as a `Microsoft.CodeAnalysis.Text.SourceText` instance.
   - The generator gathers these into a `Dictionary<string, SourceText>` keyed by file path.

3. **`JsonMerger.MergeJsonFiles`** ([`JsonMerger.cs`](./JsonMerger.cs))
   - **Sort**: Files are sorted by ordinal path key ascending — deterministic across OS/filesystem ordering quirks.
   - **Parse**: Each `SourceText` is parsed into a `JsonNode` using the canonical `JsonMergeCore.DefaultDocumentOptions` (allows trailing commas + comments).
   - **Fold**: All nodes are folded left-to-right via `JsonMergeCore.MergeAll` — last path wins for overlapping keys.
   - **Round-trip**: The merged `JsonObject` is serialized back to a flat `Dictionary<string, JsonElement>` to preserve the public method signature consumed by `GenerateFilesWorker`.

4. **`AppSettingsGen.GenerateSourceFiles`** ([`AppSettingsGen.cs:148`](./AppSettingsGen.cs))
   - Walks the merged JSON tree.
   - For each nested object: emits a partial class with backing fields + change-detection setters.
   - For each scalar: maps to a C# property of inferred type (see Type Inference table below).
   - Calls `AddBinderShims` to emit the static `AppSettingsBinder` + four `LoadDirect*` overloads.

5. **Emitted `.g.cs` files** appear in `obj/Generated/` of the consumer project and are compiled into the consumer's assembly. The strongly-typed `_AppSettings` is then available under `{RootNamespace}.AppSettingsGen`.

---

## Shared-Source Pattern (`JsonMergeCore`)

[`JsonMergeCore.cs`](./JsonMergeCore.cs) is the **single source of truth** for JSON merge semantics. It is compiled into BOTH assemblies via shared-source linking:

- **Native compilation** in `NotNot.AppSettings` (the generator).
- **Linked compilation** in `NotNot.Bcl.Core` via:
  ```xml
  <Compile Include="..\NotNot.AppSettings\JsonMergeCore.cs"
           Link="NotNot\AppSettingsHelper\JsonMergeCore.cs" />
  ```
  in [`NotNot.Bcl.Core.csproj`](../NotNot.Bcl.Core/NotNot.Bcl.Core.csproj).

### Why shared-source instead of a runtime dependency?

The generator targets `netstandard2.0` and the runtime targets `net10.0`. A cross-target dependency would invert the architecture (generator depending on runtime). Shared-source compilation lets the same code live in both worlds without a dependency edge.

### Dual-compilation caveat (CS0433)

Because `JsonMergeCore` is compiled into TWO assemblies under the same fully-qualified type name (`NotNot.AppSettingsInternal.JsonMergeCore`), any test project or downstream consumer that references BOTH `NotNot.AppSettings` AND `NotNot.Bcl.Core` will hit `CS0433: type exists in both`. The canonical mitigation is MSBuild `<ProjectReference Aliases="...">` + `extern alias` — see [`../NotNot.AppSettings.Tests/NotNot.AppSettings.Tests.csproj`](../NotNot.AppSettings.Tests/NotNot.AppSettings.Tests.csproj) for the working pattern.

For typical consumers using only the NuGet package, this is invisible — they only see one copy via the runtime path.

---

## Build-Time vs Runtime Parity

A core design invariant: **JSON parsed at build-time and JSON parsed at runtime MUST produce identical results.** Otherwise consumer settings could compile but fail to load (or vice versa).

This is enforced by routing both parse sites through `JsonMergeCore.DefaultDocumentOptions`:

```csharp
public static readonly JsonDocumentOptions DefaultDocumentOptions = new()
{
    AllowTrailingCommas = true,
    CommentHandling = JsonCommentHandling.Skip,
    MaxDepth = 64,
};
```

| Parse site | Path | Uses `DefaultDocumentOptions`? |
|---|---|---|
| Generator (`JsonMerger.MergeJsonFiles`) | [`JsonMerger.cs`](./JsonMerger.cs) | YES (via `_options` proxy property) |
| Runtime (`JsonSettingsUtils.MergeStreamsAsync`) | [`../NotNot.Bcl.Core/NotNot/AppSettingsHelper/JsonSettingsUtils.cs`](../NotNot.Bcl.Core/NotNot/AppSettingsHelper/JsonSettingsUtils.cs) | YES (passed explicitly to `JsonNode.ParseAsync`) |

Tests `DefaultDocumentOptions_AllowsTrailingCommasAndComments` and `MergeStreamsAsync_StreamWithTrailingCommas_ParsesSuccessfully` lock this contract at both layers.

### Merge semantics (RFC-7396-ish)

| JSON situation | Merge behavior |
|---|---|
| Two objects with disjoint keys | Union (both sides preserved) |
| Two objects with overlapping non-object values | Later path wins |
| Two objects with overlapping object values | Recursive deep-merge |
| Two objects with overlapping array values | **Later array REPLACES earlier** (NOT concatenated) |
| Later file has `"key": null` for a key in earlier file | Key is **DELETED** from output |
| Non-object root in source | `MergeAll` throws `JsonException` (fail-fast) |
| Non-object root in diff | `Merge` REPLACES target entirely (RFC-7396 compliant) |

---

## Generated Code Surface

For a project with `<RootNamespace>MyApp</RootNamespace>` and a single `appsettings.json`:

| Generated file | Contents |
|---|---|
| `MyApp.AppSettingsGen.AppSettings.g.cs` | Root settings class implementing `IAppSettings` + `ISettingsChangeAware` |
| `MyApp.AppSettingsGen.Interfaces.IAppSettings.g.cs` | Root settings interface (used for DispatchProxy mode) |
| `MyApp.AppSettingsGen._{NestedClass}.{Property}.g.cs` | Each nested object → its own partial class file |
| `MyApp.AppSettingsGen.Interfaces.I{Property}.g.cs` | Interface for each nested object |
| `_BinderShims.g.cs` | `AppSettingsBinder` + `IAppSettingsBinder` + four `[Obsolete]` `LoadDirect*` facades + emitted `_FlattenJson` + `_BindMergedNode` helpers |

### Namespace conventions

- Settings classes: `{RootNamespace}.AppSettingsGen`
- Settings interfaces: `{RootNamespace}.AppSettingsGen.Interfaces`
- `AppSettingsBinder`: `{RootNamespace}.AppSettingsGen`

### Visibility

- Default: `internal` (avoids namespace collisions across projects sharing root namespaces).
- Opt into `public`: `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` in consumer csproj.

---

## Type Inference Rules

| JSON value | Inferred C# type | Notes |
|---|---|---|
| `"string"` | `string` | |
| `123` (or `1.5`) | `double` | **All JSON numbers map to `double`** regardless of integer/float — JSON has no integer type, `double` is the natural C# representation. Cast to `int`/`long` if needed. |
| `true` / `false` | `bool` | |
| `null` | `object` | Type is ambiguous — falls back to `object`. |
| `{}` | Generated nested class | Recursive; one `.g.cs` per nested object. |
| `[]` (with elements) | Array of inferred element type | Element type derived from first element (or `object` if mixed). |
| `[]` (empty array) | `object[]` | No element to infer from — falls back to `object` (`unifiedChildType ??= "object"` at [`AppSettingsGen.cs:806`](./AppSettingsGen.cs)). |

### Ambiguity → `object`

If a property is `null` in one file and a typed value in another, the inferred type is `object` (cannot reconcile). Same for arrays of mixed types or empty arrays.

---

## MSBuild Integration

### `.props` file ([`build/NotNot.AppSettings.props`](./build/NotNot.AppSettings.props))

Auto-imported by NuGet on package install. Sets `<NotNot_AppSettings_AutoGlob>true</NotNot_AppSettings_AutoGlob>` if not already set.

### `.targets` file ([`build/NotNot.AppSettings.targets`](./build/NotNot.AppSettings.targets))

Injects `<AdditionalFiles Include="appsettings*.json" />` when auto-glob is enabled. Also makes `NotNot_AppSettings_GenPublic` and `NotNot_AppSettings_AutoGlob` visible to the source generator via `<CompilerVisibleProperty>`.

### Package layout

The published `NotNot.AppSettings.nupkg` contains:
- `analyzers/dotnet/cs/NotNot.AppSettings.dll` — the generator assembly (loaded by Roslyn as an analyzer).
- `build/NotNot.AppSettings.props` + `build/NotNot.AppSettings.targets` — auto-imported MSBuild integration.
- A `.nuspec` with a `<dependencies>` entry for `NotNot.Bcl.Core` in the `net10.0` group (see "Post-Pack Mechanism" below).

### Post-Pack `.nuspec` patching

The emitted `LoadDirect*` methods call `NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync` from `NotNot.Bcl.Core`. Consumers who install only `NotNot.AppSettings` need `NotNot.Bcl.Core` resolved transitively for that emitted code to compile.

A standard `<PackageReference Include="NotNot.Bcl.Core">` in the generator csproj caused dev-graph conflicts (the package would flow into the test project's compile graph at the same time as the existing transitive `ProjectReference` path, producing CS0234 "type exists in both" errors).

The solution: a custom MSBuild target ([`NotNot.AppSettings.csproj:49`](./NotNot.AppSettings.csproj) `Target Name="InjectBclCoreRuntimeDependency"`) runs after `Pack` and invokes the `PatchNupkgWithBclCoreDep` `UsingTask` (defined inline at [`NotNot.AppSettings.csproj:78`](./NotNot.AppSettings.csproj) via `RoslynCodeTaskFactory`). The task patches the emitted `.nupkg`'s embedded `.nuspec` to add the dependency, without affecting the dev-time graph. Defense-in-depth:

1. **Inline-task guard sites** fail the build with `Log.LogError` if the `.nupkg` or `.nuspec` is missing.
2. **MSBuild `<Error>` gate** fails the build if no `.nupkg` candidate is found at expected paths.
3. **Post-write re-verification** re-opens the patched `.nupkg`, re-parses the `.nuspec`, and asserts the dep is present.
4. **Artifact-level test** ([`../NotNot.AppSettings.Tests/NupkgVerificationTests.cs`](../NotNot.AppSettings.Tests/NupkgVerificationTests.cs)) opens the emitted `.nupkg` and verifies the dep node from a unit test, runnable in CI.

---

## Why `netstandard2.0`?

- Roslyn analyzers/source generators are loaded by the Roslyn compiler host process.
- The compiler host targets `netstandard2.0` for cross-SDK compatibility.
- Therefore generators MUST target `netstandard2.0` — no exceptions.

This means the generator cannot use:
- `System.IO.File` (RS1035 analyzer restriction — analyzers must be deterministic + sandboxed).
- Modern System.Text.Json features that require `net6.0+` (the generator uses the `netstandard2.0`-compatible subset).
- Nullable annotations requiring `net6.0+` runtime annotations.
- C# 9+ records, init-only properties, `System.Range` outside indexers.

Compatible APIs that ARE used:
- `System.Text.Json.Nodes` (available in `System.Text.Json` package, `netstandard2.0`-compat).
- `Microsoft.CodeAnalysis.Text.SourceText` (Roslyn API).
- `Microsoft.CodeAnalysis.AdditionalText` (Roslyn API).

---

## Testing Strategy

The test project is at [`../NotNot.AppSettings.Tests/`](../NotNot.AppSettings.Tests/). It uses xUnit + FluentAssertions and references the generator csproj with `Aliases="appsettings_gen"` to disambiguate the dual-compiled `JsonMergeCore` symbol.

| Test file | Layer tested |
|---|---|
| `JsonMergeCoreTests.cs` | The merge core itself — RFC-7396 semantics at the lowest layer. |
| `MultiFileGeneratorTests.cs` | `JsonMerger.MergeJsonFiles` — the generator's merge surface (ordinal sort + round-trip). |
| `JsonSettingsUtilsTests.cs` | The runtime utility — diff/merge/serialization. |
| `LoadDirectFacadeTests.cs` | Emission-shape (`[Obsolete]` attribute presence + body routing) + facade merge behavior at runtime. |
| `NupkgVerificationTests.cs` | The emitted `.nupkg` artifact contains the `NotNot.Bcl.Core` dependency entry. |
| `ClientWhitelistGeneratorTests.cs` | `_ClientAppSettings` whitelist marker attribute behavior (Blazor scenario). |

`SimpleStorageManager<T>` lifecycle tests live in `NotNot.Bcl.Core.Tests` (separate test assembly, in the runtime package).

### What's NOT unit-tested (boundary documentation)

- **MSBuild `<AdditionalFiles>` glob expansion** — happens at MSBuild layer, not in the generator. Skip-documented in `MultiFileGeneratorTests.cs::Wildcard_ViaPropsGlob_IncludesMultipleFiles`.
- **`SGF` Logger pipeline** — the SourceGenerator.Foundations logger sink is internal to the generator harness and not reachable from a unit test surface. Skip-documented in `MultiFileGeneratorTests.cs::Logging_LogsFilenames`.

Both are verified by code-inspection at the cited file:line locations and by manual integration in the example app.

---

## Design Decisions Log

| Decision | Rationale |
|---|---|
| All JSON numbers → `double` | JSON has no integer/float distinction. `double` is the broadest-precision numeric C# type that round-trips without loss for typical config values. Consumers cast explicitly when needed. |
| Generated properties use backing fields (not auto-properties) | Required for `ISettingsChangeAware` change detection — auto-properties cannot intercept the setter. |
| Generated classes default to `internal` | Prevents namespace collisions when multiple projects share a root namespace. Opt-in via `NotNot_AppSettings_GenPublic`. |
| Interfaces always generated | Enables a `SimpleStorageManager<IAppSettings>` workflow with a `DispatchProxy`-based change interceptor (interception works only on interface members). |
| Arrays REPLACE on merge (not CONCAT) | Matches `JsonSettingsUtils.MergeJson` runtime contract + RFC-7396 semantics. Consumers needing append-style behavior can do it imperatively at runtime. |
| Ordinal path sort for multi-file merge | Deterministic across OS/filesystem — `appsettings.Production.json` always wins over `appsettings.Development.json` regardless of disk order. |
| `LoadDirect*` preserved as `[Obsolete]` facades | Backward compatibility — existing consumers don't break. The `[Obsolete]` attribute steers new code toward `NotNot.Storage.SimpleStorageManager<T>` for runtime persistence scenarios. |
| Non-object root throws (in `MergeAll`) | Fail-fast unification per defensive-coding principle. Silent-drop would mask config errors at consumers. |
| `JsonMergeCore` is shared-source, not a separate package | Avoids inverting the dependency direction (generator depending on runtime). One source, two compilations. |

---

## File Map (Quick Reference)

| File | Role |
|---|---|
| [`AppSettingsGen.cs`](./AppSettingsGen.cs) | Main generator (~900 lines): pipeline registration, JSON tree walk, source emission, `[Obsolete]` facade emission. |
| [`AppSettingsGenConfig.cs`](./AppSettingsGenConfig.cs) | Config DTO passed through the Roslyn pipeline. |
| [`JsonMerger.cs`](./JsonMerger.cs) | Generator-side merge: ordinal-sort + fold via `JsonMergeCore`. |
| [`JsonMergeCore.cs`](./JsonMergeCore.cs) | **Shared-source** merge core: `Merge`, `MergeAll`, `ComputeDiff`, `DefaultDocumentOptions`. |
| [`ClientSettingsAttributes.cs`](./ClientSettingsAttributes.cs) | Marker attributes for `_ClientAppSettings` whitelist (Blazor scenario). |
| [`zz_Extensions.cs`](./zz_Extensions.cs) | Misc extension methods used by the generator. |
| [`build/NotNot.AppSettings.props`](./build/NotNot.AppSettings.props) | Sets `NotNot_AppSettings_AutoGlob = true` default. |
| [`build/NotNot.AppSettings.targets`](./build/NotNot.AppSettings.targets) | Injects `<AdditionalFiles Include="appsettings*.json">` when auto-glob is on. |
| [`NotNot.AppSettings.csproj`](./NotNot.AppSettings.csproj) | Package definition + analyzer packaging + post-Pack `.nuspec` patcher. |

---

## Further Reading

- [`ReadMe.md`](./ReadMe.md) — User-facing installation, examples, troubleshooting.
- [`MIGRATION.md`](./MIGRATION.md) — How to migrate from older APIs (`LoadDirect*` → `SimpleStorageManager<T>`, single→multi-file, common errors).
- [`AGENTS.md`](./AGENTS.md) — Terse machine-readable reference for AI agents.
- [`../NotNot.Bcl.Core/NotNot/AppSettingsHelper/AGENTS.md`](../NotNot.Bcl.Core/NotNot/AppSettingsHelper/AGENTS.md) — Runtime utilities (`JsonSettingsUtils`, `ISettingsChangeAware`).
- [`../NotNot.Bcl.Core/NotNot/Storage/`](../NotNot.Bcl.Core/NotNot/Storage/) — `SimpleStorageManager<T>` + `IStorageAdapter` + `FileStorageAdapter`.
- RFC 7396 (JSON Merge Patch) — https://datatracker.ietf.org/doc/html/rfc7396
