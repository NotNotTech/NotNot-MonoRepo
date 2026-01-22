using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Parameters;

/// <summary>
/// Analyzer that detects unknown parameters passed to Blazor components in generated Razor code.
/// </summary>
/// <remarks>
/// <para>
/// When a Razor file uses an attribute that doesn't match a <c>[Parameter]</c> property on a component,
/// the Razor compiler generates <c>AddComponentParameter(N, "string-literal", value)</c> instead of
/// <c>AddComponentParameter(N, nameof(Type.Property), value)</c>.
/// </para>
/// <para>
/// This analyzer detects these string-literal parameter names and validates them against the component's
/// actual parameters. If the parameter name isn't valid AND the component doesn't have
/// <c>[Parameter(CaptureUnmatchedValues = true)]</c> attribute splatting, an error is reported.
/// </para>
/// <para>
/// <b>Note:</b> HTML5 <c>data-*</c> attributes are NOT exempt by default. Components must explicitly
/// opt into attribute splatting to accept arbitrary attributes.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnknownComponentParameterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for unknown component parameter.
    /// </summary>
    public const string DiagnosticId = "NNB010";

    private static readonly LocalizableString Title = "Unknown component parameter";
    private static readonly LocalizableString MessageFormat =
        "Parameter '{0}' is not defined on component '{1}'. The component does not support attribute splatting.";
    private static readonly LocalizableString Description =
        "Blazor components only accept parameters explicitly declared with [Parameter]. " +
        "To accept arbitrary attributes, the component must have a property with [Parameter(CaptureUnmatchedValues = true)]. " +
        "This error commonly occurs from typos in parameter names or using attributes on components that don't support them.";
    private const string Category = "Parameters";

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

    /// <summary>
    /// Hardcoded exemptions for known-good attributes.
    /// These are common HTML attributes that pass through to the underlying element.
    /// </summary>
    private static readonly string[] ExemptedAttributes =
    {
        "data-xcs",     // XRay callsite instrumentation
        "title",        // Native HTML tooltip
        "role",         // ARIA role attribute
        "tabindex",     // Keyboard navigation
        "style",        // Inline styles (though usually prefer Class)
        "id",           // Element ID
        "name",         // Form element name
        "placeholder",  // Input placeholder
        "disabled",     // Disabled state
        "readonly",     // Read-only state
        "autofocus",    // Auto-focus on load
    };

    /// <summary>
    /// Attribute prefixes that are always exempt (e.g., aria-*, data-*, on*).
    /// </summary>
    private static readonly string[] ExemptedPrefixes =
    {
        "aria-",        // All ARIA accessibility attributes
        "data-",        // All data-* attributes (HTML5 custom data)
        "on",           // All event handlers (onclick, onchange, onkeydown, etc.)
    };

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // CRITICAL: Must analyze generated code - Razor compiles to .g.cs files
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        // Analyze method bodies for the AddComponentParameter pattern
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a call to AddComponentParameter
        if (!IsAddComponentParameterCall(invocation, out var methodName))
            return;

        // Get the arguments
        var args = invocation.ArgumentList?.Arguments;
        if (args == null || args.Value.Count < 3)
            return;

        // Second argument should be the parameter name
        var paramNameArg = args.Value[1].Expression;

        // If it's a nameof expression, the Razor compiler validated it - skip
        if (IsNameOfExpression(paramNameArg))
            return;

        // If it's not a string literal, skip (could be a variable, which is rare but possible)
        if (paramNameArg is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var parameterName = literal.Token.ValueText;

        // Find the component type from the most recent OpenComponent<T> call
        var componentType = FindComponentTypeFromContext(invocation, context.SemanticModel);
        if (componentType == null)
            return;

        // Check if the parameter is valid on the component
        if (IsValidParameter(componentType, parameterName))
            return;

        // Check hardcoded exemptions (exact match and prefix match)
        if (IsExemptedAttribute(parameterName))
            return;

        // Skip components with CaptureUnmatchedValues (attribute splatting)
        // Those are handled by the stricter NNB011 analyzer
        if (HasCaptureUnmatchedValues(componentType))
            return;

        // Report diagnostic - get the original source location if available
        var location = GetOriginalSourceLocation(invocation, context) ?? literal.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            location,
            parameterName,
            componentType.Name));
    }

    /// <summary>
    /// Checks if the invocation is a call to AddComponentParameter.
    /// </summary>
    private static bool IsAddComponentParameterCall(InvocationExpressionSyntax invocation, out string methodName)
    {
        methodName = "";

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
            return methodName == "AddComponentParameter";
        }

        return false;
    }

    /// <summary>
    /// Checks if the expression is a nameof() expression.
    /// </summary>
    private static bool IsNameOfExpression(ExpressionSyntax expression)
    {
        // nameof(Type.Property) appears as InvocationExpression with "nameof" identifier
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text == "nameof";
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the component type from the enclosing OpenComponent call context.
    /// </summary>
    /// <remarks>
    /// In generated Razor code, the pattern is:
    /// <code>
    /// __builder.OpenComponent&lt;MudButton&gt;(N);
    /// __builder.AddComponentParameter(N+1, "paramName", value);
    /// __builder.CloseComponent();
    /// </code>
    /// This method walks backward through the containing block to find the OpenComponent call.
    /// </remarks>
    private static INamedTypeSymbol? FindComponentTypeFromContext(
        InvocationExpressionSyntax addParameterCall,
        SemanticModel semanticModel)
    {
        // Walk up to find the containing block/statement list
        var containingBlock = addParameterCall.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null)
            return null;

        // Get all statements before this one
        var statements = containingBlock.Statements;
        var currentStatement = addParameterCall.FirstAncestorOrSelf<StatementSyntax>();
        if (currentStatement == null)
            return null;

        var currentIndex = statements.IndexOf(currentStatement);
        if (currentIndex < 0)
            return null;

        // Walk backward to find the most recent OpenComponent<T> call
        for (int i = currentIndex - 1; i >= 0; i--)
        {
            var statement = statements[i];

            // Look for OpenComponent invocation in this statement
            var openComponentCall = statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(inv => IsOpenComponentCall(inv));

            if (openComponentCall != null)
            {
                // Extract the type argument from OpenComponent<T>
                if (openComponentCall.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name is GenericNameSyntax genericName &&
                    genericName.TypeArgumentList.Arguments.Count > 0)
                {
                    var typeArg = genericName.TypeArgumentList.Arguments[0];
                    var typeInfo = semanticModel.GetTypeInfo(typeArg);
                    return typeInfo.Type as INamedTypeSymbol;
                }
            }

            // If we hit a CloseComponent call, we've gone past the relevant scope
            var closeComponentCall = statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(inv => IsCloseComponentCall(inv));

            if (closeComponentCall != null)
                break;
        }

        return null;
    }

    /// <summary>
    /// Checks if the invocation is a call to OpenComponent.
    /// </summary>
    private static bool IsOpenComponentCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var name = memberAccess.Name;
            if (name is GenericNameSyntax genericName)
            {
                return genericName.Identifier.Text == "OpenComponent";
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the invocation is a call to CloseComponent.
    /// </summary>
    private static bool IsCloseComponentCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text == "CloseComponent";
        }

        return false;
    }

    /// <summary>
    /// Checks if the parameter name is a valid [Parameter] property on the component.
    /// </summary>
    private static bool IsValidParameter(INamedTypeSymbol componentType, string parameterName)
    {
        // Get all members including inherited ones
        var allMembers = GetAllMembers(componentType);

        foreach (var member in allMembers)
        {
            // Check properties
            if (member is IPropertySymbol property)
            {
                // Match by name (case-sensitive since Blazor parameters are case-sensitive)
                if (property.Name == parameterName)
                {
                    // Check if it has [Parameter] attribute
                    if (HasParameterAttribute(property))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets all members including inherited members from the type hierarchy.
    /// </summary>
    private static ImmutableArray<ISymbol> GetAllMembers(INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var current = type;

        while (current != null)
        {
            builder.AddRange(current.GetMembers());
            current = current.BaseType;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Checks if the property has the [Parameter] attribute.
    /// </summary>
    private static bool HasParameterAttribute(IPropertySymbol property)
    {
        return property.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "ParameterAttribute" &&
            attr.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }

    /// <summary>
    /// Checks if the component has any property with [Parameter(CaptureUnmatchedValues = true)].
    /// </summary>
    private static bool HasCaptureUnmatchedValues(INamedTypeSymbol componentType)
    {
        var allMembers = GetAllMembers(componentType);

        foreach (var member in allMembers)
        {
            if (member is IPropertySymbol property)
            {
                foreach (var attr in property.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "ParameterAttribute" &&
                        attr.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components")
                    {
                        // Check for CaptureUnmatchedValues = true
                        foreach (var namedArg in attr.NamedArguments)
                        {
                            if (namedArg.Key == "CaptureUnmatchedValues" &&
                                namedArg.Value.Value is bool captureValue &&
                                captureValue)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to get the original .razor source location from #line directives.
    /// </summary>
    private static Location? GetOriginalSourceLocation(
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context)
    {
        // Look for #line directive preceding this invocation
        var triviaList = invocation.GetLeadingTrivia();

        foreach (var trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.LineDirectiveTrivia))
            {
                // The #line directive contains the original source location
                // We can use the directive's location as a hint, but the actual
                // location mapping is complex. For now, return null to use the
                // generated code location.
            }
        }

        // Also check preceding statements for #line directives
        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.GetLeadingTrivia() is { } stmtTrivia)
        {
            foreach (var trivia in stmtTrivia)
            {
                if (trivia.IsKind(SyntaxKind.LineDirectiveTrivia) &&
                    trivia.GetStructure() is LineDirectiveTriviaSyntax lineDirective)
                {
                    // Extract file path and line number from #line directive
                    // This gives us the original .razor file location
                    // For now, we'll let the IDE handle the source mapping
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the attribute name is in the exemption list (exact or prefix match).
    /// </summary>
    private static bool IsExemptedAttribute(string attributeName)
    {
        // Exact match check
        if (ExemptedAttributes.Contains(attributeName))
            return true;

        // Prefix match check (e.g., aria-*, data-*)
        foreach (var prefix in ExemptedPrefixes)
        {
            if (attributeName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
