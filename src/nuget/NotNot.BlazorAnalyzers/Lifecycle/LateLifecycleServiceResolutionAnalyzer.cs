using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects service resolution calls (GetService, GetRequiredService, etc.)
/// outside Blazor lifecycle initialization methods in ComponentBase-derived classes.
/// </summary>
/// <remarks>
/// <para>
/// When service resolution occurs in OnAfterRender, event handlers, property getters, or other
/// non-init methods, the DI scope may be disposed during circuit teardown, causing
/// ObjectDisposedException. Services should be resolved and cached during OnInitialized/OnParametersSet.
/// </para>
/// <para>
/// Constructors are automatically excluded because Roslyn represents them as
/// <see cref="ConstructorDeclarationSyntax"/>, not <see cref="MethodDeclarationSyntax"/>.
/// Disposal methods (Dispose/DisposeAsync) are excluded because NNB015 covers those.
/// </para>
/// <para>
/// Detection scope: Option A (direct-call-only). Calls inside private helper methods invoked
/// from init methods are NOT detected. This is an accepted false negative to keep the analyzer
/// simple and avoid inter-procedural analysis.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LateLifecycleServiceResolutionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB016";

    private static readonly LocalizableString Title = "Service resolution outside initialization lifecycle methods";
    private static readonly LocalizableString MessageFormat =
        "Service resolution call '{0}' in '{1}' is outside Blazor lifecycle initialization methods. " +
        "The DI scope may be disposed during circuit teardown. " +
        "Cache the service during OnInitialized/OnParametersSet instead.";
    private static readonly LocalizableString Description =
        "Service resolution calls (GetService, GetRequiredService, etc.) should only occur in Blazor " +
        "lifecycle initialization methods (OnInitialized, OnParametersSet, SetParametersAsync) where " +
        "the DI scope is guaranteed alive. Resolving in OnAfterRender, event handlers, or property " +
        "getters risks ObjectDisposedException during circuit teardown.";
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
        // Must analyze generated code because Razor components compile to generated C#
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        // Register for method declarations (covers regular methods, event handlers, OnAfterRender, etc.)
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);

        // Register for property getter accessors (covers computed properties that resolve services)
        context.RegisterSyntaxNodeAction(AnalyzeGetAccessorDeclaration, SyntaxKind.GetAccessorDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax methodDeclaration)
            return;

        var methodName = methodDeclaration.Identifier.Text;

        // Skip lifecycle init methods — service resolution is safe there
        if (BlazorLifecycleHelpers.IsLifecycleInitMethod(methodName))
            return;

        // Skip disposal methods — NNB015 covers Dispose/DisposeAsync
        if (BlazorLifecycleHelpers.IsDisposalMethod(methodName))
            return;

        // Get containing type symbol and verify it's a Blazor component
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
            return;

        var containingType = methodSymbol.ContainingType;
        if (containingType == null || !BlazorLifecycleHelpers.IsBlazorComponent(containingType))
            return;

        // Walk descendant invocations looking for service resolution calls
        var invocations = methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
                continue;

            if (!BlazorLifecycleHelpers.IsServiceProviderResolutionCall(calledMethod))
                continue;

            // Report diagnostic
            var callText = GetCallText(invocation);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                callText,
                methodName));
        }
    }

    private static void AnalyzeGetAccessorDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AccessorDeclarationSyntax accessorDeclaration)
            return;

        // Walk up to find the containing PropertyDeclaration
        if (accessorDeclaration.Parent?.Parent is not PropertyDeclarationSyntax propertyDeclaration)
            return;

        var propertyName = propertyDeclaration.Identifier.Text;

        // Property names won't match the init allowlist, but check anyway for consistency
        if (BlazorLifecycleHelpers.IsLifecycleInitMethod(propertyName))
            return;

        // Get containing type symbol from the property
        var propertySymbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (propertySymbol == null)
            return;

        var containingType = propertySymbol.ContainingType;
        if (containingType == null || !BlazorLifecycleHelpers.IsBlazorComponent(containingType))
            return;

        // Walk descendant invocations in the getter body
        var invocations = accessorDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
                continue;

            if (!BlazorLifecycleHelpers.IsServiceProviderResolutionCall(calledMethod))
                continue;

            // Use property name as the containing member name in the diagnostic
            var callText = GetCallText(invocation);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                callText,
                propertyName + " (getter)"));
        }
    }

    private static string GetCallText(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var expressionText = memberAccess.Expression.ToString();
            var methodName = memberAccess.Name.Identifier.Text;

            // Truncate long expressions
            if (expressionText.Length > 20)
                expressionText = expressionText.Substring(0, 17) + "...";

            return $"{expressionText}.{methodName}";
        }

        if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
        {
            return $"?.{memberBinding.Name.Identifier.Text}";
        }

        return invocation.Expression.ToString();
    }
}
