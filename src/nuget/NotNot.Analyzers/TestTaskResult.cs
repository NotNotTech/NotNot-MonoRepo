using System;
using System.Threading.Tasks;

namespace LoLo.Analyzers.Reliability.Concurrency;

/// <summary>
/// Test class to verify the TaskResultNotObservedAnalyzer works correctly.
/// This file should trigger NN_R002 errors for unobserved Task<T> results.
/// </summary>
public class TestTaskResult
{
    /// <summary>
    /// This should trigger NN_R002 analyzer error: await result should be observed
    /// </summary>
    public async Task TestFireAndForgetTask()
    {
        // This should trigger NN_R002 analyzer error: await result should be observed
        await DoSomethingAsync();
    }

    /// <summary>
    /// This should NOT trigger any error: result is properly observed
    /// </summary>
    public async Task<bool> TestProperlyObservedTask()
    {
        // This should NOT trigger error: result is properly observed
        var result = await DoSomethingAsync();
        return result;
    }

    /// <summary>
    /// This should NOT trigger any error: result is returned directly
    /// </summary>
    public async Task<bool> TestDirectReturnTask()
    {
        // This should NOT trigger error: result is returned directly
        return await DoSomethingAsync();
    }

    /// <summary>
    /// This should NOT trigger any error: result is used in condition
    /// </summary>
    public async Task TestConditionalUseTask()
    {
        // This should NOT trigger error: result is used in condition
        if (await DoSomethingAsync())
        {
            var tmp = "Task returned true";

        }
    }

    /// <summary>
    /// This should NOT trigger any error: void Task (not Task<T>)
    /// </summary>
    public async Task TestVoidTask()
    {
        // This should NOT trigger error: void Task (not Task<T>)
        await DoVoidAsync();
    }

    /// <summary>
    /// This should trigger NN_R002 analyzer error: ValueTask<T> result not observed
    /// </summary>
    public async Task TestValueTaskNotObserved()
    {
        // This should trigger NN_R002 analyzer error: ValueTask<T> result not observed
        await DoValueTaskAsync();
    }

    /// <summary>
    /// Returns a Task<bool> for testing
    /// </summary>
    private async Task<bool> DoSomethingAsync()
    {
        await Task.Delay(100);
        return true;
    }

    /// <summary>
    /// Returns a void Task for testing
    /// </summary>
    private async Task DoVoidAsync()
    {
        await Task.Delay(100);
    }

    /// <summary>
    /// Returns a ValueTask<int> for testing
    /// </summary>
    private async ValueTask<int> DoValueTaskAsync()
    {
        await Task.Delay(100);
        return 42;
    }
}
