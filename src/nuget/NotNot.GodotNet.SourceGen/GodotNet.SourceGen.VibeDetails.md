# GodotNet.SourceGen - Detailed Documentation

Extended architecture, implementation details, and troubleshooting extracted from AGENTS.md.

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

## Code Fix Requirements (VS Light Bulb)

Use this checklist if Visual Studio shows the diagnostic but does not offer the code-fix (Ctrl+.):

### 1. Consume the analyzer correctly

**Project reference:**
```xml
<ProjectReference Include="..\..\external-repo\NotNot-MonoRepo\src\nuget\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### 2. Roslyn binary compatibility

Known-good versions for VS 2022 17.11.x:
```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" />
<PackageReference Include="System.Composition" Version="9.0.8" PrivateAssets="all" />
```

### 3. MEF discovery of code fixes

```csharp
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(YourCodeFix))]
[Shared]
public class YourCodeFix : CodeFixProvider { ... }
```

### 4. Quick troubleshooting

- Solution Explorer → Analyzers node: confirm `NotNot.GodotNet.SourceGen` is listed
- Enable ActivityLog (`devenv /log`) and check for MEF load errors
- Ensure diagnostic ID matches a code fix provider's `FixableDiagnosticIds`

### GODOT003 specifics

- Reports for `protected override void Dispose(bool disposing)` in `GodotObject`-derived types
- Offers: "Wrap disposing block in try/catch" or "Add disposing guard and wrap in try/catch"

## Implementation Details

### Analyzer Execution

**GODOT001**: Scans `_ExitTree()` overrides in Godot.Node-derived classes. Detects reference type field accesses without null-conditional operators.

**GODOT002**: Scans `SimpleMemberAccessExpression` nodes for banned APIs in dictionary. Reports with prohibition reason and alternatives.

### Source Generator Execution

**NotNotSceneRoot_Generator**:
1. Discovers classes with `[NotNotSceneRoot]` attribute
2. Searches AdditionalFiles for matching `{ClassName}.tscn` file
3. Converts OS file path to res:// format
4. Generates partial class with `ResPath` and `InstantiateTscn()` members

**_ResPath_Generator**:
1. Scans AdditionalFiles ending with `.import` or `.uid`
2. Filters by allowed extensions (.gd, .tres, .txt, .json, .res, .model, .gdshader)
3. Generates `_ResPath` class with `StringName` constants

### MSBuild Integration

**NotNot.GodotNet.SourceGen.props**:
```xml
<AdditionalFiles Include="**/*.tscn" />
<AdditionalFiles Include="**/*.gd" />
<AdditionalFiles Include="project.godot" />
```

### NuGet Package Structure

```
NotNot.GodotNet.SourceGen.nupkg
├── analyzers/dotnet/cs/
│   ├── NotNot.GodotNet.SourceGen.dll
│   ├── System.Text.Json.dll
│   └── [... other runtime dependencies]
├── build/
│   ├── NotNot.GodotNet.SourceGen.props
│   └── NotNot.GodotNet.SourceGen.targets
└── [standard NuGet metadata]
```

## Debugging Generators

1. Uncomment `Debugger.Launch()` in `ModularGenerator_Base.Initialize()` line 68
2. Rebuild generator project
3. Clean consuming project
4. Rebuild consuming project - debugger will attach

## AdditionalFiles Configuration

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.tscn" />
  <AdditionalFiles Include="**/*.gd" />
  <AdditionalFiles Include="**/*.import" />
  <AdditionalFiles Include="project.godot" />
</ItemGroup>
```
