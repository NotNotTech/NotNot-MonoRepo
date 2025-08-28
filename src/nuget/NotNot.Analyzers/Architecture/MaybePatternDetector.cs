using System.Linq;
using Microsoft.CodeAnalysis;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Shared utility for detecting when Maybe pattern enforcement applies.
/// </summary>
internal static class MaybePatternDetector
{
    /// <summary>
    /// Determines if a method is under Maybe enforcement either through explicit attribute
    /// or by returning a Maybe type (indicating voluntary adoption).
    /// </summary>
    public static bool IsUnderMaybeEnforcement(IMethodSymbol method)
    {
        // Check if method has RequireMaybeReturn attribute
        if (HasRequireMaybeReturnAttribute(method))
            return true;

        // Check if containing type has RequireMaybeReturn attribute
        var containingType = method.ContainingType;
        if (containingType != null && HasRequireMaybeReturnAttribute(containingType))
            return true;

        // Check base classes for inherited attribute
        var baseType = containingType?.BaseType;
        while (baseType != null)
        {
            if (HasRequireMaybeReturnAttribute(baseType))
                return true;
            baseType = baseType.BaseType;
        }

        // Check if method already returns Maybe type (voluntary adoption)
        return ReturnsMaybeType(method.ReturnType);
    }

    /// <summary>
    /// Checks if a symbol has the RequireMaybeReturn attribute.
    /// </summary>
    private static bool HasRequireMaybeReturnAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "RequireMaybeReturnAttribute" ||
            a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.RequireMaybeReturnAttribute");
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