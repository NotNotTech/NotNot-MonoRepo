using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Diagnostics;

/// <summary>
/// NN_CULTURE_BANNED — prohibits direct access to
/// <c>CultureInfo.CurrentCulture</c>, <c>CultureInfo.CurrentUICulture</c>,
/// <c>CultureInfo.DefaultThreadCurrentCulture</c>, and
/// <c>CultureInfo.DefaultThreadCurrentUICulture</c> outside sites annotated with
/// <c>[Novaleaf.VibeOverwatch.Shared.Infrastructure.Localization.AllowCultureApiAttribute]</c>.
/// For localization reads, inject <c>ICultureProvider</c>.
/// </summary>
/// <remarks>
/// Authored 2026-04-17 as the D4 literal compile-time enforcement for the VOW
/// localization DevUX flicker fix. See
/// <c>.vibeOverwatch/memory/20260417-1945-PLAN-loc-flicker-fix-iter1.Vibe.TDD.md § Phase 3</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NnCultureBannedAnalyzer : DiagnosticAnalyzer
{
	/// <summary>Diagnostic ID.</summary>
	public const string DiagnosticId = "NN_CULTURE_BANNED";

	private static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		title: "Direct CultureInfo API use is banned; inject ICultureProvider or annotate [AllowCultureApi].",
		messageFormat: "'{0}' is banned outside [AllowCultureApi]-annotated scopes. Inject ICultureProvider for localization reads; add [AllowCultureApi(reason)] to the enclosing member or type for intentional uses.",
		category: "NotNot_Architecture",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "Prevents regression of VOW localization flicker (Apr-17) and English-flash (Apr-4) bugs.");

	/// <inheritdoc/>
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	private static readonly HashSet<string> BannedMembers = new()
	{
		"CurrentCulture",
		"CurrentUICulture",
		"DefaultThreadCurrentCulture",
		"DefaultThreadCurrentUICulture",
	};

	private const string AllowAttrFullName =
		"Novaleaf.VibeOverwatch.Shared.Infrastructure.Localization.AllowCultureApiAttribute";

	/// <inheritdoc/>
	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		// Compilation-scoped start: only wire the syntax-node action when the
		// [AllowCultureApi] attribute type is actually resolvable in the compilation.
		// This scopes the analyzer to VOW projects (which reference VOW.Shared where
		// the attribute lives) and avoids firing on transitive analyzer applications
		// (e.g., NotNot.Bcl.Core consumed as a PackageReference — its own compilation
		// has no [AllowCultureApi] attribute definition).
		context.RegisterCompilationStartAction(startCtx =>
		{
			var allowAttrSymbol = startCtx.Compilation.GetTypeByMetadataName(AllowAttrFullName);
			if (allowAttrSymbol is null) return;
			startCtx.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
		});
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext ctx)
	{
		var memberAccess = (MemberAccessExpressionSyntax)ctx.Node;
		var memberName = memberAccess.Name.Identifier.ValueText;
		if (!BannedMembers.Contains(memberName)) return;

		var symbol = ctx.SemanticModel.GetSymbolInfo(memberAccess).Symbol;
		var containingType = symbol?.ContainingType;
		if (containingType is null) return;
		if (containingType.ToDisplayString() != "System.Globalization.CultureInfo") return;

		// Walk enclosing scopes — method → property → constructor → accessor → local-function
		// → class/struct/record. If ANY enclosing scope bears [AllowCultureApi], suppress
		// the diagnostic.
		if (IsEnclosedByAllowAttribute(ctx.Node, ctx.SemanticModel)) return;

		ctx.ReportDiagnostic(Diagnostic.Create(
			Rule, memberAccess.GetLocation(), $"CultureInfo.{memberName}"));
	}

	private static bool IsEnclosedByAllowAttribute(SyntaxNode node, SemanticModel model)
	{
		SyntaxNode? cur = node;
		while (cur is not null)
		{
			ISymbol? sym = cur switch
			{
				MethodDeclarationSyntax m => model.GetDeclaredSymbol(m),
				ConstructorDeclarationSyntax c => model.GetDeclaredSymbol(c),
				PropertyDeclarationSyntax p => model.GetDeclaredSymbol(p),
				AccessorDeclarationSyntax a => model.GetDeclaredSymbol(a),
				LocalFunctionStatementSyntax lf => model.GetDeclaredSymbol(lf),
				ClassDeclarationSyntax cl => model.GetDeclaredSymbol(cl),
				StructDeclarationSyntax st => model.GetDeclaredSymbol(st),
				RecordDeclarationSyntax r => model.GetDeclaredSymbol(r),
				_ => null,
			};
			if (sym is not null && HasAllowAttribute(sym)) return true;
			cur = cur.Parent;
		}
		return false;
	}

	private static bool HasAllowAttribute(ISymbol sym)
	{
		foreach (var attr in sym.GetAttributes())
		{
			var attrClass = attr.AttributeClass?.ToDisplayString();
			if (attrClass == AllowAttrFullName) return true;
		}
		return false;
	}
}
