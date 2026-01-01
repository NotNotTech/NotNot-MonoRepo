using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NotNot.BlazorAnalyzers.XRay;

/// <summary>
/// Represents a parsed element from a .razor file.
/// </summary>
internal sealed class RazorElement
{
    public string Tag { get; set; } = "";
    public int Line { get; set; }
    public Dictionary<string, string> Attributes { get; } = new();
    public string? Text { get; set; }
    public int ConditionalDepth { get; set; }
    // NOTE: SiblingIndex was removed (2026-01-01) - was generated but never used in JS matching
}

/// <summary>
/// Parses .razor file content to extract elements with their line numbers
/// and metadata for XRay correlation.
/// </summary>
internal static class RazorElementParser
{
    // Match opening tags: <MudText ...> or <div ...> (PascalCase for Blazor components, lowercase for HTML)
    // Captures: tag name
    private static readonly Regex ElementRegex = new(
        @"<([A-Z][A-Za-z0-9]*|[a-z][a-z0-9-]*)(?:\s|>|/>)",
        RegexOptions.Compiled);

    // Extract Class/class attribute value (both Blazor `Class` and HTML `class`)
    private static readonly Regex ClassRegex = new(
        @"\b[Cc]lass\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Extract Typo attribute (MudBlazor typography)
    private static readonly Regex TypoRegex = new(
        @"\bTypo\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Extract Color attribute (MudBlazor)
    private static readonly Regex ColorRegex = new(
        @"\bColor\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Extract Variant attribute (MudBlazor)
    private static readonly Regex VariantRegex = new(
        @"\bVariant\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Extract Text attribute (MudBlazor - used by MudExpansionPanel, MudChip, etc.)
    private static readonly Regex TextAttrRegex = new(
        @"\bText\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Extract Icon attribute (MudBlazor - MudIconButton, MudChip, MudNavLink, etc.)
    // Captures the icon name from patterns like Icon="@Icons.Material.Filled.Home"
    private static readonly Regex IconRegex = new(
        @"\bIcon\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Extract Href attribute (MudBlazor - MudNavLink, MudLink, MudButton, etc.)
    private static readonly Regex HrefRegex = new(
        @"\bHref\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Match text content between > and </ (single-line only)
    private static readonly Regex TextRegex = new(
        @">([^<]{1,100})</",
        RegexOptions.Compiled);

    // Match conditional directives for depth tracking
    private static readonly Regex ConditionalStartRegex = new(
        @"@(if|foreach|for|while|switch)\s*[\(\{]",
        RegexOptions.Compiled);

    private static readonly Regex ConditionalEndRegex = new(
        @"^\s*\}",
        RegexOptions.Compiled);

    // MudBlazor attribute-to-runtime class mappings
    private static readonly Dictionary<string, Func<string, string?>> MudBlazorMappings = new()
    {
        ["Typo"] = MapTypo,
        ["Color"] = MapColor,
        ["Variant"] = MapVariant,
    };

    /// <summary>
    /// Parse a .razor file and extract elements with metadata.
    /// </summary>
    public static IReadOnlyList<RazorElement> ParseElements(string content)
    {
        var elements = new List<RazorElement>();
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        int conditionalDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1; // 1-based line numbers

            // Track conditional depth changes
            var condStartMatches = ConditionalStartRegex.Matches(line);
            var condEndMatches = ConditionalEndRegex.Matches(line);

            // Increase depth for conditional starts
            foreach (Match _ in condStartMatches)
            {
                conditionalDepth++;
            }

            // Parse elements on this line
            var elementMatches = ElementRegex.Matches(line);
            foreach (Match match in elementMatches)
            {
                var tag = match.Groups[1].Value;

                // Skip code blocks, script tags, and style tags
                if (tag == "script" || tag == "style" || tag.StartsWith("@"))
                    continue;

                var element = new RazorElement
                {
                    Tag = tag,
                    Line = lineNumber,
                    ConditionalDepth = conditionalDepth
                };

                // NOTE: Previously computed afterTag but used `line` for all regex matches.
                // Since single-element-per-line is the common case, we continue using `line`.
                // Multi-element-per-line is a documented limitation (~60% accuracy).

                // Class attribute
                var classMatch = ClassRegex.Match(line);
                if (classMatch.Success)
                {
                    element.Attributes["Class"] = classMatch.Groups[1].Value;
                }

                // MudBlazor attributes with runtime class mapping
                var mudRuntimeClasses = new List<string>();

                var typoMatch = TypoRegex.Match(line);
                if (typoMatch.Success)
                {
                    element.Attributes["Typo"] = typoMatch.Groups[1].Value;
                    var mapped = MapTypo(typoMatch.Groups[1].Value);
                    if (mapped != null) mudRuntimeClasses.Add(mapped);
                }

                var colorMatch = ColorRegex.Match(line);
                if (colorMatch.Success)
                {
                    element.Attributes["Color"] = colorMatch.Groups[1].Value;
                    var mapped = MapColor(colorMatch.Groups[1].Value);
                    if (mapped != null) mudRuntimeClasses.Add(mapped);
                }

                var variantMatch = VariantRegex.Match(line);
                if (variantMatch.Success)
                {
                    element.Attributes["Variant"] = variantMatch.Groups[1].Value;
                    var mapped = MapVariant(variantMatch.Groups[1].Value);
                    if (mapped != null) mudRuntimeClasses.Add(mapped);
                }

                // Text attribute (MudExpansionPanel, MudChip, etc.)
                var textAttrMatch = TextAttrRegex.Match(line);
                if (textAttrMatch.Success)
                {
                    element.Attributes["Text"] = textAttrMatch.Groups[1].Value;
                }

                // Icon attribute (MudIconButton, MudChip, MudNavLink, etc.)
                var iconMatch = IconRegex.Match(line);
                if (iconMatch.Success)
                {
                    element.Attributes["Icon"] = iconMatch.Groups[1].Value;
                }

                // Href attribute (MudNavLink, MudLink, MudButton, etc.)
                var hrefMatch = HrefRegex.Match(line);
                if (hrefMatch.Success)
                {
                    element.Attributes["Href"] = hrefMatch.Groups[1].Value;
                }

                // Add MudBlazor runtime class mapping
                if (mudRuntimeClasses.Count > 0)
                {
                    element.Attributes["_MudRuntime"] = string.Join(" ", mudRuntimeClasses);
                }

                // Extract text content (same line only)
                var textMatch = TextRegex.Match(line.Substring(match.Index));
                if (textMatch.Success)
                {
                    var text = textMatch.Groups[1].Value.Trim();
                    // Skip if it looks like Razor code
                    if (!text.StartsWith("@") && text.Length > 0 && text.Length <= 50)
                    {
                        element.Text = text;
                    }
                }

                elements.Add(element);
            }

            // Decrease depth for conditional ends (simplified - may not be 100% accurate)
            foreach (Match _ in condEndMatches)
            {
                if (conditionalDepth > 0)
                {
                    conditionalDepth--;
                }
            }
        }

        return elements;
    }

    /// <summary>
    /// Map MudBlazor Typo attribute to runtime CSS class.
    /// e.g., "Typo.h6" → "mud-typography mud-typography-h6"
    /// </summary>
    private static string? MapTypo(string value)
    {
        // Handle "Typo.xxx" format
        var typoValue = value.StartsWith("Typo.") ? value.Substring(5) : value;

        return typoValue.ToLowerInvariant() switch
        {
            "h1" => "mud-typography mud-typography-h1",
            "h2" => "mud-typography mud-typography-h2",
            "h3" => "mud-typography mud-typography-h3",
            "h4" => "mud-typography mud-typography-h4",
            "h5" => "mud-typography mud-typography-h5",
            "h6" => "mud-typography mud-typography-h6",
            "subtitle1" => "mud-typography mud-typography-subtitle1",
            "subtitle2" => "mud-typography mud-typography-subtitle2",
            "body1" => "mud-typography mud-typography-body1",
            "body2" => "mud-typography mud-typography-body2",
            "button" => "mud-typography mud-typography-button",
            "caption" => "mud-typography mud-typography-caption",
            "overline" => "mud-typography mud-typography-overline",
            _ => null
        };
    }

    /// <summary>
    /// Map MudBlazor Color attribute to runtime CSS class.
    /// e.g., "Color.Primary" → "mud-primary-text"
    /// </summary>
    private static string? MapColor(string value)
    {
        var colorValue = value.StartsWith("Color.") ? value.Substring(6) : value;

        return colorValue.ToLowerInvariant() switch
        {
            "primary" => "mud-primary-text",
            "secondary" => "mud-secondary-text",
            "tertiary" => "mud-tertiary-text",
            "info" => "mud-info-text",
            "success" => "mud-success-text",
            "warning" => "mud-warning-text",
            "error" => "mud-error-text",
            "dark" => "mud-dark-text",
            "default" => null,
            "inherit" => null,
            _ => null
        };
    }

    /// <summary>
    /// Map MudBlazor Variant attribute to runtime CSS class.
    /// e.g., "Variant.Outlined" → "mud-button-outlined"
    /// </summary>
    private static string? MapVariant(string value)
    {
        var variantValue = value.StartsWith("Variant.") ? value.Substring(8) : value;

        return variantValue.ToLowerInvariant() switch
        {
            "text" => "mud-button-text",
            "filled" => "mud-button-filled",
            "outlined" => "mud-button-outlined",
            _ => null
        };
    }
}
