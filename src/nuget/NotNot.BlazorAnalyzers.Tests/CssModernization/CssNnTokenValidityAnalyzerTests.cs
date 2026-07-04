using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.BlazorAnalyzers.CssModernization;

namespace NotNot.BlazorAnalyzers.Tests.CssModernization;

/// <summary>
/// Tests for <see cref="CssNnTokenValidityAnalyzer"/> (NNB_CSS013) — an unknown, BARE
/// <c>var(--nns-*)</c> design-token reference (a bare <c>var()</c>, no fallback, to a token declared in
/// neither authority CSS file silently resolves to nothing). Each test registers the fixture CSS as an
/// AdditionalFile; POSITIVE fixtures also register a realistic <c>nn-design.css</c>/<c>nn-colors.css</c>
/// authority slice so the producer-scoped authority-presence gate (G2) opens.
/// <para>
/// Behavioral claims pinned (one <c>[Fact]</c> each): the F1 bare-vs-fallback discriminator (bare fires /
/// fallback-guarded skips); the G2 authority-presence gate (inert when no authority file is registered);
/// the Pass-1 harvest of BOTH authority declaration forms (<c>:root</c> value AND <c>@property</c>-only);
/// the two-pass ordering invariant (the valid set is built across ALL authority files before any
/// reference is checked, regardless of AdditionalFiles order); the G4 CSS-surface restriction
/// (<c>.razor</c>/<c>.razor.cs</c> are never reference-checked); comment masking; the per-file opt-out
/// marker; the <c>CssAnalyzerEnabled=false</c> kill-switch; and vendor/producer-inclusive posture.
/// </para>
/// </summary>
public class CssNnTokenValidityAnalyzerTests
{
    // ── Authority slices + paths ─────────────────────────────────────────────

    private const string DesignPath = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-design.css";
    private const string ColorsPath = "/TestProject/NotNot.BlazorDesign/wwwroot/nn-colors.css";
    private const string AppCssPath = "/TestProject/NotNot.BlazorDesign/wwwroot/css/app.css";
    private const string ScopedCssPath = "/TestProject/Components/Widget.razor.css";
    private const string RazorPath = "/TestProject/Components/Widget.razor";
    private const string VendorCssPath = "/TestProject/wwwroot/lib/thirdparty.css";

    /// <summary>Realistic <c>nn-design.css</c> authority slice — <c>:root</c> value-form declarations.</summary>
    private const string DesignRootSlice =
@":root {
    --nns-spacing-md: 16px;
    --nns-spacing-xs: 4px;
}";

    /// <summary>Realistic <c>nn-colors.css</c> authority slice — a <c>:root</c> value-form declaration.</summary>
    private const string ColorsRootSlice =
@":root {
    --nns-bg-info: #1976D2;
}";

    /// <summary>
    /// <c>nn-colors.css</c> authority slice declaring a real brand anchor via the <c>@property</c>
    /// registration form ONLY (no <c>:root</c> value) — harvested through the <c>@property</c> branch.
    /// </summary>
    private const string ColorsAnchorPropertySlice =
@"@property --nns-anchor-info { syntax: '<color>'; inherits: true; initial-value: #1976D2; }";

    /// <summary>
    /// <c>nn-design.css</c> authority slice declaring a token via <c>@property</c> ONLY (no <c>:root</c>
    /// value form) — the dedicated proof that the <c>@property</c> harvest branch admits the token; a
    /// broken <c>@property</c> regex would false-positive on the bare reference below.
    /// </summary>
    private const string DesignGhostPropertySlice =
@"@property --nns-ghost { syntax: '<length>'; inherits: true; initial-value: 0; }";

    /// <summary>Shared <c>CssAnalyzerEnabled=false</c> kill-switch globalconfig.</summary>
    private const string KillSwitchConfig =
