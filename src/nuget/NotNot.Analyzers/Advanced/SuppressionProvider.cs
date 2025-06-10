using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Advanced;

/// <summary>
/// Provides intelligent suppression of NotNot.Analyzers diagnostics based on context
/// and common patterns where the rules may not apply
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotNotDiagnosticSuppressor : DiagnosticSuppressor
{
    /// <summary>
    /// Suppression for NN_R001 when in test methods
    /// </summary>
    private static readonly SuppressionDescriptor TaskNotAwaitedInTestSuppression = new(
        id: "NNS001",
        suppressedDiagnosticId: Reliability.Concurrency.TaskAwaitedOrReturnedAnalyzer.DiagnosticId,
        justification: "Fire-and-forget tasks are often intentional in test methods for setup/cleanup operations");

    /// <summary>
    /// Suppression for NN_R001 when in event handlers
    /// </summary>
    private static readonly SuppressionDescriptor TaskNotAwaitedInEventHandlerSuppression = new(
        id: "NNS002", 
        suppressedDiagnosticId: Reliability.Concurrency.TaskAwaitedOrReturnedAnalyzer.DiagnosticId,
        justification: "Event handlers often use fire-and-forget patterns for non-blocking operations");

    /// <summary>
    /// Suppression for NN_R002 when result is intentionally ignored
    /// </summary>
    private static readonly SuppressionDescriptor TaskResultIgnoredInCleanupSuppression = new(
        id: "NNS003",
        suppressedDiagnosticId: Reliability.Concurrency.TaskResultNotObservedAnalyzer.DiagnosticId,
        justification: "Task results are often intentionally ignored in cleanup and disposal patterns");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(
            TaskNotAwaitedInTestSuppression,
            TaskNotAwaitedInEventHandlerSuppression,
            TaskResultIgnoredInCleanupSuppression);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (ShouldSuppressDiagnostic(diagnostic, context))
            {
                var suppression = GetSuppressionDescriptor(diagnostic);
                if (suppression != null)
                {
                    context.ReportSuppression(Suppression.Create(suppression, diagnostic));
                }
            }
        }
    }

    private static bool ShouldSuppressDiagnostic(Diagnostic diagnostic, SuppressionAnalysisContext context)
    {
        var syntaxTree = diagnostic.Location.SourceTree;
        if (syntaxTree == null) return false;

        var semanticModel = context.GetSemanticModel(syntaxTree);
        var syntaxNode = syntaxTree.GetRoot().FindNode(diagnostic.Location.SourceSpan);
        
        // Check if we're in a test method
        if (IsInTestMethod(syntaxNode, semanticModel))
        {
            return diagnostic.Id == Reliability.Concurrency.TaskAwaitedOrReturnedAnalyzer.DiagnosticId;
        }

        // Check if we're in an event handler
        if (IsInEventHandler(syntaxNode, semanticModel))
        {
            return diagnostic.Id == Reliability.Concurrency.TaskAwaitedOrReturnedAnalyzer.DiagnosticId;
        }

        // Check if we're in cleanup/disposal code
        if (IsInCleanupMethod(syntaxNode, semanticModel))
        {
            return diagnostic.Id == Reliability.Concurrency.TaskResultNotObservedAnalyzer.DiagnosticId;
        }

        return false;
    }

    private static SuppressionDescriptor? GetSuppressionDescriptor(Diagnostic diagnostic)
    {
        return diagnostic.Id switch
        {
            Reliability.Concurrency.TaskAwaitedOrReturnedAnalyzer.DiagnosticId => TaskNotAwaitedInTestSuppression,
            Reliability.Concurrency.TaskResultNotObservedAnalyzer.DiagnosticId => TaskResultIgnoredInCleanupSuppression,
            _ => null
        };
    }

    private static bool IsInTestMethod(SyntaxNode node, SemanticModel semanticModel)
    {
        // Look for common test method attributes
        var method = node.FirstAncestorOrSelf<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
        if (method == null) return false;

        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(attribute);
                if (symbolInfo.Symbol is IMethodSymbol attributeMethod)
                {
                    var attributeTypeName = attributeMethod.ContainingType.Name;
                    
                    // Common test frameworks
                    if (attributeTypeName.Contains("Test") || 
                        attributeTypeName.Contains("Fact") ||
                        attributeTypeName.Contains("Theory") ||
                        attributeTypeName == "TestMethod")
                    {
                        return true;
                    }
                }
            }
        }

        // Check if the method name suggests it's a test
        var methodName = method.Identifier.ValueText;
        return methodName.StartsWith("Test") || 
               methodName.EndsWith("Test") || 
               methodName.Contains("Should") ||
               methodName.Contains("_Test_");
    }

    private static bool IsInEventHandler(SyntaxNode node, SemanticModel semanticModel)
    {
        var method = node.FirstAncestorOrSelf<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
        if (method == null) return false;

        var methodName = method.Identifier.ValueText;
        
        // Common event handler naming patterns
        return methodName.StartsWith("On") || 
               methodName.EndsWith("Handler") ||
               methodName.EndsWith("_Click") ||
               methodName.EndsWith("_Changed") ||
               methodName.Contains("Event");
    }

    private static bool IsInCleanupMethod(SyntaxNode node, SemanticModel semanticModel)
    {
        var method = node.FirstAncestorOrSelf<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
        if (method == null) return false;

        var methodName = method.Identifier.ValueText;
        
        // Common cleanup method patterns
        return methodName.Equals("Dispose") ||
               methodName.Equals("DisposeAsync") ||
               methodName.StartsWith("Cleanup") ||
               methodName.StartsWith("TearDown") ||
               methodName.Contains("Dispose") ||
               methodName.Contains("Close");
    }
}