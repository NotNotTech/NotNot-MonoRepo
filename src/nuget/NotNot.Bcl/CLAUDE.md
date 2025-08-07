# VIBEGUIDE

## Web Extensions Library Philosophy
- ASP.NET Core extensions for NotNot.Bcl.Core
- Provides web-specific functionality on top of Core
- Bridge between pure .NET patterns and web frameworks
- This is where ALL ASP.NET Core dependencies belong

## Critical Architectural Boundaries
- **ASP.NET Core dependencies GO HERE** - Not in Core
- **IResult conversions and extensions** - Web-specific patterns
- **HttpContext utilities** - Web request/response handling
- Depends on and extends NotNot.Bcl.Core functionality

# VIBECACHE

**LastCommitHash**: Unknown
**Timestamp**: 2025-08-07 03:30:00

## Primary Resources
- [ASP.NET Core Documentation](https://learn.microsoft.com/en-us/aspnet/core/)
- [Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)

## Related Topics
- [../NotNot.Bcl.Core/CLAUDE.md](../NotNot.Bcl.Core/CLAUDE.md) - Core library without web dependencies

## Web-Specific Components

### ASP.NET Core Extensions
- IResult extension methods (if implemented)
- HttpContext utilities
- Web-specific Maybe<T> helpers
- MVC/API integration points

### Current Extensions (Planned/Possible)
- `IResultExtensions.cs` - Convert IResult to/from Maybe pattern
- Web-specific serialization helpers
- HTTP status code mappings
- Problem details integration

## Dependency Rules
1. **Required**: Reference to NotNot.Bcl.Core
2. **Allowed**: Microsoft.AspNetCore.*, Microsoft.Extensions.* (web)
3. **Purpose**: All web framework dependencies centralized here

## Framework Reference
```xml
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```
This gives access to all ASP.NET Core types without individual package references.

## IResult Extension Pattern
Since IResult types have sealed constructors and can't be deserialized:
```csharp
// Extensions to convert Maybe to IResult (web concern)
public static IResult ToIResult(this Maybe maybe) { ... }
public static IResult ToIResult<T>(this Maybe<T> maybe) { ... }
```

## Why This Separation Exists

### NotNot.Bcl.Core (Pure .NET)
- Used by: Console apps, services, libraries
- Dependencies: None (pure .NET)
- Focus: Core patterns like Maybe<T>

### NotNot.Bcl (Web Extensions)
- Used by: ASP.NET Core applications
- Dependencies: ASP.NET Core framework
- Focus: Web-specific extensions and utilities

This separation ensures:
- Non-web projects don't pull in unnecessary web dependencies
- Web projects get full ASP.NET Core integration
- Clear architectural boundaries
- Minimal package sizes for each use case

## Migration Guide
When adding new functionality, ask:
1. Does it require ASP.NET Core types? → Goes in NotNot.Bcl
2. Is it pure .NET logic? → Goes in NotNot.Bcl.Core
3. Does it bridge web and core patterns? → Goes in NotNot.Bcl

## Known Limitations
- IResult types cannot be deserialized due to sealed constructors
- Solution: Return IResult directly from endpoints, not Maybe<IResult>
- Use extension methods in application code (not in this library currently)