@"
is_global = true
build_property.CssAnalyzerEnabled = false
";

    // ── Test helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the analyzer over the given AdditionalFiles (in the supplied order — order is load-bearing for
    /// the two-pass ordering fixture), optionally under a globalconfig, asserting the expected diagnostics.
    /// </summary>
    private static async Task VerifyAsync(
        (string path, string content)[] files,
        DiagnosticResult[]? expected = null,
        string? globalConfig = null)
    {
        var test = new CSharpAnalyzerTest<CssNnTokenValidityAnalyzer, DefaultVerifier>
        {
            TestCode = "class Placeholder { }"
        };
        foreach (var (path, content) in files)
            test.TestState.AdditionalFiles.Add((path, content));
        if (globalConfig != null)
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", globalConfig));
        if (expected?.Length > 0)
            test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Builds the expected NNB_CSS013 result at the bare-token span. Severity is DERIVED from
    /// <see cref="CssNnTokenValidityAnalyzer.RuleUnknownToken"/>'s <c>DefaultSeverity</c> — NOT a hardcoded
    /// <c>Warning</c> — so the later Warning→Error ratchet is a single-line descriptor flip that requires
    /// editing NO positive fixture. <paramref name="token"/> is diagnostic arg {0} (the <c>nns-*</c> name,
    /// excluding the leading <c>--</c>, exactly as the analyzer reports it).
    /// </summary>
    private static DiagnosticResult Diagnostic(
        string filePath, int line, int startColumn, int endColumn, string token)
    {
        return new DiagnosticResult(
                CssNnTokenValidityAnalyzer.DiagnosticId,
                CssNnTokenValidityAnalyzer.RuleUnknownToken.DefaultSeverity)
            .WithSpan(filePath, line, startColumn, line, endColumn)
            .WithArguments(token);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POSITIVE (WARNS) — bare unknown var(--nns-*) reference.
    // ═══════════════════════════════════════════════════════════════════════

    // 1 — bare var(--nns-typo) in a .css, authority present → fires at the token span, arg `nns-typo`.
    [Fact]
    public async Task F01_BareUnknown_InCss_AuthorityPresent_Warns()
    {
        var css = @".x {
color: var(--nns-typo);
}";
        // line 2 `color: var(--nns-typo);` → `nns-typo` at column 14, length 8 → end column 22.
        await VerifyAsync(
            new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) },
            new[] { Diagnostic(AppCssPath, 2, 14, 22, "nns-typo") });
    }

    // 6 — bare unknown ref in a .razor.css (authority present) → fires (scoped CSS is a Pass-2 surface).
    [Fact]
    public async Task F06_BareUnknown_InScopedRazorCss_Warns()
    {
        var css = @".widget {
color: var(--nns-typo);
}";
        await VerifyAsync(
            new[] { (DesignPath, DesignRootSlice), (ScopedCssPath, css) },
            new[] { Diagnostic(ScopedCssPath, 2, 14, 22, "nns-typo") });
    }

    // 12 — two DISTINCT bare unknown tokens in one file → two reports (multi-report).
    [Fact]
    public async Task F12_TwoDistinctBareUnknown_SameFile_WarnsTwice()
    {
        var css = @".a {
color: var(--nns-typo);
background: var(--nns-nope);
}";
        // line 2 `nns-typo` col 14-22; line 3 `background: var(--nns-nope);` → `nns-nope` col 19-27.
        await VerifyAsync(
            new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) },
            new[]
            {
                Diagnostic(AppCssPath, 2, 14, 22, "nns-typo"),
                Diagnostic(AppCssPath, 3, 19, 27, "nns-nope"),
            });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NEGATIVE (SILENT).
    // ═══════════════════════════════════════════════════════════════════════

    // 2 — fallback-guarded var(--nns-typo, 1rem) (unknown, but has a fallback) → SILENT (F1: delim `,`).
    [Fact]
    public async Task F02_FallbackGuardedUnknown_Silent()
    {
        var css = @".x {
color: var(--nns-typo, 1rem);
}";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) });
    }

    // 3 — authority-ABSENT: a consumer .razor.css with bare unknown refs, NO authority file → SILENT (G2).
    [Fact]
    public async Task F03_AuthorityAbsent_Silent()
    {
        var css = @".widget {
color: var(--nns-anything);
background: var(--nns-else);
}";
        await VerifyAsync(new[] { (ScopedCssPath, css) });
    }

    // 4 — bare var(--nns-typo) inside a CSS /* */ comment → SILENT (comment masking).
    [Fact]
    public async Task F04_CommentedReference_Silent()
    {
        var css = @".x {
/* color: var(--nns-typo); */
color: red;
}";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) });
    }

    // 5 — token declared via :root { --nns-spacing-md: ...; } in nn-design.css, referenced bare → SILENT.
    [Fact]
    public async Task F05_KnownViaRootValue_Design_Silent()
    {
        var css = @".x {
margin: var(--nns-spacing-md);
}";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) });
    }

    // 7 — token declared via @property --nns-anchor-info {...} in nn-colors.css, referenced bare → SILENT.
    [Fact]
    public async Task F07_KnownViaPropertyForm_Colors_Silent()
    {
        var css = @".x {
color: var(--nns-anchor-info);
}";
        await VerifyAsync(new[] { (ColorsPath, ColorsAnchorPropertySlice), (AppCssPath, css) });
    }

    // 8 — real nn-colors.css :root token --nns-bg-info referenced bare → SILENT.
    [Fact]
    public async Task F08_KnownViaRootValue_Colors_Silent()
    {
        var css = @".x {
background: var(--nns-bg-info);
}";
        await VerifyAsync(new[] { (ColorsPath, ColorsRootSlice), (AppCssPath, css) });
    }

    // 9 — per-file opt-out marker present → SILENT even with a bare unknown ref.
    [Fact]
    public async Task F09_PerFileOptOut_Silent()
    {
        var css = @"/* nnb_css013:allow-unknown-token: runtime-injected by theme host */
.x {
color: var(--nns-typo);
}";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) });
    }

    // 10 — kill-switch (CssAnalyzerEnabled=false globalconfig) → SILENT.
    [Fact]
    public async Task F10_KillSwitch_Silent()
    {
        var css = @".x {
color: var(--nns-typo);
}";
        await VerifyAsync(
            new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) },
            expected: null,
            globalConfig: KillSwitchConfig);
    }

    // 11 — a bare unknown ref located in a .razor file → SILENT (Pass-2 checks .css/.razor.css only, G4).
    [Fact]
    public async Task F11_ReferenceInRazor_NotChecked_Silent()
    {
        var razor = @"<div style=""color: var(--nns-typo)"">hi</div>";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (RazorPath, razor) });
    }

    // 13 — two-pass ordering (F3): the referencing .razor.css is registered BEFORE the authority
    // nn-colors.css that declares the token → SILENT (the valid set is built across ALL authority files
    // before any reference is checked, regardless of AdditionalFiles order).
    [Fact]
    public async Task F13_TwoPassOrdering_ReferenceBeforeAuthority_Silent()
    {
        var scoped = @".widget {
background: var(--nns-bg-info);
}";
        // Referencing file FIRST, authority nn-colors.css SECOND.
        await VerifyAsync(new[] { (ScopedCssPath, scoped), (ColorsPath, ColorsRootSlice) });
    }

    // 14 — @property-ONLY harvest: authority declares --nns-ghost via @property ONLY (no :root form) +
    // a bare var(--nns-ghost) reference → SILENT (proves the @property harvest branch; a broken
    // @property regex would false-positive here).
    [Fact]
    public async Task F14_PropertyOnlyHarvest_Silent()
    {
        var css = @".x {
margin: var(--nns-ghost);
}";
        await VerifyAsync(new[] { (DesignPath, DesignGhostPropertySlice), (AppCssPath, css) });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // COVERAGE-GAP FIXTURES (F15–F18) — each pins a distinct Pass-1 / exemption
    // branch previously unasserted (peer-review F1, AC-R1.4 / AC-R1.5c).
    // ═══════════════════════════════════════════════════════════════════════

    // 15 — vendor-file skip: a bare unknown var(--nns-typo) in a VENDOR .css (path matches
    // CssConsumerExemptions.IsVendorFile via the /lib/ segment) → SILENT even with an authority file
    // present (vendor files are skipped in BOTH passes; they never fire and never harvest).
    [Fact]
    public async Task F15_VendorFile_BareUnknown_Silent()
    {
        var vendorCss = @".x {
color: var(--nns-typo);
}";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (VendorCssPath, vendorCss) });
    }

    // 16 — authority-file self-check (producer-INCLUSIVE; IsExceptedPath deliberately NOT applied):
    // a bare typo var(--nns-typno) INSIDE nn-design.css itself — whose /NotNot.BlazorDesign/ path WOULD
    // match IsExceptedPath — still WARNS, proving the reference-check covers the authority files' own
    // wiring. The same file declares a real token so ONLY the typo fires.
    [Fact]
    public async Task F16_AuthorityFileSelfCheck_ProducerInclusive_Warns()
    {
        var authorityCss = @":root {
    --nns-spacing-md: 16px;
}
.x {
color: var(--nns-typno);
}";
        // line 5 `color: var(--nns-typno);` → `nns-typno` at column 14, length 9 → end column 23.
        await VerifyAsync(
            new[] { (DesignPath, authorityCss) },
            new[] { Diagnostic(DesignPath, 5, 14, 23, "nns-typno") });
    }

    // 17 — .razor C#-string harvest (AC-R1.4, cross-language Pass-1 branch): a producer .razor declares
    // --nns-switch-accent via a C# string; a bare var(--nns-switch-accent) in the authority nn-design.css
    // resolves against that harvested token → SILENT. A regression on this branch would false-positive the
    // REAL bare var(--nns-switch-accent) inside the producer nn-design.css.
    [Fact]
    public async Task F17_RazorCSharpStringHarvest_Silent()
    {
        var razor = @"@code {
    private const string SwitchAccentCss = ""--nns-switch-accent: #4caf50;"";
}";
        var authorityCss = @":root {
    --nns-spacing-md: 16px;
}
.switch {
background: var(--nns-switch-accent);
}";
        await VerifyAsync(new[] { (DesignPath, authorityCss), (RazorPath, razor) });
    }

    // 18 — interpolated + malformed skip (AC-R1.5c): an interpolated var(--nns-color-{role}) (dynamic
    // marker) and a malformed var(--nns--double) (double hyphen) are both CAPTURED by the reference regex
    // yet SKIPPED (ContainsDynamicMarker / !WellFormedToken) → SILENT. Authority present so the analyzer is
    // ACTIVE — this pins the Pass-2 skip predicates, not the G2 inert gate.
    [Fact]
    public async Task F18_InterpolatedAndMalformed_Skipped_Silent()
    {
        var css = @".x {
color: var(--nns-color-{role});
width: var(--nns--double);
}";
        await VerifyAsync(new[] { (DesignPath, DesignRootSlice), (AppCssPath, css) });
    }
}
