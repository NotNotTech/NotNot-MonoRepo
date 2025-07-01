using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Configuration;

/// <summary>
/// Provides configuration support for NotNot.Analyzers via EditorConfig and AnalyzerConfigOptions
/// </summary>
public static class AnalyzerConfiguration
{
    /// <summary>
    /// Gets whether concurrent execution is enabled for analyzers
    /// </summary>
    public static bool IsConcurrentExecutionEnabled(AnalyzerConfigOptions options)
    {
        return options.TryGetValue("notNot_analyzers_enable_concurrent_execution", out var value)
            && bool.TryParse(value, out var enabled)
            && enabled;
    }

    /// <summary>
    /// Gets whether generated code analysis should be skipped
    /// </summary>
    public static bool ShouldSkipGeneratedCode(AnalyzerConfigOptions options)
    {
        return !options.TryGetValue("notNot_analyzers_skip_generated_code", out var value)
            || !bool.TryParse(value, out var skip)
            || skip; // Default to true
    }

    /// <summary>
    /// Gets the configured severity for a specific diagnostic rule
    /// </summary>
    public static DiagnosticSeverity GetDiagnosticSeverity(AnalyzerConfigOptions options, string diagnosticId, DiagnosticSeverity defaultSeverity = DiagnosticSeverity.Error)
    {
        var key = $"dotnet_diagnostic.{diagnosticId}.severity";
        if (!options.TryGetValue(key, out var severityValue))
        {
            return defaultSeverity;
        }

        return severityValue.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" or "suggestion" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            "none" or "silent" => DiagnosticSeverity.Hidden,
            _ => defaultSeverity
        };
    }

    /// <summary>
    /// Gets whether a specific diagnostic rule is enabled
    /// </summary>
    public static bool IsDiagnosticEnabled(AnalyzerConfigOptions options, string diagnosticId)
    {
        var severity = GetDiagnosticSeverity(options, diagnosticId);
        return severity != DiagnosticSeverity.Hidden;
    }

    /// <summary>
    /// Creates a DiagnosticDescriptor with configuration-aware severity
    /// </summary>
    public static DiagnosticDescriptor CreateConfigurableDescriptor(
        AnalyzerConfigOptions options,
        string id,
        string title,
        string messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity,
        bool isEnabledByDefault,
        string? description = null,
        string? helpLinkUri = null,
        params string[] customTags)
    {
        var configuredSeverity = GetDiagnosticSeverity(options, id, defaultSeverity);
        var isEnabled = isEnabledByDefault && IsDiagnosticEnabled(options, id);

        return new DiagnosticDescriptor(
            id: id,
            title: title,
            messageFormat: messageFormat,
            category: category,
            defaultSeverity: configuredSeverity,
            isEnabledByDefault: isEnabled,
            description: description,
            helpLinkUri: helpLinkUri,
            customTags: customTags);
    }
}