# VIBEGUIDE

## Modern Cross-Platform Library Philosophy
- Extensions for NotNot.Bcl.Core in modern .NET environments
- Can reference (not necessarily use) Microsoft.Extensions.* and ASP.NET Core helpers
- Cross-platform: works in console apps, services, web apps, desktop apps
- Centralizes dependencies that require modern hosting/DI infrastructure

## Critical Architectural Boundaries
- **Microsoft.Extensions.* dependencies GO HERE** - Not in Core
- **ASP.NET Core utilities** - Optional web-specific patterns (IResult, HttpContext)
- **Modern DI/Hosting patterns** - IHostBuilder, IServiceCollection extensions
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
- Used by: Any .NET application, libraries, minimal environments
- Dependencies: None beyond .NET BCL
- Focus: Core patterns like Maybe<T>, pooling, diagnostics

### NotNot.Bcl (Modern Cross-Platform)
- Used by: Console apps, services, web apps, desktop apps with modern .NET hosting
- Dependencies: Microsoft.Extensions.*, optional ASP.NET Core framework reference
- Focus: DI integration, hosting extensions, web utilities when needed

This separation ensures:
- Minimal-dependency projects can use Core alone
- Modern apps get full Microsoft.Extensions.* integration
- Web projects get optional ASP.NET Core utilities
- Clear architectural boundaries
- Appropriate package sizes for each use case

## Migration Guide
When adding new functionality, ask:
1. Does it require Microsoft.Extensions.* or ASP.NET Core types? → Goes in NotNot.Bcl
2. Is it pure .NET logic with no external dependencies? → Goes in NotNot.Bcl.Core
3. Does it require desktop OS integration (Process.Start, platform detection)? → Goes in NotNot.Platform.Desktop
4. Does it bridge hosting/DI with core patterns? → Goes in NotNot.Bcl

## Known Limitations
- IResult types cannot be deserialized due to sealed constructors
- Solution: Return IResult directly from endpoints, not Maybe<IResult>
- Use extension methods in application code (not in this library currently)
