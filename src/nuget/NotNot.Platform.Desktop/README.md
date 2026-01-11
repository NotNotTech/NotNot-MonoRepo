# NotNot.Platform.Desktop

Desktop platform utilities for file system operations, process launching, and OS integration.

## Features

- **FileSystemHelper** - Cross-platform file system operations
  - `RevealInOsGui()` - Opens file explorer and highlights a specific file

## Platform Support

| Platform | RevealInOsGui |
|----------|---------------|
| Windows  | `explorer.exe /select` |
| macOS    | `open -R` |
| Linux    | `xdg-open` (parent folder only) |

## Usage

```csharp
using NotNot.Platform.Desktop;

// Open file explorer and highlight the file
FileSystemHelper.RevealInOsGui("/path/to/file.txt");
```

## Design Philosophy

This library provides **desktop-specific** utilities that don't fit in cross-platform pure .NET libraries like `NotNot.Bcl.Core`.

Key principles:
- **Platform detection**: Uses `OperatingSystem.Is*()` APIs
- **Graceful degradation**: Silent no-op on unsupported platforms
- **Best effort**: File explorer operations don't throw on failure
