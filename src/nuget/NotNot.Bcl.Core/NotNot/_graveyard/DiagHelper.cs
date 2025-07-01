// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NotNot._graveyard;

/// <summary>
///    Meant for internal logging API use only.  The actual implementation of the various diagnostic helpers.
///    Use <see cref="DebugDiag" /> and related classes for public API.
/// </summary>
[DebuggerNonUserCode]
[DebuggerStepThrough]
[Obsolete("use __.GetLogger() instead", true)]
public class DiagHelper
{
	/// <summary>
	///    value passed to DiagHelper methods, just adds some clasification detail to log output.
	///    Does not modify funcationality at this point in execution.
	/// </summary>
	public enum EnvLevel
	{
		None,
		CHECKED,
		DEBUG,
		ERROR,
	}


	private HashSet<string> _assertOnceLookup = new();

	/// <summary>
	///    cache of loggers, one per name
	/// </summary>
	private ConcurrentDictionary<string, ILogger> _loggerCache = new();

	/// <summary>
	///    internal helper to obtain a specific logger instance per name.
	/// </summary>
	protected ILogger _GetLogger(string? name = null, [CallerFilePath] string sourceFilePath = "")
	{

		return __.GetLogger();
		//refactor, don't need below:

		//var loggerName = name._IsNullOrWhiteSpace() ? sourceFilePath : name;
		////return our cached logger, unless it's the DI fallback logger, in which case we need to try again.
		//var toReturn = _loggerCache.GetOrAdd(loggerName, _name => __.GetLogger(name, sourceFilePath));
		//if (__.Services is not null && toReturn == __._DiMissingConsoleLoggerFallback)
		//{
		//	//remove fallback logger and try try again
		//	_loggerCache.Remove(name, out _);
		//	return _GetLogger(name, sourceFilePath);
		//}

		//return toReturn;
	}

