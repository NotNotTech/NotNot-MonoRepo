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

public static class zz_Extensions_CancellationTokenSource
{
	/// <summary>
	/// cancels after a delay, but will pause the timeout from counting-down while the application is paused in a debugger.
	/// </summary>
	/// <param name="cts"></param>
	/// <param name="delay"></param>
	public static void _CancelAfterDebuggable(this CancellationTokenSource cts, TimeSpan delay)
	{
		DebuggableTimeoutCancelTokenHelper.CancelAfter(cts, delay);
	}
}

public static partial class zz_Extensions_Task
{
	/// <summary>
	///    make a nullable task awaitable
	/// </summary>
	public static ValueTask _GetAwaiter(this ValueTask? task)
	{
		return task ?? ValueTask.CompletedTask;
	}

	/// <summary>
	///    make a nullable task awaitable
	/// </summary>
	public static ValueTask<TResult> _GetAwaiter<TResult>(this ValueTask<TResult>? task, TResult defaultValue)
	{
		return task ?? ValueTask.FromResult(defaultValue);
	}

	/// <summary>
	///    make a nullable task awaitable
	/// </summary>
	public static Task _GetAwaiter(this Task? task)
	{
		return task ?? Task.CompletedTask;
	}

	/// <summary>
	///    make a nullable task awaitable
	/// </summary>
	public static Task<TResult> _GetAwaiter<TResult>(this Task<TResult>? task, TResult defaultValue)
	{
		return task ?? Task.FromResult(defaultValue);
	}


	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed.  Inspect the original task for result status.
	/// </summary>
	[Obsolete("don't eat errors silently.  use task._ToMaybe() instead, or propagate the exception.", true)]
	public static async Task _WaitNoExceptions(this Task task)
	{
		if (task is null)
		{
			return;
		}
		try
		{
			await task;
		}
#pragma warning disable ERP022, RCS1075, NN_R005, NN_R006 // Method designed to swallow all exceptions
		catch (Exception)
		{
		}
#pragma warning restore ERP022, RCS1075, NN_R005, NN_R006
	}

