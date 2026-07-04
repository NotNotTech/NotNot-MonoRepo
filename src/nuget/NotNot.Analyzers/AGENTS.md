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

### NN_R007: Hand-Rolled Atomic File Write

A write to a DETERMINISTIC temp path followed by `File.Move(temp, final, overwrite: true)` — a hand-rolled atomic write missing the unique-temp + per-path-serialization concurrency guard. Concurrent writers collide on the shared `.tmp` (Windows `ERROR_SHARING_VIOLATION`); racing overwrite-renames to one destination are not mutually safe.

**Severity**: Error
**Flagged**: `File.Move(temp, final, overwrite: true)` where `temp` resolves (local declaration OR inline) to a DETERMINISTIC `+ ".tmp"` concat (no `Guid`/`Random`/`Ticks`/`GetRandomFileName`/`GetTempFileName` in the concat tree).
**Allowed**:
- `NotNot.Storage.AtomicFileWriter.WriteAtomic` / `WriteAtomicAsync` — the sanctioned helper (its temp is minted via a method call, not a literal-bearing deterministic concat).
- Unique temp: `var tmp = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";` (Guid → unique, not deterministic).
- `File.Move(a, b)` — no `overwrite: true` (not the temp-then-rename idiom).
- `File.WriteAllText(finalPath, ...)` — direct final write, no temp + Move.

**Preferred Fix** (in order):
1. `NotNot.Storage.AtomicFileWriter.WriteAtomic` / `WriteAtomicAsync` (unique temp + per-path lock + bounded retry). A `WriteAtomic(string finalPath, IEnumerable<string> lines, Encoding? = null)` overload mirrors `File.WriteAllLines`.
2. If `AtomicFileWriter` is unavailable: a UNIQUE temp (`finalPath + Guid + ".tmp"`) AND per-final-path serialization of the write+rename — the unique temp and the per-path lock are BOTH required; removing either re-opens a race.
3. `#pragma warning disable NN_R007` only if the write is provably single-threaded AND single-process for the file's lifetime.

**Key principle**: Unique-name alone is insufficient — two racers each still `Move(uniqueTmp → SAME finalPath, overwrite:true)`; the entire write+replace pair must serialize per normalized final path.

Complementary to NN_R001/NN_R002 (Task-concurrency) and NN_R005/NN_R006 (catch-reliability) on a disjoint axis — NN_R007 is the file-I/O concurrency-reliability rule.

### NN_R008: Direct File Append Has No Cross-Process Retry

A direct `System.IO.File` append — `File.AppendAllText`, `File.AppendAllLines`, or their async `AppendAllTextAsync`/`AppendAllLinesAsync` variants — to a final path, invoked OUTSIDE `NotNot.Storage.AtomicFileWriter`. These APIs open the file with the default `FileShare.Read`; a SECOND OS process appending the SAME file (e.g. a shared growing log) throws `IOException "being used by another process"` (Windows `ERROR_SHARING_VIOLATION`). An in-process `lock` cannot arbitrate a cross-process collision — the losing process must retry the transient lock.

**Severity**: Error
**Flagged**: `File.AppendAllText` / `File.AppendAllLines` / `File.AppendAllTextAsync` / `File.AppendAllLinesAsync` where the invoked method's containing type resolves to `System.IO.File` (covers `File.`, `IO.File.`, fully-qualified, aliased) AND the enclosing type is not `NotNot.Storage.AtomicFileWriter`.
**Allowed**:
- `NotNot.Storage.AtomicFileWriter.AppendWithRetry` — the sanctioned helper (not a `System.IO.File` member, never matched).
- A `File.Append*` call INSIDE `NotNot.Storage.AtomicFileWriter` itself — its `AppendWithRetry` calls `File.AppendAllText` internally (the sanctioned impl); containing-type exemption.
- `File.WriteAllText(finalPath, ...)` — a direct full-file write, not an append (out of scope).
- `File.Move(temp, final, overwrite: true)` — the NN_R007 temp-then-rename domain, not append.

**Preferred Fix** (in order):
1. `NotNot.Storage.AtomicFileWriter.AppendWithRetry(path, contents)` — bounded transient-lock retry (reuses the AtomicFileWriter `IsTransient`/`BackoffMs`/`MaxAttempts` + jitter policy, rethrow-on-exhaustion).
2. If the intent is a full-file rewrite rather than an append: `NotNot.Storage.AtomicFileWriter.WriteAtomic`.
3. `#pragma warning disable NN_R008` only if this append is provably single-process for the file's lifetime.

