using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects DotNetObjectReference&lt;T&gt; or IJSObjectReference fields in Blazor components
/// that are not properly disposed via IAsyncDisposable.
/// </summary>
/// <remarks>
/// Blazor components that create JS references must dispose them to prevent memory leaks.
/// This analyzer ensures components implement IAsyncDisposable and dispose their JS references.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class JsReferenceDisposalAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB002";

    private static readonly LocalizableString Title = "JS reference requires disposal";
    private static readonly LocalizableString MessageFormat = "Field '{0}' of type '{1}' requires disposal. Component must implement IAsyncDisposable and dispose this field.";
    private static readonly LocalizableString Description =
        "DotNetObjectReference<T> and IJSObjectReference fields in Blazor components must be disposed " +
        "to prevent memory leaks. The component should implement IAsyncDisposable and dispose these fields " +
        "in the DisposeAsync method.";
    private const string Category = "Lifecycle";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    // Known JS reference types that require disposal
    private static readonly string[] JsReferenceTypeNames =
    {
        "Microsoft.JSInterop.DotNetObjectReference`1",
        "Microsoft.JSInterop.IJSObjectReference",
        "Microsoft.JSInterop.IJSInProcessObjectReference",
        "Microsoft.JSInterop.IJSUnmarshalledObjectReference"
    };

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // Must analyze generated code because Razor components compile to generated C#
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        // Analyze class declarations to find Blazor components with JS reference fields
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Only analyze classes (components are classes)
        if (typeSymbol.TypeKind != TypeKind.Class)
            return;

        // Check if this is a Blazor component (inherits from ComponentBase or implements IComponent)
        if (!IsBlazorComponent(typeSymbol))
            return;

        // Find all fields that are JS reference types
        var jsReferenceFields = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => IsJsReferenceType(field.Type))
            .ToList();

        if (jsReferenceFields.Count == 0)
            return;

        // Check if the component implements IAsyncDisposable
        var implementsAsyncDisposable = ImplementsAsyncDisposable(typeSymbol);

        // Check if the component implements IDisposable
        var implementsDisposable = ImplementsDisposable(typeSymbol);

        // For each JS reference field, check if it's disposed
        foreach (var field in jsReferenceFields)
        {
            // If the component doesn't implement IAsyncDisposable, report diagnostic
            if (!implementsAsyncDisposable)
            {
                // Only report if not implementing IDisposable either (for DotNetObjectReference which has sync Dispose)
                if (!implementsDisposable || !IsSyncDisposableType(field.Type))
                {
                    ReportDiagnostic(context, field);
                }
            }
            else
            {
                // Component implements IAsyncDisposable - check if this field is actually disposed
                if (!IsFieldDisposedInDisposeAsync(typeSymbol, field))
                {
                    ReportDiagnostic(context, field);
                }
            }
        }
    }

    private static bool IsBlazorComponent(INamedTypeSymbol typeSymbol)
    {
        // Check inheritance chain for ComponentBase
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            if (current.Name == "ComponentBase" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components")
            {
                return true;
            }
            current = current.BaseType;
        }

        // Check if implements IComponent
        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IComponent" &&
            i.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }

    private static bool IsJsReferenceType(ITypeSymbol type)
    {
        // Handle nullable types
        if (type is INamedTypeSymbol { IsGenericType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            // Nullable value types - not applicable for reference types
            return false;
        }

        // Check if it's a nullable reference type (T?)
        var actualType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        var fullName = GetFullTypeName(actualType);

        // Check for DotNetObjectReference<T> (generic type)
        if (actualType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDefinition = GetFullTypeName(namedType.OriginalDefinition);
            if (originalDefinition == "Microsoft.JSInterop.DotNetObjectReference`1")
                return true;
        }

        // Check for IJSObjectReference and related interfaces
        return JsReferenceTypeNames.Any(typeName =>
            fullName == typeName ||
            (actualType.AllInterfaces.Any(i => GetFullTypeName(i) == typeName)));
    }

    private static bool IsSyncDisposableType(ITypeSymbol type)
    {
        // DotNetObjectReference<T> has a sync Dispose() method
        var actualType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

        if (actualType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDefinition = GetFullTypeName(namedType.OriginalDefinition);
            return originalDefinition == "Microsoft.JSInterop.DotNetObjectReference`1";
        }

        return false;
    }

    private static string GetFullTypeName(ITypeSymbol type)
    {
        return type.ContainingNamespace?.IsGlobalNamespace == true
            ? type.MetadataName
            : $"{type.ContainingNamespace?.ToDisplayString()}.{type.MetadataName}";
    }

    private static bool ImplementsAsyncDisposable(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IAsyncDisposable" &&
            i.ContainingNamespace?.ToDisplayString() == "System");
    }

    private static bool ImplementsDisposable(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.AllInterfaces.Any(i =>
            i.Name == "IDisposable" &&
            i.ContainingNamespace?.ToDisplayString() == "System");
    }

    private static bool IsFieldDisposedInDisposeAsync(INamedTypeSymbol typeSymbol, IFieldSymbol field)
    {
        // Find DisposeAsync method
        var disposeAsyncMethod = typeSymbol.GetMembers("DisposeAsync")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.ReturnType.Name == "ValueTask" && m.Parameters.Length == 0);

        if (disposeAsyncMethod == null)
            return false;

        // Get the syntax for the method
        var syntaxRefs = disposeAsyncMethod.DeclaringSyntaxReferences;
        foreach (var syntaxRef in syntaxRefs)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax is MethodDeclarationSyntax methodSyntax)
            {
                // Check if the field is referenced in the method body
                var fieldName = field.Name;

                // Look for field access in the method
                var fieldAccesses = methodSyntax.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.Text == fieldName);

                // Check if any access is a disposal pattern
                foreach (var access in fieldAccesses)
                {
                    var parent = access.Parent;

                    // Check for .Dispose() or .DisposeAsync() calls
                    if (parent is MemberAccessExpressionSyntax memberAccess)
                    {
                        var methodName = memberAccess.Name.Identifier.Text;
                        if (methodName == "Dispose" || methodName == "DisposeAsync")
                            return true;

                        // Check for .InvokeVoidAsync("dispose") or .InvokeAsync("dispose") pattern
                        // This is a common pattern for JS interop cleanup
                        if (methodName == "InvokeVoidAsync" || methodName == "InvokeAsync")
                        {
                            if (IsJsDisposeInvocation(memberAccess))
                                return true;
                        }
                    }

                    // Check for await using pattern
                    if (IsInUsingStatement(access))
                        return true;

                    // Check for ?.Dispose() pattern
                    if (parent is ConditionalAccessExpressionSyntax conditionalAccess)
                    {
                        var whenNotNull = conditionalAccess.WhenNotNull;
                        if (whenNotNull is InvocationExpressionSyntax invocation)
                        {
                            if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
                            {
                                var methodName = memberBinding.Name.Identifier.Text;
                                if (methodName == "Dispose" || methodName == "DisposeAsync")
                                    return true;

                                // Check for ?.InvokeVoidAsync("dispose") pattern
                                if (methodName == "InvokeVoidAsync" || methodName == "InvokeAsync")
                                {
                                    if (IsJsDisposeInvocationFromBinding(invocation))
                                        return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a member access expression is a JS dispose invocation like .InvokeVoidAsync("dispose").
    /// </summary>
    private static bool IsJsDisposeInvocation(MemberAccessExpressionSyntax memberAccess)
    {
        // The parent should be an InvocationExpression
        if (memberAccess.Parent is not InvocationExpressionSyntax invocation)
            return false;

        return HasDisposeArgument(invocation);
    }

    /// <summary>
    /// Checks if an invocation from a member binding (?.InvokeVoidAsync) has a "dispose" argument.
    /// </summary>
    private static bool IsJsDisposeInvocationFromBinding(InvocationExpressionSyntax invocation)
    {
        return HasDisposeArgument(invocation);
    }

    /// <summary>
    /// Checks if the first argument to an invocation is a string literal containing "dispose".
    /// </summary>
    private static bool HasDisposeArgument(InvocationExpressionSyntax invocation)
    {
        // Get the first argument
        var arguments = invocation.ArgumentList?.Arguments;
        if (arguments == null || arguments.Value.Count == 0)
            return false;

        var firstArg = arguments.Value[0].Expression;

        // Check if it's a string literal containing "dispose"
        if (firstArg is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var value = literal.Token.ValueText;
            // Match "dispose", "cleanup", "destroy" - common JS cleanup method names
            return value.Equals("dispose", System.StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("cleanup", System.StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("destroy", System.StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsInUsingStatement(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is UsingStatementSyntax || current is LocalDeclarationStatementSyntax localDecl)
            {
                if (current is LocalDeclarationStatementSyntax { UsingKeyword.ValueText: "using" })
                    return true;
                if (current is UsingStatementSyntax)
                    return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static void ReportDiagnostic(SymbolAnalysisContext context, IFieldSymbol field)
    {
        var typeName = field.Type is INamedTypeSymbol namedType && namedType.IsGenericType
            ? $"{namedType.Name}<{string.Join(", ", namedType.TypeArguments.Select(t => t.Name))}>"
            : field.Type.Name;

        foreach (var location in field.Locations)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                location,
                field.Name,
                typeName));
        }
    }
}
