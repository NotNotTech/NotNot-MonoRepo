// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Net;

namespace NotNot.Diagnostics;

/// <summary>
/// Maps a structured <see cref="Problem"/> — the canonical error transport — to a human-readable,
/// framework-neutral explanation string.
/// </summary>
/// <remarks>
/// <para>
/// <b>Canonical transport.</b> Every error source normalizes to a <see cref="Problem"/> first, then
/// the registry explains the <see cref="Problem"/>. <see cref="Explain(Exception)"/> normalizes via
/// <see cref="Problem.FromEx"/>; <see cref="Explain{TValue}(Maybe{TValue})"/> uses the failed Maybe's
/// <see cref="Maybe{TValue}.Problem"/>. Both converge on <see cref="Explain(Problem)"/>.
/// </para>
/// <para>
/// <b>Registration axes.</b> Explainers register against <see cref="Problem"/>'s OWN canonical fields
/// (no parallel key space): originating exception <see cref="Type"/> (via <see cref="Problem.Ex"/>),
/// <see cref="Problem.category"/> (see <see cref="Problem.CategoryNames"/>), and
/// <see cref="Problem.Status"/> (<see cref="HttpStatusCode"/>) — the same shape System.Text.Json uses
/// to register custom converters by type. Re-registering the same key overwrites (last-registered
/// wins), so a host overrides a library default by registering the same key after composition.
/// </para>
/// <para>
/// <b>Resolution order (first wins)</b>, the SPEC-009 contract:
/// <list type="number">
///   <item>most-specific registered explainer: exception <see cref="Type"/> via
///     <see cref="Problem.Ex"/> (most-derived first, walking the base chain) → matched
///     <see cref="Problem.category"/> → matched <see cref="Problem.Status"/>;</item>
///   <item><see cref="Problem.Detail"/>;</item>
///   <item><see cref="Problem.Title"/>;</item>
///   <item>a generic last-resort string.</item>
/// </list>
/// Output is a string (never a RenderFragment / localized object) so the mechanism stays
/// framework-neutral and Core-hostable; rich rendering + localization live in the consumer's
/// explainer closure. No auto-retry is offered.
/// </para>
/// </remarks>
public interface IErrorExplanationRegistry
{
	/// <summary>
	/// Registers <paramref name="explainer"/> for problems whose <see cref="Problem.Ex"/> is
	/// (or derives from) <typeparamref name="TException"/>. Last-registered wins per exact type.
	/// </summary>
	IErrorExplanationRegistry RegisterByException<TException>(Func<Problem, string> explainer)
		where TException : Exception;

	/// <summary>
	/// Registers <paramref name="explainer"/> for problems whose <see cref="Problem.Ex"/> is
	/// (or derives from) <paramref name="exceptionType"/>. Last-registered wins per exact type.
	/// </summary>
	IErrorExplanationRegistry RegisterByException(Type exceptionType, Func<Problem, string> explainer);

	/// <summary>
	/// Registers <paramref name="explainer"/> for problems whose <see cref="Problem.category"/> equals
	/// <paramref name="category"/> (ordinal). Last-registered wins per category.
	/// </summary>
	IErrorExplanationRegistry RegisterByCategory(string category, Func<Problem, string> explainer);

	/// <summary>
	/// Registers <paramref name="explainer"/> for problems whose <see cref="Problem.Status"/> equals
	/// <paramref name="status"/>. Last-registered wins per status.
	/// </summary>
	IErrorExplanationRegistry RegisterByStatus(HttpStatusCode status, Func<Problem, string> explainer);

	/// <summary>
	/// Explains <paramref name="problem"/> per the resolution order documented on
	/// <see cref="IErrorExplanationRegistry"/>. Never throws while explaining (the explanation path
	/// is robust); always returns a non-empty string.
	/// </summary>
	string Explain(Problem problem);

	/// <summary>
	/// Normalizes <paramref name="exception"/> via <see cref="Problem.FromEx"/> and explains the
	/// resulting <see cref="Problem"/>.
	/// </summary>
	string Explain(Exception exception);

	/// <summary>
	/// Explains the <see cref="Problem"/> carried by a failed <paramref name="failed"/> Maybe.
	/// Throws <see cref="ArgumentException"/> if the Maybe is successful (no error to explain).
	/// </summary>
	string Explain<TValue>(Maybe<TValue> failed);
}
