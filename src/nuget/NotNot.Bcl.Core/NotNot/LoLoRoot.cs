using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using NotNot._internal;
using NotNot._internal.Diagnostics;
using NotNot._internal.Threading;
using NotNot.Diagnostics.Advanced;
using NotNot.Validation;


namespace NotNot;

[DebuggerNonUserCode]
public partial class LoLoRoot
{
#pragma warning disable CS8618
	protected static LoLoRoot? _instance;
#pragma warning restore CS8618
	private ThreadLocal<Random> _random = new(() => new Random());

	/// <summary>
	///    Call the .Run() command to schedule tasks to execute on a <see cref="DebuggableTaskFactory" />
	///    This TaskFactory aids in debugging by running all scheduled tasks sequentially,  when running in #CHECKED or if
	///    <see cref="DebuggableTaskFactory" />.singleThreaded is set to True
	/// </summary>
	public DebuggableAsyncHelper Async;
	//	[ThreadStatic]
	//	private static Random? _rand;
	//	//private static ThreadLocal<Random> _rand2 = new(() => new());

	//	/// <summary>
	//	/// get a thread-local Random
	//	/// </summary>
	//	public static Random Rand
	//	{
	//		get
	//		{
	//			if (_rand == null)
	//			{
	//				_rand = new Random();
	//			}
	//			return _rand;
	//		}
	//	}

	/// <summary>
	/// convenience method to emit Trace logs.  This is useful for development/test.
	/// </summary>
	[Conditional("DEBUG"), Conditional("TRACE")]
	public void DevTrace(string? message = "", object? objToLog0 = null, object? objToLog1 = null,
		object? objToLog2 = null, [CallerMemberName] string sourceMemberName = "",
			  [CallerFilePath] string sourceFilePath = "",
					 [CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog0")]
		string? objToLog0Name = "null",
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null",
		[CallerArgumentExpression("objToLog2")]
		string? objToLog2Name = "null",
		Span<string> tags = default)
	{

		var _devTraceLogger = __.GetLogger(sourceFilePath);

		_devTraceLogger._EzTrace(message, objToLog0, objToLog1, objToLog2, sourceMemberName, sourceFilePath, sourceLineNumber, objToLog0Name, objToLog1Name, objToLog2Name, tags);

	}

	//private HashSet<string> _todoWarnOnceCache = new();
	/// <summary>
	/// shows log message (once per callsite), and will throw in RELEASE builds
	/// </summary>
	[Obsolete("use __.placeholder.ToDo() instead")]
	public void Todo(string message = "", [CallerLineNumber] int sourceLineNumber = 0, [CallerMemberName] string sourceMemberName = "", [CallerFilePath] string sourceFilePath = "")
	{
		__.placeholder.ToDo(message, sourceMemberName, sourceFilePath, sourceLineNumber);
		//#if DEBUG
		//		var warnOnceKey = $"{sourceFilePath}:{sourceLineNumber}";
		//		if (_todoWarnOnceCache.Add(warnOnceKey))
		//		{
		//			__.GetLogger()._EzWarn($"TODO: {message}", sourceLineNumber: sourceLineNumber, sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath);
		//		}
		//		return;
		//#endif
		//		throw __.Throw(message, sourceLineNumber: sourceLineNumber, sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath);
	}

	//public void Todo([CallerLineNumber] int sourceLineNumber = 0, [CallerMemberName] string sourceMemberName = "",
	//	[CallerFilePath] string sourceFilePath = "")
	//{
	//	Todo("Todo", sourceLineNumber: sourceLineNumber, sourceMemberName: sourceMemberName, sourceFilePath: sourceFilePath);
	//}

	/// <summary> 
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public bool AssertNotNull([NotNullWhen(true)] object? obj, string? message = null, [CallerArgumentExpression("obj")]
		string? objName = "null")
	{
		if (obj is null)
		{
			message ??= $"AssertNotNull failed.  {objName} is null";
			AssertIfNot(obj is not null, message, expectedConditionName: $"{objName} is not null");
			return false;
		}
		return true;
	}


	/// <summary>
	/// logs message and triggers a breakpoint.  (also Prompts to attach a debugger if not already attached)
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void AssertIfNot(bool? _expectedCondition, string? message = "", object? objToLog0 = null, object? objToLog1 = null,
		object? objToLog2 = null, [CallerMemberName] string sourceMemberName = "",
			  [CallerFilePath] string sourceFilePath = "",
					 [CallerLineNumber] int sourceLineNumber = 0, [CallerArgumentExpression("_expectedCondition")] string expectedConditionName = "",
		[CallerArgumentExpression("objToLog0")]
		string? objToLog0Name = "null",
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null",
		[CallerArgumentExpression("objToLog2")]
		string? objToLog2Name = "null",
		Span<string> tags = default)
	{
		var expectedCondition = _expectedCondition.GetValueOrDefault();

		if (expectedCondition is false)
		{
			var finalMessage = message._FormatAppendArgs(expectedConditionName, objToLog0Name: "expectedCondition")._FormatAppendArgs(objToLog0, objToLog1, objToLog2, objToLog0Name, objToLog1Name, objToLog2Name)._FormatAppendArgs(sourceMemberName, sourceFilePath, sourceLineNumber);

			if (tags.Length > 0)
			{
				finalMessage += $" tags:[{string.Join(", ", tags)}]";
			}

			Debug.Assert(false, finalMessage);
			_Debugger.LaunchOnce();

			if (__.Test.IsTestingActive)
			{

				__.placeholder.ToDo("setup test runner logger");
				//Xunit.Assert.Fail(finalMessage);
			}
		}
	}

	/// <summary>
	/// logs message and triggers a breakpoint.  (also Prompts to attach a debugger if not already attached)
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void Assert(string? message = null, object? objToLog0 = null, object? objToLog1 = null,
		object? objToLog2 = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("objToLog0")]
		string? objToLog0Name = "null",
		[CallerArgumentExpression("objToLog1")]
		string? objToLog1Name = "null",
		[CallerArgumentExpression("objToLog2")]
		string? objToLog2Name = "null",
		Span<string> tags = default)
	{
		AssertIfNot(false, message, objToLog0, objToLog1, objToLog2, sourceMemberName, sourceFilePath, sourceLineNumber, objToLog0Name, objToLog1Name, objToLog2Name, tags: tags);
	}


