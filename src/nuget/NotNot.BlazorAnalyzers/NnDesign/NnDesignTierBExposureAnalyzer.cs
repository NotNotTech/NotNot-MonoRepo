using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB043 — a public raw Tier-B APPEARANCE parameter must not be EXPOSED on an <c>Nn*</c> component,
/// governed by an explicit deny/allow POLICY TABLE. This is the "enforced or it doesn't exist" backbone
/// of the NnDesign manifesto's Tier-B/appearance-channel control: the design system must not self-violate
/// its own rule by exposing raw appearance-laundering passthroughs (the entropy channel a consumer would
/// use to bypass the prescriptive contract).
/// <para>
/// <b>Scans the PRODUCER.</b> Unlike NNB022 / NNB_CSS008 (which EXEMPT <c>/NotNot.BlazorDesign/</c> because
/// they police CONSUMER usage), NNB043 deliberately analyzes the NnDesign assembly's OWN
/// <c>Nn*</c>-component parameter declarations — the manifesto demands the rule flag the design system's
/// own raw appearance params. The producer references this analyzer via
/// <c>&lt;ProjectReference … OutputItemType="Analyzer"&gt;</c>; NNB043 therefore fires during the producer
/// build. There is NO producer-path exemption (that would defeat the rule's entire purpose).
/// </para>
/// <para>
/// <b>Symbol-based, NOT a string/regex heuristic.</b> Fires on <see cref="SymbolKind.NamedType"/>, iterates
/// each type's public <c>[Parameter]</c>-marked properties via the semantic model, and matches by
/// resolved TYPE (<c>string</c>) + property-name shape — never by source text. The first detector covers
/// string <c>*Style</c> / <c>*Class</c> params (today's appearance-laundering channel); the
/// <see cref="PolicyTable"/> structure admits future non-string Tier-B rows (e.g. raw <c>Variant</c> /
/// <c>Margin</c> / <c>Elevation</c> passthroughs) as the contract formalizes — NNB043 is the UNIFY-1
/// Tier-B-exposure OWNER, not a one-off "no strings" rule.
/// </para>
/// <para>
/// <b>Policy table (deny by default for the matched shape; explicit allow-rows survive).</b> A matched
/// param FIRES unless its <c>(componentType, paramName)</c> pair is a documented Tier-A allow-row. Seed
/// allow-rows (documented Tier-A brand-permitted-variation channels, per
/// <c>NnDesignLayoutContract.VowSpec</c> SPEC-002 / SPEC-003):
/// <list type="bullet">
///   <item><c>NnComponentBase.Style</c> / <c>NnComponentBase.Class</c> — the base-class root-element escape
///         hatch inherited by every wrapper (SPEC-003).</item>
///   <item><c>NnDialog.TitleClass</c> / <c>ContentClass</c> / <c>ActionsClass</c> — per-region dialog class
///         hooks for genuinely-unbounded per-dialog layout shape (SPEC-002).</item>
///   <item><c>NnAppBar.ToolBarClass</c> — structural class on the inner toolbar element the base
///         <c>Class</c> cannot reach (SPEC-002).</item>
/// </list>
/// The allow-rows are class-only channels — their raw <c>*Style</c> forms are NOT exposed; any OTHER public
/// <c>string</c>-typed <c>*Style</c> / <c>*Class</c> param on an <c>Nn*</c> component is a DENY (fires).
/// </para>
/// <para>
/// <b>Severity</b> = Error. The producer is verified appearance-laundering-clean (the seed conformity
/// removed BodyStyle/BodyClass/ContentStyle at the producer, so NNB043 fires ZERO on the real producer),
/// so the rule is ratcheted to a build-breaking Error: any NEW raw Tier-B exposure fails the build.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnDesignTierBExposureAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for a raw Tier-B appearance param exposed on an <c>Nn*</c> component.</summary>
    public const string DiagnosticId = "NNB043";

    private const string Category = "NnDesign";

    private const string HelpBase =
        "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#";

    /// <summary>Per-symbol opt-out marker (XML-doc or trivia) — <c>nnb043:allow-tierb: &lt;reason&gt;</c>.</summary>
    private const string OptOutMarker = "nnb043:allow-tierb";

    private static readonly LocalizableString Title =
        "Raw Tier-B appearance param exposed on an Nn* component — use a semantic param";

    private static readonly LocalizableString MessageFormat =
        "Public '{0}' on Nn* component '{1}' is a raw Tier-B appearance passthrough ('{2}'-shaped) and is "
        + "not a documented Tier-A allow-row. Raw appearance-laundering params let a consumer bypass the "
        + "prescriptive NnDesign contract. Replace it with a SEMANTIC param (e.g. a Density/BodyLayout-style "
        + "token), or — if it is a genuine brand-permitted-variation channel — document it Tier-A and add it "
        + "to the NNB043 policy table as an allow-row. (NNB043)";

    private static readonly LocalizableString Description =
        "The NnDesign manifesto's Tier-B discipline bans raw appearance laundering through string "
        + "passthrough params (*Style / *Class) exposed on Nn* components — they are the entropy channel "
        + "a consumer would use to bypass the prescriptive 'ONE TRUE WAY' contract. NNB043 is the producer-"
        + "side enforcer ('enforced or it doesn't exist'): it scans the NnDesign assembly's own Nn*-"
        + "component parameter declarations (NO producer-path exemption — the rule must flag the design "
        + "system's own params) and fires on any public string-typed [Parameter] whose name ends in 'Style' "
        + "or 'Class', UNLESS the (component, param) pair is a documented Tier-A allow-row in the policy "
        + "table. Seed allow-rows: NnComponentBase.Style / .Class (root escape hatch); NnDialog.TitleClass / "
        + "ContentClass / ActionsClass; NnAppBar.ToolBarClass. The policy table is symbol-based and "
        + "extensible to future non-string Tier-B rows (Variant / Margin / Elevation) as contracts "
        + "formalize. Per-symbol opt-out: an 'nnb043:allow-tierb: <reason>' marker in the param's XML-doc / "
        + "trivia. Project-wide kill-switch: <NnDesignTierBExposureAnalyzerEnabled>false"
        + "</NnDesignTierBExposureAnalyzerEnabled>. Authority: NotNot.BlazorDesign/AGENTS.md → Consumer "
        + "Policy (Tier A/B); NnDesignLayoutContract.VowSpec SPEC-001..003.";

    /// <summary>NNB043 descriptor — Error severity, NnDesign category.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: HelpBase + "nnb043");

    // ── Policy table ──────────────────────────────────────────────────────

    /// <summary>
    /// A Tier-B detector row: the matched parameter SHAPE. <see cref="TypeMatches"/> classifies a property's
    /// resolved type; <see cref="NameMatches"/> classifies its name. A property satisfying BOTH is a Tier-B
    /// candidate (DENY) unless an allow-row exempts it. Structured as a list so future non-string rows
    /// (e.g. raw <c>Variant</c> enum, <c>Margin</c> int) drop in without touching the scan loop.
    /// </summary>
    private sealed class TierBDetector
    {
        public string ShapeName { get; }
        private readonly Func<ITypeSymbol, bool> _typeMatch;
        private readonly Func<string, bool> _nameMatch;

        public TierBDetector(string shapeName, Func<ITypeSymbol, bool> typeMatch, Func<string, bool> nameMatch)
        {
            ShapeName = shapeName;
            _typeMatch = typeMatch;
            _nameMatch = nameMatch;
        }

        public bool TypeMatches(ITypeSymbol type) => _typeMatch(type);
        public bool NameMatches(string name) => _nameMatch(name);
    }

    /// <summary>
    /// The deny-detectors. First (and currently only) row: a <c>string</c>-typed property whose name ends
    /// in <c>Style</c> or <c>Class</c> — the raw appearance-laundering channel. Add future Tier-B rows here
    /// (the scan and allow-list machinery is shape-agnostic).
    /// </summary>
    private static readonly ImmutableArray<TierBDetector> DenyDetectors = ImmutableArray.Create(
        new TierBDetector(
            "string *Style/*Class",
            static type => type.SpecialType == SpecialType.System_String,
            static name => name.EndsWith("Style", StringComparison.Ordinal)
                        || name.EndsWith("Class", StringComparison.Ordinal)));

    /// <summary>
    /// Documented Tier-A allow-rows: <c>(componentTypeName, paramName)</c> pairs that survive the deny
    /// detectors. Keyed on the DECLARING type's simple name + the property name. Per the VowSpec these are
    /// brand-permitted-variation channels (SPEC-002 / SPEC-003), class-only (no raw <c>*Style</c> twin).
    /// </summary>
    private static readonly HashSet<(string Type, string Param)> AllowRows =
        new()
        {
            // SPEC-003 — base-class root-element escape hatch (inherited by every Nn* wrapper).
            ("NnComponentBase", "Style"),
            ("NnComponentBase", "Class"),

            // SPEC-002 — per-region dialog class hooks (genuinely-unbounded per-dialog layout shape).
            ("NnDialog", "TitleClass"),
            ("NnDialog", "ContentClass"),
            ("NnDialog", "ActionsClass"),

            // SPEC-002 — structural inner-toolbar class hook the base Class cannot reach.
            ("NnAppBar", "ToolBarClass"),
        };

    // ── DiagnosticAnalyzer overrides ─────────────────────────────────────

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (IsOptedOut(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
            return;

        if (context.Symbol is not INamedTypeSymbol type)
            return;

        // Gate on Nn* component types. A Razor component compiles to a partial class named after the file
        // (NnDialog, NnAppBar, NnContentSection, …); plain-.cs Nn* components match too. The 'Nn' prefix +
        // uppercase third char is the design-system naming convention (excludes e.g. 'Nnn...' typos, which
        // are out of scope, and non-component helpers are filtered by the [Parameter] requirement below).
        if (!IsNnComponentType(type))
            return;

        // Only the type's OWN declared members — inherited members (e.g. base Style/Class) are reported on
        // their declaring type (NnComponentBase), not re-reported on every derived wrapper.
        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            // Public-facing parameter surface: a Blazor [Parameter]-marked, externally-settable property.
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (!HasParameterAttribute(property))
                continue;

            var detector = MatchDenyDetector(property);
            if (detector == null)
                continue;

            // Allow-row exemption — documented Tier-A brand-permitted-variation channel.
            if (AllowRows.Contains((type.Name, property.Name)))
                continue;

            // Per-symbol opt-out marker in XML-doc / declaring trivia.
            if (HasOptOutMarker(property, context.CancellationToken))
                continue;

            var location = property.Locations.FirstOrDefault() ?? Location.None;
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, location, property.Name, type.Name, detector.ShapeName));
        }
    }

    // ── Matching helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the first deny-detector whose type + name shape the property satisfies, or null when the
    /// property is not a Tier-B candidate.
    /// </summary>
    private static TierBDetector? MatchDenyDetector(IPropertySymbol property)
    {
        foreach (var detector in DenyDetectors)
        {
            if (detector.TypeMatches(property.Type) && detector.NameMatches(property.Name))
                return detector;
        }
        return null;
    }

    /// <summary>
    /// True when the type is an NnDesign component: simple name starts with <c>Nn</c> followed by an
    /// uppercase letter (the design-system convention). Includes <c>NnComponentBase</c> itself so its
    /// base Style/Class are governed (and protected via the allow-rows) at their single declaring site.
    /// </summary>
    private static bool IsNnComponentType(INamedTypeSymbol type)
    {
        var name = type.Name;
        return name.Length >= 3
            && name[0] == 'N'
            && name[1] == 'n'
            && char.IsUpper(name[2]);
    }

    private static bool HasParameterAttribute(IPropertySymbol property)
    {
        foreach (var attr in property.GetAttributes())
        {
            // Match by simple name — Microsoft.AspNetCore.Components.ParameterAttribute (and the
            // CascadingParameterAttribute sibling is intentionally NOT matched: cascading values are not
            // a consumer-facing call-site appearance channel).
            if (string.Equals(attr.AttributeClass?.Name, "ParameterAttribute", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool HasOptOutMarker(IPropertySymbol property, System.Threading.CancellationToken ct)
    {
        // Scan the declaring syntax trivia (XML-doc comments live in leading trivia) for the marker.
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax(ct);
            var triviaText = node.GetLeadingTrivia().ToFullString();
            if (triviaText.IndexOf(OptOutMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool IsOptedOut(AnalyzerConfigOptions options)
    {
        return options.TryGetValue(
                   "build_property.NnDesignTierBExposureAnalyzerEnabled", out var value)
            && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }
}
