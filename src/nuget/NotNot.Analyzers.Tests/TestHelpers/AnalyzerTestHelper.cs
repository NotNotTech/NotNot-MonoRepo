using System.Text;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;

namespace NotNot.Analyzers.Tests.TestHelpers;

public static class AnalyzerTestHelper
{
	public static DiagnosticResult TaskAwaitedDiagnostic(int line, int column, string taskExpression)
	{
		return new DiagnosticResult(TaskAwaitedOrReturnedAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(taskExpression);
	}

	public static DiagnosticResult TaskResultNotObservedDiagnostic(int line, int column, string taskExpression)
	{
		return new DiagnosticResult(TaskResultNotObservedAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
			.WithLocation(line, column)
			.WithArguments(taskExpression);
	}

	public const string CommonUsings = "using System;\nusing System.Threading.Tasks;\n";

	public static string WrapInClass(string methodBody, string className = "TestClass")
	{
		var sb = new StringBuilder();
		sb.AppendLine(CommonUsings);
		sb.AppendLine();
		sb.AppendLine($"public class {className}");
		sb.AppendLine("{");
		sb.AppendLine(methodBody);
		sb.AppendLine();
		sb.AppendLine("    // Helper methods for testing");
		sb.AppendLine("    private async Task<bool> GetBoolAsync() => await Task.FromResult(true);");
		sb.AppendLine("    private async Task<int> GetIntAsync() => await Task.FromResult(42);");
		sb.AppendLine("    private async Task DoWorkAsync() => await Task.Delay(1);");
		sb.AppendLine("    private async ValueTask<string> GetStringValueTaskAsync() => await ValueTask.FromResult(\"test\");");
		sb.AppendLine("}");
		return sb.ToString();
	}
}