	/// <summary>
	/// logs message and triggers a breakpoint.  (also Prompts to attach a debugger if not already attached)
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void Assert(Exception ex, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		var finalMessage = ex.Message._FormatAppendArgs(sourceMemberName, sourceFilePath, sourceLineNumber);
		Debug.Assert(false, finalMessage, ex._ToUserFriendlyString());
		_Debugger.LaunchOnce();

		if (__.Test.IsTestingActive)
		{
			__.placeholder.ToDo("setup test runner logger");
			//Xunit.Assert.Fail(finalMessage);
		}
	}

	private ConcurrentDictionary<string, int> _assertOnceKeys = new();

	private bool _tryAssertOnce(string? message, string sourceFilePath, int sourceLineNumber)
	{
		var key = $"{sourceFilePath}:{sourceLineNumber}:{message}";

		if (_assertOnceKeys.TryAdd(key, 1))
		{
			return true;
		}
		return false;

	}
	/// <summary>
	/// if the expectedCondition is false, assert once per callsite+message.
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void AssertOnceIfNot(bool expectedCondition, string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (expectedCondition is true)
		{
			return;
		}
		if (!_tryAssertOnce(message, sourceFilePath, sourceLineNumber))
		{
			return;
		}
		AssertIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber);

	}

	/// <summary>
	/// if the expectedCondition is false, assert once per callsite+message.
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void AssertOnce(string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (!_tryAssertOnce(message, sourceFilePath, sourceLineNumber))
		{
			return;
		}
		AssertIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber);
	}


	/// <summary>
	/// if the expectedCondition is false, assert once per callsite+message.
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void AssertOnce(Exception ex, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (!_tryAssertOnce(ex.GetType().Name, sourceFilePath, sourceLineNumber))
		{
			return;
		}
		Assert(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
	}

	[Conditional("DEBUG")]
	public void DebugExec(Action action)
	{
		action();
	}
	[Conditional("DEBUG")]
	public async void DebugExec(Func<Task> func)
	{
		await func();
	}


	/// <summary>
	/// assert, but only in DEBUG builds
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	[Conditional("DEBUG")]
	public void DebugAssertIfNot(bool expectedCondition, string? message = null, [CallerMemberName] string sourceMemberName = "",
			  [CallerFilePath] string sourceFilePath = "",
					 [CallerLineNumber] int sourceLineNumber = 0)
	{
		AssertIfNot(expectedCondition, message, sourceMemberName, sourceFilePath, sourceLineNumber);
	}

	/// <summary>
	/// assert, but only in DEBUG builds
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	[Conditional("DEBUG")]
	public void DebugAssert(string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		AssertIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber);
	}


	/// <summary>
	/// assert, but only in DEBUG builds
	/// <para>IMPORTANT NOTE: execution will resume normally after an Assert</para>
	/// </summary>
	public void DebugAssert(Exception ex, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Assert(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
	}



	[Conditional("DEBUG")]
	public void DebugAssertOnceIfNot(bool expectedCondition, string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (expectedCondition is true)
		{
			return;
		}
		if (!_tryAssertOnce(message, sourceFilePath, sourceLineNumber))
		{
			return;
		}
		AssertIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber);

	}

	[Conditional("DEBUG")]
	public void DebugAssertOnce(string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (!_tryAssertOnce(message, sourceFilePath, sourceLineNumber))
		{
			return;
		}
		AssertIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber);
	}


	[Conditional("DEBUG")]
	public void DebugAssertOnce(Exception ex, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		if (!_tryAssertOnce(ex.GetType().Name, sourceFilePath, sourceLineNumber))
		{
			return;
		}
		Assert(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
	}

	/// <summary>
	/// throw an Exception if expectedCondition is false
	/// </summary>
	/// <param name="expectedCondition"></param>
	/// <exception cref="NotImplementedException"></exception>
	[DoesNotReturn]
	public Exception Throw(string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		ThrowIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber);
		//never gets here because of throw
		return null;
	}
	/// <summary>
	/// throw an Exception if expectedCondition is false
	/// </summary>
	/// <param name="expectedCondition"></param>
	/// <exception cref="NotImplementedException"></exception>
	public void ThrowIfNot([DoesNotReturnIf(false)] bool? _expectedCondition, string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0, [CallerArgumentExpression("_expectedCondition")] string expectedConditionName = "")
	{
		var expectedCondition = _expectedCondition.GetValueOrDefault();
		if (expectedCondition)
		{
			return;
		}
		AssertIfNot(false, message, sourceMemberName, sourceFilePath, sourceLineNumber, expectedConditionName);
		var ex = new LoLoDiagnosticsException(message._FormatAppendArgs(expectedConditionName, objToLog0Name: "expectedCondition"), sourceMemberName, sourceFilePath, sourceLineNumber);
		ExceptionDispatchInfo.Capture(ex).Throw(); //throw the original exception, preserving the stack trace
	}
	/// <summary>
	/// throw an Exception if expectedCondition is false
	/// </summary>
	/// <param name="expectedCondition"></param>
	/// <exception cref="NotImplementedException"></exception>
	public void Throw([DoesNotReturnIf(false)] bool? _expectedCondition, string? message = null, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0, [CallerArgumentExpression("_expectedCondition")] string expectedConditionName = "")
	{
		ThrowIfNot(_expectedCondition, message, memberName, sourceFilePath, sourceLineNumber, expectedConditionName);
	}
	/// <summary>
	/// Ensures value is not null, also returning it.  If null, throws an exception.
	/// </summary>
	[return: NotNull]
	public T NotNull<T>(
		[NotNull]
		T? value, string? message = null, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0, [CallerArgumentExpression("value")] string valueName = "")
	where T : class
	{
		ThrowIfNot(value is not null, message, sourceMemberName, sourceFilePath, sourceLineNumber, $"{valueName} is not null");
		return value!;
	}

	/// <summary>
	/// Log, assert, throw throw an Exception.
	/// </summary>
	/// <param name="expectedCondition"></param>
	[DoesNotReturn]
	public Exception Throw(Exception ex, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Assert(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
		ExceptionDispatchInfo.Capture(ex).Throw(); //throw the original exception, preserving the stack trace
																 //never gets here because of throw
		return null;
	}

	[DoesNotReturn]
	public Exception ThrowInner(Exception ex, string message, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		Assert(message + $" InnerException: {ex.Message}", sourceMemberName, sourceFilePath, sourceLineNumber);
		throw new LoLoDiagnosticsException(message, ex, sourceMemberName, sourceFilePath, sourceLineNumber);
	}


	///// <summary>
	/////    Logging/Assert functionality ONLY enabled in CHECKED builds
	///// </summary>
	//[Obsolete("use __.GetLogger() instead", true)]
	//public CheckedDiag CHECKED;

	///// <summary>
	/////    Logging/Assert functionality ONLY enabled in CHECKED or DEBUG builds
	///// </summary>
	//[Obsolete("use __.GetLogger() instead", true)]
	//public DebugDiag DEBUG;

	///// <summary>
	/////    used by GetLogger() to truncate sourceFilePath verbosity
	///// </summary>
	//[Obsolete("use __.GetLogger() instead", true)]
	//internal string entrypointFilePath = null!;

	///// <summary>
	/////    Logging/Assert functionality enabled in ALL builds
	///// </summary>
	//[Obsolete("use __.GetLogger() instead", true)]
	//public ErrorDiag ERROR;





	public Placeholder placeholder = new();

	public StaticPool pool = new();


	private string? _runtimeEnv;
	/// <summary>
	/// the current runtime $ENVIRONMENT, usually "Development", "Test", "Production"
	/// <para>IMPORTANT: where is this set from? check launchSettings.json, ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT envvars</para>
	/// <para>returns "Production" if nothing set</para>
	/// <para>can be set ONCE, only if not set by anything else.  such as in your Program.cs: ` __.RuntimeEnv = builder.Environment.EnvironmentName;` </para>
	/// </summary>
	public string RuntimeEnv
	{
		get
		{
			if (_runtimeEnv is not null)
			{
				return _runtimeEnv;
			}


			_runtimeEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
			return _runtimeEnv;
		}
		set
		{
			if (_runtimeEnv is not null && _runtimeEnv != value)
			{
				throw new LoLoException("RuntimeEnv is already set, cannot set it again.  Use __.RuntimeEnv = \"Development\"; only once, at startup, otherwise let it be set by ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT envvars");
			}
			_runtimeEnv = value;
		}
	}
	/// <summary>
	/// returns true if we are running in production environment.  false otherwise
	/// <para>Note: production==true when no ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT is specified</para>
	/// </summary>
	public bool IsProduction
	{
		get
		{
			return RuntimeEnv._AproxEqual("production");
		}
	}

	public bool IsTest
	{
		get
		{
			return RuntimeEnv._AproxEqual("test");
		}
	}


	public bool IsDevelopment
	{
		get
		{
			return RuntimeEnv._AproxEqual("development");
		}
	}
	///// <summary>
	///// shortcut to return the DI IHostEnvironment.  This should be the source of truth for determining runtime environment, not looking up envvars
	///// </summary>
	//public IHostEnvironment HostEnvironment
	//{
	//	get
	//	{
	//		return Services.GetRequiredService<IHostEnvironment>();
	//	}
	//}


	private TestHelper _test = new();

	public TestHelper Test
	{
		get
		{
			//disabling because yes soemtimes we would want to test in production.
			//if (__.IsProduction)
			//{
			//	throw new LoLoDiagnosticsException("TestHelper should only be accessed when not in production.  why is this not true?");
			//}
			return _test;
		}
	}

	/// <summary>
	/// obtain timestamp for debug printing, format: "hh:mm:ss.fff"
	/// </summary>
	public string Timestamp => DateTime.UtcNow.ToLocalTime().ToString("hh:mm:ss.fff");



	//public Validate Validate = new();

	public Normalize Normalize = new();

	public LoLoRoot(LoLoConfig? config = null, IServiceProvider services = null)
	{

		Config = config ?? new LoLoConfig();
		//_services = services;
		if (services is not null)
		{
			Initialize(services);
		}

		Async = new DebuggableAsyncHelper(Config.IsDebuggableTaskFactorySingleThreaded);
	}

#pragma warning disable IDE1006
	public static LoLoRoot __
#pragma warning restore IDE1006
	{
		get
		{
			if (_instance is null)
			{
				//throw new NullReferenceException("lolo.__ is not set, call lolo.__ = new lolo() first, in your program.cs");
				_instance = new LoLoRoot();
			}

			return _instance;
		}
		//set
		//{
		//	if (_instance != null)
		//	{
		//		//throw new Exception("lolo.__ is already set");
		//		//return;
		//	}

		//	_instance = value;
		//}
	}


	public Random Random => _random.Value!;

	public LoLoConfig Config { get; private set; }


	//private IServiceProvider _services;
	//public IServiceProvider? Services
	//{
	//	get
	//	{
	//		if (_services is null)
	//		{
	//			throw new NullReferenceException("lolo.Services is not set.  You must call '__.Services=app.Services;' immediately after calling 'app = builder.Build();'");
	//		}
	//		return _services;
	//	}
	//	set
	//	{
	//		if (_services is not null)
	//		{
	//			throw new LoLoException("lolo.Services is already set");
	//		}

	//		_services = value;
	//	}
	//}



	/// <summary>
	/// helper to dispose static variables, used by test runners.  probably not needed otherwise.
	/// </summary>
	public void Dispose()
	{
		_loggerFactory.Dispose();
		LoLoRoot._loggerFactory = null;
		LoLoRoot._instance = null;
	}

}

