using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.Lifecycle;

/// <summary>
/// Analyzer that detects service resolution calls (GetService, GetRequiredService, etc.)
/// inside Dispose or DisposeAsync methods on types implementing IDisposable or IAsyncDisposable.
/// </summary>
/// <remarks>
/// The DI scope can be disposed before component/service disposal runs, causing
/// ObjectDisposedException at runtime. Services should be cached during initialization instead.
/// This applies to ANY disposable type, not just Blazor components.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ServiceProviderInDisposalAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB015";

    private static readonly LocalizableString Title =
        "Do not resolve services from IServiceProvider in Dispose/DisposeAsync";
    private static readonly LocalizableString MessageFormat =
        "Service resolution call '{0}' in {1} may throw ObjectDisposedException. " +
        "The DI scope can be disposed before component/service disposal runs. " +
        "Cache the service reference during initialization instead.";
    private static readonly LocalizableString Description =
        "Resolving services from IServiceProvider during disposal is unsafe because the DI scope " +
        "may already be disposed, resulting in ObjectDisposedException. Inject or resolve services " +
        "during initialization and store them in fields instead.";
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

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax methodDeclaration)
            return;

        // Only analyze Dispose/DisposeAsync methods
        if (!BlazorLifecycleHelpers.IsDisposalMethod(methodDeclaration.Identifier.Text))
            return;

        // Get containing type and verify it implements IDisposable or IAsyncDisposable
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration);
        if (methodSymbol == null)
            return;

        var containingType = methodSymbol.ContainingType;
        if (containingType == null || !BlazorLifecycleHelpers.IsDisposableType(containingType))
            return;

        // Find all invocation expressions in the method body
        var invocations = methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
                continue;

            if (!BlazorLifecycleHelpers.IsServiceProviderResolutionCall(calledMethod))
                continue;

            // Report diagnostic with invocation text as {0} and method name as {1}
            var callText = invocation.ToString();
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                callText,
                methodDeclaration.Identifier.Text));
        }
    }
}
