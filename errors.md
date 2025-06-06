# Build Status

The project was built successfully.

The following fixes were applied during the build process:

1. Installed .NET SDK 9.
2. Fixed CS1929 errors in `src/nuget/NotNot.Bcl.Core/NotNot/DisposeGuard.cs` by changing `line.Split(" in ")._Reverse()._ToList()` to `line.Split(" in ").Reverse().ToList()`.
3. Fixed CS0234 error in `src/example/NotNot.Bcl.Example.HelloConsole/_globalUsings.cs` by changing `global using static NotNot.NotNotLoLo;` to `global using static NotNot.Logging;`.
4. Fixed subsequent CS7007 error in `src/example/NotNot.Bcl.Example.HelloConsole/_globalUsings.cs` by removing the line `global using static NotNot.Logging;`.
5. Fixed CS0103 errors (`The name '__' does not exist in the current context`) in `src/example/NotNot.Bcl.Example.HelloConsole/Program.cs` by fully qualifying usages of `__` to `NotNot.LoLoRoot.__` and adding a ProjectReference from `NotNot.Bcl.Example.HelloConsole.csproj` to `NotNot.Bcl.Core.csproj`.
