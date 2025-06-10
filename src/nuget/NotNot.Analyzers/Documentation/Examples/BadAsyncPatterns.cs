using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace NotNot.Analyzers.Examples;

/// <summary>
/// Examples of problematic async patterns that NotNot.Analyzers will detect and fix
/// </summary>
public class BadAsyncPatterns
{
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// NN_R001: Fire-and-forget patterns
    /// </summary>
    public void FireAndForgetExamples()
    {
        // ❌ Task not awaited, assigned, or returned
        DoWorkAsync(); // Will trigger NN_R001
        
        // ❌ Task<T> not awaited, assigned, or returned  
        GetDataAsync(); // Will trigger NN_R001
        
        // ❌ Multiple fire-and-forget calls
        DoWorkAsync(); // Will trigger NN_R001
        GetDataAsync(); // Will trigger NN_R001
        
        // ✅ Fixes available via code actions:
        // 1. await DoWorkAsync();
        // 2. _ = DoWorkAsync();
        // 3. var task = DoWorkAsync();
    }

    /// <summary>
    /// NN_R002: Unobserved Task<T> results
    /// </summary>
    public async Task UnobservedResultExamples()
    {
        // ❌ Result not observed
        await GetDataAsync(); // Will trigger NN_R002
        
        // ❌ Result ignored in complex expression
        await GetUserDataAsync(); // Will trigger NN_R002
        
        // ✅ Fixes available via code actions:
        // 1. var data = await GetDataAsync();
        // 2. _ = await GetDataAsync();
        // 3. return await GetDataAsync();
    }

    /// <summary>
    /// NN_R003: UI blocking patterns (in UI context)
    /// </summary>
    public async Task UiBlockingExamples()
    {
        // ❌ May cause deadlock in UI context
        await _httpClient.GetStringAsync("https://api.example.com"); // Will trigger NN_R003
        
        // ✅ Better approach
        // await _httpClient.GetStringAsync("https://api.example.com").ConfigureAwait(false);
    }

    /// <summary>
    /// NN_R004: Missing ConfigureAwait in library code
    /// </summary>
    public async Task<string> LibraryCodeExample()
    {
        // ❌ Missing ConfigureAwait(false) in library code
        var response = await _httpClient.GetStringAsync("https://api.example.com"); // Will trigger NN_R004
        return response;
        
        // ✅ Better approach
        // var response = await _httpClient.GetStringAsync("https://api.example.com").ConfigureAwait(false);
        // return response;
    }

    /// <summary>
    /// Complex scenarios with nested async calls
    /// </summary>
    public async Task ComplexScenarios()
    {
        // ❌ Multiple issues in one method
        DoWorkAsync(); // NN_R001
        await GetDataAsync(); // NN_R002
        
        if (DateTime.Now.Hour > 12)
        {
            GetUserDataAsync(); // NN_R001
        }
        
        // ❌ Fire-and-forget in loop
        for (int i = 0; i < 10; i++)
        {
            DoWorkAsync(); // NN_R001 (each iteration)
        }
    }

    /// <summary>
    /// Method chaining scenarios
    /// </summary>
    public async Task MethodChainingExamples()
    {
        // ❌ ConfigureAwait not at the end
        await GetDataAsync().ConfigureAwait(true); // NN_R003 (in UI context)
        
        // ❌ No ConfigureAwait in library
        await GetDataAsync().ContinueWith(t => t.Result.ToUpper()); // NN_R004
    }

    // Helper methods for examples
    private async Task DoWorkAsync()
    {
        await Task.Delay(100);
    }

    private async Task<string> GetDataAsync()
    {
        await Task.Delay(50);
        return "data";
    }

    private async Task<User> GetUserDataAsync()
    {
        await Task.Delay(75);
        return new User { Name = "John", Id = 1 };
    }

    private class User
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
    }
}

/// <summary>
/// Example UI component that demonstrates context-aware analysis
/// </summary>
public class ExamplePage // Simulates a UI page class
{
    private readonly HttpClient _httpClient = new();

    /// <summary>
    /// Event handler where NN_R001 might be suppressed automatically
    /// </summary>
    public async void OnButtonClick(object sender, EventArgs e)
    {
        // This might be auto-suppressed as it's in an event handler
        DoWorkAsync(); // May not trigger NN_R001 due to suppression
        
        // But this will still trigger NN_R003 due to UI context
        await _httpClient.GetStringAsync("https://api.example.com"); // NN_R003
    }

    private async Task DoWorkAsync()
    {
        await Task.Delay(100);
    }
}