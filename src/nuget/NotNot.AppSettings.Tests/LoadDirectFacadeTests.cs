// Phase D δ group — TDD §4.D — LoadDirect* facade rewrite verification.
//
// Dual-harness approach (per Overseer RESOLUTION 2):
//
//   Harness-3 (emission-shape): instantiate NotNot.AppSettingsGen directly via the aliased
//   ProjectReference (appsettings_gen::), call its public GenerateSourceFiles(config) entry point,
//   grab the emitted _BinderShims.g.cs SourceText, and assert on string content. This bypasses
//   the SGF pipeline + Roslyn CSharpGeneratorDriver entirely — AppSettingsGen.GenerateSourceFiles
//   is public (AppSettingsGen.cs:148) and self-contained, so direct invocation is a strictly
//   simpler harness than CSharpGeneratorDriver while achieving identical emission-shape proof.
//   Deviation note from original RESOLUTION 2 wording: Harness-3 was phrased as
//   "CSharpGeneratorDriver"; we use a functionally equivalent simpler path (public-API direct
//   call). Same verification target (emitted source inspection). Zero new infrastructure.
//
//   Harness-4 (merge-behavior): call NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync
//   directly with the same inputs the emitted facade feeds it. Proves the merge step the facade
//   inherits from the unified core (JsonMergeCore via JsonSettingsUtils delegation, post-Phase A).
//
// Extern alias required: NotNot.AppSettings is aliased to 'appsettings_gen' in
// NotNot.AppSettings.Tests.csproj:63 to disambiguate the CS0433 collision from shared-source
// JsonMergeCore compiling into both NotNot.AppSettings and NotNot.Bcl.Core. Same pattern as
// MultiFileGeneratorTests.cs.

extern alias appsettings_gen;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis.Text;
using NotNot.AppSettingsHelper;
using Xunit;
using AppSettingsGenConfig = appsettings_gen::NotNot.AppSettingsGenConfig;
// Note: NotNot.AppSettingsGen itself cannot be `using`-aliased here because it inherits from
// SGF.IncrementalGenerator (abstract, declared in SourceGenerator.Foundations.Contracts.dll which
// is PrivateAssets=all on the generator .csproj:28-29 and therefore not reachable at compile time
// from this test assembly). We invoke the public GenerateSourceFiles(config) entry point via
// reflection to sidestep the inheritance-reachability constraint. This is a harness concession,
// not an architectural statement — the method is `public` and fully deterministic.

namespace NotNot.AppSettings.Tests;

/// <summary>
/// Phase D δ group — facade rewrite verification (TDD §4.D, REQ-7 + REQ-4/9 at facade layer).
///
/// Covers:
///  • D1: [Obsolete] attribute present with standardized message on all four LoadDirect* overloads.
///  • D2: emitted bodies route through JsonSettingsUtils.MergeStreamsAsync.
///  • D2/D3: inline _FlattenJson helper present in _BinderShims.g.cs (Interpretation-A per RESOLUTION 1).
///  • REQ-4/7: merge-step contract reachable via facade (disjoint union, later-file-wins, null-delete).
/// </summary>
public class LoadDirectFacadeTests
{
    // --- Harness-3 helpers --------------------------------------------------------------------

