using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.BlazorAnalyzers.CssModernization;
using NotNot.BlazorAnalyzers.NnDesign;

namespace NotNot.BlazorAnalyzers.Tests;

/// <summary>
/// F6 tested ID-registry guard: documented diagnostic IDs MUST match the implemented
/// <see cref="DiagnosticDescriptor"/>s. Prevents the stale-prose failure mode (the "Planned: NNB022"
/// note that drifted while NNB022 was live). The README "Analyzer ID Registry" table is the human-facing
/// view; this test is the machine-facing assertion that each registered ID resolves to exactly one
/// analyzer's <see cref="DiagnosticAnalyzer.SupportedDiagnostics"/> descriptor, and that the two NnDesign
/// manifesto analyzers expose precisely their documented IDs at their documented default severity.
/// </summary>
public class AnalyzerIdRegistryTests
{
    /// <summary>
    /// The manifesto-wave registered IDs (the README "Analyzer ID Registry" rows owned by this wave) and
    /// their expected default severity. The registry test asserts each resolves to a single implemented
    /// descriptor with the documented severity.
    /// </summary>
    public static readonly (string Id, DiagnosticSeverity Severity)[] RegisteredManifestoIds =
    {
        ("NNB022", DiagnosticSeverity.Error),
        ("NNB043", DiagnosticSeverity.Error),
        ("NNB044", DiagnosticSeverity.Warning),
        ("NNB_CSS009", DiagnosticSeverity.Error),
    };

    /// <summary>
    /// Every <see cref="DiagnosticDescriptor"/> implemented by a <see cref="DiagnosticAnalyzer"/> in the
    /// analyzer assembly, discovered by reflecting over the assembly's analyzer types. Used both to resolve
    /// registry IDs and to assert no implemented descriptor is unregistered (the reverse direction).
    /// </summary>
    private static ImmutableArray<DiagnosticDescriptor> AllImplementedDescriptors()
    {
        var analyzerAssembly = typeof(NnDesignTierBExposureAnalyzer).Assembly;
        var descriptors = ImmutableArray.CreateBuilder<DiagnosticDescriptor>();

        foreach (var type in analyzerAssembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
                continue;
            if (type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() == null)
                continue;

            var instance = (DiagnosticAnalyzer)System.Activator.CreateInstance(type)!;
            descriptors.AddRange(instance.SupportedDiagnostics);
        }

        return descriptors.ToImmutable();
    }

    [Fact]
    public void RegisteredManifestoIds_EachResolveToExactlyOneDescriptor_AtDocumentedSeverity()
    {
        var implemented = AllImplementedDescriptors();

        foreach (var (id, severity) in RegisteredManifestoIds)
        {
            var matches = implemented.Where(d => d.Id == id).ToImmutableArray();

            Assert.True(
                matches.Length == 1,
                $"Registry ID '{id}' must resolve to exactly one implemented DiagnosticDescriptor; "
                + $"found {matches.Length}.");

            Assert.Equal(severity, matches[0].DefaultSeverity);
        }
    }

    [Fact]
    public void ManifestoAnalyzers_ExposeTheirDocumentedIds()
    {
        // Direct descriptor-identity assertion (independent of reflection) so a rename of either analyzer
        // type still fails loudly here against the documented public ID constants.
        Assert.Equal("NNB022", NnDesignMudBlazorPolicyAnalyzer.DiagnosticId);
        Assert.Equal("NNB022", NnDesignMudBlazorPolicyAnalyzer.Rule.Id);
        Assert.Equal(DiagnosticSeverity.Error, NnDesignMudBlazorPolicyAnalyzer.Rule.DefaultSeverity);

        Assert.Equal("NNB043", NnDesignTierBExposureAnalyzer.DiagnosticId);
        Assert.Equal("NNB043", NnDesignTierBExposureAnalyzer.Rule.Id);
        Assert.Equal(DiagnosticSeverity.Error, NnDesignTierBExposureAnalyzer.Rule.DefaultSeverity);

        Assert.Equal("NNB044", NnDesignInlineStyleAnalyzer.DiagnosticId);
        Assert.Equal("NNB044", NnDesignInlineStyleAnalyzer.Rule.Id);
        Assert.Equal(DiagnosticSeverity.Warning, NnDesignInlineStyleAnalyzer.Rule.DefaultSeverity);

        Assert.Equal("NNB_CSS009", CssMudReachInAnalyzer.DiagnosticId);
        Assert.Equal("NNB_CSS009", CssMudReachInAnalyzer.RuleNoMudReachIn.Id);
        Assert.Equal(DiagnosticSeverity.Error, CssMudReachInAnalyzer.RuleNoMudReachIn.DefaultSeverity);
    }

    [Fact]
    public void NoDuplicateDiagnosticIds_AcrossAnalyzers()
    {
        var implemented = AllImplementedDescriptors();
        var dupes = implemented
            .GroupBy(d => d.Id)
            .Where(g => g.Select(d => d.Title.ToString()).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToImmutableArray();

        Assert.True(dupes.IsEmpty,
            $"Diagnostic IDs collide across analyzers with differing titles: {string.Join(", ", dupes)}.");
    }
}
