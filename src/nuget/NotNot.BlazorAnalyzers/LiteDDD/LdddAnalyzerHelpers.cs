using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NotNot.BlazorAnalyzers.LiteDDD;

/// <summary>
/// Shared helpers for the LiteDDD analyzer family (<c>NN_LDDD_001</c> through <c>NN_LDDD_005</c>).
/// Mirrors the helper-extraction strategy used by
/// <see cref="NnDesign.NnDesignMudBlazorPolicyAnalyzer"/> but with a distinct exception list:
/// LiteDDD carve-outs target <c>Pages/Samples/**</c> + the sibling-primitive layers
/// (<c>NotNot.BlazorDesign/</c>, <c>NotNot.BlazorComponents/</c>) rather than the
/// MudBlazor-policy carve-outs.
/// </summary>
/// <remarks>
/// <para>
/// Bypass detection (<see cref="HasLdddBypassAttribute(IAssemblySymbol)"/>) matches by
/// <em>simple attribute name</em> only — consumer assemblies may either reference the canonical
/// <c>NotNot.Bcl.Diagnostics.LdddBypassAttribute</c> directly or declare a local
/// <c>internal sealed class LdddBypassAttribute : Attribute</c>. This mirrors the
/// <see cref="NnDesign.NnDesignBypassAttribute"/> contract and preserves the analyzer-only
/// <c>ReferenceOutputAssembly="false"</c> ProjectReference pattern.
/// </para>
/// <para>
/// <see cref="IsServerAssembly(IAssemblySymbol)"/> uses the <c>.Server</c> suffix convention
/// (Wave 1, per VibeConsider §4.3). A future <c>[LdddServerAssembly]</c> opt-in fallback is
/// staged for Wave 2 but intentionally not wired here — Wave 1 ships zero-configuration.
/// </para>
/// </remarks>
internal static class LdddAnalyzerHelpers
{
	/// <summary>Simple-name of the canonical bypass attribute (<see cref="System.Attribute"/> subtype).</summary>
	internal const string LdddBypassAttributeSimpleName = "LdddBypassAttribute";

	/// <summary>
	/// Fully-qualified name of the canonical bypass attribute (defensive secondary match;
	/// the simple-name check above is the authoritative path).
	/// </summary>
	internal const string LdddBypassAttributeFullName = "NotNot.Bcl.Diagnostics.LdddBypassAttribute";

	/// <summary>Simple-name of the <c>[LdddDataService]</c> marker (Wave 2 consumer).</summary>
	internal const string LdddDataServiceAttributeSimpleName = "LdddDataServiceAttribute";

	/// <summary>Fully-qualified name of the <c>[LdddDataService]</c> marker (Wave 2 consumer).</summary>
	internal const string LdddDataServiceAttributeFullName = "NotNot.Bcl.Diagnostics.LdddDataServiceAttribute";

	/// <summary>Simple-name of the <c>[LdddDomainService]</c> marker (Wave 2 consumer).</summary>
	internal const string LdddDomainServiceAttributeSimpleName = "LdddDomainServiceAttribute";

	/// <summary>Fully-qualified name of the <c>[LdddDomainService]</c> marker (Wave 2 consumer).</summary>
	internal const string LdddDomainServiceAttributeFullName = "NotNot.Bcl.Diagnostics.LdddDomainServiceAttribute";

