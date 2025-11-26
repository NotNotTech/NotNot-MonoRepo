# NotNot.GodotNet.SourceGen

Source generators and analyzers for Godot C# projects. Provides compile-time code generation and reliability checks specific to Godot engine patterns.

## Features

### Source Generators
- Resource path generation for type-safe asset references
- Scene tree node access helpers

### Analyzers

#### GODOT001: _ExitTree Null Check Analyzer
**Severity**: Error
**Category**: Reliability

Ensures member variables accessed in _ExitTree overrides use null-conditional operators or explicit null checks to prevent NullReferenceExceptions during Godot editor cold-reload.

```csharp
// ❌ Problematic
public override void _ExitTree()
{
    _myNode.QueueFree(); // May be null during cold-reload
}

// ✅ Fixed
public override void _ExitTree()
{
    _myNode?.QueueFree(); // Null-safe
}
```

#### GODOT002: Prohibited API Analyzer
**Severity**: Error
**Category**: Reliability

Prohibits usage of specific Godot APIs known to have bugs or cause confusion.

**Banned APIs**:

- `Godot.MultiMesh.CustomAabb`
- `Godot.MultiMeshInstance3D.CustomAabb`

**Reason**: CustomAabb has a positioning bug where the AABB corner (not center) is positioned at the node center, causing incorrect culling and visibility calculations. AABB.position represents the minimum corner of the bounding box, but developers often expect center-relative positioning.

**Alternatives**:
- Use `MultiMesh.GenerateAabb()` for automatic AABB calculation
- Manually adjust for corner offset if CustomAabb is required: `aabb.position = centerPos - (aabb.size * 0.5f)`

```csharp
// ❌ Prohibited
multiMesh.CustomAabb = new Aabb(Vector3.Zero, new Vector3(1, 1, 1));

// ✅ Alternative 1: Auto-generate
multiMesh.GenerateAabb();

// ✅ Alternative 2: Manual corner-offset adjustment
var aabbSize = new Vector3(1, 1, 1);
var centerPos = Vector3.Zero;
var cornerPos = centerPos - (aabbSize * 0.5f); // Convert center to corner
// Note: Still avoid CustomAabb; use mesh bounds instead
```

**Configuration**: Can be disabled via .editorconfig if absolutely necessary:
```ini
[*.cs]
dotnet_diagnostic.GODOT002.severity = none
```

#### GODOT003: Dispose Exception Safety Analyzer
**Severity**: Error
**Category**: Reliability

Ensures `GodotObject.Dispose(bool disposing)` overrides use try/catch blocks to prevent unhandled exceptions from causing fatal editor crashes. Exceptions crossing the native/managed boundary during Dispose can cause unrecoverable crashes (0xC0000005) during hot-reload or editor shutdown.

```csharp
// ❌ Problematic (Option 1 scenario - has guard but no try/catch)
protected override void Dispose(bool disposing)
{
    base.Dispose(disposing);
    if (disposing)
    {
        _field?.SomeMethod(); // Exception here crashes Godot editor
        _otherField = null;
    }
}

// ✅ Fixed (Option 1: Auto-fix wraps disposing block content)
protected override void Dispose(bool disposing)
{
    base.Dispose(disposing);
    if (disposing)
    {
        try
        {
            _field?.SomeMethod(); // Protected from crashes
            _otherField = null;
        }
        catch (Exception ex)
        {
            _GD.ThrowError(ex);
        }
    }
}

// ❌ Problematic (Option 2 scenario - no guard at all)
protected override void Dispose(bool disposing)
{
    base.Dispose(disposing);
    _field?.SomeMethod(); // Runs even when disposing=false, no exception protection
    _otherField = null;
}

// ✅ Fixed (Option 2: Auto-fix adds guard AND wraps in try/catch)
protected override void Dispose(bool disposing)
{
    base.Dispose(disposing);
    if (disposing)
    {
        try
        {
            _field?.SomeMethod(); // Now guarded AND protected
            _otherField = null;
        }
        catch (Exception ex)
        {
            _GD.ThrowError(ex);
        }
    }
}
```

**Why Critical**:
- Unhandled exceptions in Dispose during hot-reload cause non-deterministic fatal crashes
- Native Godot code cannot handle managed exceptions during cleanup
- Crash error code 0xC0000005 indicates unhandled exception at native boundary
- Try/catch with logging enables diagnosis without crashing editor

**Code Fix Behavior**:
- If `if (disposing)` guard exists: Wraps guard block content in try/catch
- If no guard: Adds guard and wraps cleanup code in try/catch
- Uses `_GD.ThrowError(ex)` for exception logging and diagnostic reporting
- Available via Ctrl+. or lightbulb in IDE

**Configuration**: Can be disabled via .editorconfig if absolutely necessary:
```ini
[*.cs]
dotnet_diagnostic.GODOT003.severity = none
```

## Usage

If referencing this project directly (not the nuget package) be sure to add ` OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the `.csproj` reference, like:

```xml
<ProjectReference Include="..\lib\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

