using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Xunit;

namespace NotNot.AppSettings.IntegrationTests;

/// <summary>
/// Wildcard MSBuild glob integration test.
///
/// This test exists because <c>MultiFileGeneratorTests.Wildcard_ViaPropsGlob_IncludesMultipleFiles</c>
/// is necessarily skipped — wildcard expansion is an MSBuild-layer concern, not a
/// JsonMerger-layer concern, and verifying it requires actually running MSBuild against a
/// consuming project that has a discriminating secondary settings file.
///
/// The fixture is <c>NotNot.AppSettings.Example</c>, which contains:
///   - <c>appsettings.json</c> (root file)
///   - <c>appsettings.Sample.json</c> (secondary file containing <c>Sample.Hello.World</c>)
///   - <c>AppSettings.Development.json</c> (secondary file with Logging config)
///
/// At consumer build time, <c>NotNot.AppSettings.targets:11-14</c> injects
/// <c>&lt;AdditionalFiles Include="appsettings*.json" /&gt;</c> into the Example. MSBuild
/// expands the glob to include all three files. The generator merges them and emits a
/// <c>Sample</c> property on the root <c>AppSettings</c> class.
///
/// This test PASSES if the built Example assembly contains a <c>Sample</c> property on
/// <c>ExampleApp.AppSettingsGen.AppSettings</c>. If wildcard expansion regresses, the
/// Sample property disappears and the test FAILS with a diagnostic message.
///
/// Evidence label: <c>RUNTIME_VERIFIED</c> — the test actually loads the built assembly
/// produced by an MSBuild compile of the Example, then reflects on the generated type.
/// </summary>
public class WildcardAutoGlobIntegrationTests
{
    private const string ExampleAssemblyName = "NotNot.AppSettings.Example.dll";
    private const string GeneratedTypeFullName = "ExampleApp.AppSettingsGen.AppSettings";
    private const string DiscriminatingPropertyName = "Sample";

    [Fact]
    public void GeneratedAppSettings_FromExample_ContainsSampleProperty_ProvingWildcardExpansion()
    {
        var asmPath = LocateExampleAssembly();
        asmPath.Should().NotBeNullOrEmpty(
            $"could not locate '{ExampleAssemblyName}' anywhere reachable from the test bin. " +
            "ProjectReference to NotNot.AppSettings.Example should have copied it into the test " +
            "output directory; if missing, the build pipeline is misconfigured.");

        // Isolated load context avoids contaminating the test process default ALC and
        // sidesteps any version conflicts between Example's transitive deps and the test SDK.
        var alc = new AssemblyLoadContext(
            name: $"WildcardIntegrationTest-{Guid.NewGuid():N}",
            isCollectible: false);

        Assembly asm;
        try
        {
            asm = alc.LoadFromAssemblyPath(asmPath!);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"failed to load assembly '{asmPath}' for wildcard verification. " +
                "Possible causes: (a) Example's transitive dependencies missing from test bin; " +
                "(b) target framework mismatch; (c) corrupted build output.",
                ex);
        }

        var generatedType = asm.GetType(GeneratedTypeFullName, throwOnError: false);
        generatedType.Should().NotBeNull(
            $"type '{GeneratedTypeFullName}' must exist in '{asmPath}'. " +
            "If missing, the source generator did not run against NotNot.AppSettings.Example. " +
            "Likely regression: NotNot.AppSettings.targets:11-14 <AdditionalFiles> injection " +
            "is not firing, OR the generator analyzer is not being loaded.");

        // The generator emits internal-by-default unless NotNot_AppSettings_GenPublic=true.
        // Example.csproj does NOT set GenPublic, so we must look at non-public members too.
        var sampleProp = generatedType!.GetProperty(
            DiscriminatingPropertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        sampleProp.Should().NotBeNull(
            $"property '{DiscriminatingPropertyName}' must exist on '{GeneratedTypeFullName}'. " +
            "This property is generated ONLY when the source generator processes " +
            "appsettings.Sample.json (which is the only file declaring \"Sample.Hello.World\"). " +
            "Its absence proves the wildcard auto-glob (NotNot.AppSettings.targets:11-14) " +
            "FAILED to feed appsettings.Sample.json to the source generator. " +
            "REGRESSION CHECK: " +
            "(1) Did the auto-glob ItemGroup gate condition change? " +
            "(2) Did the AppSettingsGen filter regex tighten beyond what the test fixture uses? " +
            "(3) Did the Example's csproj remove appsettings.Sample.json? " +
            "(4) Did the package's .props/.targets imports break in ProjectReference Analyzer mode?");

        // RUNTIME_VERIFIED claim is now satisfied: an MSBuild-compiled consumer assembly
        // contains the property that ONLY the wildcard-fed secondary file could have produced.
    }

    /// <summary>
    /// Locates the built Example assembly. Probes (in order):
    /// 1. Direct sibling of the test assembly (the typical ProjectReference-copy location).
    /// 2. Sibling Example project's bin directory at any ancestor of BaseDirectory
    ///    (handles vow-agent slotted layouts where build output goes to
    ///    <c>.vow-agent/build/{slot}/{ProjectName}/bin/...</c>).
    /// </summary>
    private static string? LocateExampleAssembly()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Direct sibling (most common case under standard ProjectReference).
        var directHit = Path.Combine(baseDir, ExampleAssemblyName);
        if (File.Exists(directHit))
        {
            return directHit;
        }

        // 2. Walk up looking for sibling Example project bin.
        var current = new DirectoryInfo(baseDir);
        while (current is not null)
        {
            var sibling = Path.Combine(current.FullName, "NotNot.AppSettings.Example", "bin");
            if (Directory.Exists(sibling))
            {
                var candidate = Directory.EnumerateFiles(
                        sibling, ExampleAssemblyName, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (candidate is not null)
                {
                    return candidate;
                }
            }
            current = current.Parent;
        }

        return null;
    }
}
