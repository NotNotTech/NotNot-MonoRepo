# NotNot.Bcl

A "kitchen sink" utility library for modern cross-platform .NET development.

This is used by other NotNot libraries and Jason's private projects.
It is high quality and can be used independently, but doesn't have any documentation yet.

## Library Architecture

| Package | Purpose | Dependencies |
|---------|---------|--------------|
| `NotNot.Bcl.Core` | Pure .NET utilities (Maybe<T>, pooling, diagnostics) | None beyond .NET BCL |
| `NotNot.Bcl` | Modern cross-platform utilities with DI/hosting integration | Microsoft.Extensions.*, optional ASP.NET Core |
| `NotNot.Platform.Desktop` | Desktop OS integration (file explorer, shell commands) | None (System.Diagnostics) |

## NotNot.Bcl vs NotNot.Bcl.Core

`NotNot.Bcl` extends `NotNot.Bcl.Core` for modern .NET environments that can reference `Microsoft.Extensions.*` and optionally ASP.NET Core helper libraries.

**Use Bcl.Core when:**
- You need minimal dependencies (libraries, console apps, embedded scenarios)
- You cannot reference Microsoft.Extensions.* packages

**Use Bcl when:**
- Your app uses modern .NET hosting (IHostBuilder, IServiceCollection)
- You want DI integration, web utilities, or hosting extensions
- Works in: console apps, services, web apps, desktop apps with modern hosting

## Interested?

Raise an issue if you are interested and I will prioritize it sooner.
