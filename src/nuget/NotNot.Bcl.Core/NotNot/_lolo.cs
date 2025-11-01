using System.Diagnostics;
using System.Runtime.CompilerServices;
//using Xunit.Abstractions;
//using Xunit.Sdk;

#pragma warning disable CA1001

namespace NotNot;

[DebuggerNonUserCode]
public class TestHelper
{
   [DebuggerNonUserCode]
   public async Task ExpectFailure(Func<Task> action, string? message = null, [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      try
      {
         await action();
      }
      catch (Exception)
      {
         return;
      }

      __.GetLogger()._Kill($"Expected an exception to be thrown, but none was.  {message}", sourceMemberName: sourceMemberName,
         sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);

   }

   [DebuggerNonUserCode]
   public async Task ExpectSuccess(Func<Task> action, string? message = null, [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      try
      {
         await action();
      }
      catch (Exception ex)
      {
         __.GetLogger()._Kill($"Expected success.  {message}", innerException: ex, sourceMemberName: sourceMemberName,
            sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
      }
   }

   //private ITestOutputHelper _testOutputHelper;
   private object _testOutputHelper;

   IEnumerable<string> _ignoreOutputRegex;

   //  /// <summary>
   //  /// each XUnit test run (class constructor) should invoke this  to enable console output
   //  /// <para>every test needs to set the output helper so console writes occur</para>
   //  /// <para>be sure to call .DisposeTest() in your test's dispose method to unhook</para>
   //  /// </summary>
   //  /// <param name="testOutputHelper"></param>
   //  /// <param name="ignoreOutputRegex"></param>
   //  public void InitTest(ITestOutputHelper testOutputHelper, IEnumerable<string>? ignoreOutputRegex = default)
   //  {
   //     _testOutputHelper = testOutputHelper;
   //     _ignoreOutputRegex = ignoreOutputRegex ?? new string[0];
   //     IsTestingActive = true;

   //}
   //  public void InitTest(ITestOutputHelper testOutputHelper, params string[] ignoreOutputRegex)
   //  {
   //     InitTest(testOutputHelper, (IEnumerable<string>)ignoreOutputRegex);
   //  }
   public void InitTest()
   {
      IsTestingActive = true;

   }

   /// <summary>
   /// each XUnit test run (class dispose) should invoke this  to cleanup console output hooks
   /// </summary>
   public void DisposeTest()
   {
      _testOutputHelper = null;
      _ignoreOutputRegex = null;
   }

   private static readonly Lazy<bool> _isXunitTest = new(() =>
   {
      return AppDomain.CurrentDomain.GetAssemblies()
          .Any(a => a.FullName?.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase) == true);
   });

   public static bool IsXunitTest => _isXunitTest.Value;

   private bool? _isTestingActive;
   public bool IsTestingActive
   {
      get
      {
         if (_isTestingActive.HasValue is false)
         {
            if (_testOutputHelper is not null || _isXunitTest.Value)
            {
               _isTestingActive = true;
            }
            else
            {
               _isTestingActive = false;
            }
         }


         return _isTestingActive.Value;

      }
      set
      {
         _isTestingActive = value;
      }
   }


   /// <summary>
   /// output to the test runner console.   This is automatically called by ILogger sink so usually you don't need to call this yourself.
   /// <para>alternatively, call __.DevTrace(msg) for easy trace messages output to ILogger (and thus to test runner console also)</para>
   /// </summary>
   [Conditional("DEBUG"), Conditional("TRACE")]
   public void Write(string message, object? objToLog0 = null, object? objToLog1 = null,
      object? objToLog2 = null,
      [CallerMemberName] string sourceMemberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0,
      [CallerArgumentExpression("objToLog0")]
      string? objToLog0Name = "null",
      [CallerArgumentExpression("objToLog1")]
      string? objToLog1Name = "null",
      [CallerArgumentExpression("objToLog2")]
      string? objToLog2Name = "null"
   )
   {

      if (_testOutputHelper is null)
      {
         return;
      }
      var msg = message._FormatAppendArgs(objToLog0, objToLog1, objToLog2, objToLog0Name, objToLog1Name, objToLog2Name);
      var method = sourceMemberName;
      var callsite = $"{sourceFilePath}:{sourceLineNumber}";
      msg = msg._FormatAppendArgs(method, callsite);

      var prefix = $"<{DateTime.UtcNow.ToLocalTime().ToString("HH:mm:ss.fff")}>";// {sourceFilePath._GetAfter("\\",true)}:{sourceLineNumber}>{sourceMemberName}|";
      var padding = 1;// 30 - prefix.Length;
      if (padding < 0)
      {
         padding = 0;
      }
      try
      {
         var completeMsg = $"{prefix}{" "._Repeat(padding)}{msg}";
         foreach (var ignore in _ignoreOutputRegex)
         {
            if (ignore._ToRegex().IsMatch(completeMsg))
            {
               //noop
               return;
            }
         }
         //write to console
         __.placeholder.ToDo("implement below");
         //_testOutputHelper.WriteLine(completeMsg);
      }
      catch (InvalidOperationException e)
      {
         //noop
      }
   }
}

public class LoLoConfig
{
   /// <summary>
   ///    if set to true, can use the CancellationTokenSource._DebuggableCancelAfter()
   ///    extension method for timeouts, and pausing in the debugger: won't cause
   ///    the timeout to be triggered.
   ///    <para>this is useful for stepping through code without timeouts expiring.</para>
   /// </summary>
   public bool IsCtsDebuggableCancelTimeoutEnabled { get; init; } = true;

   /// <summary>
   ///    Set to true to force all tasks scheduled via `__.Async` to execute on a single thread.
   ///    This is very useful for debugging, as your breakpoints won't jump between threads.
   ///    However care has to be taken to avoid livelocks:  In tasks that spin, ensure that you
   ///    periodically yield the thread by calling `await Task.Yield()`.
   /// </summary>
   public bool IsDebuggableTaskFactorySingleThreaded { get; init; } = false;
}
