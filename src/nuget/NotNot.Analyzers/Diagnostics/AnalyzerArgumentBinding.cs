using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NotNot.Analyzers.Diagnostics;

/// <summary>
/// Analyzer-infrastructure helper for binding an invocation's ARGUMENTS to their PARAMETERS.
/// </summary>
/// <remarks>
/// Family-neutral on purpose: NN_R011 (Reliability) and NN_C006 (Conventions) both need the same predicate,
/// so it lives in the shared <c>Diagnostics/</c> infrastructure folder rather than under either family.
/// <c>internal</c> keeps it off the package's public surface — there is no consumer outside this assembly.
/// </remarks>
internal static class AnalyzerArgumentBinding
{
	/// <summary>
	/// The expression bound to the parameter at <paramref name="parameterOrdinal" />, or null when no
	/// argument supplies it.
	/// </summary>
	/// <remarks>
	/// A named argument is matched by parameter NAME. An unnamed argument binds by position, which is exact
	/// rather than approximate: C# requires a positional argument to appear in its own parameter's position,
	/// so syntax index == parameter ordinal always holds for one. Returning null for an absent parameter
	/// keeps the caller conservative — an optional argument left off simply is not the proof the caller seeks.
	/// </remarks>
	internal static ExpressionSyntax? FindArgumentForParameter(
		IMethodSymbol method,
		SeparatedSyntaxList<ArgumentSyntax> arguments,
		int parameterOrdinal)
	{
		if (parameterOrdinal >= method.Parameters.Length) return null;
		var parameterName = method.Parameters[parameterOrdinal].Name;

		for (var index = 0; index < arguments.Count; index++)
		{
			var argument = arguments[index];

			if (argument.NameColon is { } nameColon)
			{
				if (string.Equals(
						nameColon.Name.Identifier.ValueText, parameterName, StringComparison.Ordinal))
				{
					return argument.Expression;
				}

				continue;
			}

			if (index == parameterOrdinal) return argument.Expression;
		}

		return null;
	}
}
