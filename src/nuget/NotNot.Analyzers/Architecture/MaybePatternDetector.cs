using System.Linq;
using Microsoft.CodeAnalysis;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Shared utility for detecting when Maybe pattern enforcement applies.
/// </summary>
internal static class MaybePatternDetector
{
    /// <summary>
    /// Determines if a method is under Maybe enforcement. With universal enforcement,
    /// all controller methods are enforced unless explicitly excluded.
    /// Non-controller methods are enforced if they return Maybe (voluntary adoption).
    /// </summary>
    public static bool IsUnderMaybeEnforcement(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null) return false;

        // Check if excluded via MaybeReturnNotRequired attribute
        if (IsExcludedFromMaybePattern(method, containingType))
            return false;

        // Check if it's a controller - if so, enforce universally
        if (IsController(containingType))
            return true;

        // For non-controllers, check if method already returns Maybe type (voluntary adoption)
        return ReturnsMaybeType(method.ReturnType);
    }

    /// <summary>
    /// Determines if a method or its containing type is excluded from Maybe pattern enforcement.
    /// </summary>
    private static bool IsExcludedFromMaybePattern(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        // Check method-level exclusion
        if (method.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "MaybeReturnNotRequiredAttribute" ||
            a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.MaybeReturnNotRequiredAttribute"))
        {
            return true;
        }

        // Check class-level exclusion (NOT inherited by design)
        if (containingType.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "MaybeReturnNotRequiredAttribute" ||
            a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.MaybeReturnNotRequiredAttribute"))
        {
            return true;
        }

        return false;
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
    /// Determines if a type is a Maybe or Maybe&lt;T&gt; type.
    /// </summary>
    public static bool ReturnsMaybeType(ITypeSymbol returnType)
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
                return innerTypeName == "Maybe" || innerTypeName.StartsWith("Maybe<");
            }

            // Handle ConfiguredTaskAwaitable patterns
            if (namedType.Name == "ConfiguredTaskAwaitable" && namedType.TypeArguments.Length == 1)
            {
                var innerType = namedType.TypeArguments[0];
                var innerTypeName = innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return innerTypeName == "Maybe" || innerTypeName.StartsWith("Maybe<");
            }
        }

        return false;
    }
}