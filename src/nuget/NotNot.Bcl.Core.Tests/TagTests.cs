using NotNot.Collections;
using Xunit;
using Xunit.Abstractions;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Unit tests for <see cref="Tag"/> — the colon-delimited name/value tag grammar.
/// <para>
/// Covers the B3 acceptance set: bare parse, colon parse, first-colon split on a multi-colon token,
/// lossless <c>Format</c>∘<c>TryParse</c> round-trip, and ordinal-ignore-case <see cref="Tag.MatchesName"/> /
/// <see cref="Tag.MatchesPrefix"/>. These document the wire grammar VOW-side consumers rely on
/// (formatted-string tag storage in the LocalStorage envelope) without any VOW vocabulary leaking here.
/// </para>
/// </summary>
public class TagTests
{
	private readonly ITestOutputHelper _output;

	public TagTests(ITestOutputHelper output)
	{
		_output = output;
	}

	// ------------------------------------------------------------------
	// TryParse — bare / colon / first-colon-split / null-whitespace
	// ------------------------------------------------------------------

	[Fact]
	public void TryParse_BareToken_NameSetValueNull()
	{
		var ok = Tag.TryParse("BulkSessionHistory", out var tag);
		Assert.True(ok);
		Assert.Equal("BulkSessionHistory", tag.Name);
		Assert.Null(tag.Value);
	}

	[Fact]
	public void TryParse_ColonToken_SplitsNameAndValue()
	{
		var ok = Tag.TryParse("name:value", out var tag);
		Assert.True(ok);
		Assert.Equal("name", tag.Name);
		Assert.Equal("value", tag.Value);
	}

	[Fact]
	public void TryParse_MultipleColons_SplitsOnFirstOnly()
	{
		// "a:b:c" must split ONCE: Name="a", Value="b:c" (value retains interior colons).
		var ok = Tag.TryParse("a:b:c", out var tag);
		Assert.True(ok);
		Assert.Equal("a", tag.Name);
		Assert.Equal("b:c", tag.Value);
	}

	[Fact]
	public void TryParse_TrailingColon_EmptyValueNotNull()
	{
		// A token ending in a colon has a colon, so Value is the empty string (NOT null) — distinct
		// from the bare-token case which yields a null Value.
		var ok = Tag.TryParse("name:", out var tag);
		Assert.True(ok);
		Assert.Equal("name", tag.Name);
		Assert.Equal(string.Empty, tag.Value);
	}

	[Fact]
	public void TryParse_LeadingColon_EmptyName()
	{
		var ok = Tag.TryParse(":value", out var tag);
		Assert.True(ok);
		Assert.Equal(string.Empty, tag.Name);
		Assert.Equal("value", tag.Value);
	}

	[Fact]
	public void TryParse_Null_ReturnsFalse()
	{
		var ok = Tag.TryParse(null, out var tag);
		Assert.False(ok);
		Assert.Equal(default, tag);
	}

	[Fact]
	public void TryParse_WhitespaceOnly_ReturnsFalse()
	{
		var ok = Tag.TryParse("   ", out var tag);
		Assert.False(ok);
		Assert.Equal(default, tag);
	}

	// ------------------------------------------------------------------
	// Format — instance + static overload
	// ------------------------------------------------------------------

	[Fact]
	public void Format_NullValue_ReturnsNameOnly()
	{
		var tag = new Tag("BulkSessionHistory", null);
		Assert.Equal("BulkSessionHistory", tag.Format());
	}

