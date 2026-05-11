using NotNot.Bcl.Core.EzLinkDetection;
using Xunit;

namespace NotNot.Bcl.Core.Tests.EzLinkDetection;

/// <summary>
/// Unit tests for <see cref="LinkTextHelpers.IsSingleWord(string?)"/>.
/// <para>
/// Contract: returns true iff the input is a non-empty string containing no whitespace
/// and no path separators ('/' or '\'). Null and empty return false.
/// </para>
/// </summary>
public class LinkTextHelpersTests
{
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
}
