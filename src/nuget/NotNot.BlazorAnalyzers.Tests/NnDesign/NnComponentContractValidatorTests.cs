using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using FluentAssertions;
using NotNot.BlazorAnalyzers.NnDesign;
using Xunit;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// THE F5 CLOSURE for Phase-3.3 5B (component-contract schema-as-data). A schema describes what SHOULD be;
/// it does NOT by itself prove the live components match it. This suite is the DIFF/VALIDATION PASS:
/// <list type="number">
///   <item><see cref="Schema_IsInSync_With_Live_Producer"/> reads the REAL schema + the REAL 3 layout-stable
///         <c>.razor</c> files + the REAL <c>nn-design.css</c> off disk and asserts ZERO drift.</item>
///   <item>The <c>Drift_*</c> facts INJECT deliberate drift (a fake live Tier-A param; a removed schema
///         entry; a removed live slot; an unsupported <c>data-nn-fill</c> mode) and assert the pass CATCHES
///         it. "The schema exists" / "it compiles" is NOT the proof — the drift-catch is.</item>
/// </list>
/// All paths resolve relative to THIS test source file (<see cref="ThisFile"/> via
/// <see cref="CallerFilePathAttribute"/>), walking up to the parent-repo root so the cross-repo
/// (submodule test → parent-repo producer) read is deterministic regardless of CWD.
/// </summary>
public class NnComponentContractValidatorTests
{
    private static string ThisFile([CallerFilePath] string path = "") => path;

