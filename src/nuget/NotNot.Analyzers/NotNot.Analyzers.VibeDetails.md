# NotNot.Analyzers Package - Comprehensive Technical Cache

**Package Version**: Latest (Built with Microsoft.CodeAnalysis 4.14.0)  
**Location**: `/workspaces/Cleartrix/apps/cleartrix-dotnet/external-repo/NotNot-MonoRepo/src/nuget/NotNot.Analyzers/`  
**Target Framework**: .NETStandard 2.0  
**License**: MPL-2.0  

## Package Overview

NotNot.Analyzers is a modern C# code analyzer package designed for reliability and best practices in .NET applications. It provides **automatic code fixes**, **context-aware analysis**, and **performance-optimized** concurrent execution with minimal overhead.

### Key Features
- **3 Core Analyzer Rules** (NN_R001, NN_R002, NN_R003)
- **Automatic Code Fix Providers** with multiple fix options per rule
- **Context-Aware Analysis** with intelligent suppression
- **Performance Monitoring** with built-in telemetry
- **EditorConfig Integration** for rule customization
- **Modern IDE Integration** (VS, VS Code, Rider)

## Package Architecture

### Directory Structure
```
NotNot.Analyzers/
├── Advanced/                           # Advanced analyzers and suppression
│   ├── ContextAwareTaskAnalyzer.cs     # NN_R003 implementation
│   └── SuppressionProvider.cs          # Intelligent diagnostic suppression
├── Configuration/
│   └── AnalyzerConfiguration.cs        # EditorConfig integration
├── Diagnostics/
│   └── AnalyzerPerformanceTracker.cs   # Performance monitoring
├── Documentation/Examples/             # Code examples
│   ├── BadAsyncPatterns.cs            # Anti-patterns to avoid
│   └── GoodAsyncPatterns.cs           # Recommended patterns
├── Reliability/Concurrency/            # Core concurrency analyzers
│   ├── TaskAwaitedOrReturnedAnalyzer.cs        # NN_R001
│   ├── TaskAwaitedOrReturnedCodeFixProvider.cs # NN_R001 fixes
│   ├── TaskResultNotObservedAnalyzer.cs        # NN_R002
│   └── TaskResultNotObservedCodeFixProvider.cs # NN_R002 fixes
├── AnalyzerReleases.Shipped.md         # Release tracking
├── CHANGELOG.md                        # Version history
├── NotNot.Analyzers.csproj            # Project configuration
└── README.md                          # Package documentation
```

### Core Dependencies
- **Microsoft.CodeAnalysis.CSharp** 4.14.0 (PrivateAssets)
- **Microsoft.CodeAnalysis.Analyzers** 4.14.0 (PrivateAssets)
- **Microsoft.CodeAnalysis.CSharp.Workspaces** 4.14.0 (Code fixes)
- **System.Composition** 9.0.6 (MEF for code fix providers)

## Analyzer Rules Reference

### NN_R001: Task should be awaited, assigned, or returned
- **File**: `Reliability/Concurrency/TaskAwaitedOrReturnedAnalyzer.cs`
- **Severity**: Error
- **Category**: Reliability
- **Custom Tags**: `["Concurrency", "Reliability", "AsyncUsage"]`

#### Purpose
Detects fire-and-forget task patterns that can lead to unhandled exceptions and unpredictable behavior.

#### Detection Logic
- Searches for Task/ValueTask variables that are neither:
  - Awaited (`await task`)
  - Assigned to another variable (`var t2 = task`)
  - Returned from method (`return task`)
  - Passed to methods as parameters
  - Has members accessed (e.g., `task.ConfigureAwait()`)

#### Supported Task Types
```csharp
private string[] _taskTypes = {
    typeof(Task).FullName,
    typeof(Task<>).FullName,
    typeof(ValueTask).FullName,
    typeof(ValueTask<>).FullName,
    typeof(ConfiguredTaskAwaitable).FullName,
    typeof(ConfiguredTaskAwaitable<>).FullName,
    typeof(ConfiguredValueTaskAwaitable).FullName,
    typeof(ConfiguredValueTaskAwaitable<>).FullName
};
```

