using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Architecture;

/// <summary>
/// Analyzer that detects redundant Maybe<T> reconstruction patterns where the result
/// is already a Maybe<T> but is being unnecessarily deconstructed and reconstructed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class DirectMaybeReturnAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_A002";

    private static readonly LocalizableString Title = "Return Maybe directly without reconstruction";
    private static readonly LocalizableString MessageFormat = "Method '{0}' unnecessarily reconstructs Maybe result - return the Maybe directly";
    private static readonly LocalizableString Description = "When a method already returns a Maybe<T>, return it directly instead of checking IsSuccess and reconstructing the result.";
    private const string Category = "Architecture";

    private static readonly DiagnosticDescriptor Rule = new(
         DiagnosticId,
         Title,
         MessageFormat,
         Category,
         DiagnosticSeverity.Error,
         isEnabledByDefault: true,
         description: Description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeMethod");

        if (context.Node is not MethodDeclarationSyntax method) return;

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null) return;

        // Only analyze methods that return Maybe or Task<Maybe>
        if (!ReturnsMaybe(methodSymbol.ReturnType)) return;

        // Look for the anti-pattern in the method body
        var body = method.Body;
        if (body == null) return;

        // Find all if statements that check Maybe.IsSuccess
        var ifStatements = body.DescendantNodes()
             .OfType<IfStatementSyntax>()
             .Where(ifStmt => ChecksMaybeIsSuccess(ifStmt, context.SemanticModel));

        foreach (var ifStatement in ifStatements)
        {
            if (IsRedundantMaybeReconstruction(ifStatement, context.SemanticModel))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                     Rule,
                     ifStatement.GetLocation(),
                     methodSymbol.Name));
            }
        }
    }

    /// <summary>
    /// Determines if a type is Maybe or Maybe<T> or wrapped in Task/ValueTask.
    /// </summary>
    private static bool ReturnsMaybe(ITypeSymbol returnType)
    {
        var typeName = returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        // Direct Maybe or Maybe<T>
        if (typeName == "Maybe" || typeName.StartsWith("Maybe<"))
            return true;

        // Unwrap Task/ValueTask wrappers
        if (returnType is INamedTypeSymbol namedType)
        {
            if ((namedType.Name == "Task" || namedType.Name == "ValueTask") && namedType.TypeArguments.Length == 1)
            {
                var innerType = namedType.TypeArguments[0];
                var innerTypeName = innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return innerTypeName == "Maybe" || innerTypeName.StartsWith("Maybe<");
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an if statement is checking Maybe.IsSuccess.
    /// </summary>
    private static bool ChecksMaybeIsSuccess(IfStatementSyntax ifStatement, SemanticModel semanticModel)
    {
        var condition = ifStatement.Condition;

        // Look for patterns like !result.IsSuccess or result.IsSuccess == false
        if (condition is PrefixUnaryExpressionSyntax negation &&
             negation.IsKind(SyntaxKind.LogicalNotExpression))
        {
            condition = negation.Operand;
        }
        else if (condition is BinaryExpressionSyntax binaryExpr &&
                    binaryExpr.IsKind(SyntaxKind.EqualsExpression) &&
                    binaryExpr.Right is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            condition = binaryExpr.Left;
        }
        else
        {
            return false;
        }

        // Check if the condition is accessing IsSuccess property on a Maybe type
        if (condition is MemberAccessExpressionSyntax memberAccess &&
             memberAccess.Name.Identifier.Text == "IsSuccess")
        {
            var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
            if (typeInfo.Type != null)
            {
                var typeName = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return typeName == "Maybe" || typeName.StartsWith("Maybe<");
            }
        }

        return false;
    }

    /// <summary>
    /// Determines if an if statement represents redundant Maybe reconstruction.
    /// </summary>
    private static bool IsRedundantMaybeReconstruction(IfStatementSyntax ifStatement, SemanticModel semanticModel)
    {
        // Pattern 1: if (!result.IsSuccess) return result.Problem!;
        // followed by: return Maybe.Success(result.Value) or return new Maybe()

        var thenStatement = ifStatement.Statement;
        ReturnStatementSyntax? problemReturn = null;

        // Check if the then block returns Problem
        if (thenStatement is BlockSyntax thenBlock)
        {
            problemReturn = thenBlock.Statements
                 .OfType<ReturnStatementSyntax>()
                 .FirstOrDefault();
        }
        else if (thenStatement is ReturnStatementSyntax returnStmt)
        {
            problemReturn = returnStmt;
        }

        if (problemReturn?.Expression == null) return false;

        // Check if it's returning result.Problem
        bool returnsProblem = false;
        if (problemReturn.Expression is MemberAccessExpressionSyntax problemAccess &&
             problemAccess.Name.Identifier.Text == "Problem")
        {
            returnsProblem = true;
        }
        else if (problemReturn.Expression is PostfixUnaryExpressionSyntax postfix &&
                    postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
                    postfix.Operand is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.Text == "Problem")
        {
            returnsProblem = true;
        }

        if (!returnsProblem) return false;

        // Now check if there's a subsequent return that reconstructs Maybe
        var parent = ifStatement.Parent;
        if (parent is BlockSyntax parentBlock)
        {
            var ifIndex = parentBlock.Statements.IndexOf(ifStatement);
            if (ifIndex < parentBlock.Statements.Count - 1)
            {
                var nextStatement = parentBlock.Statements[ifIndex + 1];
                if (nextStatement is ReturnStatementSyntax nextReturn && nextReturn.Expression != null)
                {
                    // Check for patterns:
                    // - return Maybe.Success(result.Value)
                    // - return new Maybe()
                    // - return Maybe.SuccessResult()
                    var expr = nextReturn.Expression;

                    // Check for Maybe.Success or Maybe.SuccessResult
                    if (expr is InvocationExpressionSyntax invocation)
                    {
                        if (invocation.Expression is MemberAccessExpressionSyntax maybeAccess)
                        {
                            // Check for Maybe.Success or Maybe.SuccessResult
                            if (maybeAccess.Expression is IdentifierNameSyntax identifier &&
                                identifier.Identifier.Text == "Maybe" &&
                                (maybeAccess.Name.Identifier.Text == "Success" ||
                                 maybeAccess.Name.Identifier.Text == "SuccessResult"))
                            {
                                return true;
                            }
                            // Check for Maybe<T>.Success pattern
                            else if (maybeAccess.Expression is GenericNameSyntax genericName &&
                                     genericName.Identifier.Text == "Maybe" &&
                                     maybeAccess.Name.Identifier.Text == "Success")
                            {
                                return true;
                            }
                        }
                    }
                    // Check for new Maybe()
                    else if (expr is ObjectCreationExpressionSyntax objectCreation)
                    {
                        var typeInfo = semanticModel.GetTypeInfo(objectCreation);
                        if (typeInfo.Type != null)
                        {
                            var typeName = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                            if (typeName == "Maybe" || typeName.StartsWith("Maybe<"))
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