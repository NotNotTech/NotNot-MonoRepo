using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Analyzer that ensures ASP.NET Core controller actions marked with RequireMaybeReturn attribute
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
    private static readonly LocalizableString MessageFormat = "Controller action '{0}' marked with RequireMaybeReturn must return Maybe/Maybe<T> (current: {1})";
    private static readonly LocalizableString Description = "API controllers marked with RequireMaybeReturn attribute must use Maybe-based result contracts for consistent error handling.";
    private const string Category = "Architecture";

    private static readonly DiagnosticDescriptor Rule = new(
         DiagnosticId,
         Title,
         MessageFormat,
         Category,
         DiagnosticSeverity.Warning,  // Changed from Error to Warning for broader ecosystem compatibility
         isEnabledByDefault: true,
         description: Description);


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

        // Check if this controller or method requires Maybe returns via attribute
        if (!ShouldEnforceMaybePattern(symbol, containingType)) return;

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
    /// Determines if a method or its containing type requires Maybe return pattern via attribute.
    /// </summary>
    private static bool ShouldEnforceMaybePattern(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        // Check for method-level attribute
        if (method.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "RequireMaybeReturnAttribute" ||
            a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.RequireMaybeReturnAttribute"))
        {
            return true;
        }

        // Check for class-level attribute (inherited by default)
        if (containingType.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "RequireMaybeReturnAttribute" ||
            a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.RequireMaybeReturnAttribute"))
        {
            return true;
        }

        // Check base classes for inherited attribute
        var baseType = containingType.BaseType;
        while (baseType != null)
        {
            if (baseType.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "RequireMaybeReturnAttribute" ||
                a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.RequireMaybeReturnAttribute"))
            {
                return true;
            }
            baseType = baseType.BaseType;
        }

        return false;
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