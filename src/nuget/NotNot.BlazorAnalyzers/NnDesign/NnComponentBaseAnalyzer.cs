using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB020: Blazor components in the NnDesign namespace must inherit from NnComponentBase.
/// </summary>
/// <remarks>
/// Ensures the consistent base class contract established across all NnDesign components:
/// CSS class composition via ComponentCssClass, XRay instrumentation, parameter forwarding
/// (Class, Style, AdditionalAttributes), and lifecycle hooks.
///
/// Uses RegisterSymbolAction(NamedType) for semantic analysis — this gives the full inheritance
/// chain including generated base classes from @inherits directives, which syntax-level analysis
/// would miss for transitive inheritance (e.g., NnPopupCallout → NnPopupBase → NnComponentBase).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NnComponentBaseAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB020";

    private const string NnDesignNamespace = "NotNot.BlazorDesign.NnDesign";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "NnDesign component must inherit NnComponentBase",
        messageFormat: "Type '{0}' is a Blazor component in the NnDesign namespace but does not inherit from NnComponentBase",
        category: "NnDesign Convention",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "All Blazor components in the NotNot.BlazorDesign.NnDesign namespace must inherit from " +
            "NnComponentBase to ensure consistent behavior (CSS class composition, XRay instrumentation, " +
            "parameter forwarding).",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // Must analyze generated code because Razor components compile to generated C#
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;

        // Only check classes (not interfaces, enums, structs, etc.)
        if (namedType.TypeKind != TypeKind.Class)
            return;

        // Only check types in NnDesign namespace (including sub-namespaces)
        if (!IsInNnDesignNamespace(namedType.ContainingNamespace?.ToDisplayString()))
            return;

        // Only check types that inherit ComponentBase (skips utility classes like NnClasses, NnContextMenuItem)
        if (!InheritsFromComponentBase(namedType))
            return;

        // Pass if type is or inherits NnComponentBase
        if (IsOrInheritsNnComponentBase(namedType))
            return;

        // Guard: metadata-only or error symbols may have no locations
        if (namedType.Locations.IsEmpty)
            return;

        // Violation: component in NnDesign namespace inheriting ComponentBase but not NnComponentBase
        context.ReportDiagnostic(Diagnostic.Create(Rule, namedType.Locations[0], namedType.Name));
    }

    private static bool IsInNnDesignNamespace(string? ns) =>
        ns is not null &&
        (ns == NnDesignNamespace ||
         (ns.StartsWith(NnDesignNamespace, StringComparison.Ordinal) &&
          ns.Length > NnDesignNamespace.Length &&
          ns[NnDesignNamespace.Length] == '.'));

    /// <summary>
    /// Checks whether <paramref name="type"/> is NnComponentBase itself or inherits from it.
    /// Uses simple name matching — "NnComponentBase" is distinctive enough within this codebase.
    /// </summary>
    private static bool IsOrInheritsNnComponentBase(INamedTypeSymbol type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.Name == "NnComponentBase")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Checks whether <paramref name="type"/> ultimately derives from
    /// <c>Microsoft.AspNetCore.Components.ComponentBase</c>.
    /// </summary>
    private static bool InheritsFromComponentBase(INamedTypeSymbol type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.Name == "ComponentBase" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components")
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
