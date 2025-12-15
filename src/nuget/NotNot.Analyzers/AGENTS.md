# VIBEGUIDE

## Purpose
Analyzers for human and agents, to enforce workspace design decisions.

## Design Rules
- All new analyzers should default to `DiagnosticSeverity.Error` so all instances are flagged for immediate attention
- Suppressors should be conservative - only suppress clear false positives

---

# Diagnostic Suppressors

The `NotNotDiagnosticSuppressor` in `Advanced/SuppressionProvider.cs` provides intelligent suppression of false positive diagnostics based on code patterns.

## CA2000 Suppressions

CA2000 ("Dispose objects before losing scope") often fires on valid patterns where disposal IS handled but not in a way the analyzer recognizes.

### NNS004: Out Parameter with True-Path Disposal

**Pattern**: `if (Method(out var x)) { using (x) { ... } }`

**Why it's a false positive**: The out parameter IS disposed in the true branch. The false branch intentionally skips disposal because the method assigns `default`/`null` on false return.

**Example**:
```csharp
// CA2000 fires on 'execTracker' - SUPPRESSED by NNS004
if (WorkLoadBalancer.TryGetTracker(out var execTracker))
{
    using (execTracker)
    {
        // use tracker
    }
}
// false path: execTracker = default (no resources to dispose)
```

**Detection**: Checks if out parameter in bool-returning method's condition has `using` statement in true block.

---

### NNS005: Ownership Transfer via Add/Constructor

**Pattern**: Object passed to method with "Add" in name, or to constructor

**Why it's a false positive**: Ownership is transferred to the receiving container which manages disposal.

**Examples**:
```csharp
// Variable-based (SUPPRESSED)
var mmi = new MultiMeshInstance3D();
this._AddChild(mmi);  // Ownership transferred to scene tree

// Inline in Add call (SUPPRESSED)
collection.Converters.Add(new RoundtripObjConverter<GdVec3>(...));

// Passed to constructor (SUPPRESSED)
return new ArchetypePartitionState(mmi, ...);
```

**Detection**:
1. Walks up tree from diagnostic to find if inside argument to Add method
2. Checks if variable is later passed to method with "Add" in name
3. Checks if variable is later passed to any constructor

**Limitations**:
- "Add" substring matching is broad (matches `AddChild`, `Add`, `TryAdd`, etc.)
- Constructor pattern assumes all constructors take ownership
- Lambda boundaries not enforced (could suppress leaks inside lambdas passed to Add)

---

### NNS006: Inside Using Expression (Fluent API)

**Pattern**: Allocation inside a using statement's expression

**Why it's a false positive**: The using statement WILL dispose whatever the expression evaluates to. Fluent APIs typically return `this`.

**Examples**:
```csharp
// Statement form with expression (SUPPRESSED)
using (DD3d.NewScopedConfig().SetThickness(0.01f))
{
    // NewScopedConfig() returns object, SetThickness() returns same object
    // using disposes it
}

// Statement form with declaration (SUPPRESSED)
using (var config = GetConfig().Configure())
{
    // ...
}

// Declaration form (SUPPRESSED)
using var config = GetConfig().Configure();
```

**Detection**:
1. Checks if diagnostic is inside `UsingStatementSyntax.Expression` span
2. Checks if diagnostic is inside `UsingStatementSyntax.Declaration` span
3. Walks up tree to find `using var x = ...;` local declaration with using keyword

---

## NotNot Task Suppressions

### NNS001: Task Not Awaited in Test Methods

**Suppresses**: `NN_R001` (Task not awaited or returned)

**Why**: Fire-and-forget tasks are often intentional in test methods for setup/cleanup operations.

**Detection**: Method has test attribute (`[Test]`, `[Fact]`, `[Theory]`, `[TestMethod]`) or test naming pattern.

---

### NNS002: Task Not Awaited in Event Handlers

**Suppresses**: `NN_R001` (Task not awaited or returned)

**Why**: Event handlers often use fire-and-forget patterns for non-blocking operations.

**Detection**: Method name starts with "On", ends with "Handler", "_Click", "_Changed", or contains "Event".

---

### NNS003: Task Result Ignored in Cleanup

**Suppresses**: `NN_R002` (Task result not observed)

**Why**: Task results are often intentionally ignored in cleanup and disposal patterns.

**Detection**: Method name is "Dispose", "DisposeAsync", starts with "Cleanup"/"TearDown", or contains "Dispose"/"Close".

---

## Adding New Suppressors

1. Add `SuppressionDescriptor` with unique ID (NNS007, etc.)
2. Add to `SupportedSuppressions` array
3. Add detection logic in `ReportSuppressions`
4. Implement detection method (prefer simple tree traversal over semantic analysis)
5. Test with actual codebase patterns
6. Document in this file

### Detection Best Practices

- **Prefer tree traversal** over semantic model for performance
- **Walk up tree** from diagnostic node using `FirstAncestorOrSelf<T>()` or explicit loops
- **Use span containment** for position checks: `node.Span.Contains(diagnostic.Location.SourceSpan)`
- **Add boundary stops** to prevent walking out of scope (method/constructor/lambda boundaries)
- **Test both positive and negative cases** before claiming suppression works

---

## File Structure

```
NotNot.Analyzers/
├── Advanced/
│   └── SuppressionProvider.cs    # All NNS* suppressors
├── Reliability/
│   └── Concurrency/
│       ├── TaskAwaitedOrReturnedAnalyzer.cs  # NN_R001
│       └── TaskResultNotObservedAnalyzer.cs  # NN_R002
└── AGENTS.MD                      # This file
```
