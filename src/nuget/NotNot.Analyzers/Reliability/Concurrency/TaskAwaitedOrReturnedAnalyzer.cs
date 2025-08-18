using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Reliability.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TaskAwaitedOrReturnedAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///    The diagnostic ID for this analyzer.
    /// </summary>
    public const string DiagnosticId = "NN_R001";

    /// <summary>
    ///    The diagnostic rule for this analyzer.
    /// </summary>
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Task should be awaited, assigned, or returned",
        messageFormat: "Task '{0}' is not awaited, assigned, or returned. This creates a fire-and-forget pattern that can lead to unhandled exceptions and unpredictable behavior.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Tasks that are not awaited, assigned to a variable, or returned from a method create fire-and-forget patterns. " +
                     "This can lead to unhandled exceptions being swallowed and unpredictable timing behavior. " +
                     "Either await the task, assign it to a variable for later use, return it from the method, or explicitly assign to '_' to indicate intentional fire-and-forget.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
        customTags: new[] { "Concurrency", "Reliability", "AsyncUsage" }
    );

    /// <summary>
    ///    The list of task types to check for.
    /// </summary>
    private string[] _taskTypes =
    {
        typeof(Task).FullName, typeof(Task<>).FullName, typeof(ValueTask).FullName, typeof(ValueTask<>).FullName,
        typeof(ConfiguredTaskAwaitable).FullName, typeof(ConfiguredTaskAwaitable<>).FullName,
        typeof(ConfiguredValueTaskAwaitable).FullName, typeof(ConfiguredValueTaskAwaitable<>).FullName,
    };

    /// <summary>
    ///    Gets the supported diagnostics for this analyzer.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>
    ///    Initializes the analysis context for this analyzer.
    /// </summary>
    /// <param name="context">The analysis context.</param>
    public override void Initialize(AnalysisContext context)
    {
        // Modern analyzer configuration
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // Only analyze syntax nodes for better performance
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMemberDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMemberDeclaration, SyntaxKind.PropertyDeclaration);
    }

    private void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        AnalyzeMethodWorker(context, context.Node);
    }

    /// <summary>
    ///    Analyzes expression statements within a method for direct task-returning invocations.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="taskTypeSymbols">The task type symbols to check for.</param>
    /// <param name="methodDeclaration">The method declaration to analyze.</param>
    private void AnalyzeDirectTaskExpressions(SyntaxNodeAnalysisContext context, List<INamedTypeSymbol> taskTypeSymbols, SyntaxNode methodDeclaration)
    {
        // Find expression statements within this method
        var expressionStatements = methodDeclaration.DescendantNodes().OfType<ExpressionStatementSyntax>();

        foreach (var expressionStatement in expressionStatements)
        {
            // Skip if this is already part of an await expression
            if (expressionStatement.Expression is AwaitExpressionSyntax)
                continue;

            // Skip assignments (including discard assignments)
            if (expressionStatement.Expression is AssignmentExpressionSyntax assignment)
            {
                // Discard assignments (_ = task) are intentionally fire-and-forget, so skip them
                // Regular variable assignments are handled by the variable analysis
                continue;
            }

            // Check if the expression returns a task type
            if (IsTaskReturningExpression(context, taskTypeSymbols, expressionStatement.Expression))
            {
                // This is an unawaited task expression - report diagnostic
                var diagnostic = Diagnostic.Create(Rule, expressionStatement.Expression.GetLocation(), expressionStatement.Expression.ToString());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    /// <summary>
    /// Checks if an expression returns a task type, handling various expression types including conditional expressions.
    /// </summary>
    private bool IsTaskReturningExpression(SyntaxNodeAnalysisContext context, List<INamedTypeSymbol> taskTypeSymbols, ExpressionSyntax expression)
    {
        try
        {
            var expressionType = context.SemanticModel.GetTypeInfo(expression).Type as INamedTypeSymbol;
            if (expressionType != null &&
                taskTypeSymbols.Any(taskTypeSymbol =>
                    taskTypeSymbol != null &&
                    (expressionType.Equals(taskTypeSymbol) ||
                     (expressionType.IsGenericType && expressionType.ConstructedFrom.Equals(taskTypeSymbol)))))
            {
                return true;
            }

            // Handle conditional expressions (ternary operator) - check if the result type is a task
            if (expression is ConditionalExpressionSyntax conditionalExpression)
            {
                var conditionalType = context.SemanticModel.GetTypeInfo(conditionalExpression).Type as INamedTypeSymbol;
                if (conditionalType != null &&
                    taskTypeSymbols.Any(taskTypeSymbol =>
                        taskTypeSymbol != null &&
                        (conditionalType.Equals(taskTypeSymbol) ||
                         (conditionalType.IsGenericType && conditionalType.ConstructedFrom.Equals(taskTypeSymbol)))))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // If we can't determine the type, skip analysis
            return false;
        }
    }

    /// <summary>
    ///    analyze member variables, if they are lambdas then we work on them.
    /// </summary>
    /// <param name="context"></param>
    private void AnalyzeMemberDeclaration(SyntaxNodeAnalysisContext context)
    {
        var memberDeclaration = (MemberDeclarationSyntax)context.Node;

        // Look for lambda expressions in the initializer of field and property declarations
        var lambdaExpressions = memberDeclaration.DescendantNodes()
            .OfType<EqualsValueClauseSyntax>()
            .Select(eq => eq.Value)
            .OfType<LambdaExpressionSyntax>();

        foreach (var lambda in lambdaExpressions)
        {
            AnalyzeMethodWorker(context, lambda);
        }
    }


    /// <summary>
    ///    Analyzes the given syntax node for task variables that are not awaited or returned.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private void AnalyzeMethodWorker(SyntaxNodeAnalysisContext context, SyntaxNode methodDeclaration)
    {
        // Get the symbols for the task types.
        var taskTypeSymbols = _taskTypes
            .Select(taskTypeName => context.Compilation.GetTypeByMetadataName(taskTypeName))
            .ToList();

        // Find the task variables in the method declaration.
        var taskVariables = methodDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(variable =>
            {
                try
                {
                    // Get the type of the variable.
                    var variableType =
                       context.SemanticModel.GetTypeInfo(variable.Initializer.Value).Type as INamedTypeSymbol;
                    // Check if the variable type is in the task type symbols.
                    return taskTypeSymbols.Any(taskTypeSymbol => variableType.ConstructedFrom.Equals(taskTypeSymbol));
                }
                catch
                {
                    return false;
                }
            })
            .ToList();

        InspectTaskVariables(context, taskVariables, methodDeclaration);

        // Also analyze expression statements for direct task invocations
        AnalyzeDirectTaskExpressions(context, taskTypeSymbols, methodDeclaration);
    }

    private void InspectTaskVariables(SyntaxNodeAnalysisContext context, List<VariableDeclaratorSyntax> taskVariables,
        SyntaxNode methodDeclaration)
    {
        // Loop through each task variable.
        foreach (var taskVariable in taskVariables)
        {
            // Get the identifier name of the task variable.
            var identifierName = taskVariable.Identifier.ValueText;

            // Get the await expressions and return statements in the method declaration.
            var awaitExpressions = methodDeclaration.DescendantNodes().OfType<AwaitExpressionSyntax>().ToList();
            var returnStatements = methodDeclaration.DescendantNodes().OfType<ReturnStatementSyntax>().ToList();


            //check if any of the requires activities are performed on the declared task
            {
                if (IsAwaited(awaitExpressions, identifierName))
                {
                    continue;
                }

                if (IsReturned(returnStatements, identifierName))
                {
                    continue;
                }

                if (IsAssignedToOtherVariable(context, taskVariable))
                {
                    continue;
                }

                if (IsPassedToMethod(context, taskVariable.Identifier))
                {
                    continue;
                }

                if (IsMemberAccessed(context, taskVariable))
                {
                    continue;
                }
            }
            //no required activies were performed, so Create a diagnostic for the task variable and report it.
            {
                var diagnostic = Diagnostic.Create(Rule, taskVariable.GetLocation(), identifierName);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private bool IsMemberAccessed(SyntaxNodeAnalysisContext context, VariableDeclaratorSyntax taskVariable)
    {
        //return true if any of the task's members are accessed
        var allMemberAccess = context.Node.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>();
        //.Where(m => m.Name.Identifier.Text == "ConfigureAwait");
        foreach (var call in allMemberAccess)
        {
            if (call.Expression is IdentifierNameSyntax identifier)
            {
                if (identifier.Identifier.Text == taskVariable.Identifier.Text)
                {
                    //our task had some member invoked, so we assume it's properly used
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///    Determines if the given task variable is returned.
    /// </summary>
    /// <param name="returnStatements">The list of return statements.</param>
    /// <param name="identifierName">The name of the task variable.</param>
    /// <returns>True if the task variable is returned, false otherwise.</returns>
    private static bool IsReturned(List<ReturnStatementSyntax> returnStatements, string identifierName)
    {
        return returnStatements.Any(returnStatement =>
        {
            var returnIdentifier = returnStatement.Expression as IdentifierNameSyntax;
            return returnIdentifier?.Identifier.ValueText == identifierName;
        });
    }

    /// <summary>
    ///    Determines if the given task variable is awaited.
    /// </summary>
    /// <param name="awaitExpressions">The list of await expressions.</param>
    /// <param name="identifierName">The name of the task variable.</param>
    /// <returns>True if the task variable is awaited, false otherwise.</returns>
    private static bool IsAwaited(List<AwaitExpressionSyntax> awaitExpressions, string identifierName)
    {
        return awaitExpressions.Any(awaitExpression =>
        {
            var awaitIdentifier = awaitExpression.Expression as IdentifierNameSyntax;
            return awaitIdentifier?.Identifier.ValueText == identifierName;
        });
    }

    /// <summary>
    ///    Determines if the given task variable is assigned to another variable.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="variableDeclarator">The variable declarator syntax node.</param>
    /// <returns>True if the task variable is assigned to another variable, false otherwise.</returns>
    private bool IsAssignedToOtherVariable(SyntaxNodeAnalysisContext context,
        VariableDeclaratorSyntax variableDeclarator)
    {
        //check AssignmentExpressionSyntax, if assigned to existing var
        {
            var assignments = context.Node.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>();
            foreach (var assignment in assignments)
            {
                var assignedFrom = assignment.Right as IdentifierNameSyntax;

                if (assignedFrom != null && assignedFrom.Identifier.Text == variableDeclarator.Identifier.Text)
                {
                    return true;
                }
            }
        }
        //check EqualsValueClauseSyntax, if assigned to new var
        {
            var assignments = context.Node.DescendantNodes()
                .OfType<EqualsValueClauseSyntax>();
            foreach (var assignment in assignments)
            {
                var assignedFrom = assignment.Value as IdentifierNameSyntax;

                if (assignedFrom != null && assignedFrom.Identifier.Text == variableDeclarator.Identifier.Text)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///    Determines if the given task variable is passed to a method.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="variableIdentifier">The identifier of the task variable.</param>
    /// <returns>True if the task variable is passed to a method, false otherwise.</returns>
    private bool IsPassedToMethod(SyntaxNodeAnalysisContext context, SyntaxToken variableIdentifier)
    {
        // Get all method invocations in the current context node
        var methodInvocations = context.Node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();


        // Loop through each method invocation
        foreach (var invocation in methodInvocations)
        {
            // Get all argument identifiers in the current invocation
            var argumentIdentifiers = invocation.ArgumentList.Arguments
                .Select(arg => arg.Expression as IdentifierNameSyntax)
                .Where(id => id != null)
                .Select(id => id.Identifier);

            // Check if any argument identifier matches the variable identifier
            if (argumentIdentifiers.Any(id => id.Text == variableIdentifier.Text))
            {
                // If a match is found, return true
                return true;
            }
        }

        // Get all lambda expressions in the current context node
        var lambdaExpressions = context.Node.DescendantNodes()
            .OfType<LambdaExpressionSyntax>();
        // Loop through each lambda expression
        foreach (var lambda in lambdaExpressions)
        {
            // Get all lambda parameter identifiers
            var lambdaParameterIdentifiers = lambda.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier);

            // Check if any lambda parameter identifier matches the variable identifier
            if (lambdaParameterIdentifiers.Any(id => id.Text == variableIdentifier.Text))
            {
                // If a match is found, return true
                return true;
            }

            // Get all lambda body identifiers
            var lambdaBodyIdentifiers = lambda.Body.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier);

            // Check if any lambda body identifier matches the variable identifier
            if (lambdaBodyIdentifiers.Any(id => id.Text == variableIdentifier.Text))
            {
                // If a match is found, return true
                return true;
            }
        }

        // New logic for checking lambda expressions assigned to class members
        var assignmentExpressions = context.Node.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>();
        foreach (var assignment in assignmentExpressions)
        {
            // Check if the right-hand side is a lambda expression
            if (assignment.Right is LambdaExpressionSyntax lambdaExpression)
            {
                // Check if the lambda expression uses the task variable
                var lambdaBodyIdentifiers = lambdaExpression.Body.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(id => id.Identifier);

                if (lambdaBodyIdentifiers.Any(id => id.Text == variableIdentifier.Text))
                {
                    // If the task variable is used within the lambda, return true
                    return true;
                }
            }
        }

        // If no match is found in any method invocation or lambda expression, return false
        return false;
    }
}
