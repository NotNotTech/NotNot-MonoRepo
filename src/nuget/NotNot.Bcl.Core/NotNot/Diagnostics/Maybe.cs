using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotNot.Diagnostics;
using Microsoft.AspNetCore.Http;
using NotNot.Data;

namespace NotNot;

/// <summary>
/// lite version adapted from NotNot.Server (asp)
/// </summary>
public record class Maybe : Maybe<OperationResult>
{
	public Maybe(OperationResult value = Data.OperationResult.Success, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) : base(value, memberName, sourceFilePath, sourceLineNumber)
	{
	}

	public Maybe(Problem problem, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) : base(problem, memberName, sourceFilePath, sourceLineNumber)
	{
	}

	public static implicit operator Maybe(Problem problem)
	{
		var source = problem.DecomposeSource();
		return new Maybe(problem, source.memberName, source.sourceFilePath, source.sourceLineNumber);
	}
	//public static implicit operator Maybe(Maybe<OperationResult> maybe)
	//{
	//   var trace = maybe.TraceId;
	//   if (maybe.IsSuccess)
	//   {
	//      return new Maybe(maybe.Value, trace.SourceMemberName, trace.SourceFile, trace.SourceLineNumber);
	//   }
	//   else
	//   {
	//      return new Maybe(maybe.Problem!, trace.SourceMemberName, trace.SourceFile, trace.SourceLineNumber);
	//   }
	//}

