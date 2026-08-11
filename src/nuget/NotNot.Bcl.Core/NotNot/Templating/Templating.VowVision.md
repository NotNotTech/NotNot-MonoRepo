# Templating.VowVision

The `%key%` template mini-language for `NotNot.Bcl.Core` — namespace `NotNot.Templating`, folder `NotNot/Templating/`. ONE executable owner of the grammar, so a template written for one caller resolves identically for every other. Library-wide rules it inherits (the dependency boundary, the `zz_Extensions_{type}` / `_`-prefix extension convention) are NOT restated here — `NotNot.Bcl.Core/AGENTS.MD` owns them; this vision records only what this feature owns.

## Ownership map

- `TemplateString` — the single executable owner of the `%key%` grammar: substitution, `?{…}` conditional groups, and the `\?{` / `\}` escapes. [1 per resolve]
  - Two dialects, plain and conditional-group, selected per call. This feature owns both grammars; the SELECTION belongs to the caller.
- `TemplateResolveResult` — what one resolve yields: the resolved text together with the keys the resolver declined. [1 per resolve]
- `string._ResolveTemplate(Func<…>)` — the one sugar over the engine.
- NOT owned here: Stage-B markup rendering and the dialect SELECTION for rich text — both belong to the NnDesign RichText feature; and what any key MEANS, which every caller supplies.

## Functional intents

- The conditional dialect is a per-call capability defaulting OFF, so `?`, `{`, `}` and `\` are byte-inert on a plain surface — a Windows path or an authored brace survives untouched.
- Escape ownership is exactly `\?{` and `\}`, and only in the conditional dialect; every other backslash passes through.
- Resolution is single-pass and callback-first: a substituted value is emitted byte-for-byte, never re-scanned, and never reported as unresolved.
- `Values` is held BY REFERENCE and never copied, so a caller's comparer is authoritative.
- "Unresolved" means the resolver DECLINED the key, reported distinct and in source order; a key that resolved to a blank value is resolved, not declined.
- Declined keys are reported uniformly wherever they sit — including inside a conditional group that never terminates. The report follows the decline, not the position.
- A conditional group drops in full when a contained key is unresolved-or-blank, and the drop leaves the authored inter-group whitespace behind — a downstream consumer normalizes it.
- A null template throws.

## Planning Temp

- (PRE) [other] A future per-value transform applies at value-APPEND time — outside groups and inside group bodies — AFTER the blank/drop decision; placing it earlier would let a transform resurrect a group the blank check dropped. Core owns WHERE; the RichText feature owns WHAT. — evidence: `.vibeOverwatch/memory/20260810-1625-CONSIDER-tpl-grammar-unify.VowConsider.md` §Q2
