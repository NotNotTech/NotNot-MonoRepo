using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB049: a per-instance accent FILL must be emitted together with the text colour that sits on it.
/// </summary>
/// <remarks>
/// A consumer picks the accent colour, so the ink on top of it cannot be a constant. The palette that
/// feeds these pickers includes pure black, so any fixed foreground — a role token, a theme default, a
/// literal — eventually paints a colour on itself at 1:1 contrast and the text disappears.
/// <para>
/// This is the defect class that motivated the rule, and it is exactly the kind that passes every other
/// check: the offending declaration was a legitimate token, correctly spelled, on a component that
/// satisfied every existing analyzer. Nothing was malformed. The value was simply computed from the
/// wrong thing — a per-ROLE constant standing in for a per-INSTANCE derivation.
/// </para>
/// <para>
/// Decidability and scope: the rule keys on the <c>-guide-color</c> naming convention, which is how the
/// design system names a per-instance FILL property, and requires the same method to call the on-colour
/// helper. It deliberately does NOT try to infer "is this colour painted as a background", which is not
/// statically decidable. It is therefore a REGRESSION guard over the fill properties that follow the
/// convention, not a general proof of contrast. A stroke/frame accent (named <c>-color</c>, with no text
/// on it) is correctly outside its reach.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NnDesignAccentOnColorPairingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB049";

    /// <summary>Namespace root of the design-system package this rule covers.</summary>
    private const string PackageNamespace = "NotNot.BlazorDesign";

    /// <summary>Naming convention marking a custom property that is FILLED with a per-instance colour.</summary>
    private const string FillPropertyMarker = "-guide-color";

    /// <summary>The helper that derives readable ink for an arbitrary fill.</summary>
    private const string OnColorHelperName = "PickOnColor";

    /// <summary>The descriptor for NNB049.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Per-instance accent fill must emit its paired on-color",
        messageFormat: "'{0}' is a per-instance accent FILL, but this method never calls "
            + OnColorHelperName + " to derive the text colour that sits on it. A consumer chooses this colour and "
            + "the palette includes pure black, so any fixed foreground eventually paints a colour on itself at 1:1 "
            + "contrast and the text vanishes. Emit the paired on-color custom property alongside the fill, computed "
            + "from the same value. If this property is read rather than painted, suppress NNB049 with a documented "
            + "justification.",
        category: "NnDesign Convention",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A custom property following the '-guide-color' convention carries a consumer-chosen fill colour. The "
            + "foreground drawn on that fill must be DERIVED from it, never taken from a per-role constant or a "
            + "literal: the accent picker offers pure black, so a constant foreground is guaranteed to collide with "
            + "some admissible pick. This rule requires the method emitting such a property to also call the "
            + "on-color helper, making the pairing checkable at compile time rather than at the moment a user "
            + "happens to choose the colliding colour. It is a regression guard keyed on the naming convention, not "
            + "a general contrast proof — whether a colour is painted as a background is not statically decidable.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // The whole method is the unit: the fill property name and the derivation belong together, and a
        // style builder is small enough that requiring them in one body is not a burden.
        //
        // Scanning TOKENS rather than LiteralExpressionSyntax is load-bearing. A style builder most
        // naturally writes the property inside an interpolated string, whose text is an
        // InterpolatedStringTextToken and NOT a string-literal expression — a node-level scan silently
        // misses the very shape the rule exists to catch. Token kinds also cover verbatim, raw, and
        // UTF-8 literals for free.
        var fillToken = method
            .DescendantTokens()
            .FirstOrDefault(token =>
                IsStringBearingToken(token)
                && token.ValueText.IndexOf(FillPropertyMarker, StringComparison.Ordinal) >= 0);

        if (fillToken == default)
            return;

        var containingType = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);
        if (!IsInPackage(containingType))
            return;

        if (CallsOnColorHelper(method))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, fillToken.GetLocation(), fillToken.ValueText));
    }

    /// <summary>
    /// Every token kind that can carry string TEXT: plain and verbatim literals, raw literals, UTF-8
    /// literals, and the text segments of an interpolated string.
    /// </summary>
    private static bool IsStringBearingToken(SyntaxToken token) =>
        token.IsKind(SyntaxKind.StringLiteralToken)
        || token.IsKind(SyntaxKind.InterpolatedStringTextToken)
        || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken)
        || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
        || token.IsKind(SyntaxKind.Utf8StringLiteralToken);

    /// <summary>
    /// Name-only match on the helper invocation. The helper name is distinctive, and matching the symbol
    /// would couple the analyzer to an internal type it is referenced analyzer-only against.
    /// </summary>
    private static bool CallsOnColorHelper(MethodDeclarationSyntax method) =>
        method
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == OnColorHelperName,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText == OnColorHelperName,
                _ => false,
            });

    /// <summary>Whether the declaring symbol lives inside the design-system package's namespace tree.</summary>
    private static bool IsInPackage(ISymbol? symbol)
    {
        var ns = symbol?.ContainingNamespace?.ToDisplayString();
        if (ns is null)
            return false;

        return ns == PackageNamespace
            || (ns.StartsWith(PackageNamespace, StringComparison.Ordinal)
                && ns.Length > PackageNamespace.Length
                && ns[PackageNamespace.Length] == '.');
    }
}
