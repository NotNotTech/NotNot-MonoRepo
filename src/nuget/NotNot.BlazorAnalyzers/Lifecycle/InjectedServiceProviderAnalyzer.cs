using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects <c>[Inject] IServiceProvider</c> properties in Blazor components.
/// Injecting the service provider enables the service locator anti-pattern and risks
/// ObjectDisposedException during disposal. Inject specific services directly instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class InjectedServiceProviderAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB017";

    private static readonly LocalizableString Title = "Avoid injecting IServiceProvider into Blazor components";
    private static readonly LocalizableString MessageFormat =
        "Property '{0}' injects IServiceProvider into a Blazor component. This enables the service locator anti-pattern and risks ObjectDisposedException during disposal. Inject specific services directly instead, or cache resolved services during initialization.";
    private static readonly LocalizableString Description =
        "Injecting IServiceProvider into Blazor components encourages the service locator anti-pattern, " +
        "makes dependencies opaque, and risks ObjectDisposedException when services are resolved outside " +
        "the component's safe lifecycle window. Inject specific services directly instead.";
    private const string Category = "Lifecycle";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        if (typeSymbol.TypeKind != TypeKind.Class)
            return;

        if (!BlazorLifecycleHelpers.IsBlazorComponent(typeSymbol))
            return;

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            if (!HasInjectAttribute(property))
                continue;

            if (!IsServiceProviderType(property.Type))
                continue;

            // Report on the property's declaring syntax location
            var location = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation();
            if (location != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    location,
                    property.Name));
            }
        }
    }

    private static bool HasInjectAttribute(IPropertySymbol property)
    {
        return property.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "InjectAttribute" &&
            attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }

    private static bool IsServiceProviderType(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider" &&
               type.ContainingNamespace?.ToDisplayString() == "System";
    }
}
