using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    /// <summary>
    /// Suppression for CA2000 when out parameter is properly disposed in the if-true block.
    /// Pattern: if (Method(out var x)) { using (x) { ... } }
    /// The false path is intentionally not disposing because the method assigns default/null.
    /// </summary>
    private static readonly SuppressionDescriptor CA2000OutParamTruePathDisposedSuppression = new(
        id: "NNS004",
        suppressedDiagnosticId: "CA2000",
        justification: "Out parameter is disposed in the if-true block; false path intentionally skips disposal (method assigns default/null on false)");

    /// <summary>
    /// Suppression for CA2000 when object ownership is transferred via Add method or constructor.
    /// Patterns: collection.Add(x), AddChild(x), new Wrapper(x)
    /// The receiving container/object takes ownership and manages disposal.
    /// </summary>
    private static readonly SuppressionDescriptor CA2000OwnershipTransferSuppression = new(
        id: "NNS005",
        suppressedDiagnosticId: "CA2000",
        justification: "Object ownership transferred to container/wrapper via Add method or constructor - receiver manages disposal");

    /// <summary>
    /// Suppression for CA2000 when allocation is inside a using statement's expression.
    /// Pattern: using (expr.Method1().Method2()) - fluent API pattern
    /// The using statement disposes whatever the expression evaluates to.
    /// </summary>
    private static readonly SuppressionDescriptor CA2000InsideUsingExpressionSuppression = new(
        id: "NNS006",
        suppressedDiagnosticId: "CA2000",
        justification: "Object is disposed by enclosing using statement - fluent API pattern");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(
            TaskNotAwaitedInTestSuppression,
            TaskNotAwaitedInEventHandlerSuppression,
            TaskResultIgnoredInCleanupSuppression,
            CA2000OutParamTruePathDisposedSuppression,
            CA2000OwnershipTransferSuppression,
            CA2000InsideUsingExpressionSuppression);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            // Handle CA2000 separately with its specific logic
            if (diagnostic.Id == "CA2000")
            {
                if (IsCA2000OutParamWithTruePathDisposal(diagnostic, context))
                {
                    context.ReportSuppression(Suppression.Create(CA2000OutParamTruePathDisposedSuppression, diagnostic));
                }
                else if (IsCA2000OwnershipTransfer(diagnostic, context))
                {
                    context.ReportSuppression(Suppression.Create(CA2000OwnershipTransferSuppression, diagnostic));
                }
                else if (IsCA2000InsideUsingExpression(diagnostic, context))
                {
                    context.ReportSuppression(Suppression.Create(CA2000InsideUsingExpressionSuppression, diagnostic));
                }
                continue;
            }

            // Handle NotNot diagnostics
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

    /// <summary>
    /// Detects the pattern: if (Method(out var x)) { using (x) { ... } }
    /// where the true path properly disposes the out parameter.
    /// </summary>
    private static bool IsCA2000OutParamWithTruePathDisposal(Diagnostic diagnostic, SuppressionAnalysisContext context)
    {
        var syntaxTree = diagnostic.Location.SourceTree;
        if (syntaxTree == null) return false;

        var root = syntaxTree.GetRoot(context.CancellationToken);
        var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);

        // Find the argument containing this node (could be out var declaration or just identifier)
        var argument = diagnosticNode.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument == null) return false;

        // Check if this is an 'out' argument
        if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) return false;

        // Get the variable name being declared
        string? variableName = null;

        // Pattern 1: out var x (DeclarationExpression)
        if (argument.Expression is DeclarationExpressionSyntax declExpr &&
            declExpr.Designation is SingleVariableDesignationSyntax singleVar)
        {
            variableName = singleVar.Identifier.ValueText;
        }
        // Pattern 2: out x (pre-declared variable)
        else if (argument.Expression is IdentifierNameSyntax identifier)
        {
            variableName = identifier.Identifier.ValueText;
        }

        if (string.IsNullOrEmpty(variableName)) return false;

        // Find the containing invocation
        var invocation = argument.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null) return false;

        // Check if the invocation is part of an if condition
        var ifStatement = invocation.FirstAncestorOrSelf<IfStatementSyntax>();
        if (ifStatement == null) return false;

        // Verify the invocation is IN the condition (not just anywhere in the if)
        if (!ifStatement.Condition.Contains(invocation)) return false;

        // Get semantic model to verify method returns bool
        var semanticModel = context.GetSemanticModel(syntaxTree);
        var methodSymbol = semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;

        if (methodSymbol == null) return false;
        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Boolean) return false;

        // Check if the if-true block contains a using statement for the variable
        return HasUsingForVariable(ifStatement.Statement, variableName);
    }

    /// <summary>
    /// Detects ownership transfer patterns:
    /// 1. Variable passed to method with "Add" in name: collection.Add(x), AddChild(x)
    /// 2. Variable passed to constructor: new Wrapper(x)
    /// 3. Inline new in Add method: collection.Add(new X())
    /// </summary>
    private static bool IsCA2000OwnershipTransfer(Diagnostic diagnostic, SuppressionAnalysisContext context)
    {
        var syntaxTree = diagnostic.Location.SourceTree;
        if (syntaxTree == null) return false;

        var root = syntaxTree.GetRoot(context.CancellationToken);
        var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);

        // Pattern A: Inline new in Add method - collection.Add(new X())
        // Walk up tree to find if we're inside an argument to an Add method call
        for (var node = diagnosticNode; node != null; node = node.Parent)
        {
            // Check if we've reached an argument that's passed to an Add method
            if (node is ArgumentSyntax argument &&
                argument.Parent is ArgumentListSyntax argList &&
                argList.Parent is InvocationExpressionSyntax invocation)
            {
                var methodName = GetMethodName(invocation);
                if (methodName != null && methodName.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // Check if we're an argument to a constructor (inline new passed to another new)
            if (node is ArgumentSyntax ctorArg &&
                ctorArg.Parent is ArgumentListSyntax ctorArgList &&
                ctorArgList.Parent is ObjectCreationExpressionSyntax)
            {
                return true;
            }

            // Stop at method boundaries
            if (node is MethodDeclarationSyntax || node is ConstructorDeclarationSyntax ||
                node is AccessorDeclarationSyntax)
            {
                break;
            }
        }

        // Find object creation for variable-based pattern detection
        var objectCreation = diagnosticNode.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();

        // Pattern B: Variable-based - var x = new X(); ... Add(x) or new Wrapper(x)
        string? variableName = null;

        // Try to find variable declarator (var mmi = new ...)
        var variableDeclarator = diagnosticNode.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (variableDeclarator != null)
        {
            variableName = variableDeclarator.Identifier.ValueText;
        }

        // Also check if diagnostic is on the object creation itself
        if (variableName == null && objectCreation != null)
        {
            // Find parent variable declarator
            var parentDeclarator = objectCreation.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (parentDeclarator != null)
            {
                variableName = parentDeclarator.Identifier.ValueText;
            }
        }

        if (string.IsNullOrEmpty(variableName)) return false;

        // Find the containing method/property body
        var containingMethod = diagnosticNode.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        var containingProperty = diagnosticNode.FirstAncestorOrSelf<AccessorDeclarationSyntax>();
        var containingConstructor = diagnosticNode.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();

        SyntaxNode? bodyToSearch = containingMethod?.Body as SyntaxNode
                                   ?? containingMethod?.ExpressionBody as SyntaxNode
                                   ?? containingProperty?.Body as SyntaxNode
                                   ?? containingProperty?.ExpressionBody as SyntaxNode
                                   ?? containingConstructor?.Body as SyntaxNode;

        if (bodyToSearch == null) return false;

        // Search for ownership transfer patterns
        return HasOwnershipTransfer(bodyToSearch, variableName);
    }

    /// <summary>
    /// Detects fluent API pattern inside using statement expression.
    /// Patterns:
    /// 1. using (expr.Method1().Method2()) - statement form
    /// 2. using (var x = expr.Method()) - statement with declaration
    /// 3. using var x = expr.Method(); - declaration form
    /// </summary>
    private static bool IsCA2000InsideUsingExpression(Diagnostic diagnostic, SuppressionAnalysisContext context)
    {
        var syntaxTree = diagnostic.Location.SourceTree;
        if (syntaxTree == null) return false;

        var root = syntaxTree.GetRoot(context.CancellationToken);
        var diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan);

        // Pattern 1: using (EXPRESSION) { body } - check Expression property
        var usingStatement = diagnosticNode.FirstAncestorOrSelf<UsingStatementSyntax>();
        if (usingStatement != null)
        {
            // Check Expression form: using (expr)
            if (usingStatement.Expression != null &&
                usingStatement.Expression.Span.Contains(diagnostic.Location.SourceSpan))
            {
                return true;
            }

            // Check Declaration form: using (var x = expr)
            if (usingStatement.Declaration != null &&
                usingStatement.Declaration.Span.Contains(diagnostic.Location.SourceSpan))
            {
                return true;
            }
        }

        // Pattern 2: using var x = EXPRESSION; (local declaration with using keyword)
        // Walk up to check if we're in a using declaration's initializer
        for (var node = diagnosticNode; node != null; node = node.Parent)
        {
            if (node is EqualsValueClauseSyntax equalsClause &&
                equalsClause.Parent is VariableDeclaratorSyntax declarator &&
                declarator.Parent is VariableDeclarationSyntax declaration &&
                declaration.Parent is LocalDeclarationStatementSyntax localDecl &&
                localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            {
                return true;
            }

            // Stop at statement boundaries (but not LocalDeclarationStatementSyntax which we're checking)
            if (node is StatementSyntax && node is not LocalDeclarationStatementSyntax) break;
        }

        return false;
    }

    /// <summary>
    /// Checks if the variable is passed to an Add method or constructor, indicating ownership transfer.
    /// </summary>
    private static bool HasOwnershipTransfer(SyntaxNode body, string variableName)
    {
        foreach (var node in body.DescendantNodes())
        {
            // Pattern 1: Method invocation with "Add" in name
            if (node is InvocationExpressionSyntax invocation)
            {
                var methodName = GetMethodName(invocation);
                if (methodName != null && methodName.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Check if our variable is passed as argument
                    if (HasVariableAsArgument(invocation.ArgumentList, variableName))
                    {
                        return true;
                    }
                }
            }

            // Pattern 2: Object creation (constructor call)
            if (node is ObjectCreationExpressionSyntax objectCreation)
            {
                if (objectCreation.ArgumentList != null &&
                    HasVariableAsArgument(objectCreation.ArgumentList, variableName))
                {
                    return true;
                }
            }

            // Pattern 3: Implicit object creation (new() { ... } with target type)
            if (node is ImplicitObjectCreationExpressionSyntax implicitCreation)
            {
                if (implicitCreation.ArgumentList != null &&
                    HasVariableAsArgument(implicitCreation.ArgumentList, variableName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the method name from an invocation expression.
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };
    }

    /// <summary>
    /// Checks if the variable is passed as an argument in the argument list.
    /// </summary>
    private static bool HasVariableAsArgument(ArgumentListSyntax? argumentList, string variableName)
    {
        if (argumentList == null) return false;

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == variableName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the statement block contains a using statement/declaration for the given variable.
    /// </summary>
    private static bool HasUsingForVariable(StatementSyntax? statement, string variableName)
    {
        if (statement == null) return false;

        // Handle block statement: { ... }
        if (statement is BlockSyntax block)
        {
            foreach (var childStatement in block.Statements)
            {
                if (HasUsingForVariable(childStatement, variableName))
                    return true;
            }
            return false;
        }

        // Handle using statement: using (x) { ... }
        if (statement is UsingStatementSyntax usingStatement)
        {
            // Check if the using expression references our variable
            if (usingStatement.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == variableName)
            {
                return true;
            }

            // Check declaration: using (var y = x) or using var y = x
            if (usingStatement.Declaration != null)
            {
                foreach (var variable in usingStatement.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is IdentifierNameSyntax initId &&
                        initId.Identifier.ValueText == variableName)
                    {
                        return true;
                    }
                }
            }

            // Also check nested content
            return HasUsingForVariable(usingStatement.Statement, variableName);
        }

        // Handle local declaration with using: using var x = ...;
        if (statement is LocalDeclarationStatementSyntax localDecl && localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            foreach (var variable in localDecl.Declaration.Variables)
            {
                if (variable.Initializer?.Value is IdentifierNameSyntax initId &&
                    initId.Identifier.ValueText == variableName)
                {
                    return true;
                }
            }
        }

        return false;
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
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
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
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
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
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
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
