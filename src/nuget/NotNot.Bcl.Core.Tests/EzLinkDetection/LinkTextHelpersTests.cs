using NotNot.Bcl.Core.EzLinkDetection;
using Xunit;

namespace NotNot.Bcl.Core.Tests.EzLinkDetection;

/// <summary>
/// Unit tests for <see cref="LinkTextHelpers"/>.
/// </summary>
public class LinkTextHelpersTests
{
	// ============================================================================================
	// IsSingleWord
	// ============================================================================================
	// Contract: returns true iff the input is a non-empty string containing no whitespace
	// and no path separators ('/' or '\'). Null and empty return false.

	// ---------------- true cases ----------------

	[Fact]
	public void IsSingleWord_BareWord_ReturnsTrue()
	{
		Assert.True(LinkTextHelpers.IsSingleWord("Maybe"));
	}

	[Fact]
	public void IsSingleWord_DottedExtension_ReturnsTrue()
	{
		// "Maybe.txt" has no whitespace and no path separators → still a single word
		Assert.True(LinkTextHelpers.IsSingleWord("Maybe.txt"));
	}

	[Fact]
	public void IsSingleWord_UnderscoreHyphen_ReturnsTrue()
	{
		Assert.True(LinkTextHelpers.IsSingleWord("foo_bar-baz"));
	}

	[Fact]
	public void IsSingleWord_UnicodeLetters_ReturnsTrue()
	{
		Assert.True(LinkTextHelpers.IsSingleWord("café"));
	}

	// ---------------- false cases ----------------