	[Obsolete("don't eat errors silently.  use task._ToMaybe() instead, or propagate the exception.", true)]
	public static async Task<T?> _WaitNoExceptions<T>(this Task<T> task)
	{
		try
		{
			return await task;
		}
#pragma warning disable ERP022, RCS1075, NN_R005, NN_R006 // Method designed to swallow all exceptions
		catch (Exception)
		{
			return default;
		}
#pragma warning restore ERP022, RCS1075, NN_R005, NN_R006
	}

	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed.   Inspect the original task for result status.
	/// </summary>
	[Obsolete("don't eat errors silently.  use task._ToMaybe() instead, or propagate the exception.", true)]
	public static async ValueTask _WaitNoExceptions(this ValueTask task)
	{
		try
		{
			await task;
		}
#pragma warning disable ERP022, RCS1075, NN_R005, NN_R006 // Method designed to swallow all exceptions
		catch (Exception)
		{
		}
#pragma warning restore ERP022, RCS1075, NN_R005, NN_R006
	}




	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed.   Inspect the original task for result status.
	/// </summary>
	[Obsolete("don't eat errors silently.  use task._ToMaybe() instead, or propagate the exception.", true)]
	public static async ValueTask<TResult?> _WaitNoExceptions<TResult>(this ValueTask<TResult> task)
	{
		try
		{
			return await task;
		}
#pragma warning disable ERP022, RCS1075, NN_R005, NN_R006 // Method designed to swallow all exceptions
		catch (Exception)
		{
			return default;
		}
#pragma warning restore ERP022, RCS1075, NN_R005, NN_R006
	}



	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[DebuggerHidden, DebuggerNonUserCode]
	[Obsolete("use `task._WaitIgnoreCancel()` instead.", true)]
	public static async Task _WaitNoCancelExceptions(this Task task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
			return;
		}
	}


	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[DebuggerHidden, DebuggerNonUserCode]
	[Obsolete("don't eat errors silently.  use task._ToMaybe() instead, or propagate the exception.", true)]
	public static async Task<T?> _WaitNoCancelExceptions<T>(this Task<T> task)
	{
		try
		{
			return await task;
		}
		catch (OperationCanceledException)
		{
			return default;
		}
	}

	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[Obsolete("use `task._WaitIgnoreCancel()` instead.", true)]
	[DebuggerHidden, DebuggerNonUserCode]
	public static async ValueTask _WaitNoCancelExceptions(this ValueTask task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
			return;
		}
	}


	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[Obsolete("don't eat errors silently.  use task._ToMaybe() instead, or propagate the exception.", true)]
	[DebuggerHidden, DebuggerNonUserCode]
	public static async ValueTask<TResult?> _WaitNoCancelExceptions<TResult>(this ValueTask<TResult> task)
	{
		try
		{
			return await task;
		}
		catch (OperationCanceledException)
		{
			return default;
		}
	}

	/// <summary>
	///    returns true if the task is in a faulted state and the exception is of type TException
	/// </summary>
	public static bool _IsFaultedBy<TException>(this Task task) where TException : Exception
	{
		if (task.IsFaulted is false)
		{
			return false;
		}

		foreach (var ex in task.Exception!.InnerExceptions)
		{
			if (ex is TException)
			{
				return true;
			}
		}

		return false;
	}

	public static async Task<TResult> _Then<TResult>(this Task task, Func<Task<TResult>> thenMethod, ConfigureAwaitOptions awaitOptions = ConfigureAwaitOptions.ContinueOnCapturedContext)
	{

		// Await the completion of the initial task
		await task.ConfigureAwait(awaitOptions);

		// Execute the continuation method and await its result
		return await thenMethod().ConfigureAwait(awaitOptions);
	}
	public static async Task _Then(this Task task, Func<Task> thenMethod, ConfigureAwaitOptions awaitOptions = ConfigureAwaitOptions.ContinueOnCapturedContext)
	{

		// Await the completion of the initial task
		await task.ConfigureAwait(awaitOptions);

		// Execute the continuation method and await its result
		await thenMethod().ConfigureAwait(awaitOptions);
	}
	public static async Task _Then(this Task task, Action thenMethod, ConfigureAwaitOptions awaitOptions = ConfigureAwaitOptions.ContinueOnCapturedContext)
	{

		// Await the completion of the initial task
		await task.ConfigureAwait(awaitOptions);

		// Execute the continuation method and await its result
		thenMethod();
	}

	///// <summary>
	/////    like Task.ContinueWith() but if the task is already completed, gives the callback an opportunity to complete
	/////    immediately.
	///// </summary>
	///// <param name="task"></param>
	///// <param name="callback"></param>
	///// <returns></returns>
	//public static async Task _ContinueWithSyncOrAsync(this Task task, Func<Task, Task> callback)
	//{
	//   if (task.IsCompleted)
	//   {
	//      await callback(task);
	//   }
	//   else
	//   {
	//      _ = task.ContinueWith(callback);
	//   }
	//}

	//public static async Task _ContinueWithSyncOrAsync(this Task task, Func<Task> callback)
	//{
	//   if (task.IsCompleted)
	//   {
	//      await callback();
	//   }
	//   else
	//   {
	//      _ = task.ContinueWith(async _continuingFromTask => { await callback(); });
	//   }
	//}

	//public static Task _ForAwait(this Task task)
	//{
	//	return task ?? Task.CompletedTask;
	//}

	//public static Task<T> _ForAwait<T>(this Task<T> task, T defaultValue = default)
	//{
	//	return task ?? Task.FromResult(defaultValue);
	//}

	/// <summary>
	///    await the task and obtain it's results (if any).
	///    <para>useful for letting synchronous (non async) code call async methods</para>
	/// </summary>
	/// <remarks>
	///    if help needed improving the code
	///    https://stackoverflow.com/questions/9343594/how-to-call-asynchronous-method-from-synchronous-method-in-c
	///    https://learn.microsoft.com/en-us/archive/msdn-magazine/2015/july/async-programming-brownfield-async-development#the-blocking-hack
	/// </remarks>
	public static void _SyncWait(this ValueTask task, TimeSpan timeout)
	{
		var ct = __.Async.CancelAfter(timeout);
		_SyncWait(task, ct);

		////_SyncWait(task.AsTask(), timeout);

		////if (timeout == default)
		////{
		////	timeout = TimeSpan.FromSeconds(10);
		////}

		////var ct = __.Async.CancelAfter(TimeSpan.FromSeconds(10));

		////if (useSpinWait)
		////{
		////	var wh = ct.WaitHandle;
		////	while (wh.WaitOne(10) is false)
		////	{
		////		if (task.IsCompleted)
		////		{
		////			break;
		////		}
		////	}
		////	ct.ThrowIfCancellationRequested();
		////	return;
		////}

		//if (asTask)
		//{
		//	_SyncWait(task.AsTask(), timeout);
		//	return;
		//}

		//if (useAwaiterDirect)
		//{
		//	 task.GetAwaiter().GetResult();
		//	 return;
		//}

		//if (useCurrentExecutionContext)
		//{
		//	task.ConfigureAwait(true).GetAwaiter().GetResult();
		//}
		//else
		//{
		//	task.ConfigureAwait(false).GetAwaiter().GetResult();
		//}
	}
	/// <summary>
	/// will continue the task execution later, but still using the current synchronization context
	/// </summary>
	/// <param name="task">task to continue later</param>
	/// <returns>a task that can be inspected for status.</returns>
	[Obsolete("on platforms with a synchronization context (like godot),  you can just run the async method bare, this is not needed.")]
	public static Task _ContinueLaterInCurrentContext(this Task task)
	{
		if (task.IsCompleted)
		{
			return task;
		}
		var factoryTask = Task.Factory.Run(() => task, scheduler: TaskScheduler.FromCurrentSynchronizationContext());

		return factoryTask;
	}
	/// <summary>
	/// will continue the task execution later, but still using the current synchronization context
	/// </summary>
	/// <param name="task">task to continue later</param>
	/// <returns>a task that can be inspected for status.</returns>
	[Obsolete("on platforms with a synchronization context (like godot),  you can just run the async method bare, this is not needed.")]
	public static Task<TResult> _ContinueLaterInCurrentContext<TResult>(this Task<TResult> task)
	{
		if (task.IsCompleted)
		{
			return task;
		}
		var factoryTask = Task.Factory.Run(() => task, scheduler: TaskScheduler.FromCurrentSynchronizationContext());

		return factoryTask;
	}

	public static void _SyncWait(this ValueTask task, CancellationToken ct = default)
	{
		_SyncWait(task.AsTask(), ct);
	}

	/// <summary>
	///    await the task and obtain it's results (if any).
	///    <para>useful for letting synchronous (non async) code call async methods</para>
	/// </summary>
	/// <remarks>
	///    if help needed improving the code
	///    https://stackoverflow.com/questions/9343594/how-to-call-asynchronous-method-from-synchronous-method-in-c
	///    https://learn.microsoft.com/en-us/archive/msdn-magazine/2015/july/async-programming-brownfield-async-development#the-blocking-hack
	/// </remarks>
	public static void _SyncWait(this Task task, TimeSpan timeout)
	{

		//task.GetAwaiter().GetResult();
		var ct = __.Async.CancelAfter(timeout);
		_SyncWait(task, ct);

		//if (timeout == default)
		//{
		//	timeout = TimeSpan.FromSeconds(10);
		//}



		//if (useSpinWait)
		//{
		//	var ct = __.Async.CancelAfter(TimeSpan.FromSeconds(10));

		//	//using var cts = new CancellationTokenSource();
		//	//__.Async.CancelAfter(cts,TimeSpan.FromSeconds(10));
		//	//var ct = cts.Token;

		//	var wh = ct.WaitHandle;
		//	while (wh.WaitOne(10) is false)
		//	{
		//		if (task.IsCompleted)
		//		{
		//			break;
		//		}
		//	}
		//	ct.ThrowIfCancellationRequested();
		//	//cts.Cancel();
		//	return;
		//}

		//if (useAwaiterDirect)
		//{

		//	//task.GetAwaiter().GetResult();
		//	try
		//	{
		//		var awaiter = task.GetAwaiter();

		//		try
		//		{
		//			awaiter.GetResult();
		//		}
		//		catch (Exception ex)
		//		{
		//			__.Throw(ex);
		//		}
		//	}
		//	catch (Exception ex)
		//	{
		//		__.Throw(ex);
		//	}


		//	return;
		//}


		//if (ct.IsCancellationRequested)
		//{
		//	//throw __.Throw("_SyncWait timeout expired.  waited seconds:" + timeout.TotalSeconds);

		//}


		//if (useCurrentExecutionContext)
		//{
		//	task.ConfigureAwait(true).GetAwaiter().GetResult();
		//}
		//else
		//{
		//	task.ConfigureAwait(false).GetAwaiter().GetResult();
		//}
	}



	public static void _SyncWait(this Task task, CancellationToken ct = default)
	{
#if DEBUG
		if (task.Status == TaskStatus.WaitingForActivation)
		{
			//spin loop until task is scheduled.  use our __.Async.CancelAfter ct so it pauses time while debugging
			var spinCt = __.Async.CancelAfter(ct, TimeSpan.FromSeconds(1));
			//var sw = Stopwatch.StartNew();
			var loop = 0;
			while (spinCt.IsCancellationRequested is false && ct.IsCancellationRequested is false)
			{
				loop++;
				var waitMs = loop / 10;
				var result = task.Wait(waitMs);
				if (result)
				{
					//task completed
					return;
				}
				//task.Wait(spinCt);
				if (task.Status != TaskStatus.WaitingForActivation)
				{
					break;
				}
			}

			if (ct.IsCancellationRequested is false)
			{
				__.AssertIfNot(task.Status != TaskStatus.WaitingForActivation,
					"task not yet scheduled.  likely you attempt to execute the task manually via function signature ex:'YourAsyncMethod()._SyncWait()'.  If you really need to run async code and wait Synchronously, use '__.Async.Run(YourAsyncMethod)._SyncWait()'  ");
			}
		}
#endif
		task.Wait(ct);



		//var waitTask = Task.Run(async () =>
		//{
		//	await task.ConfigureAwait(false);
		//}, ct);

		//var waitTask = __.Async.LongRun(async () =>
		//{
		//	await task; //.ConfigureAwait(false);
		//});
		////task.WaitAndUnwrapException();


		//waitTask.Wait(ct);

		//task.ConfigureAwait(false).GetAwaiter().GetResult();

		//var waitTask = __.Async.Run(async () =>
		//{
		//	await Task.Delay(100);
		//}, ct);



	}

	public static TResult _SyncWait<TResult>(this ValueTask<TResult> task, TimeSpan timeout = default)
	{

		var ct = __.Async.CancelAfter(timeout);
		return _SyncWait(task, ct);
		////return _SyncWait(task.AsTask(), timeout);
		//if (asTask)
		//{
		//	return _SyncWait(task.AsTask(), timeout);
		//}
		//if (useAwaiterDirect)
		//{
		//	return task.GetAwaiter().GetResult();
		//}
		//if (useCurrentExecutionContext)
		//{
		//	return task.ConfigureAwait(true).GetAwaiter().GetResult();
		//}

		//return task.ConfigureAwait(false).GetAwaiter().GetResult();
	}

	public static TResult _SyncWait<TResult>(this ValueTask<TResult> task, CancellationToken ct = default)
	{
		return _SyncWait(task.AsTask(), ct);
	}

	//private static bool asTask = true;
	//private static bool useSpinWait = true; //TODO:  spinwait causes godot to not able to unload assembly, likely due to debugCancelable logic.  need to investigate/fix
	//private static bool useCurrentExecutionContext = false;
	//private static bool useAwaiterDirect = true;
	public static TResult _SyncWait<TResult>(this Task<TResult> task, TimeSpan timeout)
	{

		var ct = __.Async.CancelAfter(timeout);
		return _SyncWait(task, ct);

		////if (timeout == default)
		////{
		////	timeout = TimeSpan.FromSeconds(10);
		////}

		////var ct = __.Async.CancelAfter(TimeSpan.FromSeconds(10));

		////return task.WaitAndUnwrapException(ct);

		//if (useAwaiterDirect)
		//{
		//	return task.GetAwaiter().GetResult();
		//}
		//if (useCurrentExecutionContext)
		//{
		//	return task.ConfigureAwait(true).GetAwaiter().GetResult();
		//}

		//return task.ConfigureAwait(false).GetAwaiter().GetResult();
	}

	public static TResult _SyncWait<TResult>(this Task<TResult> task, CancellationToken ct = default)
	{


#if DEBUG
		if (task.Status == TaskStatus.WaitingForActivation)
		{
			//spin loop until task is scheduled
			var sw = Stopwatch.StartNew();
			var loop = 0;
			while (sw.ElapsedMilliseconds < 1000 && ct.IsCancellationRequested is false)
			{
				loop++;
				var waitMs = loop / 10;
				task.Wait(waitMs, ct);
				if (task.Status != TaskStatus.WaitingForActivation)
				{
					break;
				}
			}

			if (ct.IsCancellationRequested is false)
			{
				__.AssertIfNot(task.Status != TaskStatus.WaitingForActivation,
					"task not yet scheduled.  likely you attempt to execute the task manually via function signature ex:'YourAsyncMethod()._SyncWait()'.  If you really need to run async code and wait Synchronously, use '__.Async.Run(YourAsyncMethod)._SyncWait()'  ");
			}
		}
#endif

		task.Wait(ct);
		return task.Result;

		////if (timeout == default)
		////{
		////	timeout = TimeSpan.FromSeconds(10);
		////}

		////var ct = __.Async.CancelAfter(TimeSpan.FromSeconds(10));

		////return task.WaitAndUnwrapException(ct);

		//if (useAwaiterDirect)
		//{
		//	return task.GetAwaiter().GetResult();
		//}
		//if (useCurrentExecutionContext)
		//{
		//	return task.ConfigureAwait(true).GetAwaiter().GetResult();
		//}

		//return task.ConfigureAwait(false).GetAwaiter().GetResult();



	}


	public static void _SyncWaitNoExceptions(this Task task, TimeSpan timeout = default)
	{

		try
		{
			_SyncWait(task, timeout);
		}
#pragma warning disable NN_R005, NN_R006 // Method designed to swallow all exceptions
		catch (Exception)
		{
			// Method contract: swallow all exceptions
		}
#pragma warning restore NN_R005, NN_R006


	}
	public static void _SyncWaitNoCancelExceptions(this Task task, TimeSpan timeout)
	{
		var ct = __.Async.CancelAfter(timeout);
		_SyncWaitNoCancelExceptions(task, ct);



	}




	public static void _SyncWaitNoCancelExceptions(this Task task, CancellationToken ct = default)
	{

		try
		{
			_SyncWait(task, ct);
		}
#pragma warning disable NN_R005 // Filters cancellation, re-throws others via __.Throw()
		catch (Exception ex)
		{
			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				case AggregateException ae:
					switch (ae.InnerException)
					{
						case TaskCanceledException:
						case OperationCanceledException:
							break;
						default:
							__.Throw(ex);
							break;
					}
					break;
				default:
					__.Throw(ex);
					break;
			}
		}
