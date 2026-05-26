# NotNot.Analyzers

Roslyn analyzers and suppressors enforcing workspace design decisions.

## Design Rules

- New analyzers default to `DiagnosticSeverity.Error`
- Suppressors are conservative - only suppress clear false positives

## Analyzers

### NN_A003: Forbid Concrete ApiResponse

Use `IApiResponse<T>` (interface) for transport returns, or `Maybe<T>` for domain/app layer code. `ApiResponse<T>` requires `HttpResponseMessage` and cannot be constructed in Server DI path.

**Severity**: Error
**Flagged**: Variable, parameter, field, property, or return type using `Refit.ApiResponse<T>`
**Allowed**: `Refit.IApiResponse<T>`, `Refit.IApiResponse` (interfaces)

### NN_A004: Forbid ApiException Catch

Avoid catching `ApiException` for business outcomes. Mutation interfaces should return `Task<IApiResponse>` for structured non-throwing error handling.

**Severity**: Warning (some legitimate infrastructure uses exist)
**Flagged**: `catch (ApiException)`, `catch (ValidationApiException)` (derived types too)
**Allowed**: `catch (Exception)`, `catch (HttpRequestException)`, other non-ApiException types

### NN_A005: Ignored ApiResponse Result

`IApiResponse` result from await must be inspected or explicitly discarded.

**Severity**: Warning
**Flagged**: `await client.MutateAsync();` (result implicitly discarded)
**Allowed**: `var result = await ...` (assigned), `_ = await ...` (explicitly discarded), `await Task.Delay(1)` (not IApiResponse)

### NN_R005: Catch Block Must Rethrow

Catch blocks catching `Exception`, `SystemException`, or bare `catch` must rethrow.

**Flagged**: `catch { }`, `catch (Exception) { }`, `catch (Exception ex) { Log(ex); }`
**Allowed**:
- `throw;` - rethrows original exception
- `throw new Wrapped(ex);` - rethrows wrapped
- `when (condition)` - exception filter narrows scope
- Specific types like `IOException`, `JsonException`
- `__.DebugAssertOnce(ex)` - debug assertion with graceful degradation

**Preferred Pattern** (graceful degradation with visibility):
```csharp
catch (Exception ex)
{
    __.DebugAssertOnce(ex);  // Fires in DEBUG, logs once, doesn't throw
    return fallbackValue;    // Graceful degradation in RELEASE
}
```

See `protocols/debugging.md` for diagnostic patterns.

### NN_R006: Empty Catch Block

Catch blocks with zero statements silently swallow exceptions. Any exception type.

**Severity**: Error
**Flagged**: `catch { }`, `catch (IOException) { }`, `catch (Ex) { // comment }`, `catch (Ex ex) when (cond) { }`
**Allowed** (has >=1 statement):
- `__.DebugAssertOnce(ex)` - debug assertion with graceful degradation
- `Log(ex)` - logging for expected exceptions
- `throw;` or `throw new Wrapped(ex);` - rethrow
- `return fallback;` - explicit control flow

**Preferred Fix** (in order):
1. Avoid exceptions for control flow — but pre-checks (`File.Exists()`, `dict.ContainsKey()`) have TOCTOU races in concurrent scenarios. Atomic operations (`FileMode.CreateNew`, `ConcurrentDictionary.TryAdd`, DB unique constraints) legitimately need try/catch for race handling.
2. `__.DebugAssertOnce(ex)` for unexpected exceptions
3. Logging for expected-but-notable exceptions (e.g., `_logger.LogDebug(ex, "race-condition conflict")` for atomic-op catches)
4. `#pragma disable` only if truly needed

**Key principle**: Even legitimate race-condition catches must not be empty — observability requires at minimum a DebugAssert or log statement.

### NN_C003: Boolean Default False

Enforces the `BOOLEAN_DEFAULT_FALSE` convention — boolean parameters, properties, and fields must default to `false`, not `true`. Default-true booleans silently flip behavior on consumers who don't know to opt out; default-false forces explicit opt-in and keeps the read surface unsurprising.

**Severity**: Error
**Flagged**:
- Parameter (method / constructor / record-positional / primary-constructor) with literal `= true` default
- Public / internal / protected-internal property auto-initializer `{ ... } = true;` (any accessor combo — `set`, `init`, get-only)
- Public / internal / protected-internal field initializer `bool _x = true;`

**Allowed**:
- Literal `= false` default (the canonical form)
- `private`, `protected`, `private protected` fields and properties (implementation-detail visibility — exempt)
- `const bool X = true;` (compile-time value, not a default state surface — exempt)
- Computed properties without initializers (no default-state surface to flip)
- Non-literal expressions — only literal `true` triggers (e.g. `= someConst` or `= GetDefault()` are not flagged)
- `[NotNot.Bcl.Diagnostics.CodeStyleBypass]` on the member, declaring type, or assembly (narrow opt-out for genuinely exceptional cases)

**Important**: `readonly` does NOT exempt. The consumer-facing read surface still presents `true` by default whether the field is mutable or not. Parameters are enforced unconditionally regardless of method visibility (a `private` method's defaults still propagate through tests, refactors, and future callers).

**Recommended Fix Options** (in preference order):
1. Flip the default to `false` and rename the member to express the negated semantics (`enabled = true` → `disabled = false`, `inherit = true` → `noInherit = false`).
2. Apply `[CodeStyleBypass]` at member scope with a comment justifying why default-true is genuinely correct here.
3. Reduce visibility to `private`/`protected` if the member doesn't need to be part of the public surface.

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
| `Architecture/ForbidConcreteApiResponseAnalyzer.cs` | NN_A003 |
| `Architecture/ForbidApiExceptionCatchAnalyzer.cs` | NN_A004 |
| `Architecture/IgnoredApiResponseAnalyzer.cs` | NN_A005 |
| `Advanced/SuppressionProvider.cs` | All NNS* suppressors |
| `Reliability/Exceptions/CatchBlockMustRethrowAnalyzer.cs` | NN_R005 |
| `Reliability/Exceptions/EmptyCatchBlockAnalyzer.cs` | NN_R006 |
| `Reliability/Concurrency/TaskAwaitedOrReturnedAnalyzer.cs` | NN_R001 |
| `Conventions/BoolDefaultFalseAnalyzer.cs` | NN_C003 |

## Adding New Suppressors

1. Add `SuppressionDescriptor` with unique ID (NNS007, etc.)
2. Add to `SupportedSuppressions` array
3. Implement detection in `ReportSuppressions`
4. Prefer tree traversal over semantic model for performance
