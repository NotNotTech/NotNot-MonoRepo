# Warning Dispositions — NotNot-MonoRepo

**Tracked build-warning ledger.** This document is the authoritative account of every compiler/analyzer warning the monorepo emits under a full Release compile. It exists so the residual warnings are recognized as *intentional and accounted-for*, not mistaken for a failed fix. (The 2026-07-15 warning-drain deferred its residue into a gitignored memory doc that no later session executed; the residue was then mistaken for a broken fix. A tracked, in-repo ledger is the fix for that process failure.)

---

## (a) Policy

- **Terminal metric = ZERO-UNDISPOSITIONED.** Every warning is either FIXED or verified per-site and terminally dispositioned. The success criterion is zero *undispositioned* warnings.
- **Disposition mechanism = SITE-SCOPED pragma + at-site justification comment** (policy updated 2026-07-23, user-ratified). Each of the 26 verified sites below now carries `#pragma warning disable {code}` with a `FALSE_POSITIVE` or `ACCEPTED_BY_DESIGN` justification comment. This keeps each rule LIVE for new violations while silencing only the verified site. Blanket suppression (`NoWarn`, file-scope pragma, `[SuppressMessage]` without site verification) remains forbidden as a fix.
- **Expected residual in build output: 0 warnings** from the sites below. A full Release compile reporting ANY warning is a genuine finding: either a new site (fix or verify+disposition it) or a pragma drifted off its diagnostic (re-anchor it). This ledger remains the narrative record of each site's verification; the pragmas at the sites are the enforcement.

---

## (b) How to re-verify

Incremental and IDE builds hide the analyzer-project and full-graph warnings. Only a forced-full Release compile surfaces the complete residual. Recipe:

1. **Delete analyzer `obj/` + `bin/`** for the analyzer/generator projects (`NotNot.Analyzers`, `NotNot.Analyzers.CodeFixes`, `NotNot.Analyzers.Package`, `NotNot.BlazorAnalyzers`, `NotNot.GodotNet.SourceGen`(+`.CodeFixes`/`.Package`), and `NotNot.AppSettings`). This forces the analyzers to re-run rather than replay cached diagnostics.
2. **Plain `build`, NOT `-t:Rebuild`.** Rebuild triggers a CS0006 ordering race (`NotNot.Analyzers.dll` is cleaned after consumers schedule against it).
3. **Configuration = Release.**
4. **`-p:UseSharedCompilation=false`** — forces a per-project analyzer pass so warnings are not incrementally elided.
5. **Dedup double-logged lines.** A multi-pass / multi-TFM build logs the same `file(line,col): warning CODE` several times. Count distinct `file(line,col): warning CODE` tuples, not raw log lines. (The forced-full build that produced this ledger logged ~100 raw lines for 26 distinct diagnostics.)
6. **Enumerate with a robust regex.** Use `: warning [^:]+:` — the naive `: warning [A-Z]+\d+:` misses underscore/lowercase codes like `PH_B009`, `RCS1194`, `NN_R003`.

Every remaining distinct warning must map to a row in section (c). Zero UNDISPOSITIONED = success.

---

## (c) Disposition table

All 26 residual warnings are **OWN** (intentional-by-design), and as of 2026-07-23 each site carries a site-scoped pragma + justification comment (classification `FALSE_POSITIVE` = analyzer model factually wrong · `ACCEPTED_BY_DESIGN` = analyzer right, behavior verified-intentional). Paths are relative to the monorepo root; the three owning projects are `NotNot.Bcl.Core`, `NotNot.Bcl`, and `NotNot.SimStorm`.

