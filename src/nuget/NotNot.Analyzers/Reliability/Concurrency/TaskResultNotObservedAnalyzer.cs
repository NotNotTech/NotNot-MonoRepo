using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Reliability.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TaskResultNotObservedAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///    The diagnostic ID for this analyzer.
    /// </summary>
    public const string DiagnosticId = "NN_R002";

    /// <summary>
    ///    The diagnostic rule for this analyzer.
    /// </summary>
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Task<T> result should be observed or explicitly discarded",
        messageFormat: "Task<T> '{0}' is awaited but its result is not observed. The returned value should be used, assigned to a variable, or explicitly discarded with '_'.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Tasks that return values (Task<T> or ValueTask<T>) should have their results observed when awaited. " +
                     "Ignoring return values can indicate a logic error where the result was intended to be used. " +
                     "If the result is intentionally unused, assign it to the discard variable '_' to make this explicit.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
        customTags: new[] { "Concurrency", "Reliability", "AsyncUsage", "ReturnValue" }
    );

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

        // Register for await expressions only for better performance
        context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);
    }

    /// <summary>
    ///    Analyzes await expressions to detect when Task<T> results are not observed.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    private void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
    {
        var awaitExpression = (AwaitExpressionSyntax)context.Node;

        // Get the type information for the awaited expression
        var typeInfo = context.SemanticModel.GetTypeInfo(awaitExpression.Expression);
        var awaitedType = typeInfo.Type as INamedTypeSymbol;

        if (awaitedType == null)
            return;

        // Check if this is a generic Task<T> or ValueTask<T>
        if (!IsGenericTaskType(context, awaitedType))
            return;

        // Check if the await result is being observed
        if (IsAwaitResultObserved(awaitExpression))
            return;

        // Get method name or expression for error message
        var methodName = GetMethodNameFromAwaitExpression(awaitExpression);

        // Create diagnostic for unobserved await result
        var diagnostic = Diagnostic.Create(Rule, awaitExpression.GetLocation(), methodName);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    ///    Determines if the awaited type is a generic Task<T> or ValueTask<T>.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a generic task type with a result.</returns>
    private bool IsGenericTaskType(SyntaxNodeAnalysisContext context, INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
            return false;

        // Get the generic Task<T> and ValueTask<T> type symbols
        var genericTaskType = context.Compilation.GetTypeByMetadataName(typeof(Task<>).FullName);
        var genericValueTaskType = context.Compilation.GetTypeByMetadataName(typeof(ValueTask<>).FullName);

        // Check if the type is constructed from Task<T> or ValueTask<T>
        return (genericTaskType != null && type.ConstructedFrom.Equals(genericTaskType)) ||
                  (genericValueTaskType != null && type.ConstructedFrom.Equals(genericValueTaskType));
    }

    /// <summary>
    ///    Determines if the await expression's result is being observed.
    /// </summary>
    /// <param name="awaitExpression">The await expression to analyze.</param>
    /// <returns>True if the result is observed, false otherwise.</returns>
    private bool IsAwaitResultObserved(AwaitExpressionSyntax awaitExpression)
    {
        var parent = awaitExpression.Parent;

        // Check various patterns where the result is observed
        switch (parent)
        {
            // Assignment: var result = await Task<T>
            case VariableDeclaratorSyntax:
            case AssignmentExpressionSyntax:
                return true;

            // Return statement: return await Task<T>
            case ReturnStatementSyntax:
                return true;

            // Expression-bodied method/property: => await Task<T>
            case ArrowExpressionClauseSyntax:
                return true;

            // Method argument: SomeMethod(await Task<T>)
            case ArgumentSyntax:
                return true;

            // Lambda expression body: x => await Task<T>
            case SimpleLambdaExpressionSyntax:
            case ParenthesizedLambdaExpressionSyntax:
                return true;

            // Binary expression: if (await Task<T> == something)
            case BinaryExpressionSyntax:
                return true;

            // Member access: (await Task<T>).ToString()
            case MemberAccessExpressionSyntax:
                return true;

            // Invocation: (await Task<T>).SomeMethod()
            case InvocationExpressionSyntax invocation when invocation.Expression == awaitExpression:
                return true;

            // Element access: (await Task<T>)[index]
            case ElementAccessExpressionSyntax:
                return true;

            // Conditional access: (await Task<T>)?.Property
            case ConditionalAccessExpressionSyntax:
                return true;

            // Cast expression: (SomeType)(await Task<T>)
            case CastExpressionSyntax:
                return true;

            // Using statement: using (await Task<T>)
            case UsingStatementSyntax:
                return true;

            // Expression statement - this means the await is standalone
            case ExpressionStatementSyntax:
                return false;

            // For other cases, check if the await expression is part of a larger expression
            default:
                return IsPartOfLargerExpression(awaitExpression);
        }
    }

    /// <summary>
    ///    Checks if the await expression is part of a larger expression where the result is used.
    /// </summary>
    /// <param name="awaitExpression">The await expression.</param>
    /// <returns>True if part of a larger expression, false otherwise.</returns>
    private bool IsPartOfLargerExpression(AwaitExpressionSyntax awaitExpression)
    {
        var current = awaitExpression.Parent;

        while (current != null)
        {
            switch (current)
            {
                // If we reach a statement that's not an expression statement,
                // then the await is being used in some way
                case StatementSyntax when !(current is ExpressionStatementSyntax):
                    return true;

                // If we find expressions that use the value
                case BinaryExpressionSyntax:
                case ConditionalExpressionSyntax:
                case InterpolationSyntax:
                case InitializerExpressionSyntax:
                case SimpleLambdaExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                    return true;

                // If we're inside a lambda expression body, the result is being used
                case LambdaExpressionSyntax:
                    return true;

                // If we reach an expression statement, the await result is not used
                case ExpressionStatementSyntax:
                    return false;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    ///    Extracts a method name or expression description from the await expression for error reporting.
    /// </summary>
    /// <param name="awaitExpression">The await expression.</param>
    /// <returns>A string describing the awaited method or expression.</returns>
    private string GetMethodNameFromAwaitExpression(AwaitExpressionSyntax awaitExpression)
    {
        switch (awaitExpression.Expression)
        {
            case InvocationExpressionSyntax invocation:
                return ExtractMethodName(invocation);

            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.ValueText;

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.ValueText;

            default:
                return awaitExpression.Expression.ToString();
        }
    }

    /// <summary>
    ///    Extracts the method name from an invocation expression.
    /// </summary>
    /// <param name="invocation">The invocation expression.</param>
    /// <returns>The method name with parentheses.</returns>
    private string ExtractMethodName(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.ValueText + "()";

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.ValueText + "()";

            default:
                return invocation.Expression.ToString() + "()";
        }
    }
}
