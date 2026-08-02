// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotNot.Diagnostics;

/// <summary>
/// <see cref="IServiceCollection"/> registration for <see cref="IErrorExplanationRegistry"/>.
/// </summary>
public static class zz_Extensions_IServiceCollection_ErrorExplanation
{
	/// <summary>
	/// Registers a singleton <see cref="IErrorExplanationRegistry"/> (empty — no explainers).
	/// </summary>
	/// <remarks>
	/// Follows the <c>AddX()</c> + <c>TryAdd</c> convention: a host (or a component library such as
	/// NnDesign) that registers its own pre-populated registry BEFORE this call keeps it (the
	/// <c>TryAdd</c> no-ops). Consumers add explainers by resolving the singleton and calling
	/// <c>RegisterByX</c> (last-registered wins per key) — there is one shared registry, not one per
	/// caller.
	/// </remarks>
	public static IServiceCollection AddErrorExplanationRegistry(this IServiceCollection services)
	{
		services.TryAddSingleton<IErrorExplanationRegistry, ErrorExplanationRegistry>();
		return services;
	}
}