    /// <summary>
    /// The parent-repo root (the dir that contains both <c>src/private-proj/</c> (producer) and
    /// <c>src/external-repo/</c> (this submodule)). Walks up from this test source file until that layout
    /// is found.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = Path.GetDirectoryName(ThisFile())!;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src", "private-proj"))
                && Directory.Exists(Path.Combine(d.FullName, "src", "external-repo")))
            {
                return d.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            $"Could not locate the parent-repo root (containing src/private-proj + src/external-repo) above '{dir}'.");
    }

    private const string SchemaRelPath =
        "src/private-proj/NotNot.BlazorDesign/NnDesign/NnComponentContracts.NnContract.md";

    private const string CssRelPath =
        "src/private-proj/NotNot.BlazorDesign/wwwroot/nn-design.css";

    private static string ReadRepoFile(string relPath)
    {
        var full = Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(full).Should().BeTrue($"the producer artifact must exist at '{full}'");
        return File.ReadAllText(full);
    }

    /// <summary>
    /// Builds the live-surface map by reading each schema block's declared <c>razor:</c> path off disk and
    /// parsing it — the engine's real input. Returns the parsed schema + CSS modes alongside so the facts
    /// can run the full pass or splice drift in.
    /// </summary>
    private static (ImmutableArray<NnComponentContractValidator.ComponentSchema> schema,
                    Dictionary<string, NnComponentContractValidator.LiveComponentSurface> live,
                    ImmutableHashSet<string> cssModes)
        LoadRealInputs()
    {
        var schema = NnComponentContractValidator.ParseSchema(ReadRepoFile(SchemaRelPath));
        var live = new Dictionary<string, NnComponentContractValidator.LiveComponentSurface>();
        foreach (var block in schema)
        {
            var razor = ReadRepoFile(block.RazorPath);
            live[block.Component] = NnComponentContractValidator.ParseLiveSurface(razor);
        }
        var cssModes = NnComponentContractValidator.ScanCssFillModes(ReadRepoFile(CssRelPath));
        return (schema, live, cssModes);
    }

    // ── Sanity: the parse actually populated (guards a vacuous green) ─────────

    [Fact]
    public void Schema_Parses_The_Three_LayoutStable_Components()
    {
        var (schema, _, cssModes) = LoadRealInputs();

        schema.Select(b => b.Component).Should().BeEquivalentTo(
            new[] { "NnContentSection", "NnTabs", "NnTabPanel" },
            "this 5B increment covers exactly the three layout-stable components");

        // The CSS scan must find the real hooks (else the mode-support check would be vacuously satisfiable).
        cssModes.Should().Contain("container");
        cssModes.Should().Contain("viewport");
        cssModes.Should().Contain("scroll");
    }

    [Fact]
    public void LiveSurface_Parse_Finds_Real_Params_And_Slots()
    {
        var (_, live, _) = LoadRealInputs();

        // Spot-check the parse is real (not empty) — a representative Tier-A param + a slot per component.
        live["NnContentSection"].TierAParams.Should().Contain(new[] { "Density", "BodyLayout", "FixedHeight" });
        live["NnContentSection"].Slots.Should().Contain(new[] { "ChildContent", "FooterContent" });
        live["NnTabs"].TierAParams.Should().Contain(new[] { "FillPanels", "ActivePanelIndex" });
        live["NnTabPanel"].TierAParams.Should().Contain("Fill");
        live["NnTabPanel"].Slots.Should().Equal("ChildContent");

        // Tier-B *Style/*Class string params must NOT appear (the producer removed them; the parser also
        // excludes the shape) — guards against a future re-introduction sneaking into the Tier-A surface.
        live["NnContentSection"].TierAParams.Should().NotContain(p =>
            p.EndsWith("Style") || p.EndsWith("Class"));
    }

    // ── THE in-sync closure ───────────────────────────────────────────────────

    [Fact]
    public void Schema_IsInSync_With_Live_Producer()
    {
        var (schema, live, cssModes) = LoadRealInputs();

        var drift = NnComponentContractValidator.Diff(schema, live, cssModes);

        drift.Should().BeEmpty(
            "the 5B schema MUST match the live producer components + CSS; drift: "
            + string.Join(" | ", drift.Select(d => d.ToString())));
    }

    // ── DRIFT-CATCH proofs (the F5 acceptance — the pass must FAIL on drift) ──

    [Fact]
    public void Drift_LiveTierAParam_NotInSchema_IsCaught()
    {
        var (schema, live, cssModes) = LoadRealInputs();

        // Inject a deliberate live param the schema does not declare (simulates adding a [Parameter] to the
        // component without updating the schema).
        var driftedLive = new Dictionary<string, NnComponentContractValidator.LiveComponentSurface>(live)
        {
            ["NnTabs"] = WithExtraParam(live["NnTabs"], "RogueAppearanceKnob"),
        };

        var drift = NnComponentContractValidator.Diff(schema, driftedLive, cssModes);

        drift.Should().Contain(d =>
            d.Component == "NnTabs"
            && d.Kind == NnComponentContractValidator.DriftKind.LiveTierAParamMissingFromSchema
            && d.Detail.Contains("RogueAppearanceKnob"));
    }

    [Fact]
    public void Drift_SchemaParam_NotOnComponent_IsCaught()
    {
        var (schema, live, cssModes) = LoadRealInputs();

        // Remove a real param from the LIVE surface (simulates deleting a [Parameter] from the component
        // while it still lingers in the schema).
        var driftedLive = new Dictionary<string, NnComponentContractValidator.LiveComponentSurface>(live)
        {
            ["NnContentSection"] = WithoutParam(live["NnContentSection"], "Density"),
        };

        var drift = NnComponentContractValidator.Diff(schema, driftedLive, cssModes);

        drift.Should().Contain(d =>
            d.Component == "NnContentSection"
            && d.Kind == NnComponentContractValidator.DriftKind.SchemaParamMissingFromComponent
            && d.Detail.Contains("Density"));
    }

    [Fact]
    public void Drift_LiveSlot_NotInSchema_IsCaught()
    {
        var (schema, live, cssModes) = LoadRealInputs();

        var driftedLive = new Dictionary<string, NnComponentContractValidator.LiveComponentSurface>(live)
        {
            ["NnTabPanel"] = WithExtraSlot(live["NnTabPanel"], "HeaderContent"),
        };

        var drift = NnComponentContractValidator.Diff(schema, driftedLive, cssModes);

        drift.Should().Contain(d =>
            d.Component == "NnTabPanel"
            && d.Kind == NnComponentContractValidator.DriftKind.LiveSlotMissingFromSchema
            && d.Detail.Contains("HeaderContent"));
    }

    [Fact]
    public void Drift_SchemaDeclaresFillMode_WithNoCssHook_IsCaught()
    {
        var (schema, live, _) = LoadRealInputs();

        // Empty the CSS-supported mode set (simulates declaring a data-nn-fill mode the CSS does not back —
        // e.g. a schema entry 'viewport' with the matching :where([data-nn-fill="viewport"]) hook deleted).
        var noCssModes = ImmutableHashSet<string>.Empty;

        var drift = NnComponentContractValidator.Diff(schema, live, noCssModes);

        // NnContentSection's 'container' + 'viewport' are non-content, non-exempt → must be flagged.
        // 'scroll' is (css-exempt) on NnContentSection → must NOT be flagged for missing CSS.
        drift.Should().Contain(d =>
            d.Component == "NnContentSection"
            && d.Kind == NnComponentContractValidator.DriftKind.DeclaredFillModeUnsupportedByCss
            && d.Detail.Contains("container"));
        drift.Should().Contain(d =>
            d.Component == "NnContentSection"
            && d.Kind == NnComponentContractValidator.DriftKind.DeclaredFillModeUnsupportedByCss
            && d.Detail.Contains("viewport"));
        drift.Should().NotContain(d =>
            d.Component == "NnContentSection"
            && d.Kind == NnComponentContractValidator.DriftKind.DeclaredFillModeUnsupportedByCss
            && d.Detail.Contains("'scroll'"));
    }

    [Fact]
    public void CssExemptMode_DoesNotFalsePositive_WhenCssAbsent()
    {
        // Isolates the (css-exempt) carve-out: a schema with ONE exempt mode + no CSS backing must produce
        // ZERO fill-mode drift. Guards against the exemption silently eroding into a false failure.
        var schema = NnComponentContractValidator.ParseSchema("""
            [component] Probe
            razor: x/Probe.razor
            tier-a: A
            data-nn-fill: content, scroll (css-exempt)
            slots: none
            """);
        var live = new Dictionary<string, NnComponentContractValidator.LiveComponentSurface>
        {
            ["Probe"] = new(ImmutableArray.Create("A"), ImmutableArray<string>.Empty),
        };

        var drift = NnComponentContractValidator.Diff(schema, live, ImmutableHashSet<string>.Empty);

        drift.Should().NotContain(d =>
            d.Kind == NnComponentContractValidator.DriftKind.DeclaredFillModeUnsupportedByCss);
    }

    // ── Surface-mutation helpers (in-memory drift injection) ─────────────────

    private static NnComponentContractValidator.LiveComponentSurface WithExtraParam(
        NnComponentContractValidator.LiveComponentSurface s, string param) =>
        new(s.TierAParams.Add(param), s.Slots);

    private static NnComponentContractValidator.LiveComponentSurface WithoutParam(
        NnComponentContractValidator.LiveComponentSurface s, string param) =>
        new(s.TierAParams.Where(p => p != param).ToImmutableArray(), s.Slots);

    private static NnComponentContractValidator.LiveComponentSurface WithExtraSlot(
        NnComponentContractValidator.LiveComponentSurface s, string slot) =>
        new(s.TierAParams.Add(slot), s.Slots.Add(slot));
}