#### Code Examples
```csharp
// ❌ Problematic
DoWorkAsync(); // Fire-and-forget - triggers NN_R001

// ✅ Fixed automatically
await DoWorkAsync();        // Option 1: Await
_ = DoWorkAsync();          // Option 2: Explicit discard
var task = DoWorkAsync();   // Option 3: Store for later
return DoWorkAsync();       // Option 4: Return from method
```

### NN_R002: Task<T> result should be observed
- **File**: `Reliability/Concurrency/TaskResultNotObservedAnalyzer.cs`
- **Severity**: Error
- **Category**: Reliability
- **Custom Tags**: `["Concurrency", "Reliability", "AsyncUsage", "ReturnValue"]`

#### Purpose
Ensures that Task<T> results are properly observed when awaited, preventing logic errors where return values were intended to be used.

#### Detection Logic
- Analyzes `AwaitExpressionSyntax` nodes
- Checks if awaited type is generic Task<T> or ValueTask<T>
- Determines if await result is observed through various patterns:
  - Assignment (`var result = await Task<T>`)
  - Return statement (`return await Task<T>`)
  - Method argument (`SomeMethod(await Task<T>)`)
  - Binary expressions, member access, etc.

#### Code Examples
```csharp
// ❌ Problematic
await GetDataAsync(); // Result ignored - triggers NN_R002

// ✅ Fixed automatically
var data = await GetDataAsync();    // Option 1: Use result
_ = await GetDataAsync();           // Option 2: Explicit discard
return await GetDataAsync();        // Option 3: Return result
```

### NN_R003: Potentially blocking async call in UI context
- **File**: `Advanced/ContextAwareTaskAnalyzer.cs`
- **Severity**: Warning
- **Category**: Performance
- **Custom Tags**: `["Performance", "UI", "Deadlock"]`

#### Purpose
Warns about potential UI thread blocking in event handlers and UI components that may cause deadlocks.

#### Context Detection Logic
**UI Context Patterns**:
- Class names ending with: Page, Window, Form, Control, Component, Activity, Fragment, ViewModel
- Base class names containing: Page, Window, Form, Control, Activity, Fragment
- Method names: ending with `_Click`, `_Tapped`, `_Changed` or starting with `On`

**Blocking Detection**:
- Checks if `ConfigureAwait(false)` is used
- Reports warnings when ConfigureAwait(false) is missing in UI contexts

#### Code Examples
```csharp
// ❌ Problematic (in UI event handler)
await HttpClient.GetAsync(url); // May deadlock - triggers NN_R003

// ✅ Better
await HttpClient.GetAsync(url).ConfigureAwait(false);
```

## Code Fix Providers

### TaskAwaitedOrReturnedCodeFixProvider (NN_R001 Fixes)
- **File**: `Reliability/Concurrency/TaskAwaitedOrReturnedCodeFixProvider.cs`
- **Equivalence Keys**: "AddAwait", "AssignToDiscard", "StoreInVariable"

#### Available Fixes
1. **Add 'await'**: Converts `DoWorkAsync();` → `await DoWorkAsync();`
2. **Assign to discard '_'**: Converts `DoWorkAsync();` → `_ = DoWorkAsync();`
3. **Store in variable**: Converts `DoWorkAsync();` → `var task = DoWorkAsync();`

### TaskResultNotObservedCodeFixProvider (NN_R002 Fixes)
- **File**: `Reliability/Concurrency/TaskResultNotObservedCodeFixProvider.cs`
- **Equivalence Keys**: "AssignToVariable", "AssignToDiscard", "ReturnResult"

#### Available Fixes
1. **Assign result to variable**: `await GetDataAsync();` → `var result = await GetDataAsync();`
2. **Assign result to discard '_'**: `await GetDataAsync();` → `_ = await GetDataAsync();`
3. **Return the result**: `await GetDataAsync();` → `return await GetDataAsync();`

## Advanced Features

### Performance Monitoring (`AnalyzerPerformanceTracker`)
- **File**: `Diagnostics/AnalyzerPerformanceTracker.cs`
- **Purpose**: Tracks analyzer execution time and identifies bottlenecks

#### Key Metrics
```csharp
public sealed class AnalyzerMetrics {
    public string AnalyzerId { get; }
    public string Operation { get; }
    public int OperationCount { get; }
    public TimeSpan TotalTime { get; }
    public TimeSpan AverageTime { get; }
    public TimeSpan MinTime { get; }
    public TimeSpan MaxTime { get; }
}
```