Complementary to NN_R007 on a disjoint axis: NN_R007 = hand-rolled atomic full-file REWRITE (deterministic temp + overwrite rename); NN_R008 = direct APPEND (no temp, no Move). Together gap-free — `AtomicFileWriter` is silent under both.

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

### NN_C004: No Code-Side Default for AppSettings Options

Forbids a code-side default on a `NotNot.AppSettings`-generated settings option — i.e. `{settingsOption} ?? {literal|const}`. The generator makes `appsettings*.json` the single source of truth for defaults (every generated property is nullable `T?`); a code-side `?? <default>` is a second, drifting copy of a default that already lives in the JSON AND it silently masks a missing/null config value that should fail loud at startup.

**Severity**: Error
**Flagged** (at the `??`/`??=` operator): `{member} ?? {default}` or `{member} ??= {default}` where the member's containing type carries `[System.CodeDom.Compiler.GeneratedCode("NotNot.AppSettings", ...)]` AND the right operand is a default-VALUE shape — a numeric / string / bool / char literal (optionally a leading unary `-`/`+` on a numeric literal), a `const` field, or a `static readonly` field whose initializer is itself a compile-time constant. Casts, parentheses, and conditional-access (`settings.Sessions?.MaxOpenPty`) on the left operand are stripped before resolving the member.
**Allowed**:
- `settings.X ?? throw …` — the PERMITTED required-no-default pattern (fail loud)
- Left operand that is not a settings option (a local, or a member of a non-`[GeneratedCode("NotNot.AppSettings")]` type)
- Computed right operand — a method call (`?? Compute()`), a property read (`?? Environment.ProcessorCount`), or a `static readonly` field with a computed initializer — none can live in static JSON
- Empty / whitespace-only string literal (`?? ""`, `?? "   "`) or `System.String.Empty` (`?? string.Empty`) — null-NORMALIZATION (coalescing null to a blank string), not a configuration default; exempt. A non-empty string literal (`?? "vs-running"`) is still a code-side default and fires.
- `?? null` / `?? default` (not a default value)
- Coalesce expressions inside generated `*.g.cs` files (skipped via `ConfigureGeneratedCodeAnalysis(None)`)

**Preferred Fix Options** (in order):
1. Set the default in `appsettings*.json` and read the typed property directly (remove the `?? default`).
2. If the value is REQUIRED with no sensible default, fail loud with `?? throw` instead of a silent fallback.
3. If genuinely optional, branch on null as a real state.
4. `#pragma warning disable NN_C004` / `.editorconfig` severity override only when a code-side default is genuinely intended.

Complementary to NN_C003 (BoolDefaultFalse): disjoint axis — C003 = bool param default-false at DECLARATION; C004 = no code-side default substitution at CONSUMPTION. Never co-fire.

### NN_C005: No ServerOnly AppSettings Read From Client-Reachable Code

Forbids reading a `NotNot.AppSettings`-generated **ServerOnly** key from a `.Shared`/`.Client` (WASM-client-reachable) compilation. The generator prunes ServerOnly keys from the client snapshot (`_ClientAppSettings`), so the read is `null` on the client at runtime — and with `.Require()` throws at bootstrap (the NN_C004 reachability failure class). Read-reachability companion to NN_C004's write-default rule; orthogonal axes, gap-free.

**Severity**: Error
**Flagged** (at the accessed-member identifier): `{settings}.{Key}` (or `{settings}?.{Key}`) where the member's containing type is a `NotNot.AppSettings`-generated FULL-tree type (`[GeneratedCode("NotNot.AppSettings", ...)]`, in the `.AppSettingsGen` `AppSettings` tree — NOT the `_ClientAppSettings` mirror) AND the corresponding path is ABSENT from the generated `_ClientAppSettings` mirror (⇒ ServerOnly), AND the current assembly name ends with `.Shared` or `.Client`.
**Allowed**:
- Read of a client-whitelisted key (present in `_ClientAppSettings` — `ClientRead`/`ClientWriteLocal`/`ClientWriteServer`)
- The same read from a `.Server` assembly (ServerOnly reads are legal server-side)
- Read of a member on a non-`[GeneratedCode("NotNot.AppSettings")]` type
- Read of the `_ClientAppSettings` mirror itself (already pruned ⇒ safe)
- `nameof({settings}.{Key})` (no runtime dereference)
- Member access inside generated `*.g.cs` files (skipped via `ConfigureGeneratedCodeAnalysis(None)`)
- Compilations whose `_ClientAppSettings` mirror is not present (conservative — cannot prove ServerOnly)

