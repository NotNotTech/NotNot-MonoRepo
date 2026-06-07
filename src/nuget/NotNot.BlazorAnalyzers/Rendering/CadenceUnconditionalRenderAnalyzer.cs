using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.BlazorAnalyzers.Lifecycle;
using NotNot.BlazorAnalyzers.LiteDDD;

namespace NotNot.BlazorAnalyzers.Rendering;

/// <summary>
/// <c>NNB042</c> — flags a <see cref="System.Threading.Timer"/> callback OR a
/// <c>PeriodicTimer.WaitForNextTickAsync</c> loop body, on a <c>ComponentBase</c>-derived type,
/// that invokes <c>StateHasChanged</c> UNCONDITIONALLY (no intervening equality / dirty-check
/// guard). A cadence-driven unconditional render produces a render every tick regardless of
/// whether any observable state changed — the render-redundancy defect class this analyzer
/// statically detects.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a Timer / PeriodicTimer signature (narrow scope)</b>: the cadence signature is the
/// low-false-positive discriminator. Event-handler-driven <c>StateHasChanged</c> calls
/// (<c>OnChanged += …</c>) are NOT cadence-driven and are correctly out of scope — flagging
/// "any unconditional <c>StateHasChanged</c>" would flood on legitimate event handlers.
/// </para>
/// <para>
/// <b>Dirty-check recognition (PASS)</b>: the rule does NOT fire when, between cadence entry
/// and <c>StateHasChanged</c>, there is (a) an equality comparison
/// (<c>EqualityComparer&lt;T&gt;.Equals</c> / <c>==</c> / <c>!=</c>) gating the
/// <c>StateHasChanged</c>, OR (b) the cadence is delegated to <c>NnLiveValue&lt;T&gt;</c> /
/// <c>NnRelativeTime</c> (whose recompute kernel IS the dirty-check). A callback with no
/// <c>StateHasChanged</c> at all does not fire by construction.
/// </para>
/// <para>
/// <b>Severity</b>: <see cref="DiagnosticSeverity.Warning"/>, advisory-only — the diagnostic
/// MESSAGE points the author at the remedy. No code-fix provider (the wrap is non-mechanical:
/// it requires choosing the projection for the dirty-check). FIX_NOT_SILENCE: the remedy is a
/// guard or an <c>NnLiveValue&lt;T&gt;</c> delegation, never a <c>#pragma</c> / <c>NoWarn</c>.
/// </para>
/// <para>
/// <b>Bounded analysis</b>: when the cadence callback is a method-group or local-function
/// reference, the analyzer follows it ONE hop to the target method body. No deeper
/// interprocedural walk — Warning-staged, narrow-scope by design.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CadenceUnconditionalRenderAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic ID for a cadence callback that invokes <c>StateHasChanged</c> unconditionally on a Blazor component.</summary>
	public const string DiagnosticId = "NNB042";

	private const string Category = "Rendering";

	private static readonly LocalizableString Title =
		"Cadence timer callback invokes StateHasChanged unconditionally";

	private static readonly LocalizableString MessageFormat =
		"Cadence callback on Blazor component invokes StateHasChanged unconditionally. A "
		+ "Timer / PeriodicTimer that renders every tick regardless of state change wastes "
		+ "renders. Add an equality / dirty-check guard before StateHasChanged (only render "
		+ "when the projected value actually changed), or delegate the cadence to "
		+ "NnLiveValue<T> whose recompute kernel performs the dirty-check.";

	private static readonly LocalizableString Description =
		"A System.Threading.Timer callback or PeriodicTimer.WaitForNextTickAsync loop body on a "
		+ "ComponentBase-derived type that calls StateHasChanged (directly, or via "
		+ "InvokeAsync(StateHasChanged) / InvokeAsync(() => StateHasChanged())) with no "
		+ "intervening equality / dirty-check guard renders on every tick whether or not any "
		+ "observable state changed. This is the cadence form of the render-redundancy defect "
		+ "class. Remedy: gate StateHasChanged behind an equality comparison "
		+ "(EqualityComparer<T>.Equals / == / !=) so a render only happens when the projected "
		+ "value changed, or delegate the cadence to NnLiveValue<T> / NnRelativeTime whose "
		+ "recompute kernel IS the dirty-check. The rule is advisory (Warning) and may be "
		+ "suppressed assembly-wide via [assembly: CadenceRenderBypass] for assemblies outside "
		+ "the render-policy scope; sample pages under Pages/Samples/** and "
		+ "Pages/NnDesignSamples/** are exempt by path.";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId, Title, MessageFormat, Category,
		DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description,
		helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	// ── Bypass attribute identity (simple-name + FQN dual-match, mirroring NnRmBypass) ──
	private const string BypassAttributeSimpleName = "CadenceRenderBypassAttribute";
	private const string BypassAttributeFullName = "NotNot.BlazorAnalyzers.Rendering.CadenceRenderBypassAttribute";

	// ── Dirty-check primitive type simple-names (delegated-cadence auto-pass) ────────────
	private static readonly string[] DirtyCheckPrimitiveTypeNames = { "NnLiveValue", "NnRelativeTime" };

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(compilationStart =>
		{
			if (!ShouldAnalyzeCompilation(compilationStart.Compilation))
				return;

			compilationStart.RegisterSyntaxNodeAction(AnalyzeTimerCreation, SyntaxKind.ObjectCreationExpression);
			compilationStart.RegisterSyntaxNodeAction(AnalyzePeriodicTimerLoop, SyntaxKind.WhileStatement);
		});
	}

	// ── Compilation gate ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns true when the compilation should be analyzed: the assembly is a Shared / Client
	/// assembly (where Blazor components live) AND it does NOT carry
	/// <c>[assembly: CadenceRenderBypass]</c>. Server-side assemblies are excluded (their
	/// PeriodicTimer loops are not ComponentBase callbacks); per-node the
	/// <see cref="BlazorLifecycleHelpers.IsBlazorComponent"/> gate is the authoritative check.
	/// </summary>
	private static bool ShouldAnalyzeCompilation(Compilation compilation)
	{
		var assembly = compilation?.Assembly;
		if (assembly == null)
			return false;

		if (!LdddAnalyzerHelpers.IsSharedOrClientAssembly(assembly))
			return false;

		if (HasBypassAttribute(assembly))
			return false;

		return true;
	}

	private static bool HasBypassAttribute(IAssemblySymbol assembly)
	{
		foreach (var attribute in assembly.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass == null)
				continue;

			if (string.Equals(attrClass.Name, BypassAttributeSimpleName, StringComparison.Ordinal))
				return true;
			if (string.Equals(attrClass.ToDisplayString(), BypassAttributeFullName, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	// ── Path-exemption (sample pages may demonstrate cadence patterns) ───────────────────

	private static bool IsExceptedPath(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return false;

		var normalized = filePath.Replace('\\', '/');

		if (normalized.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		if (normalized.IndexOf("/Pages/NnDesignSamples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		return false;
	}

	// ── Timer-creation pathway: new System.Threading.Timer(callback, …) ──────────────────

	private static void AnalyzeTimerCreation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ObjectCreationExpressionSyntax creation)
			return;

		if (IsExceptedPath(creation.SyntaxTree.FilePath))
			return;

		// Guard symbol resolution against analyzer crash (mirror IncompleteCancellationHandlingAnalyzer).
		try
		{
			// Confirm the constructed type is System.Threading.Timer.
			var typeSymbol = context.SemanticModel.GetSymbolInfo(creation.Type, context.CancellationToken).Symbol as INamedTypeSymbol;
			if (typeSymbol == null)
				typeSymbol = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type as INamedTypeSymbol;

			if (!IsSystemThreadingTimer(typeSymbol))
				return;

			// Confirm the enclosing type is a Blazor component.
			var enclosingType = GetEnclosingNamedType(context, creation);
			if (enclosingType == null || !BlazorLifecycleHelpers.IsBlazorComponent(enclosingType))
				return;

			// First constructor argument is the TimerCallback delegate.
			var args = creation.ArgumentList?.Arguments;
			if (args == null || args.Value.Count == 0)
				return;

			var callbackExpr = args.Value[0].Expression;

			// Resolve the callback body (lambda inline, or method-group / local-function one hop).
			var callbackBody = ResolveCallbackBody(context, callbackExpr);
			if (callbackBody == null)
				return;

			ReportIfUnconditionalStateHasChanged(context, callbackBody);
		}
		catch (Exception)
		{
			// Semantic resolution may fail on incomplete/erroneous code — fail silent (no diagnostic).
		}
	}

	// ── PeriodicTimer pathway: while (await pt.WaitForNextTickAsync(…)) { … } ─────────────

	private static void AnalyzePeriodicTimerLoop(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not WhileStatementSyntax whileStatement)
			return;

		if (IsExceptedPath(whileStatement.SyntaxTree.FilePath))
			return;

		try
		{
			if (!ConditionIsWaitForNextTick(context, whileStatement.Condition))
				return;

			var enclosingType = GetEnclosingNamedType(context, whileStatement);
			if (enclosingType == null || !BlazorLifecycleHelpers.IsBlazorComponent(enclosingType))
				return;

			ReportIfUnconditionalStateHasChanged(context, whileStatement.Statement);
		}
		catch (Exception)
		{
			// Semantic resolution may fail — fail silent.
		}
	}

	// ── Shared detection: does the body invoke StateHasChanged with no dirty-check guard? ─

	private static void ReportIfUnconditionalStateHasChanged(SyntaxNodeAnalysisContext context, SyntaxNode body)
	{
		// Delegated-cadence auto-pass: body recomputes via NnLiveValue / NnRelativeTime.
		if (BodyDelegatesToDirtyCheckPrimitive(context, body))
			return;

		foreach (var invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
		{
			if (!IsStateHasChangedInvocation(invocation))
				continue;

			// PASS when an equality / dirty-check guard gates this StateHasChanged.
			if (IsGuardedByDirtyCheck(invocation, body))
				continue;

			context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
			return; // one diagnostic per cadence body is sufficient
		}
	}

	// ── StateHasChanged recognition (direct + InvokeAsync(StateHasChanged) forms) ────────

	private static bool IsStateHasChangedInvocation(InvocationExpressionSyntax invocation)
	{
		// Direct: StateHasChanged()  OR  this.StateHasChanged()
		if (GetInvokedSimpleName(invocation) == "StateHasChanged")
			return true;

		// Wrapped: InvokeAsync(StateHasChanged)  OR  InvokeAsync(() => StateHasChanged())
		if (GetInvokedSimpleName(invocation) == "InvokeAsync")
		{
			var args = invocation.ArgumentList?.Arguments;
			if (args == null)
				return false;

			foreach (var arg in args.Value)
			{
				switch (arg.Expression)
				{
					// InvokeAsync(StateHasChanged) — method-group argument.
					case IdentifierNameSyntax id when id.Identifier.Text == "StateHasChanged":
						return true;
					case MemberAccessExpressionSyntax ma when ma.Name.Identifier.Text == "StateHasChanged":
						return true;
					// InvokeAsync(() => StateHasChanged()) / InvokeAsync(() => { StateHasChanged(); })
					case LambdaExpressionSyntax lambda when LambdaBodyInvokesStateHasChanged(lambda):
						return true;
				}
			}
		}

		return false;
	}

	private static bool LambdaBodyInvokesStateHasChanged(LambdaExpressionSyntax lambda)
	{
		SyntaxNode? lambdaBody = lambda.Body;
		if (lambdaBody == null)
			return false;

		return lambdaBody.DescendantNodesAndSelf()
			.OfType<InvocationExpressionSyntax>()
			.Any(inv => GetInvokedSimpleName(inv) == "StateHasChanged");
	}

	private static string? GetInvokedSimpleName(InvocationExpressionSyntax invocation)
	{
		return invocation.Expression switch
		{
			IdentifierNameSyntax id => id.Identifier.Text,
			MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
			MemberBindingExpressionSyntax mb => mb.Name.Identifier.Text,
			_ => null,
		};
	}

	// ── Dirty-check guard recognition (generic equality + delegated primitive) ───────────

	/// <summary>
	/// Returns true when a recognized equality dirty-check
	/// (<c>==</c> / <c>!=</c> / <c>EqualityComparer&lt;T&gt;.Default.Equals</c>, per
	/// <see cref="ConditionIsDirtyCheck"/>) gates the <paramref name="stateHasChanged"/>
	/// invocation: either (a) it is the condition of an enclosing <c>if</c> whose body contains
	/// the render, or (b) it is the condition of an earlier sibling <c>if</c> that early-returns.
	/// The condition SHAPE is narrow on purpose — an unrelated <c>is</c>-pattern or arbitrary
	/// <c>obj.Equals(x)</c> wrapping the render is NOT a guard, so the rule fires rather than
	/// silently suppressing a genuinely-unguarded cadence render.
	/// </summary>
	private static bool IsGuardedByDirtyCheck(InvocationExpressionSyntax stateHasChanged, SyntaxNode body)
	{
		// (a) StateHasChanged sits inside the then/else of an `if` whose condition is a
		//     recognized equality dirty-check.
		for (SyntaxNode? current = stateHasChanged.Parent;
			 current != null && current != body.Parent;
			 current = current.Parent)
		{
			if (current is IfStatementSyntax ifStatement && ConditionIsDirtyCheck(ifStatement.Condition))
				return true;
		}

		// (b) An earlier guard clause in the same body performs the dirty-check and
		//     early-returns when nothing changed (e.g. `if (a == b) return;` before render).
		foreach (var ifStatement in body.DescendantNodes().OfType<IfStatementSyntax>())
		{
			if (ifStatement.SpanStart >= stateHasChanged.SpanStart)
				continue;
			if (ConditionIsDirtyCheck(ifStatement.Condition) && BranchEarlyReturns(ifStatement))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Returns true when <paramref name="condition"/> contains a recognized EQUALITY comparison —
	/// the only condition shape that constitutes a dirty-check for this rule. Recognized forms:
	/// <list type="bullet">
	/// <item><c>a == b</c> / <c>a != b</c> (the <see cref="SyntaxKind.EqualsExpression"/> /
	///   <see cref="SyntaxKind.NotEqualsExpression"/> operators).</item>
	/// <item><c>EqualityComparer&lt;T&gt;.Default.Equals(a, b)</c> — the canonical projected
	///   dirty-check the rule's remedy MESSAGE recommends. Recognized by a two-argument
	///   <c>Equals</c> invocation whose receiver chain mentions <c>EqualityComparer</c>.</item>
	/// </list>
	/// DELIBERATELY NOT recognized (each is a false-NEGATIVE surface the prior breadth opened):
	/// a blanket <c>is</c>-pattern type-test (<c>if (x is Foo)</c> is not a dirty-check), and an
	/// arbitrary instance <c>obj.Equals(x)</c> that is not an <c>EqualityComparer</c> projection
	/// (e.g. <c>logger.Equals(other)</c> wrapping an unrelated render). A genuine projected
	/// dirty-check uses an operator or <c>EqualityComparer&lt;T&gt;</c>; tightening here lets the
	/// rule fire on a cadence render merely co-located under an unrelated comparison.
	/// </summary>
	private static bool ConditionIsDirtyCheck(ExpressionSyntax? condition)
	{
		if (condition == null)
			return false;

		foreach (var node in condition.DescendantNodesAndSelf())
		{
			switch (node)
			{
				// `a == b` / `a != b` — genuine equality operators.
				case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.EqualsExpression)
												  || bin.IsKind(SyntaxKind.NotEqualsExpression):
					return true;
				// `EqualityComparer<T>.Default.Equals(a, b)` — canonical projected dirty-check.
				// Require the two-arg `Equals` form AND an `EqualityComparer` receiver chain so an
				// unrelated `someObj.Equals(x)` does not masquerade as a guard.
				case InvocationExpressionSyntax inv when IsEqualityComparerEquals(inv):
					return true;
			}
		}
		return false;
	}

	/// <summary>
	/// True for a two-argument <c>Equals</c> invocation whose receiver chain references
	/// <c>EqualityComparer</c> (e.g. <c>EqualityComparer&lt;T&gt;.Default.Equals(a, b)</c>). This
	/// is the canonical projected dirty-check; an arbitrary one-arg instance <c>x.Equals(y)</c> or
	/// a non-<c>EqualityComparer</c> receiver is intentionally rejected.
	/// </summary>
	private static bool IsEqualityComparerEquals(InvocationExpressionSyntax invocation)
	{
		if (GetInvokedSimpleName(invocation) != "Equals")
			return false;

		// Canonical comparer form is two-argument: Equals(left, right).
		if ((invocation.ArgumentList?.Arguments.Count ?? 0) != 2)
			return false;

		// The receiver must be a member-access chain that names EqualityComparer somewhere
		// (EqualityComparer<T>.Default.Equals(...)).
		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
			return false;

		for (SyntaxNode? current = memberAccess.Expression; current != null; current = (current as MemberAccessExpressionSyntax)?.Expression)
		{
			var nameToken = current switch
			{
				IdentifierNameSyntax id => id.Identifier.Text,
				GenericNameSyntax gn => gn.Identifier.Text,
				MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
				_ => null,
			};
			if (string.Equals(nameToken, "EqualityComparer", StringComparison.Ordinal))
				return true;

			if (current is not MemberAccessExpressionSyntax)
				break;
		}

		return false;
	}

	private static bool BranchEarlyReturns(IfStatementSyntax ifStatement)
	{
		var statement = ifStatement.Statement;
		// `if (...) return;`
		if (statement is ReturnStatementSyntax)
			return true;
		// `if (...) { …; return; }`
		if (statement is BlockSyntax block)
			return block.Statements.OfType<ReturnStatementSyntax>().Any();
		return false;
	}

	private static bool BodyDelegatesToDirtyCheckPrimitive(SyntaxNodeAnalysisContext context, SyntaxNode body)
	{
		// If the cadence body routes its render through an NnLiveValue / NnRelativeTime member
		// (whose recompute kernel IS the dirty-check), treat the whole body as a pass.
		foreach (var memberAccess in body.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
		{
			// (a) Semantic: resolve the receiver's declared type and match the primitive
			//     by type simple-name (handles `_live.Recompute()` where `_live : NnLiveValue<T>`).
			var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
			if (receiverType != null && DirtyCheckPrimitiveTypeNames.Any(t => string.Equals(receiverType.Name, t, StringComparison.Ordinal)))
				return true;

			// (b) Syntactic fallback (semantic resolution may fail on incomplete code): the
			//     receiver token text EXACTLY equals a primitive name (e.g. a static-style
			//     `NnLiveValue.X`). Exact-match only — a substring test would wrongly suppress on
			//     any token merely CONTAINING the name (e.g. `_nnLiveValueCacheStale`), opening a
			//     false-NEGATIVE surface. The bare-name loop below already covers field/local
			//     references whose own token equals a primitive name.
			var leftText = memberAccess.Expression switch
			{
				IdentifierNameSyntax id => id.Identifier.Text,
				GenericNameSyntax gn => gn.Identifier.Text,
				MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
				_ => null,
			};
			if (leftText != null && DirtyCheckPrimitiveTypeNames.Any(t => string.Equals(leftText, t, StringComparison.Ordinal)))
				return true;
		}

		// Also recognize a bare reference to the primitive type by name (identifier or generic).
		foreach (var name in body.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
		{
			if (DirtyCheckPrimitiveTypeNames.Any(t => string.Equals(name.Identifier.Text, t, StringComparison.Ordinal)))
				return true;
		}

		return false;
	}

	// ── Type / symbol helpers ────────────────────────────────────────────────────────────

	private static bool IsSystemThreadingTimer(INamedTypeSymbol? typeSymbol)
	{
		if (typeSymbol == null)
			return false;
		return typeSymbol.Name == "Timer"
			&& typeSymbol.ContainingNamespace?.ToDisplayString() == "System.Threading";
	}

	private static bool ConditionIsWaitForNextTick(SyntaxNodeAnalysisContext context, ExpressionSyntax? condition)
	{
		if (condition == null)
			return false;

		// Match `await {pt}.WaitForNextTickAsync(…)` (with or without await/parens).
		foreach (var invocation in condition.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
		{
			if (GetInvokedSimpleName(invocation) != "WaitForNextTickAsync")
				continue;

			// Require a `receiver.WaitForNextTickAsync(…)` member-access whose receiver resolves to
			// PeriodicTimer (null-type permissive fallback handles incomplete-code semantic gaps).
			// A bare `WaitForNextTickAsync()` with NO receiver is NOT accepted — a component-local
			// method coincidentally named WaitForNextTickAsync must not satisfy the PeriodicTimer
			// gate (false-POSITIVE surface). The PeriodicTimer signature is the cadence
			// discriminator; a receiver-less call carries no such signature.
			if (invocation.Expression is MemberAccessExpressionSyntax ma)
			{
				var receiverType = context.SemanticModel.GetTypeInfo(ma.Expression, context.CancellationToken).Type;
				if (receiverType == null || receiverType.Name == "PeriodicTimer")
					return true;
			}
		}
		return false;
	}

	private static INamedTypeSymbol? GetEnclosingNamedType(SyntaxNodeAnalysisContext context, SyntaxNode node)
	{
		var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
		if (typeDecl == null)
			return null;
		return context.SemanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken) as INamedTypeSymbol;
	}

	/// <summary>
	/// Resolves the body to scan for the cadence callback expression: an inline lambda's body,
	/// or — following a method-group / local-function reference ONE hop — the referenced
	/// method's body.
	/// </summary>
	private static SyntaxNode? ResolveCallbackBody(SyntaxNodeAnalysisContext context, ExpressionSyntax callbackExpr)
	{
		// Inline lambda: scan the lambda body directly.
		if (callbackExpr is LambdaExpressionSyntax lambda)
			return (SyntaxNode?)lambda.Body;

		// Method-group / local-function reference: resolve the symbol and follow one hop to its body.
		var symbol = context.SemanticModel.GetSymbolInfo(callbackExpr, context.CancellationToken).Symbol;
		if (symbol is not IMethodSymbol method)
			return null;

		foreach (var reference in method.DeclaringSyntaxReferences)
		{
			var declNode = reference.GetSyntax(context.CancellationToken);
			switch (declNode)
			{
				case MethodDeclarationSyntax md when md.Body != null:
					return md.Body;
				case MethodDeclarationSyntax md when md.ExpressionBody != null:
					return md.ExpressionBody;
				case LocalFunctionStatementSyntax lf when lf.Body != null:
					return lf.Body;
				case LocalFunctionStatementSyntax lf when lf.ExpressionBody != null:
					return lf.ExpressionBody;
			}
		}
		return null;
	}
}