| Code | Count | Site(s) | Disposition |
|---|---|---|---|
| PH_B009 | 10 | `SimpleStorageManager.cs` DisposeAsync :755, :756, :762 (×3); `Collections/_unused/SlotList.cs` Dispose :43–:47 | OWN — see B009-1, B009-2 |
| PH_P007 | 5 | `DebuggerInfo.cs:88`; `SimpleStorageManager.cs:209`, `:217`; `DebuggableAsyncHelper.cs:58`; `DebuggableTimeoutCancelTokenHelper.cs:170` | OWN — see P007 |
| PH_S032 | 4 | `SimpleStorageManager.cs` UpdateAsync :285, :286; SetDataAsync :319, :320 | OWN — see S032 |
| PH_P008 | 2 | `DebuggerInfo.cs:91`; `DebuggableTimeoutCancelTokenHelper.cs:72` | OWN — see P008 |
| PH_S007 | 1 | `Concurrency/Advanced/DedicatedThreads.cs:46` | OWN — see S007 |
| PH_P006 | 1 | `Concurrency/FrameDataChannel.cs:95` | OWN — see P006 |
| RCS1194 | 1 | `Diagnostics/Problem.cs:101` (`ProblemException`) | OWN — see RCS1194 |
| CS0618 | 1 | `NotNot.SimStorm/src/_scratch/Ecs/Allocation/_allocaton.cs:2232` | OWN — see CS0618 |
| CA2255 | 1 | `NotNot.Bcl/NotNot/Serialization/_Initialize_SerializationHelper_ObjectConverters.cs:18` | OWN — see CA2255 |
| **Total** | **26** | | |

All 26 live in `NotNot.Bcl.Core` (24), `NotNot.Bcl` (1 — CA2255), and `NotNot.SimStorm` (1 — CS0618).

### Per-site rationales

**B009-1 — `SimpleStorageManager.cs` DisposeAsync (:755, :756, :762×3), PH_B009×5** — These accesses run AFTER the locked `_isDisposed = true` snapshot inside `DisposeAsync`. Disposal is single-entry (a locked double-check short-circuits any concurrent dispose), so this teardown code is single-threaded by construction. The generation counters (`_dirtyGeneration` / `_writtenGeneration`) follow the class's documented lock-free protocol — every cross-thread access is `Volatile.Read`/`Interlocked`, which the monitor-lock-modeling analyzer does not recognize. (An unrelated `#pragma warning disable PH_B010` at `:761` documents that same lock-free-vs-monitor-lock distinction for the B010 rule; it is not what dispositions these B009 warnings.)

**B009-2 — `Collections/_unused/SlotList.cs` Dispose (:43–:47), PH_B009×5** — Terminal disposal here is externally serialized by contract (the type requires exclusive caller ownership at Dispose). The lock objects themselves (`_storage`, `_freeSlots`) are the fields being cleared and nulled, so they cannot guard their own teardown; disposed instances are never reused, so no post-dispose deref occurs.

**P007 — Unused-cancellation-token, 5 verified-unsafe-to-forward sites, PH_P007×5** — Each site deliberately does NOT forward the caller's `CancellationToken`, and forwarding would break behavior:
- `DebuggerInfo.cs:88` — Dispose cancels then JOINS the worker via `_SyncWaitNoCancelExceptions`; forwarding the cancelled token would abort the join it is waiting on.
- `SimpleStorageManager.cs:209` and `:217` — the write/reload debouncer `Timer`-callback lambdas are deliberately decoupled from any single caller's token; forwarding one caller's cancel would drop other callers' coalesced mutations.
- `DebuggableAsyncHelper.cs:58` — the cleanup continuation must be uncancellable, or the `CancellationTokenRegistration` leaks.
- `DebuggableTimeoutCancelTokenHelper.cs:170` — a shutdown-join, same rationale as `DebuggerInfo`.

**S032 — sync-throw in Task-returning method, PH_S032×4** — `UpdateAsync` (:285/:286) and `SetDataAsync` (:319/:320) throw synchronous argument/state guards (`ArgumentNullException`, not-initialized, disposed) from `Task`-returning methods. This is a documented public contract (the standard BCL convention, declared in the `<exception>` tags). These are live production paths; converting the throws to `Task.FromException` would silently change throw-timing for every caller.

**P008 — throw-OperationCanceledException-on-cancel, PH_P008×2** — `DebuggerInfo.cs:91` and `DebuggableTimeoutCancelTokenHelper.cs:72` are debug-helper worker loops that exit GRACEFULLY on cancellation by design. Their only awaiter explicitly filters cancel exceptions (`_SyncWaitNoCancelExceptions`), so throwing `OperationCanceledException` on cancel would be discarded anyway — the graceful exit is the correct shape.

