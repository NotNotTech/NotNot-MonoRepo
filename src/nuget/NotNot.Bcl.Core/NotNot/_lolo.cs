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
#pragma warning disable NN_R005 // Test utility - must catch all exception types
      catch (Exception)
      {
         return;
      }
#pragma warning restore NN_R005

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
#pragma warning disable NN_R005 // Test utility - must catch all exception types
      catch (Exception ex)
      {
         __.GetLogger()._Kill($"Expected success.  {message}", innerException: ex, sourceMemberName: sourceMemberName,
            sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
      }
#pragma warning restore NN_R005
   }

   //private ITestOutputHelper _testOutputHelper;
   [Obsolete("use InitTest() without parameters, likely no longer needed for xunit?")]
   private object? _testOutputHelper;

   [Obsolete("use InitTest() without parameters, likely no longer needed for xunit?")]
   IEnumerable<string>? _ignoreOutputRegex;

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
      isTestingActive = true;

   }

   /// <summary>
   /// each XUnit test run (class dispose) should invoke this  to cleanup console output hooks
   /// </summary>
   public void DisposeTest()
   {
      _testOutputHelper = null;
      _ignoreOutputRegex = null;
   }


   private static bool? _isXUnitTest;
   public static bool IsXunitTest
   {
      get
      {
         if (_isXUnitTest.HasValue is false)
         {
            _isXUnitTest = AppDomain.CurrentDomain.GetAssemblies()
          .Any(a => a.FullName?.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase) == true);
         }
         return _isXUnitTest.Value;
      }
   }
   /// <summary>
   /// set to true by running `__.Test.InitTest() at test runner startup.`must be a static value type, otherwise some runtimes (Godot) can't unload properly
   /// </summary>
   public static bool isTestingActive
   {
      //   get; set;
      get
      {
#if CHECKED
         if (field is false && IsXunitTest)
         {
            __.Assert("running XUnit but you did not call `__.Test.InitTest()` at test startup.  Please call this to properly init runtime test notification.");
         }
#endif
         return field;


      }
      set;
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
         foreach (var ignore in _ignoreOutputRegex ?? Enumerable.Empty<string>())
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
      catch (InvalidOperationException)
      {
         // Test output helper may be disposed - safe to ignore
         return;
      }
   }
}

public record LoLoConfig
{
   /// <summary>
   ///    if set to true, DISABLES the CancellationTokenSource._DebuggableCancelAfter()
   ///    extension method's debug-aware logic and falls back to the standard CancelAfter
   ///    behavior. Default false: debug-aware logic is enabled; pausing in the debugger
   ///    won't cause the timeout to be triggered.
   ///    <para>The debug-aware mode is useful for stepping through code without timeouts
   ///    expiring; set this to true if the debug-aware behavior interferes with your
   ///    scenario.</para>
   /// </summary>
   public bool IsCtsDebuggableCancelTimeoutDisabled { get; init; } = false;

   /// <summary>
   ///    Set to true to force all tasks scheduled via `__.Async` to execute on a single thread.
   ///    This is very useful for debugging, as your breakpoints won't jump between threads.
   ///    However care has to be taken to avoid livelocks:  In tasks that spin, ensure that you
   ///    periodically yield the thread by calling `await Task.Yield()`.
   /// </summary>
   public bool IsDebuggableTaskFactorySingleThreaded { get; init; } = false;

   /// <summary>
   ///    When TRUE, all <see cref="LoLoRoot.AssertIfNot"/> / <see cref="LoLoRoot.Assert(Exception, string, string, int)"/>
   ///    paths skip the underlying <see cref="System.Diagnostics.Debug.Assert(bool, string)"/> AND
   ///    <see cref="NotNot.Diagnostics.Advanced._Debugger.LaunchOnce"/> calls. Failure is still logged
   ///    via <see cref="System.Diagnostics.Debug.WriteLine(string?)"/> and <see cref="LoLoRoot.GetLogger()"/>
   ///    so the assertion event remains observable.
   ///
   ///    <para>Required TRUE in WASM Debug builds — otherwise <c>Debug.Assert(false, ...)</c> →
   ///    <c>DebugProvider.Fail</c> → <c>FailCore</c> → <c>Environment.FailFast</c> kills the WASM tab
   ///    (verified at iter2 retest 2026-05-07; see VibeDiagnostic 20260508-1130).</para>
   ///
   ///    <para>Recommended TRUE in test runners — replaces the legacy reflection workaround on
   ///    <c>_Debugger._hasLaunched</c> (which only suppressed the desktop "Attach debugger?" modal
   ///    and was a no-op for the Exception throw on <c>Debug.Assert</c>).</para>
   ///
   ///    <para>Default FALSE preserves current desktop-dev abort + debugger-prompt experience.
   ///    The gate is RUNTIME-ONLY (no `#if DEBUG` wrap): in Release builds, the
   ///    <c>Debug.Assert(false, ...)</c> body is stripped by <c>[Conditional("DEBUG")]</c>
   ///    regardless of this flag, but <c>_Debugger.LaunchOnce()</c> is NOT
   ///    <c>[Conditional]</c> and STILL runs in knob-FALSE Release. Set knob TRUE in
   ///    Release to also suppress LaunchOnce while preserving Debug.WriteLine + Logger
   ///    sinks for observability.</para>
   /// </summary>
   public bool IsDebugAssertSuppressed { get; init; } = false;
}
