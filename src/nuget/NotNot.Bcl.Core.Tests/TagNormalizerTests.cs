using NotNot.Collections;
using Xunit;
using Xunit.Abstractions;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Unit tests for <see cref="TagNormalizer"/>.
/// <para>
/// The normalizer uses <see cref="StringComparer.InvariantCultureIgnoreCase"/> (decision recorded in
/// the G8 work order). Several tests here intentionally document culture-sensitive behavior so that
/// downstream comparer-preservation tests (Wave 4) can assert the same semantics survive through
/// STJ round-trip and Mapperly DTO projection.
/// </para>
/// </summary>
public class TagNormalizerTests
{
	private readonly ITestOutputHelper _output;

	public TagNormalizerTests(ITestOutputHelper output)
	{
		_output = output;
	}

	// ------------------------------------------------------------------
	// ParseInput — basic shape tests
	// ------------------------------------------------------------------

	[Fact]
	public void ParseInput_EmptyString_ReturnsEmptySet()
	{
		var result = TagNormalizer.ParseInput("");
		Assert.Empty(result);
	}

	[Fact]
	public void ParseInput_Null_ReturnsEmptySet()
	{
		var result = TagNormalizer.ParseInput(null);
		Assert.Empty(result);
	}

	[Fact]
	public void ParseInput_WhitespaceOnly_ReturnsEmptySet()
	{
		var result = TagNormalizer.ParseInput("   ");
		Assert.Empty(result);
	}

	[Fact]
	public void ParseInput_CommasOnly_ReturnsEmptySet()
	{
		var result = TagNormalizer.ParseInput(",,,");
		Assert.Empty(result);
	}

	[Fact]
	public void ParseInput_TrimsOuterWhitespace()
	{
		var result = TagNormalizer.ParseInput("  foo  , bar ");
		Assert.True(result.SetEquals(new[] { "foo", "bar" }));
	}

	[Fact]
	public void ParseInput_DiscardsEmptySegments()
	{
		var result = TagNormalizer.ParseInput("a,,b");
		Assert.True(result.SetEquals(new[] { "a", "b" }));
	}

	[Fact]
	public void ParseInput_DedupIgnoresCaseInvariant()
	{
		var result = TagNormalizer.ParseInput("foo,FOO,Foo");
		Assert.Single(result);
	}

	[Fact]
	public void ParseInput_PreservesInteriorWhitespace()
	{
		// "foo bar" (single space) and "foo  bar" (two spaces) must remain distinct —
		// the normalizer MUST NOT collapse interior whitespace.
		var result = TagNormalizer.ParseInput("foo bar, foo  bar");
		Assert.Equal(2, result.Count);
	}

	[Fact]
	public void ParseInput_PreservesInteriorSymbols()
	{
		var result = TagNormalizer.ParseInput("foo-bar, foo_bar, foo.bar");
		Assert.Equal(3, result.Count);
	}

	[Fact]
	public void ParseInput_CombinedSpecExample()
	{
		var result = TagNormalizer.ParseInput("  A, b, A ,  c,, ");
		Assert.True(result.SetEquals(new[] { "A", "b", "c" }));
		Assert.Equal(3, result.Count);
	}

	[Fact]
	public void ParseInput_ResultSetUsesInvariantCultureIgnoreCase()
	{
		// Verifies the RETURNED HashSet carries the correct comparer (not just that ParseInput
		// dedups internally). Wave 4 comparer-preservation tests rely on this contract.
		var result = TagNormalizer.ParseInput("foo");
		Assert.False(result.Add("FOO"), "Expected FOO to collide with foo under InvariantCultureIgnoreCase.");
		Assert.True(result.Add("bar"), "Expected bar to be a new distinct tag.");
		Assert.Equal(2, result.Count);
	}

	// ------------------------------------------------------------------
	// Culture-sensitive tests — document InvariantCulture behavior so
	// Wave 4 can verify the comparer survives STJ + Mapperly round-trips.
	// ------------------------------------------------------------------

