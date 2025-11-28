# VIBEBLOCK

No blocking issues currently identified.

# VIBEGUIDE

## Critical Design Decisions

### Source Generator Architecture
All generators inherit from `ModularGenerator_Base` which provides shared infrastructure for incremental source generation. Child generators implement `GeneratePartialClasses` with specific generation logic.

### Analyzer Safety Philosophy
Analyzers enforce runtime safety by preventing common Godot pitfalls at compile-time. Error-level diagnostics block builds for critical reliability issues like null reference exceptions during cold-reload.

### ResPath Convention
All Godot resource paths use `res://` format for portability. Source generators discover the project root via `project.godot` location and convert OS file paths to ResPath format.

### MSBuild Integration Pattern
NuGet package includes `.props` and `.targets` files to automatically configure consuming projects. The `.props` file adds Godot-specific files (.tscn, .gd, project.godot) as `AdditionalFiles` for source generator consumption. The `.targets` file marks the package as `PrivateAssets=All` to prevent transitive dependencies.

### netstandard2.0 Target
Source generators and analyzers target netstandard2.0 for compatibility with Roslyn compiler pipeline. Complex dependency scaffolding required for System.Text.Json usage in source generators.

### NotNotScene Pattern
Classes marked with `[NotNotSceneRoot]` attribute get generated partial classes with:
- Static `ResPath` property containing the .tscn file path
- Static `InstantiateTscn()` method for type-safe scene instantiation
- Automatic .tscn file discovery based on class name matching

### _ResPath Generated Class
Compile-time listing of all imported Godot assets. Provides `StringName` constants for type-safe resource loading. Only includes assets explicitly added to .csproj via `<AdditionalFiles>`.

## Code Fix Requirements (VS Light Bulb)

Use this checklist if Visual Studio shows the diagnostic but does not offer the code-fix (Ctrl+.):

1) Consume the analyzer correctly
- Project reference: add analyzer metadata so VS loads it as an analyzer (not a normal library)
  ```xml
  <ProjectReference Include="..\..\external-repo\NotNot-MonoRepo\src\nuget\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
  ```
- NuGet reference: ensure `NotNot.GodotNet.SourceGen.dll` and its deps are inside `analyzers/dotnet/cs/` in the package (this project already packs them).

2) Roslyn binary compatibility (important)
- VS ships specific Microsoft.CodeAnalysis binaries. If the analyzer references newer assemblies, MEF composition can fail silently and code fixes won’t load.
- Known-good versions for VS 2022 17.11.x:
  ```xml
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" PrivateAssets="all" />
  <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" />
  <PackageReference Include="System.Composition" Version="9.0.8" PrivateAssets="all" />
  ```
- Mismatched versions commonly result in diagnostics appearing but no code-fix suggestion.

3) MEF discovery of code fixes
- Code-fix types must be exported via MEF:
  - Add `[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(YourCodeFix))]` and `[Shared]` attributes.
  - Keep a reference to `System.Composition` in the analyzer project.

4) The analyzer assembly must load cleanly
- The analyzer project must compile without errors. A broken build prevents VS from loading the new assembly.
- If the project contains both generators and analyzers, avoid generator crashes (exceptions during initialization can block analyzer/code-fix load).

5) VS state and UX
- After changing analyzer assemblies or Roslyn package versions, rebuild the analyzer project and the consuming solution. Restart VS if needed.
- Place the caret on the diagnostic span (squiggle) and press Ctrl+.; some fixes only show when the caret is exactly on the reported span.

6) Quick troubleshooting
- Solution Explorer → Analyzers node: confirm `NotNot.GodotNet.SourceGen` is listed.
- Enable ActivityLog (`devenv /log`) and check for MEF load errors mentioning Microsoft.CodeAnalysis or System.Composition.
- Ensure the diagnostic ID matches a code fix provider’s `FixableDiagnosticIds`.

### GODOT003 specifics
- The analyzer reports for `protected override void Dispose(bool disposing)` in `GodotObject`-derived types when cleanup code isn’t exception-protected.
- Report location can be either the `if (disposing)` statement or the method body’s opening brace.
- The `GodotDisposeExceptionSafetyCodeFixProvider` offers:
  - “Wrap disposing block in try/catch” (when an `if (disposing)` block exists)
  - “Add disposing guard and wrap in try/catch” (when guard is missing)

## Known Limitations

### Source Generator Debugging
Uncomment `Debugger.Launch()` in `ModularGenerator_Base.Initialize` for debugging. Requires restarting Visual Studio or IDE to reload generator changes.

### Duplicate Scene Names
NotNotSceneLoader throws `InvalidOperationException` if multiple .tscn files share the same class name. Scene names must be unique across the project or path hints are needed.

### AdditionalFiles Configuration
Files must be explicitly included in .csproj to be accessible by source generators. JetBrains MCP tools operate within MSBuild project scope only.

# VIBECACHE