**S007 — start-thread-in-constructor, PH_S007×1** — `DedicatedThreads.cs:46` (`DedicatedThread.Start()` in the `DedicatedThreadTaskScheduler` ctor) is borrowed sample-code whose `TaskScheduler` starts its worker in the constructor. A factory-start lifecycle redesign of borrowed code is disproportionate to the benefit.

**P006 — prefer-`lock`-over-Monitor, PH_P006×1** — `FrameDataChannel.cs:95` uses `Monitor.Enter`/`Monitor.TryEnter` across a shared try/finally body: `TryEnter` in the debug (fail-loud) branch, `Enter` in the normal branch. This conditional-acquire-vs-blocking-acquire over one body legitimately requires the `Monitor` API; the `lock` statement cannot express it.

**RCS1194 — implement standard exception constructors, RCS1194×1** — `Problem.cs:101` (`public class ProblemException : LoLoException`) intentionally exposes only the `ProblemException(Problem problem)` constructor. The type deliberately restricts construction to structured `Problem` values rather than allowing the free-text/serialization exception ctors the rule wants.

**CS0618 — obsolete-member use, CS0618×1** — `_allocaton.cs:2232`: `Chunk<TComponent>._GLOBAL_LOOKUP` uses the `[Obsolete]` `ResizableArray` from Bcl.Core. Replacing it requires migrating the ECS hot-path chunk store off `.Span`/`.GetOrSet`/`.FreeSlot` — behavior-risky, deliberately deferred. (This file lives under `_scratch/` — the folder name is misleading; the code is PROVEN LIVE, a rename candidate, not a deletion candidate.)

**CA2255 — `[ModuleInitializer]` in application code, CA2255×1** — `_Initialize_SerializationHelper_ObjectConverters.cs:18`: the `[ModuleInitializer]` intentionally auto-registers serialization converters (the `ObjConverter` set) at module load. A redesign to explicit initialization is out of scope for the warning drive.

---

## (d) Related contract decisions log

Decisions from the warnings-to-zero drive that shape the package/analyzer contracts (recorded here so consumers and future maintainers see the *why*):

- **NN_R003 id collision → ContextAwareTask reassigned `NN_R010`.** `NullMaybeValueAnalyzer` keeps `NN_R003` (its registry/`.editorconfig`/`AnalyzerReleases` identity). The first-party `Novaleaf.VibeOverwatch.Shared/.editorconfig` suppresses BOTH ids to preserve prior observable behavior. **OPEN decision:** whether to re-enable `NN_R010` (ContextAwareTask) in that `.editorconfig` — deferred, not resolved.
- **RS1038 codefix split — impl / CodeFixes / Package triple.** Applied to `NotNot.Analyzers` and mirrored to `NotNot.GodotNet.SourceGen`. Consumers that want IDE quick-fixes need a SECOND `OutputItemType=Analyzer` `ProjectReference` to the `.CodeFixes` project (the analyzer reference alone gives diagnostics but not fixes). See `NotNot.Analyzers/AGENTS.MD` § *Analyzer packaging pattern (RS1038 triple split)*.
- **GodotNet package dropped 3 dead nuspec dependency entries** (`Microsoft.Extensions.Configuration.*`) — zero source usages.
- **`_graveyard/` deleted.** `_scratch/` and the remaining `Collections/_unused/` files are **PROVEN LIVE** (reference-enumerated) — the folder names are misleading. They are rename candidates, NOT deletion candidates.
- **IDE quick-fix smoke after the split (VS + Rider):** automated proxies PASSED (nupkg content layout, MEF/`[ExportCodeFixProvider]` attributes, 398 unit tests). Interactive IDE quick-fix verification remains PENDING a human.
- **Out-of-solution MSB3277 (not one of the 26):** an assembly-version-unification advisory (`System.IO.Pipelines` / `Microsoft.Bcl.AsyncInterfaces` 9-vs-10) on the non-shipped intermediate `NotNot.GodotNet.SourceGen.CodeFixes`. Counterfactually proven pre-existing; the shipped nupkg was verified correct. Producer-side dependency alignment noted as a follow-up.
