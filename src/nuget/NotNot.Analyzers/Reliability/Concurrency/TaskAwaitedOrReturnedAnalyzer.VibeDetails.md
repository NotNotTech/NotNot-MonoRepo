# TaskAwaitedOrReturnedAnalyzer.VibeDetails.md
%DATE% 2025-06-03

## Purpose
Roslyn Analyzer that detects Task/ValueTask variables that are declared but not properly consumed. Prevents fire-and-forget task anti-patterns that can cause silent failures and resource leaks.

## Technical Implementation
- **Analyzer ID**: NN_R001
- **Severity**: Error (DiagnosticSeverity.Error)
- **Category**: NotNot_Reliability_Concurrency
- **Target Framework**: netstandard2.0

## Detected Task Types
- `Task` and `Task<T>`
- `ValueTask` and `ValueTask<T>`
- `ConfiguredTaskAwaitable` and `ConfiguredTaskAwaitable<T>`
- `ConfiguredValueTaskAwaitable` and `ConfiguredValueTaskAwaitable<T>`

## Analysis Scope
- Method declarations (including lambda expressions in member initializers)
- Field and property declarations with lambda initializers
- Traverses syntax tree to find variable declarators with task-like types

## Valid Task Usage Patterns (No Warning)
1. **Awaited**: `await myTask`
2. **Returned**: `return myTask`
3. **Assigned to other variable**: `var otherTask = myTask`
4. **Passed to method**: `SomeMethod(myTask)`
5. **Member accessed**: `myTask.ConfigureAwait(false)` or any member access
6. **Used in lambda expressions**: Task referenced within lambda body

## Analysis Methods
- `AnalyzeMethodDeclaration`: Processes method syntax nodes
- `AnalyzeMemberDeclaration`: Handles field/property declarations with lambda initializers
- `AnalyzeMethodWorker`: Core analysis logic for any syntax node
- `InspectTaskVariables`: Validates each detected task variable
- `IsAwaited`, `IsReturned`, `IsAssignedToOtherVariable`, `IsPassedToMethod`, `IsMemberAccessed`: Validation checks

## Code Structure
- Uses Roslyn's `DiagnosticAnalyzer` base class
- Registers for `SyntaxKind.MethodDeclaration`, `SyntaxKind.FieldDeclaration`, `SyntaxKind.PropertyDeclaration`
- Semantic model analysis to determine variable types
- Syntax tree traversal to detect usage patterns

## Error Message
"Task '{variableName}' is not awaited or used. Fire and forget tasks are likely an error. If this is intentional, assign to the '_' discard variable instead."

## Integration
- Part of NotNot.Analyzers NuGet package
- Automatically packaged with other reliability analyzers
- Enabled by default for consuming projects
