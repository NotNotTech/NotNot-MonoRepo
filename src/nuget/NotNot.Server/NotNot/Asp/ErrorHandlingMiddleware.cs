using System.Net;
using Microsoft.AspNetCore.Http;

namespace NotNot.Asp;

public class ErrorHandlingMiddleware(RequestDelegate _next)
{
   //private readonly RequestDelegate _next;

   //public ErrorHandlingMiddleware(RequestDelegate next)
   //{
   //   _next = next;
   //}

   public async Task InvokeAsync(HttpContext context)
   {
      try
      {

         await _next(context);


         if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status415UnsupportedMediaType)
         {
            var problem = new Problem
            {
               //Type = "https://httpstatuses.com/415",
               Title = "Unsupported Media Type",
               Detail = "Content-Type header must be set to application/json",
               //Status = StatusCodes.Status415UnsupportedMediaType,   
               //Instance = context.Request.Path
               category = Problem.CategoryNames.Validation,
            };
            await context.Response.WriteAsJsonAsync(problem);

         }


      }
      catch (BadHttpRequestException ex)
      {
         Problem problem;
         //switch based on ex.TargetSite.Name
         switch (ex.TargetSite?.Name)
         {
            case "InvalidJsonRequestBody":
            case "ImplicitBodyNotProvided":
               {
                  problem = new()
                  {
                     Status = HttpStatusCode.BadRequest,// StatusCodes.Status400BadRequest,
                     Title = $"Malformed Request: {ex.TargetSite.Name}",
                     Detail = ex.Message,
                     category = Problem.CategoryNames.Validation,
                  };
               };
               break;

            default:
               {
                  problem = new()
                  {
                     Status = HttpStatusCode.InternalServerError,// StatusCodes.Status500InternalServerError,
                     Title = $"Malformed Request: {ex.GetType().Name}",
                     Detail = ex.Message,
                     category = Problem.CategoryNames.Validation,
                     ex = ex,
                  };
                  __.GetLogger()._EzError(ex, "returning error to endpoint api caller", problem);

                  break;
               }
         }

         //var problemResult = Results.Problem(problem);
         //await problemResult.ExecuteAsync(context);
         var result = Maybe<PreHandler>.Error(problem);
         await ((IResult)result).ExecuteAsync(context);
         //await context.Response.WriteAsJsonAsync(problem);



      }


   }
   public class PreHandler { }
}