/// <summary>
///    simple console format used before serilog is loaded (or if no DI exists)
/// </summary>
public class FallbackConsoleFormatter : ConsoleFormatter
{
	public FallbackConsoleFormatter() : base("fallback")
	{
	}

	public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
	{
		lock (this)
		{
			var originalColor = Console.ForegroundColor;
			try
			{
				Console.ForegroundColor = GetColor(logEntry.LogLevel);

				var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
				var logLevel = logEntry.LogLevel.ToString().ToUpperInvariant();
				var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

				textWriter.Write($"[{timestamp} {logLevel}] {message}  <NN_NoInit_Fallback>");

				if (logEntry.Exception != null)
				{
					textWriter.Write($" {Environment.NewLine}{logEntry.Exception}");
				}

				textWriter.WriteLine();


				//textWriter.Flush();
			}
			finally
			{
				Console.ForegroundColor = originalColor;
			}
		}
	}

	private ConsoleColor GetColor(LogLevel logLevel)
	{
		return logLevel switch
		{
			LogLevel.Trace => ConsoleColor.Gray,
			LogLevel.Debug => ConsoleColor.Blue,
			LogLevel.Information => ConsoleColor.Green,
			LogLevel.Warning => ConsoleColor.Yellow,
			LogLevel.Error => ConsoleColor.Red,
			LogLevel.Critical => ConsoleColor.Magenta,
			_ => ConsoleColor.White,
		};
	}
}

