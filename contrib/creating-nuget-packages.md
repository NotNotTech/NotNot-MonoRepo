# TOC
- [TOC](#toc)
- [steps to convert a .csproj to a nuget package](#steps-to-convert-a-csproj-to-a-nuget-package)
- [using a locally dev'd nuget package in another project](#using-a-locally-devd-nuget-package-in-another-project)
- [nuget package versioning](#nuget-package-versioning)


# steps to convert a .csproj to a nuget package


- add nuget reference to `MinVer`
  - afterward, the csproj should have something like this:
	```xml
			<PackageReference Include="MinVer" Version="6.0.0">
				<PrivateAssets>all</PrivateAssets>
				<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
			</PackageReference>
	```
- in VSCode or other text editor, open the .csproj
  - add/customize this at the bottom, before `</Project>`
	```xml
		<!-- standard nuget package details -->
	<ItemGroup>
		<None Include="..\..\..\meta\logos\[!!]-logos_red_cropped.png">
			<Pack>True</Pack>
			<PackagePath>\</PackagePath>
		</None>
		<None Include=".\README.md">
			<Pack>True</Pack>
			<PackagePath>\</PackagePath>
		</None>
	</ItemGroup>
	<!-- standard nuget package details -->
	<PropertyGroup>
		<GeneratePackageOnBuild>True</GeneratePackageOnBuild>
	<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<!--see https://github.com/adamralph/minver?tab=readme-ov-file#can-i-version-multiple-projects-in-a-single-repository-independently -->
		<MinVerTagPrefix>$(AssemblyName)-</MinVerTagPrefix>	
		<PackageLicenseExpression>MPL-2.0</PackageLicenseExpression>
		<OutputType>Library</OutputType>

			<!--for other macro properties, see https://learn.microsoft.com/en-us/visualstudio/msbuild/common-msbuild-project-properties?view=vs-2022 -->
	<PackageOutputPath>$(SolutionDir)..\..\..\..\..\..\..\..\.nuget-test-packages\$(AssemblyName)</PackageOutputPath>
		<Authors>Novaleaf</Authors>
		<Copyright>$(Authors)</Copyright>
		<PackageProjectUrl>https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/$(AssemblyName)/</PackageProjectUrl>
		<RepositoryUrl>https://github.com/NotNotTech/NotNot-MonoRepo</RepositoryUrl>
		<PackageIcon>[!!]-logos_red_cropped.png</PackageIcon>
		<PackageReadmeFile>README.md</PackageReadmeFile>
		<PackageTags>NotNot; Novaleaf;</PackageTags>
		<Description>$(AssemblyName) see Project URL or the README</Description>
	 <!--if minVer runs properly, the following Version gets replaced.  if it doesnt, restart visual studio and try again.-->
	<Version>0.0.0-0.projectReferenceNotNuget</Version>
	</PropertyGroup>
	```

# using a locally dev'd nuget package in another project

if you don't need to edit the nuget, you can just add it normally via nuget.org.

If you need to debug/edit the nuget:
- From your consumer project, reference the nuget normally,
- then change the nuget reference wildcards, 
- and reference the nuget's local project (Project Reference), and change it to Conditional
- here's an example of what it should look like:
	```xml
	<Choose>
		<!--only use project references when in LocalProjectsDebug, otherwise use the nuget package references-->
		<When Condition="'$(Configuration)'=='LocalProjectsDebug'">
			<ItemGroup>
				<ProjectReference Include="..\..\nuget\NotNot.Bcl\NotNot.Bcl.csproj" />
			</ItemGroup>
		</When>
		<Otherwise>
			<ItemGroup>
				<PackageReference Include="NotNot.Bcl" Version="*" />
			</ItemGroup>
		</Otherwise>
	</Choose>
	```
	this will cause the projectReference to be used in `LocalProjectsDebug` builds, and the normal nuget package otherwise.

# nuget package versioning
 - `MinVer` is used for nuget versioning. The above xml, pasted into your `.csproj`, will make so when you Tag a git commit with, for example `NotNot.Core-1.23.4`, then next build of `NotNot.Core` will use that version.
 - 