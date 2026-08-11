using System.Text;

namespace NotNot.Templating;

/// <summary>
/// The single executable owner of the <c>%key%</c> template grammar: single-pass substitution with
/// configurable priority, plus an opt-in <c>?{…}</c> conditional-group dialect.
/// </summary>
/// <remarks>
/// <para>Resolution order: <see cref="OnResolveKey"/> (if non-null) → <see cref="Values"/> → leave unchanged.</para>
/// <para>Keys are case-sensitive and must match pattern: [a-zA-Z_][a-zA-Z0-9_:]* (colons allowed for prefixed keys like <c>env:VARNAME</c>)</para>
/// <para>
/// Two dialects, selected per call by <see cref="EnableConditionalGroups"/>. In the default (plain) dialect the
/// bytes <c>?</c>, <c>{</c>, <c>}</c> and <c>\</c> carry no meaning and pass through untouched — the group and
/// escape branches are never entered, so they are inert by construction rather than by escaping.
/// </para>
/// <para><see cref="Values"/> is held BY REFERENCE and is never copied, so the caller's comparer stays authoritative.</para>
/// <para>This class is immutable after construction and thread-safe (thread-safety of a supplied
/// <see cref="OnResolveKey"/> callback or <see cref="Values"/> dictionary remains the caller's concern).</para>
/// </remarks>
/// <example>
/// <code>
/// // Basic usage with dictionary
/// var result = new TemplateString
/// {
///     Template = "Hello %name%, you have %count% messages",
///     Values = new Dictionary&lt;string, string&gt; { ["name"] = "World", ["count"] = "5" }
/// }.Resolve().Text;
/// // result: "Hello World, you have 5 messages"
///
/// // With callback override (callback has priority)
/// var result2 = new TemplateString
/// {
///     Template = "Hello %name%!",
///     OnResolveKey = key =&gt; key == "name" ? "Override" : null,
///     Values = new Dictionary&lt;string, string&gt; { ["name"] = "Dictionary" }
/// }.Resolve().Text;
/// // result2: "Hello Override!" (callback takes priority)
///
/// // Enumerate the keys a template references: with no resolver, every key is declined.
/// var keys = new TemplateString { Template = "%a%/%b%" }.Resolve().UnresolvedKeys;
/// // keys: ["a", "b"]
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
	/// <remarks>
	/// Held by reference and never copied — the supplied instance's comparer (for example
	/// <see cref="StringComparer.OrdinalIgnoreCase"/>) governs every lookup.
	/// </remarks>
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
	/// Opts this call into the conditional-group dialect. Default <c>false</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// When <c>true</c>: a <c>?{…}</c> group is emitted only when every contained key resolves to a
	/// non-blank value, and drops in full (delimiters included) otherwise; a group containing NO key keeps
	/// its body and consumes its delimiters; groups do not nest (the first <c>}</c> closes) and an
	/// unterminated <c>?{</c> leaves the remainder literal; the escapes <c>\?{</c> and <c>\}</c> unescape to
	/// <c>?{</c> and <c>}</c> while every other backslash passes through.
	/// </para>
	/// <para>
	/// When <c>false</c> (default): none of the above applies — <c>?</c>, <c>{</c>, <c>}</c> and <c>\</c> are
	/// ordinary characters, so a Windows path or a literal brace in the template survives byte-for-byte.
	/// </para>
	/// </remarks>
	public bool EnableConditionalGroups { get; init; }

	/// <summary>
	/// Resolves the template with single-pass replacement, reporting both the resolved text and the keys
	/// the resolver declined.
	/// Resolution order: <see cref="OnResolveKey"/> (if non-null) → <see cref="Values"/> → leave unchanged.
	/// </summary>
	/// <returns>
	/// The resolved text plus <see cref="TemplateResolveResult.UnresolvedKeys"/>: every key the resolver
	/// DECLINED (the callback returned null AND <see cref="Values"/> missed), distinct and in source order,
	/// empty when every key resolved. The same rule applies inside and outside a conditional group — a key
	/// that resolved to a BLANK value inside a group is resolved (it merely dropped the group), while a key
	/// nothing resolved is reported wherever it sits. With neither <see cref="OnResolveKey"/> nor
	/// <see cref="Values"/> supplied, every key is declined, so the declined set IS the template's key set.
	/// </returns>
	/// <exception cref="ArgumentNullException">Thrown if Template is null (e.g., via reflection bypass of required).</exception>
	/// <remarks>
	/// <para>
	/// Single-pass resolution means values containing %key% placeholders are NOT recursively resolved.
	/// This prevents potential DoS attacks from user-provided template values. A substituted value is
	/// emitted byte-for-byte: it is never re-scanned for keys, groups, or escapes, and never contributes to
	/// <see cref="TemplateResolveResult.UnresolvedKeys"/>.
	/// </para>
	/// <para>
	/// Scratch buffers are allocated lazily on the first matched token, so a template with nothing to do
	/// returns its own <see cref="Template"/> instance with no scratch allocation. The
	/// <see cref="TemplateResolveResult"/> itself is allocated per call by design — it is the contract.
	/// </para>
	/// </remarks>
	public TemplateResolveResult Resolve()
	{
		ArgumentNullException.ThrowIfNull(Template);

		var template = Template;
		StringBuilder? sb = null;
		List<string>? unresolvedKeys = null;
		var pos = 0;

		while (pos < template.Length)
		{
			var c = template[pos];

			// Escape ownership (conditional dialect only): unescape ONLY \?{ and \} ; every other backslash is byte-identical.
			if (EnableConditionalGroups && c == '\\')
			{
				sb ??= new StringBuilder(template.Length).Append(template, 0, pos);
				if (pos + 2 < template.Length && template[pos + 1] == '?' && template[pos + 2] == '{')
				{
					sb.Append("?{");
					pos += 3;
					continue;
				}
				if (pos + 1 < template.Length && template[pos + 1] == '}')
				{
					sb.Append('}');
					pos += 2;
					continue;
				}
				sb.Append(c);
				pos++;
				continue;
			}

			// Conditional group ?{ … } (conditional dialect only).
			if (EnableConditionalGroups && c == '?' && pos + 1 < template.Length && template[pos + 1] == '{')
			{
				sb ??= new StringBuilder(template.Length).Append(template, 0, pos);
				if (TryProcessConditional(template, pos, sb, ref unresolvedKeys, out var groupEnd))
				{
					pos = groupEnd;
					continue;
				}
				// Unterminated ?{ → literal remainder.
				sb.Append(template, pos, template.Length - pos);
				break;
			}

			// %key% substitution (outside a group).
			if (c == '%' && TryMatchKey(template, pos, out var key, out var keyEnd))
			{
				sb ??= new StringBuilder(template.Length).Append(template, 0, pos);
				if (TryResolveKey(key, out var value))
				{
					sb.Append(value);
				}
				else
				{
					// Declined key outside a group → leave literal, and report it.
					AddUnresolved(ref unresolvedKeys, key);
					sb.Append(template, pos, keyEnd - pos);
				}
				pos = keyEnd;
				continue;
			}

			// No token matched: nothing is buffered until the first match, so the no-hit path allocates nothing.
			sb?.Append(c);
			pos++;
		}

		return new TemplateResolveResult
		{
			Text = sb?.ToString() ?? template,
			UnresolvedKeys = unresolvedKeys ?? (IReadOnlyList<string>)Array.Empty<string>(),
		};
	}

	/// <summary>
	/// Processes a <c>?{…}</c> group at <paramref name="pos"/> (which points at <c>?</c>, with <c>{</c> next).
	/// Emits the substituted body to <paramref name="outer"/> when no contained key is blank; drops the whole
	/// group otherwise. Returns <c>false</c> (with no emit) when the group is unterminated.
	/// </summary>
	private bool TryProcessConditional(string template, int pos, StringBuilder outer, ref List<string>? unresolvedKeys, out int groupEnd)
	{
		groupEnd = 0;
		var i = pos + 2; // past "?{"
		var body = new StringBuilder();
		var anyBlank = false;
		var terminated = false;

		while (i < template.Length)
		{
			var c = template[i];

			// Escapes inside a group (same ownership as outside).
			if (c == '\\')
			{
				if (i + 1 < template.Length && template[i + 1] == '}')
				{
					body.Append('}');
					i += 2;
					continue;
				}
				if (i + 2 < template.Length && template[i + 1] == '?' && template[i + 2] == '{')
				{
					body.Append("?{");
					i += 3;
					continue;
				}
				body.Append(c);
				i++;
				continue;
			}

			if (c == '}')
			{
				terminated = true;
				i++; // past "}"
				break;
			}

			if (c == '%' && TryMatchKey(template, i, out var key, out var keyEnd))
			{
				if (!TryResolveKey(key, out var value))
				{
					// Declined → reported (same rule as outside a group) AND counts as blank; the whole group drops.
					AddUnresolved(ref unresolvedKeys, key);
					anyBlank = true;
				}
				else if (string.IsNullOrWhiteSpace(value))
				{
					// Resolved to blank → NOT reported (the resolver answered); the whole group still drops.
					anyBlank = true;
				}
				else
				{
					body.Append(value);
				}
				i = keyEnd;
				continue;
			}

			body.Append(c);
			i++;
		}

		if (!terminated)
			return false;

		groupEnd = i;
		if (!anyBlank)
			outer.Append(body);
		return true;
	}

	/// <summary>Matches <c>%key%</c> at <paramref name="pos"/> (grammar <c>%([a-zA-Z_][a-zA-Z0-9_:]*)%</c>).</summary>
	private static bool TryMatchKey(string s, int pos, out string key, out int end)
	{
		key = "";
		end = 0;
		var i = pos + 1;
		if (i >= s.Length)
			return false;
		var first = s[i];
		if (!char.IsAsciiLetter(first) && first != '_')
			return false;
		i++;
		while (i < s.Length)
		{
			var ch = s[i];
			if (char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == ':')
				i++;
			else
				break;
		}
		if (i >= s.Length || s[i] != '%')
			return false;
		key = s[(pos + 1)..i];
		end = i + 1; // past closing '%'
		return true;
	}

	/// <summary>Resolves a key: <see cref="OnResolveKey"/> first, then <see cref="Values"/>.</summary>
	private bool TryResolveKey(string key, out string? value)
	{
		value = null;
		if (OnResolveKey is not null)
		{
			var resolved = OnResolveKey(key);
			if (resolved is not null)
			{
				value = resolved;
				return true;
			}
		}
		if (Values is not null && Values.TryGetValue(key, out var v))
		{
			value = v;
			return true;
		}
		return false;
	}

	/// <summary>Records a declined key: distinct, in source order; the list is allocated on the first decline.</summary>
	private static void AddUnresolved(ref List<string>? unresolvedKeys, string key)
	{
		unresolvedKeys ??= new List<string>();
		if (!unresolvedKeys.Contains(key))
			unresolvedKeys.Add(key);
	}
}
