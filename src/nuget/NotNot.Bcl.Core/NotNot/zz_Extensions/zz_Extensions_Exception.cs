// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Blake3;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Helpers;
// using Newtonsoft.Json.Linq; // Removed - no longer needed
using Nito.AsyncEx.Synchronous;
using NotNot;
using NotNot._internal.Threading;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;
using NotNot.Data;
using NotNot.Diagnostics;

//using Xunit.Sdk;

//using CommunityToolkit.HighPerformance;
//using DotNext;

public static class zz_Extensions_Exception
{
	/// <summary>
	///   returns true if the exception is a routine control flow exception (like TaskCanceledException, OperationCanceledException)
	/// </summary>
	/// <param name="ex"></param>
	/// <returns></returns>
	public static bool _IsRoutineControlFlow(this Exception ex, params Span<Type> othersToIgnore)
	{
		switch (ex)
		{
			case System.Net.Sockets.SocketException:
			case TaskCanceledException:
			case OperationCanceledException:
				return true;
		}

		return false;
	}

	/// <summary>
	/// rethrow the exception using it's original stack trace.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="exception"></param>
	/// <returns>This function WILL NOT return.   pretends to return the exception if you want to use the keyword `throw` for control flow analysis purposes.</returns>
	[DoesNotReturn]
	public static T _Rethrow<T>(this T exception) where T : Exception
	{
		// capture now to preserve the current stack trace
		var captured = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
		captured.Throw();
		return exception;
	}
	/// <summary>
	/// certain runtimes like godot do weird shutdown logic.
	/// we might not want to throw exceptions when a shutdown is occuring
	/// <para>also does not rethrow if in RELEASE build</para>
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="exception"></param>
	public static void _RethrowUnlessAppShutdownOrRelease<T>(this T exception) where T : Exception
	{
#if RELEASE
      return;
#else
		if (NotNot.Internal.NotNotBclConfig.IsAppShutdownInProgress)
		{
			return;
		}
		exception._Rethrow();
#endif
	}



	/// <summary>
	/// search self and inner exceptions for the exception type.  if found, return true and set found to the exception.
	/// </summary>
	/// <typeparam name="TException"></typeparam>
	/// <param name="exception"></param>
	/// <param name="found"></param>
	/// <returns></returns>
	public static bool _Find<TException>(this Exception? exception, [NotNullWhen(true)] out TException? found) where TException : Exception
	{
		if (exception is null)
		{
			found = null;
			return false;
		}
		if (exception is TException ex)
		{
			found = ex;
			return true;
		}

		if (exception.InnerException._Find(out found))
		{
			return true;
		}

		if (exception is AggregateException ae)
		{
			foreach (var inner in ae.InnerExceptions)
			{
				if (inner._Find(out found))
				{
					return true;
				}
			}
		}
		found = null;
		return false;
	}

	public static string _ToUserFriendlyString(this Exception e)
	{
		if (e == null) return string.Empty;

		string innerErrorString = e.InnerException._ToUserFriendlyString();
		string message = e.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");

		return $$""" "{{e.GetType().Name}}": {"msg": "{{message}}","inner": {{{innerErrorString}}}""";
	}

	public static (string sourceMember, string sourceFilePath, int sourceLineNumber) _DecomposeSource(this Exception e)
	{

		// e = your Exception object
		var st = new StackTrace(e, true); // 'true' captures file info

		// Get the first stack frame with file info
		StackFrame frame = st.GetFrames()?.FirstOrDefault(f => f.GetFileLineNumber() > 0) ?? st.GetFrame(0);

		string sourceMember = frame?.GetMethod()?.Name ?? "Unknown";
		string sourceFilePath = frame?.GetFileName() ?? "Unknown";
		int sourceLineNum = frame?.GetFileLineNumber() ?? 0;

		return (sourceMember, sourceFilePath, sourceLineNum);
	}
}

