extern alias appsettings_gen;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using AppSettingsGenConfig = appsettings_gen::NotNot.AppSettingsGenConfig;

namespace NotNot.AppSettings.Tests;

public class ClientWhitelistGeneratorTests
{
    private static Dictionary<string, SourceText> RunGenerator(params (string path, string json)[] files)
    {
        var combined = new Dictionary<string, SourceText>(StringComparer.Ordinal);
        foreach (var (path, json) in files)
        {
            combined[path] = SourceText.From(json, Encoding.UTF8);
        }

        var config = new AppSettingsGenConfig
        {
            ProjectName = "ClientWhitelistGeneratorTestsFixture",
            RootNamespace = "ClientWhitelistGeneratorTestsFixture",
            IsPublic = true,
            CombinedSourceTexts = combined,
            NugetVersion = "test-0.0.0",
        };

        var generatorAsm = typeof(appsettings_gen::NotNot.AppSettingsGenConfig).Assembly;
        var genType = generatorAsm.GetType("NotNot.AppSettingsGen", throwOnError: true)!;
        var genInstance = Activator.CreateInstance(genType)!;
        var method = genType.GetMethod("GenerateSourceFiles", BindingFlags.Instance | BindingFlags.Public)!;
        return (Dictionary<string, SourceText>)method.Invoke(genInstance, new object[] { config })!;
    }

    private static string GetGeneratedSource(Dictionary<string, SourceText> emitted, string key)
    {
        emitted.Should().ContainKey(key, $"generator must emit '{key}'");
        return emitted[key].ToString();
    }

    [Fact]
    public void NoWhitelist_BaselineCompatibility_EmitsFullClientTreeWithoutRoutingAttributes()
    {
        var emitted = RunGenerator(
            ("appsettings.json", """
            {
              "Alpha": "value",
              "Nested": {
                "Flag": true
              }
            }
            """)
        );

        var appSettingsRoot = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen.AppSettings.g.cs");
        var clientSettingsRoot = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettings.g.cs");
        var clientNested = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes.Nested.g.cs");

        appSettingsRoot.Should().Contain("public string? Alpha");
        clientSettingsRoot.Should().Contain("public string? Alpha");
        clientSettingsRoot.Should().Contain("public ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes.Nested? Nested");
        clientNested.Should().Contain("public bool? Flag");
        clientSettingsRoot.Should().NotContain("ClientReadAttribute");
        clientSettingsRoot.Should().NotContain("ClientWriteLocalAttribute");
        clientSettingsRoot.Should().NotContain("ClientWriteServerAttribute");
    }

    [Fact]
    public void MixedWhitelistTypes_FilterClientSettings_AndEmitExpectedAttributes()
    {
        var emitted = RunGenerator(
            ("appsettings.json", """
            {
              "ReadOnlySetting": "visible",
              "LocalSetting": true,
              "ServerWritableSetting": 5,
              "ServerSecret": "hidden",
              "NotNotAppSettings": {
                "whitelist": {
                  "ReadOnlySetting": "ClientRead",
                  "LocalSetting": "ClientWriteLocal",
                  "ServerWritableSetting": "ClientWriteServer",
                  "ServerSecret": "ServerOnly"
                }
              }
            }
            """)
        );

        var appSettingsRoot = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen.AppSettings.g.cs");
        var clientSettingsRoot = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettings.g.cs");
        var emittedAttributes = GetGeneratedSource(emitted, "NotNot.AppSettings.ClientSettingsAttributes.g.cs");

        appSettingsRoot.Should().NotContain("NotNotAppSettings",
            "generator metadata should not pollute the strongly typed settings contract");
        emittedAttributes.Should().Contain("namespace NotNot.AppSettings;");
        emittedAttributes.Should().Contain("public sealed class ClientReadAttribute");
        clientSettingsRoot.Should().Contain("[global::NotNot.AppSettings.ClientReadAttribute]");
        clientSettingsRoot.Should().Contain("[global::NotNot.AppSettings.ClientWriteLocalAttribute]");
        clientSettingsRoot.Should().Contain("[global::NotNot.AppSettings.ClientWriteServerAttribute]");
        clientSettingsRoot.Should().Contain("public string? ReadOnlySetting");
        clientSettingsRoot.Should().Contain("public bool? LocalSetting");
        clientSettingsRoot.Should().Contain("public double? ServerWritableSetting");
        clientSettingsRoot.Should().NotContain("ServerSecret");
        clientSettingsRoot.Should().NotContain("NotNotAppSettings");
    }

    [Fact]
    public void InheritedWhitelist_SubtreeOverride_PrunesServerOnlyBranches()
    {
        var emitted = RunGenerator(
            ("appsettings.json", """
            {
              "Root": {
                "Ui": {
                  "Theme": "dark",
                  "FontScale": 1.2,
                  "ServerManaged": "en-US"
                },
                "AdminSecret": "top-secret"
              },
              "NotNotAppSettings": {
                "whitelist": {
                  "Root": "ServerOnly",
                  "Root:Ui": "ClientWriteLocal",
                  "Root:Ui:ServerManaged": "ClientRead"
                }
              }
            }
            """)
        );

        var clientRoot = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettings.g.cs");
        var clientRootNested = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes.Root.g.cs");
        var clientUiNested = GetGeneratedSource(emitted, "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes._Root.Ui.g.cs");

        clientRoot.Should().Contain("public ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes.Root? Root");
        clientRootNested.Should().Contain("public ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes._Root.Ui? Ui");
        clientRootNested.Should().NotContain("AdminSecret");
        clientUiNested.Should().Contain("[global::NotNot.AppSettings.ClientWriteLocalAttribute]");
        clientUiNested.Should().Contain("public string? Theme");
        clientUiNested.Should().Contain("public double? FontScale");
        clientUiNested.Should().Contain("[global::NotNot.AppSettings.ClientReadAttribute]");
        clientUiNested.Should().Contain("public string? ServerManaged");
    }

