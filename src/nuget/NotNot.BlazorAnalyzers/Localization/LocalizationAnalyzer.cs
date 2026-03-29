using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.Localization;

/// <summary>
/// Detects hardcoded user-facing strings in Razor markup and C# code that should use the
/// <c>@L["key"]</c> localization pattern. Scans <c>.razor</c> and <c>.razor.cs</c>
/// AdditionalTexts registered via the .props file.
/// <para>
/// Reports NNB014 for:
/// <list type="bullet">
///   <item>Hardcoded text nodes in Razor markup</item>
///   <item>Hardcoded string values in localizable HTML/component attributes (Label, Text, Title, etc.)</item>
///   <item>Hardcoded string literals assigned to localizable properties in <c>@code</c> blocks and code-behind files</item>
/// </list>
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LocalizationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for hardcoded user-facing strings.</summary>
    public const string DiagnosticId = "NNB014";

    private const string Category = "Localization";
    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>NNB014: Hardcoded user-facing string should be localized.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Hardcoded user-facing string should be localized",
        "Hardcoded string '{0}' should use @L[\"...\"] for localization",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "User-facing strings in Razor markup should use the @L[\"key\"] localization "
            + "pattern instead of hardcoded text. This enables translation and locale-aware display.",
        helpLinkUri: HelpBase + DiagnosticId);

    // ── Razor directive prefixes to skip ─────────────────────────────────

    private static readonly string[] RazorDirectivePrefixes =
    {
        "@page ", "@using ", "@inject ", "@inherits ", "@implements ",
        "@attribute ", "@typeparam ", "@layout ",
        // Also skip @page without space (e.g. @page\n) though unlikely
        "@page\t", "@page\r", "@page\n"
    };

    // ── Localizable attribute names ──────────────────────────────────────

    /// <summary>Attributes whose string values are user-facing and should be localized.</summary>
    private static readonly HashSet<string> LocalizableAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Label", "Text", "Title", "Placeholder", "HelperText",
        "AriaLabel", "RequiredError", "Description"
    };

    /// <summary>
    /// Matches attribute patterns like <c>Label="Submit"</c> or <c>Title="Hello World"</c>.
    /// Captures: group 1 = attribute name, group 2 = attribute value (without quotes).
    /// </summary>
    private static readonly Regex AttributePattern = new(
        @"(\w+)\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // ── C# string detection (Phase 3B) ──────────────────────────────────

    /// <summary>
    /// Matches C# property assignments like <c>Title = "Submit"</c> or <c>Label = "Hello"</c>.
    /// Captures: group 1 = property name, group 2 = string value.
    /// </summary>
    private static readonly Regex CSharpPropertyAssignment = new(
        @"(\w+)\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches C# string interpolation assignments like <c>Title = $"Hello {name}"</c>.
    /// Captures: group 1 = property name, group 2 = interpolated string content.
    /// </summary>
    private static readonly Regex CSharpInterpolatedAssignment = new(
        @"(\w+)\s*=\s*\$""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>Properties whose C# string assignments are user-facing and should be localized.</summary>
    private static readonly HashSet<string> LocalizableProperties = new(StringComparer.Ordinal)
    {
        "Title", "Label", "Text", "Placeholder", "HelperText",
        "AriaLabel", "RequiredError", "Description"
    };

    /// <summary>Non-localizable C# property names (technical, not user-facing).</summary>
    private static readonly HashSet<string> NonLocalizableProperties = new(StringComparer.Ordinal)
    {
        "Class", "Style", "Icon", "Route", "Href", "Id", "Name"
    };

    /// <summary>Patterns indicating a line is a logging call (skip entirely).</summary>
    private static readonly string[] LoggingPrefixes =
    {
        "_logger.Log", "Logger.Log", "logger.Log",
        "_logger.log", "Logger.log", "logger.log",
        "ILogger.Log", "Console.Write", "Console.Error",
        "Debug.Write", "Trace.Write"
    };

    /// <summary>Technical string patterns to skip (file paths, URLs, CSS classes, etc.).</summary>
    private static readonly string[] TechnicalPatterns =
    {
        "/", "\\", "://", ".cs", ".json", ".razor", ".css", ".js", ".ts", ".dll",
        ".xml", ".html", ".config", ".csproj", ".sln"
    };

    // ── DiagnosticAnalyzer overrides ──────────────────────────────────────

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    // ── Main entry point ─────────────────────────────────────────────────

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        if (IsOptedOut(context))
            return;

        foreach (var file in context.Options.AdditionalFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeFile(context, file);
        }
    }

    private static void AnalyzeFile(CompilationAnalysisContext context, AdditionalText file)
    {
        var path = file.Path;
        if (string.IsNullOrEmpty(path))
            return;

        // Skip NnDesignSamples paths
        var normalized = path.Replace('\\', '/');
        if (normalized.IndexOf("NnDesignSamples", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        var isRazorCs = path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase);
        var isRazor = !isRazorCs && path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

        // Only process .razor and .razor.cs files
        if (!isRazor && !isRazorCs)
            return;

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        if (isRazorCs)
        {
            // Code-behind: pure C# — analyze for string assignments
            AnalyzeCSharpStrings(context, file, sourceText, text);
        }
        else
        {
            // .razor file: markup + @code blocks
            var htmlComments = FindHtmlCommentRanges(text);
            var razorComments = FindRazorCommentRanges(text);
            AnalyzeRazorMarkup(context, file, sourceText, text, htmlComments, razorComments);
        }
    }

    // ── Razor markup analysis ────────────────────────────────────────────

    private static void AnalyzeRazorMarkup(
        CompilationAnalysisContext context, AdditionalText file, SourceText sourceText,
        string text, List<(int Start, int End)> htmlComments, List<(int Start, int End)> razorComments)
    {
        var insideCodeBlock = false;
        var codeBlockBraceDepth = 0;
        var insideBlockComment = false;

        foreach (var textLine in sourceText.Lines)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var lineText = textLine.ToString();
            var trimmed = lineText.Trim();
            var lineStart = textLine.Start;

            // Track @code { } blocks — analyze C# strings inside them (Phase 3B)
            if (!insideCodeBlock)
            {
                if (trimmed.StartsWith("@code", StringComparison.Ordinal))
                {
                    insideCodeBlock = true;
                    codeBlockBraceDepth = 0;
                    // Count braces on this line (skip braces inside string literals)
                    codeBlockBraceDepth += CountNetBraces(lineText);
                    // Analyze C# strings on the @code opening line (rare but possible)
                    AnalyzeCSharpLine(context, file, sourceText, lineText, lineStart, ref insideBlockComment);
                    continue;
                }
            }
            else
            {
                // Inside @code block — track brace depth (skip braces inside string literals)
                codeBlockBraceDepth += CountNetBraces(lineText);
                // Analyze C# strings on this line
                AnalyzeCSharpLine(context, file, sourceText, lineText, lineStart, ref insideBlockComment);
                if (codeBlockBraceDepth <= 0)
                    insideCodeBlock = false;
                continue;
            }

            // Skip empty/whitespace lines
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip Razor directives
            if (IsRazorDirective(trimmed))
                continue;

            // Skip lines entirely inside comments
            if (IsInComment(htmlComments, lineStart) || IsInComment(razorComments, lineStart))
                continue;

            // Check for hardcoded attribute values
            CheckAttributes(context, file, sourceText, lineText, lineStart, htmlComments, razorComments);

            // Check for hardcoded text nodes
            CheckTextNodes(context, file, sourceText, lineText, lineStart, htmlComments, razorComments);
        }
    }

    // ── Attribute detection ──────────────────────────────────────────────

    private static void CheckAttributes(
        CompilationAnalysisContext context, AdditionalText file, SourceText sourceText,
        string lineText, int lineStart,
        List<(int Start, int End)> htmlComments, List<(int Start, int End)> razorComments)
    {
        foreach (Match match in AttributePattern.Matches(lineText))
        {
            var attrName = match.Groups[1].Value;
            var attrValue = match.Groups[2].Value;

            // Only check localizable attributes
            if (!LocalizableAttributes.Contains(attrName))
                continue;

            // Skip if value is already a Razor expression
            if (attrValue.StartsWith("@", StringComparison.Ordinal))
                continue;

            // Skip pure whitespace, single-char, pure numeric values
            if (IsNonLocalizableValue(attrValue))
                continue;

            var absolutePos = lineStart + match.Groups[2].Index;

            // Skip if inside a comment
            if (IsInComment(htmlComments, absolutePos) || IsInComment(razorComments, absolutePos))
                continue;

            Report(context, file.Path, sourceText, absolutePos, attrValue.Length, Truncate(attrValue));
        }
    }

    // ── Text node detection ──────────────────────────────────────────────

    private static void CheckTextNodes(
        CompilationAnalysisContext context, AdditionalText file, SourceText sourceText,
        string lineText, int lineStart,
        List<(int Start, int End)> htmlComments, List<(int Start, int End)> razorComments)
    {
        // Extract text segments that are NOT inside HTML tags or Razor expressions
        var segments = ExtractTextSegments(lineText);

        foreach (var (segStart, segLength) in segments)
        {
            var segText = lineText.Substring(segStart, segLength);

            // Skip pure whitespace, single-char, pure numeric
            if (IsNonLocalizableValue(segText))
                continue;

            var absolutePos = lineStart + segStart;

            // Skip if inside a comment
            if (IsInComment(htmlComments, absolutePos) || IsInComment(razorComments, absolutePos))
                continue;

            Report(context, file.Path, sourceText, absolutePos, segLength, Truncate(segText));
        }
    }

    /// <summary>
    /// Extracts text segments from a Razor line that are outside HTML tags and Razor expressions.
    /// Returns list of (startIndex, length) pairs relative to the line.
    /// </summary>
    private static List<(int Start, int Length)> ExtractTextSegments(string line)
    {
        var segments = new List<(int Start, int Length)>();
        var i = 0;
        var textStart = -1;

        while (i < line.Length)
        {
            var ch = line[i];

            if (ch == '<')
            {
                // End any current text segment
                if (textStart >= 0)
                {
                    var len = i - textStart;
                    if (len > 0)
                        segments.Add((textStart, len));
                    textStart = -1;
                }
                // Skip to end of tag
                i = SkipHtmlTag(line, i);
            }
            else if (ch == '@')
            {
                // End any current text segment
                if (textStart >= 0)
                {
                    var len = i - textStart;
                    if (len > 0)
                        segments.Add((textStart, len));
                    textStart = -1;
                }
                // Skip Razor expression
                i = SkipRazorExpression(line, i);
            }
            else if (ch == '{')
            {
                // Could be part of a Razor code block — skip to matching brace
                if (textStart >= 0)
                {
                    var len = i - textStart;
                    if (len > 0)
                        segments.Add((textStart, len));
                    textStart = -1;
                }
                i = SkipBraceBlock(line, i);
            }
            else
            {
                // Regular text character
                if (textStart < 0)
                    textStart = i;
                i++;
            }
        }

        // Capture trailing text segment
        if (textStart >= 0)
        {
            var len = line.Length - textStart;
            if (len > 0)
                segments.Add((textStart, len));
        }

        return segments;
    }

    private static int SkipHtmlTag(string line, int pos)
    {
        // pos is at '<'
        var i = pos + 1;
        while (i < line.Length)
        {
            if (line[i] == '>')
                return i + 1;
            if (line[i] == '"')
            {
                // Skip quoted attribute value
                i++;
                while (i < line.Length && line[i] != '"')
                    i++;
                if (i < line.Length) i++; // skip closing quote
            }
            else if (line[i] == '\'')
            {
                i++;
                while (i < line.Length && line[i] != '\'')
                    i++;
                if (i < line.Length) i++;
            }
            else
            {
                i++;
            }
        }
        return i;
    }

    private static int SkipRazorExpression(string line, int pos)
    {
        // pos is at '@'
        if (pos + 1 >= line.Length)
            return pos + 1;

        var next = line[pos + 1];

        // @( ... ) — explicit expression
        if (next == '(')
            return SkipBalanced(line, pos + 1, '(', ')');

        // @{ ... } — code block
        if (next == '{')
            return SkipBalanced(line, pos + 1, '{', '}');

        // @L[...] — localization expression
        if (next == 'L' && pos + 2 < line.Length && line[pos + 2] == '[')
            return SkipBalanced(line, pos + 2, '[', ']');

        // @* ... *@ — Razor comment (handled elsewhere, but skip)
        if (next == '*')
        {
            var end = line.IndexOf("*@", pos + 2, StringComparison.Ordinal);
            return end >= 0 ? end + 2 : line.Length;
        }

        // @identifier or @SomeExpression — skip word chars
        var i = pos + 1;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '.'))
            i++;
        // Check for trailing ( or [ for method calls / indexers
        if (i < line.Length && line[i] == '(')
            i = SkipBalanced(line, i, '(', ')');
        else if (i < line.Length && line[i] == '[')
            i = SkipBalanced(line, i, '[', ']');

        return i;
    }

    private static int SkipBraceBlock(string line, int pos)
    {
        return SkipBalanced(line, pos, '{', '}');
    }

    /// <summary>
    /// Counts net brace depth change on a line, skipping braces inside string literals.
    /// Returns positive for net openers, negative for net closers.
    /// </summary>
    private static int CountNetBraces(string lineText)
    {
        var net = 0;
        var inString = false;
        for (var i = 0; i < lineText.Length; i++)
        {
            var ch = lineText[i];
            if (inString)
            {
                if (ch == '\\' && i + 1 < lineText.Length)
                {
                    i++; // skip escaped character
                    continue;
                }
                if (ch == '"')
                    inString = false;
                continue;
            }
            // Not inside a string
            if (ch == '"')
            {
                // Check for $" (interpolated) or @" (verbatim) prefixes — still a string
                inString = true;
            }
            else if (ch == '\'')
            {
                // Skip char literals like '{' or '}'
                if (i + 2 < lineText.Length && lineText[i + 2] == '\'')
                {
                    i += 2;
                    continue;
                }
                // Escaped char literal like '\n'
                if (i + 3 < lineText.Length && lineText[i + 1] == '\\' && lineText[i + 3] == '\'')
                {
                    i += 3;
                    continue;
                }
            }
            else if (ch == '/' && i + 1 < lineText.Length)
            {
                if (lineText[i + 1] == '/')
                    break; // rest of line is a comment
                if (lineText[i + 1] == '*')
                {
                    // Skip block comment on this line
                    var end = lineText.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + 1; // will be incremented by loop
                        continue;
                    }
                    break; // comment extends past this line
                }
            }
            else if (ch == '{') net++;
            else if (ch == '}') net--;
        }
        return net;
    }

    private static int SkipBalanced(string line, int pos, char open, char close)
    {
        var depth = 0;
        var i = pos;
        while (i < line.Length)
        {
            if (line[i] == open) depth++;
            else if (line[i] == close)
            {
                depth--;
                if (depth == 0)
                    return i + 1;
            }
            else if (line[i] == '"')
            {
                // Skip string literals inside expressions (with escape awareness)
                i++;
                while (i < line.Length && line[i] != '"')
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        i += 2; // skip escaped character
                        continue;
                    }
                    i++;
                }
            }
            i++;
        }
        return i;
    }

    // ── C# string analysis (Phase 3B) ──────────────────────────────────

    /// <summary>
    /// Analyzes a complete .razor.cs code-behind file for hardcoded string assignments
    /// to localizable properties.
    /// </summary>
    private static void AnalyzeCSharpStrings(
        CompilationAnalysisContext context, AdditionalText file, SourceText sourceText, string text)
    {
        var insideBlockComment = false;
        foreach (var textLine in sourceText.Lines)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var lineText = textLine.ToString();
            var lineStart = textLine.Start;
            AnalyzeCSharpLine(context, file, sourceText, lineText, lineStart, ref insideBlockComment);
        }
    }

    /// <summary>
    /// Analyzes a single C# line for hardcoded string assignments to localizable properties.
    /// Used by both @code block analysis and .razor.cs file analysis.
    /// Tracks multi-line block comment state via <paramref name="insideBlockComment"/>.
    /// </summary>
    private static void AnalyzeCSharpLine(
        CompilationAnalysisContext context, AdditionalText file, SourceText sourceText,
        string lineText, int lineStart, ref bool insideBlockComment)
    {
        var trimmed = lineText.Trim();

        // Handle multi-line block comment tracking
        if (insideBlockComment)
        {
            // Look for closing */ on this line
            var closeIdx = trimmed.IndexOf("*/", StringComparison.Ordinal);
            if (closeIdx < 0)
                return; // entire line is inside a block comment
            // Block comment ends partway through the line — continue with remainder
            insideBlockComment = false;
            // There could be code after the */, but for simplicity in a line-based
            // analyzer we skip the entire line when it started inside a comment
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        // Skip C# single-line comments
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return;

        // Check for block comment start
        var blockStart = trimmed.IndexOf("/*", StringComparison.Ordinal);
        if (blockStart >= 0)
        {
            // Check if the block comment closes on this same line
            var blockEnd = trimmed.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
            if (blockEnd < 0)
            {
                // Block comment spans to subsequent lines — skip this line
                insideBlockComment = true;
                return;
            }
            // Block comment opens and closes on same line — line may still have code
            // but if the entire line is the comment, skip it
            if (blockStart == 0 && blockEnd + 2 >= trimmed.Length)
                return;
        }

        // Skip attribute lines (e.g. [Parameter], [Inject])
        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.Contains("]"))
            return;

        // Skip switch case labels
        if (trimmed.StartsWith("case ", StringComparison.Ordinal))
            return;

        // Check for interpolated string assignments first (more specific pattern)
        foreach (Match match in CSharpInterpolatedAssignment.Matches(lineText))
        {
            var propName = match.Groups[1].Value;
            if (!LocalizableProperties.Contains(propName))
                continue;
            if (NonLocalizableProperties.Contains(propName))
                continue;

            // Skip if this specific match is inside a logging/nameof/typeof context
            if (IsInLoggingOrUtilityContext(lineText, match))
                continue;

            var strValue = match.Groups[2].Value;
            if (IsTechnicalString(strValue))
                continue;

            var absolutePos = lineStart + match.Groups[2].Index;
            Report(context, file.Path, sourceText, absolutePos, strValue.Length, Truncate(strValue));
        }

        // Check for regular string assignments
        foreach (Match match in CSharpPropertyAssignment.Matches(lineText))
        {
            var propName = match.Groups[1].Value;
            var strValue = match.Groups[2].Value;

            // Only flag localizable properties
            if (!LocalizableProperties.Contains(propName))
                continue;

            // Skip non-localizable property names
            if (NonLocalizableProperties.Contains(propName))
                continue;

            // Skip if already using localization (L["..."])
            if (IsLocalizedAssignment(lineText, match))
                continue;

            // Skip interpolated strings (already handled above)
            if (IsInterpolatedAssignment(lineText, match))
                continue;

            // Skip if this specific match is inside a logging/nameof/typeof context
            if (IsInLoggingOrUtilityContext(lineText, match))
                continue;

            // Skip technical patterns
            if (IsTechnicalString(strValue))
                continue;

            // Skip non-localizable values (pure numeric, single char, etc.)
            if (IsNonLocalizableValue(strValue))
                continue;

            // Skip single-word PascalCase/camelCase identifiers
            if (IsSingleWordIdentifier(strValue))
                continue;

            var absolutePos = lineStart + match.Groups[2].Index;
            Report(context, file.Path, sourceText, absolutePos, strValue.Length, Truncate(strValue));
        }
    }

    /// <summary>
    /// Returns true if the assignment uses L["..."] localization.
    /// Checks for patterns like <c>Title = L["key"]</c>.
    /// </summary>
    private static bool IsLocalizedAssignment(string lineText, Match match)
    {
        // Look for L[" right before the captured string value
        var matchStart = match.Index;
        var beforeMatch = lineText.Substring(0, matchStart + match.Groups[1].Length);
        var afterProp = lineText.Substring(matchStart + match.Groups[1].Length).TrimStart();
        // Check: after "PropName" there should be "= L[" or "= @L["
        if (afterProp.StartsWith("= L[", StringComparison.Ordinal) ||
            afterProp.StartsWith("= @L[", StringComparison.Ordinal))
            return true;
        return false;
    }

    /// <summary>
    /// Returns true if the assignment uses an interpolated string ($"...").
    /// </summary>
    private static bool IsInterpolatedAssignment(string lineText, Match match)
    {
        // Check if there's a $ before the opening quote of the string value
        var quotePos = match.Groups[2].Index - 1; // position of the opening "
        if (quotePos > 0 && lineText[quotePos - 1] == '$')
            return true;
        return false;
    }

    /// <summary>
    /// Returns true if the specific match is inside a logging call, nameof(), or typeof() context.
    /// Checks whether the text before the match on the same line contains one of these patterns,
    /// without an intervening semicolon (which would indicate a separate statement).
    /// </summary>
    private static bool IsInLoggingOrUtilityContext(string lineText, Match match)
    {
        // Look at the text before the match to see if it's inside a logging/nameof/typeof call
        var textBeforeMatch = lineText.Substring(0, match.Index);

        // Check for nameof( or typeof( that is still open (no closing paren + semicolon)
        if (textBeforeMatch.Contains("nameof(") || textBeforeMatch.Contains("typeof("))
            return true;

        // Check for logging prefixes — only if the prefix appears before this match
        // and there is no semicolon between the prefix and the match (same statement)
        foreach (var prefix in LoggingPrefixes)
        {
            var prefixIdx = textBeforeMatch.LastIndexOf(prefix, StringComparison.Ordinal);
            if (prefixIdx < 0)
                continue;

            // Check that there is no semicolon between the prefix and the match
            var between = textBeforeMatch.Substring(prefixIdx + prefix.Length);
            if (!between.Contains(";"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the string contains technical patterns (file paths, URLs, etc.).
    /// </summary>
    private static bool IsTechnicalString(string value)
    {
        foreach (var pattern in TechnicalPatterns)
        {
            if (value.Contains(pattern))
                return true;
        }
        // Check for http/https URLs
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Returns true if the string is a single PascalCase or camelCase compound identifier with
    /// no spaces. These are typically code identifiers, not user-facing text.
    /// Requires at least one internal uppercase letter (e.g., "SessionId", "firstName") to
    /// distinguish from plain English words like "Submit" or "Cancel".
    /// </summary>
    private static bool IsSingleWordIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length < 2)
            return true; // already caught by IsNonLocalizableValue

        // Must have no spaces
        if (trimmed.Contains(' '))
            return false;

        // Must be all letters/digits (identifier-like)
        foreach (var ch in trimmed)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
                return false;
        }

        // Must start with a letter
        if (!char.IsLetter(trimmed[0]))
            return false;

        // Must have at least one internal uppercase letter (compound identifier)
        // This distinguishes "SessionId", "firstName" from "Submit", "Cancel"
        var hasInternalUpper = false;
        for (var i = 1; i < trimmed.Length; i++)
        {
            if (char.IsUpper(trimmed[i]))
            {
                hasInternalUpper = true;
                break;
            }
        }

        // Also treat underscore-separated identifiers as code (e.g., "session_id")
        if (!hasInternalUpper && !trimmed.Contains('_'))
            return false;

        return true;
    }

    // ── Skip/filter helpers ──────────────────────────────────────────────

    private static bool IsRazorDirective(string trimmedLine)
    {
        foreach (var prefix in RazorDirectivePrefixes)
        {
            if (trimmedLine.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        // Also check exact match for directives without arguments
        if (trimmedLine == "@page" || trimmedLine == "@code")
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if the value is not localizable: pure whitespace, single char,
    /// pure numeric, or pure punctuation/symbols.
    /// </summary>
    private static bool IsNonLocalizableValue(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return true;
        if (trimmed.Length <= 1)
            return true;

        // Pure numeric (including decimals, negatives)
        var allNumeric = true;
        foreach (var ch in trimmed)
        {
            if (!char.IsDigit(ch) && ch != '.' && ch != '-' && ch != '+' && ch != ',')
            {
                allNumeric = false;
                break;
            }
        }
        if (allNumeric)
            return true;

        return false;
    }

    private static string Truncate(string value, int maxLength = 40)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;
        return trimmed.Substring(0, maxLength - 3) + "...";
    }

    // ── Comment range detection (reused from CssModernizationAnalyzer) ───

    private static List<(int Start, int End)> FindHtmlCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length - 3)
        {
            if (text[i] == '<' && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
            {
                var start = i;
                i += 4;
                while (i < text.Length - 2)
                {
                    if (text[i] == '-' && text[i + 1] == '-' && text[i + 2] == '>')
                    {
                        i += 3;
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

    private static List<(int Start, int End)> FindRazorCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '@' && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i < text.Length - 1)
                {
                    if (text[i] == '*' && text[i + 1] == '@')
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

    private static bool IsInComment(List<(int Start, int End)> ranges, int position)
    {
        foreach (var (s, e) in ranges)
        {
            if (position >= s && position < e) return true;
            if (s > position) break;
        }
        return false;
    }

    // ── Reporting ────────────────────────────────────────────────────────

    private static bool IsOptedOut(CompilationAnalysisContext context)
    {
        return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                   .TryGetValue("build_property.LocalizationAnalyzerEnabled", out var value) &&
               string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, string displayText)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(Diagnostic.Create(Rule, location, displayText));
    }
}
