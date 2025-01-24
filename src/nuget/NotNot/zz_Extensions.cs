using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Spectre.Console;

namespace NotNot;

public static class zz_Extensions_Spectre_Console_Color
{
   public static string _MarkupString(this Color color, string? message)
   {
      var markup = color.ToMarkup();
      return $"[{markup}]{message}[/]";
   }
}


//public static class zz_Extensions_OneOf
//{
//   public static async Task<OneOf.OneOf<T0_new, T1>> _MapT0<T0, T1, T0_new, T1_derived>(this OneOf.OneOf<T0, T1> oneOf, Func<T0, Task<OneOf<T0_new, T1_derived>>> mapFunc
//    )
//    where T1 : class
//      where T1_derived : T1
//   {
//      var tmp = oneOf.MapT0(mapFunc);

//      if (tmp.TryPickT0(out var t0, out var t1))
//      {
//         var result = await t0;
//         return result.MapT1(p => p as T1);
//      }
//      return t1;
//   }


//   public static OneOf.OneOf<T0_new, T1> _MapT0<T0, T1, T0_new>(this OneOf.OneOf<T0, T1> oneOf, Func<T0, T0_new> mapFunc
//     )
//      //where TProblem : class
//   {
//      var tmp = oneOf.MapT0(mapFunc);
//      return tmp;
//      // return tmp.MapT1(p => p as ProblemDetails);    
//   }




//   /// <summary>
//   /// allows mapping async functions to OneOf results
//   /// </summary>
//   public static async Task<OneOf.OneOf<T0_new, T1>> _MapT0<T0, T1, T0_new>(this OneOf.OneOf<T0, T1> oneOf, Func<T0, Task<T0_new>> mapFunc)
//   {
//      var tmp = oneOf.MapT0(mapFunc);

//      if (tmp.TryPickT0(out var t0, out var t1))
//      {
//         return await t0;
//      }
//      return t1;
//   }
//   /// <summary>
//   /// allows mapping async functions to OneOf results
//   /// </summary>
//   public static async Task<OneOf.OneOf<T0, T1_new>> _MapT1<T0, T1, T1_new>(this OneOf.OneOf<T0, T1> oneOf, Func<T1, Task<T1_new>> mapFunc)
//   {
//      var tmp = oneOf.MapT1(mapFunc);

//      if (tmp.TryPickT0(out var t0, out var t1))
//      {
//         return t0;
//      }
//      return await t1;
//   }


//   /// <summary>
//   /// easily map from OneOf to a web request's expected IResult return response
//   /// </summary>
//   /// <typeparam name="TResponse"></typeparam>
//   /// <param name="oneOfResult"></param>
//   /// <returns></returns>
//   public static IResult _ToResult<TResponse>(this OneOf<TResponse, TProblemDetails> oneOfResult)
//   {      
//      return oneOfResult.Match(
//         response => Results.Ok(response),
//         problem => Results.Problem(problem)         
//      );
//   }
//}

//public static class zz_Extensions_ProblemDetails
//{
//   //public static string? _CallSite(this ProblemDetails problem, string value)
//   //{
//   //   if (value is null)
//   //   {
//   //      problem.Extensions.Remove("Callsite");
//   //   }
//   //   else
//   //   {
//   //      problem.Extensions["Callsite"] = value;
//   //   }
//   //   return value;
//   //}
//   //public static string? _CallSite(this ProblemDetails problem, [CallerMemberName] string memberName = "",
//   //   [CallerFilePath] string sourceFilePath = "",
//   //   [CallerLineNumber] int sourceLineNumber = 0)
//   //{
//   //   var callsite = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";
//   //   problem.Extensions["Callsite"] = callsite;
//   //   return callsite;
//   //}

//   //public static bool TryGetCallsite(this ProblemDetails problem, out string? callsite)
//   //{
//   //   if (problem.Extensions.TryGetValue("Callsite", out var callsiteObj))
//   //   {
//   //      callsite = callsiteObj as string;
//   //      return true;
//   //   }
//   //   callsite = null;
//   //   return false;
//   //}

//   /// <summary>
//   /// useful to hint to upstream callers that a problem is recoverable.
//   /// not needed to call to set value as false, as it's redundant.
//   /// </summary>
//   public static bool _IsRecoverable(this ProblemDetails problem, bool value)
//   {
//      if (value is false)
//      {
//         problem.Extensions.Remove("isRecoverable");
//      }
//      else
//      {
//         problem.Extensions["isRecoverable"] = value;
//      }
//      return value;
//   }
//   public static bool _IsRecoverable(this ProblemDetails problem)
//   {
//      if(problem.Extensions.TryGetValue("isRecoverable", out var result)){
//         switch (result)
//         {
//            case bool toReturn:
//               return toReturn;
//               break;
//            default:
//               __.GetLogger()._EzError( "problem.isRecoverable is not a bool", result);
//               break;
//         }
//      }
//      return false;
//      //return problem.Extensions["isRecoverable"] as bool? ?? false;
//   }
//   public static Exception _Ex(this ProblemDetails problem, Exception value)
//   {
//      problem.Extensions["ex"] = value;
//      return value;
//   }

