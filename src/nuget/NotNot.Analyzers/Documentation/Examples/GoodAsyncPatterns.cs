using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace NotNot.Analyzers.Examples;

/// <summary>
/// Examples of correct async patterns that NotNot.Analyzers approves of
/// </summary>
public class GoodAsyncPatterns
{
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// ✅ Proper task handling - no NN_R001 violations
    /// </summary>
    public async Task ProperTaskHandling()
    {
        // ✅ Awaited properly
        await DoWorkAsync();
        
        // ✅ Assigned to variable for later use
        var task = DoWorkAsync();
        // ... do other work ...
        await task;
        
        // ✅ Explicitly discarded (intentional fire-and-forget)
        _ = DoWorkAsync();
        
        // ✅ Returned from method
        // return DoWorkAsync(); // (if this method returned Task)
    }

    /// <summary>
    /// ✅ Proper result observation - no NN_R002 violations
    /// </summary>
    public async Task ProperResultObservation()
    {
        // ✅ Result captured and used
        var data = await GetDataAsync();
        Console.WriteLine($"Received: {data}");
        
        // ✅ Result used directly in expression
        if (await GetBoolAsync())
        {
            Console.WriteLine("Condition was true");
        }
        
        // ✅ Result explicitly discarded
        _ = await GetDataAsync();
        
        // ✅ Result used in method call
        Console.WriteLine(await GetDataAsync());
        
        // ✅ Result used in string interpolation
        var message = $"Data: {await GetDataAsync()}";
    }

    /// <summary>
    /// ✅ Library code with proper ConfigureAwait - no NN_R004 violations
    /// </summary>
    public async Task<string> LibraryCodeBestPractices()
    {
        // ✅ ConfigureAwait(false) in library code
        var response = await _httpClient.GetStringAsync("https://api.example.com")
            .ConfigureAwait(false);
        
        // ✅ Multiple awaits with ConfigureAwait
        var data1 = await GetDataAsync().ConfigureAwait(false);
        var data2 = await GetDataAsync().ConfigureAwait(false);
        
        return $"{response}-{data1}-{data2}";
    }

    /// <summary>
    /// ✅ UI code with proper patterns - minimal NN_R003 violations
    /// </summary>
    public async Task UiCodeBestPractices()
    {
        // ✅ ConfigureAwait(false) prevents UI blocking
        var data = await _httpClient.GetStringAsync("https://api.example.com")
            .ConfigureAwait(false);
        
        // ✅ For UI updates, capture context (don't use ConfigureAwait(false))
        var userData = await GetUserDataAsync();
        // UpdateUI(userData); // This needs UI context
    }

    /// <summary>
    /// ✅ Complex async patterns done correctly
    /// </summary>
    public async Task ComplexPatternsCorrect()
    {
        // ✅ Parallel execution with proper awaiting
        var task1 = GetDataAsync();
        var task2 = GetUserDataAsync();
        var task3 = DoWorkAsync();
        
        // Wait for all to complete
        await Task.WhenAll(task1, task2, task3);
        
        // Use results properly
        var data = await task1;
        var user = await task2;
        // task3 returns void, so no result to observe
        
        Console.WriteLine($"Data: {data}, User: {user.Name}");
    }

    /// <summary>
    /// ✅ Exception handling with async
    /// </summary>
    public async Task ExceptionHandlingCorrect()
    {
        try
        {
            var result = await GetDataAsync().ConfigureAwait(false);
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            
            // ✅ Even in catch blocks, follow the patterns
            _ = LogErrorAsync(ex); // Intentional fire-and-forget for logging
        }
    }

    /// <summary>
    /// ✅ Conditional async execution
    /// </summary>
    public async Task ConditionalAsyncCorrect()
    {
        if (await ShouldProcessAsync().ConfigureAwait(false))
        {
            var data = await GetDataAsync().ConfigureAwait(false);
            Console.WriteLine($"Processing: {data}");
        }
        else
        {
            Console.WriteLine("Skipping processing");
        }
    }

