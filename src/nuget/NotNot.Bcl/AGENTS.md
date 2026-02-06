# VIBEGUIDE

## Modern Cross-Platform Library Philosophy
- Extensions for NotNot.Bcl.Core requiring ASP.NET Core framework reference
- Cross-platform: works in console apps, services, web apps, desktop apps
- Hosts SlimGraph and types requiring ASP.NET Core

## Critical Architectural Boundaries
- **ASP.NET Core framework reference** - This package has `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
- **ASP.NET Core utilities** - IResult, HttpContext patterns
- Depends on and extends NotNot.Bcl.Core functionality
- **Note**: Microsoft.Extensions.Hosting/DI/Configuration are now in Bcl.Core (not here)

## Moved Types (now in NotNot.Bcl.Core)
The following types have been migrated to NotNot.Bcl.Core for broader reuse:
- **DI Infrastructure**: `MsDIContainer`, DI service markers, auto-registration extensions
- **AppSettingsHelper**: `SettingsManager<T>`, `SettingsProxy<T>`, `JsonSettingsUtils`, storage providers
- **Diagnostic Attributes**: `RequireMaybeReturnAttribute`, `MaybeReturnNotRequiredAttribute`

All types remain available transitively since Bcl references Bcl.Core.

## Retained Types (NOT moved)
- **Mixins/Tags**: `Tags`, `ITags` — Must stay here because `[Inline<Tags>]` source generator requires same-compilation source (see `CrossAssemblyLimitationTests`)
- **SlimGraph**: Full node hierarchy framework — deferred from migration

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
2. **Allowed**: Microsoft.AspNetCore.* via framework reference
3. **Purpose**: ASP.NET Core web framework types centralized here
4. **Note**: Microsoft.Extensions.Hosting/DI/Configuration now live in Bcl.Core

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

### NotNot.Bcl.Core (Lightweight, no ASP.NET Core)
- Used by: Any .NET application, libraries, minimal environments
- Dependencies: Microsoft.Extensions.Hosting/DI/Configuration (no ASP.NET Core)
- Focus: Core patterns (Maybe<T>, pooling, diagnostics), DI infrastructure, AppSettingsHelper, Mixins

### NotNot.Bcl (ASP.NET Core capable)
- Used by: Apps that need ASP.NET Core framework reference
- Dependencies: ASP.NET Core framework reference + Bcl.Core
- Focus: SlimGraph, web utilities, IResult extensions

This separation ensures:
- Non-web projects use Core alone (no ASP.NET Core overhead)
- Web projects get ASP.NET Core integration via NotNot.Bcl
- Clear boundary: Core = no web framework, Bcl = web-capable

## Migration Guide
When adding new functionality, ask:
1. Does it require ASP.NET Core types (IResult, HttpContext)? → Goes in NotNot.Bcl
2. Does it only need Microsoft.Extensions.Hosting/DI/Configuration? → Goes in NotNot.Bcl.Core
3. Is it pure .NET logic with no external dependencies? → Goes in NotNot.Bcl.Core
4. Does it require desktop OS integration (Process.Start, platform detection)? → Goes in NotNot.Platform.Desktop

## Known Limitations
- IResult types cannot be deserialized due to sealed constructors
- Solution: Return IResult directly from endpoints, not Maybe<IResult>
- Use extension methods in application code (not in this library currently)
