namespace NotNot.Collections;

/// <summary>
/// A single colon-delimited tag using a three-segment wire grammar
/// (<c>"name"</c>, <c>"name:value"</c>, or <c>"name:value:detail"</c>).
/// Pure, immutable value type — no I/O, DI, async, or instance state beyond the three components.
/// </summary>
/// <remarks>
/// <para>
/// This is a DISTINCT grammar from <see cref="TagNormalizer"/> (which is a comma-CSV grammar with no
/// colon support). A <see cref="Tag"/> splits a single token on the FIRST TWO colons only: segment 1 is
/// <see cref="Name"/>, segment 2 is <see cref="Value"/>, and segment 3 is <see cref="Detail"/>. Segments 2
/// and 3 are OPTIONAL. Any colons beyond the first two are NOT split and remain verbatim inside
/// <see cref="Detail"/>, so a detail may itself contain colons (e.g. <c>"a:b:c:d"</c> yields
/// <see cref="Name"/>=<c>"a"</c>, <see cref="Value"/>=<c>"b"</c>, <see cref="Detail"/>=<c>"c:d"</c>).
/// </para>
/// <para>
/// Segment presence is positional: a bare token (no colon) yields a null <see cref="Value"/> and null
/// <see cref="Detail"/>; a single-colon token yields a non-null <see cref="Value"/> and null
/// <see cref="Detail"/>; a two-or-more-colon token yields both non-null.
/// </para>
/// <para>
/// IMPORTANT — case sensitivity: the record's synthesized <c>==</c> / <see cref="Equals(Tag)"/> /
/// <see cref="GetHashCode"/> compare <see cref="Name"/>, <see cref="Value"/>, and <see cref="Detail"/>
/// using the default ORDINAL CASE-SENSITIVE string comparison. For tag-NAME matching that ignores case,
/// callers MUST use <see cref="MatchesName(string)"/> / <see cref="MatchesPrefix(string)"/> (both
/// ordinal-ignore-case), NOT record equality.
/// </para>
/// </remarks>
/// <param name="Name">The tag name segment (text before the first colon, or the whole token if no colon).</param>
/// <param name="Value">The tag value segment (text between the first and second colons), or null when the token has no colon.</param>
/// <param name="Detail">The tag detail segment (text after the second colon, retaining any further colons), or null when the token has fewer than two colons.</param>
public readonly record struct Tag(string Name, string? Value, string? Detail)
{
	/// <summary>
	/// Render this tag to its wire form, omitting trailing null segments: <c>"Name"</c> when
	/// <see cref="Value"/> is null, <c>"Name:Value"</c> when <see cref="Detail"/> is null, otherwise
	/// <c>"Name:Value:Detail"</c>.
	/// </summary>
	/// <returns>
	/// The colon-delimited string representation. Round-trips losslessly through <see cref="TryParse"/>.
	/// Note: a <see cref="Detail"/> set while <see cref="Value"/> is null (an unparseable construction)
	/// renders as <see cref="Name"/> alone, since the grammar has no slot for a value-less detail.
	/// </returns>
	public string Format() => Format(Name, Value, Detail);

	/// <summary>
	/// Render a name/value pair to the tag wire form without first constructing a <see cref="Tag"/>.
	/// Equivalent to <c>new Tag(name, value, null).Format()</c>.
	/// </summary>
	/// <param name="name">The tag name segment.</param>
	/// <param name="value">The tag value segment, or null for a bare tag.</param>
	/// <returns><c>"name"</c> when <paramref name="value"/> is null, otherwise <c>"name:value"</c>.</returns>
	public static string Format(string name, string? value) => Format(name, value, null);

	/// <summary>
	/// Render a name/value/detail triple to the tag wire form, omitting trailing null segments, without
	/// first constructing a <see cref="Tag"/>. Equivalent to <c>new Tag(name, value, detail).Format()</c>.
	/// </summary>
	/// <param name="name">The tag name segment.</param>
	/// <param name="value">The tag value segment, or null for a bare tag (forces a null detail in the output).</param>
	/// <param name="detail">The tag detail segment, or null to omit the third segment.</param>
	/// <returns>
	/// <c>"name"</c> when <paramref name="value"/> is null; <c>"name:value"</c> when <paramref name="detail"/>
	/// is null; otherwise <c>"name:value:detail"</c>.
	/// </returns>
	public static string Format(string name, string? value, string? detail)
	{
		if (value is null)
		{
			return name;
		}

		return detail is null ? $"{name}:{value}" : $"{name}:{value}:{detail}";
	}

	/// <summary>
	/// Parse a single tag token. Splits on the FIRST TWO colons only.
	/// </summary>
	/// <param name="raw">
	/// The raw token (e.g. <c>"category"</c>, <c>"category:value"</c>, or <c>"category:value:detail"</c>).
	/// Null or whitespace-only input fails. The token is NOT trimmed — leading/trailing whitespace is
	/// preserved verbatim in the parsed segments (callers that want trimming should trim before calling).
	/// </param>
	/// <param name="tag">The parsed tag on success; <c>default</c> on failure.</param>
	/// <returns>
	/// <c>true</c> when a tag was parsed; <c>false</c> for null/whitespace input. A token with no colon yields
	/// <see cref="Value"/>=null and <see cref="Detail"/>=null; a single-colon token yields a non-null
	/// <see cref="Value"/> and <see cref="Detail"/>=null; a token with two or more colons splits twice so the
	/// remainder after the second colon (which may contain further colons) becomes <see cref="Detail"/>.
	/// </returns>
	public static bool TryParse(string? raw, out Tag tag)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			tag = default;
			return false;
		}

		var colon1 = raw.IndexOf(':');
		if (colon1 < 0)
		{
			tag = new Tag(raw, null, null);
			return true;
		}

		var name = raw.Substring(0, colon1);
		var rest = raw.Substring(colon1 + 1);

		var colon2 = rest.IndexOf(':');
		if (colon2 < 0)
		{
			tag = new Tag(name, rest, null);
			return true;
		}

		var value = rest.Substring(0, colon2);
		var detail = rest.Substring(colon2 + 1);
		tag = new Tag(name, value, detail);
		return true;
	}

	/// <summary>
	/// Parse many tag tokens, skipping any that fail <see cref="TryParse"/> (null/whitespace entries).
	/// </summary>
	/// <param name="raw">Source tokens. A null enumerable yields an empty list. Null/whitespace elements are skipped.</param>
	/// <returns>A new list of successfully-parsed tags, in source enumeration order.</returns>
	public static IReadOnlyList<Tag> ParseMany(IEnumerable<string>? raw)
	{
		var list = new List<Tag>();
		if (raw is null)
		{
			return list;
		}

		foreach (var token in raw)
		{
			if (TryParse(token, out var tag))
			{
				list.Add(tag);
			}
		}

		return list;
	}

	/// <summary>
	/// Test whether this tag's <see cref="Name"/> equals <paramref name="name"/>, ignoring case (ordinal).
	/// </summary>
	/// <param name="name">The name to compare against.</param>
	/// <returns><c>true</c> when the names match under <see cref="StringComparison.OrdinalIgnoreCase"/>.</returns>
	public bool MatchesName(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Test whether this tag's <see cref="Name"/> starts with <paramref name="namePrefix"/>, ignoring case (ordinal).
	/// </summary>
	/// <param name="namePrefix">The name prefix to test (matched against the NAME segment, not the formatted tag).</param>
	/// <returns><c>true</c> when <see cref="Name"/> begins with <paramref name="namePrefix"/> under <see cref="StringComparison.OrdinalIgnoreCase"/>.</returns>
	public bool MatchesPrefix(string namePrefix) => Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase);
}
