// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
#pragma warning disable CS8963
namespace NotNot._graveyard
{
   /// <summary>
   ///    Debug helper used in #CHECKED builds.  Checked builds perform extra checks to ensure thread safety and detect data
   ///    corruption
   /// </summary>
   [DebuggerNonUserCode]
   [DebuggerStepThrough]
   [Obsolete("use __.GetLogger() instead", true)]
   public class CheckedDiag
   {
      protected DiagHelper _diagHelper;

      //public Verify Verify;

      public CheckedDiag(DiagHelper diagHelper)
      {
         _diagHelper = diagHelper;
         //  Verify = new Verify(diagHelper, DiagHelper.EnvLevel.CHECKED);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Assert(string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = "",
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         Assert(false, message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
            sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Assert(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Assert(DiagHelper.EnvLevel.CHECKED, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void AssertOnce(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.AssertOnce(DiagHelper.EnvLevel.CHECKED, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Throw(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0
      )
      {
         _diagHelper.Throw<LoLoDiagnosticsException>(DiagHelper.EnvLevel.ERROR, false, message, innerException, "", memberName,
            sourceFilePath, sourceLineNumber);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Throw<TExceptionToThrow>([DoesNotReturnIf(false)] bool condition, string message = null,
         Exception? innerException = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0
      )
         where TExceptionToThrow : Exception
      {
         _diagHelper.Throw<TExceptionToThrow>(DiagHelper.EnvLevel.CHECKED, condition, message, innerException, conditionName,
            memberName, sourceFilePath, sourceLineNumber);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Throw<TExceptionToThrow>(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
         where TExceptionToThrow : Exception
      {
         _diagHelper.Throw<TExceptionToThrow>(DiagHelper.EnvLevel.CHECKED, false, message, innerException, "", memberName,
            sourceFilePath, sourceLineNumber);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Kill(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
      {
         _diagHelper.Kill(DiagHelper.EnvLevel.CHECKED, false, message, innerException, "", memberName, sourceFilePath,
            sourceLineNumber);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Kill([DoesNotReturnIf(false)] bool condition, string message = null, Exception? innerException = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
      {
         _diagHelper.Kill(DiagHelper.EnvLevel.CHECKED, condition, message, innerException, conditionName, memberName,
            sourceFilePath, sourceLineNumber);
      }

      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Log(string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Log(DiagHelper.EnvLevel.CHECKED, message, objToLog1, objToLog2, objToLog3, conditionName,
            memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [Conditional("CHECKED")]
      public void Log(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Log(DiagHelper.EnvLevel.CHECKED, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }
   }

   [DebuggerNonUserCode]
   [DebuggerStepThrough]
   [Obsolete("use __.GetLogger() instead", true)]
   public class DebugDiag
   {
      protected DiagHelper _diagHelper;

      public DebugDiag(DiagHelper diagHelper)
      {
         _diagHelper = diagHelper;
      }

      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Assert(string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = "",
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         Assert(false, message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
            sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      /// <summary>
      ///    Asserts if condition evaluates to false.
      /// </summary>
      /// <param name="condition"></param>
      /// <param name="message"></param>
      /// <param name="conditionName"></param>
      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Assert(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Assert(DiagHelper.EnvLevel.DEBUG, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void AssertOnce(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.AssertOnce(DiagHelper.EnvLevel.DEBUG, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Throw(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0
      )
      {
         _diagHelper.Throw<LoLoDiagnosticsException>(DiagHelper.EnvLevel.ERROR, false, message, innerException, "", memberName,
            sourceFilePath, sourceLineNumber);
      }


      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Throw<TExceptionToThrow>(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
         where TExceptionToThrow : Exception
      {
         _diagHelper.Throw<TExceptionToThrow>(DiagHelper.EnvLevel.DEBUG, false, message, innerException, "", memberName,
            sourceFilePath, sourceLineNumber);
      }

      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Throw<TExceptionToThrow>([DoesNotReturnIf(false)] bool condition, string message = null,
         Exception? innerException = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
         where TExceptionToThrow : Exception
      {
         _diagHelper.Throw<TExceptionToThrow>(DiagHelper.EnvLevel.DEBUG, condition, message, innerException, conditionName,
            memberName, sourceFilePath, sourceLineNumber);
      }

      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Kill(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
      {
         _diagHelper.Kill(DiagHelper.EnvLevel.DEBUG, false, message, innerException, "", memberName, sourceFilePath,
            sourceLineNumber);
      }

      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Kill([DoesNotReturnIf(false)] bool condition, string message = null, Exception? innerException = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0)
      {
         _diagHelper.Kill(DiagHelper.EnvLevel.DEBUG, condition, message, innerException, conditionName, memberName,
            sourceFilePath, sourceLineNumber);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      public void Log(string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Log(DiagHelper.EnvLevel.DEBUG, message, objToLog1, objToLog2, objToLog3, conditionName, memberName,
            sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [Conditional("DEBUG")]
      [Conditional("CHECKED")]
      public void Log(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Log(DiagHelper.EnvLevel.DEBUG, condition, message, objToLog1, objToLog2, objToLog3, conditionName,
            memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }
   }

   /// <summary>
   ///    does logging / diag functionality.  Always available (not disabled in #RELEASE)
   /// </summary>
   [DebuggerNonUserCode]
   [DebuggerStepThrough]
   [Obsolete("use __.GetLogger() instead", true)]
   public class ErrorDiag
   {
      protected DiagHelper _diagHelper;

      public ErrorDiag(DiagHelper diagHelper)
      {
         _diagHelper = diagHelper;
      }


      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Assert(string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = "",
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         Assert(false, message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
            sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Assert(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Assert(DiagHelper.EnvLevel.ERROR, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void AssertOnce(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.AssertOnce(DiagHelper.EnvLevel.ERROR, condition, message, objToLog1, objToLog2, objToLog3,
            conditionName, memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Throw(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0
      //[CallerArgumentExpression("objToLog1")] string? objToLog1Name = "null",
      //[CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
      //[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null"
      )
      {
         _diagHelper.Throw<LoLoDiagnosticsException>(DiagHelper.EnvLevel.ERROR, false, message, innerException, "", memberName,
            sourceFilePath, sourceLineNumber);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Throw<TExceptionToThrow>(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0
      //[CallerArgumentExpression("objToLog1")] string? objToLog1Name = "null",
      //[CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
      //[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null"
      )
         where TExceptionToThrow : Exception
      {
         _diagHelper.Throw<TExceptionToThrow>(DiagHelper.EnvLevel.ERROR, false, message, innerException, "", memberName,
            sourceFilePath, sourceLineNumber);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Throw<TExceptionToThrow>([DoesNotReturnIf(false)] bool condition, string message = null,
         Exception? innerException = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0
      //[CallerArgumentExpression("objToLog1")] string? objToLog1Name = "null",
      //[CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
      //[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null"
      )
         where TExceptionToThrow : Exception
      {
         _diagHelper.Throw<TExceptionToThrow>(DiagHelper.EnvLevel.ERROR, condition, message, innerException, conditionName,
            memberName, sourceFilePath, sourceLineNumber);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      [DoesNotReturn]
      public void Kill(string message = null, Exception? innerException = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Kill(DiagHelper.EnvLevel.ERROR, false, message, innerException, "", memberName, sourceFilePath,
            sourceLineNumber);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Kill([DoesNotReturnIf(false)] bool condition, string message = null, Exception? innerException = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Kill(DiagHelper.EnvLevel.ERROR, condition, message, innerException, conditionName, memberName,
            sourceFilePath, sourceLineNumber);
      }


      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Log(string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Log(DiagHelper.EnvLevel.ERROR, message, objToLog1, objToLog2, objToLog3, conditionName, memberName,
            sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }

      [DebuggerNonUserCode]
      [DebuggerHidden]
      public void Log(bool condition, string message = null, object? objToLog1 = null, object? objToLog2 = null,
         object? objToLog3 = null,
         [CallerArgumentExpression("condition")]
         string? conditionName = null,
         [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
         [CallerLineNumber] int sourceLineNumber = 0,
         [CallerArgumentExpression("objToLog1")]
         string? objToLog1Name = "null",
         [CallerArgumentExpression("objToLog2")]
         string? objToLog2Name = "null",
         [CallerArgumentExpression("objToLog3")]
         string? objToLog3Name = "null")
      {
         _diagHelper.Log(DiagHelper.EnvLevel.ERROR, condition, message, objToLog1, objToLog2, objToLog3, conditionName,
            memberName, sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
      }
   }
}
