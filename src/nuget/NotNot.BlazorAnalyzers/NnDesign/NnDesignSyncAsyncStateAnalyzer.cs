using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NN_NND_ASYNC_001 — NnDesign sync handler must not observe async mixin state.
/// </summary>
/// <remarks>
/// <para>
/// Enforces the <c>sync = fire-and-forget</c> tenet of <c>NnAsyncBoundBehavior&lt;TValue&gt;</c>
/// (NnDesign async-bound mixin v2 R3). Sync user-input handlers fire the immediate visual signal
/// (<c>ValueChanged</c>) on user input; async outcome flows ONLY through
/// <c>OnAsyncChange</c>'s <see cref="System.Threading.Tasks.Task{T}"/> return + the mixin's
/// state-changed callback driving the render-time @markup expressions.
/// </para>
/// <para>
/// Sync handler bodies that observe <c>_behavior.IsInFlight</c> / <c>InDebounce</c> /
/// <c>SpinnerVisible</c> / <c>InlineErrorMessage</c> / <c>LastError</c> race the mixin's own
/// state mutation and produce flicker / stale-read / wrong-state UX. Sync code that does
/// <c>.GetAwaiter().GetResult()</c> on <c>_behavior.NotifyChange</c> / <c>ExecuteAsync</c>
/// blocks the render thread and defeats the entire async pipeline.
/// </para>
/// <para>
/// Three syntactic detection patterns, all gated by namespace-scope filter
/// <c>NotNot.BlazorDesign.*</c> (component DEFINITION code only — consumer call sites are not
/// flagged per Q2 R3 user resolution 2026-05-11):
/// <list type="number">
///   <item>
///     <description>
///     <b>Pattern 1</b>: sync method body or sync lambda reads
///     <c>_behavior.IsInFlight</c> / <c>_behavior.InDebounce</c> /
///     <c>_behavior.SpinnerVisible</c> / <c>_behavior.InlineErrorMessage</c> /
///     <c>_behavior.LastError</c> (also <c>_singleBehavior.*</c> / <c>_multiBehavior.*</c>
///     for NnChipSet dual-mixin).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Pattern 2</b>: <c>.GetAwaiter().GetResult()</c> on <c>_behavior.NotifyChange(...)</c>
///     / <c>_behavior.ExecuteAsync(...)</c> (or single/multi variants).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Pattern 3</b>: sync lambda passed to <c>ValueChanged</c> / <c>OnInput</c> /
///     <c>OnClick</c> argument or assigned to a property/field with one of those names AND
///     captures the <c>_behavior</c> field.
///     </description>
///   </item>
/// </list>
/// Render-time @markup interpolation (computed property getters, <c>BuildRenderTree</c> in
/// .razor-generated code) is ALLOWED — those are legitimate render-time reads driven by the
/// mixin's state-changed callback. Async methods and async lambdas are ALLOWED unconditionally.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnDesignSyncAsyncStateAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for sync handler observing async mixin state.</summary>
    public const string DiagnosticId = "NN_NND_ASYNC_001";

    private const string Category = "NnDesign Convention";

    private const string NnDesignNamespacePrefix = "NotNot.BlazorDesign";

    /// <summary>Mixin field names recognized by the analyzer (single + dual-mixin variants).</summary>
    private static readonly HashSet<string> MixinFieldNames = new(StringComparer.Ordinal)
    {
        "_behavior",
        "_singleBehavior",
        "_multiBehavior",
    };

    /// <summary>Mixin state-property names recognized by Pattern 1.</summary>
    private static readonly HashSet<string> MixinStateMemberNames = new(StringComparer.Ordinal)
    {
        "IsInFlight",
        "InDebounce",
        "SpinnerVisible",
        "InlineErrorMessage",
        "LastError",
    };

    /// <summary>Mixin async-method names recognized by Pattern 2 receiver-chain check.</summary>
    private static readonly HashSet<string> MixinAsyncMethodNames = new(StringComparer.Ordinal)
    {
        "NotifyChange",
        "ExecuteAsync",
    };

    /// <summary>Sync-callback parameter / property / field names recognized by Pattern 3.</summary>
    private static readonly HashSet<string> SyncCallbackNames = new(StringComparer.Ordinal)
    {
        "ValueChanged",
        "OnInput",
        "OnClick",
    };

    /// <summary>NN_NND_ASYNC_001 descriptor — Warning severity by default (Phase 8 task 8 may demote).</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "NnDesign sync handler must not observe async mixin state",
        messageFormat: "Sync handler in {0} reads async mixin state '{1}' — violates NnDesign sync=fire-and-forget tenet. Move async observation to OnAsyncChange callback or async method.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Components in NotNot.BlazorDesign that wire NnAsyncBoundBehavior<T> as their async-write " +
            "state machine MUST keep sync user-input handlers (ValueChanged, OnInput, OnClick) " +
            "fire-and-forget. Sync handler bodies that read _behavior.IsInFlight / InDebounce / " +
            "SpinnerVisible / InlineErrorMessage / LastError race the mixin's own state mutation. " +
            "Move async observation to the OnAsyncChange callback or an async method. Render-time " +
            "@markup interpolation and computed property getters are allowed (those reads are driven " +
            "by the mixin's state-changed callback). Authority: NotNot.BlazorDesign mixin v2 R3 TDD " +
            "Phase 8 (R1.11 closure).",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // Skip generated code — .razor compiles to generated C# whose BuildRenderTree method
        // contains render-time @markup interpolation that DOES legitimately read _behavior.X.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Pattern 1 — sync handler reads mixin state property.
        context.RegisterSyntaxNodeAction(AnalyzePattern1MemberAccess, SyntaxKind.SimpleMemberAccessExpression);

        // Pattern 2 — sync handler blocks on Task via .GetAwaiter().GetResult().
        context.RegisterSyntaxNodeAction(AnalyzePattern2GetAwaiterGetResult, SyntaxKind.InvocationExpression);

        // Pattern 3 — sync lambda registered as sync-callback captures _behavior.
        context.RegisterSyntaxNodeAction(AnalyzePattern3SyncLambda,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
    }

    // ── Pattern 1 — sync method/lambda reads `_behavior` mixin state ──────────────────────

    private static void AnalyzePattern1MemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Match `<mixin-field>.<state-property>` — receiver may be a bare IdentifierName OR a
        // null-forgiving suppression (`_behavior!.X`). Unwrap suppression to reach the inner
        // IdentifierName; member name must be in MixinStateMemberNames.
        var receiverName = TryGetMixinReceiverName(memberAccess.Expression);
        if (receiverName is null)
            return;
        var memberName = memberAccess.Name.Identifier.ValueText;
        if (!MixinStateMemberNames.Contains(memberName))
            return;

        // Namespace-scope filter: containing type must be in NotNot.BlazorDesign.*
        if (!IsInNnDesignNamespace(memberAccess, context.SemanticModel, context.CancellationToken))
            return;

        // Determine the enclosing execution context.
        // - Async method / async lambda → ALLOWED.
        // - Method returning Task / Task<T> / ValueTask / ValueTask<T> → ALLOWED (async without keyword).
        // - Property getter (computed property) → ALLOWED (render-time read).
        // - BuildRenderTree method (Razor-generated) → ALLOWED (render-time read).
        // - Sync method body / sync lambda → REPORT.
        var disposition = ClassifyEnclosingContext(memberAccess);
        if (disposition != EnclosingDisposition.SyncExecution)
            return;

        var enclosingDescription = GetEnclosingDescription(memberAccess);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            memberAccess.GetLocation(),
            enclosingDescription,
            $"{receiverName}.{memberName}"));
    }

    // ── Pattern 2 — `.GetAwaiter().GetResult()` on mixin async method ─────────────────────

    private static void AnalyzePattern2GetAwaiterGetResult(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Target shape: <expr>.GetResult()
        if (invocation.Expression is not MemberAccessExpressionSyntax getResultAccess)
            return;
        if (getResultAccess.Name.Identifier.ValueText != "GetResult")
            return;
        if (invocation.ArgumentList.Arguments.Count != 0)
            return;

        // Receiver of GetResult() must be: <expr>.GetAwaiter()
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation)
            return;
        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess)
            return;
        if (getAwaiterAccess.Name.Identifier.ValueText != "GetAwaiter")
            return;
        if (getAwaiterInvocation.ArgumentList.Arguments.Count != 0)
            return;

        // Walk further: the receiver of GetAwaiter() should be an invocation of a mixin
        // async method: <mixinField>.NotifyChange(...) or <mixinField>.ExecuteAsync(...).
        // Receiver of the mixin call MAY be a bare IdentifierName OR a null-forgiving
        // suppression `<mixinField>!.<method>` (PostfixUnaryExpressionSyntax wrapping the
        // IdentifierName) — both are unwrapped via TryGetMixinReceiverName.
        var taskProducer = getAwaiterAccess.Expression;
        if (taskProducer is not InvocationExpressionSyntax mixinCallInvocation)
            return;
        if (mixinCallInvocation.Expression is not MemberAccessExpressionSyntax mixinMemberAccess)
            return;

        var receiverName = TryGetMixinReceiverName(mixinMemberAccess.Expression);
        if (receiverName is null)
            return;
        var methodName = mixinMemberAccess.Name.Identifier.ValueText;
        if (!MixinAsyncMethodNames.Contains(methodName))
            return;

        // Namespace-scope filter.
        if (!IsInNnDesignNamespace(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // For Pattern 2, the very act of blocking on the Task via GetAwaiter().GetResult()
        // defeats the async pipeline regardless of enclosing context. Still, async methods
        // SHOULD use `await` instead — report unconditionally inside NnDesign.
        var enclosingDescription = GetEnclosingDescription(invocation);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            enclosingDescription,
            $"{receiverName}.{methodName}().GetAwaiter().GetResult()"));
    }

    // ── Pattern 3 — sync lambda registered as sync-callback captures `_behavior` ──────────

    private static void AnalyzePattern3SyncLambda(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;

        // The lambda/anonymous-method must be SYNC — no `async` keyword.
        if (IsAsyncLambdaOrAnonymousMethod(node))
            return;

        // The lambda must be in a position that registers it as a sync callback —
        // ValueChanged / OnInput / OnClick assignment target, named-argument, or
        // a positional argument to a method whose parameter name matches.
        if (!IsRegisteredAsSyncCallback(node))
            return;

        // The lambda body must contain a reference to a mixin field (Pattern 3 capture).
        var lambdaBody = GetLambdaBody(node);
        if (lambdaBody is null)
            return;
        var capturedMixinField = FindCapturedMixinFieldName(lambdaBody);
        if (capturedMixinField is null)
            return;

        // Namespace-scope filter.
        if (!IsInNnDesignNamespace(node, context.SemanticModel, context.CancellationToken))
            return;

        var enclosingDescription = GetEnclosingDescription(node);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            node.GetLocation(),
            enclosingDescription,
            $"sync lambda captures '{capturedMixinField}'"));
    }

    // ── Enclosing-context classification (Pattern 1) ──────────────────────────────────────

    private enum EnclosingDisposition
    {
        AllowedAsync,
        AllowedRenderTime,
        SyncExecution,
    }

    private static EnclosingDisposition ClassifyEnclosingContext(SyntaxNode node)
    {
        // Walk up the syntax tree to find the nearest enclosing executable scope.
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                // Computed property getter — render-time read driven by stateChanged callback.
                case AccessorDeclarationSyntax accessor when accessor.IsKind(SyntaxKind.GetAccessorDeclaration):
                    return EnclosingDisposition.AllowedRenderTime;

                // Arrow expression body on property — `private bool EffectiveDisabled => ...`
                case ArrowExpressionClauseSyntax arrow when arrow.Parent is PropertyDeclarationSyntax:
                    return EnclosingDisposition.AllowedRenderTime;

                // Methods — async modifier OR Task/ValueTask return type → ALLOWED.
                case MethodDeclarationSyntax method:
                    // Razor-generated render method.
                    if (method.Identifier.ValueText == "BuildRenderTree")
                        return EnclosingDisposition.AllowedRenderTime;
                    if (HasAsyncModifier(method.Modifiers) || IsAsyncReturnType(method.ReturnType))
                        return EnclosingDisposition.AllowedAsync;
                    return EnclosingDisposition.SyncExecution;

                // Local function — same rules as method.
                case LocalFunctionStatementSyntax localFn:
                    if (HasAsyncModifier(localFn.Modifiers) || IsAsyncReturnType(localFn.ReturnType))
                        return EnclosingDisposition.AllowedAsync;
                    return EnclosingDisposition.SyncExecution;

                // Lambdas — async modifier check.
                case ParenthesizedLambdaExpressionSyntax plambda:
                    if (HasAsyncModifier(plambda.Modifiers))
                        return EnclosingDisposition.AllowedAsync;
                    return EnclosingDisposition.SyncExecution;
                case SimpleLambdaExpressionSyntax slambda:
                    if (HasAsyncModifier(slambda.Modifiers))
                        return EnclosingDisposition.AllowedAsync;
                    return EnclosingDisposition.SyncExecution;
                case AnonymousMethodExpressionSyntax anonMethod:
                    if (HasAsyncModifier(anonMethod.Modifiers))
                        return EnclosingDisposition.AllowedAsync;
                    return EnclosingDisposition.SyncExecution;

                // Field/constructor initializers — render-time (executed during initialization,
                // safe by construction since mixin isn't constructed yet).
                case ConstructorDeclarationSyntax:
                    return EnclosingDisposition.AllowedAsync; // ctor body — safe by ordering
                case EqualsValueClauseSyntax when current.Parent is VariableDeclaratorSyntax:
                    return EnclosingDisposition.AllowedRenderTime;
            }
        }

        // Default: outside any executable scope (e.g., top-level statement) — treat as sync.
        return EnclosingDisposition.SyncExecution;
    }

    private static string GetEnclosingDescription(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return $"method '{method.Identifier.ValueText}'";
                case LocalFunctionStatementSyntax localFn:
                    return $"local function '{localFn.Identifier.ValueText}'";
                case ParenthesizedLambdaExpressionSyntax:
                case SimpleLambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    return "sync lambda";
                case ConstructorDeclarationSyntax ctor:
                    return $"constructor '{ctor.Identifier.ValueText}'";
            }
        }
        return "sync context";
    }

    // ── Pattern 3 helpers ─────────────────────────────────────────────────────────────────

    private static bool IsAsyncLambdaOrAnonymousMethod(SyntaxNode node)
    {
        return node switch
        {
            ParenthesizedLambdaExpressionSyntax plambda => HasAsyncModifier(plambda.Modifiers),
            SimpleLambdaExpressionSyntax slambda => HasAsyncModifier(slambda.Modifiers),
            AnonymousMethodExpressionSyntax anonMethod => HasAsyncModifier(anonMethod.Modifiers),
            _ => false,
        };
    }

    private static SyntaxNode? GetLambdaBody(SyntaxNode node)
    {
        return node switch
        {
            ParenthesizedLambdaExpressionSyntax plambda => plambda.Body,
            SimpleLambdaExpressionSyntax slambda => slambda.Body,
            AnonymousMethodExpressionSyntax anonMethod => anonMethod.Body,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the name of the first mixin field referenced inside the lambda body
    /// (purely syntactic — matches IdentifierNameSyntax whose text is in MixinFieldNames).
    /// </summary>
    private static string? FindCapturedMixinFieldName(SyntaxNode body)
    {
        foreach (var ident in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = ident.Identifier.ValueText;
            if (MixinFieldNames.Contains(name))
                return name;
        }
        return null;
    }

    /// <summary>
    /// Returns true when the lambda/anonymous-method is registered to a sync-callback name —
    /// either as named-argument <c>ValueChanged: lambda</c>, attribute-syntax-style assignment,
    /// member-initializer for a property of that name, or positional argument to a parameter
    /// whose name matches. Heuristic: walk parent chain; check argument context, assignment
    /// context, and equals-value clause context.
    /// </summary>
    private static bool IsRegisteredAsSyncCallback(SyntaxNode node)
    {
        var parent = node.Parent;

        // Direct equals-value clause: `Action onClick = () => { ... };` where the target
        // variable name is a sync-callback name.
        if (parent is EqualsValueClauseSyntax eqv)
        {
            // Variable declarator inside variable declaration
            if (eqv.Parent is VariableDeclaratorSyntax vd && SyncCallbackNames.Contains(vd.Identifier.ValueText))
                return true;
        }

        // Argument context: `Callback(ValueChanged: () => { ... })` or positional.
        if (parent is ArgumentSyntax arg)
        {
            // Named argument: `ValueChanged: lambda` → parameter name = arg.NameColon.Name.Identifier.ValueText
            if (arg.NameColon is { } nameColon)
            {
                if (SyncCallbackNames.Contains(nameColon.Name.Identifier.ValueText))
                    return true;
            }
            // Positional argument: skip semantic resolution; this is the conservative path.
            // (A full semantic check would resolve the parameter from the enclosing invocation —
            // intentionally deferred per spec: "if pure-syntactic detection is impractical,
            // use SemanticModel". For Phase 8, named-argument and assignment-target checks
            // are the high-signal cases; positional in c# is rare for callback registration.)
        }

        // Assignment: `this.ValueChanged = () => { ... };` or `instance.OnClick = lambda`.
        if (parent is AssignmentExpressionSyntax assignment && assignment.Right == node)
        {
            string? targetName = assignment.Left switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText,
                MemberAccessExpressionSyntax mae => mae.Name.Identifier.ValueText,
                _ => null,
            };
            if (targetName != null && SyncCallbackNames.Contains(targetName))
                return true;
        }

        // Attribute / object-initializer member assignment:
        // `new Foo { ValueChanged = () => { ... } }` or attribute-style.
        if (parent is AttributeArgumentSyntax attrArg)
        {
            if (attrArg.NameEquals is { } nameEquals
                && SyncCallbackNames.Contains(nameEquals.Name.Identifier.ValueText))
                return true;
        }

        return false;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the mixin field name (e.g. <c>_behavior</c>) when <paramref name="expression"/>
    /// resolves to a recognized mixin receiver — either a bare <see cref="IdentifierNameSyntax"/>
    /// or a null-forgiving suppression (<c>_behavior!</c>) wrapping one. Returns null otherwise.
    /// </summary>
    private static string? TryGetMixinReceiverName(ExpressionSyntax expression)
    {
        // Unwrap null-forgiving operator `!` (PostfixUnaryExpression with `!` token).
        if (expression is PostfixUnaryExpressionSyntax postfix
            && postfix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            expression = postfix.Operand;
        }

        if (expression is IdentifierNameSyntax ident)
        {
            var name = ident.Identifier.ValueText;
            if (MixinFieldNames.Contains(name))
                return name;
        }
        return null;
    }

    private static bool HasAsyncModifier(SyntaxTokenList modifiers)
    {
        foreach (var token in modifiers)
        {
            if (token.IsKind(SyntaxKind.AsyncKeyword))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="returnType"/> is <c>Task</c>, <c>Task&lt;T&gt;</c>,
    /// <c>ValueTask</c>, or <c>ValueTask&lt;T&gt;</c> by simple name (no semantic resolution
    /// — matches the rightmost identifier in the type expression).
    /// </summary>
    private static bool IsAsyncReturnType(TypeSyntax? returnType)
    {
        if (returnType is null)
            return false;

        // Unwrap generic / qualified name.
        var name = returnType switch
        {
            GenericNameSyntax gen => gen.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax qn => GetRightmostName(qn),
            _ => null,
        };
        return name is "Task" or "ValueTask";
    }

    private static string? GetRightmostName(QualifiedNameSyntax qn)
    {
        return qn.Right switch
        {
            GenericNameSyntax gen => gen.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null,
        };
    }

    /// <summary>
    /// Returns true when the syntax node's containing type is in the
    /// <c>NotNot.BlazorDesign</c> namespace (or any sub-namespace).
    /// </summary>
    private static bool IsInNnDesignNamespace(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // Walk up to the containing TypeDeclarationSyntax.
        var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null)
            return false;

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
        if (typeSymbol is null)
            return false;

        var ns = typeSymbol.ContainingNamespace?.ToDisplayString();
        return ns is not null
            && (ns == NnDesignNamespacePrefix
                || (ns.StartsWith(NnDesignNamespacePrefix, StringComparison.Ordinal)
                    && ns.Length > NnDesignNamespacePrefix.Length
                    && ns[NnDesignNamespacePrefix.Length] == '.'));
    }
}
