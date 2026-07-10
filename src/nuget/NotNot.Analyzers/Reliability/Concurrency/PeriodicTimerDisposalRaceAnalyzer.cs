using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Reliability.Concurrency;

/// <summary>
/// Flags a graceful-shutdown crash class: a FIELD/PROPERTY <c>System.Threading.PeriodicTimer</c> that is BOTH
/// polled via <c>WaitForNextTickAsync(...)</c> AND disposed inside a disposal method
/// (<c>Dispose()</c> / <c>Dispose(bool)</c> / <c>DisposeAsync()</c>). <c>WaitForNextTickAsync</c> is backed by a
/// single-consumer <c>IValueTaskSource</c>: a tick firing at the same instant as EITHER the CancellationToken
/// cancel OR <c>_timer.Dispose()</c> tears that shared source and throws <c>InvalidOperationException</c> from
/// the <c>while (await ...)</c> loop condition — OUTSIDE any per-tick try/catch — faulting the loop task,
/// escaping an OCE-only catch, and aborting the process (SIGABRT) on graceful shutdown. Passing a
/// CancellationToken does NOT prevent it.
/// </summary>
/// <remarks>
/// Complementary to NN_R001/NN_R002 (Task-await) and NN_R007/NN_R008 (atomic-file-write) on a disjoint axis —
/// NN_R009 is the timer-lifecycle concurrency-reliability rule; gap-free with the existing NN_R matrix.
///
/// FLAGGED (error) — a field/property PeriodicTimer polled AND disposed in a disposal method:
/// <code>
/// private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1)); // ← fires at the field decl
/// // ...
/// while (await _timer.WaitForNextTickAsync(ct)) { }   // condition (b): polled
/// // ...
/// public async ValueTask DisposeAsync() { _timer.Dispose(); }  // condition (c): disposed in a disposal method
/// </code>
///
/// ALLOWED (silent):
/// - A <c>using var</c> / local <c>PeriodicTimer</c> disposed by its scope AFTER the loop exits — the SAFE
///   pattern (no active wait at dispose). A local is never a field/property member, so never collected.
/// - The canonical fix: a per-iteration <c>await Task.Delay(interval, token)</c> loop with no PeriodicTimer.
/// - A field/property PeriodicTimer that is waited-on but NEVER disposed (condition (c) unmet), OR one that is
///   disposed but NEVER waited-on (condition (b) unmet) — neither is the fire-vs-terminate race.
///
/// DETECTION (semantic, type-level): registers on <c>SymbolKind.NamedType</c>. Resolves
/// <c>System.Threading.PeriodicTimer</c> via <c>Compilation.GetTypeByMetadataName</c>, collects the type's
/// PeriodicTimer-typed fields/properties, then walks the type's declaration syntax for <c>WaitForNextTickAsync</c>
/// and <c>Dispose</c> invocations whose receiver (via SemanticModel) resolves to a collected member. Reports at
/// the field/property declaration; <c>{0}</c> = the member name.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PeriodicTimerDisposalRaceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NN_R009";

    private static readonly LocalizableString Title =
        "Field PeriodicTimer disposed while WaitForNextTickAsync may be pending risks a shutdown-race exception";

    private static readonly LocalizableString MessageFormat =
        "PeriodicTimer field '{0}' is polled via WaitForNextTickAsync and disposed in a disposal method — "
        + "a tick firing at the same instant as dispose/cancel tears its single-consumer IValueTaskSource "
        + "(InvalidOperationException) on graceful shutdown. "
        + "Fix: replace the PeriodicTimer poll loop with a per-iteration 'await Task.Delay(interval, token)' loop "
        + "(atomic TrySetResult/TrySetCanceled, race-safe), and remove the timer field + its Dispose(). "
        + "Suppress with '#pragma warning disable NN_R009' only if disposal provably never races a pending wait.";

    private static readonly LocalizableString Description =
        "PeriodicTimer.WaitForNextTickAsync is backed by a single-consumer IValueTaskSource: a tick firing at the "
        + "same instant as EITHER the CancellationToken cancel OR _timer.Dispose() tears that shared source and "
        + "throws InvalidOperationException from the `while (await ...)` loop condition — outside any per-tick "
        + "try/catch — which faults the loop task, escapes an OperationCanceledException-only catch, and aborts the "
        + "process (SIGABRT) on graceful shutdown. Passing a CancellationToken does NOT prevent it. "
        + "This fires when a field/property PeriodicTimer is BOTH polled via WaitForNextTickAsync AND disposed "
        + "inside a disposal method (Dispose/Dispose(bool)/DisposeAsync); a `using var` local PeriodicTimer "
        + "disposed AFTER its loop exits is the safe pattern and is not flagged. "
        + "Preferred fix (in order): "
        + "(1) replace the PeriodicTimer poll loop with a per-iteration `await Task.Delay(interval, token)` loop — "
        + "the cheapest correct and strictly safer shape (atomic TrySetResult/TrySetCanceled) — and delete the timer "
        + "field plus its Dispose(); "
        + "(2) if a timer is required, use a `using var` local disposed AFTER the loop exits, so no wait is active "
        + "at dispose; "
        + "(3) #pragma warning disable NN_R009 only when disposal provably can never race a pending wait. "
        + "See reference commits 2ce203f8 / cd3a9ad5 and the canonical good shape at "
        + "Novaleaf.VibeOverwatch.Server/.../VowPtyMetadataPollService.cs.";

    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
        customTags: new[] { "Concurrency", "Reliability", "Lifetime" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeNamedType");

        if (context.Symbol is not INamedTypeSymbol namedType) return;
        if (namedType.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return;

        // Resolve the PeriodicTimer type; absent (unreferenced) → nothing to flag.
        var periodicTimerType = context.Compilation.GetTypeByMetadataName("System.Threading.PeriodicTimer");
        if (periodicTimerType is null) return;

        // (a) Collect the type's fields/properties whose type is System.Threading.PeriodicTimer.
        var candidates = new List<ISymbol>();
        foreach (var member in namedType.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field
                    when SymbolEqualityComparer.Default.Equals(field.Type, periodicTimerType):
                    candidates.Add(field);
                    break;
                case IPropertySymbol prop
                    when SymbolEqualityComparer.Default.Equals(prop.Type, periodicTimerType):
                    candidates.Add(prop);
                    break;
            }
        }

        if (candidates.Count == 0) return;

        // Walk the type's declaration syntax for the two decisive invocations, resolving receivers semantically.
        var waited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);            // condition (b)
        var disposedInDisposal = new HashSet<ISymbol>(SymbolEqualityComparer.Default); // condition (c)

        foreach (var syntaxRef in namedType.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(context.CancellationToken) is not TypeDeclarationSyntax typeDecl) continue;
            var model = context.Compilation.GetSemanticModel(typeDecl.SyntaxTree);

            foreach (var invocation in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;
                var calledName = memberAccess.Name.Identifier.ValueText;
                if (calledName != "WaitForNextTickAsync" && calledName != "Dispose") continue;

                // Resolve the RECEIVER (e.g. `_timer` in `_timer.WaitForNextTickAsync(ct)`,
                // or `this._timer`) to a collected field/property symbol.
                var receiverSymbol = model.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol;
                if (receiverSymbol is null) continue;

                var match = candidates.FirstOrDefault(
                    c => SymbolEqualityComparer.Default.Equals(c, receiverSymbol));
                if (match is null) continue;

                if (calledName == "WaitForNextTickAsync")
                {
                    waited.Add(match);
                }
                else if (IsInsideDisposalMethod(invocation))
                {
                    disposedInDisposal.Add(match);
                }
            }
        }

        // Report each member satisfying ALL THREE conditions, at its declaration.
        foreach (var candidate in candidates)
        {
            if (!waited.Contains(candidate) || !disposedInDisposal.Contains(candidate)) continue;

            var location = candidate.Locations.FirstOrDefault(l => l.IsInSource);
            if (location is null) continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, candidate.Name));
        }
    }

    /// <summary>
    /// True iff <paramref name="node"/>'s nearest enclosing method declaration is a disposal method —
    /// <c>Dispose()</c>, <c>Dispose(bool)</c>, or <c>DisposeAsync()</c> (matched by identifier name).
    /// </summary>
    private static bool IsInsideDisposalMethod(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is MethodDeclarationSyntax method)
            {
                var name = method.Identifier.ValueText;
                return name is "Dispose" or "DisposeAsync";
            }
        }

        return false;
    }
}