public partial class LoLoRoot
{
	/// <summary>
	///    logger used when DI Services are not set.  (see .GetLogger() logic below)
	/// </summary>
	internal ILogger? _DiMissingConsoleLoggerFallback;

	private bool _hasWarnedServicesMissing;

	private ConcurrentDictionary<string, object> _knownLoggerTypes = new();

	/// <summary>
	/// Obtain a logger for the current file.
	/// <para>This overload will cache the first call per-file and reuse the logger on all other calls from the same file.</para>
	/// </summary>
	/// <param name="callerFilePath_for_reflection_optimization_do_not_use"></param>
	/// <returns></returns>
	/// <exception cref="Exception"></exception>
	public ILogger GetLogger([CallerFilePath] string callerFilePath_for_reflection_optimization_do_not_use = "")
	{
		//if (_services is null)
		//{
		//	return _GetFallbackLogger();
		//}

		//try
		{
			var typeOrName = _knownLoggerTypes.GetOrAdd(callerFilePath_for_reflection_optimization_do_not_use, (key) =>
			{
				var fileInfo = new FileInfo(key);
				var toReturn = fileInfo.Name + fileInfo.Extension;
				//var toReturn = key._GetAfter(Path.DirectorySeparatorChar);
				__.AssertIfNot(toReturn is not null);
				return toReturn;
				////////////  the following is brittle/doesn't work right so just returning the file name.
				//__.Assert("test");
				//var trace = new StackTrace(3, false);
				//var categoryType = trace.GetFrame(0)?.GetMethod()?.DeclaringType?.DeclaringType;
				//if (categoryType is null)
				//{
				//	var targetAssembly = Assembly.GetCallingAssembly();
				//	var loggerName = $"{targetAssembly.GetName().Name ?? "UNNAMED_ASSEMBLY"}_Logger";

				//	return loggerName;
				//	//var toReturn = Services!.GetRequiredService<ILoggerFactory>().CreateLogger(loggerName);           
				//	//var loggerType = typeof(ILogger<>).MakeGenericType(targetAssembly);
				//}
				//return categoryType;
			});
			switch (typeOrName)
			{
				case Type categoryType:
					return _loggerFactory.CreateLogger(categoryType);
				//return GetLogger(type);
				case string categoryName:
					return _loggerFactory.CreateLogger(categoryName);
				default:
					throw new Exception("unexpected");
			}
		}
		//catch (Exception ex)
		//{
		//	//if we can't get a logger, just return the fallback logger
		//	//__.Assert(ex);
		//	_GetFallbackLogger()._EzError(ex, "failed to get logger: ");
		//	return _GetFallbackLogger();
		//}
	}

