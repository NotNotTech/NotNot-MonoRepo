using NotNot.BlazorAnalyzers.Reliability;
using NotNot.BlazorAnalyzers.Tests.TestHelpers;

namespace NotNot.BlazorAnalyzers.Tests.Reliability;

/// <summary>
/// Tests for JsonExceptionCatchAnalyzer (NNB008).
/// Verifies detection of catching+eating JsonException from JS interop calls.
/// </summary>
public class JsonExceptionCatchAnalyzerTests
{
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<JsonExceptionCatchAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        // Add Blazor type stubs (includes JsonException and JsonSerializer)
        test.TestState.Sources.Add(BlazorAnalyzerTestHelper.BlazorTypeStubs);

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    private static DiagnosticResult JsonExceptionDiagnostic(string callText)
    {
        return new DiagnosticResult(JsonExceptionCatchAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments(callText);
    }

    /// <summary>
    /// Test 1: Empty catch (JsonException) around InvokeAsync<T> - should REPORT NNB008
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_AroundInvokeAsyncT_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            var result = await _module!.InvokeAsync<string>(""parseJson"", ""data"");
        }
        {|#0:catch (JsonException)
        {
            // WRONG: Silently swallows JS error - fix the JS function instead
        }|}
    }
}";

        await VerifyAnalyzerAsync(source, JsonExceptionDiagnostic("_module!.InvokeAsync"));
    }

    /// <summary>
    /// Test 2: Empty catch (JsonException) around InvokeVoidAsync - should REPORT NNB008
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_AroundInvokeVoidAsync_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync(""cleanup"");
        }
        {|#0:catch (JsonException)
        {
        }|}
    }
}";

        await VerifyAnalyzerAsync(source, JsonExceptionDiagnostic("_module!.InvokeVoidAsync"));
    }

    /// <summary>
    /// Test 3: catch (JsonException) around JsonSerializer.Deserialize - NO DIAGNOSTIC (STJ namespace)
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_AroundJsonSerializerDeserialize_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

public class TestComponent : ComponentBase
{
    public void DoWork()
    {
        try
        {
            var result = JsonSerializer.Deserialize<string>(""invalid json"");
        }
        catch (JsonException)
        {
            // OK: Legitimate STJ usage
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 4: catch (JsonException) around BOTH JS interop AND JsonSerializer - NO DIAGNOSTIC (ambiguous)
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_AroundBothJsInteropAndJsonSerializer_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            // Both JS interop AND STJ in same try block - skip to avoid false positive
            var rawJson = await _module!.InvokeAsync<string>(""getData"");
            var parsed = JsonSerializer.Deserialize<object>(rawJson);
        }
        catch (JsonException)
        {
            // Ambiguous: could be from either call
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 5: catch (JsonException) with non-trivial logic - NO DIAGNOSTIC
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_WithNonTrivialLogic_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;
    private bool _hasError;

    public async Task DoWorkAsync()
    {
        try
        {
            var result = await _module!.InvokeAsync<string>(""parseJson"", ""data"");
        }
        catch (JsonException ex)
        {
            // Has logic - not just eating the exception
            _hasError = true;
            throw new InvalidOperationException(""Parse failed"", ex);
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 6: Non-Blazor class with catch (JsonException) around JS interop - REPORT NNB008
    /// NNB008 is NOT scoped to Blazor components only (unlike NNB007)
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_NonBlazorClass_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.JSInterop;

public class RegularService
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            var result = await _module!.InvokeAsync<string>(""parseJson"", ""data"");
        }
        {|#0:catch (JsonException)
        {
        }|}
    }
}";

        await VerifyAnalyzerAsync(source, JsonExceptionDiagnostic("_module!.InvokeAsync"));
    }

    /// <summary>
    /// Test 7: catch (JsonException ex) when (IsExpected(ex)) with filter - NO DIAGNOSTIC (conditional)
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_WithNonTrivialWhenFilter_NoDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    private bool IsExpected(JsonException ex) => ex.Message.Contains(""expected"");

    public async Task DoWorkAsync()
    {
        try
        {
            var result = await _module!.InvokeAsync<string>(""parseJson"", ""data"");
        }
        catch (JsonException ex) when (IsExpected(ex))
        {
            // Conditional handling - intentional
        }
    }
}";

        await VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Test 8: catch (JsonException) when (true) trivial filter - REPORT NNB008 (equivalent to no filter)
    /// </summary>
    [Fact]
    public async Task JsonExceptionCatch_WithTrivialWhenTrueFilter_ReportsDiagnostic()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

public class TestComponent : ComponentBase
{
    private IJSObjectReference? _module;

    public async Task DoWorkAsync()
    {
        try
        {
            var result = await _module!.InvokeAsync<string>(""parseJson"", ""data"");
        }
        {|#0:catch (JsonException) when (true)
        {
            // Trivial filter = no filter
        }|}
    }
}";

        await VerifyAnalyzerAsync(source, JsonExceptionDiagnostic("_module!.InvokeAsync"));
    }
}
