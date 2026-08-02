using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.XRay;

/// <summary>
/// Analyzer (NNB046) that detects when an XRay path-marker mirror declaration has drifted from the
/// canonical <see cref="XRayPathMarkers.SingleSegment"/> set.
/// </summary>
/// <remarks>
/// The XRay folder-marker set { /Features/, /Pages/, /Shared/, /Layout/, /Components/ } has multiple
/// mirrors: the generator (via <see cref="XRayPathMarkers.SingleSegment"/>, the C# SSOT),
/// NotNot.BlazorComponents.XRay.XRayHelper._fallbackMarkers (a bare-init field), and
/// XRayEndpoints.fallbackPrefixes (a collection-expression method-local). The runtime mirrors live in a
/// different assembly that cannot reference the analyzer const, so equality is enforced at compile time.
/// Drift silently broke XRay source resolution (the proven root cause of Defect A). The detector covers
/// BOTH a field and a method-local via <see cref="SyntaxKind.VariableDeclarator"/>, and guards TWO drift
/// axes: (1) segment membership — slashes normalized away, the SET compared (order-free); (2) the per-site
/// slash CONVENTION — keyed on the matched mirror name (_fallbackMarkers ⇒ leading+trailing slashes for the
/// IndexOf/Substring fallback; fallbackPrefixes ⇒ trailing-only for Path.Combine). A collection-expression
/// spread makes the set un-knowable → bail (never false-positive).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class XRayMarkerSetDriftAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB046";

    private static readonly LocalizableString Title =
        "XRay path-marker set has drifted from the canonical XRayPathMarkers.SingleSegment";

    private static readonly LocalizableString MessageFormat =
        "XRay path-marker set '{0}' has drifted from the canonical XRayPathMarkers.SingleSegment " +
        "(generator SSOT) — {1}. Sync it: (1) update '{0}' to the canonical segment set in its REQUIRED " +
        "slash form (_fallbackMarkers ⇒ leading+trailing '/Features/'; fallbackPrefixes ⇒ trailing-only " +
        "'Features/'); (2) if the marker set legitimately CHANGED, update the canonical const + ALL mirrors " +
        "(XRayMetadataGenerator via XRayPathMarkers.SingleSegment, XRayHelper._fallbackMarkers, " +
        "XRayEndpoints.fallbackPrefixes, and the JS SSOT xray-path-helpers.js::SINGLE_SEGMENT_MARKERS) " +
        "together; (3) suppress with #pragma warning disable NNB046 only if this array is intentionally " +
        "NOT an XRay marker mirror.";

    private static readonly LocalizableString Description =
        "The XRay single-segment folder-marker set has ONE canonical source (XRayPathMarkers.SingleSegment) " +
        "with multiple comment-linked mirrors; if a mirror's segment SET or its per-site SLASH CONVENTION " +
        "diverges, XRay source resolution silently breaks (the proven root cause of the prior XRay drift " +
        "defect). NNB046 enforces BOTH axes: segment membership AND the slash form required at the matched " +
        "site (_fallbackMarkers ⇒ leading+trailing slashes for the IndexOf/Substring fallback; " +
        "fallbackPrefixes ⇒ trailing-only for Path.Combine). See README #nnb046 for the full mirror list " +
        "and the JS drift-test companion.";

    private const string Category = "Conventions";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>The known XRay marker-mirror declaration names (the name-key guard).</summary>
    private static readonly HashSet<string> MirrorNames =
        new(System.StringComparer.Ordinal) { FieldMirrorName, LocalMirrorName };

    /// <summary>The canonical normalized segment set (slashes stripped, case-sensitive, order-free).</summary>
    private static readonly HashSet<string> CanonicalSet =
        new(XRayPathMarkers.SingleSegment.Select(NormalizeSegment), System.StringComparer.Ordinal);

    private static string NormalizeSegment(string s) => s.Trim('/');

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // The XRay marker mirrors live in hand-authored .cs, not generated code.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // VariableDeclarator is the common child of FieldDeclarationSyntax AND
        // LocalDeclarationStatementSyntax — one handler reaches both the field and the method-local mirror.
        context.RegisterSyntaxNodeAction(AnalyzeDeclarator, SyntaxKind.VariableDeclarator);
    }

    private static void AnalyzeDeclarator(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not VariableDeclaratorSyntax declarator)
            return;

        // Name guard FIRST (cheapest syntactic pre-filter, before any semantic work).
        if (!MirrorNames.Contains(declarator.Identifier.ValueText))
            return;

        // Identification guard: the declarator must live in an XRay namespace.
        // GetDeclaredSymbol returns IFieldSymbol for a field-context declarator OR ILocalSymbol for a
        // method-local declarator; both reach the containing type (a local via its containing method).
        var symbol = context.SemanticModel.GetDeclaredSymbol(declarator);
        var containingType = (symbol as IFieldSymbol)?.ContainingType
                             ?? (symbol as ILocalSymbol)?.ContainingType;
        if (containingType is null)
            return;

        var ns = containingType.ContainingNamespace?.ToDisplayString();
        if (ns is null ||
            !(ns == "NotNot.BlazorComponents.XRay" || ns.EndsWith(".XRay", System.StringComparison.Ordinal)))
            return;

        // Extract the literal string elements across the three initializer value-node shapes (G3).
        var valueNode = declarator.Initializer?.Value;
        if (valueNode is null)
            return;

        // A collection-expression spread (`[..other]`) means the literal set is NOT statically
        // knowable → cannot prove drift; bail (preserves the never-false-positive guarantee).
        if (valueNode is CollectionExpressionSyntax colCheck &&
            colCheck.Elements.Any(e => e is not ExpressionElementSyntax))
            return;

        IEnumerable<ExpressionSyntax>? elements = valueNode switch
        {
            // (a) bare  T[] x = { ... }  → the Value IS an InitializerExpressionSyntax directly.
            InitializerExpressionSyntax bareInit => bareInit.Expressions,
            // (b) new[] { ... } / new T[] { ... }
            ImplicitArrayCreationExpressionSyntax iac => iac.Initializer.Expressions,
            ArrayCreationExpressionSyntax ac => ac.Initializer?.Expressions,
            // (c) [ ... ] collection expression (spread-free, guarded above)
            CollectionExpressionSyntax col => col.Elements
                .OfType<ExpressionElementSyntax>()
                .Select(e => e.Expression),
            _ => null,
        };

        // Unrecognized initializer shape → cannot prove drift; bail (never false-positive).
        if (elements is null)
            return;

        var values = new List<string>();
        foreach (var expr in elements)
        {
            var constant = context.SemanticModel.GetConstantValue(expr);
            if (!constant.HasValue || constant.Value is not string str)
                return; // non-constant / non-string element → cannot prove drift.
            values.Add(str);
        }

        var name = declarator.Identifier.ValueText;

        // Axis 1 — segment membership (slash-normalized, order-free).
        var actual = new HashSet<string>(values.Select(NormalizeSegment), System.StringComparer.Ordinal);
        if (!actual.SetEquals(CanonicalSet))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                declarator.Identifier.GetLocation(),
                name,
                "its segment set differs from the canonical 5"));
            return;
        }

        // Axis 2 — per-site slash CONVENTION. The segments match, but the slash FORM is load-bearing
        // and differs per site: _fallbackMarkers is consumed via IndexOf(marker)+Substring(idx+1)
        // (needs LEADING+TRAILING slashes); fallbackPrefixes is consumed via prefix+file into
        // Path.Combine (needs TRAILING-ONLY, no leading). A wrong form silently breaks resolution.
        if (!SlashFormMatches(name, values))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                declarator.Identifier.GetLocation(),
                name,
                name == FieldMirrorName
                    ? "its slash convention is wrong (this mirror requires LEADING+TRAILING slashes, e.g. \"/Features/\")"
                    : "its slash convention is wrong (this mirror requires TRAILING-ONLY slashes, e.g. \"Features/\")"));
        }
    }

    /// <summary>The slashed field-mirror name (leading+trailing slash convention).</summary>
    private const string FieldMirrorName = "_fallbackMarkers";

    /// <summary>The slash-less local-mirror name (trailing-only slash convention).</summary>
    private const string LocalMirrorName = "fallbackPrefixes";

    /// <summary>
    /// Verifies each RAW element matches the slash FORM required at the matched site.
    /// <c>_fallbackMarkers</c> ⇒ <c>"/" + segment + "/"</c> (leading+trailing);
    /// <c>fallbackPrefixes</c> ⇒ <c>segment + "/"</c> (trailing-only, no leading).
    /// </summary>
    private static bool SlashFormMatches(string name, List<string> rawValues)
    {
        foreach (var raw in rawValues)
        {
            var seg = NormalizeSegment(raw);
            var expected = name == FieldMirrorName ? "/" + seg + "/" : seg + "/";
            if (!string.Equals(raw, expected, System.StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
