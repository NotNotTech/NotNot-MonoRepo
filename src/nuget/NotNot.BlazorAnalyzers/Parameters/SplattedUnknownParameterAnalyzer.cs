using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Parameters;

/// <summary>
/// Strict analyzer that detects unknown parameters passed to Blazor components that have attribute splatting.
/// </summary>
/// <remarks>
/// <para>
/// This is a stricter sibling to NNB010. While NNB010 only catches unknown parameters on components
/// that would fail at runtime (no splatting), this analyzer catches ALL unknown parameters including
/// those on components with <c>[Parameter(CaptureUnmatchedValues = true)]</c>.
/// </para>
/// <para>
/// <b>Use case:</b> Catch typos and enforce explicit parameter usage even when splatting would
/// silently accept the unknown attribute. Disable this analyzer if you intentionally use splatting
/// for HTML passthrough attributes.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SplattedUnknownParameterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for unknown parameter on splatted component.
    /// </summary>
    public const string DiagnosticId = "NNB011";

    private static readonly LocalizableString Title = "Unknown parameter on splatted component";
    private static readonly LocalizableString MessageFormat =
        "Parameter '{0}' is not defined on component '{1}'. While splatting allows this at runtime, it may indicate a typo.";
    private static readonly LocalizableString Description =
        "This component has [Parameter(CaptureUnmatchedValues = true)] which accepts arbitrary attributes at runtime. " +
        "However, using explicit parameters is preferred for type safety and IntelliSense support. " +
        "Disable NNB011 if you intentionally use attribute splatting for HTML passthrough.";
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
    /// Hardcoded exemptions for known-good splatted attributes.
    /// </summary>
    private static readonly string[] ExemptedAttributes =
    {
        "data-xcs", // XRay callsite instrumentation - proven to work
    };

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // CRITICAL: Must analyze generated code - Razor compiles to .g.cs files
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a call to AddComponentParameter
        if (!IsAddComponentParameterCall(invocation))
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

        // If it's not a string literal, skip
        if (paramNameArg is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var parameterName = literal.Token.ValueText;

        // Check hardcoded exemptions
        if (ExemptedAttributes.Contains(parameterName))
            return;

        // Find the component type
        var componentType = FindComponentTypeFromContext(invocation, context.SemanticModel);
        if (componentType == null)
            return;

        // Check if the parameter is valid on the component
        if (IsValidParameter(componentType, parameterName))
            return;

        // ONLY report if the component HAS splatting (opposite of NNB010)
        if (!HasCaptureUnmatchedValues(componentType))
            return;

        // Report diagnostic
        var location = literal.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            location,
            parameterName,
            componentType.Name));
    }

    private static bool IsAddComponentParameterCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text == "AddComponentParameter";
        }
        return false;
    }

    private static bool IsNameOfExpression(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text == "nameof";
            }
        }
        return false;
    }

    private static INamedTypeSymbol? FindComponentTypeFromContext(
        InvocationExpressionSyntax addParameterCall,
        SemanticModel semanticModel)
    {
        var containingBlock = addParameterCall.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null)
            return null;

        var statements = containingBlock.Statements;
        var currentStatement = addParameterCall.FirstAncestorOrSelf<StatementSyntax>();
        if (currentStatement == null)
            return null;

        var currentIndex = statements.IndexOf(currentStatement);
        if (currentIndex < 0)
            return null;

        for (int i = currentIndex - 1; i >= 0; i--)
        {
            var statement = statements[i];

            var openComponentCall = statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(inv => IsOpenComponentCall(inv));

            if (openComponentCall != null)
            {
                if (openComponentCall.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name is GenericNameSyntax genericName &&
                    genericName.TypeArgumentList.Arguments.Count > 0)
                {
                    var typeArg = genericName.TypeArgumentList.Arguments[0];
                    var typeInfo = semanticModel.GetTypeInfo(typeArg);
                    return typeInfo.Type as INamedTypeSymbol;
                }
            }

            var closeComponentCall = statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(inv => IsCloseComponentCall(inv));

            if (closeComponentCall != null)
                break;
        }

        return null;
    }

    private static bool IsOpenComponentCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name is GenericNameSyntax genericName)
            {
                return genericName.Identifier.Text == "OpenComponent";
            }
        }
        return false;
    }

    private static bool IsCloseComponentCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text == "CloseComponent";
        }
        return false;
    }

    private static bool IsValidParameter(INamedTypeSymbol componentType, string parameterName)
    {
        var allMembers = GetAllMembers(componentType);

        foreach (var member in allMembers)
        {
            if (member is IPropertySymbol property)
            {
                if (property.Name == parameterName)
                {
                    if (HasParameterAttribute(property))
                        return true;
                }
            }
        }

        return false;
    }

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

    private static bool HasParameterAttribute(IPropertySymbol property)
    {
        return property.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "ParameterAttribute" &&
            attr.AttributeClass?.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components");
    }

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
}
