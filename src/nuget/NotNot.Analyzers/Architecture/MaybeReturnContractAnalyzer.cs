using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Analyzer that ensures ASP.NET Core controller actions in core feature namespaces
/// return Maybe or Maybe&lt;T&gt; for consistent error handling per architectural decisions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MaybeReturnContractAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_A001";

    private static readonly LocalizableString Title = "Endpoint should return Maybe or Maybe<T>";
    private static readonly LocalizableString MessageFormat = "Controller action '{0}' in core feature namespace must return Maybe/Maybe<T> (current: {1})";
    private static readonly LocalizableString Description = "Core feature API controllers must use Maybe-based result contracts for consistent error handling.";
    private const string Category = "Architecture";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    /// <summary>
    /// Default namespace prefixes that require Maybe return contracts.
    /// Can be overridden via .editorconfig.
    /// </summary>
    private static readonly string[] DefaultCoreApiNamespacePrefixes = new[]
    {
        "Cleartrix.Cloud.Feature.Account.Api",
        "Cleartrix.Cloud.Feature.EzAccess.Api",
        "Cleartrix.Cloud.Feature.NewAudit.Api",
        "Cleartrix.Cloud.Feature.Localization.Api",
        "Cleartrix.Cloud.Persistence.Api"
    };

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax method) return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (symbol == null) return;

        var containingType = symbol.ContainingType;
        if (containingType == null) return;

        // Only analyze public methods on controller classes
        if (symbol.DeclaredAccessibility != Accessibility.Public) return;
        if (!IsController(containingType)) return;

        // Check if this controller is in a namespace that requires Maybe returns
        var ns = containingType.ContainingNamespace?.ToDisplayString();
        if (ns == null || !ShouldEnforceMaybePattern(ns, context)) return;

        // Check if return type follows Maybe pattern
        if (IsMaybeReturnType(symbol.ReturnType)) return;

        // Report diagnostic for non-compliant methods
        var returnTypeName = symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            method.Identifier.GetLocation(),
            symbol.Name,
            returnTypeName));
    }

    /// <summary>
    /// Determines if a type is an ASP.NET Core controller.
    /// </summary>
    private static bool IsController(INamedTypeSymbol type)
    {
        // Check for [ApiController] attribute
        if (type.GetAttributes().Any(a => a.AttributeClass?.Name == "ApiControllerAttribute"))
            return true;

        // Check if inherits from Controller or ControllerBase
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "Controller" || baseType.Name == "ControllerBase")
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Determines if a namespace should enforce Maybe return pattern.
    /// </summary>
    private static bool ShouldEnforceMaybePattern(string namespaceName, SyntaxNodeAnalysisContext context)
    {
        // Try to get configured namespaces from .editorconfig
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
        if (options.TryGetValue($"dotnet_diagnostic.{DiagnosticId}.required_namespaces", out var configuredNamespaces))
        {
            var namespaces = configuredNamespaces.Split(',').Select(n => n.Trim());
            return namespaces.Any(prefix => namespaceName.StartsWith(prefix));
        }

        // Fall back to default namespaces
        return DefaultCoreApiNamespacePrefixes.Any(prefix => namespaceName.StartsWith(prefix));
    }

    /// <summary>
    /// Determines if a return type follows the Maybe pattern.
    /// </summary>
    private static bool IsMaybeReturnType(ITypeSymbol returnType)
    {
        var typeName = returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        // Direct Maybe or Maybe<T>
        if (typeName == "Maybe" || typeName.StartsWith("Maybe<"))
            return true;

        // Unwrap Task/ValueTask wrappers
        if (returnType is INamedTypeSymbol namedType)
        {
            if ((namedType.Name == "Task" || namedType.Name == "ValueTask") && namedType.TypeArguments.Length == 1)
            {
                var innerType = namedType.TypeArguments[0];
                var innerTypeName = innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (innerTypeName == "Maybe" || innerTypeName.StartsWith("Maybe<"))
                    return true;
            }

            // Handle ConfiguredTaskAwaitable patterns
            if (namedType.Name == "ConfiguredTaskAwaitable" && namedType.TypeArguments.Length == 1)
            {
                var innerType = namedType.TypeArguments[0];
                var innerTypeName = innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (innerTypeName == "Maybe" || innerTypeName.StartsWith("Maybe<"))
                    return true;
            }
        }

        return false;
    }
}