    /// <summary>
    /// ✅ Loop with async - proper patterns
    /// </summary>
    public async Task AsyncLoopCorrect()
    {
        var items = new[] { "a", "b", "c" };
        
        // ✅ Sequential processing
        foreach (var item in items)
        {
            var result = await ProcessItemAsync(item).ConfigureAwait(false);
            Console.WriteLine($"Processed {item}: {result}");
        }
        
        // ✅ Parallel processing with proper awaiting
        var tasks = items.Select(item => ProcessItemAsync(item)).ToArray();
        var results = await Task.WhenAll(tasks);
        
        for (int i = 0; i < items.Length; i++)
        {
            Console.WriteLine($"Parallel processed {items[i]}: {results[i]}");
        }
    }

    /// <summary>
    /// ✅ Resource disposal with async
    /// </summary>
    public async Task ResourceDisposalCorrect()
    {
        // ✅ Using statement with async
        using var httpClient = new HttpClient();
        var data = await httpClient.GetStringAsync("https://api.example.com")
            .ConfigureAwait(false);
        
        // ✅ Async using for IAsyncDisposable
        // await using var resource = new AsyncDisposableResource();
        // var result = await resource.ProcessAsync().ConfigureAwait(false);
    }

    // Helper methods
    private async Task DoWorkAsync()
    {
        await Task.Delay(100).ConfigureAwait(false);
    }

    private async Task<string> GetDataAsync()
    {
        await Task.Delay(50).ConfigureAwait(false);
        return "data";
    }

    private async Task<bool> GetBoolAsync()
    {
        await Task.Delay(25).ConfigureAwait(false);
        return true;
    }

    private async Task<User> GetUserDataAsync()
    {
        await Task.Delay(75).ConfigureAwait(false);
        return new User { Name = "John", Id = 1 };
    }

    private async Task<bool> ShouldProcessAsync()
    {
        await Task.Delay(10).ConfigureAwait(false);
        return DateTime.Now.Millisecond % 2 == 0;
    }

    private async Task<string> ProcessItemAsync(string item)
    {
        await Task.Delay(30).ConfigureAwait(false);
        return item.ToUpper();
    }

    private async Task LogErrorAsync(Exception ex)
    {
        await Task.Delay(10).ConfigureAwait(false);
        // Log the error somewhere
        Console.WriteLine($"Logged: {ex.Message}");
    }

    private class User
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
    }
}

/// <summary>
/// ✅ Test class example - fire-and-forget might be auto-suppressed
/// </summary>
public class AsyncTestPatterns
{
    /// <summary>
    /// Test method where NN_R001 might be suppressed
    /// </summary>
    [Fact] // Simulating xUnit attribute
    public async Task TestAsyncPatterns()
    {
        // In test methods, fire-and-forget might be auto-suppressed
        // since it's often used for setup/cleanup
        DoBackgroundSetupAsync(); // Might not trigger NN_R001
        
        // But proper patterns are still recommended
        var result = await GetDataAsync();
        result.Should().NotBeNull(); // Simulating FluentAssertions
    }

    /// <summary>
    /// Test cleanup where result observation might be suppressed
    /// </summary>
    [Fact]
    public async Task TestCleanupPatterns()
    {
        try
        {
            var data = await GetDataAsync();
            // Test logic here
        }
        finally
        {
            // Cleanup code - NN_R002 might be suppressed here
            await CleanupAsync(); // Result might be ignored intentionally
        }
    }

    private async Task DoBackgroundSetupAsync()
    {
        await Task.Delay(10);
    }

    private async Task<string> GetDataAsync()
    {
        await Task.Delay(50);
        return "test-data";
    }

    private async Task<bool> CleanupAsync()
    {
        await Task.Delay(20);
        return true; // Return value might be ignored in cleanup
    }
}

// Simulated attributes for the example
public class FactAttribute : Attribute { }