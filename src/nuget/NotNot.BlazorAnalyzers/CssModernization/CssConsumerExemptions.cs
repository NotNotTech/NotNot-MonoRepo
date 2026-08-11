using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.CssModernization;

/// <summary>
/// Shared exemption / kill-switch predicates for the CssModernization consumer-reach-in analyzers
/// (NNB_CSS008 <see cref="CssNnReachInAnalyzer"/>, NNB_CSS009 <see cref="CssMudReachInAnalyzer"/>,
/// NNB_CSS011 <see cref="CssNnConsumerClassAnalyzer"/>). Each predicate was duplicated near-verbatim
/// across those analyzers; this is their single owner.
/// <para>
/// Each analyzer keeps its OWN <c>DiagnosticDescriptor</c>, subject classification, and per-rule
/// opt-out MARKER string — only the mechanical path/comment/build-property scaffolding lives here.
/// The <see cref="HasOptOutComment"/> marker is threaded as a parameter so each rule passes its own
/// (<c>nnb_css008:allow-reachin</c> / <c>nnb_css009:allow-reachin</c> / <c>nnb_css011:allow-reachin</c>).
/// </para>
/// </summary>
internal static class CssConsumerExemptions
{
    /// <summary>Path segments identifying vendor/third-party files to skip.</summary>
    private static readonly string[] VendorPathSegments =
    {
        "/lib/", "/node_modules/", "/xterm", "/prism", "/tiny-mde"
    };

    /// <summary>
    /// File-name allow-list — samples CSS + global theme CSS, where namespaced roots / wrapper-level
    /// overrides are authored legitimately rather than reached into from a feature component.
    /// </summary>
    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nn-design-samples.css",
        "app.css",
    };

    /// <summary>
    /// Determines if a file is a vendor/third-party CSS file that should be skipped.
    /// Matches: <c>*.min.css</c>, paths containing <c>/lib/</c>, <c>/node_modules/</c>, <c>/xterm</c>,
    /// <c>/prism</c>, <c>/tiny-mde</c>.
    /// </summary>
    public static bool IsVendorFile(string filePath)
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

    /// <summary>
    /// Path-bucket exemption. Producer CSS / wrapper internals (<c>NotNot.BlazorDesign</c> and
    /// <c>NotNot.BlazorDesign.Desktop</c>) legitimately DEFINE / restyle namespaced classes; samples
    /// (<c>NnDesignSamples/</c>, <c>Pages/Samples/</c>) + global theme (the file-name allow-list) are not
    /// consumers reaching in.
    /// </summary>
    public static bool IsExceptedPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var p = filePath.Replace('\\', '/');

        // The NnDesign producer / wrapper layers legitimately define/restyle namespaced classes.
        if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0 ||
            p.IndexOf("/NotNot.BlazorDesign.Desktop/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Samples pages — both the canonical NnDesignSamples folder and the generic Pages/Samples bucket.
        if (p.IndexOf("/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Specific sample / global-theme CSS files (own-roots / namespaced classes live here legitimately).
        var fileName = Path.GetFileName(p);
        return AllowedFileNames.Contains(fileName);
    }

    /// <summary>
    /// Whole-file per-file opt-out scan. <paramref name="optOutMarker"/> is the rule-specific marker
    /// (e.g. <c>nnb_css008:allow-reachin</c>); coarse whole-file <c>IndexOf</c> semantics, identical
    /// across the sibling analyzers.
    /// </summary>
    public static bool HasOptOutComment(string fileText, string optOutMarker)
    {
        if (string.IsNullOrEmpty(fileText))
            return false;
        return fileText.IndexOf(optOutMarker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Shared kill-switch: <c>&lt;CssAnalyzerEnabled&gt;false&lt;/CssAnalyzerEnabled&gt;</c> build property.
    /// </summary>
    public static bool IsOptedOut(CompilationAnalysisContext context)
    {
        return context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                   .TryGetValue("build_property.CssAnalyzerEnabled", out var value) &&
               string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }
}
