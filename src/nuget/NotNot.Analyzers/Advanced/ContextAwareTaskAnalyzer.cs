using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Advanced;

/// <summary>
/// Advanced context-aware analyzer that provides more intelligent detection
/// of async/await issues based on the surrounding code context
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextAwareTaskAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic for potentially blocking async calls in UI contexts
    /// </summary>
    public const string UiBlockingDiagnosticId = "NN_R003";
    
    /// <summary>
    /// Diagnostic for missing ConfigureAwait(false) in library code
    /// </summary>
    public const string ConfigureAwaitDiagnosticId = "NN_R004";

    private static readonly DiagnosticDescriptor UiBlockingRule = new(
        id: UiBlockingDiagnosticId,
        title: "Potentially blocking async call in UI context",
        messageFormat: "Async call '{0}' may block the UI thread. Consider using ConfigureAwait(false) or ensuring proper async/await patterns.",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Blocking async calls in UI contexts can cause deadlocks and poor user experience. " +
                     "Use ConfigureAwait(false) for library calls or ensure proper async patterns.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{UiBlockingDiagnosticId}",
        customTags: new[] { "Performance", "UI", "Deadlock" });

    private static readonly DiagnosticDescriptor ConfigureAwaitRule = new(
        id: ConfigureAwaitDiagnosticId,
        title: "Missing ConfigureAwait(false) in library code",
        messageFormat: "Await expression '{0}' in library code should use ConfigureAwait(false) to avoid potential deadlocks.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Library code should use ConfigureAwait(false) to avoid capturing the synchronization context " +
                     "and prevent potential deadlocks when called from UI contexts.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{ConfigureAwaitDiagnosticId}",
        customTags: new[] { "Reliability", "Library", "ConfigureAwait" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UiBlockingRule, ConfigureAwaitRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        
        context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);
    }

    private static void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking("NN_R003_R004", "AnalyzeAwaitExpression");
        
        var awaitExpression = (AwaitExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Check for UI context blocking potential
        if (IsInUiContext(awaitExpression, semanticModel))
        {
            if (CouldCauseUiBlocking(awaitExpression, semanticModel))
            {
                var diagnostic = Diagnostic.Create(
                    UiBlockingRule,
                    awaitExpression.GetLocation(),
                    awaitExpression.Expression.ToString());
                
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for missing ConfigureAwait(false) in library code
        if (IsInLibraryCode(awaitExpression, semanticModel))
        {
            if (!HasConfigureAwait(awaitExpression))
            {
                var diagnostic = Diagnostic.Create(
                    ConfigureAwaitRule,
                    awaitExpression.GetLocation(),
                    awaitExpression.Expression.ToString());
                
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsInUiContext(SyntaxNode node, SemanticModel semanticModel)
    {
        // Check if we're in a class that likely represents a UI component
        var containingClass = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass != null)
        {
            var className = containingClass.Identifier.ValueText;
            
            // Common UI class patterns
            if (className.EndsWith("Page") ||
                className.EndsWith("Window") ||
                className.EndsWith("Form") ||
                className.EndsWith("Control") ||
                className.EndsWith("Component") ||
                className.EndsWith("Activity") ||
                className.EndsWith("Fragment") ||
                className.EndsWith("ViewModel"))
            {
                return true;
            }

            // Check for UI framework base classes
            var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
            if (classSymbol?.BaseType != null)
            {
                var baseTypeName = classSymbol.BaseType.Name;
                if (baseTypeName.Contains("Page") ||
                    baseTypeName.Contains("Window") ||
                    baseTypeName.Contains("Form") ||
                    baseTypeName.Contains("Control") ||
                    baseTypeName.Contains("Activity") ||
                    baseTypeName.Contains("Fragment"))
                {
                    return true;
                }
            }
        }

        // Check method names that suggest UI event handlers
        var containingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (containingMethod != null)
        {
            var methodName = containingMethod.Identifier.ValueText;
            if (methodName.EndsWith("_Click") ||
                methodName.EndsWith("_Tapped") ||
                methodName.EndsWith("_Changed") ||
                methodName.StartsWith("On") && methodName.Contains("Click"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CouldCauseUiBlocking(AwaitExpressionSyntax awaitExpression, SemanticModel semanticModel)
    {
        // Check if the awaited expression doesn't use ConfigureAwait(false)
        if (awaitExpression.Expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name.Identifier.ValueText == "ConfigureAwait")
                {
                    // Check if it's ConfigureAwait(false)
                    if (invocation.ArgumentList.Arguments.Count == 1)
                    {
                        var argument = invocation.ArgumentList.Arguments[0];
                        if (argument.Expression is LiteralExpressionSyntax literal &&
                            literal.Token.IsKind(SyntaxKind.FalseKeyword))
                        {
                            return false; // ConfigureAwait(false) is used, no blocking risk
                        }
                    }
                }
            }
        }

        // If no ConfigureAwait(false), there's potential for blocking
        return true;
    }

    private static bool IsInLibraryCode(SyntaxNode node, SemanticModel semanticModel)
    {
        // Check if we're in a public API (public classes/methods)
        var containingClass = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass?.Modifiers.Any(SyntaxKind.PublicKeyword) == true)
        {
            var containingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (containingMethod?.Modifiers.Any(SyntaxKind.PublicKeyword) == true ||
                containingMethod?.Modifiers.Any(SyntaxKind.ProtectedKeyword) == true)
            {
                return true;
            }
        }

        // Check namespace - avoid UI-related namespaces
        var namespaceDeclaration = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>() ??
                                   node.FirstAncestorOrSelf<FileScopedNamespaceDeclarationSyntax>() as BaseNamespaceDeclarationSyntax;
        
        if (namespaceDeclaration != null)
        {
            var namespaceName = namespaceDeclaration.Name.ToString();
            
            // Don't apply rule to UI namespaces
            if (namespaceName.Contains(".UI") ||
                namespaceName.Contains(".Views") ||
                namespaceName.Contains(".Pages") ||
                namespaceName.Contains(".Controls"))
            {
                return false;
            }

            // Apply to library/service namespaces
            if (namespaceName.Contains(".Services") ||
                namespaceName.Contains(".Library") ||
                namespaceName.Contains(".Core") ||
                namespaceName.Contains(".Infrastructure"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConfigureAwait(AwaitExpressionSyntax awaitExpression)
    {
        if (awaitExpression.Expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.Identifier.ValueText == "ConfigureAwait";
            }
        }

        return false;
    }
}