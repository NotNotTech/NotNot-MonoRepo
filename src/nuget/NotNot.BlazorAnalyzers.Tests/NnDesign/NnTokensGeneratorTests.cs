using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NotNot.BlazorAnalyzers.NnDesign;
using Xunit;

namespace NotNot.BlazorAnalyzers.Tests.NnDesign;

/// <summary>
/// Discriminating consumer-fixture verification for <see cref="NnTokensGenerator"/> (Phase-1.4 5A).
/// Runs the generator over a synthetic compilation with representative authority stylesheets
/// (<c>nn-design.css</c> + <c>nn-colors.css</c>) registered as <see cref="AdditionalText"/>s, then
/// inspects the GENERATED sources to confirm the generator actually ran and produced the correct
/// strongly-typed accessor + manifest. "It compiles" is NOT the assertion — these tests assert the
/// generated CONTENT: the real <c>--nns-*</c> token family is CAPTURED (the pre-repair generator matched
/// the nonexistent <c>--nn-*</c> family and emitted the empty Count=0 branch), the <c>nns-</c> prefix is
/// dropped from member names, and the second authority file is read.
/// </summary>
public class NnTokensGeneratorTests
{
    /// <summary>A realistic slice of the canonical nn-design.css token block (the real authoring shape).</summary>
    private const string CanonicalCssSlice = """
        @layer nn-design {
        :root {
          color-scheme: light dark;

          /* Layout Dimensions */
          --nns-navbar-height: 40px;
          --nns-panel-header-height: 32px;

          /* === SPACING SCALE === */
          --nns-spacing-xs: 4px;
          --nns-spacing-sm: 8px;
          --nns-spacing-md: 16px;

          /* var()-valued token with a fallback (must capture the whole value) */
          --nns-splitter-color: var(--mud-palette-lines-default, light-dark(#e0e0e0, #444));
        }

        /* A dark-mode redeclaration: FIRST (canonical :root) wins, this must NOT override the value */
        :root.nns-dark {
          --nns-navbar-height: 999px;
        }
        }
        """;

    /// <summary>A slice of the second authority stylesheet nn-colors.css (proves the two-file read).</summary>
    private const string ColorsCssSlice = """
        @layer nn-design {
        :root {
          --nns-bg-info: #1e88e5;
          --nns-fg-default: #e0e0e0;
        }
        }
        """;

    private static (string nnTokens, string manifest, ImmutableArray<Diagnostic> diagnostics) Generate(
        string cssContent, string cssFileName = "nn-design.css")
        => Generate((cssFileName, cssContent));

    private static (string nnTokens, string manifest, ImmutableArray<Diagnostic> diagnostics) Generate(
        params (string fileName, string content)[] cssFiles)
    {
        var compilation = CSharpCompilation.Create(
            "NotNot.BlazorDesign",
            new[] { CSharpSyntaxTree.ParseText("namespace Placeholder { class C { } }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NnTokensGenerator();
        var additionalTexts = cssFiles
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.fileName, f.content))
            .ToArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: additionalTexts);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var runResult = driver.GetRunResult();
        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(gs => gs.HintName, gs => gs.SourceText.ToString());

        sources.TryGetValue("NnTokens.g.cs", out var nnTokens);
        sources.TryGetValue("NnDesignTokenManifest.g.cs", out var manifest);
        return (nnTokens ?? "", manifest ?? "", diagnostics);
    }

    [Fact]
    public void Generator_Runs_And_Emits_Both_Artifacts()
    {
        var (nnTokens, manifest, diagnostics) = Generate(CanonicalCssSlice);

        diagnostics.Should().BeEmpty("the generator must not raise diagnostics on valid CSS");
        nnTokens.Should().NotBeNullOrEmpty("NnTokens.g.cs must be emitted");
        manifest.Should().NotBeNullOrEmpty("NnDesignTokenManifest.g.cs must be emitted");
    }