	public static Maybe Success([CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) => new(OperationResult.Success, memberName, sourceFilePath, sourceLineNumber);

	public static Maybe<T> Success<T>(T value, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) => Maybe<T>.Success(value, memberName, sourceFilePath, sourceLineNumber);

	/// <summary>
	/// an ez hack for development:  throw a ProblemException if the condition fails.  This is fast and easy but throwing exceptions is low performance.  should replace with a no-throw solution for hot paths
	/// <para>this is different from __.Throw() in that it isn't logged as an error</para>
	/// </summary>
	public static void Throw(bool condition, string category, HttpStatusCode errorCode,[CallerArgumentExpression("condition")] string conditionExpression="", [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (condition)
		{
			return;
		}
		var problem = new Problem(memberName, sourceFilePath, sourceLineNumber)
		{
			category = category,
			Detail = $"condition failed: {conditionExpression}",
			Status = errorCode,
			Title = $"{category} condition failed"
		};

		throw problem.ToException();

	}
}

/// <summary>
/// <para>lite version adapted from NotNot.Server (asp)</para>
/// <para>contains .Value or .Problem returned from api calls, and logic to help process/return from aspnetcore endpoints</para>
/// <para>Needed because C# doesn't support true Monad. to handle results from api calls that might return your expected value, or a strongly-typed error.</para>
/// </summary>
/// <typeparam name="TValue"></typeparam>
[JsonConverter(typeof(MaybeJsonConverter))]
public record class Maybe<TValue> : IMaybe
{

	[MemberNotNullWhen(true, "IsSuccess")]
	protected TValue? _Value { get; init; }

	/// <summary>
	/// will throw an exception if IsSuccess is false
	/// </summary>
	public TValue Value => GetValue();

	/// <summary>
	/// get the value or throw exception
	/// </summary>
	/// <returns></returns>
	protected TValue GetValue()
	{
		if (!IsSuccess)
		{
			__.AssertNotNull(Problem, "Problem is null, but IsSuccess is false.  This is a bug.");
			throw Problem.ToException();
		}
		return _Value;
	}

	object? IMaybe.GetValue()
	{
		return this.GetValue();
	}

	/// <summary>
	/// useful for debugging to see where in code this 'Maybe' was generated from
	/// </summary>
	public TraceId TraceId { get; protected init; }

	[MemberNotNullWhen(false, "IsSuccess")]
	public Problem? Problem { get; protected init; }

	/// <summary>
	/// If true, .Value is set.  otherwise .Problem is set.
	/// </summary>
	public bool IsSuccess { get; private init; }

	/// <summary>
	/// default null.   lets you write a human readable explanation as to what this Maybe's operational intent was, for inspection by outside systems.
	/// </summary>
	public string? IntentSummary { get; set; }

	public bool IsProblem => !IsSuccess;
	public HttpStatusCode StatusCode
	{
		get
		{
			if (IsSuccess)
			{
				return HttpStatusCode.OK;
			}
			else
			{
				return Problem!.Status ?? HttpStatusCode.InternalServerError;
			}
		}
	}

	/// <summary>
	/// The type of TValue shown as a string.  mostly used for serialization/debugging
	/// </summary>
	/// <example>typeof(TValue).Name</example>
	public string ValueName => typeof(TValue).Name;


	public Maybe(TValue value, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{

		__.Throw(value is not null, null, memberName, sourceFilePath, sourceLineNumber);

		_Value = value;
		IsSuccess = true;
		TraceId = TraceId.Generate(memberName, sourceFilePath, sourceLineNumber);
	}
	public Maybe(Problem problem, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		__.Throw(problem is not null, null, memberName, sourceFilePath, sourceLineNumber);
		Problem = problem;
		TraceId = TraceId.Generate(memberName, sourceFilePath, sourceLineNumber);
	}

	public static Maybe<TValue> Success(TValue value, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) => new Maybe<TValue>(value, memberName, sourceFilePath, sourceLineNumber);
	public static Maybe<TValue> Error(Problem problem, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) => new Maybe<TValue>(problem, memberName, sourceFilePath, sourceLineNumber);

	public static Maybe<TValue> ConvertFrom(TValue value, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		return new Maybe<TValue>(value, memberName, sourceFilePath, sourceLineNumber);
	}

	public static Maybe<TValue> ConvertFrom(Problem problem, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{

		return new Maybe<TValue>(problem, memberName, sourceFilePath, sourceLineNumber);

	}


	// Operator overloads for implicit conversion
	//public static implicit operator Maybe<TValue>(TValue value) => Success(value,"op_Success");
	public static implicit operator Maybe<TValue>(Problem problem) {
		var source =  problem.DecomposeSource();
		return new (problem,source.memberName,source.sourceFilePath, source.sourceLineNumber);
	}



	// Method to map success value to a different type
	public Maybe<TNew> Map<TNew>(Func<TValue, TNew> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Maybe<TNew> toReturn;
		if (IsSuccess)
		{
			toReturn = Maybe<TNew>.Success(func(_Value), memberName, sourceFilePath, sourceLineNumber);
		}
		else
		{
			// Propagate the existing problem to the new Result
			toReturn = Maybe<TNew>.Error(Problem!, memberName, sourceFilePath, sourceLineNumber);
		}
		return toReturn._TryMergeTraceFrom(this);
		//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };

	}

	public Maybe Map([CallerMemberName] string memberName = "",
	  [CallerFilePath] string sourceFilePath = "",
	  [CallerLineNumber] int sourceLineNumber = 0)
	{
		Maybe toReturn;
		if (IsSuccess)
		{
			toReturn = new Maybe(OperationResult.Success, memberName, sourceFilePath, sourceLineNumber);
		}
		else
		{
			// Propagate the existing problem to the new Result
			toReturn = new Maybe(Problem!, memberName, sourceFilePath, sourceLineNumber);
		}
		return (Maybe) toReturn._TryMergeTraceFrom(this);
		//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };

	}


	public Maybe<TNew> Map<TNew>(Func<TValue, Maybe<TNew>> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Maybe<TNew> toReturn;
		if (IsSuccess)
		{
			toReturn = func(_Value!);
		}
		else
		{
			// Propagate the existing problem to the new Result
			toReturn = Maybe<TNew>.Error(Problem!, memberName, sourceFilePath, sourceLineNumber);
		}
		return toReturn._TryMergeTraceFrom(this);
		//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };
	}
	public async Task<Maybe<TNew>> Map<TNew>(Func<TValue, Task<TNew>> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Maybe<TNew> toReturn;
		if (IsSuccess)
		{
			var successResult = await func(_Value!);
			toReturn = Maybe<TNew>.Success(successResult, memberName, sourceFilePath, sourceLineNumber);
		}
		else
		{
			toReturn = Maybe<TNew>.Error(Problem!, memberName, sourceFilePath, sourceLineNumber);
		}
		return toReturn._TryMergeTraceFrom(this);
		//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };
	}

	public async Task<Maybe<TNew>> Map<TNew>(Func<TValue, Task<Maybe<TNew>>> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Maybe<TNew> toReturn;
		if (IsSuccess)
		{
			toReturn = await func(_Value!);
		}
		else
		{
			toReturn = Maybe<TNew>.Error(Problem!, memberName, sourceFilePath, sourceLineNumber);
		}
		return toReturn._TryMergeTraceFrom(this);
		//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };
	}

	/// <summary>
	/// returns this Maybe instance if it's successful
	/// otherwise allows you to fix the problem and return a new Maybe instance
	/// </summary>
	public Maybe<TValue> Fix(Func<Problem, TValue> func, [CallerMemberName] string memberName = "",
			  [CallerFilePath] string sourceFilePath = "",
					 [CallerLineNumber] int sourceLineNumber = 0)
	{

		if (IsSuccess)
		{
			return this;
		}
		else
		{
			var toReturn = Maybe<TValue>.Success(func(Problem!), memberName, sourceFilePath, sourceLineNumber);
			return toReturn._TryMergeTraceFrom(this);
			//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };
		}

	}

	/// <summary>
	/// returns this Maybe instance if it's successful
	/// otherwise allows you to fix the problem and return a new Maybe instance
	/// </summary>
	public Maybe<TValue> Fix(Func<Problem, Maybe<TValue>> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (IsSuccess)
		{
			return this;
		}
		else
		{
			var toReturn = func(Problem!);
			return toReturn._TryMergeTraceFrom(this);
			//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };
		}
	}
	public async Task<Maybe<TValue>> Fix(Func<Problem, Task<TValue>> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{

		if (IsSuccess)
		{
			return this;
		}
		else
		{
			var value = await func(Problem!);
			var toReturn = Maybe<TValue>.Success(value, memberName, sourceFilePath, sourceLineNumber);
			return toReturn._TryMergeTraceFrom(this);
		}

	}

	/// <summary>
	/// helper to merge the trace chain from another Maybe instance into this one, if this one doesn't already have a trace chain.
	/// </summary>
	/// <typeparam name="TAny"></typeparam>
	/// <param name="other"></param>
	/// <returns></returns>
	protected Maybe<TValue> _TryMergeTraceFrom<TAny>(Maybe<TAny> other)
	{
		if (TraceId.From is not null)
		{
			//don't truncate the existing trace chain
			return this;
		}

		return this with
		{
			TraceId = TraceId with { From = other.TraceId }
		};
	}

	public async Task<Maybe<TValue>> Fix(Func<Problem, Task<Maybe<TValue>>> func, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (IsSuccess)
		{
			return this;
		}
		else
		{
			var toReturn = await func(Problem!);
			return toReturn._TryMergeTraceFrom(this);
			//return toReturn with { TraceId = toReturn.TraceId with { From = this.TraceId } };
		}
	}

	/// <summary>
	/// conditionally work on the results.  returns true if success (value is set) otherwise false (problem is set)
	/// </summary>
	/// <param name="value"></param>
	/// <param name="problem"></param>
	/// <returns></returns>
	public bool Pick([NotNullWhen(true)] out TValue? value, [NotNullWhen(false)] out Problem? problem)
	{
		if (IsSuccess)
		{
			value = _Value!;
			problem = default!;
			return true;
		}
		else
		{
			value = default!;
			problem = Problem!;
			return false;
		}
	}



	public bool TryGetValue([NotNullWhen(true)] out TValue? value)
	{
		if (IsSuccess)
		{
			value = _Value!;
			return true;
		}
		else
		{
			value = default;
			return false;
		}
	}
}

/// <summary>
/// JSON converter for Maybe<T> to support deserialization from Content.ReadFromJsonAsync
/// </summary>
public class MaybeJsonConverter : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
	{
		return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Maybe<>);
	}

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		var valueType = typeToConvert.GetGenericArguments()[0];
		var converterType = typeof(MaybeJsonConverter<>).MakeGenericType(valueType);
		return (JsonConverter)Activator.CreateInstance(converterType)!;
	}
}

/// <summary>
/// Typed JSON converter for Maybe<T>
/// </summary>
public class MaybeJsonConverter<T> : JsonConverter<Maybe<T>>
{
	public override Maybe<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var doc = JsonDocument.ParseValue(ref reader);
		var root = doc.RootElement;

		// Check if it's a success response with Value property
		if (root.TryGetProperty("Value", out var valueElement) && !valueElement.ValueKind.Equals(JsonValueKind.Null))
		{
			var value = JsonSerializer.Deserialize<T>(valueElement, options);
			return Maybe<T>.Success(value!);
		}

		// Check if it's an error response with Problem property
		if (root.TryGetProperty("Problem", out var problemElement))
		{
			var problem = JsonSerializer.Deserialize<Problem>(problemElement, options);
			return Maybe<T>.Error(problem!);
		}

		// Check if it's a direct value (for simple cases)
		if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Any())
		{
			var directValue = JsonSerializer.Deserialize<T>(root, options);
			return Maybe<T>.Success(directValue!);
		}

		// Default to error if structure doesn't match expected patterns
		var defaultProblem = new Problem("JsonDeserialization", "", 0)
		{
			category = "Deserialization",
			Title = "Invalid Maybe JSON structure",
			Detail = "JSON does not match expected Maybe<T> format",
			Status = HttpStatusCode.BadRequest
		};
		return Maybe<T>.Error(defaultProblem);
	}

	public override void Write(Utf8JsonWriter writer, Maybe<T> value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.IsSuccess)
		{
			writer.WritePropertyName("Value");
			JsonSerializer.Serialize(writer, value.Value, options);
			writer.WriteBoolean("IsSuccess", true);
			writer.WriteNull("Problem");
		}
		else
		{
			writer.WriteNull("Value");
			writer.WriteBoolean("IsSuccess", false);
			writer.WritePropertyName("Problem");
			JsonSerializer.Serialize(writer, value.Problem, options);
		}

		writer.WriteString("ValueName", value.ValueName);
		writer.WriteString("TraceId", value.TraceId.ToString());

		writer.WriteEndObject();
	}
}

public interface IMaybe
{
	public bool IsSuccess { get; }
	public bool IsProblem { get; }
	public HttpStatusCode StatusCode { get; }
	public string ValueName { get; }
	public TraceId TraceId { get; }
	public Problem? Problem { get; }

	public object? GetValue();

	public string? IntentSummary { get; }

}