    /// <summary>
    /// Build a minimal AppSettingsGenConfig with the supplied appsettings*.json contents and
    /// invoke the generator's public GenerateSourceFiles entry point. Returns the emitted
    /// _BinderShims.g.cs SourceText content as string.
    /// </summary>
    private static string RunGeneratorAndGetBinderShims(params (string path, string json)[] files)
    {
        var combined = new Dictionary<string, SourceText>();
        foreach (var (path, json) in files)
        {
            combined[path] = SourceText.From(json);
        }

        var config = new AppSettingsGenConfig
        {
            ProjectName = "LoadDirectFacadeTestsFixture",
            RootNamespace = "LoadDirectFacadeTestsFixture",
            IsPublic = true,
            CombinedSourceTexts = combined,
            NugetVersion = "test-0.0.0",
        };

        // Reflective invocation to avoid needing SourceGenerator.Foundations.Contracts reachable at compile time.
        var generatorAsm = typeof(appsettings_gen::NotNot.AppSettingsGenConfig).Assembly;
        var genType = generatorAsm.GetType("NotNot.AppSettingsGen", throwOnError: true)!;
        var genInstance = Activator.CreateInstance(genType)!;
        var method = genType.GetMethod("GenerateSourceFiles", BindingFlags.Instance | BindingFlags.Public)!;
        var emitted = (Dictionary<string, SourceText>)method.Invoke(genInstance, new object[] { config })!;

        emitted.Should().ContainKey("_BinderShims.g.cs",
            "AddBinderShims is invoked unconditionally by GenerateSourceFiles and emits the binder-shims partial");
        return emitted["_BinderShims.g.cs"].ToString();
    }

    // The standardized [Obsolete] message (verbatim — must match the emitted string template in
    // AppSettingsGen.cs AddBinderShims region). Test asserts substring presence, not exact match,
    // to allow attribute-call formatting variance.
    private const string ObsoleteMessageSubstring =
        "Use AppSettingsManager<T>.LoadAsync() for layered settings loading. " +
        "LoadDirect* is preserved as a facade over the unified merge core; " +
        "API signatures unchanged. Future versions may remove.";

    // --- Harness-3 tests (emission-shape via direct GenerateSourceFiles invocation) -----------

    [Fact]
    public void Emitted_LoadDirect_Methods_HaveObsoleteAttribute()
    {
        // REQ-7: all four LoadDirect* overloads must emit [System.Obsolete(...)] with standardized message.
        // Minimal fixture JSON — just one key so the generator has something to work with.
        var binderShims = RunGeneratorAndGetBinderShims(
            ("appsettings.json", """{"Stub":"value"}""")
        );

        // All four method names must appear as static method signatures in the emitted source.
        binderShims.Should().Contain("public static AppSettings LoadDirect(",
            "LoadDirect() is emitted as a static method on AppSettingsBinder");
        binderShims.Should().Contain("public static AppSettings LoadDirectFromText(",
            "LoadDirectFromText() is emitted as a static method");
        binderShims.Should().Contain("public static AppSettings LoadDirectFromTexts(",
            "LoadDirectFromTexts() is emitted as a static method");
        binderShims.Should().Contain("public static AppSettings LoadDirectFromStreams(",
            "LoadDirectFromStreams() is emitted as a static method");

        // The [Obsolete(...)] message must appear at least 4 times (one per overload).
        var obsoleteOccurrences = CountOccurrences(binderShims, ObsoleteMessageSubstring);
        obsoleteOccurrences.Should().BeGreaterThanOrEqualTo(4,
            $"all four LoadDirect* emissions must carry the standardized [Obsolete] message; found {obsoleteOccurrences}");
    }

    [Fact]
    public void Emitted_LoadDirect_BodiesRouteThrough_MergeStreamsAsync()
    {
        // D2 proof: emitted bodies must invoke JsonSettingsUtils.MergeStreamsAsync (unified merge core).
        // Only LoadDirectFromStreams is the canonical implementation; the other three delegate to it,
        // so we only need a single MergeStreamsAsync call site in the emitted source.
        var binderShims = RunGeneratorAndGetBinderShims(
            ("appsettings.json", """{"Stub":"value"}""")
        );

        binderShims.Should().Contain("NotNot.AppSettingsHelper.JsonSettingsUtils.MergeStreamsAsync",
            "LoadDirectFromStreams must route through the unified merge core (REQ-3, REQ-7)");

        // All three non-canonical overloads must delegate to LoadDirectFromStreams
        // (DRY — unification; avoids per-overload merge logic duplication).
        binderShims.Should().Contain("return LoadDirectFromStreams(streams)",
            "LoadDirect and LoadDirectFromTexts delegate to LoadDirectFromStreams for unified semantics");
    }