	[Fact]
	public void Format_WithValue_ReturnsNameColonValue()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.Equal("SessionId:da83", tag.Format());
	}

	[Fact]
	public void Format_Static_MatchesInstance()
	{
		Assert.Equal("name", Tag.Format("name", null));
		Assert.Equal("name:value", Tag.Format("name", "value"));
	}

	// ------------------------------------------------------------------
	// Format ∘ TryParse round-trip (lossless)
	// ------------------------------------------------------------------

	[Theory]
	[InlineData("BulkSessionHistory")] // bare
	[InlineData("SessionId:da83")] // simple colon
	[InlineData("a:b:c")] // first-colon split, value retains colons
	[InlineData("name:")] // trailing colon → empty value
	public void RoundTrip_FormatThenTryParse_IsLossless(string original)
	{
		var parsed = Tag.TryParse(original, out var tag);
		Assert.True(parsed);

		var formatted = tag.Format();
		Assert.Equal(original, formatted);

		// And the re-parse of the formatted form yields an equal Tag (idempotent).
		var reparsed = Tag.TryParse(formatted, out var tag2);
		Assert.True(reparsed);
		Assert.Equal(tag, tag2);
	}

	// ------------------------------------------------------------------
	// ParseMany
	// ------------------------------------------------------------------

	[Fact]
	public void ParseMany_Null_ReturnsEmpty()
	{
		var result = Tag.ParseMany(null);
		Assert.Empty(result);
	}

	[Fact]
	public void ParseMany_MixedTokens_ParsesAllValid_InOrder()
	{
		var result = Tag.ParseMany(new[] { "BulkSessionHistory", "SessionId:da83" });
		Assert.Equal(2, result.Count);
		Assert.Equal("BulkSessionHistory", result[0].Name);
		Assert.Null(result[0].Value);
		Assert.Equal("SessionId", result[1].Name);
		Assert.Equal("da83", result[1].Value);
	}

	[Fact]
	public void ParseMany_SkipsNullAndWhitespaceElements()
	{
		// Null element passes a string?[] through; the per-token TryParse guard skips it without NRE.
		var input = new string?[] { "good", null, "   ", "name:value" };
		var result = Tag.ParseMany(input!);
		Assert.Equal(2, result.Count);
		Assert.Equal("good", result[0].Name);
		Assert.Equal("name", result[1].Name);
		Assert.Equal("value", result[1].Value);
	}

	// ------------------------------------------------------------------
	// MatchesName — ordinal-ignore-case
	// ------------------------------------------------------------------

	[Fact]
	public void MatchesName_ExactCase_True()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.True(tag.MatchesName("SessionId"));
	}

	[Fact]
	public void MatchesName_MixedCase_True()
	{
		// Ordinal-ignore-case: "sessionid" / "SESSIONID" match "SessionId".
		var tag = new Tag("SessionId", "da83");
		Assert.True(tag.MatchesName("sessionid"));
		Assert.True(tag.MatchesName("SESSIONID"));
	}

	[Fact]
	public void MatchesName_PrefixSubstring_False()
	{
		// MatchesName is full-name equality, NOT a prefix test.
		var tag = new Tag("SessionId", "da83");
		Assert.False(tag.MatchesName("Session"));
	}

	[Fact]
	public void MatchesName_DifferentName_False()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.False(tag.MatchesName("BulkSessionHistory"));
	}

	// ------------------------------------------------------------------
	// MatchesPrefix — ordinal-ignore-case, on the NAME segment
	// ------------------------------------------------------------------

	[Fact]
	public void MatchesPrefix_ExactCasePrefix_True()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.True(tag.MatchesPrefix("Session"));
	}

	[Fact]
	public void MatchesPrefix_MixedCasePrefix_True()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.True(tag.MatchesPrefix("session"));
		Assert.True(tag.MatchesPrefix("SESSION"));
	}

	[Fact]
	public void MatchesPrefix_FullNameAsPrefix_True()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.True(tag.MatchesPrefix("SessionId"));
		Assert.True(tag.MatchesPrefix("SESSIONID"));
	}

	[Fact]
	public void MatchesPrefix_MatchesNameSegmentNotValue()
	{
		// The prefix is tested against the NAME segment only — the value "da83" must not satisfy it.
		var tag = new Tag("SessionId", "da83");
		Assert.False(tag.MatchesPrefix("da83"));
	}

	[Fact]
	public void MatchesPrefix_NonMatchingPrefix_False()
	{
		var tag = new Tag("SessionId", "da83");
		Assert.False(tag.MatchesPrefix("Bulk"));
	}

	// ------------------------------------------------------------------
	// Record equality — documents the case-SENSITIVE default (see Tag <summary>)
	// ------------------------------------------------------------------

	[Fact]
	public void Equality_NameCaseSensitive_ByDefault()
	{
		// Record == compares Name ordinally case-SENSITIVELY. This is the documented reason callers
		// must use MatchesName/MatchesPrefix for case-insensitive tag-name comparison.
		var a = new Tag("SessionId", "da83");
		var b = new Tag("sessionid", "da83");
		Assert.NotEqual(a, b);
		Assert.True(a.MatchesName(b.Name)); // ...but ordinal-ignore-case matching DOES treat them as the same name.
	}
}
