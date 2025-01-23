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
		<!--see https://github.com/adamralph/minver?tab=readme-ov-file#can-i-version-multiple-projects-in-a-single-repository-independently -->
		<MinVerTagPrefix>$(AssemblyName)-</MinVerTagPrefix>
		<PackageProjectUrl>https://github.com/jasonswearingen/NotNot/src/nuget/$(AssemblyName)/</PackageProjectUrl>
		<Copyright>$(Authors)</Copyright>
		<RepositoryUrl>https://github.com/jasonswearingen/NotNot</RepositoryUrl>
		<PackageIcon>[!!]-logos_red_cropped.png</PackageIcon>
		<PackageReadmeFile>README.md</PackageReadmeFile>
		<PackageTags>NotNot; Novaleaf;</PackageTags>
		<PackageLicenseExpression>MPL-2.0</PackageLicenseExpression>
		<OutputType>Library</OutputType>
		<PackageOutputPath>$(SolutionDir).nuget-test-packages\$(AssemblyName)</PackageOutputPath>
		<Authors>Novaleaf</Authors>
		<PackageDescription>see https://github.com/jasonswearingen/NotNot/src/nuget/$(AssemblyName)/ or the README</PackageDescription>
	</PropertyGroup>
	```

