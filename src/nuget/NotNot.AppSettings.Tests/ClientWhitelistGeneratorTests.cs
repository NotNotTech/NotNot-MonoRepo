extern alias appsettings_gen;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
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
    public void Policy_Keys_Are_CaseInsensitive_Regression()
    {
        // R1.5 regression: whitelist policy lookups must be case-insensitive.
        // appsettings.json property names are case-insensitive per .NET IConfiguration (colon-separated
        // paths flattened by Microsoft.Extensions.Configuration.Json use OrdinalIgnoreCase). A whitelist
        // authored with "VibeOverwatch:ui" (lowercase segment) must resolve the same policy as the
        // canonical casing "VibeOverwatch:Ui" used by the JSON structure.
        //
        // Structured as paired generator runs: same settings tree + same access map, differing only in
        // the whitelist key casing. Both runs MUST produce equivalent emitted client surface, proving
        // policy keys are matched OrdinalIgnoreCase.

        const string settingsTree = """
        {
          "VibeOverwatch": {
            "Ui": {
              "Theme": "dark",
              "FontScale": 1.2
            },
            "ServerSecret": "hidden"
          },
          "NotNotAppSettings": {
            "whitelist": {
              "VibeOverwatch": "ServerOnly",
              "__WHITELIST_KEY__": "ClientWriteLocal"
            }
          }
        }
        """;

        var canonicalCasingEmitted = RunGenerator(
            ("appsettings.json", settingsTree.Replace("__WHITELIST_KEY__", "VibeOverwatch:Ui"))
        );

        var lowerCasingEmitted = RunGenerator(
            ("appsettings.json", settingsTree.Replace("__WHITELIST_KEY__", "VibeOverwatch:ui"))
        );

        const string uiNestedKey = "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes._VibeOverwatch.Ui.g.cs";
        const string vowNestedKey = "ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettingsTypes.VibeOverwatch.g.cs";

        // Canonical casing: baseline — Ui subtree emitted with ClientWriteLocal attributes, ServerSecret pruned.
        var canonicalUi = GetGeneratedSource(canonicalCasingEmitted, uiNestedKey);
        canonicalUi.Should().Contain("[global::NotNot.AppSettings.ClientWriteLocalAttribute]");
        canonicalUi.Should().Contain("public string? Theme");
        canonicalUi.Should().Contain("public double? FontScale");

        var canonicalVow = GetGeneratedSource(canonicalCasingEmitted, vowNestedKey);
        canonicalVow.Should().NotContain("ServerSecret", "ServerOnly keys must be pruned from canonical-casing run");

        // Lowercase-segment casing: MUST produce same surface (same files emitted, same attributes, same pruning).
        // Pre-fix behavior: Ui subtree would be pruned entirely because the policy dict lookup of
        // "VibeOverwatch:Ui" against key "VibeOverwatch:ui" would miss under StringComparer.Ordinal.
        var lowerUi = GetGeneratedSource(lowerCasingEmitted, uiNestedKey);
        lowerUi.Should().Contain("[global::NotNot.AppSettings.ClientWriteLocalAttribute]",
            "lowercase whitelist segment must still resolve ClientWriteLocal policy");
        lowerUi.Should().Contain("public string? Theme");
        lowerUi.Should().Contain("public double? FontScale");

        var lowerVow = GetGeneratedSource(lowerCasingEmitted, vowNestedKey);
        lowerVow.Should().NotContain("ServerSecret", "ServerOnly keys must be pruned from lower-casing run");
    }

    [Fact]
    public void ExplicitAdditionalFiles_MergedAcrossMultipleAppsettings_ProduceCompilableClientTypes()
    {
        // R1.4: end-to-end Roslyn-pipeline verification — provide the generator with multiple
        // appsettings*.json inputs DIRECTLY (simulating what an MSBuild AutoGlob or an explicit
        // <AdditionalFiles> list would produce in a real build) and assert that the emitted C#
        // types compile cleanly into a loadable assembly with the expected client-facing surface.
        //
        // Scope clarification (TDD Phase 3 Q1-D 2026-04-23): this test exercises the generator's
        // Roslyn layer ONLY. It does NOT evaluate MSBuild .props/.targets — the AutoGlob discovery
        // path is verified manually via Phase 2 Task 4's `dotnet build` on the example project.
        // The previously-here XML-structure test was a weak proxy for MSBuild behavior and has been
        // superseded by this real compile-verification.
        var emitted = RunGenerator(
            ("appsettings.json", """
            {
              "Shared": {
                "ApiBaseUrl": "https://api.example.com",
                "ServerSecret": "from-base"
              },
              "Telemetry": {
                "Enabled": true
              },
              "NotNotAppSettings": {
                "whitelist": {
                  "Shared:ApiBaseUrl": "ClientRead",
                  "Shared:ServerSecret": "ServerOnly",
                  "Telemetry": "ClientWriteLocal"
                }
              }
            }
            """),
            ("appsettings.Development.json", """
            {
              "Shared": {
                "ApiBaseUrl": "https://dev.example.com"
              },
              "DevOnly": {
                "FeatureFlag": "preview"
              },
              "NotNotAppSettings": {
                "whitelist": {
                  "DevOnly:FeatureFlag": "ClientWriteServer"
                }
              }
            }
            """)
        );

        // Sanity-check the generator produced the expected emitted artifacts before attempting compile.
        emitted.Should().ContainKey("ClientWhitelistGeneratorTestsFixture.AppSettingsGen.AppSettings.g.cs",
            "generator must emit the strong-typed root from merged AdditionalFiles input");
        emitted.Should().ContainKey("ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettings.g.cs",
            "generator must emit the client-facing root from merged AdditionalFiles input");
        emitted.Should().ContainKey("NotNot.AppSettings.ClientSettingsAttributes.g.cs",
            "generator must emit the shared client-attribute definitions");

        // Compile the emitted source set into an in-memory assembly. The CompileGeneratedAssembly
        // helper invokes CSharpCompilation.Emit and asserts emitResult.Success — any source-level
        // generator regression that breaks compilation surfaces as a test failure with diagnostics.
        var loadContext = new AssemblyLoadContext($"ExplicitAdditionalFilesEmit_{Guid.NewGuid():N}", isCollectible: true);
        var assembly = CompileGeneratedAssembly(emitted, loadContext, includeGeneratorAssemblyReference: false);

        // Assert the strong-typed client surface matches the merged whitelist policy:
        //  - Shared.ApiBaseUrl: ClientRead   (visible on _ClientAppSettingsTypes.Shared, with ClientReadAttribute)
        //  - Shared.ServerSecret: ServerOnly (PRUNED from client surface)
        //  - Telemetry: ClientWriteLocal     (subtree visible, with ClientWriteLocalAttribute)
        //  - DevOnly.FeatureFlag: ClientWriteServer (visible on _ClientAppSettingsTypes.DevOnly, with ClientWriteServerAttribute)
        var clientRoot = assembly.GetType("ClientWhitelistGeneratorTestsFixture.AppSettingsGen._ClientAppSettings", throwOnError: true)!;

        var sharedProp = clientRoot.GetProperty("Shared");
        sharedProp.Should().NotBeNull("Shared subtree contains a ClientRead key, so the wrapper must reach the client surface");
        var sharedType = sharedProp!.PropertyType.GenericTypeArguments.FirstOrDefault() ?? sharedProp.PropertyType;
        sharedType.GetProperty("ApiBaseUrl")!.GetCustomAttributes(false)
            .Select(a => a.GetType().FullName)
            .Should().Contain("NotNot.AppSettings.ClientReadAttribute");
        sharedType.GetProperty("ServerSecret").Should().BeNull(
            "ServerOnly keys must be pruned from the client-facing strong type even when their parent subtree is exposed");

        var telemetryProp = clientRoot.GetProperty("Telemetry");
        telemetryProp.Should().NotBeNull("Telemetry subtree is whitelisted ClientWriteLocal");
        telemetryProp!.GetCustomAttributes(false)
            .Select(a => a.GetType().FullName)
            .Should().Contain("NotNot.AppSettings.ClientWriteLocalAttribute");

        var devOnlyProp = clientRoot.GetProperty("DevOnly");
        devOnlyProp.Should().NotBeNull(
            "DevOnly was introduced solely by appsettings.Development.json — its presence on the client surface proves the generator merged BOTH AdditionalFiles inputs");
        var devOnlyType = devOnlyProp!.PropertyType.GenericTypeArguments.FirstOrDefault() ?? devOnlyProp.PropertyType;
        devOnlyType.GetProperty("FeatureFlag")!.GetCustomAttributes(false)
            .Select(a => a.GetType().FullName)
            .Should().Contain("NotNot.AppSettings.ClientWriteServerAttribute");
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
}