**LastCommitHash**: d7f4c662e7ee72edbb72bd4e2fe8c2a0f12b7568

**YYYYMMDD-HHmm**: 20251108-1035

## Primary Resources

- [Roslyn Source Generators Documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)
- [Creating a Source Generator](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md)
- [Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [Roslyn Analyzers Documentation](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview)
- [Godot C# Documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/index.html)
- [MSBuild NuGet Package Integration](https://learn.microsoft.com/en-us/nuget/create-packages/creating-a-package)

## Related Topics

- `NotNot.Bcl.Core` - Provides helper utilities used in generated code (`_GD`, `__`, extension methods)
- `NotNot` - Core NotNot framework that generated code depends on
- Godot Engine - Target platform for all source generators and analyzers
- Roslyn SDK - Underlying compiler platform for all source generation and analysis

## Child Topics

None. This is a leaf library project with no sub-modules.

## E2E Scenario Summary

**GODOT_SOURCEGEN_E2E_01_NullCheck**: Developer writes `_ExitTree` override accessing member field without null check → Analyzer detects unsafe access → Build fails with GODOT001 error → Developer adds null-conditional operator → Build succeeds

**GODOT_SOURCEGEN_E2E_02_ProhibitedApi**: Developer uses `MultiMesh.CustomAabb` property → Analyzer detects prohibited API → Build fails with GODOT002 error explaining corner positioning bug → Developer switches to `MultiMesh.GenerateAabb()` → Build succeeds

**GODOT_SOURCEGEN_E2E_03_SceneInstantiation**: Developer creates `MyScene.cs` class with `[NotNotSceneRoot]` attribute and matching `MyScene.tscn` file → Source generator discovers .tscn → Generates partial class with `ResPath` and `InstantiateTscn()` → Developer calls `MyScene.InstantiateTscn()` at runtime → Type-safe scene loaded

**GODOT_SOURCEGEN_E2E_04_ResPathAccess**: Developer needs to load texture asset at `res://assets/icon.png` → Source generator scans .import files → Generates `_ResPath.ASSETS_ICON_PNG` constant → Developer uses `_ResPath.ASSETS_ICON_PNG` for compile-time validated asset reference → Asset loads successfully

## Technical Architecture

### Component Overview

1. **Analyzers** ([Analyzers/](Analyzers/))
   - `GodotExitTreeNullCheckAnalyzer.cs` - GODOT001 null safety analyzer
   - `GodotExitTreeNullCheckCodeFixProvider.cs` - Auto-fix for GODOT001
   - `GodotProhibitedApiAnalyzer.cs` - GODOT002 banned API analyzer

2. **Source Generators** ([Generators/Modular/](Generators/Modular/))
   - `ModularGenerator_Base.cs` - Base class for all incremental generators
   - `NotNotSceneRoot_Generator.cs` - Scene instantiation helper generator
   - `_ResPath_Generator.cs` - Asset path constants generator

3. **Helpers** ([Helpers/](Helpers/))
   - `Func.cs` - Template evaluation helpers
   - `JsonMerger.cs` - JSON processing utilities
   - `zz_Extensions.cs` - Extension methods for code generation

4. **Configuration** (Root)
   - `GodotResourceGeneratorContextConfig.cs` - Shared context for generators
   - `NotNot.GodotNet.SourceGen.props` - MSBuild configuration for consuming projects
   - `NotNot.GodotNet.SourceGen.targets` - Package configuration

### Data Flow

```
Developer writes Godot C# code
    ↓
Roslyn compiler invokes analyzers
    ↓
GODOT001/GODOT002 analyzers scan syntax trees
    ↓
Build blocked if violations found
    ↓ (no violations)
Roslyn invokes incremental source generators
    ↓
ModularGenerator_Base collects AdditionalFiles (.tscn, .gd, .import, project.godot)
    ↓
NotNotSceneRoot_Generator finds [NotNotSceneRoot] classes → generates partial classes
    ↓
_ResPath_Generator scans .import files → generates _ResPath class
    ↓
Generated code compiled with user code
    ↓
Runtime: Developer calls InstantiateTscn() or uses _ResPath constants
```

### Integration Points

- **Roslyn Compiler Pipeline**: Analyzers execute during semantic analysis; generators execute during compilation
- **MSBuild**: `.props` file auto-configures AdditionalFiles; `.targets` file configures package consumption
- **Godot Project Structure**: Generators discover project root via `project.godot` location
- **NotNot Framework**: Generated code depends on `NotNot` namespace helpers (`_GD.InstantiateScene<T>`)

## Project Structure

```
NotNot.GodotNet.SourceGen/
├── Analyzers/
│   ├── GodotExitTreeNullCheckAnalyzer.cs          [GODOT001 null safety]
│   ├── GodotExitTreeNullCheckCodeFixProvider.cs   [Auto-fix provider]
│   └── GodotProhibitedApiAnalyzer.cs              [GODOT002 banned APIs]
├── Generators/
│   └── Modular/
│       ├── ModularGenerator_Base.cs               [Base generator infrastructure]
│       ├── NotNotSceneRoot_Generator.cs           [Scene instantiation helper]
│       └── _ResPath_Generator.cs                  [Asset path constants]
├── Helpers/
│   ├── Func.cs                                    [Template evaluation]
│   ├── JsonMerger.cs                              [JSON utilities]
│   └── zz_Extensions.cs                           [Extension methods]
├── GodotResourceGeneratorContextConfig.cs         [Shared generator context]
├── NotNot.GodotNet.SourceGen.csproj               [Project file]
├── NotNot.GodotNet.SourceGen.props                [MSBuild integration]
├── NotNot.GodotNet.SourceGen.targets              [Package configuration]
└── ReadMe.md                                      [User-facing documentation]
```

## Implementation Details

### Analyzer Execution

**GODOT001**: Scans all `_ExitTree()` method overrides in Godot.Node-derived classes. Detects reference type field accesses without null-conditional operators or explicit null checks. Reports error at member access location with suggested fix.

**GODOT002**: Scans all `SimpleMemberAccessExpression` nodes for property access. Checks if accessed property is in BannedApis dictionary. Reports error with prohibition reason and alternatives.

### Source Generator Execution

**NotNotSceneRoot_Generator**:
1. Discovers classes with `[NotNotSceneRoot]` attribute
2. Searches AdditionalFiles for matching `{ClassName}.tscn` file
3. Converts OS file path to res:// format
4. Generates partial class with `ResPath` and `InstantiateTscn()` members
5. Generates `NotNotSceneLoader` class with dictionary mapping class names to .tscn paths

**_ResPath_Generator**:
1. Scans all AdditionalFiles ending with `.import` or `.uid`
2. Removes import suffix to get original asset filename
3. Filters by allowed extensions (.gd, .tres, .txt, .json, .res, .model, .gdshader)
4. Converts file paths to res:// format
5. Generates alphanumeric-caps identifiers from file paths
6. Generates `_ResPath` class with `StringName` constants for each asset

### Incremental Generation Pipeline

1. `Initialize()` sets up syntax providers and additional file providers
2. `Predicate()` filters syntax nodes (classes with attributes)
3. `Transform()` extracts relevant class declarations
4. `Collect()` aggregates all matching classes and additional files
5. `RegisterSourceOutput()` invokes `ExecuteGenerator()` with aggregated data
6. `GeneratePartialClasses()` produces source text and adds to compilation

### MSBuild Integration

**NotNot.GodotNet.SourceGen.props**:
```xml
<AdditionalFiles Include="**/*.tscn" />
<AdditionalFiles Include="**/*.gd" />
<AdditionalFiles Include="project.godot" />
<AdditionalFiles Include="main.tscn" />
```

**NotNot.GodotNet.SourceGen.targets**:
```xml
<PackageReference Update="NotNot.GodotNet.SourceGen" PrivateAssets="All"/>
```

### NuGet Package Structure

```
NotNot.GodotNet.SourceGen.nupkg
├── analyzers/dotnet/cs/
│   ├── NotNot.GodotNet.SourceGen.dll
│   ├── System.Text.Json.dll                [All dependencies]
│   ├── System.Memory.dll
│   └── [... other runtime dependencies]
├── build/
│   ├── NotNot.GodotNet.SourceGen.props
│   └── NotNot.GodotNet.SourceGen.targets
└── [standard NuGet metadata]
```

## Dependencies

**Roslyn APIs** (Microsoft.CodeAnalysis.CSharp 4.11.0): Required for all source generation and analysis operations. Provides semantic model, syntax tree access, and compilation context. Versions must be compatible with Visual Studio.

**System.Text.Json** (9.0.8): Used for parsing Godot .import and project.godot files. Complex scaffolding required due to netstandard2.0 target - all transitive dependencies must be packaged in analyzers/dotnet/cs folder.

**NotNot Framework**: Generated code assumes availability of `_GD.InstantiateScene<T>()` helper and other NotNot utilities.

## Usage Notes

### Project Reference vs NuGet

When referencing this project directly (not the NuGet package), add `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the .csproj reference:

```xml
<ProjectReference Include="..\lib\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Disabling Analyzers

Use .editorconfig to disable specific diagnostics:

```ini
[*.cs]
dotnet_diagnostic.GODOT001.severity = none
dotnet_diagnostic.GODOT002.severity = none
```

### Debugging Generators

1. Uncomment `Debugger.Launch()` in `ModularGenerator_Base.Initialize()` line 68
2. Rebuild generator project
3. Clean consuming project
4. Rebuild consuming project - debugger will attach

### AdditionalFiles Configuration

Ensure all Godot files are included in .csproj:

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.tscn" />
  <AdditionalFiles Include="**/*.gd" />
  <AdditionalFiles Include="**/*.import" />
  <AdditionalFiles Include="project.godot" />
</ItemGroup>
```