**Preferred Fix Options** (the fix is a DECISION, not a reflex):
1. If the client genuinely needs the value, whitelist it as `ClientRead` (or `ClientWriteLocal`/`ClientWriteServer`) under `NotNotAppSettings:whitelist` in `appsettings*.json`.
2. If it is server-only/secret, move the read to a `.Server`-side type. **Do NOT whitelist server-only/secret config** — that exposes it to the client.
3. `#pragma warning disable NN_C005` / `.editorconfig` severity override only if the code path provably never runs on the client.

Complementary to NN_C004 (AppSettingsCodeDefault) on a disjoint axis: C004 = write-side `?? default` ban at CONSUMPTION; C005 = read-side ServerOnly-reachability ban. Never co-fire.

### NN_DI_006: Hosted Service Required Delegate Ctor Param

A concrete `IHostedService` (e.g. `BackgroundService`) whose single public constructor has a REQUIRED delegate-typed parameter (`Func<>`/`Action<>`/custom delegate) that DI never registers. The NotNot Scrutor convention auto-registers every `IHostedService` `AsSelfWithInterfaces` with constructor injection; a required delegate param makes the concrete registration unconstructible → the host crashes at `builder.Build()` (Dev `ValidateOnBuild`) / `Host.StartAsync` (Prod) BEFORE any port binds. Gap-filling third rule of the hosted-service matrix (NN_DI_004 = marker+IHostedService conflict; NN_DI_005 = marker-absence; NN_DI_006 = ctor-unconstructibility) — marker-INDEPENDENT (fires whether or not the type carries an `IDi{L}Service` marker).

**Severity**: Error
**Flagged**: A concrete (non-abstract) class implementing `Microsoft.Extensions.Hosting.IHostedService` (directly or via `BackgroundService`/a base) with EXACTLY ONE public instance constructor whose parameter list includes a `!HasExplicitDefaultValue` parameter of `TypeKind.Delegate`.
**Allowed**:
- Optional-default delegate param (`Func<...>? f = null`) — DI passes null, safe no-op.
- Required NON-delegate param (a registered interface abstraction) — DI resolves it.
- Non-`IHostedService` class with a delegate ctor param (manual-factory pattern; never auto-registered).
- Abstract `IHostedService` (not auto-registered as concrete).
- Zero or >1 public instance ctors (ambiguous; MS.DI greedy-resolve / `[ActivatorUtilitiesConstructor]` undecidable — conservative skip).
- `[AutoDiBypass]` on the type / assembly.

**Preferred Fix** (in order):
1. Replace the delegate with a DI-registered abstraction — a small interface implemented by the providing service (e.g. `ISessionReapPurger` implemented by `VowSessionService`) — and inject that.
2. ONLY if a null delegate is a SAFE no-op for the auto-registered instance, make the parameter optional with a default (`Func<...>? f = null`). NOT if absence silently disables required behavior.
3. `[AutoDiBypass]` on the type, or `#pragma warning disable NN_DI_006` / `.editorconfig` severity, ONLY if the type is provably never auto-registered / DI-constructed.

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
| `Reliability/Concurrency/HandRolledAtomicFileWriteAnalyzer.cs` | NN_R007 |
| `Reliability/Concurrency/DirectFileAppendAnalyzer.cs` | NN_R008 |
| `Conventions/BoolDefaultFalseAnalyzer.cs` | NN_C003 |
| `Conventions/AppSettingsCodeDefaultAnalyzer.cs` | NN_C004 |
| `Conventions/NnAppSettingsServerOnlyReadAnalyzer.cs` | NN_C005 |
| `Architecture/DI/DiMarkerEnforcementAnalyzer.cs` | NN_DI_001..006 |

## Adding New Suppressors

1. Add `SuppressionDescriptor` with unique ID (NNS007, etc.)
2. Add to `SupportedSuppressions` array
3. Implement detection in `ReportSuppressions`
4. Prefer tree traversal over semantic model for performance
