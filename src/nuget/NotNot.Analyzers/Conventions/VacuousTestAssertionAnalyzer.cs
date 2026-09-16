using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Flags a test assertion whose outcome is fixed by DECLARATION SHAPE rather than by the behavior of the
/// code under test — a ceremony shape that passes whether or not the subject works. Three cases share one
/// diagnostic ID because they share one remedy and one disposition: constant tautologies, enum shape pins,
/// and non-public reflection into a foreign type's declaration metadata.
/// </summary>
/// <remarks>
/// <para>
/// The finding is a CANDIDATE, never a verdict. The enforced authority
/// (<c>greenfield-patterns.md#CEREMONY_SHAPE_REMOVAL</c>) gates deletion on TWO conditions: (1) the shape
/// matches — this is the analyzer-decidable half — and (2) the test's justification names no protected
/// behavior or consumer, which is a manual judgment by design. Shape match ALONE never authorizes deletion,
/// which is why the severity is <c>Warning</c> and why the message's first remediation tier KEEPS the test.
/// A cross-assembly binary-interface mirror is all-constant by construction and is load-bearing; condemning
/// it would cause exactly the silent coverage loss this rule exists to avoid.
/// </para>
/// <para>
/// SCOPE GATE (all three cases): the enclosing type declares at least one method carrying a test attribute
/// (simple name ending <c>Fact</c> / <c>Theory</c> / <c>Test</c> / <c>TestMethod</c> / <c>TestCase</c>).
/// ANY method of that type is then in scope, INCLUDING non-attributed private helpers — Case C's real locus
/// class puts the reflection in a <c>private static</c> helper, which a gate restricted to attributed
/// methods would miss. Membership is decided on the TYPE, so a <c>partial</c> class whose attributed methods
/// live in another part, and a class whose attributed methods are INHERITED from a base, are both in scope.
/// </para>
/// <para>
/// FLAGGED (warning):
/// <code>
/// Assert.NotEqual(typeof(Foo), typeof(Bar));                        // ← constant-tautology
/// Assert.True(typeof(Foo).IsPublic);                                // ← constant-tautology
/// Assert.Equal(DenialReason.A, (int)WireStatus.A);                  // ← constant-tautology
/// Assert.Equal("Foo", nameof(Foo));                                 // ← constant-tautology
/// Assert.Equal(new[]{"A","B"}, Enum.GetNames(typeof(E)));           // ← enum-shape-pin
/// Assert.Equal(3, Enum.GetValues&lt;E&gt;().Length);                     // ← enum-shape-pin
/// typeof(ForeignBase).GetMethod("Hidden", Instance | NonPublic);     // ← foreign-nonpublic-reflection
/// </code>
/// </para>
/// <para>
/// ALLOWED (silent):
/// <code>
/// Assert.Equal(7, list.Count);                                      // behavior-dependent operand
/// Assert.Equal(expected, Subject.Compute(input));                   // calls the code under test
/// Assert.All(Enum.GetValues&lt;E&gt;(), v =&gt; Assert.NotNull(Map(v)));     // enumeration SOURCE, not a shape pin
/// Assert.True(obj.GetType().IsPublic);                              // runtime receiver
/// obj.GetType().GetProperty(name, Instance | Public);               // runtime receiver, Public flags
/// typeof(OwnType).GetMethod("Hidden", Instance | NonPublic);        // compilation owns the type
/// Assert.Fail("expected ArgumentException");                        // unconditional-outcome MARKER
/// Assert.Skip("not supported on this platform");                    // unconditional-outcome MARKER
/// Assert.True(false, "unreachable branch");                         // fixed outcome is FAILURE, a detector
/// Assert.True(Ready(), string.Join(",", Enum.GetNames&lt;E&gt;()));        // enum call is CARRIED, not compared
/// </code>
/// </para>
/// <para>
/// COMPARED versus CARRIED — one condition, consumed by both Cases A and B. An argument participates in the
/// verdict (COMPARED) unless it is bound to a parameter that carries diagnostic text (<c>userMessage</c>,
/// <c>message</c>, <c>reason</c>, … or any <c>params</c> tail). Case A's all-quantifier quantifies over
/// compared arguments only, and Case B searches only those. Two precision properties depend on it: a
/// failure message is not an operand, and an enum accessor formatted into text or projected into input for
/// the code under test pins nothing. Vacuity additionally means a fixed outcome of PASS — an assertion
/// fixed to FAIL (<c>Assert.Fail</c>, <c>Assert.Skip</c>, <c>Assert.True(false, …)</c>,
/// <c>Assert.False(true, …)</c>) is an unreachable-path detector, and reporting it would route a reader to
/// deletion and silently convert a failing test into a passing one.
/// </para>
/// <para>
/// KNOWN SCOPE BOUNDARY (deliberate, not an oversight — an ALLOWED list that implies completeness is itself
/// a defect). These are NOT flagged, each for its own reason:
/// <list type="number">
///   <item><description>
///     Assertion libraries other than a type whose simple name is <c>Assert</c> — FluentAssertions
///     <c>.Should()</c> chains, Shouldly, raw <c>Debug.Assert</c>. Their call shapes are structurally
///     different and would need their own predicates; one rule claiming them all would be wrong on each.
///   </description></item>
///   <item><description>
///     <c>Assert.All</c> / <c>Assert.Collection</c> / <c>Assert.Contains</c> over an enum reflection
///     source. These use reflection as a per-member ENUMERATION SOURCE for behavioral assertions rather
///     than pinning shape — they are the pattern this rule steers toward, so flagging them would invert it.
///   </description></item>
///   <item><description>
///     Reflection through a RUNTIME receiver (<c>obj.GetType().GetMethod(...)</c>) rather than
///     <c>typeof(T)</c>. The reflected type is not decidable at compile time, so ownership cannot be
///     proven and the rule stays silent rather than guessing.
///   </description></item>
///   <item><description>
///     Non-public reflection into a type the compilation DOES own. Testing your own internals is ordinary;
///     the hazard is specific to pinning ANOTHER assembly's private declaration surface.
///   </description></item>
///   <item><description>
///     Condition 2 of the enforced authority — whether a justification names a protected behavior or
///     consumer. That is protocol-manual by design and no analyzer decides it.
///   </description></item>
///   <item><description>
///     An extension method shadowing <c>Type.GetMethod</c> could evade Case C, whose dispatch requires the
///     resolved method's containing type to be <c>System.Type</c>. A contrived shape with no observed
///     locus; recorded rather than fixtured.
///   </description></item>
/// </list>
/// Widening this rule to any of the above is a deliberate future decision, not an assumed gap.
/// </para>
/// <para>
/// DETECTION (perf-conscious): registers on <c>InvocationExpression</c>, the highest-frequency node in the
/// grammar, so the ordering inside the handler is a contract rather than a style choice — a cheap syntactic
/// shape check, then the scope gate on its syntax-only fast path (which exits every non-test file with no
/// symbol work, escalating to the type symbol only for a <c>partial</c> or base-listed declaration, the two
/// shapes where syntax alone is not exact), then the bypass walk, and only then any <c>SemanticModel</c>
/// query on the invocation itself. Cases A and B evaluate in that order on an
/// <c>Assert.*</c> node and return on the FIRST match, so one assertion can never produce two diagnostics.
/// Case C is disjoint by construction: its node is a <c>System.Type</c> reflection call, never an
/// <c>Assert</c> member call. Both the <c>Assert</c> gate and Case B's inner scan decide on the RESOLVED
/// SYMBOL's containing type, never on syntax shape, so <c>using static</c> cannot silence the rule.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VacuousTestAssertionAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NN_C006";

	// Category string matches NN_C004/NN_C005 exactly so the NN_C family shares one category bucket for
	// category-based .editorconfig + release tracking.
	private const string Category = "CodeStyle";

	private const string CodeStyleBypassAttributeFullName =
		"NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute";

	/// <summary>Case name emitted as message argument <c>{0}</c>.</summary>
	private const string CaseConstantTautology = "constant-tautology";

	/// <inheritdoc cref="CaseConstantTautology" />
	private const string CaseEnumShapePin = "enum-shape-pin";

	/// <inheritdoc cref="CaseConstantTautology" />
	private const string CaseForeignNonPublicReflection = "foreign-nonpublic-reflection";

	/// <summary>The assertion surface Cases A and B gate on, matched by the RESOLVED symbol's containing
	/// type simple name so qualification cannot decide whether the rule fires.</summary>
	private const string AssertTypeSimpleName = "Assert";

	private const string SystemTypeDisplayName = "System.Type";
	private const string SystemEnumDisplayName = "System.Enum";
	private const string MemberInfoDisplayName = "System.Reflection.MemberInfo";

	/// <summary><c>System.Reflection.BindingFlags.NonPublic</c>.</summary>
	private const int BindingFlagsNonPublic = 32;

	/// <summary>
	/// Case B's assertion-method allowlist, and it is load-bearing: it EXCLUDES <c>All</c>,
	/// <c>Collection</c>, and <c>Contains</c>, which consume enum reflection as a per-member enumeration
	/// source for behavioral assertions rather than pinning shape.
	/// </summary>
	private static readonly ImmutableHashSet<string> EqualityAssertionNames = ImmutableHashSet.Create(
		"Equal", "NotEqual", "StrictEqual", "Same", "True", "False");

	/// <summary>
	/// Assertions whose outcome is UNCONDITIONAL by design — they are control markers, not comparisons.
	/// </summary>
	/// <remarks>
	/// Load-bearing exclusion. These are all-constant BY CONSTRUCTION, so a bare all-quantifier over their
	/// arguments reports them as vacuous. They are the opposite of vacuous: each is the detector for an
	/// unexpected branch or a missing exception. Because a local control marker names no consumer, tier 1
	/// (KEEP-and-cite) cannot apply and the remediation ladder would route the reader to tier 3 — deleting
	/// <c>Assert.Fail("expected X")</c> from a negative-path test converts a test that FAILS when the
	/// expected exception is not thrown into one that PASSES. That is exactly the silent coverage loss this
	/// rule exists to prevent.
	/// </remarks>
	private static readonly ImmutableHashSet<string> UnconditionalOutcomeAssertionNames =
		ImmutableHashSet.Create("Fail", "Skip");

	/// <summary>
	/// Parameter names that CARRY diagnostic text rather than participate in the verdict.
	/// </summary>
	/// <remarks>
	/// The compared/carried distinction is the single condition behind two precision properties: Case A's
	/// all-quantifier must quantify over arguments that decide the outcome, and Case B must search only
	/// those. Without it, a failure message is treated as an operand — so
	/// <c>Assert.True(Subject.IsReady(), string.Join(",", Enum.GetNames&lt;E&gt;()))</c> is reported as an enum
	/// shape pin when the enum call is merely being formatted into text. Names cover the xunit
	/// (<c>userMessage</c>), NUnit and MSTest (<c>message</c> + a <c>params</c> tail) spellings; any
	/// <c>params</c> parameter is carried regardless of name.
	/// </remarks>
	private static readonly ImmutableHashSet<string> CarriedParameterNames = ImmutableHashSet.Create(
		StringComparer.OrdinalIgnoreCase,
		"userMessage", "userMessageFormat", "message", "messageFormat", "reason", "detail",
		"because", "becauseArgs", "args", "formatArgs", "parameters", "comment");

	/// <summary>The <c>System.Enum</c> reflection accessors that expose an enum's declaration shape.</summary>
	private static readonly ImmutableHashSet<string> EnumShapeAccessorNames = ImmutableHashSet.Create(
		"GetNames", "GetValues");

	/// <summary>
	/// Case C's method-name set: the <c>System.Type</c> accessors that look a NAMED member up under binding
	/// flags. Excludes <c>GetMethods</c>/<c>GetInterfaces</c> and friends, which name no member.
	/// </summary>
	private static readonly ImmutableHashSet<string> MemberLookupNames = ImmutableHashSet.Create(
		"GetMethod", "GetProperty", "GetField", "GetMember", "GetEvent");

	/// <summary>Test-attribute simple-name suffixes. No namespace requirement, which is what lets one rule
	/// serve xunit, NUnit, and MSTest.</summary>
	private static readonly string[] TestAttributeSuffixes =
		{ "Fact", "Theory", "Test", "TestMethod", "TestCase" };

	private static readonly LocalizableString Title =
		"Test assertion is behaviorally vacuous";

	private static readonly LocalizableString MessageFormat =
		"Test assertion in '{1}' is behaviorally vacuous ({0}): its outcome is fixed by declaration shape, "
		+ "not by the behavior of the code under test. This is a CANDIDATE, not a verdict — removal "
		+ "additionally requires that the test's justification name no protected behavior or consumer. "
		+ "Fix options: (1) If the pinned shape IS a contract — a wire format or cross-assembly "
		+ "binary-interface mirror — keep the test and name the consumer it protects in its documentation; "
		+ "that citation is what makes it a KEEP. (2) Replace the assertion with one over observable "
		+ "behavior of the code under test. (3) Delete the assertion only after confirming no consumer is "
		+ "named. (4) [NotNot.Bcl.Diagnostics.CodeStyleBypass] on the member, or #pragma warning disable "
		+ "NN_C006, for a deliberately declaration-level assertion.";

	private static readonly LocalizableString Description =
		"An assertion whose every operand is fixed by declaration shape passes whether or not the code "
		+ "under test works, so it consumes review attention and CI time while protecting nothing. Three "
		+ "shapes share this property: a tautology over constants, type identities, nameof, or declaration "
		+ "metadata; a pin on an enum's member names or count via Enum.GetNames/GetValues; and a lookup of "
		+ "a named non-public member on a type the compilation does not own, which pins another assembly's "
		+ "private declaration surface. "
		+ "This is a CANDIDATE, not a verdict: shape match alone never authorizes deletion, because the "
		+ "same all-constant shape is exactly how a legitimate wire-format or cross-assembly "
		+ "binary-interface mirror is written, and deleting one silently removes the only guard against "
		+ "value drift between two build graphs. "
		+ "Preferred fix, in order: (1) if the pinned shape IS a contract, KEEP the test and name the "
		+ "consumer it protects in its documentation — that citation is what makes it a keep, and it is the "
		+ "cheapest correct remedy for a genuine contract; "
		+ "(2) replace the assertion with one over observable behavior of the code under test; "
		+ "(3) delete the assertion only after confirming no consumer is named; "
		+ "(4) mark the member [NotNot.Bcl.Diagnostics.CodeStyleBypass], or suppress with #pragma warning "
		+ "disable NN_C006, for a deliberately declaration-level assertion. "
		+ "See the NN_C006 ReadMe section for the full rationale and the declared scope boundary.";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: Title,
		messageFormat: MessageFormat,
		category: Category,
		// Warning, NOT the project-default Error (AGENTS.md "Design Rules"), and the override is deliberate:
		// this rule decides only condition 1 of CEREMONY_SHAPE_REMOVAL (the shape matches). Condition 2 —
		// whether the justification names a protected behavior or consumer — is a manual judgment, so the
		// finding is a CANDIDATE. Error would assert a deletability the enforced authority forbids and would
		// break builds on load-bearing cross-assembly ABI mirrors, which are all-constant by construction.
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: "https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#nn_c006",
		customTags: new[] { "CodeStyle", "Conventions", "Testing" });

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

		context.RegisterCompilationStartAction(OnCompilationStart);
	}

	private static void OnCompilationStart(CompilationStartAnalysisContext context)
	{
		// Assembly-level bypass short-circuit — the cheapest available exit when a whole assembly opts out.
		if (HasCodeStyleBypassAttribute(context.Compilation.Assembly))
			return;

		context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeInvocation");

		if (context.Node is not InvocationExpressionSyntax invocation) return;

		// (a) Cheap SYNTACTIC shape check. Both call shapes are admitted — a qualified member access AND the
		// bare name that `using static` produces — because they bind to the identical method, so accepting
		// only the qualified form would let mere qualification decide whether the rule fires. SimpleNameSyntax
		// rather than IdentifierNameSyntax is deliberate: GenericNameSyntax is its SIBLING, not its subtype,
		// so matching the narrower type would exclude a bare `Equal<int>(1, 1)` — the same
		// qualification-decides hole in a different spelling. No method-NAME filter is possible here: Case A
		// applies to any method on a type named `Assert`, and the name needs the SemanticModel to resolve.
		if (invocation.Expression is not (MemberAccessExpressionSyntax or SimpleNameSyntax)) return;

		var arguments = invocation.ArgumentList.Arguments;
		// PERF prefilter only. The correctness guard against a vacuously-true all-quantifier is the
		// compared-argument count inside Case A, NOT this arity test — an arity-based guard would make the
		// rule turn on whether a message string was supplied.
		if (arguments.Count == 0) return;

		// (b) SCOPE GATE. Syntax-only on the fast path, so a non-test file exits with no symbol work.
		if (!IsInTestScope(invocation, context.ContainingSymbol)) return;

		// (c) Member → type → assembly bypass walk. Remediation tier 4 promises [CodeStyleBypass] ON THE
		// MEMBER, and the assembly-level short-circuit alone would not honour that promise.
		if (context.ContainingSymbol is { } containingSymbol
			&& HasCodeStyleBypassInScope(containingSymbol)) return;

		// (d) Resolve the symbol, then dispatch.
		if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
			is not IMethodSymbol method) return;

		var containingType = method.ContainingType;
		if (containingType is null) return;

		if (string.Equals(containingType.Name, AssertTypeSimpleName, StringComparison.Ordinal))
		{
			// An assertion whose outcome is unconditional is a control MARKER, never a vacuous comparison.
			if (UnconditionalOutcomeAssertionNames.Contains(method.Name)) return;

			// COMPARED vs CARRIED. Both cases below quantify over the arguments that decide the outcome and
			// ignore those that merely carry diagnostic text. An unclassifiable argument list yields
			// conservative silence rather than a guess.
			if (!TryGetComparedArguments(method, arguments, out var compared)) return;

			// No verdict-bearing operand ⇒ nothing can make the verdict vacuous.
			if (compared.Count == 0) return;

			// A fixed outcome of FAILURE is a deliberate unreachable-path marker, not ceremony:
			// `Assert.True(false, "unreachable")` and `Assert.False(true, …)` are detectors.
			if (IsUnconditionalFailureMarker(method, compared, context)) return;

			// FIRST-MATCH-WINS. On the specified predicates A and B are disjoint (an argument tree rooted at
			// an Enum.GetNames/GetValues invocation is not behavior-independent under Case A's arms), but
			// nothing ENFORCES that, and two NN_C006 diagnostics at one location would read as a detection
			// bug. Returning on the first match makes the disjointness structural.
			if (IsConstantTautology(compared, context))
			{
				Report(context, invocation, CaseConstantTautology);
				return;
			}

			if (IsEnumShapePin(method, compared, context))
			{
				Report(context, invocation, CaseEnumShapePin);
			}

			return;
		}

		if (IsForeignNonPublicReflection(invocation, method, containingType, arguments, context))
		{
			// Reported at the REFLECTION invocation, not at an assertion: Case C's locus class puts the call
			// in a non-attributed private helper, frequently inside no assertion at all.
			Report(context, invocation, CaseForeignNonPublicReflection);
		}
	}

	private static void Report(
		SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, string caseName)
		=> context.ReportDiagnostic(Diagnostic.Create(
			Rule,
			invocation.GetLocation(),
			caseName,
			GetEnclosingSymbolName(context.ContainingSymbol)));

	// ── SCOPE GATE ───────────────────────────────────────────────────────────

	/// <summary>
	/// True when an enclosing type declaration declares at least one method carrying a test attribute.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Membership is a property of the TYPE, not of the method containing the call and not of one syntax
	/// DECLARATION: Case C's locus class puts the reflection in a non-attributed <c>private static</c>
	/// helper, and restricting the gate to attributed methods would miss it entirely.
	/// </para>
	/// <para>
	/// Two-stage on purpose, to keep the perf contract on <c>InvocationExpression</c>. Stage 1 is
	/// syntax-only and decides the overwhelming majority — a test class that declares its own attributed
	/// method — with zero symbol work, walking OUTWARD so a type nested inside a test class is in scope
	/// too. Stage 2 runs ONLY when a declaration could hide an attributed method from its own member list,
	/// which is exactly two shapes: another <c>partial</c> part, or an inherited one behind a base list.
	/// For any other declaration the syntax answer is exact, so stage 2 is skipped. The type SYMBOL merges
	/// partial parts for free and its base chain covers inheritance.
	/// </para>
	/// </remarks>
	private static bool IsInTestScope(SyntaxNode node, ISymbol? containingSymbol)
	{
		var mayHideInheritedOrPartialMembers = false;

		for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
		{
			if (ancestor is not TypeDeclarationSyntax typeDeclaration) continue;

			if (DeclaresTestAttributedMethod(typeDeclaration)) return true;

			if (typeDeclaration.BaseList is not null)
			{
				mayHideInheritedOrPartialMembers = true;
				continue;
			}

			foreach (var modifier in typeDeclaration.Modifiers)
			{
				if (modifier.IsKind(SyntaxKind.PartialKeyword))
				{
					mayHideInheritedOrPartialMembers = true;
					break;
				}
			}
		}

		if (!mayHideInheritedOrPartialMembers) return false;

		for (var symbol = containingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
		{
			if (symbol is INamedTypeSymbol namedType && DeclaresTestAttributedMethod(namedType)) return true;
		}

		return false;
	}

	/// <summary>Stage 1 — the members of ONE declaration, syntax only.</summary>
	private static bool DeclaresTestAttributedMethod(TypeDeclarationSyntax typeDeclaration)
	{
		foreach (var member in typeDeclaration.Members)
		{
			if (member is not MethodDeclarationSyntax methodDeclaration) continue;

			foreach (var attributeList in methodDeclaration.AttributeLists)
			{
				foreach (var attribute in attributeList.Attributes)
				{
					if (IsTestAttributeName(GetAttributeSimpleName(attribute.Name))) return true;
				}
			}
		}

		return false;
	}

	/// <summary>Stage 2 — the members of the TYPE, merged across partial parts, plus its base chain.</summary>
	private static bool DeclaresTestAttributedMethod(INamedTypeSymbol type)
	{
		for (var current = type; current is not null; current = current.BaseType)
		{
			foreach (var member in current.GetMembers())
			{
				if (member is not IMethodSymbol method) continue;

				foreach (var attribute in method.GetAttributes())
				{
					var attributeName = attribute.AttributeClass?.Name;
					if (attributeName is not null && IsTestAttributeName(attributeName)) return true;
				}
			}
		}

		return false;
	}

	/// <summary>The right-most identifier of an attribute name, so <c>[Xunit.Fact]</c> and <c>[Fact]</c>
	/// match identically.</summary>
	private static string GetAttributeSimpleName(NameSyntax name) => name switch
	{
		QualifiedNameSyntax qualified => GetAttributeSimpleName(qualified.Right),
		AliasQualifiedNameSyntax aliased => GetAttributeSimpleName(aliased.Name),
		GenericNameSyntax generic => generic.Identifier.ValueText,
		SimpleNameSyntax simple => simple.Identifier.ValueText,
		_ => string.Empty,
	};

	private static bool IsTestAttributeName(string simpleName)
	{
		if (simpleName.Length == 0) return false;

		// `[FactAttribute]` and `[Fact]` are the same attribute, so the optional suffix is stripped first.
		if (simpleName.EndsWith("Attribute", StringComparison.Ordinal))
		{
			simpleName = simpleName.Substring(0, simpleName.Length - "Attribute".Length);
		}

		foreach (var suffix in TestAttributeSuffixes)
		{
			if (simpleName.EndsWith(suffix, StringComparison.Ordinal)) return true;
		}

		return false;
	}

	// ── CASE A: constant-tautology ───────────────────────────────────────────

	/// <summary>
	/// True when EVERY COMPARED argument is behavior-independent. The all-quantifier is the whole precision
	/// story: one operand whose value depends on the code under test makes the assertion meaningful. It
	/// quantifies over compared arguments only — a failure message is not an operand.
	/// </summary>
	private static bool IsConstantTautology(
		List<ExpressionSyntax> compared, SyntaxNodeAnalysisContext context)
	{
		foreach (var expression in compared)
		{
			if (!IsBehaviorIndependent(expression, context)) return false;
		}

		return true;
	}

	/// <summary>
	/// Splits an argument list into the arguments that participate in the assertion's verdict (COMPARED) and
	/// those that merely carry diagnostic text (CARRIED), returning false when the list cannot be
	/// classified.
	/// </summary>
	/// <remarks>
	/// Each argument is mapped to its PARAMETER — by name for a named argument, by position otherwise, with
	/// a trailing <c>params</c> parameter absorbing the tail. An argument that cannot be mapped means the
	/// rule cannot tell an operand from a message, so the caller stays silent rather than guessing.
	/// </remarks>
	private static bool TryGetComparedArguments(
		IMethodSymbol method,
		SeparatedSyntaxList<ArgumentSyntax> arguments,
		out List<ExpressionSyntax> compared)
	{
		compared = new List<ExpressionSyntax>(arguments.Count);
		var parameters = method.Parameters;

		for (var index = 0; index < arguments.Count; index++)
		{
			var argument = arguments[index];
			IParameterSymbol? parameter = null;

			if (argument.NameColon is { } nameColon)
			{
				var argumentName = nameColon.Name.Identifier.ValueText;
				foreach (var candidate in parameters)
				{
					if (string.Equals(candidate.Name, argumentName, StringComparison.Ordinal))
					{
						parameter = candidate;
						break;
					}
				}
			}
			else if (index < parameters.Length)
			{
				parameter = parameters[index];
			}
			else if (parameters.Length > 0 && parameters[parameters.Length - 1].IsParams)
			{
				parameter = parameters[parameters.Length - 1];
			}

			// Unmappable argument ⇒ cannot separate operand from message ⇒ conservative silence.
			if (parameter is null) return false;

			if (IsCarriedParameter(parameter)) continue;

			compared.Add(argument.Expression);
		}

		return true;
	}

	private static bool IsCarriedParameter(IParameterSymbol parameter)
		=> parameter.IsParams || CarriedParameterNames.Contains(parameter.Name);

	/// <summary>
	/// True when the assertion's fixed outcome is FAILURE rather than success — an unreachable-path marker.
	/// </summary>
	/// <remarks>
	/// Vacuity is specifically a fixed outcome of PASS. <c>Assert.True(false, "unreachable")</c> and
	/// <c>Assert.False(true, …)</c> are all-constant, but they FAIL whenever reached, so they are detectors
	/// for an unexpected branch. Reporting them would route a reader to deletion and silently convert a
	/// failing test into a passing one.
	/// </remarks>
	private static bool IsUnconditionalFailureMarker(
		IMethodSymbol method, List<ExpressionSyntax> compared, SyntaxNodeAnalysisContext context)
	{
		var expectsTrue = string.Equals(method.Name, "True", StringComparison.Ordinal);
		var expectsFalse = string.Equals(method.Name, "False", StringComparison.Ordinal);
		if (!expectsTrue && !expectsFalse) return false;

		foreach (var expression in compared)
		{
			var constant = context.SemanticModel.GetConstantValue(
				Unparenthesize(expression), context.CancellationToken);
			if (!constant.HasValue || constant.Value is not bool asserted) continue;

			// The asserted condition is fixed to the value that makes this assertion always fail.
			if (asserted != expectsTrue) return true;
		}

		return false;
	}

	/// <summary>
	/// An expression whose value cannot depend on the behavior of the code under test.
	/// </summary>
	/// <remarks>
	/// This is the CORRECTED predicate. The authority's own shape-1 wording ("every assertion operand is a
	/// compile-time constant") inverts the rule as written in C#: it misses <c>typeof(X)</c>, which is not a
	/// constant expression, and <c>typeof(X).IsPublic</c>, which is a property access — the two clearest
	/// true positives. The implemented predicate follows the stated INTENT instead: no operand's value
	/// depends on the behavior of the code under test.
	/// </remarks>
	private static bool IsBehaviorIndependent(ExpressionSyntax expression, SyntaxNodeAnalysisContext context)
	{
		expression = Unparenthesize(expression);

		// Literals, const reads, enum members, and casts of any of them.
		if (context.SemanticModel.GetConstantValue(expression, context.CancellationToken).HasValue)
			return true;

		// Type identity. Not a constant expression in C#, which is why the authority's wording misses it.
		if (expression is TypeOfExpressionSyntax) return true;

		// A declaration-name read performs no runtime dereference.
		if (IsNameOfExpression(expression)) return true;

		// Declaration metadata (`typeof(T).IsPublic`, `typeof(T).Name`, …). The RECEIVER condition is what
		// separates this from a behavior-dependent read: `obj.GetType().IsPublic` reads the same member
		// through a runtime value and must stay silent.
		if (expression is MemberAccessExpressionSyntax memberAccess)
		{
			if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol
				is { } member
				&& IsDeclarationMetadataContainer(member.ContainingType)
				&& IsBehaviorIndependent(memberAccess.Expression, context))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>True for <c>System.Type</c> and anything deriving from <c>System.Reflection.MemberInfo</c>
	/// — the declaration-metadata surfaces.</summary>
	private static bool IsDeclarationMetadataContainer(INamedTypeSymbol? type)
	{
		for (var current = type; current is not null; current = current.BaseType)
		{
			var displayName = current.ToDisplayString();
			if (string.Equals(displayName, SystemTypeDisplayName, StringComparison.Ordinal)) return true;
			if (string.Equals(displayName, MemberInfoDisplayName, StringComparison.Ordinal)) return true;
		}

		return false;
	}

	private static bool IsNameOfExpression(ExpressionSyntax expression)
		=> expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } };

	// ── CASE B: enum-shape-pin ───────────────────────────────────────────────

	/// <summary>
	/// True when the assertion is an EQUALITY-family assertion and some COMPARED argument's expression tree
	/// contains an <c>Enum.GetNames</c>/<c>Enum.GetValues</c> invocation — i.e. the assertion pins an enum's
	/// declaration shape rather than any behavior.
	/// </summary>
	/// <remarks>
	/// Searching COMPARED arguments only is what makes the declared "enumeration source is silent" boundary
	/// true of the property it is about rather than of the method name alone: an enum accessor formatted
	/// into a failure message, or projected into input for the code under test, pins nothing.
	/// </remarks>
	private static bool IsEnumShapePin(
		IMethodSymbol method,
		List<ExpressionSyntax> compared,
		SyntaxNodeAnalysisContext context)
	{
		if (!EqualityAssertionNames.Contains(method.Name)) return false;

		foreach (var expression in compared)
		{
			foreach (var node in expression.DescendantNodesAndSelf())
			{
				// Every descendant invocation is resolved through its SYMBOL, so the scan is indifferent to
				// whether the call was written `Enum.GetNames(…)` or bare under `using static System.Enum;`.
				if (node is not InvocationExpressionSyntax inner) continue;

				if (context.SemanticModel.GetSymbolInfo(inner, context.CancellationToken).Symbol
					is not IMethodSymbol innerMethod) continue;

				if (!EnumShapeAccessorNames.Contains(innerMethod.Name)) continue;

				// The containing-type check is what keeps a same-named local helper from matching.
				if (string.Equals(
						innerMethod.ContainingType?.ToDisplayString(),
						SystemEnumDisplayName,
						StringComparison.Ordinal))
				{
					return true;
				}
			}
		}

		return false;
	}

	// ── CASE C: foreign-nonpublic-reflection ─────────────────────────────────

	/// <summary>
	/// True when the invocation looks a NAMED non-public member up on a <c>typeof(T)</c> whose
	/// <c>T</c> lives in another assembly. All four conditions must hold; each one has an isolating
	/// negative fixture.
	/// </summary>
	private static bool IsForeignNonPublicReflection(
		InvocationExpressionSyntax invocation,
		IMethodSymbol method,
		INamedTypeSymbol containingType,
		SeparatedSyntaxList<ArgumentSyntax> arguments,
		SyntaxNodeAnalysisContext context)
	{
		// The member-lookup accessors on System.Type, and only those.
		if (!MemberLookupNames.Contains(method.Name)) return false;
		if (!string.Equals(
				containingType.ToDisplayString(), SystemTypeDisplayName, StringComparison.Ordinal))
		{
			return false;
		}

		// Condition 1 — the member name is a constant. Resolved through its PARAMETER, never its syntax
		// position: `GetMethod(bindingAttr: …, name: "Hidden")` binds the same overload, and reading
		// `arguments[0]` there inspects the FLAGS and silently misses the lookup.
		var nameExpression = ArgumentForParameterNamed(method, arguments, "name");
		if (nameExpression is null) return false;
		var nameConstant = context.SemanticModel.GetConstantValue(nameExpression, context.CancellationToken);
		if (!nameConstant.HasValue || nameConstant.Value is not string) return false;

		// Condition 2 — the binding flags request the NON-PUBLIC surface. An unresolvable flags argument
		// cannot be proven, so stay silent rather than guess.
		var flagsExpression = ArgumentForParameterNamed(method, arguments, "bindingAttr");
		if (flagsExpression is null) return false;
		var flagsConstant = context.SemanticModel.GetConstantValue(
			flagsExpression, context.CancellationToken);
		if (!flagsConstant.HasValue || flagsConstant.Value is not int flags) return false;
		if ((flags & BindingFlagsNonPublic) == 0) return false;

		// Condition 3 — the receiver is a compile-time type identity. A runtime receiver
		// (`obj.GetType()`) leaves the reflected type undecidable, so ownership cannot be proven.
		if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
		if (Unparenthesize(memberAccess.Expression) is not TypeOfExpressionSyntax typeOf) return false;

		var reflectedType = context.SemanticModel.GetTypeInfo(typeOf.Type, context.CancellationToken).Type;
		if (reflectedType is null) return false;

		// Condition 4 — the compilation does NOT own the reflected type. Testing your own internals is
		// ordinary; the hazard is pinning ANOTHER assembly's private declaration surface.
		return !SymbolEqualityComparer.Default.Equals(
			reflectedType.ContainingAssembly, context.SemanticModel.Compilation.Assembly);
	}

	/// <summary>
	/// The expression bound to the parameter with the given NAME, resolved through the shared
	/// parameter→argument binder so a reordered named argument cannot evade the predicate.
	/// </summary>
	private static ExpressionSyntax? ArgumentForParameterNamed(
		IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, string parameterName)
	{
		for (var ordinal = 0; ordinal < method.Parameters.Length; ordinal++)
		{
			if (!string.Equals(method.Parameters[ordinal].Name, parameterName, StringComparison.Ordinal))
				continue;

			return AnalyzerArgumentBinding.FindArgumentForParameter(method, arguments, ordinal);
		}

		return null;
	}

	// ── SHARED ───────────────────────────────────────────────────────────────

	private static ExpressionSyntax Unparenthesize(ExpressionSyntax expression)
	{
		while (expression is ParenthesizedExpressionSyntax parenthesized)
		{
			expression = parenthesized.Expression;
		}

		return expression;
	}

	/// <summary>
	/// The enclosing METHOD's name, falling back to the enclosing type's. The message argument must name
	/// the member a reader has to inspect: Case C's reflection lives in a private helper, and a type name
	/// would point at the wrong place.
	/// </summary>
	private static string GetEnclosingSymbolName(ISymbol? containingSymbol)
	{
		for (var symbol = containingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
		{
			if (symbol is IMethodSymbol method) return method.Name;
		}

		for (var symbol = containingSymbol; symbol is not null; symbol = symbol.ContainingSymbol)
		{
			if (symbol is INamedTypeSymbol namedType) return namedType.Name;
		}

		return "<unknown>";
	}

	/// <summary>
	/// Walks the symbol's containment chain (member → containing type → containing assembly) and returns
	/// true if any link carries the FQN-matched bypass attribute.
	/// </summary>
	private static bool HasCodeStyleBypassInScope(ISymbol symbol)
	{
		for (ISymbol? current = symbol; current is not null; current = current.ContainingSymbol)
		{
			if (HasCodeStyleBypassAttribute(current)) return true;
		}

		return false;
	}

	/// <summary>
	/// FQN-strict bypass check. Matches <c>NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute</c> only, so a
	/// locally-declared shadow attribute in an unrelated namespace cannot silently disable the rule.
	/// </summary>
	private static bool HasCodeStyleBypassAttribute(ISymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			var attributeClass = attribute.AttributeClass;
			if (attributeClass is null) continue;

			var fullName = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fullName.StartsWith("global::", StringComparison.Ordinal))
			{
				fullName = fullName.Substring("global::".Length);
			}

			if (string.Equals(fullName, CodeStyleBypassAttributeFullName, StringComparison.Ordinal))
				return true;
		}

		return false;
	}
}
