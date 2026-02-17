using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB021: NnDesign components that render DOM elements should include a data-nnxid attribute.
/// </summary>
/// <remarks>
/// Implemented as an <see cref="IIncrementalGenerator"/> rather than a DiagnosticAnalyzer because
/// DiagnosticAnalyzers cannot access AdditionalTexts (.razor file content). The .razor files are
/// registered as AdditionalTexts via the Razor SDK, and a source generator can read them directly,
/// perform text analysis, and report diagnostics with accurate .razor file locations.
///
/// Detection logic:
/// 1. Filter to .razor files in the NnDesign namespace (via @namespace directive)
/// 2. Verify the file is a component (has @inherits directive)
/// 3. Check for DOM elements in markup section only (lowercase HTML tags — Blazor components use PascalCase)
/// 4. Check for data-nnxid attribute presence
///
/// Renderless components (e.g., NnToCSection — code-only, no markup) naturally pass because
/// they have no DOM elements.
///
/// Suppress per-file via .editorconfig:
///   [NnToCSection.razor]
///   dotnet_diagnostic.NNB021.severity = none
/// </remarks>
[Generator(LanguageNames.CSharp)]
public class NnXrayAttributeAnalyzer : IIncrementalGenerator
{
    /// <summary>
    /// Diagnostic ID for the generator-reported rule.
    /// </summary>
    public const string DiagnosticId = "NNB021";

    private const string NnDesignNamespacePrefix = "NotNot.BlazorDesign.NnDesign";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "NnDesign component missing data-nnxid",
        messageFormat: "Component '{0}' in NnDesign namespace renders DOM elements but has no data-nnxid attribute for XRay instrumentation",
        category: "NnDesign Convention",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "NnDesign components that render DOM elements should include " +
            "data-nnxid=\"@Rh.XrayId()\" on their root element for XRay instrumentation support.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    // @namespace directive followed by the namespace value
    private static readonly Regex NamespaceRegex = new(@"@namespace\s+(\S+)", RegexOptions.Compiled);

    // @inherits directive followed by the base type name
    private static readonly Regex InheritsRegex = new(@"@inherits\s+(\S+)", RegexOptions.Compiled);

    // Opening HTML tag: lowercase letter = native DOM element, not a Blazor component (PascalCase)
    private static readonly Regex HtmlElementRegex = new(@"<[a-z][a-zA-Z0-9]*[\s>/@]", RegexOptions.Compiled);

    // Actual @code directive (not mentions in Razor comments).
    // Multiline anchors ^ to start-of-line; requires opening brace to distinguish from @* ...@code... *@
    private static readonly Regex CodeBlockRegex = new(@"^@code\s*\{", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var violations = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, ct) => AnalyzeRazorFile(file, ct))
            .Where(static v => v.FilePath is not null);

        context.RegisterSourceOutput(violations, static (spc, violation) =>
        {
            var location = Location.Create(
                violation.FilePath!,
                new TextSpan(violation.SpanStart, violation.SpanLength),
                new LinePositionSpan(violation.StartPos, violation.EndPos));

            spc.ReportDiagnostic(Diagnostic.Create(Rule, location, violation.FileName));
        });
    }

    private static ViolationData AnalyzeRazorFile(AdditionalText file, CancellationToken ct)
    {
        var text = file.GetText(ct);
        if (text is null)
            return default;

        var content = text.ToString();

        // 1. Check @namespace — must be in NnDesign namespace (exact match or sub-namespace)
        var nsMatch = NamespaceRegex.Match(content);
        if (!nsMatch.Success)
            return default;

        var ns = nsMatch.Groups[1].Value;
        if (!IsNnDesignNamespace(ns))
            return default;

        // 2. Check @inherits — presence indicates this is a component file
        //    (filters out _Imports.razor and other non-component .razor files)
        var inheritsMatch = InheritsRegex.Match(content);
        if (!inheritsMatch.Success)
            return default;

        // 3. Check for DOM elements (lowercase HTML tags like <div>, <section>, <nav>)
        //    Blazor component tags use PascalCase and won't match.
        //    Renderless components (code-only, no markup) naturally pass this gate.
        //
        //    IMPORTANT: Only check the markup section (before @code block). XML doc comments
        //    inside @code blocks contain tags like <summary>, <remarks>, <see> that would
        //    false-positive as HTML elements.
        var markupContent = content;
        var codeBlockMatch = CodeBlockRegex.Match(content);
        if (codeBlockMatch.Success)
            markupContent = content.Substring(0, codeBlockMatch.Index);

        if (!HtmlElementRegex.IsMatch(markupContent))
            return default;

        // 4. Check for data-nnxid in markup section (not in Razor comments or @code blocks)
        if (markupContent.Contains("data-nnxid"))
            return default;

        // Violation: NnDesign component with DOM output but no data-nnxid
        // Point the diagnostic at the @inherits line for actionable location
        var startPos = text.Lines.GetLinePosition(inheritsMatch.Index);
        var endPos = text.Lines.GetLinePosition(inheritsMatch.Index + inheritsMatch.Length);

        return new ViolationData(
            file.Path,
            Path.GetFileNameWithoutExtension(file.Path),
            inheritsMatch.Index,
            inheritsMatch.Length,
            startPos,
            endPos);
    }

    private static bool IsNnDesignNamespace(string ns) =>
        ns == NnDesignNamespacePrefix ||
        (ns.StartsWith(NnDesignNamespacePrefix, StringComparison.Ordinal) &&
         ns.Length > NnDesignNamespacePrefix.Length &&
         ns[NnDesignNamespacePrefix.Length] == '.');

    /// <summary>
    /// Value type for incremental generator pipeline caching.
    /// All fields are value-equatable to support correct incremental behavior.
    /// </summary>
    private readonly struct ViolationData : IEquatable<ViolationData>
    {
        public readonly string? FilePath;
        public readonly string? FileName;
        public readonly int SpanStart;
        public readonly int SpanLength;
        public readonly LinePosition StartPos;
        public readonly LinePosition EndPos;

        public ViolationData(string filePath, string fileName, int spanStart, int spanLength,
            LinePosition startPos, LinePosition endPos)
        {
            FilePath = filePath;
            FileName = fileName;
            SpanStart = spanStart;
            SpanLength = spanLength;
            StartPos = startPos;
            EndPos = endPos;
        }

        public bool Equals(ViolationData other) =>
            FilePath == other.FilePath &&
            FileName == other.FileName &&
            SpanStart == other.SpanStart &&
            SpanLength == other.SpanLength &&
            StartPos.Equals(other.StartPos) &&
            EndPos.Equals(other.EndPos);

        public override bool Equals(object obj) => obj is ViolationData other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = FilePath?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ (FileName?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ SpanStart;
                hash = (hash * 397) ^ SpanLength;
                hash = (hash * 397) ^ StartPos.GetHashCode();
                hash = (hash * 397) ^ EndPos.GetHashCode();
                return hash;
            }
        }
    }
}
