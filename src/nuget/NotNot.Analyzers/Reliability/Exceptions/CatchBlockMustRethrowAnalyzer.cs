using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability.Exceptions;

/// <summary>
/// Analyzer that enforces catch blocks catching general exception types (Exception, SystemException, or bare catch)
/// must rethrow the exception. Catching specific exception types is allowed without rethrowing.
/// </summary>
/// <remarks>
/// This prevents silent exception swallowing which can mask bugs and make debugging difficult.
///
/// FLAGGED (error):
/// - catch { } - bare catch without rethrow
/// - catch (Exception) { } - catches Exception without rethrow
/// - catch (Exception ex) { Log(ex); } - logs but doesn't rethrow
/// - catch (SystemException) { } - catches SystemException without rethrow
///
/// ALLOWED:
/// - catch (Exception ex) { throw; } - rethrows original exception
/// - catch (Exception ex) { throw new WrapperException(ex); } - rethrows wrapped
/// - catch (Exception ex) when (condition) { } - exception filter narrows scope
/// - catch (IOException ex) { } - specific exception type
/// - catch (JsonException ex) { Log(ex); } - specific exception type can swallow
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CatchBlockMustRethrowAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NN_R005";

    private static readonly LocalizableString Title = "Catch block must rethrow exception";
    private static readonly LocalizableString MessageFormat = "Catch block catches '{0}' but does not rethrow. Either rethrow the exception or catch a more specific exception type.";
    private static readonly LocalizableString Description =
        "Catch blocks that catch general exception types (Exception, SystemException, or bare catch) must rethrow the exception. " +
        "Silent exception swallowing masks bugs and makes debugging difficult. " +
        "Either add 'throw;' to rethrow the original exception, wrap and throw a new exception, or catch a specific exception type instead.";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeCatchClause");

        if (context.Node is not CatchClauseSyntax catchClause) return;

        // Skip if has exception filter - filters narrow the scope intentionally
        if (catchClause.Filter != null) return;

        // Check if this catches a general exception type
        var caughtTypeName = GetCaughtTypeName(catchClause, context);
        if (!IsGeneralExceptionType(caughtTypeName)) return;

        // Check if the catch block contains any throw statement or expression
        if (HasThrowInBlock(catchClause.Block)) return;

        // Report diagnostic - general exception caught without rethrow
        var displayName = string.IsNullOrEmpty(caughtTypeName) ? "(bare catch)" : caughtTypeName;
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            catchClause.CatchKeyword.GetLocation(),
            displayName));
    }

    /// <summary>
    /// Gets the type name being caught, or empty string for bare catch.
    /// </summary>
    private static string GetCaughtTypeName(CatchClauseSyntax catchClause, SyntaxNodeAnalysisContext context)
    {
        if (catchClause.Declaration == null)
        {
            // Bare catch { } - no type specified
            return string.Empty;
        }

        var type = catchClause.Declaration.Type;

        // Try to get the fully qualified name via semantic model
        var typeInfo = context.SemanticModel.GetTypeInfo(type);
        if (typeInfo.Type != null)
        {
            return typeInfo.Type.ToDisplayString();
        }

        // Fall back to syntax name
        return type.ToString();
    }

    /// <summary>
    /// Determines if the caught type is a general exception type that requires rethrow.
    /// </summary>
    /// <remarks>
    /// Only System.Exception and System.SystemException are considered "general" types.
    /// All other exception types (IOException, ArgumentException, etc.) are specific
    /// and can be swallowed without rethrowing.
    /// </remarks>
    private static bool IsGeneralExceptionType(string typeName)
    {
        // Bare catch is always general
        if (string.IsNullOrEmpty(typeName)) return true;

        // Check against known general exception types using the fully qualified name
        // from semantic model (GetCaughtTypeName already resolves this)
        return typeName == "System.Exception" || typeName == "System.SystemException";
    }

    /// <summary>
    /// Checks if the catch block contains any throw statement or throw expression
    /// at the direct catch block scope (not inside nested lambdas/local functions).
    /// </summary>
    private static bool HasThrowInBlock(BlockSyntax? block)
    {
        if (block == null) return false;

        // Use DescendantNodes with a filter to stop traversal at function boundaries.
        // This prevents false negatives where a throw inside a lambda is incorrectly
        // counted as rethrowing from the catch block.
        var directDescendants = block.DescendantNodes(ShouldDescendIntoNode);

        // Check for throw statements: throw; or throw expr;
        var hasThrowStatement = directDescendants
            .OfType<ThrowStatementSyntax>()
            .Any();

        if (hasThrowStatement) return true;

        // Check for throw expressions (C# 7+): x ?? throw new Exception()
        var hasThrowExpression = directDescendants
            .OfType<ThrowExpressionSyntax>()
            .Any();

        return hasThrowExpression;
    }

    /// <summary>
    /// Determines whether to descend into a node during traversal.
    /// Returns false for lambdas, anonymous methods, and local functions to avoid
    /// counting throws in nested scopes as throws in the catch block.
    /// </summary>
    private static bool ShouldDescendIntoNode(SyntaxNode node)
    {
        // Stop at function boundaries - throws inside these don't rethrow from the catch
        return node switch
        {
            LambdaExpressionSyntax => false,           // () => throw ex
            AnonymousMethodExpressionSyntax => false,  // delegate { throw ex; }
            LocalFunctionStatementSyntax => false,     // void Local() { throw ex; }
            _ => true
        };
    }
}
