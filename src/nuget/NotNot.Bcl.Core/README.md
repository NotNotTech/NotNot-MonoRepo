# NotNot.Bcl.Core

A pure .NET utility library with no external dependencies beyond the .NET BCL.

## Library Architecture

| Package | Purpose | Dependencies |
|---------|---------|-----------------|
| `NotNot.Bcl.Core` | Pure .NET utilities (Maybe<T>, pooling, diagnostics) | None beyond .NET BCL |
| `NotNot.Bcl` | Modern cross-platform utilities with DI/hosting integration | Microsoft.Extensions.*, optional ASP.NET Core |
| `NotNot.Platform.Desktop` | Desktop OS integration (file explorer, shell commands) | None (System.Diagnostics) |

## When to Use This Package

**Use NotNot.Bcl.Core when:**
- You need minimal dependencies (libraries, console apps, embedded scenarios)
- You cannot reference Microsoft.Extensions.* packages
- You're building a library that must remain framework-agnostic

**Use NotNot.Bcl when:**
- Your app uses modern .NET hosting (IHostBuilder, IServiceCollection)
- You want DI integration, web utilities, or hosting extensions
- Works in: console apps, services, web apps, desktop apps with modern hosting

## Key Features

- **Maybe<T> Pattern** - Functional error handling with structured `Problem` types
- **Pooled Memory Abstractions** - `Mem<T>`, `RefMem<T>`, `SpanGuard<T>` for low-GC allocations
- **OpenGenericMethodExecutor** - Reflection utilities for creating delegates from generic methods
- **Extension Methods** - Prefixed with `_` for easy discovery (e.g., `myList._Shuffle()`)

## Interested?

Raise an issue if you are interested and I will prioritize it sooner.