# VIBEGUIDE

## Core Library Philosophy
- Pure .NET library with NO web framework dependencies
- Foundation for Maybe<T> pattern and core utilities
- Must remain framework-agnostic for broad compatibility
- Used by both web and non-web applications

## Critical Architectural Boundaries
- **NO ASP.NET Core dependencies** - This is NotNot.Bcl's responsibility
- **NO MVC/Web API references** - Keep this pure .NET
- **NO IResult or HttpContext usage** - Web concerns belong in NotNot.Bcl
- Type detection by name/string matching only when needed for web types

## Important usage patterns

### Maybe<T> Pattern
- `Maybe.cs` - Core Maybe monad implementation
- `MaybeJsonConverter` - JSON serialization support
- `Problem.cs` - Structured error representation
- Detection of IResult types by name (not reference) to avoid ASP.NET dependency

### Pooled Arrays/Spans Pattern: Mem<T> and SpanGuard<T>
- `Mem.cs` - Pooled array/span allocation cache.
- use `Mem<T>` for high-performance scenarios where an array would commonly be used
   - very low GC pressure: just a small tracking object.  (array is pooled))
   - has fast reliable Span<T> access and conversion to/from Read-only mode (`ReadMem<T>`)
- use `SpanGuard<T>` for "stackalloc"" scenarios where a large allocation is needed.
   - no GC pressure: the array is pooled and reused.
   - useful when you need an array for the lifetime of a method.  useful because the allocation is automatically freed when the method exits. 


### Serialization
- `SerializationHelper.cs` - JSON configuration
- Custom converters for Maybe<T> types
- Framework-agnostic serialization patterns

### Extensions
- Pure .NET extension methods only.   
- conventions:
	- follow same `zz_Extensions_{type}` naming convention for new extensions.  
   - always prefix our extension methods with `_` to visibly signal this is our extension method.
- No web-specific extensions (those go in NotNot.Bcl)
- Focus on general-purpose utilities

# VIBECACHE

**LastCommitHash**: Unknown
**Timestamp**: 2025-08-07 03:30:00

## Primary Resources
- [System.Text.Json Documentation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json)
- [.NET Base Class Library](https://learn.microsoft.com/en-us/dotnet/api/)

## Related Topics
- [../NotNot.Bcl/CLAUDE.md](../NotNot.Bcl/CLAUDE.md) - Web framework extensions

## Dependency Rules
1. **Allowed**: System.* namespaces, pure .NET libraries
2. **Forbidden**: Microsoft.AspNetCore.*, Microsoft.Extensions.* (web-specific)
3. **Exception**: Can detect web types by fully-qualified name strings

## IResult Handling Strategy
When Maybe<T> encounters IResult types during deserialization:
- Detect by type name: `Microsoft.AspNetCore.Http.HttpResults.*`
- Throw clear exception guiding to use Maybe or Maybe<OperationResult>
- Cannot reference IResult directly - only string-based detection

## Why This Separation Matters
- Allows NotNot.Bcl.Core to be used in:
  - Console applications
  - Desktop applications (WPF, WinForms)
  - Background services
  - Libraries that don't need web dependencies
- Keeps package size minimal
- Avoids forcing web dependencies on non-web projects