    [Fact]
    public void NnTokens_Exposes_StronglyTyped_Members_With_VarRefs()
    {
        var (nnTokens, _, _) = Generate(CanonicalCssSlice);

        // PascalCase members (nns- prefix dropped), each holding the var(--nns-*) reference.
        nnTokens.Should().Contain("public const string SpacingMd = \"var(--nns-spacing-md)\";");
        nnTokens.Should().Contain("public const string NavbarHeight = \"var(--nns-navbar-height)\";");
        nnTokens.Should().Contain("public const string PanelHeaderHeight = \"var(--nns-panel-header-height)\";");
        nnTokens.Should().Contain("namespace NotNot.BlazorDesign.Tokens;");
    }

    [Fact]
    public void NnTokens_Captures_Authored_Default_Values()
    {
        var (nnTokens, _, _) = Generate(CanonicalCssSlice);

        // Defaults dictionary carries the authored value verbatim (var()-with-fallback captured whole).
        nnTokens.Should().Contain("[\"nns-spacing-md\"] = \"16px\",");
        nnTokens.Should().Contain("var(--mud-palette-lines-default, light-dark(#e0e0e0, #444))");
    }

    [Fact]
    public void Nns_Prefix_Is_Dropped_From_Member_Names()
    {
        var (nnTokens, manifest, _) = Generate(CanonicalCssSlice);

        // The nns- prefix is stripped for the C# member (Substring(4)); NOT the wrong NnsSpacingMd.
        nnTokens.Should().Contain("public const string SpacingMd =");
        nnTokens.Should().NotContain("NnsSpacingMd");
        // The CSS name / manifest RETAIN the full nns- token name.
        manifest.Should().Contain("\"nns-spacing-md\",");
    }

    [Fact]
    public void FirstDeclaration_Wins_DarkMode_Redeclaration_Ignored()
    {
        var (nnTokens, _, _) = Generate(CanonicalCssSlice);

        // Canonical :root value (40px) wins; the :root.nns-dark 999px override must be ignored.
        nnTokens.Should().Contain("[\"nns-navbar-height\"] = \"40px\",");
        nnTokens.Should().NotContain("999px");
    }

    [Fact]
    public void TwoFile_Read_Merges_Both_Authority_Stylesheets()
    {
        var (nnTokens, manifest, _) = Generate(
            ("nn-design.css", CanonicalCssSlice),
            ("nn-colors.css", ColorsCssSlice));

        // The second authority file (nn-colors.css) is READ too — its tokens reach both artifacts.
        nnTokens.Should().Contain("public const string BgInfo = \"var(--nns-bg-info)\";");
        nnTokens.Should().Contain("[\"nns-bg-info\"] = \"#1e88e5\",");
        nnTokens.Should().Contain("public const string FgDefault = \"var(--nns-fg-default)\";");
        manifest.Should().Contain("\"nns-bg-info\",");
        // Tokens from the first file (nn-design.css) are still present — the two sets are merged.
        nnTokens.Should().Contain("public const string SpacingMd = \"var(--nns-spacing-md)\";");
        // 6 design tokens + 2 color tokens = 8, merged across both files.
        manifest.Should().Contain("public const int Count = 8;");
    }

    [Fact]
    public void Manifest_Lists_Canonical_Token_Names_And_Json()
    {
        var (_, manifest, _) = Generate(CanonicalCssSlice);

        manifest.Should().Contain("public static partial class NnDesignTokenManifest");
        manifest.Should().Contain("public static readonly string[] ValidTokenNames");
        manifest.Should().Contain("\"nns-spacing-md\",");
        manifest.Should().Contain("\"nns-splitter-color\",");
        // Embedded JSON manifest (the schema-as-data SSOT artifact).
        manifest.Should().Contain("\"version\":1");
        manifest.Should().Contain("\"name\":\"nns-spacing-md\"");
        // Count = 6 --nns-* tokens (navbar-height, panel-header-height, spacing-xs/sm/md, splitter-color)
        // in the single-file slice.
        manifest.Should().Contain("public const int Count = 6;");
    }

    [Fact]
    public void EmptyCss_Still_Emits_Minimal_Confirmation_File()
    {
        var (nnTokens, manifest, _) = Generate("/* no tokens here */");

        nnTokens.Should().Contain("public const int Count = 0;");
        manifest.Should().Contain("public const int Count = 0;");
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