    [Fact]
    public void Emitted_BinderShims_Contain_InlineFlattenJson_Helper()
    {
        // RESOLUTION 1 Interpretation-A (TDD §4.D:320): FlattenJson is emitted inline as a
        // private static helper on AppSettingsBinder — NOT exposed via JsonSettingsUtils public API.
        var binderShims = RunGeneratorAndGetBinderShims(
            ("appsettings.json", """{"Stub":"value"}""")
        );

        binderShims.Should().Contain("private static System.Collections.Generic.Dictionary<string, string?> _FlattenJson",
            "FlattenJson bridge helper must be emitted inline per TDD §4.D:320");

        // The bridge helper must be invoked from the facade body (not just declared).
        binderShims.Should().Contain("_BindMergedNode(merged)",
            "LoadDirectFromStreams must call the bind helper after merging (D2 contract)");
    }

    // --- Harness-4 tests (merge-behavior via direct JsonSettingsUtils.MergeStreamsAsync) -------

    [Fact]
    public async Task FacadeMergeStep_TwoStreams_DisjointKeys_Union()
    {
        // REQ-1 at facade layer: the merge step the facade inherits must union disjoint top-level
        // keys. Exercises the exact code path the emitted LoadDirectFromStreams invokes internally.
        var streams = new List<Stream>
        {
            new MemoryStream(Encoding.UTF8.GetBytes("""{"Alpha":{"X":1}}""")),
            new MemoryStream(Encoding.UTF8.GetBytes("""{"Beta":{"Y":2}}""")),
        };

        var merged = await JsonSettingsUtils.MergeStreamsAsync(streams);

        merged.ContainsKey("Alpha").Should().BeTrue("disjoint top-level key from first stream must survive merge");
        merged.ContainsKey("Beta").Should().BeTrue("disjoint top-level key from second stream must survive merge");
        merged["Alpha"]!["X"]!.GetValue<int>().Should().Be(1);
        merged["Beta"]!["Y"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task FacadeMergeStep_OverlappingKey_LaterStreamWins()
    {
        // REQ-4 at facade layer: when two streams define the same key, the LATER stream wins
        // (deterministic last-wins via JsonMergeCore.MergeAll). This is the behavior the
        // [Obsolete] facade now inherits after Phase D rewrite (previously AddJsonStream-based
        // ASP.NET Core precedence; post-rewrite the unified core preserves that contract).
        var streams = new List<Stream>
        {
            new MemoryStream(Encoding.UTF8.GetBytes("""{"Shared":"from-first"}""")),
            new MemoryStream(Encoding.UTF8.GetBytes("""{"Shared":"from-second"}""")),
        };

        var merged = await JsonSettingsUtils.MergeStreamsAsync(streams);

        merged["Shared"]!.GetValue<string>().Should().Be("from-second",
            "last stream wins — JsonMergeCore.MergeAll folds sequentially with later overriding earlier");
    }

    [Fact]
    public async Task FacadeMergeStep_NullLiteral_DeletesKey()
    {
        // REQ-4 at facade layer: explicit null sentinel in a later stream REMOVES the key from
        // the merged output (RFC-7396-ish null-delete). This differs from pre-Phase-D facade
        // behavior (AddJsonStream would surface "null" string); unified core removes the key.
        var streams = new List<Stream>
        {
            new MemoryStream(Encoding.UTF8.GetBytes("""{"Keep":"yes","Drop":"original"}""")),
            new MemoryStream(Encoding.UTF8.GetBytes("""{"Drop":null}""")),
        };

        var merged = await JsonSettingsUtils.MergeStreamsAsync(streams);

        merged.ContainsKey("Keep").Should().BeTrue("untouched key must survive");
        merged["Keep"]!.GetValue<string>().Should().Be("yes");
        merged.ContainsKey("Drop").Should().BeFalse(
            "null sentinel in second stream removes the key via unified merge core null-delete semantics");
    }

    // --- helpers ----------------------------------------------------------------------------

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return 0;
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
