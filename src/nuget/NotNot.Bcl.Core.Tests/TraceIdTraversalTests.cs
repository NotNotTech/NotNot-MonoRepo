using NotNot.Diagnostics;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Regression tests for the TraceId ancestor walk-up in <see cref="TraceId.ToString"/>.
/// <para>
/// Previously the loop advanced its cursor (<c>current = current.From</c>) but formatted the FIXED
/// <c>From</c> node each iteration, so a 3-deep chain rendered the immediate parent's
/// <c>SourceFile:SourceLineNumber</c> repeated instead of walking distinct ancestors. These tests
/// prove the corrected traversal emits each distinct ancestor once, oldest-first.
/// </para>
/// </summary>
public class TraceIdTraversalTests
{
	/// <summary>
	/// Build a 3-deep chain where each generation is created on a distinct source line, then assert
	/// the rendered string contains each ancestor's distinct line number exactly once (no duplication
	/// of the immediate parent, which was the pre-fix symptom).
	/// </summary>
	[Fact]
	public void ToString_WithAncestorChain_WalksDistinctAncestorsOldestFirst()
	{
		// Each Generate() call captures its own [CallerLineNumber]; keep them on separate lines so the
		// line numbers are guaranteed distinct.
		var root = TraceId.Generate();
		var mid = TraceId.Generate(root);
		var leaf = TraceId.Generate(mid);

		var rendered = leaf.ToString();

		// The two ancestors (mid, then root) are prepended oldest-first. Their distinct line numbers must
		// both appear. Under the bug, only `mid` (leaf.From) appeared, duplicated — `root` never showed.
		var midMarker = $"{mid.SourceFile}:{mid.SourceLineNumber}>";
		var rootMarker = $"{root.SourceFile}:{root.SourceLineNumber}>";

		Assert.Contains(midMarker, rendered);
		Assert.Contains(rootMarker, rendered);

		// oldest-first: root's marker precedes mid's marker in the final string.
		Assert.True(
			rendered.IndexOf(rootMarker, StringComparison.Ordinal) < rendered.IndexOf(midMarker, StringComparison.Ordinal),
			$"expected root ancestor before mid ancestor in '{rendered}'");

		// The immediate parent's marker must appear exactly once (the bug rendered it N times).
		Assert.Equal(1, CountOccurrences(rendered, midMarker));
	}

	/// <summary>
	/// A single-ancestor chain must render exactly that one ancestor once (no self-repetition).
	/// </summary>
	[Fact]
	public void ToString_WithSingleAncestor_RendersItOnce()
	{
		var root = TraceId.Generate();
		var child = TraceId.Generate(root);

		var rendered = child.ToString();
		var rootMarker = $"{root.SourceFile}:{root.SourceLineNumber}>";

		Assert.Equal(1, CountOccurrences(rendered, rootMarker));
	}

	private static int CountOccurrences(string haystack, string needle)
	{
		var count = 0;
		var index = 0;
		while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += needle.Length;
		}
		return count;
	}
}
