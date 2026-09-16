using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

// Namespace is EnvironmentAccess, not Environment: a namespace segment named `Environment` would shadow
// `System.Environment` for every unqualified use inside it, which is a trap for the next author here.
namespace NotNot.Analyzers.Reliability.EnvironmentAccess;

/// <summary>
/// Flags a read of the INHERITED <c>PATH</c> environment variable —
/// <c>Environment.GetEnvironmentVariable("PATH")</c>, or the same call with an explicit
/// <c>EnvironmentVariableTarget.Process</c> — outside the single type marked
/// <c>[NotNot.Bcl.Diagnostics.SystemPathOwner]</c>. A process never re-reads <c>PATH</c> after creation, so
/// a long-lived parent hands it a stale search path for the process's entire life and executable resolution
/// silently fails.
/// </summary>
/// <remarks>
/// Complementary to NN_R008 (direct file APPEND outside the sanctioned writer) on a disjoint axis: NN_R008
/// is a file-I/O concurrency rule, NN_R011 is an environment-staleness rule. Different APIs, different
/// hazards; they can never co-fire.
///
/// FLAGGED (error) — resolution derived from the inherited environment:
/// <code>
/// var path = Environment.GetEnvironmentVariable("PATH");                                   // ← fires
/// var p2   = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process); // ← fires
/// </code>
///
/// ALLOWED (silent):
/// - <c>Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)</c> and the
///   <c>.User</c> target — these read the PERSISTENT store and are current even when this process's copy is
///   stale. They are the FIX this rule steers toward, so flagging them would invert the rule.
/// - Any read inside a type marked <c>[NotNot.Bcl.Diagnostics.SystemPathOwner]</c> — the sanctioned owner
///   legitimately uses the inherited value as a soft fallback when the persistent store is unreadable.
/// - Any other variable name, including <c>PATHEXT</c>: the executable-extension list is effectively
///   immutable on a machine, so the staleness hazard that motivates this rule does not apply to it.
///   Including it would produce findings with no defect behind them.
/// - A non-constant first argument (<c>GetEnvironmentVariable(someVariable)</c>) — the rule cannot prove
///   which variable is read, so it stays silent rather than guessing.
/// - A second argument whose value cannot be resolved at compile time — same conservative principle.
/// - A same-named <c>GetEnvironmentVariable</c> on any type other than <c>System.Environment</c>.
///
/// KNOWN SCOPE BOUNDARY (deliberate, not an oversight): this rule matches ONLY
/// <c>Environment.GetEnvironmentVariable</c>. The sibling accessors <c>Environment.GetEnvironmentVariables()</c>
/// (the whole inherited block, indexable by <c>"PATH"</c>) and
/// <c>Environment.ExpandEnvironmentVariables("%PATH%")</c> read the SAME stale inherited data and are NOT
/// flagged. They are excluded because their legitimate uses — diagnostic dumps, composing a child process's
/// environment — vastly outnumber search-path derivation, so flagging them at Error severity would break
/// correct code. If a PATH-resolution defect ever arrives through one of them, widen this rule deliberately
/// rather than assuming it was already covered.
///
/// DETECTION (perf-conscious): registers on <c>InvocationExpression</c>; a cheap SYNTACTIC name prefilter
/// runs FIRST — accepting both <c>Environment.GetEnvironmentVariable(…)</c> and the bare
/// <c>GetEnvironmentVariable(…)</c> that <c>using static System.Environment;</c> enables — and SemanticModel
/// is touched only after it, to confirm the invoked method's containing type is <c>System.Environment</c>,
/// to constant-evaluate the variable name and the target argument (each resolved through its PARAMETER, so
/// out-of-order named arguments cannot evade the predicate), and to walk the enclosing symbol for the
/// owner-marker exemption.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class InheritedEnvironmentPathReadAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NN_R011";

    private static readonly LocalizableString Title =
        "Inherited PATH read outside the sanctioned system-path owner";

    private static readonly LocalizableString MessageFormat =
        "'{0}' reads the PATH environment variable this process INHERITED at creation. A process never "
        + "re-reads PATH, so a long-lived parent (Explorer, a service host, a shell started before an "
        + "install) hands it a stale search path for the process's whole life and executable resolution "
        + "silently fails. "
        + "Fix: (1) resolve the executable through the application's sanctioned resolver instead of walking "
        + "PATH by hand; "
        + "(2) if a raw search path is genuinely needed, read the PERSISTENT store — "
        + "GetEnvironmentVariable(\"PATH\", EnvironmentVariableTarget.Machine) composed with "
        + "EnvironmentVariableTarget.User; "
        + "(3) if this type IS the single owner of system-path composition, mark it "
        + "[NotNot.Bcl.Diagnostics.SystemPathOwner]; "
        + "(4) #pragma warning disable NN_R011 when the read never drives executable resolution — a "
        + "diagnostic dump, a log line, or a test asserting the owner's fallback — or when the process is "
        + "provably short-lived and started from a fresh environment.";

    private static readonly LocalizableString Description =
        "A process receives its environment from its parent at creation and never re-reads it. A "
        + "long-lived ancestor that started before an install therefore hands every descendant a stale "
        + "PATH indefinitely — on Windows only a sign-out repairs it — so code resolving an executable "
        + "against that inherited value silently stops finding tools installed later or living in a "
        + "directory added to the USER path afterwards. The failure is invisible in review because the "
        + "code is correct in isolation, and it surfaces far from its cause: the caller reports the app "
        + "'not found' or a startup timeout, never a search-path problem. "
        + "Preferred fix: (1) resolve through the application's sanctioned executable resolver rather than "
        + "walking PATH locally; "
        + "(2) when a raw search path is genuinely required, read the persistent per-machine and per-user "
        + "stores via EnvironmentVariableTarget.Machine / EnvironmentVariableTarget.User, which are current "
        + "regardless of what this process inherited; "
        + "(3) if this type is the single sanctioned owner of system-path composition, mark it "
        + "[NotNot.Bcl.Diagnostics.SystemPathOwner] — the inherited value is a legitimate soft fallback "
        + "there when the persistent store is unreadable; "
        + "(4) suppress with #pragma warning disable NN_R011 when the read never drives executable "
        + "resolution — a diagnostic dump, a log line, or a test asserting the owner's fallback behavior — "
        + "or when the process is provably short-lived and started from a freshly composed environment. "
        + "See the NN_R011 ReadMe section for the full rationale.";

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
        customTags: new[] { "Reliability", "Environment", "ProcessLaunch" });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <summary>The BCL accessor this rule inspects.</summary>
    private const string TargetMethodName = "GetEnvironmentVariable";

    /// <summary>The declaring type the invoked method must resolve to.</summary>
    private const string TargetContainingType = "System.Environment";

    /// <summary>The one environment variable whose staleness produces the hazard.</summary>
    private const string GuardedVariableName = "PATH";

    /// <summary>
    /// Marker exempting the sanctioned owner. Matched as simple-name + namespace, so a same-named attribute
    /// declared in an unrelated namespace is deliberately NOT honored.
    /// </summary>
    private const string OwnerMarkerSimpleName = "SystemPathOwnerAttribute";

    /// <inheritdoc cref="OwnerMarkerSimpleName" />
    private const string OwnerMarkerNamespace = "NotNot.Bcl.Diagnostics";

    /// <summary><c>EnvironmentVariableTarget.Process</c> — the inherited, per-process value.</summary>
    private const int EnvironmentVariableTargetProcess = 0;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeInvocation");

        if (context.Node is not InvocationExpressionSyntax invocation) return;

        // Cheap SYNTACTIC prefilter FIRST: bail before touching the SemanticModel unless the invoked
        // simple name matches. BOTH call shapes must be admitted — `Environment.GetEnvironmentVariable(…)`
        // AND the bare `GetEnvironmentVariable(…)` that `using static System.Environment;` enables. They
        // bind to the identical method with identical runtime behavior, so accepting only the qualified
        // form would let mere qualification decide whether the rule fires.
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            _ => null,
        };
        if (invokedName != TargetMethodName) return;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count is not (1 or 2)) return;

        // SemanticModel: the syntactic member name alone is insufficient — a same-named method on an
        // unrelated (or deliberately spoofed) type must not match.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method) return;
        if (method.ContainingType?.ToDisplayString() != TargetContainingType) return;

        // Resolve each argument through its PARAMETER, never its syntax position: a named argument may be
        // written out of order (`GetEnvironmentVariable(target: …, variable: "PATH")`), and reading
        // `arguments[0]` there inspects the target and silently misses the guarded read.
        var nameExpression = AnalyzerArgumentBinding.FindArgumentForParameter(
            method, arguments, parameterOrdinal: 0);
        if (nameExpression is null) return;

        // Only the guarded variable. A non-constant name cannot be proven, so stay silent rather than guess.
        var nameConstant = context.SemanticModel.GetConstantValue(nameExpression, context.CancellationToken);
        if (!nameConstant.HasValue || nameConstant.Value is not string variableName) return;
        if (!string.Equals(variableName, GuardedVariableName, StringComparison.OrdinalIgnoreCase)) return;

        // The two-argument overload is the whole precision story: Machine/User read the PERSISTENT store
        // and are the FIX this rule steers toward, so they must stay silent. Only an explicit Process
        // target reads the inherited value and is therefore equivalent to the one-argument form.
        if (arguments.Count == 2)
        {
            var targetExpression = AnalyzerArgumentBinding.FindArgumentForParameter(
                method, arguments, parameterOrdinal: 1);
            if (targetExpression is null) return;

            var targetConstant = context.SemanticModel.GetConstantValue(
                targetExpression, context.CancellationToken);
            // Unresolvable target ⇒ cannot prove it is the inherited read ⇒ conservative silence.
            if (!targetConstant.HasValue || targetConstant.Value is not int targetValue) return;
            if (targetValue != EnvironmentVariableTargetProcess) return;
        }

        // Exemption: the sanctioned owner legitimately falls back to the inherited value.
        if (IsInsideOwnerMarkedType(context.ContainingSymbol)) return;

        var enclosingTypeName = GetEnclosingTypeName(context.ContainingSymbol);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            enclosingTypeName));
    }

    /// <summary>
    /// Walks up from the invocation's containing symbol through every enclosing type, returning true iff
    /// any of them carries the owner marker. Nested types inherit the enclosing type's exemption, which
    /// keeps a private helper nested inside the owner from tripping the rule.
    /// </summary>
    private static bool IsInsideOwnerMarkedType(ISymbol? containingSymbol)
    {
        for (var symbol = containingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (symbol is not INamedTypeSymbol namedType) continue;

            foreach (var attribute in namedType.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass is null) continue;

                // Compare the simple name and the containing namespace SEPARATELY rather than one rendered
                // string: display formats vary by symbol kind and Roslyn version, and a mismatch there fails
                // OPEN (the marker silently stops exempting). The namespace half is what keeps this strict —
                // a same-named attribute in another namespace still cannot disable the rule.
                if (!string.Equals(attributeClass.Name, OwnerMarkerSimpleName, StringComparison.Ordinal)) continue;

                var attributeNamespace = attributeClass.ContainingNamespace?.ToDisplayString();
                if (string.Equals(attributeNamespace, OwnerMarkerNamespace, StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    /// <summary>The nearest enclosing type's name, used as the message argument.</summary>
    private static string GetEnclosingTypeName(ISymbol? containingSymbol)
    {
        for (var symbol = containingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            if (symbol is INamedTypeSymbol namedType) return namedType.Name;
        }

        return "<unknown>";
    }
}
