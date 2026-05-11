namespace NotNot.Bcl.Core.EzLinkDetection;

/// <summary>
/// Lightweight text-shape helpers for link-detection consumers.
/// </summary>
public static class LinkTextHelpers
{
	/// <summary>
	/// Returns true when the input is a single bareword — no whitespace, no path separators.
	/// Used by EzLinkDetection's PathResolver to decide whether to add a filename-glob
	/// search branch (e.g. "Maybe" → search for "Maybe.*" under workspace).
	/// Empty or null = false.
	/// </summary>
	public static bool IsSingleWord(string? s)
	{
		if (string.IsNullOrEmpty(s)) return false;
		foreach (var c in s)
		{
			if (char.IsWhiteSpace(c)) return false;
			if (c == '/' || c == '\\') return false;
		}
		return true;
	}
}
