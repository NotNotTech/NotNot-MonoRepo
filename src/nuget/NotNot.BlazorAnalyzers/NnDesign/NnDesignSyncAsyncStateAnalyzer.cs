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
///     <c>{receiver}.IsInFlight</c> / <c>{receiver}.InDebounce</c> /
///     <c>{receiver}.SpinnerVisible</c> / <c>{receiver}.InlineErrorMessage</c> /
///     <c>{receiver}.LastError</c>, where <c>{receiver}</c> matches a name in
///     <c>MixinFieldNames</c>: <c>_behavior</c> (pre-D9 private field),
///     <c>_singleBehavior</c>/<c>_multiBehavior</c> (NnChipSet dual-mixin), or
///     <c>Behavior</c> (post-D9 inherited property from
///     <c>NnAsyncBoundComponentBase&lt;TValue&gt;</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Pattern 2</b>: <c>.GetAwaiter().GetResult()</c> on
///     <c>{receiver}.NotifyChange(...)</c> / <c>{receiver}.ExecuteAsync(...)</c>. <b>D6
///     (2026-05-12)</b>: receiver verification uses <see cref="SemanticModel"/> to check that
///     the receiver's resolved type's <see cref="ISymbol.OriginalDefinition"/> equals
///     <c>NnAsyncBoundBehavior&lt;T&gt;</c>, replacing the pre-D6 name-only match. This makes
///     Pattern 2 robust against renames (e.g. the D9 <c>_behavior</c> → <c>Behavior</c>
///     refactor) and false-positive-free against unrelated types that happen to have a
///     <c>NotifyChange</c> method.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Pattern 3</b>: sync lambda passed to <c>ValueChanged</c> / <c>OnInput</c> /
///     <c>OnClick</c> — either as named-argument, assignment target, attribute argument,
///     OR positional argument — AND the lambda body captures a mixin receiver (name in
///     <c>MixinFieldNames</c>). <b>D5 (2026-05-12)</b>: positional-argument detection uses
///     <see cref="SemanticModel"/> to resolve the enclosing invocation's method symbol and
///     look up the parameter name at the argument's index, replacing the pre-D5 skip-path.
///     </description>
///   </item>
/// </list>
/// Render-time @markup interpolation (computed property getters, <c>BuildRenderTree</c> in
/// .razor-generated code) is ALLOWED — those are legitimate render-time reads driven by the
/// mixin's state-changed callback. Async methods and async lambdas are ALLOWED unconditionally.
/// </para>
/// <para>
/// <b>Per-compilation caching</b>: <see cref="Initialize"/> resolves the open-generic
/// <c>NnAsyncBoundBehavior&lt;T&gt;</c> symbol once via <see cref="AnalysisContext.RegisterCompilationStartAction"/>
/// and threads it into Pattern 2. If the type isn't referenced by the compilation, action
/// registration is skipped entirely (no analysis can legitimately match without the type
/// in scope).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnDesignSyncAsyncStateAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for sync handler observing async mixin state.</summary>
    public const string DiagnosticId = "NN_NND_ASYNC_001";

    private const string Category = "NnDesign Convention";

    private const string NnDesignNamespacePrefix = "NotNot.BlazorDesign";

    /// <summary>
    /// Mixin receiver-identifier names recognized by Patterns 1 + 3 (token-level capture detection).
    /// Includes the pre-D9 private-field names (<c>_behavior</c>, <c>_singleBehavior</c>,
    /// <c>_multiBehavior</c>) AND the post-D9 inherited property name <c>Behavior</c> from
    /// <c>NnAsyncBoundComponentBase&lt;TValue&gt;</c>. Pattern 2 no longer relies on this list —
    /// D6 upgraded Pattern 2 to SemanticModel-based receiver-type verification, which is
    /// authoritative regardless of receiver name. The list still gates Pattern 1 (member-access
    /// receiver) and Pattern 3 (lambda-capture) for performance (token check is O(1); semantic
    /// resolution per MemberAccess would be CPU-expensive on every identifier).
    /// </summary>
    private static readonly HashSet<string> MixinFieldNames = new(StringComparer.Ordinal)
    {
        "_behavior",
        "_singleBehavior",
        "_multiBehavior",
        "Behavior",
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
        // contains render-time @markup interpolation that DOES legitimately read Behavior.X.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // D6: Resolve NnAsyncBoundBehavior<T> open-generic symbol once per compilation. Pattern 2
        // requires the symbol for receiver-type verification via SymbolEqualityComparer on
        // OriginalDefinition. If the type isn't referenced by this compilation, no Pattern can
        // legitimately match anyway — skip registration entirely.
        context.RegisterCompilationStartAction(compStart =>
        {
            var behaviorType = compStart.Compilation.GetTypeByMetadataName(
                "NotNot.BlazorDesign.NnDesign.AsyncBound.NnAsyncBoundBehavior`1");
            if (behaviorType is null)
                return;

            // Pattern 1 — sync handler reads mixin state property.
            compStart.RegisterSyntaxNodeAction(
                AnalyzePattern1MemberAccess,
                SyntaxKind.SimpleMemberAccessExpression);

            // Pattern 2 — sync handler blocks on Task via .GetAwaiter().GetResult().
            // D6: Pattern 2 receives `behaviorType` for SemanticModel-based receiver verification.
            compStart.RegisterSyntaxNodeAction(
                ctx => AnalyzePattern2GetAwaiterGetResult(ctx, behaviorType),
                SyntaxKind.InvocationExpression);

            // Pattern 3 — sync lambda registered as sync-callback captures the mixin receiver.
            // D5: positional-arg detection uses ctx.SemanticModel inside IsRegisteredAsSyncCallback.
            compStart.RegisterSyntaxNodeAction(
                AnalyzePattern3SyncLambda,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression,
                SyntaxKind.AnonymousMethodExpression);
        });
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

    private static void AnalyzePattern2GetAwaiterGetResult(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol behaviorType)
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
        // async method: <receiver>.NotifyChange(...) or <receiver>.ExecuteAsync(...).
        var taskProducer = getAwaiterAccess.Expression;
        if (taskProducer is not InvocationExpressionSyntax mixinCallInvocation)
            return;
        if (mixinCallInvocation.Expression is not MemberAccessExpressionSyntax mixinMemberAccess)
            return;

        var methodName = mixinMemberAccess.Name.Identifier.ValueText;
        if (!MixinAsyncMethodNames.Contains(methodName))
            return;

        // D6: SemanticModel-based receiver-type verification.
        // Resolve the receiver expression's type via SemanticModel and check its
        // OriginalDefinition against the cached open-generic NnAsyncBoundBehavior<T> symbol.
        // This is authoritative — the receiver may be a field named `_behavior` (pre-D9), a
        // property named `Behavior` (post-D9 inherited from NnAsyncBoundComponentBase<TValue>),
        // a local variable, a parameter, or any other expression that types as
        // NnAsyncBoundBehavior<closed-generic>. The pre-D9 token-level match by name silently
        // missed the D9 rename; SymbolEqualityComparer on OriginalDefinition catches any
        // closed-generic regardless of receiver-syntax shape.
        var receiverExpr = UnwrapNullForgiving(mixinMemberAccess.Expression);
        var typeInfo = context.SemanticModel.GetTypeInfo(receiverExpr, context.CancellationToken);
        var receiverType = typeInfo.Type ?? typeInfo.ConvertedType;
        if (receiverType is not INamedTypeSymbol namedReceiver)
            return;
        if (!SymbolEqualityComparer.Default.Equals(namedReceiver.OriginalDefinition, behaviorType))
            return;

        // Namespace-scope filter.
        if (!IsInNnDesignNamespace(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // For Pattern 2, the very act of blocking on the Task via GetAwaiter().GetResult()
        // defeats the async pipeline regardless of enclosing context. Still, async methods
        // SHOULD use `await` instead — report unconditionally inside NnDesign.
        var receiverDisplayName = GetReceiverDisplayName(receiverExpr);
        var enclosingDescription = GetEnclosingDescription(invocation);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            enclosingDescription,
            $"{receiverDisplayName}.{methodName}().GetAwaiter().GetResult()"));
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
        // a positional argument to a method whose parameter name matches (D5).
        if (!IsRegisteredAsSyncCallback(node, context.SemanticModel, context.CancellationToken))
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
    /// member-initializer for a property of that name, OR a positional argument whose parameter
    /// name (resolved via <see cref="SemanticModel"/>) matches a sync-callback name.
    /// </summary>
    /// <remarks>
    /// D5 (2026-05-12): added positional-argument resolution via SemanticModel parameter lookup.
    /// Prior to D5, positional callsites silently bypassed Pattern 3 (the deferred-during-Phase-8
    /// case). Positional resolution walks: <c>ArgumentSyntax</c> → <c>ArgumentListSyntax</c> →
    /// enclosing invocation / object-creation / constructor-initializer, resolves the target
    /// <see cref="IMethodSymbol"/>, indexes into <see cref="IMethodSymbol.Parameters"/> at the
    /// argument's position, and returns the parameter <c>Name</c>.
    /// </remarks>
    private static bool IsRegisteredAsSyncCallback(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
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
            else
            {
                // D5: Positional argument — resolve the enclosing invocation's method symbol
                // via SemanticModel, then look up the parameter at this argument's index.
                if (TryResolvePositionalParameterName(arg, semanticModel, cancellationToken, out var paramName)
                    && paramName is not null
                    && SyncCallbackNames.Contains(paramName))
                {
                    return true;
                }
            }
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

    /// <summary>
    /// D5 helper — resolves the parameter name corresponding to a positional argument's
    /// position in the enclosing invocation, object-creation, or constructor-initializer.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> AND sets <paramref name="parameterName"/> when:
    /// (a) the argument's grandparent is an <c>InvocationExpressionSyntax</c>,
    /// <c>BaseObjectCreationExpressionSyntax</c>, or <c>ConstructorInitializerSyntax</c>,
    /// (b) <see cref="SemanticModel"/> resolves a single <see cref="IMethodSymbol"/> for that
    /// call, and (c) the argument's index is in range of the method's parameter list.
    /// Returns <c>false</c> for params-array overflow positions, ambiguous overload resolution,
    /// or any case where the parameter cannot be uniquely determined.
    /// </remarks>
    private static bool TryResolvePositionalParameterName(
        ArgumentSyntax arg,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string? parameterName)
    {
        parameterName = null;

        if (arg.Parent is not ArgumentListSyntax argList)
            return false;
        var argIndex = argList.Arguments.IndexOf(arg);
        if (argIndex < 0)
            return false;

        var targetInfo = argList.Parent switch
        {
            InvocationExpressionSyntax invocation => semanticModel.GetSymbolInfo(invocation, cancellationToken),
            BaseObjectCreationExpressionSyntax objCreation => semanticModel.GetSymbolInfo(objCreation, cancellationToken),
            ConstructorInitializerSyntax ctorInit => semanticModel.GetSymbolInfo(ctorInit, cancellationToken),
            _ => default,
        };

        var targetSymbol = targetInfo.Symbol ?? targetInfo.CandidateSymbols.FirstOrDefault();
        if (targetSymbol is not IMethodSymbol method)
            return false;

        if (argIndex >= method.Parameters.Length)
            return false; // params-array overflow OR ambiguous overload

        parameterName = method.Parameters[argIndex].Name;
        return true;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the mixin receiver name (e.g. <c>_behavior</c> or <c>Behavior</c>) when
    /// <paramref name="expression"/> resolves to a recognized mixin receiver — either a bare
    /// <see cref="IdentifierNameSyntax"/> or a null-forgiving suppression (<c>_behavior!</c>)
    /// wrapping one. Returns null otherwise.
    /// </summary>
    /// <remarks>
    /// Used by Pattern 1 (member-access receiver name match). Pattern 2 no longer uses this
    /// helper — D6 replaced its name-based check with SemanticModel-based receiver-type
    /// verification (see <c>AnalyzePattern2GetAwaiterGetResult</c>). Pattern 1 retains the
    /// token-level check for performance: Pattern 1 fires on every
    /// <c>SimpleMemberAccessExpression</c> in the compilation, and resolving each receiver's
    /// type via SemanticModel would be CPU-expensive; the name-list filter is a cheap O(1)
    /// pre-screen that yields high signal because the state-property name list
    /// (<c>IsInFlight</c>, <c>InDebounce</c>, etc.) is already <c>NnAsyncBoundBehavior</c>-specific.
    /// </remarks>
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

    /// <summary>
    /// D6 helper — strips a trailing null-forgiving operator (<c>!</c>) from an expression,
    /// returning the inner operand. Pre-D9, the mixin receiver was typically a nullable
    /// field accessed with <c>_behavior!.Method(...)</c>; post-D9 it's a nullable property
    /// accessed with <c>Behavior!.Method(...)</c>. Both shapes wrap the actual receiver in a
    /// <see cref="PostfixUnaryExpressionSyntax"/>; SemanticModel resolution must run on the
    /// inner operand to get the value's type (not the operator's type).
    /// </summary>
    private static ExpressionSyntax UnwrapNullForgiving(ExpressionSyntax expression)
    {
        if (expression is PostfixUnaryExpressionSyntax postfix
            && postfix.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            return postfix.Operand;
        }
        return expression;
    }

    /// <summary>
    /// D6 helper — extracts a human-readable receiver name for the diagnostic message format.
    /// For an <see cref="IdentifierNameSyntax"/> receiver (the common case: a field or property
    /// reference like <c>Behavior</c>), returns the identifier text. For a
    /// <see cref="MemberAccessExpressionSyntax"/> receiver (e.g. <c>this.Behavior</c>),
    /// returns the rightmost member name. Fallback uses the expression's textual form.
    /// </summary>
    private static string GetReceiverDisplayName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.ValueText,
            _ => expression.ToString(),
        };
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
