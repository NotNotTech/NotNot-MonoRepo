namespace NotNot.BlazorAnalyzers.Tests.TestHelpers;

/// <summary>
/// Helper class for Blazor analyzer tests providing common type stubs.
/// </summary>
public static class BlazorAnalyzerTestHelper
{
    /// <summary>
    /// Blazor and JSInterop type stubs needed for analyzer tests.
    /// These stubs provide minimal implementations of Blazor types for testing analyzers.
    /// </summary>
    public const string BlazorTypeStubs = @"
using System;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Components
{
    public interface IComponent { }

    public abstract class ComponentBase : IComponent
    {
        protected virtual void OnInitialized() { }
        protected virtual Task OnInitializedAsync() => Task.CompletedTask;
        protected virtual void OnParametersSet() { }
        protected virtual Task OnParametersSetAsync() => Task.CompletedTask;
        protected virtual void OnAfterRender(bool firstRender) { }
        protected virtual Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
        protected virtual bool ShouldRender() => true;
        protected void StateHasChanged() { }
        protected Task InvokeAsync(Action workItem) => Task.CompletedTask;
        protected Task InvokeAsync(Func<Task> workItem) => Task.CompletedTask;
    }
}

namespace Microsoft.JSInterop
{
    public interface IJSRuntime
    {
        ValueTask<TValue> InvokeAsync<TValue>(string identifier, params object?[]? args);
        ValueTask InvokeVoidAsync(string identifier, params object?[]? args);
    }

    public interface IJSObjectReference : IAsyncDisposable
    {
        ValueTask<TValue> InvokeAsync<TValue>(string identifier, params object?[]? args);
        ValueTask InvokeVoidAsync(string identifier, params object?[]? args);
    }

    public interface IJSInProcessObjectReference : IJSObjectReference, IDisposable { }

    public sealed class DotNetObjectReference<TValue> : IDisposable where TValue : class
    {
        private TValue? _value;
        public TValue Value => _value!;
        public static DotNetObjectReference<TValue> Create(TValue value) => new();
        public void Dispose() { }
    }

    public class JSDisconnectedException : Exception
    {
        public JSDisconnectedException(string message) : base(message) { }
    }

    public class JSException : Exception
    {
        public JSException(string message) : base(message) { }
    }
}

namespace System.Text.Json
{
    public class JsonException : Exception
    {
        public JsonException() { }
        public JsonException(string message) : base(message) { }
    }

    public static class JsonSerializer
    {
        public static T? Deserialize<T>(string json) => default;
        public static string Serialize<T>(T value) => string.Empty;
    }
}
";
}
