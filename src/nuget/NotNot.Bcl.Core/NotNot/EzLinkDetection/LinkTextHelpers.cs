namespace NotNot.Bcl.Core.EzLinkDetection;

/// <summary>
/// Lightweight text-shape helpers for link-detection consumers.
/// </summary>
/// <remarks>
/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
/// </remarks>
public static class LinkTextHelpers
{
	/// <summary>
	/// Returns true when the input is a single bareword — no whitespace, no path separators.
	/// Used by EzLinkDetection's PathResolver to decide whether to add a filename-glob
	/// search branch (e.g. "Maybe" → search for "Maybe.*" under workspace).
	/// Empty or null = false.
	/// </summary>
	/// <remarks>
	/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
	/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
	/// </remarks>
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

	/// <summary>
	/// Folds the single Unicode horizontal-ellipsis character <c>…</c> (U+2026) into the
	/// three-character ASCII run <c>...</c>. Pure text normalization: changes only character
	/// indices/length downstream, never link classification. Run BEFORE link detection so the
	/// ellipsis-bearing segment survives the InferredPath char-class (which admits ASCII <c>.</c>
	/// but not <c>…</c>). Returns the input unchanged when no U+2026 is present.
	/// </summary>
	/// <remarks>
	/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
	/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
	/// </remarks>
	public static string NormalizeEllipsis(string text)
	{
		if (string.IsNullOrEmpty(text) || !text.Contains('…'))
			return text;
		return text.Replace("…", "...");
	}

	/// <summary>
	/// Extracts the whitespace-bounded segment containing <paramref name="cursorPosition"/>.
	/// Walks back to whitespace/start, walks forward to whitespace/end, and returns the
	/// substring plus its start index plus its length. Returns <c>null</c> when the cursor
	/// sits on whitespace or out of bounds (cursorPosition &lt; 0 or &gt;= text.Length, or
	/// the text is null/empty).
	/// </summary>
	/// <remarks>
	/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
	/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
	/// </remarks>
	public static (string Text, int Start, int Length)? ExtractWhitespaceBoundary(string text, int cursorPosition)
	{
		if (string.IsNullOrEmpty(text) || cursorPosition < 0 || cursorPosition >= text.Length)
			return null;
		if (char.IsWhiteSpace(text[cursorPosition]))
			return null;

		// Scan left to whitespace or BOL
		var left = cursorPosition;
		while (left > 0 && !char.IsWhiteSpace(text[left - 1]))
			left--;

		// Scan right to whitespace or EOL
		var right = cursorPosition;
		while (right < text.Length - 1 && !char.IsWhiteSpace(text[right + 1]))
			right++;

		var length = right - left + 1;
		if (length <= 0)
			return null;

		return (text.Substring(left, length), left, length);
	}

	/// <summary>
	/// Returns the (inclusive) bounds of the whitespace-bounded segment containing
	/// <paramref name="col"/> in <paramref name="line"/>. Returns <c>null</c> when the cursor
	/// sits on whitespace or out of bounds (col &lt; 0 or &gt;= line.Length, or the line
	/// is null/empty).
	/// </summary>
	/// <remarks>
	/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
	/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
	/// </remarks>
	public static (int Start, int End)? GetSegmentBoundsAt(string line, int col)
	{
		if (string.IsNullOrEmpty(line) || col < 0 || col >= line.Length)
			return null;
		if (char.IsWhiteSpace(line[col]))
			return null;

		var left = col;
		while (left > 0 && !char.IsWhiteSpace(line[left - 1]))
			left--;
		var right = col;
		while (right < line.Length - 1 && !char.IsWhiteSpace(line[right + 1]))
			right++;
		return (left, right);
	}

	/// <summary>
	/// Returns (start, end) inclusive bounds of the first whitespace-bounded segment in
	/// <paramref name="line"/>, or <c>null</c> when the line is null, empty, or
	/// entirely whitespace.
	/// </summary>
	/// <remarks>
	/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
	/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
	/// </remarks>
	public static (int Start, int End)? GetFirstSegmentBounds(string line)
	{
		if (string.IsNullOrEmpty(line)) return null;
		var start = 0;
		while (start < line.Length && char.IsWhiteSpace(line[start])) start++;
		if (start >= line.Length) return null;
		var end = start;
		while (end < line.Length - 1 && !char.IsWhiteSpace(line[end + 1])) end++;
		return (start, end);
	}

	/// <summary>
	/// Returns (start, end) inclusive bounds of the last whitespace-bounded segment in
	/// <paramref name="line"/>, or <c>null</c> when the line is null, empty, or
	/// entirely whitespace.
	/// </summary>
	/// <remarks>
	/// Package: <c>NotNot.Bcl.Core</c> · Namespace: <c>NotNot.Bcl.Core.EzLinkDetection</c><br/>
	/// Using directive: <c>using NotNot.Bcl.Core.EzLinkDetection;</c>
	/// </remarks>
	public static (int Start, int End)? GetLastSegmentBounds(string line)
	{
		if (string.IsNullOrEmpty(line)) return null;
		var end = line.Length - 1;
		while (end >= 0 && char.IsWhiteSpace(line[end])) end--;
		if (end < 0) return null;
		var start = end;
		while (start > 0 && !char.IsWhiteSpace(line[start - 1])) start--;
		return (start, end);
	}
}
