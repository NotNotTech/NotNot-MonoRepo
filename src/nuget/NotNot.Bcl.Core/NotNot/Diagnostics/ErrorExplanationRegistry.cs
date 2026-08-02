// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Concurrent;
using System.Net;

namespace NotNot.Diagnostics;

/// <summary>
/// Default <see cref="IErrorExplanationRegistry"/>. DI-registrable singleton; thread-safe so
/// composition-time registration and runtime explanation may interleave. Resolution order +
/// registration axes are documented on <see cref="IErrorExplanationRegistry"/>.
/// </summary>
public sealed class ErrorExplanationRegistry : IErrorExplanationRegistry
{
	/// <summary>
	/// Returned when no registered explainer matches and the <see cref="Problem"/> carries neither a
	/// <see cref="Problem.Detail"/> nor a <see cref="Problem.Title"/>.
	/// </summary>
	public const string GenericFallback = "An unexpected error occurred.";

	private readonly ConcurrentDictionary<Type, Func<Problem, string>> _byException = new();
	private readonly ConcurrentDictionary<string, Func<Problem, string>> _byCategory = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<HttpStatusCode, Func<Problem, string>> _byStatus = new();

	/// <inheritdoc />
	public IErrorExplanationRegistry RegisterByException<TException>(Func<Problem, string> explainer)
		where TException : Exception
		=> RegisterByException(typeof(TException), explainer);

	/// <inheritdoc />
	public IErrorExplanationRegistry RegisterByException(Type exceptionType, Func<Problem, string> explainer)
	{
		ArgumentNullException.ThrowIfNull(exceptionType);
		ArgumentNullException.ThrowIfNull(explainer);
		_byException[exceptionType] = explainer;
		return this;
	}

	/// <inheritdoc />
	public IErrorExplanationRegistry RegisterByCategory(string category, Func<Problem, string> explainer)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(category);
		ArgumentNullException.ThrowIfNull(explainer);
		_byCategory[category] = explainer;
		return this;
	}

	/// <inheritdoc />
	public IErrorExplanationRegistry RegisterByStatus(HttpStatusCode status, Func<Problem, string> explainer)
	{
		ArgumentNullException.ThrowIfNull(explainer);
		_byStatus[status] = explainer;
		return this;
	}

	/// <inheritdoc />
	public string Explain(Problem problem)
	{
		ArgumentNullException.ThrowIfNull(problem);

		// (1) most-specific registered explainer: exception Type (most-derived first) → category → Status.
		var ex = problem.Ex;
		if (ex is not null)
		{
			for (var type = ex.GetType(); type is not null; type = type.BaseType)
			{
				if (_byException.TryGetValue(type, out var byEx))
				{
					return byEx(problem);
				}
			}
		}

		var category = ReadCategory(problem);
		if (category is not null && _byCategory.TryGetValue(category, out var byCategory))
		{
			return byCategory(problem);
		}

		if (_byStatus.TryGetValue(problem.Status, out var byStatus))
		{
			return byStatus(problem);
		}

		// (2) Problem.Detail → (3) Problem.Title → (4) generic last-resort.
		var detail = problem.Detail;
		if (!string.IsNullOrWhiteSpace(detail))
		{
			return detail;
		}

		var title = problem.Title;
		if (!string.IsNullOrWhiteSpace(title))
		{
			return title;
		}

		return GenericFallback;
	}

	/// <inheritdoc />
	public string Explain(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return Explain(Problem.FromEx(exception));
	}

	/// <inheritdoc />
	public string Explain<TValue>(Maybe<TValue> failed)
	{
		ArgumentNullException.ThrowIfNull(failed);
		if (failed.IsSuccess || failed.Problem is null)
		{
			throw new ArgumentException("Maybe is successful; there is no error to explain.", nameof(failed));
		}
		return Explain(failed.Problem);
	}

	/// <summary>
	/// <see cref="Problem.category"/> is a <c>required</c> member, but the explanation path must never
	/// throw while explaining an error — read it defensively from <see cref="Problem.Extensions"/>
	/// rather than through the casting accessor.
	/// </summary>
	private static string? ReadCategory(Problem problem)
		=> problem.Extensions.TryGetValue(nameof(Problem.category), out var value) ? value as string : null;
}
