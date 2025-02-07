using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NotNot;

public partial class ServiceValidator(ILogger<ServiceValidator> _logger) : ISingletonService
{
   private readonly ConcurrentDictionary<Type, WeakReference<object>> _singletonStorage = new();
   private readonly object _singletonLock = new();


   /// <summary>
   ///    Ensure this object is the only of the given type to exist. (type seen only once by this Verify instance), will
   ///    Assert+Throw if not.
   /// </summary>
   [DebuggerNonUserCode]
   [DebuggerStepThrough]
   public void MustBeSingleton<T>(T item, [CallerMemberName] string memberName = "",
     [CallerFilePath] string sourceFilePath = "",
     [CallerLineNumber] int sourceLineNumber = 0)
   {
      var type = typeof(T);

      lock (_singletonLock)
      {
         var storage = _singletonStorage.GetOrAdd(type, new WeakReference<object>(item));
         if (storage.TryGetTarget(out var existing))
         {

            //_diagHelper.Throw<VerifyException>(_envLevel, ReferenceEquals(existing, item),
            //   "different instances of singleton type found");
            if (!ReferenceEquals(existing, item))
            {
               var ex = new VerifyException("different instances of singleton type found", memberName, sourceFilePath, sourceLineNumber);
               _logger._EzError(ex);
               throw ex;
            }
         }
         else
         {
            storage.SetTarget(item);
         }
      }
   }

   public class VerifyException : LoLoDiagnosticsException
   {
      public VerifyException()
      {
      }

      public VerifyException(string message, [CallerMemberName] string memberName = "",
         [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0) : base(message, memberName, sourceFilePath, sourceLineNumber)
      {
      }

      public VerifyException(string message, Exception? innerException = null, [CallerMemberName] string memberName = "",
         [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0) : base(message, innerException, memberName, sourceFilePath, sourceLineNumber)
      {
      }
   }

}

public class SimpleValidator : ISingletonService
{

   public class SimpleValidatorException : System.ComponentModel.DataAnnotations.ValidationException
   {
      public SimpleValidatorException()
      {
      }

      public SimpleValidatorException(string message) : base(message)
      {
      }

      public SimpleValidatorException(string message, Exception? innerException = null) : base(message, innerException)
      {
      }

      public SimpleValidatorException(ValidationResult validationResult, ValidationAttribute validationAttribute,
         object? value) :
         base(validationResult, validationAttribute, value)
      {
      }

      public SimpleValidatorException(string? errorMessage, ValidationAttribute? validatingAttribute, object? value) : base(
         errorMessage, validatingAttribute, value)
      {
      }

   }

   /// <summary>
   ///    returns true if all args are not null.
   /// </summary>
   [DebuggerNonUserCode]
   [DebuggerHidden]
   public bool NotNull(params object?[] args)
   {
      foreach (var arg in args)
      {
         if (arg is null)
         {
            return false;
         }
      }

      return true;
   }

   [DebuggerNonUserCode]
   [DebuggerHidden]
   public void NotNullThrow<T>([NotNull] T arg)
   {
      if (arg is null)
      {
#if DEBUG
         if (Debugger.IsAttached)
         {
            Debugger.Break();
         }
#endif
         throw new SimpleValidatorException();
      }
   }


   [DebuggerNonUserCode]
   [DebuggerHidden]
   public void NotNullThrow<T1, T2>([NotNull] T1 arg1, [NotNull] T2 arg2)
   {
      NotNullThrow(arg1);
      NotNullThrow(arg2);
   }

   [DebuggerNonUserCode]
   [DebuggerHidden]
   public void Throw(string message, object? arg = null, [CallerArgumentExpression("arg")] string argName = "",
      [CallerMemberName] string caller = "")
   {
      var msg = $"Validation Failed.  msg={message} arg={argName} argValue={arg} caller={caller}";
      var validationResult = new ValidationResult(msg, new[] { argName });

      //#if DEBUG
      //      if (Debugger.IsAttached)
      //      {
      //         Debugger.Break();
      //      }
      //#endif

      throw new SimpleValidatorException(validationResult, null, arg);
   }

