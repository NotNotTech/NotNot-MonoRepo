# NotNot-MonoRepo

This is a monoRepo of free open source projects licensed publicly under the `MPL-2.0`

## Important links

### Nuget Packages
- Source code for nuget packages, such as `NotNot.Core` can be found under [./src/nuget](./src/nuget)
  - readme's for each nuget package can be found in their respective folders

### Examples

- see [./src/example/](./src/example/) for listing of example projects

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