	[Fact]
	public void IsSingleWord_WithForwardSlash_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord("foo/bar"));
	}

	[Fact]
	public void IsSingleWord_WithBackslash_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord("foo\\bar"));
	}

	[Fact]
	public void IsSingleWord_WithSpace_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord("hello world"));
	}

	[Fact]
	public void IsSingleWord_WithTab_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord("foo\tbar"));
	}

	[Fact]
	public void IsSingleWord_WithNewline_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord("foo\nbar"));
	}

	[Fact]
	public void IsSingleWord_Empty_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord(""));
	}

	[Fact]
	public void IsSingleWord_Null_ReturnsFalse()
	{
		Assert.False(LinkTextHelpers.IsSingleWord(null));
	}

	// ============================================================================================
	// ExtractWhitespaceBoundary
	// ============================================================================================
	// Contract: returns (Text, Start, Length)? for the whitespace-bounded segment containing
	// cursorPosition. Returns null when the cursor sits on whitespace or is out of bounds.

	[Fact]
	public void ExtractWhitespaceBoundary_CursorOnWord_ReturnsSegment()
	{
		// text="foo bar baz", cursor=5 (on 'a' of "bar") → ("bar", 4, 3)
		var result = LinkTextHelpers.ExtractWhitespaceBoundary("foo bar baz", 5);
		Assert.NotNull(result);
		Assert.Equal("bar", result.Value.Text);
		Assert.Equal(4, result.Value.Start);
		Assert.Equal(3, result.Value.Length);
	}

	[Fact]
	public void ExtractWhitespaceBoundary_CursorOnWhitespace_ReturnsNull()
	{
		// text="foo bar", cursor=3 (space) → null
		var result = LinkTextHelpers.ExtractWhitespaceBoundary("foo bar", 3);
		Assert.Null(result);
	}

	[Fact]
	public void ExtractWhitespaceBoundary_CursorAtStartOfWord_ReturnsSegment()
	{
		// text="foo bar baz", cursor=0 (on 'f') → ("foo", 0, 3)
		var result = LinkTextHelpers.ExtractWhitespaceBoundary("foo bar baz", 0);
		Assert.NotNull(result);
		Assert.Equal("foo", result.Value.Text);
		Assert.Equal(0, result.Value.Start);
		Assert.Equal(3, result.Value.Length);
	}

	[Fact]
	public void ExtractWhitespaceBoundary_CursorAtEndOfWord_ReturnsSegment()
	{
		// text="foo bar baz", cursor=10 (on 'z', last char) → ("baz", 8, 3)
		var result = LinkTextHelpers.ExtractWhitespaceBoundary("foo bar baz", 10);
		Assert.NotNull(result);
		Assert.Equal("baz", result.Value.Text);
		Assert.Equal(8, result.Value.Start);
		Assert.Equal(3, result.Value.Length);
	}

	[Fact]
	public void ExtractWhitespaceBoundary_CursorOutOfBounds_ReturnsNull()
	{
		// cursor >= text.Length → null
		Assert.Null(LinkTextHelpers.ExtractWhitespaceBoundary("foo", 3));
		Assert.Null(LinkTextHelpers.ExtractWhitespaceBoundary("foo", 99));
	}

	[Fact]
	public void ExtractWhitespaceBoundary_NegativeCursor_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.ExtractWhitespaceBoundary("foo", -1));
	}

	[Fact]
	public void ExtractWhitespaceBoundary_EmptyOrNullText_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.ExtractWhitespaceBoundary("", 0));
		Assert.Null(LinkTextHelpers.ExtractWhitespaceBoundary(null!, 0));
	}

	[Fact]
	public void ExtractWhitespaceBoundary_SingleCharWord_ReturnsSegment()
	{
		// text="a b c", cursor=2 (on 'b') → ("b", 2, 1)
		var result = LinkTextHelpers.ExtractWhitespaceBoundary("a b c", 2);
		Assert.NotNull(result);
		Assert.Equal("b", result.Value.Text);
		Assert.Equal(2, result.Value.Start);
		Assert.Equal(1, result.Value.Length);
	}

	// ============================================================================================
	// GetSegmentBoundsAt
	// ============================================================================================
	// Contract: returns inclusive (Start, End)? bounds of the whitespace-bounded segment
	// containing col. Returns null when col is on whitespace or out of bounds.

	[Fact]
	public void GetSegmentBoundsAt_CursorOnWord_ReturnsBounds()
	{
		// line="alpha beta", col=2 (on 'p') → (0, 4)
		var result = LinkTextHelpers.GetSegmentBoundsAt("alpha beta", 2);
		Assert.NotNull(result);
		Assert.Equal(0, result.Value.Start);
		Assert.Equal(4, result.Value.End);
	}

	[Fact]
	public void GetSegmentBoundsAt_CursorOnWhitespace_ReturnsNull()
	{
		// line="alpha beta", col=5 (space) → null
		var result = LinkTextHelpers.GetSegmentBoundsAt("alpha beta", 5);
		Assert.Null(result);
	}

	[Fact]
	public void GetSegmentBoundsAt_CursorOnSecondWord_ReturnsBounds()
	{
		// line="alpha beta", col=7 (on 'e' of "beta") → (6, 9)
		var result = LinkTextHelpers.GetSegmentBoundsAt("alpha beta", 7);
		Assert.NotNull(result);
		Assert.Equal(6, result.Value.Start);
		Assert.Equal(9, result.Value.End);
	}

	[Fact]
	public void GetSegmentBoundsAt_CursorOutOfBounds_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.GetSegmentBoundsAt("alpha", 5));
		Assert.Null(LinkTextHelpers.GetSegmentBoundsAt("alpha", -1));
	}

	[Fact]
	public void GetSegmentBoundsAt_EmptyLine_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.GetSegmentBoundsAt("", 0));
	}

	// ============================================================================================
	// GetFirstSegmentBounds
	// ============================================================================================
	// Contract: returns inclusive (Start, End)? of the first whitespace-bounded segment in line.
	// Returns null when line is null, empty, or entirely whitespace.

	[Fact]
	public void GetFirstSegmentBounds_NonEmptyLine_ReturnsFirstSegment()
	{
		// line="alpha beta" → (0, 4) — "alpha"
		var result = LinkTextHelpers.GetFirstSegmentBounds("alpha beta");
		Assert.NotNull(result);
		Assert.Equal(0, result.Value.Start);
		Assert.Equal(4, result.Value.End);
	}

	[Fact]
	public void GetFirstSegmentBounds_AllWhitespace_ReturnsNull()
	{
		// line="   " → null
		Assert.Null(LinkTextHelpers.GetFirstSegmentBounds("   "));
	}

	[Fact]
	public void GetFirstSegmentBounds_LeadingWhitespace_SkipsToFirstSegment()
	{
		// line="   alpha beta" → (3, 7) — "alpha" starts at index 3
		var result = LinkTextHelpers.GetFirstSegmentBounds("   alpha beta");
		Assert.NotNull(result);
		Assert.Equal(3, result.Value.Start);
		Assert.Equal(7, result.Value.End);
	}

	[Fact]
	public void GetFirstSegmentBounds_EmptyLine_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.GetFirstSegmentBounds(""));
	}

	[Fact]
	public void GetFirstSegmentBounds_SingleWord_ReturnsWholeBounds()
	{
		// line="solo" → (0, 3)
		var result = LinkTextHelpers.GetFirstSegmentBounds("solo");
		Assert.NotNull(result);
		Assert.Equal(0, result.Value.Start);
		Assert.Equal(3, result.Value.End);
	}

	// ============================================================================================
	// GetLastSegmentBounds
	// ============================================================================================
	// Contract: returns inclusive (Start, End)? of the last whitespace-bounded segment in line.
	// Returns null when line is null, empty, or entirely whitespace.

	[Fact]
	public void GetLastSegmentBounds_NonEmptyLine_ReturnsLastSegment()
	{
		// line="alpha beta" → (6, 9) — "beta" runs index 6..9
		var result = LinkTextHelpers.GetLastSegmentBounds("alpha beta");
		Assert.NotNull(result);
		Assert.Equal(6, result.Value.Start);
		Assert.Equal(9, result.Value.End);
	}

	[Fact]
	public void GetLastSegmentBounds_EmptyLine_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.GetLastSegmentBounds(""));
	}

	[Fact]
	public void GetLastSegmentBounds_AllWhitespace_ReturnsNull()
	{
		Assert.Null(LinkTextHelpers.GetLastSegmentBounds("   "));
	}

	[Fact]
	public void GetLastSegmentBounds_TrailingWhitespace_SkipsBackToLastSegment()
	{
		// line="alpha beta   " → (6, 9) — "beta", trailing spaces ignored
		var result = LinkTextHelpers.GetLastSegmentBounds("alpha beta   ");
		Assert.NotNull(result);
		Assert.Equal(6, result.Value.Start);
		Assert.Equal(9, result.Value.End);
	}

	[Fact]
	public void GetLastSegmentBounds_SingleWord_ReturnsWholeBounds()
	{
		// line="solo" → (0, 3)
		var result = LinkTextHelpers.GetLastSegmentBounds("solo");
		Assert.NotNull(result);
		Assert.Equal(0, result.Value.Start);
		Assert.Equal(3, result.Value.End);
	}
}