	public ILogger GetLogger(object categoryFromType)
	{

		if (categoryFromType is Type categoryType)
		{//in case wrong overload called
			return GetLogger(categoryType);
		}

		var categoryTyepe = categoryFromType.GetType();
		return GetLogger(categoryTyepe);
	}

	public ILogger GetLogger(Type categoryType)
	{
		//if (_services is null)
		//{
		//	return _GetFallbackLogger();
		//}

		//return (ILogger)Services!.GetRequiredService(loggerType);
		return _loggerFactory.CreateLogger(categoryType);


	}

	/// <summary>
	/// logger factory.  defaults to a simple console logger factory until DI services have registered.
	/// </summary>
	private static ILoggerFactory _loggerFactory = LoggerFactory.Create(builder =>
	{
		builder.ClearProviders();
		builder.SetMinimumLevel(LogLevel.Trace);

		builder.AddConsole(options => { options.FormatterName = "fallback"; }).AddConsoleFormatter<FallbackConsoleFormatter, ConsoleFormatterOptions>();
	});

	public void Initialize(IServiceProvider serviceProvider)
	{
		//_loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
	}

	//private ILogger _GetFallbackLogger()
	//{
	//	if (_DiMissingConsoleLoggerFallback is null)
	//	{
	//		_DiMissingConsoleLoggerFallback = LoggerFactory.Create(logging =>
	//		{
	//			logging.ClearProviders();
	//			logging.SetMinimumLevel(LogLevel.Trace);

