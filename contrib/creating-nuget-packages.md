# steps to convert a .csproj to a nuget package

- open the .csproj in VS
- go to "Package" section, click "Produce a package file during build operations"
- close the .csproj settings
- add nuget reference to `MinVer`
  - afterward, the csproj should have something like this:
	```xml
			<PackageReference Include="MinVer" Version="6.0.0">
				<PrivateAssets>all</PrivateAssets>
				<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
			</PackageReference>
	```
- in VSCode or other text editor, open the .csproj
  - add this at the top under standard dotnet stuff, before `</PropertyGroup>`
	```xml
			<!--see https://github.com/adamralph/minver?tab=readme-ov-file#can-i-version-multiple-projects-in-a-single-repository-independently -->
			<MinVerTagPrefix>NotNot.Core-</MinVerTagPrefix>
	```
	- 
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

		<PropertyGroup>
			<PackageProjectUrl>https://github.com/jasonswearingen/NotNot/src/nuget/NotNot.Core/</PackageProjectUrl>
			<Copyright>Jason Swearingen</Copyright>
			<RepositoryUrl>https://github.com/jasonswearingen/NotNot</RepositoryUrl>
			<PackageIcon>[!!]-logos_red_cropped.png</PackageIcon>
			<PackageReadmeFile>README.md</PackageReadmeFile>
			<PackageTags>NotNot; Corelib; Novaleaf;</PackageTags>
			<PackageLicenseExpression>MPL-2.0</PackageLicenseExpression>
			<OutputType>Library</OutputType>
			<PackageOutputPath>$(SolutionDir).nuget-test-packages\$(AssemblyName)</PackageOutputPath>
		</PropertyGroup>
	```

