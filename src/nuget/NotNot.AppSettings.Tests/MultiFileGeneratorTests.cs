// extern alias required because NotNot.Bcl.Core link-compiles JsonMergeCore.cs as shared-source
// (Bcl.Core.csproj:112) and NotNot.AppSettings native-compiles the same file. The test csproj
// routes the AppSettings assembly behind Aliases="appsettings_gen" (see NotNot.AppSettings.Tests.csproj:63)
// so that JsonMergeCore remains default-aliased via Bcl.Core (keeps existing JsonMergeCoreTests.cs
// working) and JsonMerger is pulled explicitly via the alias here.
extern alias appsettings_gen;

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using JsonMerger = appsettings_gen::NotNot.AppSettingsInternal.JsonMerger;

namespace NotNot.AppSettings.Tests;

/// <summary>
/// Generator-side multi-file merge tests targeting the <see cref="JsonMerger.MergeJsonFiles"/>
/// surface (Dictionary&lt;string, SourceText&gt; → Dictionary&lt;string, JsonElement&gt;).
///
/// Phase C γ group — TDD §4.C. Proves REQ-1 (deterministic ordinal-path ordering),
/// REQ-4 (RFC-7396-ish null-delete + array-replace contract), REQ-9 (single-file regression).
///
/// Reachability: test project has an aliased ProjectReference to NotNot.AppSettings.csproj
/// (tests.csproj:63, Aliases="appsettings_gen"). InternalsVisibleTo("NotNot.AppSettings.Tests")
/// at AppSettingsGen.cs:14 opens internal JsonMerger to this assembly.
/// </summary>
public class MultiFileGeneratorTests
{
    // --- Helpers ----------------------------------------------------------------

    private static SourceText Text(string json) => SourceText.From(json);

    private static Dictionary<string, SourceText> Files(params (string path, string json)[] entries)
    {
        var dict = new Dictionary<string, SourceText>();
        foreach (var (path, json) in entries)
        {
            dict[path] = Text(json);
        }
        return dict;
    }

    // --- REQ-1: union + deterministic ordinal-path winner -----------------------

    [Fact]
    public void MultiFile_NonOverlappingSections_Union()
    {
        var files = Files(
            ("appsettings.a.json", """{"Alpha":{"X":1}}"""),
            ("appsettings.b.json", """{"Beta":{"Y":2}}""")
        );

        var merged = JsonMerger.MergeJsonFiles(files);

        merged.Should().ContainKey("Alpha", "disjoint top-level key from file A must survive merge");
        merged.Should().ContainKey("Beta", "disjoint top-level key from file B must survive merge");
        merged["Alpha"].GetProperty("X").GetInt32().Should().Be(1);
        merged["Beta"].GetProperty("Y").GetInt32().Should().Be(2);
    }

    [Fact]
    public void MultiFile_OverlappingKey_DeterministicWinnerByPath()
    {
        // Ordinal-sorted path order: "appsettings.a.json" < "appsettings.z.json".
        // MergeJsonFiles folds via MergeAll in ordinal-path order; later fold-in wins.
        // Therefore the ordinal-LAST path ("z") must win for conflicting keys.
        var files = Files(
            ("appsettings.a.json", """{"Shared":"from-a"}"""),
            ("appsettings.z.json", """{"Shared":"from-z"}""")
        );

        var merged = JsonMerger.MergeJsonFiles(files);

        merged["Shared"].GetString().Should().Be("from-z",
            "ordinal-last path wins deterministically — JsonMerger.cs:39 sorts by path ordinal before fold");
    }

    [Fact]
    public void MultiFile_DictionaryInputOrder_DoesNotAffectOutput()
    {
        // Same two files inserted in two different iteration orders into the dict.
        // Ordinal path sort at JsonMerger.cs:39 must cancel any dict-iteration nondeterminism.
        var forward = new Dictionary<string, SourceText>
        {
            ["appsettings.a.json"] = Text("""{"Key":"from-a","OnlyA":1}"""),
            ["appsettings.z.json"] = Text("""{"Key":"from-z","OnlyZ":9}"""),
        };
        var reversed = new Dictionary<string, SourceText>
        {
            ["appsettings.z.json"] = Text("""{"Key":"from-z","OnlyZ":9}"""),
            ["appsettings.a.json"] = Text("""{"Key":"from-a","OnlyA":1}"""),
        };

        var mergedForward = JsonMerger.MergeJsonFiles(forward);
        var mergedReversed = JsonMerger.MergeJsonFiles(reversed);

        // Compare by serialized form to normalize JsonElement equality.
        JsonSerializer.Serialize(mergedReversed)
            .Should().Be(JsonSerializer.Serialize(mergedForward),
                "dict insertion order must not affect merge output — ordinal path sort is the only ordering source");
        mergedForward["Key"].GetString().Should().Be("from-z", "ordinal-last path 'z' wins regardless of dict order");
    }

    // --- REQ-4: null-delete + array-replace (contract delta from prior behavior) --

