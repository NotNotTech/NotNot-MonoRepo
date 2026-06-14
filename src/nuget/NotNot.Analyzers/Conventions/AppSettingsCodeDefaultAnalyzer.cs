using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NotNot.Analyzers.Diagnostics;

namespace NotNot.Analyzers.Conventions;

/// <summary>
/// Analyzer enforcing that the default value of a <c>NotNot.AppSettings</c>-generated settings option
/// lives ONLY in the <c>appsettings*.json</c> source-of-truth — never as a code-side
/// <c>{settingsOption} ?? {literal|const}</c> fallback (NN_C004).
/// </summary>
/// <remarks>
/// <para>
/// <b>Rationale</b>: The <c>NotNot.AppSettings</c> source generator makes <c>appsettings*.json</c> the
/// single source of truth for settings defaults (every generated property is nullable <c>T?</c>). A
/// code-side <c>?? &lt;default&gt;</c> is a SECOND, drifting copy of a default that already lives in the
/// JSON, and it SILENTLY MASKS a missing/null configuration value — the value should fail loud at startup,
/// not limp on a hardcoded fallback. Keep ONE default location (the JSON) and prefer fail-loud over
/// fail-quiet.
/// </para>
/// <para>
/// <b>Detection</b>: fires on a coalesce expression <c>a ?? b</c> where
/// <list type="number">
///   <item><description>
///     the LEFT operand (after stripping casts / parentheses / conditional-access) resolves to a
///     property or field whose containing type carries
///     <c>[System.CodeDom.Compiler.GeneratedCode("NotNot.AppSettings", ...)]</c> — the reliable
///     generator marker, robust to namespace/version; AND
///   </description></item>
///   <item><description>
///     the RIGHT operand is a DEFAULT-VALUE shape: a numeric / string / bool / char literal (optionally
///     a leading unary <c>-</c>/<c>+</c> on a numeric literal), or a reference to a <c>const</c> /
///     <c>static readonly</c> field (e.g. <c>SomeType.DefaultMaxOpenPty</c>).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Silent (out of scope)</b>:
/// <list type="bullet">
///   <item><description>
///     <c>settings.X ?? throw …</c> — the PERMITTED required-no-default pattern (fail loud).
///   </description></item>
///   <item><description>
///     a left operand that is NOT a settings option (local variable, or a member of a type without the
///     <c>[GeneratedCode("NotNot.AppSettings")]</c> marker).
///   </description></item>
///   <item><description>
///     a right operand that is computed: a method invocation (<c>?? Compute()</c>) or a property read
///     (<c>?? Environment.ProcessorCount</c>) — these cannot live in static JSON.
///   </description></item>
///   <item><description>
///     a coalesce expression INSIDE generated code (e.g. the <c>*.g.cs</c> settings files themselves) —
///     skipped via <see cref="GeneratedCodeAnalysisFlags.None"/>.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AppSettingsCodeDefaultAnalyzer : DiagnosticAnalyzer
{
	/// <summary>
	/// Diagnostic ID for the analyzer rule.
	/// </summary>
	public const string DiagnosticId = "NN_C004";

	private const string Category = "CodeStyle";

	/// <summary>
	/// The tool name emitted as the first <c>[GeneratedCode]</c> constructor argument on every
	/// <c>NotNot.AppSettings</c>-generated settings class (see
	/// <c>NotNot.AppSettings/AppSettingsGen.cs</c>).
	/// </summary>
	private const string AppSettingsToolName = "NotNot.AppSettings";

	private const string GeneratedCodeAttributeFullName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

	private static readonly LocalizableString Title =
		"Code-side default for an AppSettings option";

	private static readonly LocalizableString MessageFormat =
		"Code-side default for AppSettings option '{0}'. Settings defaults must live in the appsettings*.json "
		+ "source-of-truth (NotNot.AppSettings). Fix: (1) set the default in appsettings*.json and read the typed "
		+ "property directly (remove the '?? default'); (2) if the value is REQUIRED with no sensible default, "
		+ "fail loud with '?? throw' instead of a silent fallback; (3) if genuinely optional, branch on null as a "
		+ "real state. Suppress only if truly intended (#pragma warning disable NN_C004 / .editorconfig).";

	private static readonly LocalizableString Description =
		"A NotNot.AppSettings-generated settings option already has its default defined in the appsettings*.json "
		+ "source-of-truth (every generated property is nullable T?). A code-side '?? <literal/const>' fallback is "
		+ "a SECOND, drifting copy of that default AND it silently masks a missing/null configuration value that "
		+ "should fail loud at startup rather than limp on a hardcoded value. Keep ONE default location (the JSON) "
		+ "and prefer fail-loud over fail-quiet: a missing config should throw at startup, not be masked by '?? 4'. "
		+ "The PERMITTED required-value pattern is '?? throw' (fail loud). Computed fallbacks (method calls, property "
		+ "reads such as Environment.ProcessorCount) are out of scope — they cannot live in static JSON. Suppress "
		+ "with '#pragma warning disable NN_C004' or a '.editorconfig' severity override only when a code-side "
		+ "default is genuinely intended.";

	private static readonly DiagnosticDescriptor Rule = new(
		id: DiagnosticId,
		title: Title,
		messageFormat: MessageFormat,
		category: Category,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: Description,
		helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#{DiagnosticId}",
		customTags: new[] { "CodeStyle", "Conventions" });

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.EnableConcurrentExecution();

		// None => the analyzer is never invoked for nodes in generated code. This is what makes the
		// `?? default` smell INSIDE a generated *.g.cs settings file silent — the smell only matters in
		// non-generated consumer code. (The [GeneratedCode] check below is on the REFERENCED settings
		// type, a separate concern from whether the ANALYZED file is generated.)
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

		context.RegisterSyntaxNodeAction(AnalyzeCoalesce, SyntaxKind.CoalesceExpression);
	}

	private static void AnalyzeCoalesce(SyntaxNodeAnalysisContext context)
	{
		using var _ = AnalyzerPerformanceTracker.StartTracking(DiagnosticId, "AnalyzeCoalesce");

		var coalesce = (BinaryExpressionSyntax)context.Node;

		// Right operand must be a default-VALUE shape (literal or const / static-readonly field).
		// throw-expressions, method calls, and property reads are computed → out of scope.
		if (!IsDefaultValueShape(coalesce.Right, context.SemanticModel, context.CancellationToken))
			return;

		// Left operand must resolve to a settings option (member of a [GeneratedCode("NotNot.AppSettings")]
		// type). Anything else → not a settings default → silent.
		var memberName = TryGetAppSettingsOptionName(coalesce.Left, context.SemanticModel, context.CancellationToken);
		if (memberName is null)
			return;

		var diagnostic = Diagnostic.Create(Rule, coalesce.OperatorToken.GetLocation(), memberName);
		context.ReportDiagnostic(diagnostic);
	}

	/// <summary>
	/// If <paramref name="left"/> (after stripping casts / parentheses / conditional-access) resolves to a
	/// property or field on a <c>NotNot.AppSettings</c>-generated type, returns the member name; otherwise
	/// <c>null</c>.
	/// </summary>
	private static string? TryGetAppSettingsOptionName(ExpressionSyntax left, SemanticModel model, CancellationToken ct)
	{
		var target = StripToMemberExpression(left);
		if (target is null)
			return null;

		var symbol = model.GetSymbolInfo(target, ct).Symbol;
		if (symbol is not IPropertySymbol and not IFieldSymbol)
			return null;

		var containingType = symbol.ContainingType;
		if (containingType is null || !IsAppSettingsGeneratedType(containingType))
			return null;

		return symbol.Name;
	}

	/// <summary>
	/// Peels casts, parentheses, and conditional-access off an expression to reach the underlying
	/// member-access (or member-binding for a <c>?.</c> chain). Returns <c>null</c> when the expression is
	/// not ultimately a member access (e.g. a bare local/parameter identifier).
	/// </summary>
	private static ExpressionSyntax? StripToMemberExpression(ExpressionSyntax expr)
	{
		while (true)
		{
			switch (expr)
			{
				case ParenthesizedExpressionSyntax paren:
					expr = paren.Expression;
					break;
				case CastExpressionSyntax cast:
					expr = cast.Expression;
					break;
				case ConditionalAccessExpressionSyntax conditional:
					// `settings.Sessions?.MaxOpenPty` → resolve the WhenNotNull member-binding chain, whose
					// symbol is the accessed member (MaxOpenPty) on the nested settings type.
					expr = conditional.WhenNotNull;
					break;
				case MemberAccessExpressionSyntax:
				case MemberBindingExpressionSyntax:
					return expr;
				default:
					return null;
			}
		}
	}

	/// <summary>
	/// True iff <paramref name="right"/> is a default-VALUE shape: a numeric / string / bool / char literal
	/// (optionally a leading unary <c>-</c>/<c>+</c> on a numeric literal), or a reference to a <c>const</c>
	/// or <c>static readonly</c> field. Method invocations, property reads, <c>throw</c> expressions, and
	/// <c>null</c>/<c>default</c> literals are NOT default-value shapes.
	/// </summary>
	private static bool IsDefaultValueShape(ExpressionSyntax right, SemanticModel model, CancellationToken ct)
	{
		var expr = right;

		// Allow a leading unary +/- on a numeric literal (e.g. `?? -1`).
		if (expr is PrefixUnaryExpressionSyntax unary
			&& (unary.IsKind(SyntaxKind.UnaryMinusExpression) || unary.IsKind(SyntaxKind.UnaryPlusExpression)))
		{
			expr = unary.Operand;
		}

		if (expr is LiteralExpressionSyntax literal)
		{
			return literal.Kind() switch
			{
				SyntaxKind.NumericLiteralExpression => true,
				SyntaxKind.StringLiteralExpression => true,
				SyntaxKind.CharacterLiteralExpression => true,
				SyntaxKind.TrueLiteralExpression => true,
				SyntaxKind.FalseLiteralExpression => true,
				_ => false, // null literal, default literal, etc. — not a default VALUE.
			};
		}

		// A const or static-readonly field used as a canonical default (e.g. PtyRegistryOptions.DefaultMaxOpenPty).
		var symbol = model.GetSymbolInfo(right, ct).Symbol;
		return symbol is IFieldSymbol field && (field.IsConst || (field.IsStatic && field.IsReadOnly));
	}

	/// <summary>
	/// True iff <paramref name="type"/> carries
	/// <c>[System.CodeDom.Compiler.GeneratedCode("NotNot.AppSettings", ...)]</c>. Matches on the FQN of the
	/// attribute and the first constructor argument (the tool name) — robust to the generated namespace and
	/// version string.
	/// </summary>
	private static bool IsAppSettingsGeneratedType(INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass is null)
				continue;

			var fullName = attrClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (fullName.StartsWith("global::", StringComparison.Ordinal))
				fullName = fullName.Substring("global::".Length);

			if (!string.Equals(fullName, GeneratedCodeAttributeFullName, StringComparison.Ordinal))
				continue;

			var ctorArgs = attribute.ConstructorArguments;
			if (ctorArgs.Length >= 1
				&& ctorArgs[0].Value is string toolName
				&& string.Equals(toolName, AppSettingsToolName, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