	/// <summary>
	///    asset (break into debugger) and log a message
	/// </summary>
	[DebuggerHidden]
	public void Assert(EnvLevel level, bool condition, string message = null, object? objToLog1 = null,
		object? objToLog2 = null, object? objToLog3 = null,
		[CallerArgumentExpression("condition")]
		string? conditionName = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null", [CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
		[CallerArgumentExpression("objToLog3")]
		string? objToLog3Name = "null")
	{
		if (condition)
		{
			return;
		}

		message ??= "Assert condition failed";

		//Debug.Assert(false, (string)$"ASSERT({conditionName}) {message}");
		//DoAssertFail($"ASSERT({conditionName}) {message}");
		DoAssertFail(level, "ASSERT", message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
			sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
	}


	/// <summary>
	///    assert for the given message ONLY ONCE at the callsite.  future calls with identical message are ignored.
	///    different ojbToLog values are not considered when determining uniqueness.
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	public void AssertOnce(EnvLevel level, bool condition, string message = null, object? objToLog1 = null,
		object? objToLog2 = null, object? objToLog3 = null,
		[CallerArgumentExpression("condition")]
		string? conditionName = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null", [CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
		[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null")
	{
		if (condition)
		{
			return;
		}

		//message ??= "Assert condition failed";
		var assertKey = $"{sourceFilePath}:{sourceLineNumber}:{message}";
		lock (_assertOnceLookup)
		{
			if (_assertOnceLookup.Add(assertKey) == false)
			{
				return;
			}
		}

		//Debug.Assert(false, "ASSERT ONCE: " + message);
		//Debug.Assert(false,(string)$"ASSERT_ONCE({conditionName}) {message}");
		//DoAssertFail($"ASSERT_ONCE({conditionName}) {message}");
		DoAssertFail(level, "ASSERT_ONCE", message, objToLog1, objToLog2, objToLog3, conditionName, memberName,
			sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
	}

	/// <summary>
	///    Something failed that shouldn't, but up to the caller to handle (this throws an exception)
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	[DoesNotReturn]
	public void Throw<TException>(EnvLevel level, bool condition, string message = null,
		Exception? innerException = null,
		[CallerArgumentExpression("condition")]
		string? conditionName = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) where TException : Exception
	{
		if (condition)
		{
			return;
		}

		message ??= "Throw condition failed";


		//Assert(false, message, conditionName, memberName, sourceFilePath,sourceLineNumber);
		//throw new(message);

		TException ex = null;
		try
		{
			ex = typeof(TException)._CreateInstance<TException>($"{level}_THROW({conditionName}) {message}",
				innerException);
		}
		catch (Exception e)
		{
			Kill(EnvLevel.ERROR, false
				, $"Failed to create exception of type {typeof(TException).FullName}.  probably because it doesn not have a public construtor that takes message+innerException.",
				e
				, conditionName, memberName, sourceFilePath, sourceLineNumber
			);
		}

		ex.Source = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";


		_BreakIntoDebugger();


		throw ex;
	}

	/// <summary>
	///    something super bad happened. log it and kill the process
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	[DoesNotReturn]
	public void Kill(EnvLevel level, bool condition, string message = null, Exception? innerException = null,
		[CallerArgumentExpression("condition")]
		string? conditionName = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (condition)
		{
			return;
		}

		message ??= "KILL condition failed";

		//Assert(false, message, conditionName, memberName, sourceFilePath,sourceLineNumber);
		//throw new(message);
		var ex = new Exception($"{level}_KILL({conditionName}) {message}", innerException);
		ex.Source = $"{memberName}:{sourceFilePath}:{sourceLineNumber}";

		DoLog(level, nameof(Kill), ex.Message, ex, null, null, conditionName, memberName, sourceFilePath,
			sourceLineNumber);

		_BreakIntoDebugger();

		Environment.FailFast(ex.Message, ex);
	}

	/// <summary>
	///    helper to break into the debugger
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	[DoesNotReturn]
	private void _BreakIntoDebugger()
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

	/// <summary>
	///    log according to input level
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	public void Log(EnvLevel level, string message, object? objToLog1 = null, object? objToLog2 = null,
		object? objToLog3 = null,
		//[CallerArgumentExpression("condition")]
		string? conditionName = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null", [CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
		[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null")
	{
		//Console.WriteLine(message);
		DoLog(level, nameof(Log), message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
			sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
	}

	/// <summary>
	///    log according to input level
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	public void Log(EnvLevel level, bool condition, string message = null, object? objToLog1 = null,
		object? objToLog2 = null, object? objToLog3 = null,
		[CallerArgumentExpression("condition")]
		string? conditionName = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null", [CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
		[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null")
	{
		if (condition)
		{
			return;
		}

		DoLog(level, nameof(Log), message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
			sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);
	}

	/// <summary>
	///    does the actual logging to serilog, according to input level
	/// </summary>
	//[DebuggerNonUserCode, DebuggerHidden]
	private void DoLog(EnvLevel level, string eventName, string message, object? objToLog1 = null,
		object? objToLog2 = null, object? objToLog3 = null, string? conditionName = null,
		string memberName = "", string sourceFilePath = "", int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null", [CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
		[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null")
	{
		//get cached logger instance
		//var loggerName = sourceFilePath;//._GetAfter(System.IO.Path.DirectorySeparatorChar, true);
		var logger = _GetLogger(name: null, sourceFilePath: sourceFilePath);
		//log to ILogger
		logger._EzWarn($"{level}_{eventName}({conditionName}) {message}", objToLog1, objToLog2, objToLog3, memberName,
			sourceFilePath, sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);


		////pretty color printout to console using the ANSI.Console nuget package.   
		//var timeFormat = DateTime.Now.ToString("HH:mm:ss.ffff").Color(ConsoleColor.Gray).Bold();
		//      var eventFormat = $"{eventName}".Color(ConsoleColor.DarkBlue).Bold().Background(ConsoleColor.White);
		//      var conditionFormat = $"{conditionName}".Color(ConsoleColor.Red).Bold().Background(ConsoleColor.Black);
		//      var callsiteFormat = $"{sourceFilePath._GetAfter('\\', true)}:{sourceLineNumber}({memberName})"
		//          .Color(ConsoleColor.Magenta).Background(ConsoleColor.Black).Bold();
		//      var messageFormat = message.Color(ConsoleColor.White).Bold().Background(ConsoleColor.Black);
		//      Console.WriteLine($"{timeFormat}-{callsiteFormat}[{eventFormat}({conditionFormat})]{messageFormat}");

		// If a different ansi color package is needed, can try Spectre.Console.  https://spectreconsole.net/
		//new Spectre.Console.Markup("test",
		//	Spectre.Console.Style.WithForeground(Spectre.Console.Color.Grey)
		//		.Combine(Spectre.Console.Style.WithDecoration(Spectre.Console.Decoration.Bold)));

		//Spectre.Console.AnsiConsole.
	}

	/// <summary>
	///    log according to input level then tries to break into debugger if attached.
	/// </summary>
	[DebuggerNonUserCode]
	[DebuggerHidden]
	[DoesNotReturn]
	private void DoAssertFail(EnvLevel level, string eventName, string message, object? objToLog1 = null,
		object? objToLog2 = null, object? objToLog3 = null, string? conditionName = null,
		string memberName = "", string sourceFilePath = "", int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null", [CallerArgumentExpression("objToLog2")] string? objToLog2Name = "null",
		[CallerArgumentExpression("objToLog3")] string? objToLog3Name = "null")
	{
		//log that an assert occurs to the console
		DoLog(level, eventName, message, objToLog1, objToLog2, objToLog3, conditionName, memberName, sourceFilePath,
			sourceLineNumber, objToLog1Name, objToLog2Name, objToLog3Name);

		//break into debugger
		if (Debugger.IsAttached == false)
		{
			Debugger.Launch();
		}

		if (Debugger.IsAttached)
		{
			Debugger.Break();
		}

		//// crash the app
		//var ex = new Exception(message);
		//Environment.FailFast(ex.Message, ex);
	}
}
