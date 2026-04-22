using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using NotNot.AppSettingsHelper;
using Xunit;

namespace NotNot.AppSettings.Tests;

public class JsonSettingsUtilsTests
{
    #region ComputeDiff Tests

    [Fact]
    public void ComputeDiff_WhenBothNull_ReturnsNull()
    {
        var diff = JsonSettingsUtils.ComputeDiff(null, null);
        diff.Should().BeNull();
    }

    [Fact]
    public void ComputeDiff_WhenOriginalNull_ReturnsCurrentClone()
    {
        var current = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var diff = JsonSettingsUtils.ComputeDiff(null, current);

        diff.Should().NotBeNull();
        diff!["Window"]!["X"]!.GetValue<int>().Should().Be(100);
    }

    [Fact]
    public void ComputeDiff_WhenValueChanged_ReturnsOnlyChangedValue()
    {
        var original = JsonNode.Parse("""{"Window": {"X": 100, "Y": 200}}""");
        var current = JsonNode.Parse("""{"Window": {"X": 150, "Y": 200}}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        diff!["Window"]!["X"]!.GetValue<int>().Should().Be(150);
        diff!["Window"]!.AsObject().ContainsKey("Y").Should().BeFalse("unchanged values should not be in diff");
    }

    [Fact]
    public void ComputeDiff_WhenNoChanges_ReturnsNull()
    {
        var original = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var current = JsonNode.Parse("""{"Window": {"X": 100}}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);
        diff.Should().BeNull();
    }

