# NotNot-MonoRepo

This is a monoRepo of open source C# Nuget packages.  
  

## Nuget Packages (you can use)

### [`NotNot.AppSettings`](./src/nuget/NotNot.AppSettings/)  
- **Get it on Nuget: https://www.nuget.org/packages/NotNot.AppSettings**
- Strongly-Typed `appSettings.json` via source-generator
- A high quality example of how to write a production-capable source-generator (includes inline docs)
  
_This project was formerly found at https://github.com/jasonswearingen/NotNot.AppSettings_


## Example Projects
- see [./src/example/](./src/example/) for listing of example projects using the Nuget Packages

## Other Projects
_These projects are not in Nuget yet, and likely not ready for public use_

### [`NotNot.Bcl`](./src/nuget/NotNot.Bcl/)
- Base Class Library  ***Kitchen Sink Included***
- includes a huge amount of extension methods usable in any project
- focuses on supporting projects with support for `Microsoft.Extensions.DependencyInjection`
  - if you don't use DI, most the library features are also found in `NotNot.Bcl.Core`

###  [`NotNot.Bcl.Core`]
- functionality of `NotNot.Bcl` that doesn't require platform specific features (ex: no DI)

###  [`NotNot.Analyzers`]
- A couple code analazyers and some examples for making more.


###  [`NotNot.GodotNet.SourceGen`]
- Source Generators for Godot Engine specific functionality.
- For use with the `NotNot.GodotNet` library, which is currently private because Godot doesn't properly support libraries (lib code needs to be in the same project as the propritary game)
- TODO:
  - Needs a mirrored copy workflow setup to be able to checkin the libcode

## Why MonoRepo?

MonoRepo because:
- easier to maintain/debug multiple Nugets when they are part of the same solution (build breaks are noticeable)
- can share the same build workflows
  - see `src/CommonSettings.targets`
  - also see `Debug` vs `LocalProjectsDebug` Build Configurations for how to easily switch between nuget dev/release builds

### MonoRepo weirdness

Because this repo is focused on the development of multiple Nuget projects, there are a few weird "quality of life" tweaks to aid development:
- `MinVer` used for nuget versioning ()
-`LocalProjectsDebug`: used to run examples referencing the local library project source code instead of their nuget packages (useful for debugging and development)
  - This requires modifying `.csproj` for nuget references

see `./contrib/creating-nuget-packages.md` for some more details on the above.

## License: MPL-2.0

A summary from [TldrLegal](https://www.tldrlegal.com/license/mozilla-public-license-2-0-mpl-2):

>   MPL is a copyleft license that is easy to comply with. You must make the source code for any of your changes available under MPL, but you can combine the MPL software with proprietary code, as long as you keep the MPL code in separate files. Version 2.0 is, by default, compatible with LGPL and GPL version 2 or greater. You can distribute binaries under a proprietary license, as long as you make the source available under MPL.

See the [./LICENSE](./LICENSE) file for the license full text.
