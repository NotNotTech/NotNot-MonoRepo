# TOC
- [TOC](#toc)
- [steps to convert a .csproj to a nuget package](#steps-to-convert-a-csproj-to-a-nuget-package)
- [using a locally dev'd nuget package in another project](#using-a-locally-devd-nuget-package-in-another-project)


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

	<PackageOutputPath>$(SolutionDir)..\..\..\..\..\..\..\..\.nuget-test-packages\$(AssemblyName)</PackageOutputPath>
		<Authors>Novaleaf</Authors>
		<Copyright>$(Authors)</Copyright>
		<PackageProjectUrl>https://github.com/NotNotTech/NotNot-MonoRepo/src/nuget/$(AssemblyName)/</PackageProjectUrl>
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
- From your consumer project, reference the nuget normally
- then reference the nuget's local project (Project Reference)
- then open your consumer .csproj, and change it to Conditional, such as shown:
	```xml
		<!--only use project references when in DEBUG, otherwise use the nuget package references--> 
			<ItemGroup Condition="'$(Configuration)' == 'Debug'">
				<ProjectReference Include="..\..\nuget\NotNot.Core\NotNot.Core.csproj" />
			</ItemGroup>
	```
	this will cause the projectReference to be used in DEBUG builds, and the normal nuget package otherwise.