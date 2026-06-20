using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NN_NND_ASYNC_002 — an <c>NnAsyncBoundComponentBase&lt;TValue&gt;</c> subclass that overrides a
/// lifecycle method the base ITSELF implements must call <c>base.{Method}(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>NnAsyncBoundComponentBase&lt;TValue&gt;</c> performs essential per-lifecycle bookkeeping:
/// <c>OnInitialized</c> constructs the <c>Behavior</c> save machine and seeds the
/// <c>_lastCommitted</c> self-coerce-echo sidecar; <c>OnParametersSet</c> refreshes
/// <c>_lastCommitted</c> from genuine external parameter changes; <c>Dispose</c> disposes the
/// <c>Behavior</c>. A subclass override that omits the <c>base</c> call silently disables that
/// work — e.g. <c>_lastCommitted</c> never refreshes (stale self-coerce-echo discriminator) or the
/// <c>Behavior</c> is never constructed (no save machine). Authority: the trio-rollout P2 finding
/// (commit 293aaf72) — NnChipSetSingle/Multi shipped this latent override-without-base until
/// peer-review caught it.
/// </para>
/// <para>
/// <b>Why SEMANTIC, not syntactic</b>: a plain <c>ComponentBase</c> subclass overriding
/// <c>OnParametersSet</c> without <c>base</c> is FINE (ComponentBase's impl is a no-op). The defect
/// is specific to <c>NnAsyncBoundComponentBase</c>, whose lifecycle methods carry real work. The
/// detection therefore requires the override's <c>OverriddenMethod</c> chain to contain a link whose
/// <c>ContainingType.OriginalDefinition</c> is <c>NnAsyncBoundComponentBase`1</c> AND is non-abstract
/// (the base provides a real impl). This single check subsumes both the "is-a-subclass" and
/// "base-has-work" conditions precisely, and auto-excludes:
/// <list type="bullet">
///   <item>plain <c>ComponentBase</c> overrides (no NnAsyncBoundComponentBase link in the chain);</item>
///   <item>methods the base does NOT implement, e.g. <c>OnAfterRender</c> (no link);</item>
///   <item>the Razor-compiler-generated <c>BuildRenderTree</c> override (its base is
///     <c>ComponentBase</c>, not <c>NnAsyncBoundComponentBase</c>).</item>
/// </list>
/// </para>
/// <para>
/// <b>Generated-code analysis (DIVERGES from the NN_NND_ASYNC_001 sibling)</b>: the most common
/// defect form lives in <c>.razor</c> <c>@code</c> blocks, which compile to Razor-GENERATED C#
/// (<c>.g.cs</c>). This analyzer therefore uses
/// <c>GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics</c> — without
/// it, the analyzer would MISS the override-without-base defect that appears in component
/// <c>@code</c>. The precise base-chain check (above) is the false-positive guard against the
/// Razor-emitted <c>BuildRenderTree</c> that <c>Analyze</c> now also surfaces.
/// </para>
/// <para>
/// <b>Per-compilation caching</b>: <see cref="Initialize"/> resolves the open-generic
/// <c>NnAsyncBoundComponentBase`1</c> symbol once via
/// <see cref="AnalysisContext.RegisterCompilationStartAction"/>. If the type isn't referenced by the
/// compilation, syntax-node registration is skipped entirely (no subclass can exist).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnAsyncBoundLifecycleBaseCallAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID — NnAsyncBoundComponentBase lifecycle override must call base.</summary>
    public const string DiagnosticId = "NN_NND_ASYNC_002";

    private const string Category = "NnDesign Convention";

    private const string BaseTypeMetadataName =
        "NotNot.BlazorDesign.NnDesign.AsyncBound.NnAsyncBoundComponentBase`1";

    /// <summary>NN_NND_ASYNC_002 descriptor — Warning severity (matches the NN_NND_ASYNC_001 sibling).</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "NnAsyncBoundComponentBase subclass lifecycle override must call base",
        messageFormat:
            "Override of '{0}' omits 'base.{0}(...)' — the NnAsyncBoundComponentBase base performs " +
            "required lifecycle bookkeeping that is silently skipped. Call 'base.{0}(...)'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "NnAsyncBoundComponentBase<TValue> performs essential per-lifecycle bookkeeping: " +
            "OnInitialized constructs the Behavior save machine and seeds the _lastCommitted " +
            "self-coerce-echo sidecar; OnParametersSet refreshes _lastCommitted from genuine external " +
            "parameter changes; Dispose disposes the Behavior. A subclass override that omits the base " +
            "call freezes that work (e.g. _lastCommitted never refreshes -> stale self-coerce-echo " +
            "discriminator; Behavior never constructed -> no save machine). Call base.{Method}(...) " +
            "(conventionally the first statement). Only suppress (#pragma warning disable " +
            "NN_NND_ASYNC_002) if the base bookkeeping is genuinely undesired — rare. Authority: " +
            "trio-rollout P2 finding (commit 293aaf72) — NnChipSetSingle/Multi shipped this latent " +
            "until peer-review caught it.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // DIVERGES from NN_NND_ASYNC_001: the most common defect lives in .razor @code blocks, which
        // compile to Razor-GENERATED C#. ANALYZE generated trees so the override-without-base defect
        // there is caught. The precise base-chain check below is the FP guard against the
        // Razor-emitted BuildRenderTree override that Analyze also surfaces.
        //
        // ACCEPTED LIMITATION: Analyze surfaces ALL generated code, not just Razor `.g.cs`. A
        // non-Razor source generator emitting an NnAsyncBoundComponentBase subclass with a
        // base-omitting lifecycle override would also fire (and the consumer cannot edit generated
        // source — only #pragma / .editorconfig). This is accepted: no such generator exists, and a
        // `.razor.g.cs` path-gate would be fragile (filepath coupling). See README NN_NND_ASYNC_002.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        // Resolve the open-generic NnAsyncBoundComponentBase<T> base symbol once per compilation.
        // If the type isn't referenced, no subclass can exist — skip registration entirely.
        context.RegisterCompilationStartAction(compStart =>
        {
            var baseType = compStart.Compilation.GetTypeByMetadataName(BaseTypeMetadataName);
            if (baseType is null)
                return;

            compStart.RegisterSyntaxNodeAction(
                ctx => AnalyzeMethodDeclaration(ctx, baseType),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethodDeclaration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol baseType)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;

        // Cheap syntax pre-screen: must carry the `override` modifier.
        if (!HasOverrideModifier(methodDecl.Modifiers))
            return;

        // Resolve the method symbol and walk its OverriddenMethod chain. Require a link whose
        // ContainingType.OriginalDefinition == NnAsyncBoundComponentBase`1 AND is non-abstract
        // (the base provides a real impl that must run). This SINGLE check subsumes both the
        // "is-a-subclass" and "base-has-work" conditions and excludes plain ComponentBase overrides
        // + base-unimplemented methods (OnAfterRender) + the Razor-emitted BuildRenderTree.
        if (context.SemanticModel.GetDeclaredSymbol(methodDecl, context.CancellationToken)
            is not IMethodSymbol methodSymbol)
            return;
        if (!OverridesNonAbstractBaseMethod(methodSymbol, baseType))
            return;

        // Scan the override body for a `base.{methodName}(...)` invocation. Presence-based
        // (conditional / non-first-statement still counts) — conservative against false positives.
        if (BodyContainsBaseCall(methodDecl, methodSymbol.Name))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            methodDecl.Identifier.GetLocation(),
            methodSymbol.Name));
    }

    /// <summary>
    /// True when <paramref name="methodSymbol"/>'s <see cref="IMethodSymbol.OverriddenMethod"/> chain
    /// contains a link whose containing type's <see cref="ISymbol.OriginalDefinition"/> equals
    /// <paramref name="baseType"/> (the open-generic <c>NnAsyncBoundComponentBase`1</c>) AND that link
    /// is non-abstract (the base supplies a real impl). Walking the FULL chain (not just the immediate
    /// override) handles an intermediate subclass that itself overrode-and-called-base: the deepest
    /// link still resolves to the NnAsyncBoundComponentBase impl.
    /// </summary>
    /// <remarks>
    /// SOUNDNESS INVARIANT (latent footgun): the Analyze-generated-code decision in
    /// <see cref="Initialize"/> is FP-safe ONLY because NnAsyncBoundComponentBase (and any
    /// intermediate base in the chain) does NOT override any Razor-compiler-SYNTHESIZED member —
    /// notably <c>BuildRenderTree</c> (and <c>SetParametersAsync</c>). The Razor compiler emits an
    /// override of such members into EVERY component's `.g.cs` with no <c>base.X()</c> call. If the
    /// base ever overrode one, this presence-based chain match would resolve that link and the
    /// analyzer would FALSE-POSITIVE on every generated component. The N4/N5 fixtures pin this
    /// boundary; a matching invariant comment lives on NnAsyncBoundComponentBase's lifecycle region.
    /// </remarks>
    private static bool OverridesNonAbstractBaseMethod(IMethodSymbol methodSymbol, INamedTypeSymbol baseType)
    {
        for (var overridden = methodSymbol.OverriddenMethod;
             overridden is not null;
             overridden = overridden.OverriddenMethod)
        {
            var containing = overridden.ContainingType;
            if (containing is not null
                && SymbolEqualityComparer.Default.Equals(containing.OriginalDefinition, baseType)
                && !overridden.IsAbstract)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when the method's body (block OR expression-bodied arrow) contains an invocation of the
    /// form <c>base.{methodName}(...)</c>. Expression body <c>=&gt; base.OnParametersSet()</c> counts;
    /// an <c>await base.{methodName}Async()</c> counts (the method name still matches the override's
    /// own name). Presence-based: a base call inside a conditional still satisfies the rule.
    /// </summary>
    private static bool BodyContainsBaseCall(MethodDeclarationSyntax methodDecl, string methodName)
    {
        SyntaxNode? body = (SyntaxNode?)methodDecl.Body ?? methodDecl.ExpressionBody;
        if (body is null)
            return true; // No body (abstract/partial-defn shape) — nothing to flag.

        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Expression is BaseExpressionSyntax
                && memberAccess.Name.Identifier.ValueText == methodName)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasOverrideModifier(SyntaxTokenList modifiers)
    {
        foreach (var token in modifiers)
        {
            if (token.IsKind(SyntaxKind.OverrideKeyword))
                return true;
        }
        return false;
    }
}
