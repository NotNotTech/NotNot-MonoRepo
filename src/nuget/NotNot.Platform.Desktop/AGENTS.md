# VIBEGUIDE

## Desktop Platform Library Philosophy

- Platform-specific utilities for desktop OS integration
- Windows, macOS, and Linux support where possible
- Best-effort operations that fail silently (UX convenience, not critical)
- Separate from NotNot.Bcl.Core to maintain cross-platform purity

## Architectural Boundaries

- **NO ASP.NET Core dependencies** - This is pure .NET
- **NO UI framework dependencies** - Works with any desktop app framework
- **Platform detection only** - Use `OperatingSystem.Is*()` APIs
- **Best effort semantics** - Operations that launch external processes should not throw

## Design Principles

### Silent Failure Pattern

Desktop integration operations (file explorer, browser launch) should:
1. Validate inputs (paths exist, non-empty)
2. Execute platform command in try/catch
3. Swallow exceptions - these are UX conveniences, not critical operations
4. Never disrupt application flow due to shell command failure

### Platform Detection

Use modern .NET APIs:
```csharp
if (OperatingSystem.IsWindows()) { ... }
if (OperatingSystem.IsMacOS()) { ... }
if (OperatingSystem.IsLinux()) { ... }
```

### Process.Start Patterns

**Windows**: Use `UseShellExecute = true` for shell commands
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"/select,\"{filePath}\"",
    UseShellExecute = true
});
```

**macOS/Linux**: Use `UseShellExecute = false` with `ArgumentList`
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "open",
    ArgumentList = { "-R", filePath },
    UseShellExecute = false
});
```

## Current Features

| Class | Method | Purpose |
|-------|--------|---------|
| `FileSystemHelper` | `RevealInOsGui()` | Open file explorer and highlight file |

## Future Expansion

Potential additions following the same patterns:
- `OpenInDefaultApp()` - Open file with associated application
- `OpenUrl()` - Open URL in default browser
- `LaunchTerminal()` - Open terminal at path

# VIBECACHE

**LastCommitHash**: N/A (new package)
**Timestamp**: 2026-01-11

## Primary Resources

- [OperatingSystem.Is* APIs](https://learn.microsoft.com/en-us/dotnet/api/system.operatingsystem)
- [ProcessStartInfo](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo)

## Related Topics

- `../NotNot.Bcl.Core/AGENTS.md` - Pure .NET utilities (no external dependencies)
- `../NotNot.Bcl/AGENTS.md` - Modern cross-platform utilities with Microsoft.Extensions.* and optional ASP.NET Core support

## Platform-Specific Notes

### Windows

- `explorer.exe /select,"path"` - Opens folder with file selected
- Requires `UseShellExecute = true`
- Paths with spaces need quoting

### macOS

- `open -R "path"` - Reveal in Finder
- `open "path"` - Open with default app
- Uses ArgumentList for proper escaping

### Linux

- `xdg-open "path"` - Open with default handler
- **Limitation**: Cannot select specific file in most file managers
- Different behavior across Nautilus, Dolphin, Thunar, etc.
