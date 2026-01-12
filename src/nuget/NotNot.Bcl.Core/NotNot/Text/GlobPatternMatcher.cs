using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace NotNot.Text;

/// <summary>
/// Compiled pattern matcher with O(1) exact lookups and cached glob/regex patterns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hybrid Storage:</b><br/>
/// Patterns are split into exact matches (HashSet, O(1)) and wildcard patterns (compiled Regex).
/// Any pattern containing '*' is treated as a glob pattern and converted to regex.
/// </para>
/// <para>
/// <b>Performance:</b><br/>
/// Exact matches: O(1) via <see cref="HashSet{T}.Overlaps"/><br/>
/// Pattern matches: O(tags × patterns) but patterns are compiled and cached
/// </para>
/// <para>
/// <b>Glob Syntax:</b><br/>
/// <c>*</c> matches any sequence of characters. Example: <c>Tool:*</c> matches <c>Tool:Read</c>, <c>Tool:Write</c>, etc.
/// All other characters are treated literally (escaped for regex).
/// </para>
/// </remarks>
public sealed class GlobPatternMatcher
{
    // Fast O(1) for exact matches
    private readonly HashSet<string> _exactPatterns;

    // Cached compiled patterns for glob matches
    private readonly IReadOnlyList<Regex> _wildcardPatterns;

    /// <summary>
    /// Creates a pattern matcher from a list of patterns.
    /// </summary>
    /// <param name="patterns">
    /// Patterns to match. Patterns containing '*' are treated as globs.
    /// </param>
    public GlobPatternMatcher(IEnumerable<string> patterns)
    {
        (_exactPatterns, _wildcardPatterns) = CompilePatterns(patterns);
    }

    /// <summary>
    /// Creates an empty matcher (nothing matches).
    /// </summary>
    public GlobPatternMatcher() : this([]) { }

    /// <summary>
    /// Returns true if no patterns are configured.
    /// </summary>
    public bool IsEmpty => _exactPatterns.Count == 0 && _wildcardPatterns.Count == 0;

    /// <summary>
    /// Test if ANY pattern matches ANY of the given tags.
    /// </summary>
    /// <param name="tags">Tags to test against patterns.</param>
    /// <returns>True if at least one pattern matches at least one tag.</returns>
    public bool MatchesAny(FrozenSet<string> tags)
    {
        if (tags.Count == 0)
            return false;

        // Fast path: O(1) exact match check
        if (_exactPatterns.Count > 0 && _exactPatterns.Overlaps(tags))
            return true;

        // Slow path: regex matching
        if (_wildcardPatterns.Count > 0)
            return MatchesAnyPattern(tags, _wildcardPatterns);

        return false;
    }

    /// <summary>
    /// Test if ANY pattern matches ANY of the given tags.
    /// </summary>
    /// <param name="tags">Tags to test against patterns.</param>
    /// <returns>True if at least one pattern matches at least one tag.</returns>
    public bool MatchesAny(IEnumerable<string> tags)
    {
        // Fast path: O(1) exact match check via enumerable
        foreach (var tag in tags)
        {
            if (_exactPatterns.Contains(tag))
                return true;
        }

        // Slow path: regex matching (need to iterate tags again)
        if (_wildcardPatterns.Count > 0)
        {
            foreach (var tag in tags)
            {
                foreach (var pattern in _wildcardPatterns)
                {
                    if (pattern.IsMatch(tag))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Test if a single value matches any pattern.
    /// </summary>
    /// <param name="value">Single value to test.</param>
    /// <returns>True if any pattern matches the value.</returns>
    public bool Matches(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Fast path: exact match
        if (_exactPatterns.Contains(value))
            return true;

        // Slow path: regex matching
        foreach (var pattern in _wildcardPatterns)
        {
            if (pattern.IsMatch(value))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Compiles patterns into exact matches and regex patterns.
    /// </summary>
    /// <param name="patterns">Raw pattern strings.</param>
    /// <returns>Tuple of exact matches (HashSet) and wildcard patterns (compiled Regex list).</returns>
    public static (HashSet<string> exact, List<Regex> wildcards) CompilePatterns(
        IEnumerable<string> patterns)
    {
        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wildcards = new List<Regex>();

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (pattern.Contains('*'))
            {
                // Convert glob to regex: Tool:* → ^Tool:.*$
                var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                wildcards.Add(new Regex(regexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.NonBacktracking));
            }
            else
            {
                exact.Add(pattern);
            }
        }

        return (exact, wildcards);
    }

    private static bool MatchesAnyPattern(FrozenSet<string> tags, IReadOnlyList<Regex> patterns)
    {
        foreach (var tag in tags)
        {
            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(tag))
                    return true;
            }
        }
        return false;
    }
}
