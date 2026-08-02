using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NotNot.AppSettingsInternal;
using Xunit;

namespace NotNot.AppSettings.Tests;

/// <summary>
/// Tests covering the shared-source <see cref="JsonMergeCore"/> utility.
/// Access enabled via <c>InternalsVisibleTo("NotNot.AppSettings.Tests")</c> on
/// the <c>NotNot.AppSettings</c> assembly (AppSettingsGen.cs line 13).
/// </summary>
public class JsonMergeCoreTests
{
    [Fact]
    public void Merge_NullSentinel_RemovesKey()
    {
        var target = JsonNode.Parse("""{"a":1}""")!;
        var diff = JsonNode.Parse("""{"a":null}""")!;

        var merged = JsonMergeCore.Merge(target, diff);

        merged.Should().NotBeNull();
        merged!.AsObject().ContainsKey("a").Should().BeFalse("null sentinel should remove the key, not set it to null");
    }

    [Fact]
    public void Merge_NestedNullSentinel_RemovesNestedKey()
    {
        var target = JsonNode.Parse("""{"o":{"a":1,"b":2}}""")!;
        var diff = JsonNode.Parse("""{"o":{"a":null}}""")!;

        var merged = JsonMergeCore.Merge(target, diff);

        merged.Should().NotBeNull();
        var obj = merged!["o"]!.AsObject();
        obj.ContainsKey("a").Should().BeFalse("nested null sentinel should remove 'a'");
        obj.ContainsKey("b").Should().BeTrue("unchanged sibling 'b' must remain");
        obj["b"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public void Merge_PreservesUnchangedKeys()
    {
        var target = JsonNode.Parse("""{"a":1,"b":2}""")!;
        var diff = JsonNode.Parse("""{"a":3}""")!;

        var merged = JsonMergeCore.Merge(target, diff);

        merged.Should().NotBeNull();
        merged!["a"]!.GetValue<int>().Should().Be(3, "diff should overwrite 'a'");
        merged["b"]!.GetValue<int>().Should().Be(2, "'b' was not in diff and must be preserved");
    }

    [Fact]
    public void Merge_ReplacesArraysNotConcatenates()
    {
        var target = JsonNode.Parse("""{"arr":[1,2]}""")!;
        var diff = JsonNode.Parse("""{"arr":[3]}""")!;

        var merged = JsonMergeCore.Merge(target, diff);

        merged.Should().NotBeNull();
        var arr = merged!["arr"]!.AsArray();
        arr.Count.Should().Be(1, "array should be fully replaced, not concatenated");
        arr[0]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void ComputeDiff_ThenMerge_DeletionRoundTrip()
    {
        // original has 'a' and 'b'; current drops 'b' and changes 'a'
        var original = JsonNode.Parse("""{"a":1,"b":2}""")!;
        var current = JsonNode.Parse("""{"a":5}""")!;

        var diff = JsonMergeCore.ComputeDiff(original, current);
        diff.Should().NotBeNull("there is a real difference between original and current");

        var merged = JsonMergeCore.Merge(original, diff);

        merged.Should().NotBeNull();
        var mergedObj = merged!.AsObject();
        mergedObj.ContainsKey("a").Should().BeTrue();
        mergedObj["a"]!.GetValue<int>().Should().Be(5, "diff+merge must reproduce current value for 'a'");
        mergedObj.ContainsKey("b").Should().BeFalse("deletion must round-trip via null sentinel in diff");

        // Semantic equality with `current`
        mergedObj.ToJsonString().Should().Be(current.AsObject().ToJsonString());
    }

    // --- Phase-5 H2 closure: parser-options parity between build-time and runtime ---

    [Fact]
    public void DefaultDocumentOptions_AllowsTrailingCommasAndComments()
    {
        // Contract: the single source of truth for JSON document options shared between
        // JsonMerger (generator) and JsonSettingsUtils.MergeStreamsAsync (runtime) MUST
        // permit trailing commas and skip comments so human-authored appsettings*.json
        // files parse identically in both environments.
        JsonMergeCore.DefaultDocumentOptions.AllowTrailingCommas.Should().BeTrue(
            "appsettings files commonly have trailing commas after final properties");
        JsonMergeCore.DefaultDocumentOptions.CommentHandling.Should().Be(
            JsonCommentHandling.Skip,
            "appsettings files commonly carry // and /* */ comments for operator guidance");
    }

    [Fact]
    public void DefaultDocumentOptions_ParsesJsonWithTrailingCommasAndComments()
    {
        // Runtime evidence: actually parse a realistic-shape appsettings snippet WITH comments
        // and trailing commas using the shared options. Throw-on-parse = contract regression.
        const string jsonWithLenientSyntax = """
        {
            // top-level comment
            "Database": {
                "ConnectionString": "Server=.;", /* trailing block comment */
                "Timeout": 30, // trailing line comment + trailing comma below
            },
            "Logging": {
                "Level": "Info",
            },
        }
        """;

        var act = () => JsonNode.Parse(jsonWithLenientSyntax, documentOptions: JsonMergeCore.DefaultDocumentOptions);
        act.Should().NotThrow("DefaultDocumentOptions MUST accept comments + trailing commas");

        var node = JsonNode.Parse(jsonWithLenientSyntax, documentOptions: JsonMergeCore.DefaultDocumentOptions)!;
        node["Database"]!["Timeout"]!.GetValue<int>().Should().Be(30);
        node["Logging"]!["Level"]!.GetValue<string>().Should().Be("Info");
    }

    // --- Phase-5 M1 closure: non-object root parity between generator + runtime ---

    [Fact]
    public void MergeAll_NonObjectRoot_ThrowsJsonException()
    {
        // Contract: arrays and primitives at the root of an appsettings source are REJECTED
        // loudly — both build-time (this path) and runtime (JsonSettingsUtils.MergeStreamsAsync)
        // now fail-fast rather than silently discarding or resetting state (RULE_4).
        var arrayRoot = JsonNode.Parse("""[1,2,3]""")!;
        var sources = new List<JsonNode> { arrayRoot };

        var act = () => JsonMergeCore.MergeAll(sources);

        act.Should().Throw<JsonException>()
            .WithMessage("*JSON object at the root*");
    }

    [Fact]
    public void MergeAll_ObjectRoots_MergesAsExpected()
    {
        // Sanity: normal two-object merge still works after the non-object-root guard.
        var s1 = JsonNode.Parse("""{"a":1}""")!;
        var s2 = JsonNode.Parse("""{"b":2}""")!;
        var sources = new List<JsonNode> { s1, s2 };

        var merged = JsonMergeCore.MergeAll(sources);

        merged["a"]!.GetValue<int>().Should().Be(1);
        merged["b"]!.GetValue<int>().Should().Be(2);
    }

    // --- VibeReview L4 closure: non-object diff branch in Merge ---

    [Fact]
    public void Merge_DiffIsNonObject_ReplacesTargetEntirely()
    {
        // JsonMergeCore.Merge L47-51: if diff is not an object, it replaces target entirely.
        // Covers the branch the L4 finding flagged as untested.
        var target = JsonNode.Parse("""{"a":1}""")!;
        var diff = JsonNode.Parse("42")!;

        var merged = JsonMergeCore.Merge(target, diff);

        merged.Should().NotBeNull();
        merged!.GetValue<int>().Should().Be(42, "non-object diff replaces the target entirely");
    }

    [Fact]
    public void Merge_DoesNotModifyOriginalTarget()
    {
        var target = JsonNode.Parse("""{"a":1,"b":2}""")!;
        var targetSnapshot = target.ToJsonString();
        var diff = JsonNode.Parse("""{"a":99,"c":3}""")!;

        var merged = JsonMergeCore.Merge(target, diff);

        merged.Should().NotBeNull();
        merged!["a"]!.GetValue<int>().Should().Be(99);

        // Mutate the returned node; original target must remain untouched
        merged.AsObject()["a"] = 12345;
        merged.AsObject()["zzz"] = "injected";

        target.ToJsonString().Should().Be(targetSnapshot, "mutating the merge result must not affect the original target input");
    }
}
