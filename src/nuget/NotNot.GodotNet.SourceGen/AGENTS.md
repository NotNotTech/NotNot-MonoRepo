# NotNot.GodotNet.SourceGen

Source generators and analyzers for Godot C# development. Target: netstandard2.0 for Roslyn compatibility.

## Core Components

| Component | Purpose |
|-----------|---------|
| `GODOT001` | Null safety analyzer for `_ExitTree()` |
| `GODOT002` | Prohibited API analyzer |
| `NotNotSceneRoot_Generator` | Generates `ResPath` + `InstantiateTscn()` for `[NotNotSceneRoot]` classes |
| `_ResPath_Generator` | Generates `StringName` constants for Godot assets |

## Critical Design Decisions

- **All generators inherit from `ModularGenerator_Base`** - shared incremental generation infrastructure
- **ResPath uses `res://` format** - discovered via `project.godot` location
- **MSBuild integration** - `.props` auto-adds `.tscn`, `.gd`, `project.godot` as AdditionalFiles
- **netstandard2.0** - required for Roslyn pipeline; System.Text.Json needs complex scaffolding

## E2E Scenarios

| ID | Scenario |
|----|----------|
| `E2E_01_NullCheck` | `_ExitTree` without null check → GODOT001 → add `?.` → pass |
| `E2E_02_ProhibitedApi` | `MultiMesh.CustomAabb` → GODOT002 → use `GenerateAabb()` → pass |
| `E2E_03_SceneInstantiation` | `[NotNotSceneRoot]` + `.tscn` → generated `InstantiateTscn()` |
| `E2E_04_ResPathAccess` | `.import` files → generated `_ResPath.ASSETS_ICON_PNG` |

## Known Limitations

- **Source Generator Debugging**: Uncomment `Debugger.Launch()` in `ModularGenerator_Base.Initialize`
- **Duplicate Scene Names**: `InvalidOperationException` if multiple `.tscn` share class name
- **AdditionalFiles**: Must be explicitly included in `.csproj`

## Critical Files

| File | Purpose |
|------|---------|
| `Generators/Modular/ModularGenerator_Base.cs` | Base generator infrastructure |
| `Analyzers/GodotExitTreeNullCheckAnalyzer.cs` | GODOT001 |
| `NotNot.GodotNet.SourceGen.props` | MSBuild AdditionalFiles config |

## Details

- [GodotNet.SourceGen.VibeDetails.md](./GodotNet.SourceGen.VibeDetails.md) - Architecture, project structure, code fix troubleshooting

## Related Topics

- `NotNot.Bcl.Core` - Provides `_GD`, `__` helpers used in generated code