   [DebuggerNonUserCode]
   [DebuggerHidden]
   public void ExpectThrow(bool condition, string? message = null, object arg = null,
      [CallerArgumentExpression("arg")] string argName = "", [CallerMemberName] string caller = "",
      [CallerArgumentExpression("condition")]
      string conditionName = "")
   {
      if (condition)
      {
         return;
      }

      message ??= "we expected: " + conditionName;

      Throw(message, arg, argName, caller);
   }
}
/// <summary>
/// easy invoke FluentValidation and access other validation services
/// </summary>
/// <param name="_services"></param>
/// <param name="_logger"></param>
/// <param name="_serviceValidator"></param>
public class EzValidator(IServiceProvider _services, ILogger<EzValidator> _logger, ServiceValidator _serviceValidator, SimpleValidator _simpleValidator) : ISingletonService
{
   public ServiceValidator Service => _serviceValidator;

   public SimpleValidator Simple => _simpleValidator;

   /// <summary>
   /// automatically validates the payload with all DI auto-registered validators  (IValidator<typeparamref name="T"/>)
   /// </summary>
   public bool TryValidate<T>(T payload, out Problem? problem, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      problem = default;
      var validator = _services.GetService<IValidator<T>>();
      if (validator is null)
      {
         problem = new Problem(memberName, sourceFilePath, sourceLineNumber)
         {
            Status = 500,
            Title = $"no validator found for type {typeof(T).Name}",
            category = Problem.CategoryNames.Validation,
         };

         var ex = problem.ToException();
         var logger = __.GetLogger();
         __.GetLogger()._EzError(ex);
         throw ex;

         return false;
      }
      var result = validator.Validate(payload);
      if (result.IsValid)
      {
         return true;
      }

      var errors = new Dictionary<string, string[]>();
      foreach (var err in result.Errors)
      {
         if (errors.TryGetValue(err.PropertyName, out var arr))
         {
            errors[err.PropertyName] = arr.Append(err.ErrorMessage).ToArray();
         }
         else
         {
            errors[err.PropertyName] = new[] { err.ErrorMessage };
         }
      }

      problem = new Problem(new ValidationProblemDetails()
      {
         //Status = 400,
         Title = $"validation failed for {typeof(T).Name}",
         Detail = result.ToString(),
         Errors = errors,
      }, memberName, sourceFilePath, sourceLineNumber)
      {
         category = Problem.CategoryNames.Validation,
      };

      return false;
   }
   /// <summary>
   ///  automatically validates the payload with all DI auto-registered validators  (IValidator<typeparamref name="T"/>) 
   /// </summary>
   public Maybe<T> Validate<T>(T payload, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
      where T : class
   {
      if (TryValidate(payload, out var problem, memberName, sourceFilePath, sourceLineNumber))
      {
         return new(payload, memberName, sourceFilePath, sourceLineNumber);
      }
      return new(problem!, memberName, sourceFilePath, sourceLineNumber);
   }

   /// <summary>
   /// apply a custom validator to the payload (not one registered with DI)
   /// </summary>
   public Maybe<TPayload> Custom<TValidator, TPayload>(TPayload payload)
      where TValidator : AbstractValidator<TPayload>, new()
   {
      var validator = new TValidator();
      var result = validator.Validate(payload);
      if (result.IsValid)
      {
         return new(payload);
      }
      //return problem
      var errors = new Dictionary<string, string[]>();
      foreach (var err in result.Errors)
      {
         if (errors.TryGetValue(err.PropertyName, out var arr))
         {
            errors[err.PropertyName] = arr.Append(err.ErrorMessage).ToArray();
         }
         else
         {
            errors[err.PropertyName] = new[] { err.ErrorMessage };
         }
      }

      var problemDetails = new ValidationProblemDetails()
      {
         //Status = 400,
         Title = $"validation failed for {typeof(TPayload).Name}",
         Detail = result.ToString(),
         Errors = errors,
      };
      var problem = new Problem(problemDetails)
      {
         category = Problem.CategoryNames.Validation,
      };

      return Maybe<TPayload>.Error(problem);
   }

}