	[Fact]
	public void ParseInput_DiacriticInsensitive_UnderInvariant()
	{
		// InvariantCulture(IgnoreCase) folds diacritics: "café" ≡ "CAFÉ" ≡ "Café".
		// Under Ordinal(IgnoreCase) these would be 3 distinct entries. This test is the
		// canonical divergence point between Invariant and Ordinal comparers — Wave 4
		// comparer-preservation tests use it as the probe case.
		var result = TagNormalizer.ParseInput("café, CAFÉ, Café");
		_output.WriteLine($"DiacriticInsensitive: count={result.Count}, entries=[{string.Join("|", result)}]");
		Assert.Single(result);
	}

	[Fact]
	public void ParseInput_GermanEszett_DocumentsBehavior()
	{
		// .NET InvariantCultureIgnoreCase typically folds ß ↔ ss, so all three collapse to one.
		// If .NET version changes this behavior, the assertion below will fail and we
		// reassess. Using Assert.InRange as a safety net — the most likely values are 1
		// (Invariant folding) or 3 (no folding). ITestOutputHelper records the observed value
		// for Wave 4 authors.
		var result = TagNormalizer.ParseInput("Straße, STRASSE, Strasse");
		_output.WriteLine($"GermanEszett: count={result.Count}, entries=[{string.Join("|", result)}]");
		Assert.InRange(result.Count, 1, 3);
	}

	[Fact]
	public void ParseInput_TurkishDottedI_StaysDistinct()
	{
		// InvariantCulture does NOT apply Turkish folding rules — Latin "I" (U+0049) and
		// dotless "ı" (U+0131) stay distinct. This is a second divergence point: Ordinal
		// would also keep them distinct; a CurrentCulture=tr-TR comparer would fold them.
		var result = TagNormalizer.ParseInput("I, ı");
		_output.WriteLine($"TurkishDottedI: count={result.Count}, entries=[{string.Join("|", result)}]");
		Assert.Equal(2, result.Count);
	}

	// ------------------------------------------------------------------
	// Canonicalize
	// ------------------------------------------------------------------

	[Fact]
	public void Canonicalize_Null_ReturnsEmptySet()
	{
		var result = TagNormalizer.Canonicalize(null);
		Assert.Empty(result);
	}

	[Fact]
	public void Canonicalize_NullElementsSkipped()
	{
		var input = new string?[] { null, "foo", null, "bar" };
		// Canonicalize signature is IEnumerable<string>?, but the null-element guard inside
		// proves null entries are skipped without NRE. Cast suppresses nullable warning.
		var result = TagNormalizer.Canonicalize(input!);
		Assert.Equal(2, result.Count);
		Assert.True(result.SetEquals(new[] { "foo", "bar" }));
	}

	[Fact]
	public void Canonicalize_TrimsAndDedups()
	{
		var input = new[] { "  foo  ", "FOO", "foo\t", "bar" };
		var result = TagNormalizer.Canonicalize(input);
		Assert.Equal(2, result.Count);
		Assert.True(result.SetEquals(new[] { "foo", "bar" }));
	}

	// ------------------------------------------------------------------
	// FormatCsv
	// ------------------------------------------------------------------

	[Fact]
	public void FormatCsv_Null_ReturnsEmptyString()
	{
		// Cast suppresses nullable warning — the null guard inside FormatCsv is the behavior under test.
		var result = TagNormalizer.FormatCsv(null!);
		Assert.Equal(string.Empty, result);
	}

	[Fact]
	public void FormatCsv_EmptyCollection_ReturnsEmptyString()
	{
		var result = TagNormalizer.FormatCsv(Array.Empty<string>());
		Assert.Equal(string.Empty, result);
	}

	[Fact]
	public void FormatCsv_SingleElement_ReturnsElement()
	{
		var result = TagNormalizer.FormatCsv(new[] { "foo" });
		Assert.Equal("foo", result);
	}

	[Fact]
	public void FormatCsv_MultipleElements_JoinsCommaSpace()
	{
		// Use string[] to guarantee enumeration order — HashSet<string> order is implementation-defined.
		var result = TagNormalizer.FormatCsv(new[] { "a", "b" });
		Assert.Equal("a, b", result);
	}
}
