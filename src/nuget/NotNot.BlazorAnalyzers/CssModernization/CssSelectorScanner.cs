using System;
using System.Collections.Generic;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// Shared CSS selector-walk + comment-range scaffolding for the CssModernization consumer-reach-in
/// analyzers (NNB_CSS008 <see cref="CssNnReachInAnalyzer"/>, NNB_CSS009 <see cref="CssMudReachInAnalyzer"/>,
/// NNB_CSS011 <see cref="CssNnConsumerClassAnalyzer"/>). The double-loop (top-level selector-list spans →
/// comma-split individual selectors) and the subject-start / comment-range helpers were duplicated
/// near-verbatim across those analyzers; this is their single owner.
/// <para>
/// <b>Per-caller subject classification stays in the analyzer.</b> <see cref="ForEachSelector"/> yields
/// each individual selector + its ABSOLUTE offset into the scanned text; the caller's callback applies
/// its own rule (<c>.nns-*</c> / <c>.mud-*</c> subject token, or the consumer-class binding lookup) and
/// reports. Offset semantics are preserved exactly: the callback receives <c>listOffset + partStart</c>
/// (the same value the inlined <c>AnalyzeSingleSelector</c> calls received), so block-relative scanning
/// (NNB_CSS011's inline-<c>&lt;style&gt;</c> path, where the caller applies its own <c>baseOffset</c> at
/// the Report boundary) is unaffected.
/// </para>
/// </summary>
internal static class CssSelectorScanner
{
    /// <summary>
    /// Walks every top-level selector-list span (the text preceding each top-level <c>{</c>), splits each
    /// on commas into individual selectors, and invokes <paramref name="onSelector"/> with each
    /// individual selector text and its absolute offset within <paramref name="text"/>.
    /// <para>
    /// Brace depth tracks declaration blocks so a <c>{</c> inside a value does not derail the scan;
    /// at-rule preludes (<c>@media</c>, <c>@supports</c>, <c>@layer</c>, <c>@keyframes</c> ...) are
    /// skipped (their nested rules are visited as the scan continues). Comment spans are skipped via
    /// <paramref name="comments"/>.
    /// </para>
    /// </summary>
    public static void ForEachSelector(
        string text, List<(int Start, int End)> comments, Action<string, int> onSelector)
    {
        var selectorStart = 0;
        var depth = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (IsInComment(comments, i))
                continue;

            if (ch == '{')
            {
                if (depth == 0)
                {
                    var selectorList = text.Substring(selectorStart, i - selectorStart);
                    // Skip at-rule preludes (@media, @supports, @layer, @keyframes ...): those have no
                    // styled subject themselves; their nested rules are visited as the scan continues.
                    var trimmed = selectorList.TrimStart();
                    if (!trimmed.StartsWith("@", StringComparison.Ordinal))
                    {
                        ForEachSelectorInList(selectorList, selectorStart, onSelector);
                    }
                }
                depth++;
            }
            else if (ch == '}')
            {
                if (depth > 0)
                    depth--;
                selectorStart = i + 1;
            }
        }
    }

    /// <summary>
    /// Splits a comma-separated selector list into individual selectors and invokes
    /// <paramref name="onSelector"/> with each part + its absolute offset (<paramref name="listOffset"/>
    /// + the part's start within the list). Each comma-part is an independent selector with its own
    /// subject.
    /// </summary>
    private static void ForEachSelectorInList(
        string selectorList, int listOffset, Action<string, int> onSelector)
    {
        var partStart = 0;
        for (var i = 0; i <= selectorList.Length; i++)
        {
            var atEnd = i == selectorList.Length;
            if (atEnd || selectorList[i] == ',')
            {
                var part = selectorList.Substring(partStart, i - partStart);
                onSelector(part, listOffset + partStart);
                partStart = i + 1;
            }
        }
    }

    /// <summary>
    /// Returns the start index (within <paramref name="selector"/>) of the SUBJECT — the rightmost
    /// compound selector. Everything before this index is ancestor / state-gate / <c>::deep</c> context.
    /// </summary>
    /// <remarks>
    /// The subject begins after the last top-level combinator: descendant (whitespace), child
    /// (<c>&gt;</c>), adjacent (<c>+</c>), general sibling (<c>~</c>). Blazor's <c>::deep</c> is
    /// whitespace-separated from its following compound, so the trailing-whitespace scan already treats
    /// it as an ancestor boundary. We scan from the right and stop at the first boundary.
    /// </remarks>
    public static int FindSubjectStart(string selector)
    {
        // Normalize trailing whitespace for the scan (the subject is the last NON-empty compound).
        var end = selector.Length;
        while (end > 0 && char.IsWhiteSpace(selector[end - 1]))
            end--;

        // Walk leftward from `end` to the previous combinator boundary.
        var i = end - 1;
        while (i >= 0)
        {
            var ch = selector[i];
            if (ch == '>' || ch == '+' || ch == '~' || char.IsWhiteSpace(ch))
            {
                // Boundary found; subject starts at the next non-combinator, non-whitespace char.
                var start = i + 1;
                while (start < end && (char.IsWhiteSpace(selector[start]) ||
                       selector[start] == '>' || selector[start] == '+' || selector[start] == '~'))
                    start++;
                return start;
            }
            i--;
        }
        return 0;
    }

    /// <summary>Builds a sorted list of <c>/* ... */</c> comment ranges. Single O(N) forward pass.</summary>
    public static List<(int Start, int End)> FindCssCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '/' && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i < text.Length - 1)
                {
                    if (text[i] == '*' && text[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }
                    i++;
                }
                ranges.Add((start, i));
            }
            else
            {
                i++;
            }
        }
        return ranges;
    }

    /// <summary>
    /// Checks if a character position falls within any comment range. Ranges are sorted by start
    /// position; linear scan with early exit.
    /// </summary>
    public static bool IsInComment(List<(int Start, int End)> ranges, int position)
    {
        foreach (var (s, e) in ranges)
        {
            if (position >= s && position < e) return true;
            if (s > position) break;
        }
        return false;
    }
}
