namespace NotNot.Collections;

/// <summary>
/// A single name/value tag using a colon-delimited wire grammar (<c>"name"</c> or <c>"name:value"</c>).
/// Pure, immutable value type — no I/O, DI, async, or instance state beyond the two components.
/// </summary>
/// <remarks>
/// <para>
/// This is a DISTINCT grammar from <see cref="TagNormalizer"/> (which is a comma-CSV grammar with no
/// colon support). A <see cref="Tag"/> splits a single token on the FIRST colon only, so values may
/// themselves contain colons (e.g. <c>"a:b:c"</c> yields <see cref="Name"/>=<c>"a"</c>,
/// <see cref="Value"/>=<c>"b:c"</c>). A bare token with no colon yields a null <see cref="Value"/>.
/// </para>
/// <para>
/// IMPORTANT — case sensitivity: the record's synthesized <c>==</c> / <see cref="Equals(Tag)"/> /
/// <see cref="GetHashCode"/> compare <see cref="Name"/> (and <see cref="Value"/>) using the default
/// ORDINAL CASE-SENSITIVE string comparison. For tag-NAME matching that ignores case, callers MUST use
/// <see cref="MatchesName(string)"/> / <see cref="MatchesPrefix(string)"/> (both ordinal-ignore-case),
/// NOT record equality.
/// </para>
/// </remarks>
/// <param name="Name">The tag name segment (text before the first colon, or the whole token if no colon).</param>
/// <param name="Value">The tag value segment (text after the first colon), or null for a bare/no-colon tag.</param>
public readonly record struct Tag(string Name, string? Value)
{
	/// <summary>
	/// Render this tag to its wire form: <c>"Name"</c> when <see cref="Value"/> is null, otherwise <c>"Name:Value"</c>.
	/// </summary>
	/// <returns>The colon-delimited string representation. Round-trips losslessly through <see cref="TryParse"/>.</returns>
	public string Format() => Value is null ? Name : $"{Name}:{Value}";

	/// <summary>
	/// Render a name/value pair to the tag wire form without first constructing a <see cref="Tag"/>.
	/// Equivalent to <c>new Tag(name, value).Format()</c>.
	/// </summary>
	/// <param name="name">The tag name segment.</param>
	/// <param name="value">The tag value segment, or null for a bare tag.</param>
	/// <returns><c>"name"</c> when <paramref name="value"/> is null, otherwise <c>"name:value"</c>.</returns>
	public static string Format(string name, string? value) => value is null ? name : $"{name}:{value}";

	/// <summary>
	/// Parse a single tag token. Splits on the FIRST colon only.
	/// </summary>
	/// <param name="raw">
	/// The raw token (e.g. <c>"category"</c> or <c>"category:value"</c>). Null or whitespace-only
	/// input fails. The token is NOT trimmed — leading/trailing whitespace is preserved verbatim in the parsed
	/// <see cref="Name"/> / <see cref="Value"/> (callers that want trimming should trim before calling).
	/// </param>
	/// <param name="tag">The parsed tag on success; <c>default</c> on failure.</param>
	/// <returns>
	/// <c>true</c> when a tag was parsed; <c>false</c> for null/whitespace input. A token with no colon yields
	/// <see cref="Value"/>=null; a token with a colon splits once so the remainder (which may contain further
	/// colons) becomes <see cref="Value"/>.
	/// </returns>
	public static bool TryParse(string? raw, out Tag tag)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			tag = default;
			return false;
		}

		var colon = raw.IndexOf(':');
		if (colon < 0)
		{
			tag = new Tag(raw, null);
			return true;
		}

		var name = raw.Substring(0, colon);
		var value = raw.Substring(colon + 1);
		tag = new Tag(name, value);
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
