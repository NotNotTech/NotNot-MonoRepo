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
/// Runs the generator over a synthetic compilation with a representative <c>nn-design.css</c> registered
/// as an <see cref="AdditionalText"/>, then inspects the GENERATED sources to confirm the generator
/// actually ran and produced the correct strongly-typed accessor + analyzer manifest. "It compiles" is
/// NOT the assertion — these tests assert the generated CONTENT (token members, var() refs, manifest set).
/// </summary>
public class NnTokensGeneratorTests
{
    /// <summary>A realistic slice of the canonical nn-design.css token block (the real authoring shape).</summary>
    private const string CanonicalCssSlice = """
        @layer nn-design {
        :root {
          color-scheme: light dark;

          /* Layout Dimensions */
          --nn-navbar-height: 40px;
          --nn-panel-header-height: 32px;

          /* === SPACING SCALE === */
          --nn-spacing-xs: 4px;
          --nn-spacing-sm: 8px;
          --nn-spacing-md: 16px;

          /* var()-valued token with a fallback (must capture the whole value) */
          --nn-splitter-color: var(--mud-palette-lines-default, light-dark(#e0e0e0, #444));

          /* --nns-* family MUST be ignored (distinct state-token axis) */
          --nns-surface: #fff;
          --nns-text: #212121;
        }

        /* A dark-mode redeclaration: FIRST (canonical :root) wins, this must NOT override the value */
        :root.nns-dark {
          --nn-navbar-height: 999px;
        }
        }
        """;

    private static (string nnTokens, string manifest, ImmutableArray<Diagnostic> diagnostics) Generate(
        string cssContent, string cssFileName = "nn-design.css")
    {
        var compilation = CSharpCompilation.Create(
            "NotNot.BlazorDesign",
            new[] { CSharpSyntaxTree.ParseText("namespace Placeholder { class C { } }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new NnTokensGenerator();
        var additionalText = new InMemoryAdditionalText(cssFileName, cssContent);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator.AsSourceGenerator() },
            additionalTexts: new[] { (AdditionalText)additionalText });

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

        // PascalCase members (nn- prefix dropped), each holding the var(--nn-*) reference.
        nnTokens.Should().Contain("public const string SpacingMd = \"var(--nn-spacing-md)\";");
        nnTokens.Should().Contain("public const string NavbarHeight = \"var(--nn-navbar-height)\";");
        nnTokens.Should().Contain("public const string PanelHeaderHeight = \"var(--nn-panel-header-height)\";");
        nnTokens.Should().Contain("namespace NotNot.BlazorDesign.Tokens;");
    }

    [Fact]
    public void NnTokens_Captures_Authored_Default_Values()
    {
        var (nnTokens, _, _) = Generate(CanonicalCssSlice);

        // Defaults dictionary carries the authored value verbatim (var()-with-fallback captured whole).
        nnTokens.Should().Contain("[\"nn-spacing-md\"] = \"16px\",");
        nnTokens.Should().Contain("var(--mud-palette-lines-default, light-dark(#e0e0e0, #444))");
    }

    [Fact]
    public void FirstDeclaration_Wins_DarkMode_Redeclaration_Ignored()
    {
        var (nnTokens, _, _) = Generate(CanonicalCssSlice);

        // Canonical :root value (40px) wins; the :root.nns-dark 999px override must be ignored.
        nnTokens.Should().Contain("[\"nn-navbar-height\"] = \"40px\",");
        nnTokens.Should().NotContain("999px");
    }

    [Fact]
    public void Nns_State_Tokens_Are_Excluded()
    {
        var (nnTokens, manifest, _) = Generate(CanonicalCssSlice);

        // --nns-* is a DISTINCT axis (nn-colors.css) — must NOT leak into the --nn-* manifest/accessor.
        nnTokens.Should().NotContain("nns-surface");
        nnTokens.Should().NotContain("nns-text");
        manifest.Should().NotContain("nns-surface");
    }

    [Fact]
    public void Manifest_Lists_Canonical_Token_Names_And_Json()
    {
        var (_, manifest, _) = Generate(CanonicalCssSlice);

        manifest.Should().Contain("public static partial class NnDesignTokenManifest");
        manifest.Should().Contain("public static readonly string[] ValidTokenNames");
        manifest.Should().Contain("\"nn-spacing-md\",");
        manifest.Should().Contain("\"nn-splitter-color\",");
        // Embedded JSON manifest (the analyzer-consumable artifact).
        manifest.Should().Contain("\"version\":1");
        manifest.Should().Contain("\"name\":\"nn-spacing-md\"");
        // Count = 6 --nn-* tokens (navbar-height, panel-header-height, spacing-xs/sm/md, splitter-color);
        // the two --nns-* tokens are excluded.
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
