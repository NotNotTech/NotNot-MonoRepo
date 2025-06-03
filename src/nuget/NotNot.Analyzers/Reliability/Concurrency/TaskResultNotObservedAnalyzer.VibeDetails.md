# TaskResultNotObservedAnalyzer.VibeDetails.md
%DATE% 2025-06-03

## CURRENT UNDERSTANDING SUMMARY
This Roslyn analyzer detects Task<T> and ValueTask<T> operations that are awaited but whose return values are not used. It prevents wasteful async patterns where results are unnecessarily discarded.

## Purpose
Roslyn Analyzer that detects when Task<T> or ValueTask<T> results are awaited but their return values are not observed. Prevents wasteful patterns where async operations return values that are immediately discarded.

## Technical Implementation
- **Analyzer ID**: NN_R002
- **Severity**: Error (DiagnosticSeverity.Error)
- **Category**: NotNot_Reliability_Concurrency
- **Target Framework**: netstandard2.0
- **Namespace**: NotNot.Analyzers.Reliability.Concurrency

## Code Structure Analysis
The analyzer consists of these key components:

### Main Analysis Entry Point
- `AnalyzeAwaitExpression`: Triggered by SyntaxKind.AwaitExpression registration
- Uses semantic model to determine if awaited expression is Task<T>/ValueTask<T>
- Calls IsAwaitResultObserved to check if result is properly consumed

### Type Detection
- `IsGenericTaskType`: Identifies Task<T> and ValueTask<T> types using OriginalDefinition comparison
- Uses typeof(Task<>).FullName and typeof(ValueTask<>).FullName for exact matching
- Only triggers on generic task types with return values

### Result Observation Analysis
- `IsAwaitResultObserved`: Main logic determining if await result is consumed
- Checks parent syntax nodes for various usage patterns
- `IsPartOfLargerExpression`: Recursive parent node analysis for complex expressions

### Error Message Generation
- `GetMethodNameFromAwaitExpression`: Extracts method name for diagnostic messages
- `ExtractMethodName`: Helper for invocation expression method name extraction
- Provides context-specific error descriptions

## Detected Task Types
- `Task<T>` - Generic Task with return value
- `ValueTask<T>` - Generic ValueTask with return value
- Excludes non-generic Task and ValueTask (no return value)

## Valid Patterns (No Warning)
1. **Assignment**: `var result = await DoSomethingAsync();`
2. **Return statement**: `return await DoSomethingAsync();`
3. **Method argument**: `SomeMethod(await DoSomethingAsync());`
4. **Binary expression**: `if (await DoSomethingAsync() == value)`
5. **Member access**: `(await DoSomethingAsync()).ToString()`
6. **Method invocation**: `(await DoSomethingAsync()).SomeMethod()`
7. **Element access**: `(await DoSomethingAsync())[index]`
8. **Conditional access**: `(await DoSomethingAsync())?.Property`
9. **Cast expression**: `(SomeType)(await DoSomethingAsync())`
10. **Using statement**: `using (await DoSomethingAsync())`
11. **Part of larger expression**: Any complex expression that uses the result

## Invalid Patterns (Triggers Error)
1. **Expression statement**: `await DoSomethingAsync();` where `DoSomethingAsync()` returns `Task<T>`
2. **Standalone await**: Any await expression that becomes an ExpressionStatementSyntax with no further usage

## Error Message Format
"Task<T> '{methodName}' is awaited but its result is not observed. Consider using the return value or change method to return Task instead of Task<T>."

## Integration with NotNot.Analyzers Project
- Part of NotNot.Analyzers NuGet package targeting netstandard2.0
- Uses Microsoft.CodeAnalysis.CSharp 4.14.0 for Roslyn analyzer framework
- Configured as analyzer package with proper MSBuild integration
- Works alongside TaskAwaitedOrReturnedAnalyzer (NN_R001) for comprehensive async pattern analysis

## Related Files
- `TaskAwaitedOrReturnedAnalyzer.cs`: Sibling analyzer for detecting fire-and-forget Task patterns
- `NotNot.Analyzers.csproj`: Project configuration with analyzer packaging
- Works as part of the broader NotNot reliability analyzer suite

## Analysis Scope and Performance
- Registers only for AwaitExpressionSyntax nodes for efficiency
- Uses semantic model analysis to determine precise type information
- Concurrent execution enabled for performance
- Generated code analysis disabled to focus on user code

## Integration Status
Currently integrated into the NotNot.Analyzers project and builds successfully. The analyzer follows standard Roslyn analyzer patterns and integrates with the existing build pipeline.
