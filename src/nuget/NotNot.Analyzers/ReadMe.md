
# NotNot.Analyzers

**Modern C# code analyzers for reliability and best practices in .NET applications.**

[![NuGet](https://img.shields.io/nuget/v/NotNot.Analyzers.svg)](https://www.nuget.org/packages/NotNot.Analyzers/)
[![License: MPL-2.0](https://img.shields.io/badge/License-MPL--2.0-blue.svg)](https://opensource.org/licenses/MPL-2.0)

## 🚀 Features

- **Automatic Code Fixes** - Get instant suggestions to fix common async/await issues
- **Context-Aware Analysis** - Smart detection based on your code's context (UI, library, test code)
- **Performance Optimized** - Concurrent execution with minimal overhead
- **Configurable Rules** - Customize severity and behavior via EditorConfig
- **Modern IDE Integration** - Works seamlessly with Visual Studio, VS Code, and Rider

## 📦 Installation

### Package Manager Console
```powershell
Install-Package NotNot.Analyzers
```

### .NET CLI
```bash
dotnet add package NotNot.Analyzers
```

### PackageReference
```xml
<PackageReference Include="NotNot.Analyzers" Version="latest" PrivateAssets="all" />
```

> **Note:** Use `PrivateAssets="all"` to prevent the analyzer from being transitively referenced.

## 🔍 Analyzer Rules

### NN_R001: Task should be awaited, assigned, or returned
**Severity:** Error  
**Category:** Reliability

Detects fire-and-forget task patterns that can lead to unhandled exceptions.

```csharp
// ❌ Problematic
DoWorkAsync(); // Fire-and-forget

// ✅ Fixed automatically
await DoWorkAsync();        // Option 1: Await
_ = DoWorkAsync();          // Option 2: Explicit discard
var task = DoWorkAsync();   // Option 3: Store for later
return DoWorkAsync();       // Option 4: Return from method
```

### NN_R002: Task<T> result should be observed
**Severity:** Error  
**Category:** Reliability

Ensures that Task<T> results are properly observed when awaited.

```csharp
// ❌ Problematic
await GetDataAsync(); // Result ignored

// ✅ Fixed automatically
var data = await GetDataAsync();    // Option 1: Use result
_ = await GetDataAsync();           // Option 2: Explicit discard
return await GetDataAsync();        // Option 3: Return result
```

### NN_R003: Potentially blocking async call in UI context
**Severity:** Warning  
**Category:** Performance

Warns about potential UI thread blocking in event handlers and UI components.

```csharp
// ❌ Problematic (in UI event handler)
await HttpClient.GetAsync(url); // May deadlock

// ✅ Better
await HttpClient.GetAsync(url).ConfigureAwait(false);
```

## ⚙️ Configuration

Configure rules using `.editorconfig`:

```ini
[*.cs]
# Configure rule severity
dotnet_diagnostic.NN_R001.severity = error
dotnet_diagnostic.NN_R002.severity = error
dotnet_diagnostic.NN_R003.severity = warning

# Disable specific rules
dotnet_diagnostic.NN_R003.severity = none

# Category-based configuration
dotnet_analyzer_diagnostic.category-reliability.severity = error
dotnet_analyzer_diagnostic.category-performance.severity = warning
```

### Advanced Configuration

```ini
# Performance settings
notNot_analyzers_enable_concurrent_execution = true
notNot_analyzers_skip_generated_code = true

# Context-aware features
notNot_analyzers_ui_context_detection = true
notNot_analyzers_library_code_detection = true
```

## 🎯 Smart Suppressions

The analyzers include intelligent suppression for common scenarios:

- **Test Methods** - Automatic suppression of NN_R001 in test methods where fire-and-forget is often intentional
- **Event Handlers** - Reduced warnings for UI event handlers where blocking patterns are common
- **Cleanup Code** - Suppression of NN_R002 in `Dispose` and cleanup methods

## 🛠️ IDE Integration

### Visual Studio
- Automatic code fixes appear in the Quick Actions menu (Ctrl+.)
- Bulk fix available for entire projects
- Integration with Error List and Solution Explorer

### VS Code
- Works with C# extension
- Fixes available through Code Actions
- Integrated with Problems panel

### JetBrains Rider
- Full IntelliSense integration
- Context Actions for automatic fixes
- Inspection results in Problems view

## 📊 Performance Monitoring

Built-in performance tracking helps identify analyzer overhead:

```csharp
// Access performance metrics (in debug builds)
var metrics = AnalyzerPerformanceTracker.GetAllMetrics();
foreach (var metric in metrics)
{
    Console.WriteLine($"{metric.AnalyzerId}: {metric.AverageTime.TotalMilliseconds:F2}ms avg");
}
```

## 🔧 Troubleshooting

### Common Issues

**1. Analyzer not running**
- Ensure `PrivateAssets="all"` is set in PackageReference
- Restart IDE after installation
- Check that analysis is enabled in IDE settings

**2. Too many warnings**
- Configure severity levels in `.editorconfig`
- Use bulk suppression for legacy code
- Consider gradual adoption with `severity = suggestion`

**3. Performance issues**
- Enable concurrent execution: `notNot_analyzers_enable_concurrent_execution = true`
- Skip generated code: `notNot_analyzers_skip_generated_code = true`
- Monitor performance with built-in tracking

### Debugging

Enable verbose logging in `.editorconfig`:
```ini
notNot_analyzers_verbose_logging = true
```

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

### Development Setup
1. Clone the repository
2. Open in Visual Studio 2022 or later
3. Build the solution
4. Run tests with `dotnet test`

### Creating Custom Rules
```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MyCustomAnalyzer : DiagnosticAnalyzer
{
    // Implement your custom logic
}
```

## 📄 License

This project is licensed under the [Mozilla Public License 2.0](LICENSE.md).

## 🔗 Links

- [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers)
- [Issue Tracker](https://github.com/NotNotTech/NotNot-MonoRepo/issues)
- [Release Notes](CHANGELOG.md)
- [NuGet Package](https://www.nuget.org/packages/NotNot.Analyzers/)

## 🏆 Why NotNot.Analyzers?

- **Battle-tested** - Used in production applications
- **Modern** - Built with latest .NET analyzer APIs
- **Intelligent** - Context-aware analysis reduces false positives
- **Fast** - Optimized for large codebases
- **Configurable** - Adapt to your team's coding standards
- **Educational** - Detailed error messages help developers learn best practices

---

**Made with ❤️ by the NotNot team**



## Direct ProjectReference Usage

If referencing this project directly (not the nuget package) be sure to add ` OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the `.csproj` reference, like:

```xml
<ProjectReference Include="..\lib\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

