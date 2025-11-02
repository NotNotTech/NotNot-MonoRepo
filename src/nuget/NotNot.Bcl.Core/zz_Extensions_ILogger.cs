using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NotNot.Serialization;



/// <summary>
///    wrapper over Microsoft.Extensions.Logging.ILogger to include callsite and reduce boilerplate.
/// </summary>
[SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]

[DebuggerNonUserCode]
public static class zz_Extensions_ILogger
{
   public static int MaxFileInfoFolderDepth = 3;

   /// <summary>
   /// internal thread safety
   /// </summary>
   private static object _lock = new();
   /// <summary>
   /// internal cache to reduce allocations
   /// </summary>
   //private static List<(string name, object? value)> _argPairsCache = new();

   [DebuggerNonUserCode]
   private static void _Ez(this ILogger logger, LogLevel level, string? message = "", object? objToLog0 = null,
      object? objToLog1 = null, object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")] string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")] string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")] string? objToLog2Name = null,
      Exception? ex = null,
      [CallerArgumentExpression("ex")] string? exName = null,
      Span<string> tags = default)
   {
      //store all (objToLog,objToLogName) pairs in a list, discarding any pairs with an objToLogName of null
      //create a finalLogMessage combining the message with the names from each pair, showing the values from each pair      
      //pass the finalLogMessage and all the values to the Microsoft.Extensions.Logging.ILogger.Log method
      //that ILogger.Log has the following signature: public static void Log(this ILogger logger, LogLevel logLevel, Exception? exception, string? message, params object?[] args)

      var originalMessage = message;
      lock (_lock)
      {

         try
         {
            //	var argPairs =Mem. _argPairsCache;
            using var argPairOwner = __.pool.Rent<List<(string name, object? value)>>(out var argPairs);

#if DEBUG
            if (argPairs.Count > 0)
            {
               throw new Exception("argPairs.Count > 0.  argPairs is a cached temp object.  likely there is some multithreading going on, and this needs to be made thread safe");
            }
#endif
            if (objToLog0 is not null || (objToLog0Name is not null && string.IsNullOrWhiteSpace(objToLog0Name) is false))
            {
               argPairs.Add((objToLog0Name, objToLog0));
            }

            if (objToLog1 is not null || (objToLog1Name is not null && string.IsNullOrWhiteSpace(objToLog1Name) is false))
            {
               argPairs.Add((objToLog1Name, objToLog1));
            }

            if (objToLog2 is not null || (objToLog2Name is not null && string.IsNullOrWhiteSpace(objToLog2Name) is false))
            {
               argPairs.Add((objToLog2Name, objToLog2));
            }

            //roundtrip argValues to json to avoid logger (serilog) max depth errors
            {
               for (var i = 0; i < argPairs.Count; i++)
               {
                  try
                  {
                     var obj = argPairs[i].value;

                     if (obj is null)
                     {
                        continue;
                     }

                     if (obj.GetType().IsEnum)
                     {
                        //special handling for enum values
                        argPairs[i] = (argPairs[i].name, $"{obj.ToString()} ({((uint)obj).ToString("X")})");
                     }
                     else
                     {
                        argPairs[i] = (argPairs[i].name, __.SerializationHelper.ToLogPoCo(obj));
                     }
                  }
                  catch (Exception err)
                  {
                     logger.LogError($"could not roundtrip {argPairs[i].name} due to error {argPairs[i].value}.", err);
                     throw;
                  }
               }
            }



            //sanitize message to avoid serilog errors
            {
               //__.Assert(message?.Contains('{') is false, "serilog throws errors if braces are in message");
               message = message._Replace("{}", '_');
            }

            //add callsite to output message/args
            {
               var method = $"{sourceMemberName}";
               //argPairs.Add(("method", method));

               //var callsite = $"{sourceFilePath}({sourceLineNumber}); ";


               var callsite = $"{method} @ {sourceFilePath}({sourceLineNumber})";
               argPairs.Add(("callsite", callsite));
            }

            //adjust our message to include all arg Name+Values
            for (var i = 0; i < argPairs.Count; i++)
            {
               var (argName, argValue) = argPairs[i];

               argName = argName.Trim('"');

               //serilog can't log braces so replace them with aproximates 
               var sanitizedArgName =
                  argName._ConvertToAlphanumeric(); //?._Replace(" {}[]", '_'); //?.Replace('{', '[').Replace('}', ']');

               if (
                  argName.Equals(argValue) //string variable as argument
                  || argName.Equals(argValue?.ToString()) //primitive variable as argument
                  || argName.Contains(',')) //tuple as argument
               {
                  //argName is the same as the value that will be logged, so just set it to "_arg{i}" to avoid redundancy
                  sanitizedArgName = $"_arg{i}";
               }

               sanitizedArgName ??= $"_UNKNOWN{i}";

               message += $"\n\t{sanitizedArgName} : {{@{sanitizedArgName}}}";
            }

            //copy argValues for passing to base logger (object[] params)
            var argValues = __.pool.GetArray<object?>(argPairs.Count);
            for (var i = 0; i < argPairs.Count; i++)
            {
               argValues[i] = argPairs[i].value;
            }

            if (tags.Length > 0)
            {
               message += $"\n\ttags:[{string.Join(", ", tags)}]";
            }

            //invoke base ILogger functionality with our ez message/objsToLogg/Callsite            
            if (ex is null)
            {
               logger.Log(level, message, argValues);
            }
            else
            {
               message = $"[Exception:{exName}] " + message;
               logger.Log(level, ex, message, argValues);
            }

            //if running XUnit, also write to test output (console)
            if (TestHelper.isTestingActive)
            {
               __.Test.Write($"[{level.ToString().ToUpperInvariant()}] {originalMessage}", objToLog0, objToLog1, objToLog2, sourceMemberName, sourceFilePath, sourceLineNumber, objToLog0Name, objToLog1Name, objToLog2Name);
            }

            __.pool.ReturnArray(argValues);
         }
         finally
         {
            //_argPairsCache.Clear();
         }
      }
   }

   public static void _EzTrace(this ILogger logger, string? message, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      logger._Ez(LogLevel.Trace, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzDebug(this ILogger logger, string? message, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      logger._Ez(LogLevel.Debug, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzWarn(this ILogger logger, string? message, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default)
   {
      logger._Ez(LogLevel.Warning, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzInfo(this ILogger logger, string? message, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      logger._Ez(LogLevel.Information, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   [DebuggerNonUserCode]
   public static void _EzError(this ILogger logger, string? message, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      logger._Ez(LogLevel.Error, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzTrace(this ILogger logger, bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      if (condition)
      {
         return;
      }
      logger._Ez(LogLevel.Trace, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzDebug(this ILogger logger, bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      if (condition)
      {
         return;
      }
      logger._Ez(LogLevel.Debug, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzWarn(this ILogger logger, bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default)
   {
      logger._Ez(LogLevel.Warning, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzInfo(this ILogger logger, bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      if (condition)
      {
         return;
      }
      logger._Ez(LogLevel.Information, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   public static void _EzError(this ILogger logger, bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      if (condition)
      {
         return;
      }
      logger._Ez(LogLevel.Error, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }

   [DoesNotReturn]
   public static void _EzErrorThrow(this ILogger logger, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      logger._EzErrorThrow(false, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
   }
   public static void _EzErrorThrow(this ILogger logger, [DoesNotReturnIf(false)] bool condition, string message = "_EzErrorThrow", object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   )
   {
      if (condition)
      {
         return;
      }
      logger._Ez(LogLevel.Error, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);
      if (tags.Length > 0)
      {
         message += $"\n\ttags:[{string.Join(", ", tags)}]";
      }
      throw new LoLoDiagnosticsException(message._FormatAppendArgs(objToLog0, objToLog1, objToLog2, objToLog0Name, objToLog1Name, objToLog2Name), sourceMemberName, sourceFilePath, sourceLineNumber);
   }

   public static void _EzErrorThrow<TException>(this ILogger logger, [DoesNotReturnIf(false)] bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      [CallerArgumentExpression("condition")]
      string? conditionName = null,
      Span<string> tags = default
   ) where TException : Exception, new()
   {
      if (condition)
      {
         return;
      }
      logger._Ez(LogLevel.Error, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, tags: tags);

      if (tags.Length > 0)
      {
         message += $"\n\ttags:[{string.Join(", ", tags)}]";
      }

      TException ex = null;
      try
      {
         ex = typeof(TException)._CreateInstance<TException>($"ERROR_THROW({conditionName}) {message}");
         ex.Source = $"{sourceMemberName}:{sourceFilePath}:{sourceLineNumber}";
         throw ex;
      }
      catch (Exception e)
      {
         throw new LoLoDiagnosticsException(message._FormatAppendArgs(objToLog0, objToLog1, objToLog2, objToLog0Name, objToLog1Name, objToLog2Name)
            + $"(Could not create {typeof(TException).Name}, creating LoLoDiagnosticsException instead)"
            , sourceMemberName, sourceFilePath, sourceLineNumber);
      }
   }
   [Conditional("CHECKED")]
   public static void _EzCheckedThrow<TException>(this ILogger logger, [DoesNotReturnIf(false)] bool condition, string? message = null, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      Span<string> tags = default
   ) where TException : Exception, new()
   {
      logger._EzErrorThrow<TException>(condition, message, objToLog0, objToLog1, objToLog2, sourceMemberName, sourceFilePath, sourceLineNumber, objToLog0Name, objToLog1Name, objToLog2Name, tags: tags);

   }

   public static TException _EzTrace<TException>(this ILogger logger, TException ex, string? message = null, object? objToLog0 = null,
   object? objToLog1 = null, object? objToLog2 = null,
   [CallerMemberName] string sourceMemberName = "",
   [CallerFilePath] string sourceFilePath = "",
   [CallerLineNumber] int sourceLineNumber = 0,
   [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
   [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
   [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
   [CallerArgumentExpression("ex")] string? exName = null,
   Span<string> tags = default
) where TException : Exception
   {
      logger._Ez(LogLevel.Trace, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, ex, exName, tags: tags);
      return ex;
   }

   public static TException _EzInfo<TException>(this ILogger logger, TException ex, string? message = null, object? objToLog0 = null,
      object? objToLog1 = null, object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      [CallerArgumentExpression("ex")] string? exName = null,
      Span<string> tags = default
   ) where TException : Exception
   {
      logger._Ez(LogLevel.Information, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, ex, exName, tags);
      return ex;
   }


   public static TException _EzDebug<TException>(this ILogger logger, TException ex, string? message = null, object? objToLog0 = null,
      object? objToLog1 = null, object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      [CallerArgumentExpression("ex")] string? exName = null,
      Span<string> tags = default
   ) where TException : Exception
   {
      logger._Ez(LogLevel.Debug, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, ex, exName, tags: tags);
      return ex;
   }


   public static TException _EzWarn<TException>(this ILogger logger, TException ex, string? message = null, object? objToLog0 = null,
      object? objToLog1 = null, object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      [CallerArgumentExpression("ex")] string? exName = null,
      Span<string> tags = default
   ) where TException : Exception
   {
      logger._Ez(LogLevel.Warning, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, ex, exName, tags: tags);
      return ex;
   }
   public static TException _EzError<TException>(this ILogger logger, TException ex, string? message = null, object? objToLog0 = null,
      object? objToLog1 = null, object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = null,
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = null,
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = null,
      [CallerArgumentExpression("ex")] string? exName = null,
      Span<string> tags = default
   ) where TException : Exception
   {
      logger._Ez(LogLevel.Error, message, objToLog0, objToLog1, objToLog2
         , sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber, objToLog0Name,
         objToLog1Name, objToLog2Name, ex, exName, tags: tags);
      return ex;
   }



   public static void _Kill(this ILogger logger, bool condition, string? message = null, Exception? innerException = null,
           [CallerArgumentExpression("condition")]
                string? conditionName = null,
                [CallerMemberName] string sourceMemberName = "", [CallerFilePath] string sourceFilePath = "",
                     [CallerLineNumber] int sourceLineNumber = 0)
   {
      logger._KillHelper(LogLevel.Error, condition, message, innerException, conditionName, sourceMemberName, sourceFilePath,
                 sourceLineNumber);
   }
   public static void _Kill(this ILogger logger, string message, Exception? innerException = null,
                               //[CallerArgumentExpression("condition")]
                               string? conditionName = null,
                               [CallerMemberName] string sourceMemberName = "", [CallerFilePath] string sourceFilePath = "",
                                                   [CallerLineNumber] int sourceLineNumber = 0)
   {
      logger._KillHelper(LogLevel.Error, true, message, innerException, conditionName, sourceMemberName, sourceFilePath,
                         sourceLineNumber);
   }
   public static void _Kill(this ILogger logger, Exception innerException,
                                                   //[CallerArgumentExpression("condition")]
                                                   string? conditionName = null,
                                                   [CallerMemberName] string sourceMemberName = "", [CallerFilePath] string sourceFilePath = "",
                                                                                                     [CallerLineNumber] int sourceLineNumber = 0)
   {
      logger._KillHelper(LogLevel.Error, true, null, innerException, conditionName, sourceMemberName, sourceFilePath,
                                 sourceLineNumber);
   }

   /// <summary>
   ///    something super bad happened. log it and kill the process
   /// </summary>
   [DebuggerNonUserCode]
   [DebuggerHidden]
   [DoesNotReturn]
   private static void _KillHelper(this ILogger logger, LogLevel level, bool condition, string? message = null, Exception? innerException = null,
      [CallerArgumentExpression("condition")]
      string? conditionName = null,
      [CallerMemberName] string sourceMemberName = "", [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      if (condition)
      {
         return;
      }

      message ??= "KILL condition failed";

      var ex = new LoLoDiagnosticsException($"{level}_KILL({conditionName}) {message}", innerException);
      ex.Source = $"{sourceMemberName}:{sourceFilePath}:{sourceLineNumber}";

      logger._Ez(level: level, ex: ex, message: message, sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);

      _BreakIntoDebugger();

      Environment.FailFast(ex.Message, ex);
   }

   /// <summary>
   ///    helper to break into the debugger
   /// </summary>
   [DebuggerNonUserCode]
   [DebuggerHidden]
   private static void _BreakIntoDebugger()
   {
      if (Debugger.IsAttached == false)
      {
         Debugger.Launch();
      }

      if (Debugger.IsAttached)
      {
         Debugger.Break();
      }
   }


   public static bool _If(this ILogger logger, LogLevel level, Action? action = null)
   {
      if (logger.IsEnabled(level))
      {
         if (action is not null)
         {
            action.Invoke();
         }

         return true;
      }

      return false;
   }


   public static bool _IfTrace(this ILogger logger, Action? action = null)
   {
      return logger._If(LogLevel.Trace, action);
   }

   public static bool _IfDebug(this ILogger logger, Action? action = null)
   {
      return logger._If(LogLevel.Debug, action);
   }

   public static bool _IfWarn(this ILogger logger, Action? action = null)
   {
      return logger._If(LogLevel.Warning, action);
   }

   public static bool _IfError(this ILogger logger, Action? action = null)
   {
      return logger._If(LogLevel.Error, action);
   }

   public static bool _IfInfo(this ILogger logger, Action? action = null)
   {
      return logger._If(LogLevel.Information, action);
   }

}
