using NotNot.Collections;
using Xunit;
using Xunit.Abstractions;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Unit tests for <see cref="Tag"/> — the colon-delimited three-segment name/value/detail tag grammar.
/// <para>
/// Covers the acceptance set: bare parse, one-colon parse, two-colon parse, the first-TWO-colons split on a
/// many-colon token (remainder stays in <see cref="Tag.Detail"/>), lossless <c>Format</c>∘<c>TryParse</c>
/// round-trip across all shapes, and ordinal-ignore-case <see cref="Tag.MatchesName"/> /
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
	// TryParse — segment-count grammar (bare / one-colon / two-colon / first-two-split)
	// ------------------------------------------------------------------

	[Fact]
	public void TryParse_BareToken_NameSet_ValueAndDetailNull()
	{
		var ok = Tag.TryParse("BulkSessionHistory", out var tag);
		Assert.True(ok);
		Assert.Equal("BulkSessionHistory", tag.Name);
		Assert.Null(tag.Value);
		Assert.Null(tag.Detail);
	}

	[Fact]
	public void TryParse_OneColon_NameAndValue_DetailNull()
	{
		var ok = Tag.TryParse("key:value", out var tag);
		Assert.True(ok);
		Assert.Equal("key", tag.Name);
		Assert.Equal("value", tag.Value);
		Assert.Null(tag.Detail);
	}

	[Fact]
	public void TryParse_TwoColons_NameValueDetail()
	{
		// "a:b:c" splits on the first TWO colons: Name="a", Value="b", Detail="c".
		var ok = Tag.TryParse("a:b:c", out var tag);
		Assert.True(ok);
		Assert.Equal("a", tag.Name);
		Assert.Equal("b", tag.Value);
		Assert.Equal("c", tag.Detail);
	}

	[Fact]
	public void TryParse_ThreeOrMoreColons_SplitsOnFirstTwoOnly_RemainderInDetail()
	{
		// "key:value:detail:extra" splits ONLY on the first two colons; the remainder (which itself
		// contains a colon) stays verbatim in Detail.
		var ok = Tag.TryParse("key:value:detail:extra", out var tag);
		Assert.True(ok);
		Assert.Equal("key", tag.Name);
		Assert.Equal("value", tag.Value);
		Assert.Equal("detail:extra", tag.Detail);
	}

	[Fact]
	public void TryParse_GenuineThreeSegment_ColorVariant()
	{
		// A genuine 3-segment producer token (e.g. "color:light-blue:userInput").
		var ok = Tag.TryParse("color:light-blue:userInput", out var tag);
		Assert.True(ok);
		Assert.Equal("color", tag.Name);
		Assert.Equal("light-blue", tag.Value);
		Assert.Equal("userInput", tag.Detail);
	}

	[Fact]
	public void TryParse_TrailingColon_EmptyValue_DetailNull()
	{
		// A token with exactly one trailing colon has one colon, so Value is the empty string (NOT null)
		// and Detail is null — distinct from the bare-token case which yields a null Value.
		var ok = Tag.TryParse("name:", out var tag);
		Assert.True(ok);
		Assert.Equal("name", tag.Name);
		Assert.Equal(string.Empty, tag.Value);
		Assert.Null(tag.Detail);
	}

	[Fact]
	public void TryParse_TrailingDoubleColon_EmptyDetail()
	{
		// Two colons present → Detail is the empty string (NOT null), since the second colon was consumed.
		var ok = Tag.TryParse("name:value:", out var tag);
		Assert.True(ok);
		Assert.Equal("name", tag.Name);
		Assert.Equal("value", tag.Value);
		Assert.Equal(string.Empty, tag.Detail);
	}

	[Fact]
	public void TryParse_EmptyMiddleSegment_EmptyValueWithDetail()
	{
		// "key::detail" → Name="key", Value="" (empty, the second colon was consumed), Detail="detail".
		var ok = Tag.TryParse("key::detail", out var tag);
		Assert.True(ok);
		Assert.Equal("key", tag.Name);
		Assert.Equal(string.Empty, tag.Value);
		Assert.Equal("detail", tag.Detail);
	}

	[Fact]
	public void TryParse_LeadingColon_EmptyName()
	{
		var ok = Tag.TryParse(":value", out var tag);
		Assert.True(ok);
		Assert.Equal(string.Empty, tag.Name);
		Assert.Equal("value", tag.Value);
		Assert.Null(tag.Detail);
	}

	[Fact]
	public void TryParse_Null_ReturnsFalse()
	{
		var ok = Tag.TryParse(null, out var tag);
		Assert.False(ok);
		Assert.Equal(default, tag);
	}

	[Fact]
	public void TryParse_EmptyString_ReturnsFalse()
	{
		var ok = Tag.TryParse(string.Empty, out var tag);
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
	// Format — instance + static overloads (2-arg and 3-arg)
	// ------------------------------------------------------------------

	[Fact]
	public void Format_NullValue_ReturnsNameOnly()
	{
		var tag = new Tag("BulkSessionHistory", null, null);
		Assert.Equal("BulkSessionHistory", tag.Format());
	}

	[Fact]
	public void Format_WithValue_NoDetail_ReturnsNameColonValue()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.Equal("SessionId:da83", tag.Format());
	}

	[Fact]
	public void Format_WithValueAndDetail_ReturnsThreeSegments()
	{
		var tag = new Tag("color", "light-blue", "userInput");
		Assert.Equal("color:light-blue:userInput", tag.Format());
	}

	[Fact]
	public void Format_DetailWithoutValue_DropsDetail()
	{
		// An unparseable construction (Detail set while Value is null) renders as Name alone — the
		// grammar has no slot for a value-less detail.
		var tag = new Tag("name", null, "orphanDetail");
		Assert.Equal("name", tag.Format());
	}

	[Fact]
	public void Format_Static_TwoArg_MatchesInstance()
	{
		Assert.Equal("name", Tag.Format("name", null));
		Assert.Equal("name:value", Tag.Format("name", "value"));
	}

	[Fact]
	public void Format_Static_ThreeArg_MatchesInstance()
	{
		Assert.Equal("name", Tag.Format("name", null, null));
		Assert.Equal("name:value", Tag.Format("name", "value", null));
		Assert.Equal("name:value:detail", Tag.Format("name", "value", "detail"));
	}

	// ------------------------------------------------------------------
	// Format ∘ TryParse round-trip (lossless) — all segment shapes
	// ------------------------------------------------------------------

	[Theory]
	[InlineData("BulkSessionHistory")] // bare
	[InlineData("SessionId:da83")] // one colon → name:value
	[InlineData("a:b:c")] // two colons → name:value:detail
	[InlineData("key:value:detail:extra")] // 3+ colons → detail retains the remainder
	[InlineData("color:light-blue:userInput")] // genuine 3-segment producer token
	[InlineData("name:")] // trailing colon → empty value, null detail
	[InlineData("name:value:")] // trailing double colon → empty detail
	[InlineData("key::detail")] // empty middle segment
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
		var result = Tag.ParseMany(new[] { "BulkSessionHistory", "SessionId:da83", "color:light-blue:userInput" });
		Assert.Equal(3, result.Count);

		Assert.Equal("BulkSessionHistory", result[0].Name);
		Assert.Null(result[0].Value);
		Assert.Null(result[0].Detail);

		Assert.Equal("SessionId", result[1].Name);
		Assert.Equal("da83", result[1].Value);
		Assert.Null(result[1].Detail);

		Assert.Equal("color", result[2].Name);
		Assert.Equal("light-blue", result[2].Value);
		Assert.Equal("userInput", result[2].Detail);
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
	// MatchesName — ordinal-ignore-case (NAME-segment matching unchanged by the grammar extension)
	// ------------------------------------------------------------------

	[Fact]
	public void MatchesName_ExactCase_True()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.True(tag.MatchesName("SessionId"));
	}

	[Fact]
	public void MatchesName_MixedCase_True()
	{
		// Ordinal-ignore-case: "sessionid" / "SESSIONID" match "SessionId".
		var tag = new Tag("SessionId", "da83", null);
		Assert.True(tag.MatchesName("sessionid"));
		Assert.True(tag.MatchesName("SESSIONID"));
	}

	[Fact]
	public void MatchesName_PrefixSubstring_False()
	{
		// MatchesName is full-name equality, NOT a prefix test.
		var tag = new Tag("SessionId", "da83", null);
		Assert.False(tag.MatchesName("Session"));
	}

	[Fact]
	public void MatchesName_DifferentName_False()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.False(tag.MatchesName("BulkSessionHistory"));
	}

	// ------------------------------------------------------------------
	// MatchesPrefix — ordinal-ignore-case, on the NAME segment
	// ------------------------------------------------------------------

	[Fact]
	public void MatchesPrefix_ExactCasePrefix_True()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.True(tag.MatchesPrefix("Session"));
	}

	[Fact]
	public void MatchesPrefix_MixedCasePrefix_True()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.True(tag.MatchesPrefix("session"));
		Assert.True(tag.MatchesPrefix("SESSION"));
	}

	[Fact]
	public void MatchesPrefix_FullNameAsPrefix_True()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.True(tag.MatchesPrefix("SessionId"));
		Assert.True(tag.MatchesPrefix("SESSIONID"));
	}

	[Fact]
	public void MatchesPrefix_MatchesNameSegmentNotValueOrDetail()
	{
		// The prefix is tested against the NAME segment only — neither value nor detail satisfy it.
		var tag = new Tag("SessionId", "da83", "extra");
		Assert.False(tag.MatchesPrefix("da83"));
		Assert.False(tag.MatchesPrefix("extra"));
	}

	[Fact]
	public void MatchesPrefix_NonMatchingPrefix_False()
	{
		var tag = new Tag("SessionId", "da83", null);
		Assert.False(tag.MatchesPrefix("Bulk"));
	}

	// ------------------------------------------------------------------
	// Record equality — documents the case-SENSITIVE default + Detail participation
	// ------------------------------------------------------------------

	[Fact]
	public void Equality_NameCaseSensitive_ByDefault()
	{
		// Record == compares Name ordinally case-SENSITIVELY. This is the documented reason callers
		// must use MatchesName/MatchesPrefix for case-insensitive tag-name comparison.
		var a = new Tag("SessionId", "da83", null);
		var b = new Tag("sessionid", "da83", null);
		Assert.NotEqual(a, b);
		Assert.True(a.MatchesName(b.Name)); // ...but ordinal-ignore-case matching DOES treat them as the same name.
	}

	[Fact]
	public void Equality_DetailParticipatesInValueEquality()
	{
		// The synthesized record equality includes the Detail segment: tags identical in Name/Value but
		// differing in Detail are NOT equal; identical in all three ARE equal.
		var withDetail = new Tag("color", "light-blue", "userInput");
		var otherDetail = new Tag("color", "light-blue", "sidechain");
		var noDetail = new Tag("color", "light-blue", null);
		var sameDetail = new Tag("color", "light-blue", "userInput");

		Assert.NotEqual(withDetail, otherDetail);
		Assert.NotEqual(withDetail, noDetail);
		Assert.Equal(withDetail, sameDetail);
		Assert.Equal(withDetail.GetHashCode(), sameDetail.GetHashCode());
	}
}
