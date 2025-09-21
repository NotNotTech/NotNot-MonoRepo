using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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


/// <summary>
/// Extension methods for converting Maybe&lt;T&gt; results to ASP.NET Core ActionResult&lt;T&gt; responses.
/// Enables clean controller endpoints that return standard HTTP responses with proper status codes.
/// </summary>
public static class zz_Extensions_Maybe
{
	/// <summary>
	/// Converts a Maybe&lt;T&gt; to ActionResult&lt;T&gt; for HTTP API responses.
	/// Success returns the value directly with appropriate status code.
	/// Failure returns ProblemDetails with proper HTTP status code and RFC 9457 compliance.
	/// </summary>
	/// <typeparam name="T">The success value type</typeparam>
	/// <param name="maybe">The Maybe&lt;T&gt; to convert</param>
	/// <param name="webHostEnvironment">Optional environment for development-specific error details</param>
	/// <returns>ActionResult&lt;T&gt; suitable for controller return</returns>
	public static ActionResult<T> _ToActionResult<T>(
		 this Maybe<T> maybe,
		 IWebHostEnvironment? webHostEnvironment = null)
	{
		if (maybe.IsSuccess)
		{
			// Handle null success values with 204 No Content
			if (maybe.Value == null)
				return new ObjectResult(null) { StatusCode = StatusCodes.Status204NoContent };

			// Return the value directly - ASP.NET Core will serialize and set 200 OK
			return maybe.Value;
		}

		// Convert Problem to ProblemDetails
		var problemDetails = maybe.Problem._ToProblemDetails(webHostEnvironment);

		return new ObjectResult(problemDetails)
		{
			StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError,
			ContentTypes = { "application/problem+json" } // RFC 9457 compliance
		};
	}

	/// <summary>
	/// Converts a Maybe&lt;T&gt; to ActionResult&lt;T&gt; using the current HTTP context environment.
	/// Automatically detects development vs production mode from the service provider.
	/// </summary>
	/// <typeparam name="T">The success value type</typeparam>
	/// <param name="maybe">The Maybe&lt;T&gt; to convert</param>
	/// <param name="serviceProvider">Service provider to resolve IWebHostEnvironment</param>
	/// <returns>ActionResult&lt;T&gt; suitable for controller return</returns>
	public static ActionResult<T> _ToActionResult<T>(
		 this Maybe<T> maybe,
		 IServiceProvider serviceProvider)
	{
		var environment = serviceProvider.GetService<IWebHostEnvironment>();
		return maybe._ToActionResult(environment);
	}

	/// <summary>
	/// Converts a non-generic Maybe to IActionResult for HTTP API responses.
	/// Success returns 204 No Content.
	/// Failure returns ProblemDetails with proper HTTP status code and RFC 9457 compliance.
	/// </summary>
	/// <param name="maybe">The Maybe to convert</param>
	/// <param name="webHostEnvironment">Optional environment for development-specific error details</param>
	/// <returns>IActionResult suitable for controller return</returns>
	public static IActionResult _ToActionResult(
		 this Maybe maybe,
		 IWebHostEnvironment? webHostEnvironment = null)
	{
		if (maybe.IsSuccess)
			return new NoContentResult(); // 204 No Content for successful operations with no return value

		// Convert Problem to ProblemDetails
		var problemDetails = maybe.Problem._ToProblemDetails(webHostEnvironment);

		return new ObjectResult(problemDetails)
		{
			StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError,
			ContentTypes = { "application/problem+json" } // RFC 9457 compliance
		};
	}

	/// <summary>
	/// Converts a non-generic Maybe to IActionResult using the current HTTP context environment.
	/// Automatically detects development vs production mode from the service provider.
	/// </summary>
	/// <param name="maybe">The Maybe to convert</param>
	/// <param name="serviceProvider">Service provider to resolve IWebHostEnvironment</param>
	/// <returns>IActionResult suitable for controller return</returns>
	public static IActionResult _ToActionResult(
		 this Maybe maybe,
		 IServiceProvider serviceProvider)
	{
		var environment = serviceProvider.GetService<IWebHostEnvironment>();
		return maybe._ToActionResult(environment);
	}
}


/// <summary>
/// Extension methods for converting NotNot.Problem to ASP.NET Core ProblemDetails.
/// Enables seamless integration between internal error handling and HTTP API responses.
/// </summary>
public static class zz_Extensions_Problem
{
	/// <summary>
	/// Converts a NotNot.Problem to Microsoft.AspNetCore.Mvc.ProblemDetails for HTTP API responses.
	/// Follows RFC 9457 with proper type URIs and includes debug information in development.
	/// </summary>
	/// <param name="problem">The NotNot.Problem to convert</param>
	/// <param name="webHostEnvironment">Optional environment for development-specific details</param>
	/// <returns>ProblemDetails suitable for HTTP response</returns>
	public static ProblemDetails _ToProblemDetails(
		 this Problem problem,
		 IWebHostEnvironment? webHostEnvironment = null)
	{
		var problemDetails = new ProblemDetails
		{
			Type = $"https://cleartrix.com/errors/{problem.category}",
			Title = problem.Title ?? "Error",
			Status = (int)problem.Status,
			Detail = problem.Detail,
			Instance = null // Can be set per specific scenarios if needed
		};

		// Add category for client-side categorization
		problemDetails.Extensions["category"] = problem.category;

		// Copy problem extensions (excluding source and category to avoid duplication)
		foreach (var kvp in problem.Extensions)
		{
			if (kvp.Key != "source" && kvp.Key != "category")
			{
				problemDetails.Extensions[kvp.Key] = kvp.Value;
			}
		}

		// Include debug information in development environment
		bool isDevelopment = webHostEnvironment?.EnvironmentName == "Development";
		if (isDevelopment)
		{
			if (problem.Ex != null)
			{
				problemDetails.Extensions["trace"] = problem.Ex.StackTrace;
				problemDetails.Extensions["exceptionType"] = problem.Ex.GetType().Name;
			}
			problemDetails.Extensions["source"] = problem.source;
		}

		return problemDetails;
	}

	/// <summary>
	/// Converts a NotNot.Problem to Microsoft.AspNetCore.Mvc.ProblemDetails using the current HTTP context environment.
	/// Automatically detects development vs production mode from the service provider.
	/// </summary>
	/// <param name="problem">The NotNot.Problem to convert</param>
	/// <param name="serviceProvider">Service provider to resolve IWebHostEnvironment</param>
	/// <returns>ProblemDetails suitable for HTTP response</returns>
	public static ProblemDetails _ToProblemDetails(
		 this Problem problem,
		 IServiceProvider serviceProvider)
	{
		var environment = serviceProvider.GetService<IWebHostEnvironment>();
		return problem._ToProblemDetails(environment);
	}
}
