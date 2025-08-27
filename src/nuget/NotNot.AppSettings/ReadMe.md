# NotNot.AppSettings

Automatically create strongly typed C# settings objects from AppSettings.json. Uses Source Generators.

Includes a simple deserialization helper for when you are using Dependency Injection, or not.

## Table of Contents

- [NotNot.AppSettings](#notnotappsettings)
	- [Table of Contents](#table-of-contents)
	- [Getting Started](#getting-started)
	- [How it works](#how-it-works)
	- [Example](#example)
	- [Troubleshooting / Tips](#troubleshooting--tips)
		- [How to access the `AppSettings` class from external code?](#how-to-access-the-appsettings-class-from-external-code)
		- [How to extend the generated `AppSettings` class?](#how-to-extend-the-generated-appsettings-class)
		- [Some settings not being loaded (value is `NULL`). Or:  My `appSettings.Development.json` file is not loaded](#some-settings-not-being-loaded-value-is-null-or--my-appsettingsdevelopmentjson-file-is-not-loaded)
		- [Intellisense not working for `AppSettings` class](#intellisense-not-working-for-appsettings-class)
		- [Why are some of my nodes typed as `object`?](#why-are-some-of-my-nodes-typed-as-object)
		- [Tip: Backup generated code in your git repository](#tip-backup-generated-code-in-your-git-repository)
	- [Contribute](#contribute)
		- [Local Development (Reference `.csproj`, not Nuget)](#local-development-reference-csproj-not-nuget)
		- [Nuget](#nuget)
	- [Acknowledgments](#acknowledgments)
	- [License: MPL-2.0](#license-mpl-20)
	- [Notable Changes](#notable-changes)



## Getting Started

1) Add an `appsettings.json` file to your project *(make sure it's copied to the output)*.
2) **[Install this nuget package `NotNot.AppSettings`](https://www.nuget.org/packages/NotNot.AppSettings)**.
3) Build your project
4) Use the generated `AppSettings` class in your code. (See the example section below).

## How it works

During your project's build process, NotNot.AppSettings will parse the  `appsettings*.json` in your project's root folder.  These files are all merged into a single schema. Using source-generators it then creates a set of csharp classes that matches each node in the json hierarchy.

After building your project, an `AppSettings` class contains the strongly-typed definitions,
and an `AppSettingsBinder` helper/loader util will be found under the `{YourProjectRootNamespace}.AppSettingsGen` namespace.

## Example

`appsettings.json`

```json
{
  "Hello": {
	"World": "Hello back at you!"
  }
}
```

`Program.cs`

```csharp
using ExampleApp.AppSettingsGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExampleApp;
public class Program
{ 
   public static async Task Main(string[] args)
   {
      {
         Console.WriteLine("NON-DI EXAMPLE");
                  
         var appSettings = ExampleApp.AppSettingsGen.AppSettingsBinder.LoadDirect();
         Console.WriteLine(appSettings.Hello.World);         
      }
      {
         Console.WriteLine("DI EXAMPLE");

         HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
         builder.Services.AddSingleton<IAppSettingsBinder, AppSettingsBinder>();
         var app = builder.Build();
         var appSettings = app.Services.GetRequiredService<IAppSettingsBinder>().AppSettings;
         Console.WriteLine(appSettings.Hello.World);
      }
   }
}
```

*See the **`./NotNot.AppSettings.Example`** folder in the repository for a fully buildable version of this example.*

### New in `v2.0.3`
There's now an IConfiguration extension method to make usage even easier:

```csharp
public class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var appSettings = builder.Configuration._AppSettings();
		Console.WriteLine($"appSettings.AllowedHosts={appSettings.AllowedHosts}");

```

## Troubleshooting / Tips

### How to access the generated classes from external code?

To prevent namespace collisions with other projects, the generated classes are `internal` by default.
If you need to access it from another project, the best solution is to make a wrapper class in the project that uses the generated code.
Alternatively, you can make the generated code `public`:

- v`2.0.0` and later: you can add the following to your `.csproj` file:
```xml
	<PropertyGroup>
		<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>
	</PropertyGroup>
```
- v`1.x` and earlier: The `AppSettings` class is `public` by default.


### How to extend the generated `AppSettings` class?

You can extend any/all of the generated code by creating a partial class in the same namespace.

### Some settings not being loaded (value is `NULL`). Or:  My `appSettings.Development.json` file is not loaded

Ensure the proper environment variable is set.   For example, The `appSettings.Development.json` file is only loaded when the `ASPNETCORE_ENVIRONMENT` 
or `DOTNET_ENVIORNMENT` environment variable is set to `Development`.

### Intellisense not working for `AppSettings` class

A strongly-typed `AppSettings` (and sub-classes) is recreated every time you build your project.
This may confuse your IDE and you might need to restart it to get intellisense working again.

### Why are some of my nodes typed as `object`?

Under some circumstances, the type of a node's value in `appsettings.json` would be ambiguous, so `object` is used:

- If the value is `null` or `undefined`
- If the value is a POJO/Array/primitive in one appsettings file, and a different one of those three in another.


### Tip: Backup generated code in your git repository

Add this to your `.csproj` to have the code output to `./Generated` and have it be ***ignored*** by your project.
This way you can check it into source control and have a backup of the generated code in case you need to stop using this package.
```xml
<!--output the source generator build files-->
<Target Name="DeleteFolder" BeforeTargets="PreBuildEvent">
	<RemoveDir Directories="$(CompilerGeneratedFilesOutputPath)" />
</Target>	
<PropertyGroup>
	<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
	<CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
<ItemGroup>
	<!--Exclude the output of source generators from the compilation-->
	<Compile Remove="$(CompilerGeneratedFilesOutputPath)/**" />
</ItemGroup>
```

## Contribute

- If you find value from this project, consider sponsoring.

### Local Development (Reference `.csproj`, not Nuget)

- Add ` OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the `<ProjectReference/>`
- **IMPORTANT**: When using Project References, you must manually import the targets file to expose MSBuild properties to the source generator:
```xml
<!-- Import NotNot.AppSettings targets to expose MSBuild properties to source generator -->
<Import Project="path/to/NotNot.AppSettings.targets" />
```
Without this import, properties like `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` will be ignored.
- beware when attempting to update nuget packages, it will likely break the Source Generator.  Default to just leaving them as is, unless you want to spend time troubleshooting sourcegen thrown exceptions.

### Nuget

- current version is set via `MinVer`, which matches the repo git tags.
- read the repo's `Contrib/` folder for more info.


## Acknowledgments

- This project was inspired by https://github.com/FrodeHus/AppSettingsSourceGenerator which unfortunately did not match my needs in fundamental ways.

## License: MPL-2.0

A summary from [TldrLegal](https://www.tldrlegal.com/license/mozilla-public-license-2-0-mpl-2):

>   MPL is a copyleft license that is easy to comply with. You must make the source code for any of your changes available under MPL, but you can combine the MPL software with proprietary code, as long as you keep the MPL code in separate files. Version 2.0 is, by default, compatible with LGPL and GPL version 2 or greater. You can distribute binaries under a proprietary license, as long as you make the source available under MPL.

**In brief**: You can basically use this project however you want, but all changes to it must be open sourced.

## Notable Changes

- **`2.0.3`** :
	- New IConfiguration extension method to make usage easier
- **`2.0.2`** :
  - Generated code files now use the `.g.cs` suffix (a7dc012)
  - Added an extension method for faster Dependency Injection acquisition (7fd7f1f)
  - Improved Linux compatibility for finding `appsettings.json` files (a29270f)
- **`2.0.0`** : generated code is now `internal` by default.  Make it public by adding `<NotNot_AppSettings_GenPublic>true</NotNot_AppSettings_GenPublic>` to your `.csproj`
- **`1.2.1`** : improve doc for missing appSettings.json, handle projects with blank default namespace. move to new repository.
- **`1.1.1`** : make the nuget package `<PrivateAsset>` so only the project that directly references it uses it. 
  - (needed for example: test projects)
- **`1.0.0`** : polish and readme tweaks.  **Put a fork in it, it's done!**
- **`0.12.0`** : change appsettings read logic to use "AdditionalFiles" workflow instead of File.IO
- **`0.10.0`** : Initial Release.