//   /// <summary>
//   /// extract exception from problemDetails.  if not found, return null
//   /// </summary>
//   /// <param name="problem"></param>
//   /// <returns></returns>
//   public static Exception? _Ex(this ProblemDetails problem)
//   {
//      if (problem.Extensions["ex"] is Exception ex)
//      {
//         return ex;
//      }
//      foreach (var (key, value) in problem.Extensions)
//      {
//         if (value is Exception ex2)
//         {
//            return ex2;
//         }
//      }
//      return null;
//   }

//}
public static class zz_Extensions_HttpRequest
{
   public static StringValues _GetKeyValues(this HttpRequest request, string key, string? altKey = null)
   {
      if (request.Query.TryGetValue(key, out var value))
      {
         return value;
      }
      if (altKey is not null && request.Query.TryGetValue(altKey, out var altValue))
      {
         return altValue;
      }
      if (request.Headers.TryGetValue(key, out value))
      {
         return value;
      }
      if (altKey is not null && request.Headers.TryGetValue(altKey, out altValue))
      {
         return altValue;
      }
      return StringValues.Empty;
   }
}



public static class zz_Extensions_DbSet
{
   /// <summary>
   ///    sets all entities in the dbSet to detached (which allows it's cache to be cleared, freeing GC)
   ///    BE SURE TO SAVE CHANGES FIRST!
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="dbSet"></param>
   /// <returns></returns>
   [Obsolete("not working properly, clearing makes dbSet think entity doesn't exist (doesn't reaquire from underlying db)")]
   public static async Task _ClearCache<T>(this DbSet<T> dbSet) where T : class
   {
      //disabling as seems to not work properly
      foreach (var entity in dbSet.Local)
      {
         switch (dbSet.Entry(entity).State)
         {
            case EntityState.Deleted:
            case EntityState.Modified:
            case EntityState.Added:
               __.GetLogger()._EzError(false, "should have saved changes before clearing cache");
               break;
            case EntityState.Unchanged:
            case EntityState.Detached:
               break;
            default:
               __.GetLogger()._EzErrorThrow(false, $"unhandled EntityState: {dbSet.Entry(entity).State}");
               break;
         }

         dbSet.Entry(entity).State = EntityState.Detached;
      }

      //clearing via the changeTracker has the same problem.
      //dbSet._Context().ChangeTracker.Clear();
   }

   /// <summary>
   ///    for reducing memory use.  save all dbContext changes (including other dbsets!), and clears all local cached entities
   ///    (from ONLY this dbSet).
   /// </summary>
   public static async ValueTask _SaveAndClearCache<T>(this DbSet<T> dbSet, DbContext context, CancellationToken ct) where T : class
   {
      //var context = dbSet._Context();
      await context.SaveChangesAsync(ct);

      //mark all entities as detached (which allows it's cache to be cleared, freeing GC)
      foreach (var entity in dbSet.Local)
      {
         var entry = dbSet.Entry(entity);
         switch (entry.State)
         {
            case EntityState.Deleted:
            case EntityState.Modified:
            case EntityState.Added:
               __.GetLogger()._EzError("threading race condition?  should have saved changes before clearing cache", entry.State, entity);
               break;
            case EntityState.Unchanged:
            case EntityState.Detached:
               break;
            default:
               __.GetLogger()._EzError("unhandled EntityState", entry.State, entity);
               break;
         }

         entry.State = EntityState.Detached;
      }

      //save changes for detach to take effect properly 
      //otherwise if detatched eneity would be returned, nothing would (it won't re-aquire a new copy from db either)
      await context.SaveChangesAsync(ct);
   }


   /// <summary>
   ///    expensive way to get the context.  don't use this method if at all possible
   /// </summary>
   public static DbContext _Context<T>(this DbSet<T> dbSet) where T : class
   {
      var infrastructureInterface = dbSet as IInfrastructure<IServiceProvider>;
      var serviceProvider = infrastructureInterface.Instance;
      var currentDbContext = serviceProvider.GetRequiredService<ICurrentDbContext>();
      //var currentDbContext = serviceProvider.GetService(typeof(ICurrentDbContext))
      //   as ICurrentDbContext;
      return currentDbContext.Context;
   }

}

public static class zz_Extensions_DbContext
{

   /// <summary>
   ///    for reducing memory use.  save all dbContext changes, and clears all local cached entities (from ALL dbsets).
   /// </summary>
   public static async ValueTask _SaveAndClearCache(this DbContext context, CancellationToken ct)
   {
      await context.SaveChangesAsync(ct);
      context.ChangeTracker.Clear();
      //await context.SaveChangesAsync(ct);
   }
}