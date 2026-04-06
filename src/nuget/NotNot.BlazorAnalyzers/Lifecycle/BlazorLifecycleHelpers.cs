using System.Linq;
using Microsoft.CodeAnalysis;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Shared helper methods for Blazor lifecycle analyzers.
/// Centralizes component detection, disposal identification, and service-provider call verification
/// to avoid duplication across NNB002, NNB007, NNB009, NNB015, NNB016, NNB017.
/// </summary>
internal static class BlazorLifecycleHelpers
{
    /// <summary>
    /// Checks if a type derives from ComponentBase or implements IComponent.
    /// </summary>
    public static bool IsBlazorComponent(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            if (current.Name == "ComponentBase" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components")
                return true;
            current = current.BaseType;
        }
        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IComponent" &&
            i.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }

    /// <summary>
    /// Checks if a method name is Dispose or DisposeAsync.
    /// </summary>
    public static bool IsDisposalMethod(string methodName)
        => methodName is "Dispose" or "DisposeAsync";

    /// <summary>
    /// Checks if a method name is a Blazor lifecycle init method where service resolution is considered safe.
    /// Policy: OnParametersSet{Async} is included because the DI scope is guaranteed alive during parameter updates.
    /// </summary>
    public static bool IsLifecycleInitMethod(string methodName)
        => methodName is "OnInitialized" or "OnInitializedAsync"
            or "OnParametersSet" or "OnParametersSetAsync"
            or "SetParametersAsync"
            or ".ctor";

    /// <summary>
    /// All method names that constitute service location through IServiceProvider.
    /// Covers standard, generic, non-generic, keyed, and enumerable APIs.
    /// </summary>
    private static readonly string[] ServiceLocationMethodNames =
    {
        "GetService",
        "GetRequiredService",
        "GetServices",
        "GetKeyedService",
        "GetRequiredKeyedService",
        "GetKeyedServices",
    };

    /// <summary>
    /// Verifies that a method symbol is a service-location call on IServiceProvider
    /// or its extension methods. Uses semantic analysis for accuracy.
    /// </summary>
    public static bool IsServiceProviderResolutionCall(IMethodSymbol method)
    {
        if (!ServiceLocationMethodNames.Contains(method.Name))
            return false;

        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        var ns = containingType.ContainingNamespace?.ToDisplayString();

        // Direct IServiceProvider.GetService(Type)
        if (containingType.Name == "IServiceProvider" && ns == "System")
            return true;

        // Extension methods on ServiceProviderServiceExtensions
        if (ns == "Microsoft.Extensions.DependencyInjection" &&
            (containingType.Name == "ServiceProviderServiceExtensions" ||
             containingType.Name == "ServiceProviderKeyedServiceExtensions"))
            return true;

        // IKeyedServiceProvider
        if (containingType.Name == "IKeyedServiceProvider" && ns == "Microsoft.Extensions.DependencyInjection")
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a type implements IDisposable or IAsyncDisposable.
    /// </summary>
    public static bool IsDisposableType(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.AllInterfaces.Any(i =>
            (i.Name == "IDisposable" && i.ContainingNamespace?.ToDisplayString() == "System") ||
            (i.Name == "IAsyncDisposable" && i.ContainingNamespace?.ToDisplayString() == "System"));
    }
}
