using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NotNot._internal.Diagnostics;

/// <summary>
///    logic that is run in #DEBUG but throws exceptions when called in #RELEASE
///    Related but "opposed" to the <see cref="Assert" /> class
/// </summary>
public class Placeholder
{
   [DebuggerHidden]
   [DebuggerNonUserCode]
   public void Deprecated([CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      //throw new NotImplementedException();
      //__.GetLogger._EzWarn("DEPRECATED");
      __.GetLogger<Placeholder>()._EzWarn("DEPRECATED", memberName: memberName, sourceFilePath: sourceFilePath,
         sourceLineNumber: sourceLineNumber);
   }

   [DebuggerHidden]
   [DebuggerNonUserCode]
   [DoesNotReturn]
   public void NotImplemented([CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      __.GetLogger<Placeholder>()._Kill("Not Implemented", memberName: memberName, sourceFilePath: sourceFilePath,
         sourceLineNumber: sourceLineNumber);
      throw new NotImplementedException();
   }

   public Task Delay(double minSeconds, double maxSeconds, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      _ThrowIfRelease();
      ToDo("Delay()", memberName: memberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);

      return __.Async.Delay(__.Random._NextTimeSpan(minSeconds, maxSeconds));
   }

   [DebuggerHidden]
   [DebuggerNonUserCode]
   public Task Delay(double maxSeconds = 0.1, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      _ThrowIfRelease();
      ToDo("Delay()", memberName: memberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);

      return __.Async.Delay(__.Random._NextTimeSpan(0, maxSeconds));
	}


   private HashSet<string> _todoWarnOnceCache = new();
	/// <summary>
	/// shows log message (once per callsite), and will throw in RELEASE builds
	/// </summary>
	[DebuggerHidden]
   [DebuggerNonUserCode]
   public void ToDo(string message = "Do Soon", [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      _ThrowIfRelease();

#if DEBUG
      var warnOnceKey = $"{sourceFilePath}:{sourceLineNumber}";
      if (_todoWarnOnceCache.Add(warnOnceKey))
      {
	      __.GetLogger()._EzWarn($"TODO: {message}", sourceLineNumber: sourceLineNumber, memberName: memberName, sourceFilePath: sourceFilePath);
      }
      return;
#endif

		//__.GetLogger<Placeholder>()._EzWarn("TODO", message, memberName: memberName, sourceFilePath: sourceFilePath,
  //       sourceLineNumber: sourceLineNumber);
   }

   [DebuggerHidden]
   [DebuggerNonUserCode]
   public void Later(string message, [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      _ThrowIfRelease();
      //__.GetLogger<Placeholder>()._EzWarn("TODO LATER", message, memberName: memberName, sourceFilePath: sourceFilePath,sourceLineNumber: sourceLineNumber);
   }

   [DebuggerHidden]
   [DebuggerNonUserCode]
   [DoesNotReturn]
   [Conditional("RELEASE")]
   private void _ThrowIfRelease([CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      var toThrow = new LoLoDiagnosticsException("Placeholder code executed when RELEASE is defined");
      toThrow.Source = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";
      throw toThrow;
   }

   /// <summary>
   ///    break into the debugger to inspect this code
   /// </summary>
   /// <param name="message"></param>
   [DebuggerNonUserCode]
   [DebuggerHidden]
   public void Inspect(string? message = "__.Placeholder.Inspect()", [CallerMemberName] string memberName = "",
      [CallerFilePath] string sourceFilePath = "",
      [CallerLineNumber] int sourceLineNumber = 0)
   {
      __.Assert(message, memberName: memberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
   }
}