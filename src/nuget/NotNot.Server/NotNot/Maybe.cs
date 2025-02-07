using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using NotNot.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using NotNot.Data;
using NotNot.SwaggerGen;

namespace NotNot;


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

}

/// <summary>
/// <para>contains .Value or .Problem returned from api calls, and logic to help process/return from aspnetcore endpoints</para>
/// <para>Needed because C# doesn't support true Monad. to handle results from api calls that might return your expected value, or a strongly-typed error.</para>
/// </summary>
/// <typeparam name="TValue"></typeparam>
public record class Maybe<TValue> : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<TValue>, IEndpointMetadataProvider
{
   int? IStatusCodeHttpResult.StatusCode => StatusCode;


   object? IValueHttpResult.Value => _Value;
   TValue? IValueHttpResult<TValue>.Value => _Value;

   
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
         throw Problem.ToException();
      }
      return _Value;
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

   public bool IsProblem => !IsSuccess;
   public int StatusCode
   {
      get
      {
         if (IsSuccess)
         {
            return StatusCodes.Status200OK;
         }
         else
         {
            return Problem!.Status ?? StatusCodes.Status500InternalServerError;
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

   /// <summary>
   /// while this Result implement IResult, if you want you can obtain a Result for the specific value/problem this contains.
   /// Currently there is no difference, but later we'll wrap the contained value with meta information (like a cache time and status)
   /// </summary>
   /// <returns></returns>
   public IResult ExtractBareResult()
   {
      if (IsSuccess)
      {
         return Results.Ok(_Value);
      }
      return Results.Problem(Problem!);
   }

   /// <summary>
   /// 
   /// </summary>
   /// <param name="httpContext"></param>
   /// <returns></returns>
   Task IResult.ExecuteAsync(HttpContext httpContext)
   {
      if (IsSuccess)
      {
         return Results.Ok(this).ExecuteAsync(httpContext);
      }
      else
      {
         var problemWrapper = new ResultProblem
         {
            Problem = Problem!,
            TraceId = TraceId.ToString(),
         };
         return Results.Problem(problemWrapper).ExecuteAsync(httpContext);

      }
   }

   /// <summary>
   /// a wrapper over this Maybe's problem, to mimic the same structure as a value result.
   /// </summary>
   public class ResultProblem : ProblemDetails
   {
      [SwaggerExample(Value = null, SchemaDocTitle = "always null")]
      public TValue? Value => default;

      [SwaggerExample(Value = false, SchemaDocTitle = "always false")]
      public bool IsSuccess => false;

      public required Problem Problem { get; init; }

      //[SwaggerExample(Value = "gitsha7|Machine_Name|Source|Method|ln|yyyy-MM-ddTHH:mm:ssZ|id")]
      ///<example>gitsha7|Machine_Name|Source|Method|ln|yyyy-MM-ddTHH:mm:ssZ|id</example>
      public string TraceId { get; init; }

      [SwaggerExample(Value = 400)]

      public new int Status => Problem.Status ?? StatusCodes.Status500InternalServerError;

      /// <summary>
      /// the Type of Maybe.Value
      /// </summary>
      /// <example>Type_of_Value</example>
      //[SwaggerExample(Value = "Type_of_Value")]
      public string ValueName => typeof(TValue).Name;

      //[SwaggerIgnore]
      //public new IDictionary<string, object?> Extensions { get; init; } = null;
   }

   public class ResultSuccess
   {
      public required TValue Value { get; init; }
      /// <summary>
      /// problem isSuccess true
      /// </summary>
      /// <example>true</example>
      [SwaggerExample(Value = true, SchemaDocTitle = "always true")]
      public bool IsSuccess => true;

      /// <summary>
      /// problem summary
      /// </summary>
      [SwaggerExample(Value = null, SchemaDocTitle = "always null")]
      public Problem? Problem => null;

      //[SwaggerExample(Value = "gitsha7|Machine_Name|Source|Method|ln|yyyy-MM-ddTHH:mm:ssZ|id")]
      ///<example>gitsha7|Machine_Name|Source|Method|ln|yyyy-MM-ddTHH:mm:ssZ|id</example>
      public required string TraceId { get; init; }

      public int Status => StatusCodes.Status200OK;

      /// <summary>
      /// the Type of Maybe.Value
      /// </summary>
      /// <example>Type_of_Value</example>
      //[SwaggerExample(Value = "Type_of_Value")]
      public string ValueName => typeof(TValue).Name;

   }

   static void IEndpointMetadataProvider.PopulateMetadata(MethodInfo method, EndpointBuilder builder)
   {
      ArgumentNullException.ThrowIfNull(method);
      ArgumentNullException.ThrowIfNull(builder);

      builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(ResultSuccess), new[] { "application/json" }));
      builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ResultProblem), new[] { "application/json" }));

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