	//			logging.AddConsole(options => { options.FormatterName = "fallback"; }).AddConsoleFormatter<FallbackConsoleFormatter, ConsoleFormatterOptions>();
	//		}).CreateLogger("NoDIServices_Fallback");
	//	}

	//	if (_hasWarnedServicesMissing is false)
	//	{
	//		_hasWarnedServicesMissing = true;
	//		_DiMissingConsoleLoggerFallback._EzWarn(
	//			"NoDIServices_Fallback IS BEING USED.  __.Services is null. It must be set to log properly (do so as soon as DI Services are available).  a simple console-only logger will be used until DI Services are available");
	//		//Serilog.Log.Logger.Warning("__.Services is null. It must be set to log properly (do so as soon as DI Services are available).  a simple console-only logger will be used until DI Services are available");
	//	}

	//	return _DiMissingConsoleLoggerFallback;
	//}


	///// <summary>
	/////    shortcut to create a new logger
	///// </summary>
	//[Obsolete("use GetLogger() with either object or type instead", true)]
	//public ILogger GetLogger(string? name = null, [CallerFilePath] string sourceFilePath = "")
	//{
	//   throw new NotImplementedException("obsolete");
	//   //if (name._IsNullOrWhiteSpace())
	//   //{
	//   //	//set name to a friendly truncation of sourceFilePath
	//   //	name = sourceFilePath._GetUniqueSuffix(entrypointFilePath);
	//   //}

