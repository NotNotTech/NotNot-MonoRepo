using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// Enforces CSS modernization rules across global CSS, scoped CSS (.razor.css), and Razor files.
/// Scans AdditionalTexts registered via the .props file during build.
/// <para>
/// Rules prevent regression of @layer, light-dark(), and other modern CSS patterns.
/// File type determines which rules apply — see individual rule documentation.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CssModernizationAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "CssModernization";
    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-file opt-out marker for NNB_CSS010 — <c>nnb_css010:allow-viewport-unit: &lt;reason&gt;</c>.</summary>
    private const string ViewportOptOutMarker = "nnb_css010:allow-viewport-unit";

    // ── Diagnostic Descriptors ──────────────────────────────────────────

    /// <summary>NNB_CSS001: No !important in global CSS (*.css only, NOT .razor.css).</summary>
    public static readonly DiagnosticDescriptor RuleNoImportant = new(
        "NNB_CSS001",
        "Avoid !important in global CSS",
        "Use @layer for specificity control instead of !important",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "!important in global CSS defeats @layer specificity ordering. "
            + "Scoped CSS (.razor.css) is exempt for ::deep MudBlazor overrides.",
        helpLinkUri: HelpBase + "NNB_CSS001");

    /// <summary>NNB_CSS002: No @media prefers-color-scheme in global CSS.</summary>
    public static readonly DiagnosticDescriptor RuleNoPrefersColorScheme = new(
        "NNB_CSS002",
        "Avoid @media prefers-color-scheme",
        "Use light-dark() instead of @media prefers-color-scheme",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "prefers-color-scheme media queries create duplicate rule sets. "
            + "Use the CSS light-dark() function for automatic theme adaptation.",
        helpLinkUri: HelpBase + "NNB_CSS002");

    /// <summary>NNB_CSS003: Hardcoded rgba black/white overlays not inside light-dark() (Info).</summary>
    public static readonly DiagnosticDescriptor RuleNoHardcodedRgba = new(
        "NNB_CSS003",
        "Consider light-dark() for rgba overlays",
        "rgba({0}) overlay may need light-dark() for theme support",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        description: "Hardcoded rgba(0,0,0,...) or rgba(255,255,255,...) overlays don't adapt to "
            + "theme changes. Wrap in light-dark() for automatic theme support.",
        helpLinkUri: HelpBase + "NNB_CSS003");

    /// <summary>NNB_CSS004: No CSS nesting (&amp;) in .razor.css files.</summary>
    public static readonly DiagnosticDescriptor RuleNoNestingInScoped = new(
        "NNB_CSS004",
        "Avoid CSS nesting in scoped CSS",
        "CSS nesting (&) in .razor.css breaks Blazor scope isolation",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Blazor scoped CSS adds b-xxxxxx attributes to each selector. CSS nesting "
            + "with & produces selectors that bypass scoping, causing styles to leak or not apply.",
        helpLinkUri: HelpBase + "NNB_CSS004");

    /// <summary>NNB_CSS005: No content-visibility in any Blazor CSS.</summary>
    public static readonly DiagnosticDescriptor RuleNoContentVisibility = new(
        "NNB_CSS005",
        "Avoid content-visibility in Blazor",
        "content-visibility conflicts with Blazor DOM diffing",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "content-visibility: auto causes the browser to skip rendering off-screen "
            + "content, conflicting with Blazor's DOM diffing algorithm.",
        helpLinkUri: HelpBase + "NNB_CSS005");

    /// <summary>NNB_CSS006: No layer= attribute on &lt;link&gt; elements (Error — always wrong).</summary>
    public static readonly DiagnosticDescriptor RuleNoLinkLayer = new(
        "NNB_CSS006",
        "Invalid layer attribute on <link>",
        "<link layer=\"...\"> is not valid HTML — use @layer in CSS",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "The layer attribute on <link> elements does not exist in any browser. "
            + "CSS layers must be declared using @layer in stylesheets.",
        helpLinkUri: HelpBase + "NNB_CSS006");

    /// <summary>NNB_CSS007: No :has() in .razor.css files.</summary>
    public static readonly DiagnosticDescriptor RuleNoHasInScoped = new(
        "NNB_CSS007",
        "Avoid :has() in scoped CSS",
        ":has() in .razor.css fails due to Blazor scope attribute mismatch",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: ":has() in scoped CSS fails because Blazor adds scope attributes to the "
            + "outer selector but not the :has() argument, causing the selector to never match.",
        helpLinkUri: HelpBase + "NNB_CSS007");

    /// <summary>NNB_CSS010: No viewport-relative units (vh/vw/vmin/vmax + d/s/l dynamic variants) in
    /// consumer CSS declaration values or .razor attribute values (Error).</summary>
    public static readonly DiagnosticDescriptor RuleNoViewportUnits = new(
        "NNB_CSS010",
        "Avoid viewport-relative units in consumer code",
        "Viewport unit '{0}' sizes against the window, not the space the layout granted — inside the "
            + "app shell it overflows the pane clip line (content hides under navbar/statusbar chrome). "
            + "Fix, cheapest-correct-first: (1) section inside an NnContentSectionGroup that should take "
            + "the remaining height → Fill=\"true\"; (2) box filling a bounded parent → height:100% or "
            + "data-nn-fill=\"container\"; (3) genuine viewport-owned surface (portal dialog/overlay, "
            + "standalone page outside the shell) → keep the unit and add a per-file "
            + "'nnb_css010:allow-viewport-unit' comment stating why; (4) only if truly intended → "
            + "dotnet_diagnostic.NNB_CSS010.severity in .editorconfig.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "The smell is viewport-relative sizing in a container-bounded context — NOT big "
            + "numbers or any specific property (fires property-agnostically on any declaration or "
            + "attribute value). Inside the app shell (which reserves navbar + statusbar chrome), a "
            + "viewport-sized box overflows the pane clip line and content is silently clipped. "
            + "Sanctioned vocabulary: NnContentSection Fill=\"true\" (group remainder), height:100% / "
            + "data-nn-fill=\"container\" (bounded parent), producer-tier data-nn-fill=\"viewport\" "
            + "(genuine viewport surfaces). Portal overlays (dialogs/popups/reconnect modal) are the "
            + "legitimate exception class — CORRECTLY viewport-relative; they get the per-file "
            + "allow-comment, not silence. Dynamic Razor values (attribute value containing '@') are "
            + "allowed. Exempt: NotNot.BlazorDesign producer internals, Pages/Samples/** + "
            + "NnDesignSamples/**, samples/global-theme CSS (nn-design-samples.css, app.css), vendor "
            + "files. Per-file opt-out: nnb_css010:allow-viewport-unit: <reason>. Kill-switch: "
            + "<CssAnalyzerEnabled>false</CssAnalyzerEnabled>.",
        helpLinkUri: HelpBase + "NNB_CSS010");

    // ── Regex Patterns ──────────────────────────────────────────────────

    /// <summary>Matches rgba(0,0,0,...) — black overlay pattern.</summary>
    private static readonly Regex RgbaBlackPattern = new(
        @"rgba\(\s*0\s*,\s*0\s*,\s*0\s*,[^)]*\)",
        RegexOptions.Compiled);

    /// <summary>Matches rgba(255,255,255,...) — white overlay pattern.</summary>
    private static readonly Regex RgbaWhitePattern = new(
        @"rgba\(\s*255\s*,\s*255\s*,\s*255\s*,[^)]*\)",
        RegexOptions.Compiled);

    /// <summary>Matches &amp; in CSS selector position (preceded by whitespace/{/; or line start).
    /// Includes - and _ for BEM-style nesting (&amp;-modifier, &amp;--variant, &amp;__element).</summary>
    private static readonly Regex NestingAmpersandPattern = new(
        @"(?:^|[\s{;])(&)(?:\s|[-_.:\[>~+{])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Matches &lt;link ... layer= in Razor HTML.</summary>
    private static readonly Regex LinkLayerPattern = new(
        @"<link\b[^>]*\blayer\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Matches a numeric viewport-relative unit token: <c>100vh</c>, <c>80vw</c>,
    /// <c>0.5vmin</c>, dynamic variants <c>100dvh</c>/<c>svh</c>/<c>lvh</c> etc. Word-bounded so
    /// identifiers (e.g. <c>divhole</c>, custom-property names) and bare unit mentions without a
    /// number do not match.</summary>
    private static readonly Regex ViewportUnitPattern = new(
        @"\b\d+(?:\.\d+)?(?:d|s|l)?v(?:h|w|min|max)\b",
        RegexOptions.Compiled);

    /// <summary>Path segments identifying vendor/third-party files to skip.</summary>
    private static readonly string[] VendorPathSegments =
    {
        "/lib/", "/node_modules/", "/xterm", "/prism", "/tiny-mde"
    };

    // ── DiagnosticAnalyzer overrides ────────────────────────────────────

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            RuleNoImportant, RuleNoPrefersColorScheme, RuleNoHardcodedRgba,
            RuleNoNestingInScoped, RuleNoContentVisibility, RuleNoLinkLayer, RuleNoHasInScoped,
            RuleNoViewportUnits);

    public override void Initialize(AnalysisContext context)
    {
        // No generated C# code analysis needed — we scan AdditionalTexts only
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    // ── Main entry point ────────────────────────────────────────────────

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        // Opt-out: set <CssAnalyzerEnabled>false</CssAnalyzerEnabled> in project
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
        if (string.IsNullOrEmpty(path) || IsVendorFile(path))
            return;

        var sourceText = file.GetText(context.CancellationToken);
        if (sourceText == null || sourceText.Length == 0)
            return;

        var text = sourceText.ToString();

        // Classification: .razor.css must be checked BEFORE .css (it ends with .css too)
        var isRazorCss = path.EndsWith(".razor.css", StringComparison.OrdinalIgnoreCase);
        var isGlobalCss = !isRazorCss && path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        var isRazor = !isRazorCss && path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

        if (isGlobalCss)
        {
            var comments = FindCssCommentRanges(text);
            CheckNoImportant(context, file, sourceText, text, comments);
            CheckNoPrefersColorScheme(context, file, sourceText, text, comments);
            CheckNoHardcodedRgba(context, file, sourceText, text, comments);
            CheckNoContentVisibility(context, file, sourceText, text, comments);
            CheckNoViewportUnitsCss(context, file, sourceText, text, comments);
        }
        else if (isRazorCss)
        {
            var comments = FindCssCommentRanges(text);
            CheckNoNestingAmpersand(context, file, sourceText, text, comments);
            CheckNoHasSelector(context, file, sourceText, text, comments);
            CheckNoContentVisibility(context, file, sourceText, text, comments);
            CheckNoViewportUnitsCss(context, file, sourceText, text, comments);
        }
        else if (isRazor)
        {
            CheckNoLinkLayer(context, file, sourceText, text);
            CheckNoViewportUnitsRazor(context, file, sourceText, text);
        }
    }

    // ── Rule implementations ────────────────────────────────────────────

    /// <summary>NNB_CSS001: No !important in global CSS.</summary>
    private static void CheckNoImportant(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        int pos = 0;
        while ((pos = text.IndexOf("!important", pos, StringComparison.Ordinal)) >= 0)
        {
            if (!IsInComment(comments, pos))
                Report(ctx, file.Path, src, pos, "!important".Length, RuleNoImportant);
            pos += "!important".Length;
        }
    }

    /// <summary>NNB_CSS002: No prefers-color-scheme in global CSS.</summary>
    private static void CheckNoPrefersColorScheme(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        int pos = 0;
        while ((pos = text.IndexOf("prefers-color-scheme", pos, StringComparison.Ordinal)) >= 0)
        {
            if (!IsInComment(comments, pos))
                Report(ctx, file.Path, src, pos, "prefers-color-scheme".Length, RuleNoPrefersColorScheme);
            pos += "prefers-color-scheme".Length;
        }
    }

    /// <summary>NNB_CSS003: Hardcoded rgba overlays not inside light-dark().</summary>
    private static void CheckNoHardcodedRgba(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        CheckRgbaPattern(ctx, file.Path, src, text, comments, RgbaBlackPattern, "0,0,0");
        CheckRgbaPattern(ctx, file.Path, src, text, comments, RgbaWhitePattern, "255,255,255");
    }

    private static void CheckRgbaPattern(
        CompilationAnalysisContext ctx, string path, SourceText src, string text,
        List<(int Start, int End)> comments, Regex pattern, string colorLabel)
    {
        foreach (Match m in pattern.Matches(text))
        {
            if (IsInComment(comments, m.Index))
                continue;

            // Skip if the line already uses light-dark() wrapping
            var line = src.Lines.GetLineFromPosition(m.Index);
            if (line.ToString().IndexOf("light-dark(", StringComparison.Ordinal) >= 0)
                continue;

            Report(ctx, path, src, m.Index, m.Length, RuleNoHardcodedRgba, colorLabel);
        }
    }

    /// <summary>NNB_CSS004: No CSS nesting (&amp;) in .razor.css.</summary>
    private static void CheckNoNestingAmpersand(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        foreach (Match m in NestingAmpersandPattern.Matches(text))
        {
            // Capturing group 1 is the & character itself
            var amp = m.Groups[1];
            if (IsInComment(comments, amp.Index))
                continue;

            // & can appear legitimately inside url() strings
            var line = src.Lines.GetLineFromPosition(amp.Index);
            if (line.ToString().IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            Report(ctx, file.Path, src, amp.Index, 1, RuleNoNestingInScoped);
        }
    }

    /// <summary>NNB_CSS005: No content-visibility in any Blazor CSS.</summary>
    private static void CheckNoContentVisibility(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        int pos = 0;
        while ((pos = text.IndexOf("content-visibility", pos, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (!IsInComment(comments, pos))
                Report(ctx, file.Path, src, pos, "content-visibility".Length, RuleNoContentVisibility);
            pos += "content-visibility".Length;
        }
    }

    /// <summary>NNB_CSS006: No layer= attribute on &lt;link&gt; in Razor files (Error).
    /// Skips matches inside HTML comments and Razor comments to avoid false positives.</summary>
    private static void CheckNoLinkLayer(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text)
    {
        var htmlComments = FindHtmlCommentRanges(text);
        var razorComments = FindRazorCommentRanges(text);

        foreach (Match m in LinkLayerPattern.Matches(text))
        {
            if (IsInComment(htmlComments, m.Index) || IsInComment(razorComments, m.Index))
                continue;

            Report(ctx, file.Path, src, m.Index, m.Length, RuleNoLinkLayer);
        }
    }

    /// <summary>NNB_CSS007: No :has() in .razor.css.</summary>
    private static void CheckNoHasSelector(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        int pos = 0;
        while ((pos = text.IndexOf(":has(", pos, StringComparison.Ordinal)) >= 0)
        {
            if (!IsInComment(comments, pos))
                Report(ctx, file.Path, src, pos, ":has(".Length, RuleNoHasInScoped);
            pos += ":has(".Length;
        }
    }

    /// <summary>NNB_CSS010: viewport-relative units in global/scoped CSS declaration values.
    /// Property-agnostic — the unit in a container-bounded context is the smell, not a specific
    /// property. Skips matches inside CSS comments; whole file exempt via path bucket or the
    /// per-file allow-comment.</summary>
    private static void CheckNoViewportUnitsCss(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text,
        List<(int Start, int End)> comments)
    {
        if (IsViewportUnitExempt(file.Path, text))
            return;

        foreach (Match m in ViewportUnitPattern.Matches(text))
        {
            if (IsInComment(comments, m.Index))
                continue;

            Report(ctx, file.Path, src, m.Index, m.Length, RuleNoViewportUnits, m.Value);
        }
    }

    /// <summary>NNB_CSS010: viewport-relative units in .razor markup.
    /// Scope discriminators: only matches INSIDE a quoted attribute value (<c>="..."</c> /
    /// <c>='...'</c>) fire — prose/doc text mentioning a unit is silent; a value containing
    /// <c>@</c> (Razor expression) is dynamic → allowed (mirrors NNB044's static-vs-dynamic
    /// discriminator at text level); HTML and Razor comments are skipped.</summary>
    private static void CheckNoViewportUnitsRazor(
        CompilationAnalysisContext ctx, AdditionalText file, SourceText src, string text)
    {
        if (IsViewportUnitExempt(file.Path, text))
            return;

        var htmlComments = FindHtmlCommentRanges(text);
        var razorComments = FindRazorCommentRanges(text);
        var attributeValues = FindQuotedAttributeValueRanges(text);

        foreach (Match m in ViewportUnitPattern.Matches(text))
        {
            if (IsInComment(htmlComments, m.Index) || IsInComment(razorComments, m.Index))
                continue;

            if (!TryGetEnclosingRange(attributeValues, m.Index, out var value))
                continue;

            // Dynamic-value skip: any '@' in the enclosing quoted value = Razor expression → allowed.
            if (text.IndexOf('@', value.Start, value.End - value.Start) >= 0)
                continue;

            Report(ctx, file.Path, src, m.Index, m.Length, RuleNoViewportUnits, m.Value);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>NNB_CSS010 exemption: path buckets + per-file allow-comment
    /// (<c>nnb_css010:allow-viewport-unit</c> anywhere in the file — portal overlays are the
    /// legitimate class).</summary>
    private static bool IsViewportUnitExempt(string filePath, string text)
    {
        if (IsViewportUnitExemptPath(filePath))
            return true;

        return text.IndexOf(ViewportOptOutMarker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Path-bucket exemption for NNB_CSS010 (conforms to CssMudReachInAnalyzer/NNB_CSS009's set):
    /// the NnDesign producer legitimately owns sanctioned viewport sizing (nn-app-shell 100vh,
    /// .nn-popup 90vw/85vh, data-nn-fill="viewport"); samples + global theme CSS are not consumer
    /// layout code.
    /// </summary>
    private static bool IsViewportUnitExemptPath(string filePath)
    {
        var p = filePath.Replace('\\', '/');

        if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        var fileName = Path.GetFileName(p);
        return ViewportExemptFileNames.Contains(fileName);
    }

    /// <summary>File-name allow-list for NNB_CSS010 — samples CSS + global theme CSS
    /// (mirrors NNB_CSS009).</summary>
    private static readonly HashSet<string> ViewportExemptFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nn-design-samples.css",
        "app.css",
    };

    /// <summary>
    /// Builds sorted ranges of quoted attribute VALUES in Razor markup: <c>="..."</c> / <c>='...'</c>
    /// (value characters between, exclusive of, the quotes). Forward O(N) scan.
    /// </summary>
    private static List<(int Start, int End)> FindQuotedAttributeValueRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '=')
            {
                var j = i + 1;
                while (j < text.Length && (text[j] == ' ' || text[j] == '\t'))
                    j++;
                if (j < text.Length && (text[j] == '"' || text[j] == '\''))
                {
                    var quote = text[j];
                    var start = j + 1;
                    var k = start;
                    while (k < text.Length && text[k] != quote)
                        k++;
                    ranges.Add((start, k));
                    i = k + 1;
                    continue;
                }
            }
            i++;
        }
        return ranges;
    }

    /// <summary>Finds the range containing <paramref name="position"/>; ranges sorted by start,
    /// linear scan with early exit (mirrors <see cref="IsInComment"/>).</summary>
    private static bool TryGetEnclosingRange(
        List<(int Start, int End)> ranges, int position, out (int Start, int End) enclosing)
    {
        foreach (var r in ranges)
        {
            if (position >= r.Start && position < r.End)
            {
                enclosing = r;
                return true;
            }
            if (r.Start > position) break;
        }
        enclosing = default;
        return false;
    }

    private static bool IsOptedOut(CompilationAnalysisContext context)
    {
        return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                   .TryGetValue("build_property.CssAnalyzerEnabled", out var value) &&
               string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static void Report(
        CompilationAnalysisContext ctx, string filePath, SourceText src,
        int position, int length, DiagnosticDescriptor rule, params object[] args)
    {
        var start = src.Lines.GetLinePosition(position);
        var end = src.Lines.GetLinePosition(position + length);
        var location = Location.Create(
            filePath,
            new TextSpan(position, length),
            new LinePositionSpan(start, end));

        ctx.ReportDiagnostic(args.Length > 0
            ? Diagnostic.Create(rule, location, args)
            : Diagnostic.Create(rule, location));
    }

    /// <summary>
    /// Builds a sorted list of /* ... */ comment ranges for O(1)-per-lookup comment detection.
    /// Single forward pass over the text — O(N) total.
    /// </summary>
    private static List<(int Start, int End)> FindCssCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        int i = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '/' && text[i + 1] == '*')
            {
                int start = i;
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
                // If no closing */ found, treat rest of file as comment
                ranges.Add((start, i));
            }
            else
            {
                i++;
            }
        }
        return ranges;
    }

    /// <summary>Builds sorted list of &lt;!-- ... --&gt; comment ranges in HTML/Razor text.</summary>
    private static List<(int Start, int End)> FindHtmlCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        int i = 0;
        while (i < text.Length - 3)
        {
            if (text[i] == '<' && text[i + 1] == '!' && text[i + 2] == '-' && text[i + 3] == '-')
            {
                int start = i;
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

    /// <summary>Builds sorted list of @* ... *@ comment ranges in Razor text.</summary>
    private static List<(int Start, int End)> FindRazorCommentRanges(string text)
    {
        var ranges = new List<(int Start, int End)>();
        int i = 0;
        while (i < text.Length - 1)
        {
            if (text[i] == '@' && text[i + 1] == '*')
            {
                int start = i;
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

    /// <summary>
    /// Checks if a character position falls within any comment range.
    /// Ranges are sorted by start position; linear scan with early exit.
    /// </summary>
    private static bool IsInComment(List<(int Start, int End)> ranges, int position)
    {
        foreach (var (s, e) in ranges)
        {
            if (position >= s && position < e) return true;
            if (s > position) break;
        }
        return false;
    }

    /// <summary>
    /// Determines if a file is a vendor/third-party CSS file that should be skipped.
    /// Matches: *.min.css, paths containing /lib/, /node_modules/, /xterm, /prism, /tiny-mde.
    /// </summary>
    private static bool IsVendorFile(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');

        if (normalized.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var segment in VendorPathSegments)
        {
            if (normalized.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}
