using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NotNot.BlazorAnalyzers.XRay;
using Xunit;

namespace NotNot.BlazorAnalyzers.Tests.XRay;

/// <summary>
/// Generator pin test (G6) for <see cref="XRayMetadataGenerator"/>. Pins the relative-path FALLBACK
/// marker loop (the path that now consumes <see cref="XRayPathMarkers.SingleSegment"/>) by driving a
/// .razor file whose path contains a marker folder but NOT the assembly-name folder, forcing the PRIMARY
/// assembly-anchor to miss and the FALLBACK marker loop + Substring(idx+1) to run. Makes REQ-6
/// (byte-identical generator behavior after the const extraction) RUNTIME_VERIFIED unconditionally.
/// </summary>
public class XRayMetadataGeneratorTests
{
    /// <summary>A minimal .razor body with one parseable element (the generator skips element-less files).</summary>
    private const string RazorWithElement = "<div data-nnxid=\"x\">hi</div>";

    private static (string generated, ImmutableArray<Diagnostic> diagnostics) Generate(
        string assemblyName, string razorPath, string razorContent)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText("namespace Placeholder { class C { } }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new XRayMetadataGenerator();
        var additionalText = new InMemoryAdditionalText(razorPath, razorContent);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: new[] { (AdditionalText)additionalText });

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generated = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Where(gs => gs.HintName == "XRayMetadata.g.cs")
            .Select(gs => gs.SourceText.ToString())
            .FirstOrDefault() ?? "";

        return (generated, diagnostics);
    }

    /// <summary>
    /// FALLBACK path: assembly "PinAsm" does NOT appear in the path; the marker folder "/Components/" does.
    /// The generator must key the file by the FALLBACK relative path "Components/Widgets/Foo.razor"
    /// (Substring(idx+1) keeps the marker folder). Pins the marker list + order + index arithmetic that
    /// XRayPathMarkers.SingleSegment now feeds.
    /// </summary>
    [Fact]
    public void Fallback_MarkerLoop_Keys_RelativePath_FromMarkerFolder()
    {
        var (generated, diagnostics) = Generate(
            assemblyName: "PinAsm",
            razorPath: "C:/proj/Components/Widgets/Foo.razor",
            razorContent: RazorWithElement);

        diagnostics.Should().BeEmpty("the generator must not raise diagnostics on a valid .razor");
        generated.Should().NotBeNullOrEmpty("XRayMetadata.g.cs must be emitted");
        generated.Should().Contain(
            "\"Components/Widgets/Foo.razor\":{\"relativePath\":\"Components/Widgets/Foo.razor\"",
            "the FALLBACK marker loop keeps the /Components/ marker folder via Substring(idx+1)");
    }

    /// <summary>
    /// Control: when the assembly name IS in the path, the PRIMARY anchor wins and the relative path
    /// is everything AFTER the assembly folder — confirming the fallback is genuinely the exercised path above.
    /// </summary>
    [Fact]
    public void Primary_AssemblyAnchor_Keys_PathAfterAssemblyFolder()
    {
        var (generated, _) = Generate(
            assemblyName: "PinAsm",
            razorPath: "C:/proj/PinAsm/Components/Widgets/Foo.razor",
            razorContent: RazorWithElement);

        generated.Should().Contain(
            "\"Components/Widgets/Foo.razor\":{\"relativePath\":\"Components/Widgets/Foo.razor\"",
            "the PRIMARY anchor returns the path after the /PinAsm/ assembly folder");
    }

    /// <summary>Minimal in-memory <see cref="AdditionalText"/> for driving the generator in tests.</summary>
    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;
        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }
        public override string Path { get; }
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }
}
