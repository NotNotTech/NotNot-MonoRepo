using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using NotNot.Analyzers.Reliability.Concurrency;
using NotNot.Analyzers.Tests.TestHelpers;
using Xunit;

namespace NotNot.Analyzers.Tests.Reliability.Concurrency;

/// <summary>
/// Comprehensive tests for TaskAwaitedOrReturnedAnalyzer (NN_R001)
/// Verifies that tasks are properly awaited or assigned to prevent fire-and-forget patterns
/// </summary>
public class TaskAwaitedOrReturnedAnalyzerTests
{
    /// <summary>
    /// Analyzer verifier shortcut
    /// </summary>
    private static readonly CSharpAnalyzerTest<TaskAwaitedOrReturnedAnalyzer, DefaultVerifier> TestVerifier = new();

    [Fact]
    public async Task TaskNotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:DoWorkAsync()|};  // Should trigger NN_R001
    }
    
    private async Task DoWorkAsync() =&gt; await Task.Delay(1);
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "DoWorkAsync()");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskProperlyAwaited_ShouldNotReportDiagnostic()
    {
        const string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        await DoWorkAsync(); // Properly awaited - should not trigger
    }");

        await TestVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TaskAssignedToVariable_ShouldNotReportDiagnostic()
    {
        const string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        var task = DoWorkAsync(); // Assigned to variable - should not trigger
        await task;
    }");

        await TestVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TaskAssignedToDiscard_ShouldNotReportDiagnostic()
    {
        const string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        _ = DoWorkAsync(); // Assigned to discard - should not trigger
    }");

        await TestVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TaskReturned_ShouldNotReportDiagnostic()
    {
        const string source = AnalyzerTestHelper.WrapInClass(@"
    public Task TestMethod()
    {
        return DoWorkAsync(); // Returned - should not trigger
    }");

        await TestVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TaskWithGenericResult_NotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:GetBoolAsync()|};  // Should trigger NN_R001
    }
    
    private async Task&lt;bool&gt; GetBoolAsync() =&gt; await Task.FromResult(true);
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "GetBoolAsync()");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task MultipleUnawaitedTasks_ShouldReportMultipleDiagnostics()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:DoWorkAsync()|};   // Should trigger NN_R001
        {|#1:GetBoolAsync()|};  // Should trigger NN_R001
    }
    
    private async Task DoWorkAsync() =&gt; await Task.Delay(1);
    private async Task&lt;bool&gt; GetBoolAsync() =&gt; await Task.FromResult(true);
}";

        var expected1 = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "DoWorkAsync()");
        var expected2 = AnalyzerTestHelper.TaskAwaitedDiagnostic(9, 9, "GetBoolAsync()");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected1, expected2);
    }

    [Fact]
    public async Task TaskInMethodChain_NotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:DoWorkAsync().ConfigureAwait(false)|};  // Should trigger NN_R001
    }
    
    private async Task DoWorkAsync() =&gt; await Task.Delay(1);
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "DoWorkAsync().ConfigureAwait(false)");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskInConditionalExpression_NotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        var condition = true;
        {|#0:condition ? DoWorkAsync() : GetBoolAsync()|};  // Should trigger NN_R001
    }
    
    private async Task DoWorkAsync() =&gt; await Task.Delay(1);
    private async Task&lt;bool&gt; GetBoolAsync() =&gt; await Task.FromResult(true);
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(9, 9, "condition ? DoWorkAsync() : GetBoolAsync()");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskPassedToMethod_ShouldNotReportDiagnostic()
    {
        const string source = AnalyzerTestHelper.WrapInClass(@"
    public async Task TestMethod()
    {
        ProcessTask(DoWorkAsync()); // Passed to method - should not trigger
    }
    
    private void ProcessTask(Task task) { }");

        await TestVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ValueTaskNotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:GetStringValueTaskAsync()|};  // Should trigger NN_R001
    }
    
    private async ValueTask&lt;string&gt; GetStringValueTaskAsync() =&gt; await ValueTask.FromResult(""test"");
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "GetStringValueTaskAsync()");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskFromResult_NotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:Task.FromResult(42)|};  // Should trigger NN_R001
    }
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "Task.FromResult(42)");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TaskRun_NotAwaited_ShouldReportDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        {|#0:Task.Run(() =&gt; 42)|};  // Should trigger NN_R001
    }
}";

        var expected = AnalyzerTestHelper.TaskAwaitedDiagnostic(8, 9, "Task.Run(() => 42)");
        
        await TestVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task SyncMethod_ShouldNotReportDiagnostic()
    {
        const string source = AnalyzerTestHelper.WrapInClass(@"
    public void TestMethod()
    {
        DoWork(); // Synchronous method - should not trigger
    }
    
    private void DoWork() { }");

        await TestVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TaskUsedInUsingStatement_ShouldNotReportDiagnostic()
    {
        const string source = @"
using System;
using System.Threading.Tasks;

public class TestClass
{
    public async Task TestMethod()
    {
        using (var cts = new System.Threading.CancellationTokenSource())
        {
            await DoWorkAsync(); // Inside using - should not trigger
        }
    }
    
    private async Task DoWorkAsync() =&gt; await Task.Delay(1);
}";

        await TestVerifier.VerifyAnalyzerAsync(source);
    }
}