using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Analyzer enforcing the project's <c>BOOLEAN_DEFAULT_FALSE</c> convention — boolean
/// parameters, auto-properties, and fields must default to <c>false</c>, not <c>true</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rationale</b>: Default-<c>true</c> booleans cause silent behavioral drift on consumers
/// who don't know to flip them; default-<c>false</c> is opt-in discoverable. The fix is
/// usually a rename: name the positive (active) condition so default-<c>false</c> = inactive.
/// Examples: <c>SkipValidation</c> instead of <c>Validate=true</c>; <c>SkipGitIgnore</c>
/// instead of <c>RespectGitignore=true</c>. Prefer natural-English negation verbs
/// (<c>Skip*</c>, <c>Omit*</c>, <c>Disable*</c>, <c>Suppress*</c>, <c>No*</c>) over awkward
/// prefixed antonyms (<c>Dis-</c>, <c>Un-</c>, <c>Non-</c>).
/// </para>
/// <para>
/// <b>Detection surfaces</b> (all check <c>bool</c> and <c>bool?</c>, only fire on a literal
/// <c>true</c> default):
/// <list type="bullet">
///   <item><description>
///     <b>Parameters</b> with optional default value — method · constructor · record
///     positional · primary-constructor. <b>Always enforced regardless of method or type
///     visibility</b> — parameters define the call-site contract, which is part of the
///     convention surface even for private methods. Local-function parameters and lambda
///     parameters do not support defaults syntactically and are out of scope.
///   </description></item>
///   <item><description>
///     <b>Property declarations</b> with an auto-initializer (<c>{ get; set; } = true;</c> or
///     <c>{ get; init; } = true;</c>). Properties with only a <c>get</c> accessor
///     (<c>{ get; } = true;</c>) are readonly-equivalent and exempt — the value cannot be
///     reassigned after construction. <c>private</c>, <c>protected</c>, and
///     <c>private protected</c> properties are exempt as implementation details (no
///     external consumer surface). Computed properties without initializers carry no
///     default state and are out of scope.
///   </description></item>
///   <item><description>
///     <b>Field declarations</b> with an initializer (<c>bool _x = true;</c>). Fields tagged
///     <c>const</c> or <c>readonly</c> (instance or static) are skipped — those declare a
///     value locked at construction, not a mutable default state. <c>private</c>,
///     <c>protected</c>, and <c>private protected</c> fields are exempt as implementation
///     details.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Bypass mechanisms</b>:
/// <list type="number">
///   <item><description>
///     <c>[assembly: NotNot.Bcl.Diagnostics.CodeStyleBypass]</c> — compilation-level short-
///     circuit; no <c>NN_C*</c> rule fires.
///   </description></item>
///   <item><description>
///     <c>[CodeStyleBypass]</c> on the containing type — type-scope bypass; rule skips for
///     all members declared inside the bypassed type.
///   </description></item>
///   <item><description>
///     <c>[CodeStyleBypass]</c> on the member (parameter/property/field) — narrowest scope;
///     rule skips only at this declaration.
///   </description></item>
///   <item><description>
///     <c>.editorconfig</c>: <c>dotnet_diagnostic.NN_C003.severity = none</c> — disables the
///     rule for the scope of the editorconfig file (folder or project tree). Coarser-grained;
///     prefer the attribute for narrowly documented member-level exceptions.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Visibility-based exemption rationale</b>: The convention exists to prevent
/// consumer-facing silent drift. <c>private</c>, <c>protected</c>, and
/// <c>private protected</c> members are not part of the API surface visible to external
/// consumers — they are implementation details. Within a single class or inheritance
/// hierarchy, the default-state intent is a local concern that the author controls
/// completely. The rule applies to <c>internal</c>, <c>protected internal</c>, and
/// <c>public</c> members where the "consumer forgot to override" risk is real.
/// <c>Internal</c> is enforced because it is visible to the whole assembly which
/// functionally matches "many consumers."
/// </para>
/// <para>
/// <b>Out of scope</b>:
/// <list type="bullet">
///   <item><description>
///     <b>Local variables</b> (<c>bool x = true;</c> inside a method body) — local state isn't
///     a "default" surface that consumers depend on.
///   </description></item>
///   <item><description>
///     <b>Non-literal defaults</b> (<c>bool X = Method();</c> · <c>bool X = SomeConst;</c>) —
///     the analyzer only fires on explicit <c>true</c> literals. Compile-time-constant
///     expressions evaluating to <c>true</c> are technically equivalent but statically
///     harder to identify without false positives on constants whose value is intentionally
///     environment-driven.
///   </description></item>
///   <item><description>
///     <b>Non-bool types</b> — only <c>bool</c> and <c>bool?</c>. Other types with a
///     "true-equivalent" sentinel (e.g. enums named <c>Yes</c>/<c>No</c>) are not in scope.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoolDefaultFalseAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NN_C003";

	private const string CodeStyleBypassAttributeFullName =
		"NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: "Boolean default value should be false",
		messageFormat:
			"Boolean {0} '{1}' defaults to 'true'. Boolean defaults should be 'false' to avoid silent behavioral drift. "
			+ "Fix options: (1) Rename to express the inverse condition (Skip*, Omit*, Disable*, Suppress*, No*) so default 'false' matches intent. "
			+ "(2) For fields: mark as 'readonly' to lock the value at construction. For properties: remove both 'set' and 'init' accessors (use '{ get; } = ...' instead). "
			+ "(3) Private/protected/private-protected fields and properties are exempt automatically — only public, internal, and protected-internal members on the API surface are enforced. "
			+ "(4) Annotate the member, type, or assembly with [NotNot.Bcl.Diagnostics.CodeStyleBypass] for narrowly documented exceptions. "
			+ "Note: parameters are enforced unconditionally regardless of method visibility.",
		category: "CodeStyle",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description:
			"Boolean parameters, properties, and fields must default to false. Default-true booleans cause silent "
			+ "behavioral drift on consumers who don't know to flip them; default-false is opt-in discoverable. "
			+ "Name the positive (active) condition so default-false = inactive: SkipValidation not Validate=true; "
			+ "SkipGitIgnore not RespectGitignore=true. Prefer natural-English negation verbs (Skip*, Omit*, "
			+ "Disable*, Suppress*, No*) over awkward prefixed antonyms (Dis-, Un-, Non-). For fields, marking "
			+ "as 'readonly' is an alternative fix; for properties, making them get-only ({ get; }) is an "
			+ "alternative fix. Private/protected fields and properties are auto-exempt as implementation "
			+ "details. Annotate scopes that genuinely require default-true with "
			+ "NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute.",
		helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
		customTags: new[] { "CodeStyle", "Conventions", "Naming" }
	);

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
		// Assembly-level bypass short-circuit — cheapest exit when an entire assembly opts out.
		if (HasCodeStyleBypassAttribute(context.Compilation.Assembly))
			return;

		context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
		context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
		context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
	}

	private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeParameter");

		var parameter = (ParameterSyntax)context.Node;

		// No default value → no violation possible
		if (parameter.Default is null)
			return;

		if (!IsTrueLiteral(parameter.Default.Value))
			return;

		if (!IsBoolType(parameter.Type, context.SemanticModel, context.CancellationToken))
			return;

		// Parameters are enforced unconditionally regardless of method/type visibility —
		// the convention surface is the call site, which exists even for private methods.
		// Only [CodeStyleBypass] opts out.

		var paramSymbol = context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken);
		if (paramSymbol is not null && HasCodeStyleBypassInScope(paramSymbol))
			return;

		// Pick a human-readable kind name for the message so the error reads naturally.
		var kindName = parameter.Parent?.Parent switch
		{
			RecordDeclarationSyntax => "record positional parameter",
			ConstructorDeclarationSyntax => "constructor parameter",
			ClassDeclarationSyntax => "primary-constructor parameter",
			StructDeclarationSyntax => "primary-constructor parameter",
			_ => "parameter",
		};

		// Pin the diagnostic to the literal `true` token itself rather than the full
		// EqualsValueClauseSyntax (`= true`) so tooling highlights the offending value
		// — matches the property/field handlers which use `Initializer.Value`.
		var location = parameter.Default.Value.GetLocation();
		var diagnostic = Diagnostic.Create(
			Rule,
			location,
			kindName,
			parameter.Identifier.ValueText);
		context.ReportDiagnostic(diagnostic);
	}

	private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzePropertyDeclaration");

		var prop = (PropertyDeclarationSyntax)context.Node;

		// Auto-property initializer absent → no default state set; out of scope.
		if (prop.Initializer is null)
			return;

		if (!IsTrueLiteral(prop.Initializer.Value))
			return;

		if (!IsBoolType(prop.Type, context.SemanticModel, context.CancellationToken))
			return;

		// Readonly-equivalent property (no `set`, no `init`) — value locked at construction, exempt.
		if (IsReadOnlyEquivalentProperty(prop))
			return;

		var propSymbol = context.SemanticModel.GetDeclaredSymbol(prop, context.CancellationToken);
		if (propSymbol is null)
			return;

		// Implementation-detail visibility (private, protected, private protected) → exempt.
		if (IsRestrictedAccessibility(propSymbol.DeclaredAccessibility))
			return;

		if (HasCodeStyleBypassInScope(propSymbol))
			return;

		var location = prop.Initializer.Value.GetLocation();
		var diagnostic = Diagnostic.Create(
			Rule,
			location,
			"property",
			prop.Identifier.ValueText);
		context.ReportDiagnostic(diagnostic);
	}

	private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeFieldDeclaration");

		var field = (FieldDeclarationSyntax)context.Node;

		// `const bool X = true` is a declared value, not a default state — skip.
		if (field.Modifiers.Any(SyntaxKind.ConstKeyword))
			return;

		// Any `readonly` field (instance or static) is locked at construction — semantic intent
		// matches the rule's goal of preventing silent default-true mutation. Exempt.
		if (field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
			return;

		if (!IsBoolType(field.Declaration.Type, context.SemanticModel, context.CancellationToken))
			return;

		foreach (var variable in field.Declaration.Variables)
		{
			if (variable.Initializer is null)
				continue;

			if (!IsTrueLiteral(variable.Initializer.Value))
				continue;

			var fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) as IFieldSymbol;
			if (fieldSymbol is null)
				continue;

			// Implementation-detail visibility (private, protected, private protected) → exempt.
			if (IsRestrictedAccessibility(fieldSymbol.DeclaredAccessibility))
				continue;

			if (HasCodeStyleBypassInScope(fieldSymbol))
				continue;

			var location = variable.Initializer.Value.GetLocation();
			var diagnostic = Diagnostic.Create(
				Rule,
				location,
				"field",
				variable.Identifier.ValueText);
			context.ReportDiagnostic(diagnostic);
		}
	}

	/// <summary>
	/// True iff <paramref name="type"/> resolves to <c>System.Boolean</c> or
	/// <c>System.Nullable&lt;System.Boolean&gt;</c>.
	/// </summary>
	private static bool IsBoolType(TypeSyntax? type, SemanticModel model, System.Threading.CancellationToken ct)
	{
		if (type is null)
			return false;

		var typeInfo = model.GetTypeInfo(type, ct);
		var t = typeInfo.Type;
		if (t is null)
			return false;

		if (t.SpecialType == SpecialType.System_Boolean)
			return true;

		if (t is INamedTypeSymbol named
			&& named.IsGenericType
			&& named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
			&& named.TypeArguments.Length == 1
			&& named.TypeArguments[0].SpecialType == SpecialType.System_Boolean)
			return true;

		return false;
	}

	/// <summary>
	/// True iff <paramref name="expr"/> is the literal token <c>true</c>. Non-literal
	/// expressions that evaluate to <c>true</c> (constants, method calls, ternaries) are
	/// out of scope by design — see class-level remarks.
	/// </summary>
	private static bool IsTrueLiteral(ExpressionSyntax expr)
	{
		return expr is LiteralExpressionSyntax literal
			&& literal.Kind() == SyntaxKind.TrueLiteralExpression;
	}

	/// <summary>
	/// True iff <paramref name="accessibility"/> is one of <see cref="Accessibility.Private"/>,
	/// <see cref="Accessibility.Protected"/>, or <see cref="Accessibility.ProtectedAndInternal"/>
	/// (the latter representing C#'s <c>private protected</c>). Members with these
	/// accessibilities are considered implementation details and not part of the
	/// consumer-facing API surface where the default-<c>false</c> rule applies.
	/// <c>Internal</c>, <c>ProtectedOrInternal</c> (<c>protected internal</c>), and
	/// <c>Public</c> remain enforced — <c>internal</c> is visible to the whole assembly
	/// which functionally matches "many consumers".
	/// </summary>
	private static bool IsRestrictedAccessibility(Accessibility accessibility)
	{
		return accessibility == Accessibility.Private
			|| accessibility == Accessibility.Protected
			|| accessibility == Accessibility.ProtectedAndInternal;
	}

	/// <summary>
	/// True iff <paramref name="prop"/> has no <c>set</c> accessor and no <c>init</c>
	/// accessor — i.e., a get-only property (<c>{ get; }</c>). Such properties cannot be
	/// reassigned after construction, making the default value effectively locked.
	/// Properties with <c>{ get; init; }</c> are <b>not</b> exempt (Option A): <c>init</c>
	/// is still consumer-facing through object-initializer syntax, and the consumer-
	/// forgets-to-override concern applies.
	/// </summary>
	private static bool IsReadOnlyEquivalentProperty(PropertyDeclarationSyntax prop)
	{
		if (prop.AccessorList is null)
			return false;
		foreach (var accessor in prop.AccessorList.Accessors)
		{
			var kind = accessor.Kind();
			if (kind == SyntaxKind.SetAccessorDeclaration)
				return false;
			if (kind == SyntaxKind.InitAccessorDeclaration)
				return false;
		}
		return true;
	}

	/// <summary>
	/// Walks the symbol's containment chain (member → containing type → containing assembly)
	/// and returns true if any link in the chain is annotated with the FQN-matched bypass
	/// attribute.
	/// </summary>
	private static bool HasCodeStyleBypassInScope(ISymbol symbol)
	{
		ISymbol? current = symbol;
		while (current is not null)
		{
			if (HasCodeStyleBypassAttribute(current))
				return true;
			current = current.ContainingSymbol;
		}
		return false;
	}

	/// <summary>
	/// FQN-strict bypass check. Matches by
	/// <c>NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute</c> only; locally-declared shadow
	/// attributes in unrelated namespaces are NOT honored. Prevents silent rule-disable via
	/// naming collision.
	/// </summary>
	private static bool HasCodeStyleBypassAttribute(ISymbol symbol)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass is null)
				continue;

			var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fullName.StartsWith("global::", StringComparison.Ordinal))
				fullName = fullName.Substring("global::".Length);

			if (string.Equals(fullName, CodeStyleBypassAttributeFullName, StringComparison.Ordinal))
				return true;
		}
		return false;
	}
}