#pragma warning restore NN_R005


	}
	/// <summary>
	/// Converts a Task&lt;T&gt; to Task&lt;Maybe&lt;T&gt;&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks.
	/// </summary>
	/// <typeparam name="T">The type of the task result</typeparam>
	/// <param name="task">The task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe&lt;T&gt; containing either the successful result or a Problem</returns>
	public static async Task<Maybe<T>> _ToMaybe<T>(
	this Task<Maybe<T>> task,
	[CallerMemberName] string memberName = "",
	[CallerFilePath] string sourceFilePath = "",
	[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			Maybe<T> maybeResult = await task.ConfigureAwait(false);
			return maybeResult;
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return Maybe<T>.Error(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}
	/// <summary>
	/// Converts a Task&lt;T&gt; to Task&lt;Maybe&lt;T&gt;&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks.
	/// </summary>
	/// <typeparam name="T">The type of the task result</typeparam>
	/// <param name="task">The task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe&lt;T&gt; containing either the successful result or a Problem</returns>
	public static async Task<Maybe<T>> _ToMaybe<T>(
	this ValueTask<Maybe<T>> task,
	[CallerMemberName] string memberName = "",
	[CallerFilePath] string sourceFilePath = "",
	[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			Maybe<T> maybeResult = await task.ConfigureAwait(false);
			return maybeResult;
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return Maybe<T>.Error(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}
	/// <summary>
	/// Converts a Task to Task&lt;Maybe&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks.
	/// </summary>
	/// <param name="task">The task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe containing either the successful result or a Problem</returns>
	public static async Task<Maybe> _ToMaybe(
	this Task<Maybe> task,
	[CallerMemberName] string memberName = "",
	[CallerFilePath] string sourceFilePath = "",
	[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			Maybe maybeResult = await task.ConfigureAwait(false);
			return maybeResult;
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return new Maybe(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}
	/// <summary>
	/// Converts a Task to Task&lt;Maybe&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks.
	/// </summary>
	/// <param name="task">The task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe containing either the successful result or a Problem</returns>
	public static async Task<Maybe> _ToMaybe(
	this ValueTask<Maybe> task,
	[CallerMemberName] string memberName = "",
	[CallerFilePath] string sourceFilePath = "",
	[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			Maybe maybeResult = await task.ConfigureAwait(false);
			return maybeResult;
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return new Maybe(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}


	/// <summary>
	/// Converts a Task&lt;T&gt; to Task&lt;Maybe&lt;T&gt;&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks.
	/// </summary>
	/// <typeparam name="T">The type of the task result</typeparam>
	/// <param name="task">The task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe&lt;T&gt; containing either the successful result or a Problem</returns>
	[return: NotNull]
	public static async Task<Maybe<T>> _ToMaybe<T>(
		this Task<T> task,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			T result = await task.ConfigureAwait(false);
			return Maybe<T>.Success(result, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);

			var toReturn = Maybe<T>.Error(problem, memberName, sourceFilePath, sourceLineNumber);

			return toReturn;
		}
#pragma warning restore NN_R005
	}

	/// <summary>
	/// Converts a ValueTask&lt;T&gt; to Task&lt;Maybe&lt;T&gt;&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks.
	/// </summary>
	/// <typeparam name="T">The type of the task result</typeparam>
	/// <param name="valueTask">The value task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe&lt;T&gt; containing either the successful result or a Problem</returns>
	[return: NotNull]
	public static async Task<Maybe<T>> _ToMaybe<T>(
		this ValueTask<T> valueTask,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			T result = await valueTask.ConfigureAwait(false);
			return Maybe<T>.Success(result, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return Maybe<T>.Error(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}

	/// <summary>
	/// Converts a Task (non-generic) to Task&lt;Maybe&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks for void-returning async methods.
	/// </summary>
	/// <param name="task">The task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe containing either success or a Problem</returns>
	[return: NotNull]
	public static async Task<Maybe> _ToMaybe(
		this Task task,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			await task.ConfigureAwait(false);
			return Maybe.SuccessResult(memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return new Maybe(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}

	/// <summary>
	/// Converts a ValueTask (non-generic) to Task&lt;Maybe&gt;, capturing any exceptions as Problems.
	/// This enables fluent exception handling without try-catch blocks for void-returning async methods.
	/// </summary>
	/// <param name="valueTask">The value task to convert</param>
	/// <param name="memberName">The calling member name (auto-captured)</param>
	/// <param name="sourceFilePath">The calling source file (auto-captured)</param>
	/// <param name="sourceLineNumber">The calling source line (auto-captured)</param>
	/// <returns>A Maybe containing either success or a Problem</returns>
	[return: NotNull]
	public static async ValueTask<Maybe> _ToMaybe(
		this ValueTask valueTask,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		try
		{
			await valueTask.ConfigureAwait(false);
			return Maybe.SuccessResult(memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning disable NN_R005 // _ToMaybe is designed to convert all exceptions to Maybe<T>
		catch (Exception ex)
		{
			NotNot.Problem problem = NotNot.Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber);
			return new Maybe(problem, memberName, sourceFilePath, sourceLineNumber);
		}
#pragma warning restore NN_R005
	}

	public static async ValueTask<Maybe> _ToMaybe(this ValueTask? valueTask,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		)
	{
		if (valueTask.HasValue)
		{
			return await valueTask.Value._ToMaybe();
		}
		var problem = new Problem(new ArgumentNullException(valueTaskArgName), memberName, sourceFilePath, sourceLineNumber);
		return problem;
	}

	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>also ignores Microsoft.JSInterop.JSDisconnectedException</para>
	/// </summary>
	/// <returns></returns>
	public static async Task _WaitIgnoreCancel(this Task valueTask,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		)
	{
		try
		{
			await valueTask;
		}
		catch (Exception ex)
		{
			//handle aggregate exceptions (wrapped)
			if (ex is AggregateException ae && ae.InnerException != null)
			{
				ex = ae.InnerException;
			}

			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				default:
					{
						//handle cancel exceptions from other non-core libraries (like Blazor WebAssembly)
						switch (ex.GetType().FullName)
						{
							case "Microsoft.JSInterop.JSDisconnectedException":
								//JSInterop exception when Blazor WebAssembly is unloaded/disconnected
								break;
							default:
								throw __.Throw(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
								break;
						}
						break;
					}

			}
		}
	}


	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>also ignores Microsoft.JSInterop.JSDisconnectedException</para>
	/// </summary>
	/// <returns></returns>
	public static async ValueTask _WaitIgnoreCancel(this ValueTask valueTask,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		)
	{
		try
		{
			await valueTask;
		}
		catch (Exception ex)
		{
			//handle aggregate exceptions (wrapped)
			if (ex is AggregateException ae && ae.InnerException != null)
			{
				ex = ae.InnerException;
			}

			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				default:
					{
						//handle cancel exceptions from other non-core libraries (like Blazor WebAssembly)
						switch (ex.GetType().FullName)
						{
							case "Microsoft.JSInterop.JSDisconnectedException":
								//JSInterop exception when Blazor WebAssembly is unloaded/disconnected
								break;
							default:
								throw __.Throw(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
								break;
						}
						break;
					}

			}
		}
	}

	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// </summary>
	/// <returns></returns>
	public static async ValueTask _WaitIgnoreCancelOrNull(this ValueTask? valueTask,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		)
	{
		if (valueTask.HasValue)
		{
			await valueTask.Value._WaitIgnoreCancel(sourceMemberName, sourceFilePath, sourceLineNumber, valueTaskArgName);
		}
		else
		{
			return;
		}
	}


	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>also ignores Microsoft.JSInterop.JSDisconnectedException</para>
	/// <para>returns null if throws for the above reasons</para>
	/// </summary>
	/// <returns></returns>
	public static async Task<TValue?> _WaitIgnoreCancel<TValue>(this Task<TValue?> task,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("task")] string valueTaskArgName = ""
		) where TValue:class
	{
		try
		{
			return await task;
		}
		catch (Exception ex)
		{
			//handle aggregate exceptions (wrapped)
			if (ex is AggregateException ae && ae.InnerException != null)
			{
				ex = ae.InnerException;
			}

			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				default:
					{
						//handle cancel exceptions from other non-core libraries (like Blazor WebAssembly)
						switch (ex.GetType().FullName)
						{
							case "Microsoft.JSInterop.JSDisconnectedException":
								//JSInterop exception when Blazor WebAssembly is unloaded/disconnected
								break;
							default:
								throw __.Throw(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
								break;
						}
						break;
					}
			}
			return null;
		}
	}


	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>also ignores Microsoft.JSInterop.JSDisconnectedException</para>
	/// <para>returns null if throws for the above reasons</para>
	/// </summary>
	/// <returns></returns>
	public static async ValueTask<TValue?> _WaitIgnoreCancel<TValue>(this ValueTask<TValue?> valueTask,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		) where TValue : class
	{
		try
		{
			return await valueTask;
		}
		catch (Exception ex)
		{
			//handle aggregate exceptions (wrapped)
			if (ex is AggregateException ae && ae.InnerException != null)
			{
				ex = ae.InnerException;
			}

			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				default:
					{
						//handle cancel exceptions from other non-core libraries (like Blazor WebAssembly)
						switch (ex.GetType().FullName)
						{
							case "Microsoft.JSInterop.JSDisconnectedException":
								//JSInterop exception when Blazor WebAssembly is unloaded/disconnected
								break;
							default:
								throw __.Throw(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
								break;
						}
						break;
					}

			}
			return null;
		}
	}

	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>returns null if throws for the above reasons</para>
	/// </summary>
	/// <returns></returns>
	public static async ValueTask<TValue?> _WaitIgnoreCancelOrNull<TValue>(this ValueTask<TValue?>? valueTask,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		) where TValue : class
	{
		if (valueTask.HasValue)
		{
			return await valueTask.Value._WaitIgnoreCancel(sourceMemberName, sourceFilePath, sourceLineNumber, valueTaskArgName);
		}
		else
		{
			return null;
		}
	}
	

	/////////////////////////
	///



	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>also ignores Microsoft.JSInterop.JSDisconnectedException</para>
	/// <para>returns null if throws for the above reasons</para>
	/// </summary>
	/// <returns></returns>
	public static async Task<TValue?> _WaitIgnoreCancel<TValue>(this Task<TValue?> task, TValue defaultValue,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("task")] string valueTaskArgName = ""
		) 
	{
		try
		{
			return await task;
		}
		catch (Exception ex)
		{
			//handle aggregate exceptions (wrapped)
			if (ex is AggregateException ae && ae.InnerException != null)
			{
				ex = ae.InnerException;
			}

			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				default:
					{
						//handle cancel exceptions from other non-core libraries (like Blazor WebAssembly)
						switch (ex.GetType().FullName)
						{
							case "Microsoft.JSInterop.JSDisconnectedException":
								//JSInterop exception when Blazor WebAssembly is unloaded/disconnected
								break;
							default:
								throw __.Throw(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
								break;
						}
						break;
					}
			}
			return defaultValue;
		}
	}


	/// <summary>
	/// ignore cancel exceptions, or if the ValueTask? is null
	/// <para>meant for throw-away awaits, like during disposals</para>
	/// <para>also ignores Microsoft.JSInterop.JSDisconnectedException</para>
	/// <para>returns null if throws for the above reasons</para>
	/// </summary>
	/// <returns></returns>
	public static async ValueTask<TValue?> _WaitIgnoreCancel<TValue>(this ValueTask<TValue?> valueTask, TValue defaultValue,
		[CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0,
		[CallerArgumentExpression("valueTask")] string valueTaskArgName = ""
		)
	{
		try
		{
			return await valueTask;
		}
		catch (Exception ex)
		{
			//handle aggregate exceptions (wrapped)
			if (ex is AggregateException ae && ae.InnerException != null)
			{
				ex = ae.InnerException;
			}

			switch (ex)
			{
				case TaskCanceledException:
				case OperationCanceledException:
					break;
				default:
					{
						//handle cancel exceptions from other non-core libraries (like Blazor WebAssembly)
						switch (ex.GetType().FullName)
						{
							case "Microsoft.JSInterop.JSDisconnectedException":
								//JSInterop exception when Blazor WebAssembly is unloaded/disconnected
								break;
							default:
								throw __.Throw(ex, sourceMemberName, sourceFilePath, sourceLineNumber);
								break;
						}
						break;
					}

			}
			return defaultValue;
		}
	}


}


//    Provides extension methods for task factories.
//    coppied from Nito.AsyncEx but adding CancellationToken support