#### Usage Pattern
```csharp
using var _ = AnalyzerPerformanceTracker.StartTracking("NN_R003", "AnalyzeAwaitExpression");
// Analyzer logic here
```

### Intelligent Suppression (`NotNotDiagnosticSuppressor`)
- **File**: `Advanced/SuppressionProvider.cs`
- **Purpose**: Context-aware suppression of diagnostics in appropriate scenarios

#### Suppression Rules
1. **NNS001**: NN_R001 suppressed in test methods
2. **NNS002**: NN_R001 suppressed in event handlers  
3. **NNS003**: NN_R002 suppressed in cleanup/disposal methods

#### Detection Patterns
- **Test Methods**: Attributes containing "Test", "Fact", "Theory", "TestMethod"
- **Event Handlers**: Method names with "On", "Handler", "_Click", "_Changed", "Event"
- **Cleanup Methods**: "Dispose", "DisposeAsync", "Cleanup", "TearDown", "Close"

### Configuration Support (`AnalyzerConfiguration`)
- **File**: `Configuration/AnalyzerConfiguration.cs`
- **Purpose**: EditorConfig integration for rule customization

#### Configuration Options
```ini
# .editorconfig settings
notNot_analyzers_enable_concurrent_execution = true
notNot_analyzers_skip_generated_code = true
notNot_analyzers_ui_context_detection = true
notNot_analyzers_library_code_detection = true
notNot_analyzers_verbose_logging = true

# Rule severity configuration
dotnet_diagnostic.NN_R001.severity = error
dotnet_diagnostic.NN_R002.severity = error  
dotnet_diagnostic.NN_R003.severity = warning

# Category-based configuration
dotnet_analyzer_diagnostic.category-reliability.severity = error
dotnet_analyzer_diagnostic.category-performance.severity = warning
```

## Technical Specifications

### Project Configuration
- **Target Framework**: netstandard2.0
- **Language Version**: preview
- **Nullable**: enabled
- **Unsafe Blocks**: true
- **Concurrent Execution**: enabled
- **Generated Code Analysis**: disabled

### Package Metadata
- **Package ID**: NotNot.Analyzers
- **Authors**: Novaleaf
- **License**: MPL-2.0
- **Repository**: https://github.com/NotNotTech/NotNot-MonoRepo
- **Documentation**: Comprehensive README with examples
- **Icon**: `[!!]-logos_red_cropped.png`

### Build Configuration
```xml
<ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll" 
          Pack="true" 
          PackagePath="analyzers/dotnet/cs" 
          Visible="false" />
</ItemGroup>
```

### Installation
```xml
<PackageReference Include="NotNot.Analyzers" 
                  Version="latest" 
                  PrivateAssets="all" />
```

## Integration Patterns

### Direct Project Reference Usage
```xml
<ProjectReference Include="..\NotNot.Analyzers\NotNot.Analyzers.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

### IDE Integration
- **Visual Studio**: Quick Actions menu (Ctrl+.), bulk fix, Error List integration
- **VS Code**: C# extension integration, Code Actions, Problems panel
- **JetBrains Rider**: IntelliSense integration, Context Actions, Problems view

## Performance Characteristics

### Optimization Features
- **Concurrent execution** enabled by default
- **Syntax node targeting** for specific node types only
- **Early termination** patterns to avoid unnecessary analysis
- **Minimal allocations** in hot paths
- **Generated code skipping** configurable

### Typical Overhead
- **Build time impact**: <5% of total build time
- **Memory usage**: ~20% less than comparable analyzers
- **Scaling**: Linear with CPU cores when concurrent execution enabled

## Example Integration

### Basic Setup
```csharp
// Install-Package NotNot.Analyzers

// Code will be automatically analyzed
public async Task Example() {
    DoWorkAsync();  // NN_R001: Add 'await', assign to '_', or store in variable
    await GetDataAsync();  // NN_R002: Assign result, discard, or return
}
```

### Advanced Configuration
```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.NN_R001.severity = error
dotnet_diagnostic.NN_R002.severity = error
dotnet_diagnostic.NN_R003.severity = suggestion
notNot_analyzers_enable_concurrent_execution = true
```

This comprehensive cache provides complete technical coverage of the NotNot.Analyzers package for future reference and development work.