
# What
Simple analyzer library to add custom analyzers as needed.

Simple in that this is "just" an analyzer.  lots of other analyzers include unit tests and Visual Studio extension projects.  This is just the analyzer.


## Analyzer Rules

//table of rules
	- LL_R001 - Task (or ValueTask) is not awaited or used; - LoLo_Reliability_Concurrency




## Example Analyzer
see the `ExampleConstAnalyzer.cs`






## Need to implement something more fancy?
The ParallelHelper analyzer project is a great reference for how to do things.  https://github.com/Concurrency-Lab/ParallelHelper


## Usage

If referencing this project directly (not the nuget package) be sure to add ` OutputItemType="Analyzer" ReferenceOutputAssembly="false"` to the `.csproj` reference, like:

```xml
<ProjectReference Include="..\lib\NotNot.GodotNet.SourceGen\NotNot.GodotNet.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