	/// <summary>
	/// Returns true when the given file path falls under a LiteDDD path-bucket exemption:
	/// <list type="bullet">
	///   <item><description><c>Pages/Samples/**</c> — pedagogical sample pages exempt by AGENTS.md policy.</description></item>
	///   <item><description><c>/NotNot.BlazorDesign/</c> — sibling wrapper layer, primitive consumer.</description></item>
	///   <item><description><c>/NotNot.BlazorComponents/</c> — sibling primitive layer alongside MudBlazor.</description></item>
	/// </list>
	/// Case-insensitive path matching after normalizing path separators to <c>/</c>.
	/// </summary>
	internal static bool IsExceptedPath(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return false;

		var p = filePath.Replace('\\', '/');

		// Pages/Samples/** — pedagogical sample pages
		if (p.IndexOf("/Pages/Samples/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		// Sibling-primitive / wrapper-layer libraries
		if (p.IndexOf("/NotNot.BlazorDesign/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		if (p.IndexOf("/NotNot.BlazorComponents/", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;

		return false;
	}

	/// <summary>
	/// Cheap structural pre-filter copied from
	/// <see cref="NnDesign.NnDesignMudBlazorPolicyAnalyzer.IsLikelyTypeOrNamespaceReference"/> —
	/// filters the millions of <c>IdentifierName</c> events that fire on every local-variable
	/// read down to type/namespace-position events worth a <c>SemanticModel.GetSymbolInfo</c>
	/// resolution.
	/// </summary>
	/// <remarks>
	/// Skips inner positions of compound names (the OUTERMOST identifier is the reported anchor).
	/// Returns true for type-syntax positions: type-args, object-creation type, cast/typeof,
	/// method/property return types, field/variable/parameter types, using directives.
	/// </remarks>
	internal static bool IsLikelyTypeOrNamespaceReference(IdentifierNameSyntax node)
	{
		var parent = node.Parent;

		// Skip inner positions of compound names — report only on the OUTERMOST identifier.
		// (a) Qualified-name right-side: `MudBlazor.Color` → fire on `MudBlazor`, skip `Color`.
		// (b) Member-access name-side: `Color.Primary` → fire on `Color`, skip `Primary`.
		if (parent is QualifiedNameSyntax qns && qns.Right == node)
			return false;
		if (parent is MemberAccessExpressionSyntax memberAccessRight && memberAccessRight.Name == node)
			return false;

		// TypeSyntax base catches QualifiedName (Left-side), Array, Generic, Nullable, etc.
		if (parent is TypeSyntax)
			return true;

		switch (parent)
		{
			case MemberAccessExpressionSyntax mae when mae.Expression == node:
			case TypeArgumentListSyntax:
			case ObjectCreationExpressionSyntax oce when oce.Type == node:
			case TypeOfExpressionSyntax:
			case CastExpressionSyntax cast when cast.Type == node:
			case UsingDirectiveSyntax:
			case MethodDeclarationSyntax md when md.ReturnType == node:
			case PropertyDeclarationSyntax pd when pd.Type == node:
			case FieldDeclarationSyntax:
			case VariableDeclarationSyntax vd when vd.Type == node:
			case ParameterSyntax ps when ps.Type == node:
				return true;
			default:
				return false;
		}
	}

	/// <summary>
	/// Returns true when the compilation's assembly carries an <c>[assembly: LdddBypass]</c>
	/// marker. Matched by <em>simple attribute name</em>
	/// (<c>AttributeClass.Name == "LdddBypassAttribute"</c>) — consumer assemblies may
	/// reference the canonical attribute or declare a local internal copy. Cost: O(N) over
	/// assembly attributes (typically N&lt;10).
	/// </summary>
	internal static bool HasLdddBypassAttribute(IAssemblySymbol assembly)
	{
		if (assembly == null)
			return false;

		foreach (var attribute in assembly.GetAttributes())
		{
			var attrClass = attribute.AttributeClass;
			if (attrClass == null)
				continue;

			if (string.Equals(attrClass.Name, LdddBypassAttributeSimpleName, StringComparison.Ordinal))
				return true;

			// Defensive secondary match against fully-qualified name — covers cases where the
			// simple name diverges (e.g. ReadOnlyAttribute-style nested-type scenarios).
			if (string.Equals(attrClass.ToDisplayString(), LdddBypassAttributeFullName, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Symbol-scope bypass check — returns true when the type/method carries an
	/// <c>[LdddBypass]</c> attribute, OR when any containing type up the chain does.
	/// Used by Wave 2 (NN_LDDD_003/005) for class-level / method-level suppression. Wave 1
	/// callers should use <see cref="HasLdddBypassAttribute(IAssemblySymbol)"/> for the
	/// assembly-level short-circuit.
	/// </summary>
	internal static bool HasLdddBypassAttribute(ISymbol? symbol)
	{
		var current = symbol;
		while (current != null)
		{
			foreach (var attribute in current.GetAttributes())
			{
				var attrClass = attribute.AttributeClass;
				if (attrClass == null)
					continue;

				if (string.Equals(attrClass.Name, LdddBypassAttributeSimpleName, StringComparison.Ordinal))
					return true;
				if (string.Equals(attrClass.ToDisplayString(), LdddBypassAttributeFullName, StringComparison.Ordinal))
					return true;
			}
			current = current.ContainingSymbol;
		}
		return false;
	}

	/// <summary>
	/// Returns true when the assembly identity name ends with <c>.Server</c>
	/// (<see cref="StringComparison.Ordinal"/>). Wave 1 detection is purely suffix-based per
	/// VibeConsider §4.3 — Wave 2 will add an <c>[LdddServerAssembly]</c> opt-in fallback for
	/// projects whose assembly names diverge from the convention.
	/// </summary>
	internal static bool IsServerAssembly(IAssemblySymbol? assembly)
	{
		if (assembly == null)
			return false;

		return assembly.Identity.Name.EndsWith(".Server", StringComparison.Ordinal);

		// Wave 2 staged addition (uncomment when [LdddServerAssembly] attribute lands):
		// || assembly.GetAttributes().Any(a =>
		//     a.AttributeClass?.Name == "LdddServerAssemblyAttribute"
		//     || a.AttributeClass?.ToDisplayString() == "NotNot.Bcl.Diagnostics.LdddServerAssemblyAttribute");
	}

	/// <summary>
	/// Returns true when the assembly identity name ends with <c>.Shared</c> or <c>.Client</c>
	/// (<see cref="StringComparison.Ordinal"/>). Gates LiteDDD rule firing: rules only fire
	/// inside Shared/Client compilations.
	/// </summary>
	internal static bool IsSharedOrClientAssembly(IAssemblySymbol? assembly)
	{
		if (assembly == null)
			return false;

		var name = assembly.Identity.Name;
		return name.EndsWith(".Shared", StringComparison.Ordinal)
			|| name.EndsWith(".Client", StringComparison.Ordinal);
	}

	/// <summary>
	/// Walks the inheritance chain of <paramref name="type"/> looking for a base type whose
	/// fully-qualified name (display form, no global prefix) equals
	/// <c>Microsoft.EntityFrameworkCore.DbContext</c>. Returns true if found.
	/// </summary>
	/// <remarks>
	/// Full-name match (not simple-name) avoids false positives on user types named
	/// <c>DbContext</c> in unrelated namespaces. Walks via
	/// <see cref="INamedTypeSymbol.BaseType"/> with explicit depth bound to defend against
	/// pathological inheritance graphs (depth&gt;100).
	/// </remarks>
	internal static bool IsEntityFrameworkDbContext(ITypeSymbol? type)
	{
		const int MaxDepth = 100;
		const string DbContextFullName = "Microsoft.EntityFrameworkCore.DbContext";

		var current = type as INamedTypeSymbol;
		var depth = 0;
		while (current != null && depth < MaxDepth)
		{
			var fullName = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			// SymbolDisplayFormat.FullyQualifiedFormat emits "global::Microsoft.EntityFrameworkCore.DbContext".
			// Strip the global:: prefix for comparison.
			if (fullName.StartsWith("global::", StringComparison.Ordinal))
				fullName = fullName.Substring("global::".Length);

			if (string.Equals(fullName, DbContextFullName, StringComparison.Ordinal))
				return true;

			current = current.BaseType;
			depth++;
		}
		return false;
	}
}
