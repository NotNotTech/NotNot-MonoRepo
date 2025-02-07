using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotNot.SwaggerGen;


namespace NotNot;

/// <summary>
/// wrapper around ProblemDetails to include details we care about
/// </summary>
public class Problem : ProblemDetails
{

   public class ProblemException : LoLoException
   {
      public ProblemException(Problem problem) : base(problem.Title + ":" + problem.Detail, problem.GetEx())
      {
         Problem = problem;
         Source = problem.source;
      }

      public Problem Problem { get; }
   }

   /// <summary>
   /// general classification of the Problem
   /// </summary>
   public static class CategoryNames
   {
      /// <summary>
      /// data failed validation
      /// </summary>
      public static string Validation => nameof(Validation);
      /// <summary>
      /// a problem with a database operation, ex: a record not found, transaction failure, or a unique constraint violation
      /// </summary>
      public static string DbIo => nameof(DbIo);
      /// <summary>
      /// the call not authenicated / permissions
      /// </summary>
      public static string Auth => nameof(Auth);
      /// <summary>
      /// problem calling a network api, ex: timeout, 404, 500, etc.
      /// </summary>
      public static string NetIo => nameof(NetIo);

      /// <summary>
      /// a timeout occcured, ex: CancellationToken
      /// </summary>
      public static string Timeout => nameof(Timeout);
   }

   public (string memberName, string sourceFilePath, int sourceLineNumber) DecomposeSource()
   {
      var parts = source?.Split(':') ?? new string[0];
      if (parts.Length >= 3 && int.TryParse(parts[2], out int lineNumber))
      {
         return (parts[0], parts[1], lineNumber);
      }
      else
      {
         // Handle the error case where the source format is incorrect
         // You can throw an exception or return a default value based on your requirements
         throw new InvalidOperationException("Invalid source format");
      }
   }
   public Problem([CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      Extensions[nameof(source)] = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";
   }
   public Problem(ProblemDetails problemBase, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0) : this(memberName, sourceFilePath, sourceLineNumber)
   {
      Status = problemBase.Status;
      Title = problemBase.Title;
      Type = problemBase.Type;
      Detail = problemBase.Detail;
      Instance = problemBase.Instance;

      foreach (var pair in problemBase.Extensions)
      {
         Extensions[pair.Key] = pair.Value;
      }
   }

   /// <summary>
   /// Convert this problem to an Exception, preserving details.
   /// </summary>
   /// <returns></returns>
   public ProblemException ToException()
   {
      return new ProblemException(this);
   }

   /// <summary>
   /// the callsite (source) of the problem
   /// </summary>
   public string source { get => (string)Extensions[nameof(source)]!; init => Extensions[nameof(source)] = value; }

   public required string category { get => (string)Extensions[nameof(category)]!; init => Extensions[nameof(category)] = value; }
   /// <summary>
   /// useful to hint to upstream callers that a problem is recoverable.
   /// not needed to call to set value as false, as it's redundant.
   /// </summary>
   /// <example>false</example>
   public bool isRecoverable
   {
      get
      {
         if (Extensions.TryGetValue(nameof(isRecoverable), out var result))
         {
            switch (result)
            {
               case bool toReturn:
                  return toReturn;
                  break;
               default:
                  __.GetLogger()._EzError("problem.IsRecoverable is not a bool", result);
                  break;
            }
         }
         return false;
      }
      set
      {
         if (value is false)
         {
            Extensions.Remove(nameof(isRecoverable));
         }
         else
         {
            Extensions[nameof(isRecoverable)] = value;
         }
      }
   }
   /// <summary>
   /// only set in DEBUG
   /// </summary>
   [SwaggerIgnore]
   public Exception? ex
   {
      get
      {
         if (Extensions.TryGetValue(nameof(ex), out var result))
         {
            switch (result)
            {
               case Exception toReturn:
                  return toReturn;
               default:
                  __.GetLogger()._EzError("problem.Ex is not an exception", result);
                  break;
            }
         }
         return null;
      }
      set
      {
         if (value is null)
         {
            Extensions.Remove(nameof(ex));
         }
         else
         {
            Extensions[nameof(ex)] = value;
         }
      }
   }

   /// <summary>
   /// Helper to add to the .Extensions dictionary
   /// </summary>
   /// <param name="value"><para>If you don't provide a key, the current variable-name of the `value` will be used</para></param>
   /// <param name="key"><para>If you don't provide a key, the current variable-name of the `value` will be used</para></param>
   public void SetExtension(object value,[CallerMemberName] string key="")
   {
      Extensions[key] = value;
   }


   public Exception? GetEx()
   {
      return ex as Exception;
   }

   public static Problem FromCancellationToken(CancellationToken ct, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      if(ct.IsCancellationRequested is false)
      {
         throw new Exception("ct not cancelled");
      }

      return new Problem(memberName, sourceFilePath, sourceLineNumber)
      {
         Title = "CancellationToken cancel requested",
         Detail = "cancellation requested",
         Status = StatusCodes.Status408RequestTimeout,
         category = CategoryNames.Timeout,
      };
   }
}
