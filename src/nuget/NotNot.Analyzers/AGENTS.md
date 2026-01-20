# NotNot.Analyzers

Roslyn analyzers and suppressors enforcing workspace design decisions.

## Design Rules

- New analyzers default to `DiagnosticSeverity.Error`
- Suppressors are conservative - only suppress clear false positives

## Analyzers

### NN_R005: Catch Block Must Rethrow

Catch blocks catching `Exception`, `SystemException`, or bare `catch` must rethrow.

**Flagged**: `catch { }`, `catch (Exception) { }`, `catch (Exception ex) { Log(ex); }`
**Allowed**: `throw;`, `throw new Wrapped(ex);`, `when (condition)`, specific types like `IOException`

## Suppressors (CA2000)

| ID | Pattern | Why Suppressed |
|----|---------|----------------|
| NNS004 | `if (Method(out var x)) { using (x) }` | True-path disposal |
| NNS005 | Object passed to Add/constructor | Ownership transfer |
| NNS006 | Allocation in using expression | Fluent API disposal |

## Task Suppressors

| ID | Suppresses | Pattern |
|----|------------|---------|
| NNS001 | NN_R001 | Test methods (fire-and-forget OK) |
| NNS002 | NN_R001 | Event handlers (On*, *Handler) |
| NNS003 | NN_R002 | Cleanup methods (Dispose, TearDown) |

## Critical Files

| File | Purpose |
|------|---------|
| `Advanced/SuppressionProvider.cs` | All NNS* suppressors |
| `Reliability/Exceptions/CatchBlockMustRethrowAnalyzer.cs` | NN_R005 |
| `Reliability/Concurrency/TaskAwaitedOrReturnedAnalyzer.cs` | NN_R001 |

## Adding New Suppressors

1. Add `SuppressionDescriptor` with unique ID (NNS007, etc.)
2. Add to `SupportedSuppressions` array
3. Implement detection in `ReportSuppressions`
4. Prefer tree traversal over semantic model for performance
