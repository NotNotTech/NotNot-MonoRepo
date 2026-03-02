namespace NotNot;

/// <summary>
/// String template with %key% placeholder replacement.
/// Provides single-pass resolution with configurable priority.
/// </summary>
/// <remarks>
/// <para>Resolution order: <see cref="OnResolveKey"/> (if non-null) → <see cref="Values"/> → leave unchanged.</para>
/// <para>Keys are case-sensitive and must match pattern: [a-zA-Z_][a-zA-Z0-9_:]* (colons allowed for prefixed keys like <c>env:VARNAME</c>)</para>
/// <para>This class is immutable after construction and thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// // Basic usage with dictionary
/// var result = new TemplateString
/// {
///     Template = "Hello %name%, you have %count% messages",
///     Values = new Dictionary&lt;string, string&gt; { ["name"] = "World", ["count"] = "5" }
/// }.Resolve();
/// // result: "Hello World, you have 5 messages"
///
/// // With callback override (callback has priority)
/// var result2 = new TemplateString
/// {
///     Template = "Hello %name%!",
///     OnResolveKey = key => key == "name" ? "Override" : null,
///     Values = new Dictionary&lt;string, string&gt; { ["name"] = "Dictionary" }
/// }.Resolve();
/// // result2: "Hello Override!" (callback takes priority)
/// </code>
/// </example>
public sealed class TemplateString
{
	/// <summary>
	/// The template string containing %key% placeholders.
	/// </summary>
	public required string Template { get; init; }

	/// <summary>
	/// Dictionary of key-value pairs for replacement. Checked after <see cref="OnResolveKey"/>.
	/// </summary>
	public IReadOnlyDictionary<string, string>? Values { get; init; }

	/// <summary>
	/// Callback for custom key resolution. Checked first; if returns non-null, that value is used.
	/// </summary>
	/// <remarks>
	/// Use this to intercept/override keys before dictionary lookup.
	/// Return null to fall through to <see cref="Values"/> dictionary.
	/// </remarks>
	public Func<string, string?>? OnResolveKey { get; init; }

	/// <summary>
	/// Resolves template with single-pass replacement.
	/// Resolution order: <see cref="OnResolveKey"/> (if non-null) → <see cref="Values"/> → leave unchanged.
	/// </summary>
	/// <returns>Template with resolved placeholders.</returns>
	/// <exception cref="ArgumentNullException">Thrown if Template is null (e.g., via reflection bypass of required).</exception>
	/// <remarks>
	/// Single-pass resolution means values containing %key% placeholders are NOT recursively resolved.
	/// This prevents potential DoS attacks from user-provided template values.
	/// </remarks>
	public string Resolve()
	{
		ArgumentNullException.ThrowIfNull(Template);
		return Template._ResolveTemplate(key =>
		{
			// Priority 1: Callback
			if (OnResolveKey != null)
			{
				var resolved = OnResolveKey(key);
				if (resolved != null)
					return resolved;
			}

			// Priority 2: Dictionary
			if (Values != null && Values.TryGetValue(key, out var value))
				return value;

			// Priority 3: Leave unchanged
			return null;
		});
	}
}
