@Templating.VowVision.md

# Templating (NotNot.Bcl.Core)

The `%key%` template grammar, namespace `NotNot.Templating`. One hand-written scanner resolves every template
in the repo — callers select a dialect, they do not re-encode the syntax. Pure `System.*`: no ASP.NET Core, no
Blazor, no regex ([../../AGENTS.MD](../../AGENTS.MD) §Dependency Rules).

## Folder Contents

| File | Purpose |
|---|---|
| `TemplateString.cs` | The engine: `Template` · `Values` · `OnResolveKey` · `EnableConditionalGroups`, and `Resolve()`. |
| `TemplateResolveResult.cs` | The result: `Text` · `UnresolvedKeys`. |

## Surface map

- **Engine** — `new TemplateString { Template, Values?, OnResolveKey?, EnableConditionalGroups? }.Resolve()`.
  Resolution order: `OnResolveKey` → `Values` → leave literal. `Values` is held by reference, so the caller's
  comparer governs every lookup. `Resolve()` throws on a null `Template`.
- **Result** — `Text` (declined keys are left literal, so it is never lossy) plus `UnresolvedKeys` (the keys
  the resolver DECLINED — callback returned null AND the dictionary missed — distinct, source-ordered, empty
  when all resolved). Three consumer roles come out of the one method: RESOLVE reads `Text`; VALIDATE gates on
  `UnresolvedKeys.Count > 0`; ENUMERATE resolves with NO resolver, where every key is declined and the declined
  set IS the template's key set.
- **Dialect flag** — `EnableConditionalGroups`, default `false`, chosen per call.
- **Sugar** — `string._ResolveTemplate(Func<string,string?>)` in
  [../zz_Extensions/zz_Extensions_String.cs](../zz_Extensions/zz_Extensions_String.cs): plain dialect, text
  only. Needing the conditional dialect or the declined-key report → construct `TemplateString` directly.

## The two dialects

| Input | Plain (default) | Conditional (`EnableConditionalGroups = true`) |
|---|---|---|
| `%key%` | substituted; grammar `%([a-zA-Z_][a-zA-Z0-9_:]*)%`, case-sensitive | same |
| `?{…}` | ordinary characters, byte-identical passthrough | group: emitted only when every contained key resolves non-blank; drops in full, delimiters included, otherwise |
| `\?{` `\}` | ordinary characters | unescape to `?{` and `}`; every OTHER backslash passes through |
| `?{no key}` | ordinary characters | body kept, delimiters consumed |
| `?{` unterminated | ordinary characters | groups do not nest (first `}` closes); an unterminated `?{` leaves the remainder literal |

In BOTH dialects a substituted value is emitted byte-for-byte: single-pass, never re-scanned for keys, groups,
or escapes, and never contributing to `UnresolvedKeys`.

## Fence

[../../../NotNot.Bcl.Core.Tests/TemplateStringTests.cs](../../../NotNot.Bcl.Core.Tests/TemplateStringTests.cs)
pins the grammar, the dialect default, comparer preservation, two-phase resolution, and the declined-key
report. Change any behavior above → the pin moves with it.

## Related Topics

- [../zz_Extensions/zz_Extensions_String.cs](../zz_Extensions/zz_Extensions_String.cs) — the sugar; extension
  naming convention in [../../AGENTS.MD](../../AGENTS.MD) §Extensions.
- The NnDesign RichText feature (parent workspace, outside this repo) selects the conditional dialect for its
  Stage-A pre-pass and owns Stage-B markup rendering on top of this output.
