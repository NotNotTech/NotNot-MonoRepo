namespace NotNot.Collections;

/// <summary>
/// Pure, stateless helpers for parsing, canonicalizing, and formatting session tags.
/// Tag equality is InvariantCulture-insensitive; interior whitespace and symbols are preserved.
/// </summary>
/// <remarks>
/// All members are stateless pure functions — no I/O, DI, async, or instance state.
/// Tag sets use <see cref="StringComparer.InvariantCultureIgnoreCase"/> so that, for example,
/// <c>"Bug"</c> and <c>"bug"</c> collapse to a single entry while preserving the first inserted casing.
/// Interior whitespace is intentionally NOT collapsed: <c>"foo bar"</c> and <c>"foo  bar"</c> (two spaces)
/// remain distinct tags.
/// </remarks>
public static class TagNormalizer
{
	/// <summary>
	/// Parse comma-delimited input into a canonicalized, case-insensitive tag set.
	/// </summary>
	/// <param name="raw">
	/// Raw user input (e.g. <c>"bug, feature, needs-review"</c>). Null or whitespace-only input
	/// returns an empty set.
	/// </param>
	/// <returns>
	/// A new <see cref="HashSet{T}"/> using <see cref="StringComparer.InvariantCultureIgnoreCase"/>.
	/// Each segment is trimmed of leading/trailing whitespace; empty segments are discarded.
	/// Interior whitespace and symbols within a tag are preserved verbatim.
	/// </returns>
	public static HashSet<string> ParseInput(string? raw)
	{
		var set = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return set;
		}

		foreach (var segment in raw.Split(','))
		{
			var trimmed = segment.Trim();
			if (trimmed.Length > 0)
			{
				set.Add(trimmed);
			}
		}

		return set;
	}

	/// <summary>
	/// Rebuild a tag set from any enumerable, applying trim, empty-filter, and
	/// <see cref="StringComparer.InvariantCultureIgnoreCase"/> deduplication.
	/// </summary>
	/// <param name="tags">Source tags. Null enumerable returns an empty set. Null elements are skipped.</param>
	/// <returns>
	/// A new case-insensitive <see cref="HashSet{T}"/> containing the trimmed, non-empty, deduplicated tags.
	/// </returns>
	public static HashSet<string> Canonicalize(IEnumerable<string>? tags)
	{
		var set = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
		if (tags is null)
		{
			return set;
		}

		foreach (var tag in tags)
		{
			if (tag is null)
			{
				continue;
			}

			var trimmed = tag.Trim();
			if (trimmed.Length > 0)
			{
				set.Add(trimmed);
			}
		}

		return set;
	}

	/// <summary>
	/// Format a tag collection as a comma-space-separated string for display.
	/// </summary>
	/// <param name="tags">Tags to format. Null returns the empty string.</param>
	/// <returns>
	/// A comma-space-separated string (e.g. <c>"bug, feature"</c>). Empty collections return <see cref="string.Empty"/>.
	/// Order follows the input enumeration order — this method does NOT sort.
	/// Note: <see cref="HashSet{T}"/> enumeration order is implementation-defined; callers that require
	/// a stable display ordering should sort the collection before passing it here.
	/// </returns>
	public static string FormatCsv(IEnumerable<string> tags)
	{
		if (tags is null)
		{
			return string.Empty;
		}

		return string.Join(", ", tags);
	}
}