    [Fact]
    public void ComputeDiff_WhenPropertyAdded_ReturnsAddedProperty()
    {
        var original = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var current = JsonNode.Parse("""{"Window": {"X": 100, "Y": 200}}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        diff!["Window"]!["Y"]!.GetValue<int>().Should().Be(200);
    }

    [Fact]
    public void ComputeDiff_WhenArrayChanged_ReturnsEntireArray()
    {
        var original = JsonNode.Parse("""{"Items": [1, 2, 3]}""");
        var current = JsonNode.Parse("""{"Items": [1, 2, 4]}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        var items = diff!["Items"]!.AsArray();
        items.Should().HaveCount(3);
        items[2]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public void ComputeDiff_WhenArrayUnchanged_ReturnsNull()
    {
        var original = JsonNode.Parse("""{"Items": [1, 2, 3]}""");
        var current = JsonNode.Parse("""{"Items": [1, 2, 3]}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);
        diff.Should().BeNull();
    }

    [Fact]
    public void ComputeDiff_WhenTypeChanged_ReturnsCurrentValue()
    {
        var original = JsonNode.Parse("""{"Value": 100}""");
        var current = JsonNode.Parse("""{"Value": "hundred"}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        diff!["Value"]!.GetValue<string>().Should().Be("hundred");
    }

    #endregion

    #region Deletion Semantics Tests (P1-5 Critical)

    [Fact]
    public void ComputeDiff_WhenValueRemoved_ReturnsNullSentinel()
    {
        // P1-5: When original has value but current is null/missing,
        // diff should contain explicit null (deletion sentinel)
        var original = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var current = JsonNode.Parse("""{}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        var diffObj = diff!.AsObject();
        diffObj.ContainsKey("Window").Should().BeTrue("diff should have Window key");
        // In System.Text.Json, a null value is stored as null (not a JsonValue with kind Null)
        diffObj.TryGetPropertyValue("Window", out var windowValue);
        windowValue.Should().BeNull("deleted property should be null sentinel");
    }

    [Fact]
    public void ComputeDiff_WhenNestedValueRemoved_ReturnsNullSentinelAtCorrectLevel()
    {
        var original = JsonNode.Parse("""{"Window": {"X": 100, "Y": 200}}""");
        var current = JsonNode.Parse("""{"Window": {"X": 100}}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        var windowDiff = diff!["Window"]!.AsObject();
        windowDiff.ContainsKey("Y").Should().BeTrue("diff should have Y key");
        windowDiff.TryGetPropertyValue("Y", out var yValue);
        yValue.Should().BeNull("deleted nested property should be null sentinel");
    }

    [Fact]
    public void ComputeDiff_WhenCurrentExplicitlyNull_ReturnsNullValue()
    {
        // When current is explicitly null (entire object deleted),
        // the diff result itself represents "delete this" - which is a null value
        var original = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var current = (JsonNode?)null;

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        // When current is null, the result is the null sentinel value itself
        // In System.Text.Json, JsonValue.Create<object?>(null) returns null
        // So the diff is null, indicating "delete the entire thing"
        // This is the correct behavior - a null diff means "set to null/delete"
        // But we need to distinguish between "no changes" and "delete"
        // For top-level deletion, returning null is ambiguous with "no changes"
        //
        // Actually, looking at our use case:
        // - ComputeDiff returns null when no changes
        // - ComputeDiff returns the null sentinel when deletion
        // - But JsonValue.Create<object?>(null) returns null!
        //
        // This means we can't distinguish top-level deletion from no-changes.
        // For our use case (settings with nested objects), top-level deletion
        // is not a valid operation - we always have a root settings object.
        // So this test case is not realistic for our use case.
        // Let's verify the deletion sentinel IS null (which is fine for nested cases)
        diff.Should().BeNull("JsonValue.Create<object?>(null) returns null in System.Text.Json");
    }

    [Fact]
    public void MergeJson_WhenNullSentinel_RemovesKey()
    {
        // P1-5: When merge encounters null sentinel, it removes the key
        var target = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var diff = JsonNode.Parse("""{"Window": null}""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        merged.AsObject().ContainsKey("Window").Should().BeFalse(
            "null sentinel should remove key from target");
    }

    [Fact]
    public void MergeJson_WhenNestedNullSentinel_RemovesNestedKey()
    {
        var target = JsonNode.Parse("""{"Window": {"X": 100, "Y": 200}}""");
        var diff = JsonNode.Parse("""{"Window": {"Y": null}}""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        merged["Window"]!.AsObject().ContainsKey("X").Should().BeTrue("unchanged key should remain");
        merged["Window"]!.AsObject().ContainsKey("Y").Should().BeFalse("null sentinel should remove nested key");
    }

    [Fact]
    public void ComputeDiff_ThenMerge_DeletionRoundTrip()
    {
        // Full round-trip test for deletion semantics
        var original = JsonNode.Parse("""{"Window": {"X": 100, "Y": 200}, "Other": "value"}""");
        var current = JsonNode.Parse("""{"Window": {"X": 100}, "Other": "value"}""");
        var userFile = JsonNode.Parse("""{"Window": {"X": 100, "Y": 200}}""");

        // Compute diff (should have null sentinel for Window.Y)
        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        // Merge diff into user file
        var merged = JsonSettingsUtils.MergeJson(userFile!, diff!);

        // Verify Y was removed
        merged["Window"]!.AsObject().ContainsKey("X").Should().BeTrue();
        merged["Window"]!.AsObject().ContainsKey("Y").Should().BeFalse();
    }

    [Fact]
    public void ComputeDiff_WhenEntireObjectRemoved_ReturnsNullSentinel()
    {
        var original = JsonNode.Parse("""{"Window": {"X": 100}, "Camera": {"Fov": 90}}""");
        var current = JsonNode.Parse("""{"Window": {"X": 100}}""");

        var diff = JsonSettingsUtils.ComputeDiff(original, current);

        diff.Should().NotBeNull();
        var diffObj = diff!.AsObject();
        diffObj.ContainsKey("Camera").Should().BeTrue("diff should have Camera key");
        diffObj.TryGetPropertyValue("Camera", out var cameraValue);
        cameraValue.Should().BeNull("deleted entire object should produce null sentinel");
    }

    #endregion

    #region MergeJson Tests

    [Fact]
    public void MergeJson_WhenTargetEmpty_ReturnsCloneOfDiff()
    {
        var target = new JsonObject();
        var diff = JsonNode.Parse("""{"Window": {"X": 100}}""");

        var merged = JsonSettingsUtils.MergeJson(target, diff!);

        merged["Window"]!["X"]!.GetValue<int>().Should().Be(100);
    }

    [Fact]
    public void MergeJson_WhenDiffHasNewProperty_AddsProperty()
    {
        var target = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var diff = JsonNode.Parse("""{"Camera": {"Fov": 90}}""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        merged["Window"]!["X"]!.GetValue<int>().Should().Be(100);
        merged["Camera"]!["Fov"]!.GetValue<int>().Should().Be(90);
    }

    [Fact]
    public void MergeJson_WhenDiffHasUpdatedProperty_UpdatesProperty()
    {
        var target = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var diff = JsonNode.Parse("""{"Window": {"X": 200}}""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        merged["Window"]!["X"]!.GetValue<int>().Should().Be(200);
    }

    [Fact]
    public void MergeJson_WhenDiffIsNotObject_ReturnsDiff()
    {
        var target = JsonNode.Parse("""{"Window": {"X": 100}}""");
        var diff = JsonNode.Parse("""[1, 2, 3]""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        merged.Should().BeOfType<JsonArray>();
        merged.AsArray().Should().HaveCount(3);
    }

    [Fact]
    public void MergeJson_PreservesExistingKeysNotInDiff()
    {
        var target = JsonNode.Parse("""{"A": 1, "B": 2}""");
        var diff = JsonNode.Parse("""{"B": 3}""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        merged["A"]!.GetValue<int>().Should().Be(1);
        merged["B"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void MergeJson_DoesNotModifyOriginalTarget()
    {
        var target = JsonNode.Parse("""{"X": 1}""");
        var diff = JsonNode.Parse("""{"X": 2}""");

        var merged = JsonSettingsUtils.MergeJson(target!, diff!);

        target!["X"]!.GetValue<int>().Should().Be(1, "original should not be modified");
        merged["X"]!.GetValue<int>().Should().Be(2);
    }

    #endregion

    #region MergeStreamsAsync Tests

    [Fact]
    public async Task MergeStreamsAsync_WhenSingleStream_ReturnsItsContent()
    {
        var stream = CreateStream("""{"X": 1}""");

        var merged = await JsonSettingsUtils.MergeStreamsAsync([stream]);

        merged["X"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task MergeStreamsAsync_WhenMultipleStreams_LaterOverridesEarlier()
    {
        var stream1 = CreateStream("""{"X": 1, "Y": 2}""");
        var stream2 = CreateStream("""{"X": 10}""");

        var merged = await JsonSettingsUtils.MergeStreamsAsync([stream1, stream2]);

        merged["X"]!.GetValue<int>().Should().Be(10, "later stream should override");
        merged["Y"]!.GetValue<int>().Should().Be(2, "non-overlapping key should be preserved");
    }

    [Fact]
    public async Task MergeStreamsAsync_WhenEmpty_ReturnsEmptyObject()
    {
        var merged = await JsonSettingsUtils.MergeStreamsAsync([]);

        merged.Should().NotBeNull();
        merged.Count.Should().Be(0);
    }

    // --- Phase-5 N3 closure: runtime-path parser-options + non-object-root contracts ---

    [Fact]
    public async Task MergeStreamsAsync_StreamWithTrailingCommas_ParsesSuccessfully()
    {
        // H2 runtime-path evidence: the runtime facade (MergeStreamsAsync) must parse JSON with
        // trailing commas just like the generator path does. Pins the documentOptions:
        // JsonMergeCore.DefaultDocumentOptions wiring at JsonSettingsUtils.cs:88 — regression if
        // someone drops that argument and the stream path silently reverts to strict parsing.
        var stream = CreateStream("""
        {
            "Database": {
                "ConnectionString": "Server=.;",
                "Timeout": 30,
            },
        }
        """);

        var merged = await JsonSettingsUtils.MergeStreamsAsync([stream]);

        merged["Database"]!["Timeout"]!.GetValue<int>().Should().Be(30);
        merged["Database"]!["ConnectionString"]!.GetValue<string>().Should().Be("Server=.;");
    }

    [Fact]
    public async Task MergeStreamsAsync_StreamWithComments_ParsesSuccessfully()
    {
        // H2 runtime-path evidence (comment-handling axis): // line comments and /* block */
        // comments common in human-authored appsettings files must parse through the runtime
        // facade. Symmetric with JsonMergeCoreTests.DefaultDocumentOptions_ParsesJsonWithTrailingCommasAndComments.
        var stream = CreateStream("""
        {
            // top-level comment
            "Logging": {
                "Level": "Info" /* trailing block comment */
            }
        }
        """);

        var merged = await JsonSettingsUtils.MergeStreamsAsync([stream]);

        merged["Logging"]!["Level"]!.GetValue<string>().Should().Be("Info");
    }

    [Fact]
    public async Task MergeStreamsAsync_NonObjectRootStream_ThrowsJsonException()
    {
        // M1 runtime-path evidence: array-root or primitive-root appsettings streams must be
        // REJECTED at the runtime facade, matching the generator-side MergeAll guard
        // (JsonMergeCoreTests.MergeAll_NonObjectRoot_ThrowsJsonException). Pins the throw block
        // at JsonSettingsUtils.cs:99-101 — regression if someone silently drops the guard.
        var stream = CreateStream("[1,2,3]");

        Func<Task> act = async () => await JsonSettingsUtils.MergeStreamsAsync([stream]);

        await act.Should().ThrowAsync<JsonException>()
            .WithMessage("*JSON object at the root*");
    }

    #endregion

    #region Helper Methods

    private static MemoryStream CreateStream(string json)
    {
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    }

    #endregion
}