    [Fact]
    public void MultiFile_NullLiteral_DeletesKey()
    {
        // Ordinal path: "appsettings.a.json" < "appsettings.b.json".
        // File B's null sentinel on "K" must remove the key per JsonMergeCore null-delete semantics.
        var files = Files(
            ("appsettings.a.json", """{"K":"v","Keep":"yes"}"""),
            ("appsettings.b.json", """{"K":null}""")
        );

        var merged = JsonMerger.MergeJsonFiles(files);

        merged.Should().NotContainKey("K",
            "null sentinel in later file must remove the key (RFC-7396-ish), not set it to null");
        merged.Should().ContainKey("Keep", "unrelated keys must survive null-delete of a sibling");
        merged["Keep"].GetString().Should().Be("yes");
    }

    [Fact]
    public void MultiFile_Arrays_AreReplacedNotConcatenated()
    {
        // CONTRACT DELTA from old behavior: arrays are now REPLACED (later wins), NOT concatenated.
        // Phase B1 routed JsonMerger.MergeJsonFiles through JsonMergeCore.MergeAll which replaces arrays.
        // This test IS the regression lock for that contract change (TDD §4.C, R2 risk row).
        var files = Files(
            ("appsettings.a.json", """{"Items":[1,2]}"""),
            ("appsettings.b.json", """{"Items":[3]}""")
        );

        var merged = JsonMerger.MergeJsonFiles(files);

        var items = merged["Items"];
        items.ValueKind.Should().Be(JsonValueKind.Array);
        items.GetArrayLength().Should().Be(1, "later file's array replaces earlier; no concatenation");
        items[0].GetInt32().Should().Be(3, "replacement value is verbatim from the later file");
    }

    [Fact]
    public void MultiFile_NestedObjects_DeepMergeDeterministically()
    {
        // Depth ≥2 merge: preserve unchanged leaf ("X"), override changed leaf ("Y"), add new leaf ("Z").
        var files = Files(
            ("appsettings.a.json", """{"Window":{"X":100,"Y":200}}"""),
            ("appsettings.b.json", """{"Window":{"Y":999,"Z":42}}""")
        );

        var merged = JsonMerger.MergeJsonFiles(files);

        var window = merged["Window"];
        window.GetProperty("X").GetInt32().Should().Be(100, "unchanged leaf from earlier file must survive deep merge");
        window.GetProperty("Y").GetInt32().Should().Be(999, "changed leaf from later file must win");
        window.GetProperty("Z").GetInt32().Should().Be(42, "added leaf from later file must appear");
    }

    // --- REQ-9: single-file regression protection --------------------------------

    [Fact]
    public void SingleFile_BehaviorUnchanged()
    {
        // One file in → round-trips through MergeAll + JsonSerializer with no structural changes.
        // Regression lock for REQ-9 (zero behavior change for single-file consumers).
        var files = Files(
            ("appsettings.json", """{"Name":"app","Port":8080,"Features":["a","b"]}""")
        );

        var merged = JsonMerger.MergeJsonFiles(files);

        merged.Should().HaveCount(3);
        merged["Name"].GetString().Should().Be("app");
        merged["Port"].GetInt32().Should().Be(8080);
        var features = merged["Features"];
        features.ValueKind.Should().Be(JsonValueKind.Array);
        features.GetArrayLength().Should().Be(2);
        features[0].GetString().Should().Be("a");
        features[1].GetString().Should().Be("b");
    }

    // --- REQ-5: Logging (skip-documented — not reachable at this surface) -------

    [Fact(Skip = "Logging verified by integration — AppSettingsGen.cs:168 emits " +
        "`Logger.Information($\"Processing source generation: rootNamespace={...}, fileCount={...}, files=[{sortedFileList}]\")` " +
        "inside GenerateSourceFiles BEFORE calling MergeJsonFiles. The log format is a literal string " +
        "at the generator's SGF Logger sink; there is no pure-function hook reachable from the " +
        "JsonMerger.MergeJsonFiles surface. Asserting format here would require spinning a full " +
        "GeneratorDriver harness — out of scope for Phase C γ group (unit-level tests).")]
    public void Logging_LogsFilenames()
    {
        // Intentionally empty — see Skip rationale above.
    }

    // --- REQ-2: Wildcard glob (skip-documented — MSBuild-layer, not unit-testable) --

    [Fact(Skip = "Wildcard behavior is an MSBuild-layer concern, not a JsonMerger.MergeJsonFiles " +
        "input-shaping concern. NotNot.AppSettings.props:6 expands `appsettings*.json` into an " +
        "<AdditionalFiles> item list BEFORE the generator ever runs; the generator receives an " +
        "already-materialized dict of AdditionalTextsProvider entries. Verifying glob semantics " +
        "requires an MSBuild integration test (e.g., spinning dotnet build against a sample project) " +
        "which exceeds Phase C γ unit-test scope. TDD §8 R1 acknowledges this boundary.")]
    public void Wildcard_ViaPropsGlob_IncludesMultipleFiles()
    {
        // Intentionally empty — see Skip rationale above.
    }
}