	//   //if (Services is null)
	//   //{
	//   //	if (_DiMissingConsoleLoggerFallback is null)
	//   //	{
	//   //		_DiMissingConsoleLoggerFallback = LoggerFactory.Create(logging =>
	//   //		{
	//   //			logging.ClearProviders();
	//   //			logging.SetMinimumLevel(LogLevel.Trace);

	//   //             logging.AddConsole(options => { options.FormatterName = "fallback"; }).AddConsoleFormatter<FallbackConsoleFormatter, ConsoleFormatterOptions>();
	//   //          }).CreateLogger("NoDIServices_Fallback");
	//   //	}

	//   //	if (_hasWarnedServicesMissing is false)
	//   //	{
	//   //		_hasWarnedServicesMissing = true;
	//   //		_DiMissingConsoleLoggerFallback._EzWarn(
	//   //			"NoDIServices_Fallback IS BEING USED.  __.Services is null. It must be set to log properly (do so as soon as DI Services are available).  a simple console-only logger will be used until DI Services are available");
	//   //		//Serilog.Log.Logger.Warning("__.Services is null. It must be set to log properly (do so as soon as DI Services are available).  a simple console-only logger will be used until DI Services are available");
	//   //	}

	//   //	return _DiMissingConsoleLoggerFallback;
	//   //}


	//   //return Services._GetService<ILoggerFactory>()!.CreateLogger(name);
	//}

	/// <summary>
	///    shortcut to create a new logger
	/// </summary>
	public ILogger GetLogger<TCategory>()
	{
		//if (_services is null)
		//{
		//	return _GetFallbackLogger();
		//}
		//return GetLogger(typeof(TCategory));
		return _loggerFactory.CreateLogger<TCategory>();
	}


	//public AsyncLazy<ILogger> GetLoggerAsync(string? name = null, [CallerFilePath] string sourceFilePath = "")
	//{
	//	var toReturn = new AsyncLazy<ILogger>(async () =>
	//	{
	//		return GetLogger(name, sourceFilePath);
	//	});

	//	return toReturn;

	//	//if (this.IsInitialized)
	//	//{
	//	//	var logger = GetLogger(name);
	//	//	var toReturn = new AsyncLazy<ILogger>(logger);
	//	//	return toReturn;
	//	//}
	//	//else
	//	//{
	//	//	var toReturn = new AsyncLazy<ILogger>(async () =>
	//	//	{
	//	//		await _isInitializedTcs.Task;
	//	//		return GetLogger(name);
	//	//	});
	//	//	return toReturn;
	//	//}
	//}

	public AsyncLazy<ILogger> GetLoggerLazy<TCategory>()
	{
		return new AsyncLazy<ILogger>(async () =>
		{
			return GetLogger<TCategory>();
		});

		//if (this.IsInitialized)
		//{
		//	return new AsyncLazy<ILogger>(GetLogger<ILoggerFactory>());
		//}
		//else
		//{
		//	return new AsyncLazy<ILogger>(async () =>
		//	{
		//		await _isInitializedTcs.Task;
		//		return GetLogger<ILoggerFactory>();
		//	});
		//}
	}
}

//public class _Tests
//{

//	public _Tests()
//	{
//		//if (__ == null)
//		//{
//		//	__ = new LoLoRoot();
//		//}
//	}

//	[Fact]
//	public void Logger_Basic()
//	{
//		var AX = "VX";
//		var AY = "VY";



//		__.GetLogger(this)._EzInfo("warmup");
//		__.GetLogger<_Tests>()._EzInfo("T", (AX: "VX", AY: "VY"));
//		__.GetLogger()._EzInfo("test", AX, "AA", 123);
//		__.GetLogger(this)._EzInfo("T", new { AX, AY });
//		__.GetLogger(this)._EzInfo("T", (AX: "VX", AY: "VY"));
//		__.GetLogger(this)._EzInfo("T", (AX, AY));
//	}


//	[Fact]
//	public void Logger_RefEquals()
//	{
//		var logger = __.GetLogger();

//		Assert.True(ReferenceEquals(logger, logger));
//		Assert.True(ReferenceEquals(logger, __.GetLogger()));
//		Assert.True(ReferenceEquals(logger, __.GetLogger(this)));
//		Assert.True(ReferenceEquals(logger, __.GetLogger<_Tests>()));
//	}
//}