    [Fact]
    public void ReflectionAssertions_FindExpectedClientAttributes_OnCompiledClientSettingsType()
    {
        var emitted = RunGenerator(
            ("appsettings.json", """
            {
              "ReadOnlySetting": "visible",
              "LocalSetting": true,
              "ServerWritableSetting": 5,
              "ServerSecret": "hidden",
              "NotNotAppSettings": {
                "whitelist": {
                  "ReadOnlySetting": "ClientRead",
                  "LocalSetting": "ClientWriteLocal",
                  "ServerWritableSetting": "ClientWriteServer",
                  "ServerSecret": "ServerOnly"
                }
              }
            }
            """)
        );

        var loadContext = new AssemblyLoadContext($"ClientWhitelistReflection_{Guid.NewGuid():N}", isCollectible: true);
        var assembly = CompileGeneratedAssembly(emitted, loadContext, includeGeneratorAssemblyReference: false);
        var clientType = assembly.GetType("ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettings", throwOnError: true)!;

        clientType.GetProperty("ReadOnlySetting")!.GetCustomAttributes(false)
            .Select(a => a.GetType().FullName)
            .Should().Contain("NotNot.AppSettings.ClientReadAttribute");

        clientType.GetProperty("LocalSetting")!.GetCustomAttributes(false)
            .Select(a => a.GetType().FullName)
            .Should().Contain("NotNot.AppSettings.ClientWriteLocalAttribute");

        clientType.GetProperty("ServerWritableSetting")!.GetCustomAttributes(false)
            .Select(a => a.GetType().FullName)
            .Should().Contain("NotNot.AppSettings.ClientWriteServerAttribute");

        clientType.GetProperty("ServerSecret").Should().BeNull("ServerOnly keys must be pruned from _ClientAppSettings");
    }

    [Fact]
    public void MsbuildOptOut_DisablesAutoGlob_WhenExplicitAdditionalFilesAreProvided()
    {
        var repoRoot = FindRepoRoot();
        var propsPath = Path.Combine(repoRoot, "src", "external-repo", "NotNot-MonoRepo", "src", "nuget", "NotNot.AppSettings", "NotNot.AppSettings.props");
        var targetsPath = Path.Combine(repoRoot, "src", "external-repo", "NotNot-MonoRepo", "src", "nuget", "NotNot.AppSettings", "NotNot.AppSettings.targets");

        var propsDoc = XDocument.Load(propsPath);
        var targetsDoc = XDocument.Load(targetsPath);
        var propsNs = propsDoc.Root!.Name.Namespace;
        var targetsNs = targetsDoc.Root!.Name.Namespace;

        propsDoc.Descendants(propsNs + "NotNot_AppSettings_AutoGlob")
            .Select(x => x.Value)
            .Should().ContainSingle("true",
                "props must default NotNot_AppSettings_AutoGlob to true for existing consumers");

        propsDoc.Descendants(propsNs + "AdditionalFiles")
            .Should().BeEmpty("auto-glob item should live in .targets so consumer csproj properties can opt out before item evaluation");

        var autoGlobItem = targetsDoc.Descendants(targetsNs + "AdditionalFiles")
            .Single(x => (string?)x.Attribute("Include") == "appsettings*.json");
        var autoGlobGroupCondition = ((XElement)autoGlobItem.Parent!).Attribute("Condition")!.Value;

        autoGlobGroupCondition.Should().Contain("$(_NotNotAppSettingsAutoGlobNormalized)");
        autoGlobGroupCondition.Should().Contain("'true'",
            "targets should only add appsettings*.json when the normalized opt-out property remains true");

        targetsDoc.Descendants(targetsNs + "CompilerVisibleProperty")
            .Select(x => (string?)x.Attribute("Include"))
            .Should().Contain("NotNot_AppSettings_AutoGlob",
                "the opt-out property should remain compiler-visible for downstream diagnostics/debugging");
    }

    private static Assembly CompileGeneratedAssembly(Dictionary<string, SourceText> emitted, AssemblyLoadContext loadContext, bool includeGeneratorAssemblyReference = true)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(
                "global using System.Collections.Generic; global using System.IO;",
                new CSharpParseOptions(LanguageVersion.Preview),
                path: "_ImplicitUsings.g.cs")
        }.Concat(emitted.Select(kvp => CSharpSyntaxTree.ParseText(
            kvp.Value.ToString(),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: kvp.Key)));

        var references = new List<MetadataReference>();
        var tpa = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();
        references.AddRange(tpa.Select(path => MetadataReference.CreateFromFile(path)));

        var generatorAssembly = typeof(appsettings_gen::NotNot.AppSettingsGenConfig).Assembly;
        if (includeGeneratorAssemblyReference && !string.IsNullOrWhiteSpace(generatorAssembly.Location))
        {
            references.Add(MetadataReference.CreateFromFile(generatorAssembly.Location));
        }

        var compilation = CSharpCompilation.Create(
            $"ClientWhitelistCompiled_{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        emitResult.Success.Should().BeTrue($"generated source must compile cleanly:\n{string.Join(Environment.NewLine, emitResult.Diagnostics)}");

        peStream.Position = 0;
        return loadContext.LoadFromStream(peStream);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "src", "external-repo", "NotNot-MonoRepo", "src", "nuget", "NotNot.AppSettings", "NotNot.AppSettings.props");
            if (File.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
