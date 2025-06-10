using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;

namespace NotNot.Analyzers.Tests.TestHelpers;

/// <summary>
/// Helper class for setting up analyzer tests with common configuration
/// </summary>
public static class AnalyzerTestHelper
{
    /// <summary>
    /// Creates a diagnostic result for TaskAwaitedOrReturnedAnalyzer
    /// </summary>
    public static DiagnosticResult TaskAwaitedDiagnostic(int line, int column, string taskExpression)
    {
        return new DiagnosticResult(TaskAwaitedOrReturnedAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(line, column)
            .WithArguments(taskExpression);
    }

    /// <summary>
    /// Creates a diagnostic result for TaskResultNotObservedAnalyzer
    /// </summary>
    public static DiagnosticResult TaskResultNotObservedDiagnostic(int line, int column, string taskExpression)
    {
        return new DiagnosticResult(TaskResultNotObservedAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(line, column)
            .WithArguments(taskExpression);
    }

    /// <summary>
    /// Common using statements for test code
    /// </summary>
    public const string CommonUsings = @"
using System;
using System.Threading.Tasks;
";

    /// <summary>
    /// Creates a basic class wrapper for test methods
    /// </summary>
    public static string WrapInClass(string methodBody, string className = "TestClass")
    {
        return $@"{CommonUsings}

public class {className}
{{
{methodBody}
    
    // Helper methods for testing
    private async Task&lt;bool&gt; GetBoolAsync() =&gt; await Task.FromResult(true);
    private async Task&lt;int&gt; GetIntAsync() =&gt; await Task.FromResult(42);
    private async Task DoWorkAsync() =&gt; await Task.Delay(1);
    private async ValueTask&lt;string&gt; GetStringValueTaskAsync() =&gt; await ValueTask.FromResult(""test"");
}}";
    }
}