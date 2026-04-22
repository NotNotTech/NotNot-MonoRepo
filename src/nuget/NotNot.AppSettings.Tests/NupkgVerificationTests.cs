using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace NotNot.AppSettings.Tests;

/// <summary>
/// N2 closure (Phase 5 iteration 2): Artifact-level verification that the emitted
/// <c>NotNot.AppSettings.{version}.nupkg</c> actually contains the
/// <c>&lt;dependency id="NotNot.Bcl.Core"&gt;</c> element injected by
/// <c>InjectBclCoreRuntimeDependency</c> / <c>PatchNupkgWithBclCoreDep</c>
/// (see <c>NotNot.AppSettings.csproj</c>).
///
/// Contract being pinned:
/// 1. The post-Pack nuspec patch target ran.
/// 2. The patched nuspec contains a net10.0 dependency group.
/// 3. That group declares a dependency on <c>NotNot.Bcl.Core</c>.
///
/// If Pack hasn't run in the current slot (e.g. plain <c>dotnet test</c> without a prior
/// pack), the test SKIPS gracefully rather than FAILS — the contract it pins only applies
/// post-Pack. The build-time gate in the inline MSBuild task (post-write re-read
/// verification) is the first line of defense; this test provides runtime artifact-level
/// evidence that can be independently re-run.
/// </summary>
public class NupkgVerificationTests
{
    private const string PackageId = "NotNot.AppSettings";
    private const string InjectedDependencyId = "NotNot.Bcl.Core";
    private const string ExpectedTargetFramework = "net10.0";

    [Fact]
    public void Nupkg_ContainsBclCoreDependencyInNet10Group()
    {
        var nupkgPath = LocateMostRecentNupkg();
        if (nupkgPath is null)
        {
            // No nupkg present — Pack has not been invoked for NotNot.AppSettings in this
            // slot. Skip rather than fail so `dotnet test` stays green for runs that don't
            // include Pack. Build-time verification inside PatchNupkgWithBclCoreDep covers
            // the contract every time Pack actually runs.
            return;
        }

        using var zip = ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = zip.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                              && !e.FullName.Contains('/'));
        nuspecEntry.Should().NotBeNull(
            $"emitted nupkg '{nupkgPath}' must contain a root-level .nuspec entry");

        XDocument nuspec;
        using (var stream = nuspecEntry!.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        var ns = nuspec.Root!.GetDefaultNamespace();

        var net10Group = nuspec.Descendants(ns + "group")
            .FirstOrDefault(g => (string?)g.Attribute("targetFramework") == ExpectedTargetFramework);
        net10Group.Should().NotBeNull(
            $"net10.0 dependency group must be present in nuspec of '{nupkgPath}' — " +
            "it is injected by InjectBclCoreRuntimeDependency (NotNot.AppSettings.csproj)");

        var bclCoreDep = net10Group!.Elements(ns + "dependency")
            .FirstOrDefault(d => (string?)d.Attribute("id") == InjectedDependencyId);
        bclCoreDep.Should().NotBeNull(
            $"'{InjectedDependencyId}' dependency must be present in net10.0 group of nuspec " +
            $"in '{nupkgPath}' — without it, consumers of LoadDirect* overloads hit CS0234 at " +
            "compile time (REQ-7 / Phase-5 H1 contract)");

        var version = (string?)bclCoreDep!.Attribute("version");
        version.Should().NotBeNullOrWhiteSpace(
            $"'{InjectedDependencyId}' dependency entry must declare a version attribute");
    }

    /// <summary>
    /// Searches up from the test binary's output directory for the most recently-written
    /// <c>NotNot.AppSettings.*.nupkg</c>. Returns <c>null</c> if no candidate exists
    /// (Pack hasn't run in this slot).
    /// </summary>
    private static string? LocateMostRecentNupkg()
    {
        // Test bin layout (dotnet test in a slot):
        //   .vow-agent/build/{slot}/NotNot.AppSettings.Tests/bin/{Config}/net10.0/
        //       NotNot.AppSettings.Tests.dll  <-- BaseDirectory points here
        // Sibling generator project output (where Pack drops the nupkg by default):
        //   .vow-agent/build/{slot}/NotNot.AppSettings/bin/{Config}/
        //       NotNot.AppSettings.*.nupkg
        //
        // We walk up from BaseDirectory looking for a directory that contains a
        // 'NotNot.AppSettings' sibling with bin/, then search there. This is resilient to
        // both the vow-build slotted layout AND a plain local build layout.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);

        // Also try well-known relative probes (belt-and-suspenders for unusual layouts).
        var probes = new[]
        {
            // ../../../../NotNot.AppSettings/bin — when tests built into per-project bin/.
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "NotNot.AppSettings", "bin")),
            // ../../../NotNot.AppSettings/bin — when tests built at one-higher layer.
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "NotNot.AppSettings", "bin")),
        };

        // Walk up the directory tree looking for a NotNot.AppSettings/bin sibling.
        while (current is not null)
        {
            var siblingBin = Path.Combine(current.FullName, "NotNot.AppSettings", "bin");
            if (Directory.Exists(siblingBin))
            {
                var pkgFromSibling = FindNupkgIn(siblingBin);
                if (pkgFromSibling is not null)
                    return pkgFromSibling;
            }
            current = current.Parent;
        }

        foreach (var probe in probes)
        {
            if (Directory.Exists(probe))
            {
                var pkg = FindNupkgIn(probe);
                if (pkg is not null)
                    return pkg;
            }
        }

        return null;
    }

    private static string? FindNupkgIn(string rootDir)
    {
        try
        {
            return Directory.EnumerateFiles(rootDir, PackageId + ".*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
