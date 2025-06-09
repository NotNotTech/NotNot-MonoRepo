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
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Blake3;
using CommunityToolkit.HighPerformance.Helpers;
using Newtonsoft.Json.Linq;
using Nito.AsyncEx.Synchronous;
using NotNot;
using NotNot._internal.Threading;
using NotNot.Collections.Advanced;

//using Xunit.Sdk;

//using CommunityToolkit.HighPerformance;
//using DotNext;

public static class zz_Extensions_Char
{
	public static string _Repeat(this char c, int count)
	{
		return new string(c, count);
	}
}

public static class zz_Extensions_Regex
{
	/// <summary>
	/// like normal regex.Match but returns null on match failure.  (normal regex returns a match with .Success=false)
	/// </summary>
	/// <param name="regex"></param>
	/// <param name="input"></param>
	/// <returns></returns>
	public static Match? _Match(this Regex regex, string input)
	{
		if (input is null)
		{
			return null;
		}

		var match = regex.Match(input);
		if (match.Success is false)
		{
			return null;
		}

		return match;
	}

	/// <summary>
	/// returns the first match result, or a default string if no match  noMatchDefault = ""
	/// </summary>
	/// <param name="regex"></param>
	/// <param name="input"></param>
	/// <param name="noMatchDefault"></param>
	/// <returns></returns>
	public static string _FirstMatch(this Regex regex, string input, string noMatchDefault = "")
	{
		var match = regex.Match(input);
		if (match.Success is false)
		{
			return noMatchDefault;
		}
		return match.Groups[1].Value;
	}




}

public static class zz_Extensions_IList
{
	/// <summary>
	/// allows adding new, or removing the current item while enumerating.  Does not impact the enumeration.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <returns></returns>
	public static IEnumerable<T> _ForEachReverse<T>(this IList<T> list)
	{
		if (list is null)
		{
			yield break;
		}
		for (var i = list.Count - 1; i >= 0; i--)
		{
			yield return list[i];
		}
	}



	/// <summary>
	/// remove the first occurance and return it
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <returns></returns>
	public static T _Pop<T>(this IList<T> list)
	{
		var toReturn = list[0];
		list.RemoveAt(0);
		return toReturn;
	}
	public static bool _Contains<T>(this IList<T> list, Predicate<T> predicate) where T : class
	{
		foreach (var item in list)
		{
			if (predicate(item))
			{
				return true;
			}
		}

		return false;
	}

	public static bool _AddIfNotNull<T>(this IList<T> list, T? value) where T : class
	{
		if (value != null)
		{
			list.Add(value);
			return true;
		}

		return false;
	}

	public static T _GetOrCreate<T>(this IList<T> list, Func<T, bool> findPredicate,
		Func<T> createFunc)
	{
		T value;
		if (list._TryGet(findPredicate, out value))
		{
			return value;
		}

		value = createFunc();
		list.Add(value);
		return value;
	}


	public static int _RemoveAll<T>(this IList<T> list, Func<T, bool> predicate)
	{
		var removeCount = 0;
		for (var i = list.Count - 1; i >= 0; i--)
			if (predicate(list[i]))
			{
				list.RemoveAt(i);
				removeCount++;
			}

		return removeCount;
	}

	public static bool _RemoveLast<T>(this IList<T> list, Func<T, bool> predicate)
	{
		for (var i = list.Count - 1; i >= 0; i--)
			if (predicate(list[i]))
			{
				list.RemoveAt(i);
				return true;
			}

		return false;
	}
}

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
		string innerErrorString = null;
		if (e.InnerException != null)
		{
			var innerError = e.InnerException;
			innerErrorString = $" innerError: {innerError.GetType().Name}: {innerError.Message}";
		}

		return $"{e.GetType().Name}: {e.Message}{innerErrorString}";
	}
}

public static class zz_Extensions_CancellationToken
{
	/// <summary>
	///    create a CancellationTokenSource that is linked to this CancellationToken and another CancellationToken
	/// </summary>
	public static CancellationTokenSource _LinkedCts(this CancellationToken cancellationToken, CancellationToken other)
	{
		cancellationToken.ThrowIfCancellationRequested();
		other.ThrowIfCancellationRequested();
		return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, other);
	}

	/// <summary>
	///    create a CancellationTokenSource that is linked to this CancellationToken
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>
	public static CancellationTokenSource _LinkedCts(this CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
	}

	public static CancellationToken _Link(this CancellationToken cancellationToken, CancellationToken other)
	{
		cancellationToken.ThrowIfCancellationRequested();
		other.ThrowIfCancellationRequested();
		return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, other).Token;
	}
	/// <summary>
	/// create a new ct that cancels when the original ct cancels, or after a delay.  debuggable, in that it will pause the timeout from counting-down while the application is paused in a debugger.
	/// </summary>
	/// <param name="linked"></param>
	/// <param name="delay"></param>
	/// <returns></returns>
	public static CancellationToken _TimeoutDebuggable(this CancellationToken linked, TimeSpan delay)
	{
		return DebuggableTimeoutCancelTokenHelper.Timeout(linked, delay);
	}


}

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

public static class zz_Extensions_Task
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
	public static async Task _WaitWithoutException(this Task task)
	{
		if (task is null)
		{
			return;
		}
		try
		{
			await task;
		}
#pragma warning disable ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
		catch (Exception)
		{
		}
#pragma warning restore ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
	}

	public static async Task<T?> _WaitWithoutException<T>(this Task<T> task)
	{
		try
		{
			return await task;
		}
		catch (Exception)
		{
#pragma warning disable ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
			return default;
#pragma warning restore ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
		}
	}

	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed.   Inspect the original task for result status.
	/// </summary>
	public static async ValueTask _WaitWithoutException(this ValueTask task)
	{
		try
		{
			await task;
		}
#pragma warning disable ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
		catch (Exception)
		{
		}
#pragma warning restore ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
	}

	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed.   Inspect the original task for result status.
	/// </summary>
	public static async ValueTask<TResult?> _WaitWithoutException<TResult>(this ValueTask<TResult> task)
	{
		try
		{
			return await task;
		}
#pragma warning disable ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
		catch (Exception)
		{
			return default;
		}
#pragma warning restore ERP022, RCS1075 // Avoid empty catch clause that catches System.Exception.
	}



	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[DebuggerHidden, DebuggerNonUserCode]
	public static async Task _WaitWithoutCancel(this Task task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
	}


	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[DebuggerHidden, DebuggerNonUserCode]
	public static async Task<T?> _WaitWithoutCancel<T>(this Task<T> task)
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
	[DebuggerHidden, DebuggerNonUserCode]
	public static async ValueTask _WaitWithoutCancel(this ValueTask task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
	}


	/// <summary>
	///    awaits any result (success, cancel, error) without throwing.
	///    the result of this call will always succeed if original task throws a TaskAbort or operationCancelled exception.   Inspect the original task for result status.
	/// </summary>
	[DebuggerHidden, DebuggerNonUserCode]
	public static async ValueTask<TResult?> _WaitWithoutCancel<TResult>(this ValueTask<TResult> task)
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
		catch (Exception ex)
		{
			//no op
		}


	}
	public static void _SyncWaitNoCancelException(this Task task, TimeSpan timeout)
	{
		var ct = __.Async.CancelAfter(timeout);
		_SyncWaitNoCancelException(task, ct);



	}




	public static void _SyncWaitNoCancelException(this Task task, CancellationToken ct = default)
	{

		try
		{
			_SyncWait(task, ct);
		}
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


	}
}

/// <summary>
///    Provides extension methods for task factories.
///    coppied from Nito.AsyncEx but adding CancellationToken support
/// </summary>
public static class zz_Extensions_TaskFactory
{
	/// <summary>
	///    Queues work to the task factory and returns a <see cref="Task" /> representing that work. If the task factory does
	///    not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task Run(this TaskFactory @this, Action action, CancellationToken ct = default,
		TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		options = options | TaskCreationOptions.DenyChildAttach;
		//	__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false, "LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(action, ct, options, scheduler);
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(action, cts.Token, options, scheduler);

#pragma warning disable PH_P007 // Unused Cancellation Token
		// ReSharper disable once MethodSupportsCancellation
		_ = toReturn.ContinueWith(_ => { cts.Dispose(); });
#pragma warning restore PH_P007 // Unused Cancellation Token

		return toReturn;
	}

	/// <summary>
	///    Queues work to the task factory and returns a <see cref="Task{TResult}" /> representing that work. If the task
	///    factory does not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task<TResult> Run<TResult>(this TaskFactory @this, Func<TResult> action,
		CancellationToken ct = default, TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		options = options | TaskCreationOptions.DenyChildAttach;
		//		__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false, "LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(action, ct, options, scheduler);
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(action, cts.Token, options, scheduler);

#pragma warning disable PH_P007 // Unused Cancellation Token
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007 // Unused Cancellation Token

		return toReturn;
	}

	/// <summary>
	///    Queues work to the task factory and returns a proxy <see cref="Task" /> representing that work. If the task factory
	///    does not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task Run(this TaskFactory @this, Func<Task> action, CancellationToken ct = default,
		TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		//options = options | TaskCreationOptions.DenyChildAttach;
		//		__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false, "LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(action, ct, options, scheduler).Unwrap();
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(action, cts.Token, options, scheduler).Unwrap();

#pragma warning disable PH_P007
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007

		return toReturn;

		//return @this.StartNew(action, ct, @this.CreationOptions | TaskCreationOptions.DenyChildAttach, @this.Scheduler ?? TaskScheduler.Default).Unwrap();
	}

	/// <summary>
	///    Queues work to the task factory and returns a proxy <see cref="Task{TResult}" /> representing that work. If the task
	///    factory does not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task<TResult> Run<TResult>(this TaskFactory @this, Func<Task<TResult>> action,
		CancellationToken ct = default, TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		//options = options | TaskCreationOptions.DenyChildAttach;
		//	__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false,"LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(action, ct, options, scheduler).Unwrap();
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(action, cts.Token, options, scheduler).Unwrap();

#pragma warning disable PH_P007
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007

		return toReturn;
		//return @this.StartNew(action, ct, @this.CreationOptions | TaskCreationOptions.DenyChildAttach, @this.Scheduler ?? TaskScheduler.Default).Unwrap();
	}


	/// <summary>
	///    Queues work to the task factory and returns a <see cref="Task" /> representing that work. If the task factory does
	///    not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task Run(this TaskFactory @this, Action<CancellationToken> action, CancellationToken ct = default,
		TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		options = options | TaskCreationOptions.DenyChildAttach;
		//		__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false, "LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(() => action(ct), ct, options, scheduler);
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(() => action(cts.Token), cts.Token, options, scheduler);

#pragma warning disable PH_P007
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007

		return toReturn;
	}

	/// <summary>
	///    Queues work to the task factory and returns a <see cref="Task{TResult}" /> representing that work. If the task
	///    factory does not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task<TResult> Run<TResult>(this TaskFactory @this, Func<CancellationToken, TResult> action,
		CancellationToken ct = default, TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		options = options | TaskCreationOptions.DenyChildAttach;
		//		__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false, "LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(() => action(ct), ct, options, scheduler);
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(() => action(cts.Token), cts.Token, options, scheduler);

#pragma warning disable PH_P007 // Unused Cancellation Token
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007 // Unused Cancellation Token

		return toReturn;
	}

#pragma warning disable PH_S014
	/// <summary>
	///    Queues work to the task factory and returns a proxy <see cref="Task" /> representing that work. If the task factory
	///    does not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task Run(this TaskFactory @this, Func<CancellationToken, Task> action, CancellationToken ct = default,
		TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		options = options | TaskCreationOptions.DenyChildAttach;
		//		__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false, "LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(() => action(ct), ct, options, scheduler).Unwrap();
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(() => action(cts.Token), cts.Token, options, scheduler).Unwrap();

#pragma warning disable PH_P007 // Unused Cancellation Token
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007 // Unused Cancellation Token

		return toReturn;

		//return @this.StartNew(action, ct, @this.CreationOptions | TaskCreationOptions.DenyChildAttach, @this.Scheduler ?? TaskScheduler.Default).Unwrap();
	}

	/// <summary>
	///    Queues work to the task factory and returns a proxy <see cref="Task{TResult}" /> representing that work. If the task
	///    factory does not specify a task scheduler, the thread pool task scheduler is used.
	/// </summary>
	/// <param name="this">The <see cref="TaskFactory" />. May not be <c>null</c>.</param>
	/// <param name="action">The action delegate to execute. May not be <c>null</c>.</param>
	/// <returns>The started task.</returns>
	public static Task<TResult> Run<TResult>(this TaskFactory @this, Func<CancellationToken, Task<TResult>> action,
		CancellationToken ct = default, TaskCreationOptions? creationOptions = null, TaskScheduler? scheduler = null)
	{
		if (@this == null)
		{
			throw new ArgumentNullException(nameof(@this));
		}

		if (action == null)
		{
			throw new ArgumentNullException(nameof(action));
		}

		TaskCreationOptions options = creationOptions ?? @this.CreationOptions;
		options = options | TaskCreationOptions.DenyChildAttach;
		//	__.GetLogger()._EzError(options.HasFlag(TaskCreationOptions.LongRunning) is false,"LongRunning is put on it's own thread.  are you sure this is what you want?");

		scheduler ??= @this.Scheduler ?? TaskScheduler.Default;

		if (ct == CancellationToken.None)
		{
			ct = @this.CancellationToken;
			return @this.StartNew(() => action(ct), ct, options, scheduler).Unwrap();
		}

		var cts = ct._LinkedCts(@this.CancellationToken);
		var toReturn = @this.StartNew(() => action(cts.Token), cts.Token, options, scheduler).Unwrap();

#pragma warning disable PH_P007 // Unused Cancellation Token
		_ = toReturn.ContinueWith(task => { cts.Dispose(); });
#pragma warning restore PH_P007 // Unused Cancellation Token


		return toReturn;
		//return @this.StartNew(action, ct, @this.CreationOptions | TaskCreationOptions.DenyChildAttach, @this.Scheduler ?? TaskScheduler.Default).Unwrap();
	}

#pragma warning restore PH_S014
}

public static unsafe class zz_Extensions_IntPtr
{
	public static T* _As<T>(this nint intPtr) where T : unmanaged
	{
		return (T*)intPtr;
	}
}

public static class zz_Extensions_HashSet
{
	/// <summary>
	/// return a new hashset excluding the elements in the other collection
	/// </summary>
	public static HashSet<T> _Except<T>(this HashSet<T> set, IEnumerable<T> other)
	{
		var result = new HashSet<T>(set);
		result.ExceptWith(other);
		return result;
	}

	/// <summary>
	/// returns a new hashset of all elements in both collections
	/// </summary>
	public static HashSet<T> _Union<T>(this HashSet<T> set, IEnumerable<T> other)
	{
		var result = new HashSet<T>(set);
		result.UnionWith(other);
		return result;
	}

	/// <summary>
	/// returns a new hashset of common elements between the two collections
	/// </summary>
	public static HashSet<T> _Intersection<T>(this HashSet<T> set, IEnumerable<T> other)
	{
		var result = new HashSet<T>(set);
		result.IntersectWith(other);
		return result;
	}

	public static HashSet<T> _Copy<T>(this HashSet<T> set)
	{
		return new HashSet<T>(set);
	}
}
public static class zz_Extensions_List
{
	/// <summary>
	/// get a copy of the list in a MemoryOwner_Custom, used to reduce allocations
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <returns></returns>
	public static MemoryOwner_Custom<T> _MemoryOwnerCopy<T>(this List<T> list)
	{
		if (list is null)
		{
			return MemoryOwner_Custom<T>.Empty;
		}
		var toReturn = MemoryOwner_Custom<T>.Allocate(list.Count);
		list._AsSpan_Unsafe().CopyTo(toReturn.Span);
		return toReturn;
	}

	private static ThreadLocal<Random> _rand = new(() => new Random());

	public static T _PickRandom<T>(this IList<T> target, Random? randomInstance = null)
	{
		randomInstance ??= _rand.Value;
		return target[randomInstance.Next(target.Count)];
	}

	public static bool _TryRemoveRandom<T>(this IList<T> target, out T value)
	{
		if (target.Count == 0)
		{
			value = default;
			return false;
		}

		var index = -1;
		//lock (_rand)
		{
			index = _rand.Value.Next(0, target.Count);
		}

		value = target[index];
		target.RemoveAt(index);
		return true;
	}

	public static List<T> _TakeAndRemove<T>(this List<T> list, int maxCount, bool takeFromStart = false)
	{
		maxCount = Math.Min(maxCount, list.Count);
		if (takeFromStart)
		{
			var toReturn = list.GetRange(0, maxCount).ToList();
			list.RemoveRange(0, maxCount);
			return toReturn;
		}
		else
		{
			var toReturn = list.GetRange(list.Count - maxCount, maxCount);
			list.RemoveRange(list.Count - maxCount, maxCount);
			return toReturn;
		}
	}

	public static void _TakeFrom<T>(this List<T> list, List<T> other, int maxCount, bool takeFromStart = false)
	{
		maxCount = Math.Min(maxCount, other.Count);
		if (takeFromStart)
		{
			for (var i = 0; i < maxCount; i++)
			{
				list.Add(other[i]);
			}

			other.RemoveRange(0, maxCount);
		}
		else
		{
			for (var i = other.Count - maxCount; i < other.Count; i++)
			{
				list.Add(other[i]);
			}

			other.RemoveRange(other.Count - maxCount, maxCount);
		}
	}

	public static bool _TryTakeLast<T>(this IList<T> target, out T value)
	{
		if (target.Count == 0)
		{
			value = default;
			return false;
		}

		var index = target.Count - 1;
		value = target[index];
		target.RemoveAt(index);
		return true;
	}

	public static void _RemoveLast<T>(this IList<T> target)
	{
		target.RemoveAt(target.Count - 1);
	}

	/// <summary>
	///    create a clone of this list.  individual value's should inherits from IClonable, or be structs with no references.
	///    (otherwise error)
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="source"></param>
	/// <returns></returns>
	public static List<TValue> _Clone<TValue>(this List<TValue> source)
	{
		//try to clone all values
		var toReturn = new List<TValue>(source.Count);

		foreach (var value in source)
		{
			__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);

			if (value is ICloneable cv)
			{
				toReturn.Add((TValue)cv.Clone());
			}
			else
			{
				toReturn.Add(value);
			}
		}

		return toReturn;
	}

	/// <summary>
	///    expands the list to the target capacity if it's not already, then sets the value at that index
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="target"></param>
	public static void _ExpandAndSet<T>(this IList<T> target, int index, T value)
	{
		while (target.Count <= index)
			target.Add(default);

		target[index] = value;
	}

	public static void _Randomize<T>(this IList<T> target)
	{
		//lock (_rand)
		{
			for (var index = 0; index < target.Count; index++)
			{
				var swapIndex = _rand.Value.Next(0, target.Count);
				var value = target[index];
				target[index] = target[swapIndex];
				target[swapIndex] = value;
			}
		}
	}

	/// <summary>
	///    true if all elements of the lists match.  can be out of order.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="target"></param>
	/// <param name="other"></param>
	/// <returns></returns>
	public static bool _ContainsIdential<T>(this List<T> target, List<T> other)
	{
		__.GetLogger()._EzError(other is not null && target is not null);
		if (other == null || target.Count != other.Count)
		{
			return false;
		}

		var span1 = target._AsSpan_Unsafe();
		var span2 = other._AsSpan_Unsafe();


		//look through all span1 for all matches
		for (var i = 0; i < span1.Length; i++)
		{
			var found = false;
			for (var j = 0; j < span2.Length; j++)
				if (Equals(span1[j], other[j]))
				{
					found = true;
					break;
				}

			if (found == false)
			{
				return false;
			}
		}


		//look through all span2 for all matches
		for (var i = 0; i < span2.Length; i++)
		{
			var found = false;
			for (var j = 0; j < span1.Length; j++)
				if (Equals(span1[j], other[j]))
				{
					found = true;
					break;
				}

			if (found == false)
			{
				return false;
			}
		}

		return true;
	}


	/// <summary>
	///    warning: do not modify list while enumerating span
	/// </summary>
	public static Span<T> _AsSpan_Unsafe<T>(this List<T> list)
	{
		return CollectionsMarshal.AsSpan(list);
	}

	public static ref T _GetRef<T>(this List<T> list, int index)
	{
		var span = list._AsSpan_Unsafe();
		return ref span[index];
	}
}

/// <summary>Extension methods for <see cref="TaskCompletionSource{TResult}" />.</summary>
/// <threadsafety static="true" instance="false" />
/// <remarks>
///    from:
///    https://github.com/tunnelvisionlabs/dotnet-threading/blob/3e99a9d13476a1e8224d81f282f3cedad143c1bc/Rackspace.Threading/TaskCompletionSourceExtensions.cs
/// </remarks>
public static class zz_Extensions_TaskCompletionSource
{
	/// <summary>Transfers the result of a <see cref="Task{TResult}" /> to a <see cref="TaskCompletionSource{TResult}" />.</summary>
	/// <remarks>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.RanToCompletion" /> state,
	///       the result of the task is assigned to the <see cref="TaskCompletionSource{TResult}" />
	///       using the <see cref="TaskCompletionSource{TResult}.SetResult(TResult)" /> method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Faulted" /> state,
	///       the unwrapped exceptions are bound to the <see cref="TaskCompletionSource{TResult}" />
	///       using the <see cref="TaskCompletionSource{TResult}.SetException(IEnumerable{Exception})" />
	///       method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Canceled" /> state,
	///       the <see cref="TaskCompletionSource{TResult}" /> is transitioned to the
	///       <see cref="TaskStatus.Canceled" /> state using the
	///       <see cref="TaskCompletionSource{TResult}.SetCanceled" /> method.
	///    </para>
	/// </remarks>
	/// <typeparam name="TSource">Specifies the result type of the source <see cref="Task{TResult}" />.</typeparam>
	/// <typeparam name="TResult">Specifies the result type of the <see cref="TaskCompletionSource{TResult}" />.</typeparam>
	/// <param name="taskCompletionSource">The <see cref="TaskCompletionSource{TResult}" /> instance.</param>
	/// <param name="task">The result task whose completion results should be transferred.</param>
	/// <exception cref="ArgumentNullException">
	///    <para>If <paramref name="taskCompletionSource" /> is <see langword="null" />.</para>
	///    <para>-or-</para>
	///    <para>If <paramref name="task" /> is <see langword="null" />.</para>
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	///    <para>If the underlying <see cref="Task{TResult}" /> of <paramref name="taskCompletionSource" /> was disposed.</para>
	/// </exception>
	/// <exception cref="InvalidOperationException">
	///    <para>
	///       If the underlying <see cref="Task{TResult}" /> produced by <paramref name="taskCompletionSource" /> is already
	///       in one of the three final states: <see cref="TaskStatus.RanToCompletion" />,
	///       <see cref="TaskStatus.Faulted" />, or <see cref="TaskStatus.Canceled" />.
	///    </para>
	/// </exception>
	public static void _SetFromTask<TSource, TResult>(this TaskCompletionSource<TResult> taskCompletionSource,
		Task<TSource> task)
		where TSource : TResult
	{
		if (taskCompletionSource == null)
		{
			throw new ArgumentNullException("taskCompletionSource");
		}

		if (task == null)
		{
			throw new ArgumentNullException("task");
		}

		switch (task.Status)
		{
			case TaskStatus.RanToCompletion:
				taskCompletionSource.SetResult(task.Result);
				break;

			case TaskStatus.Faulted:
				taskCompletionSource.SetException(task.Exception.InnerExceptions);
				break;

			case TaskStatus.Canceled:
				taskCompletionSource.SetCanceled();
				break;

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	public static void _SetFromTask(this TaskCompletionSource taskCompletionSource, Task task)
	{
		if (taskCompletionSource == null)
		{
			throw new ArgumentNullException("taskCompletionSource");
		}

		if (task == null)
		{
			throw new ArgumentNullException("task");
		}

		switch (task.Status)
		{
			case TaskStatus.RanToCompletion:
				taskCompletionSource.SetResult();
				break;

			case TaskStatus.Faulted:
				taskCompletionSource.SetException(task.Exception.InnerExceptions);
				break;

			case TaskStatus.Canceled:
				taskCompletionSource.SetCanceled();
				break;

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	/// <summary>
	///    Transfers the result of a <see cref="Task{TResult}" /> to a <see cref="TaskCompletionSource{TResult}" />,
	///    using a specified result value when the task is in the <see cref="TaskStatus.RanToCompletion" />
	///    state.
	/// </summary>
	/// <remarks>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.RanToCompletion" /> state,
	///       the specified <paramref name="result" /> value is assigned to the
	///       <see cref="TaskCompletionSource{TResult}" /> using the
	///       <see cref="TaskCompletionSource{TResult}.SetResult(TResult)" /> method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Faulted" /> state,
	///       the unwrapped exceptions are bound to the <see cref="TaskCompletionSource{TResult}" />
	///       using the <see cref="TaskCompletionSource{TResult}.SetException(IEnumerable{Exception})" />
	///       method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Canceled" /> state,
	///       the <see cref="TaskCompletionSource{TResult}" /> is transitioned to the
	///       <see cref="TaskStatus.Canceled" /> state using the
	///       <see cref="TaskCompletionSource{TResult}.SetCanceled" /> method.
	///    </para>
	/// </remarks>
	/// <typeparam name="TResult">Specifies the result type of the <see cref="TaskCompletionSource{TResult}" />.</typeparam>
	/// <param name="taskCompletionSource">The <see cref="TaskCompletionSource{TResult}" /> instance.</param>
	/// <param name="task">The result task whose completion results should be transferred.</param>
	/// <param name="result">The result of the completion source when the specified task completed successfully.</param>
	/// <exception cref="ArgumentNullException">
	///    <para>If <paramref name="taskCompletionSource" /> is <see langword="null" />.</para>
	///    <para>-or-</para>
	///    <para>If <paramref name="task" /> is <see langword="null" />.</para>
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	///    <para>If the underlying <see cref="Task{TResult}" /> of <paramref name="taskCompletionSource" /> was disposed.</para>
	/// </exception>
	/// <exception cref="InvalidOperationException">
	///    <para>
	///       If the underlying <see cref="Task{TResult}" /> produced by <paramref name="taskCompletionSource" /> is already
	///       in one of the three final states: <see cref="TaskStatus.RanToCompletion" />,
	///       <see cref="TaskStatus.Faulted" />, or <see cref="TaskStatus.Canceled" />.
	///    </para>
	/// </exception>
	public static void _SetFromTask<TResult>(this TaskCompletionSource<TResult> taskCompletionSource, Task task,
		TResult result)
	{
		switch (task.Status)
		{
			case TaskStatus.RanToCompletion:
				taskCompletionSource.SetResult(result);
				break;

			case TaskStatus.Faulted:
				taskCompletionSource.SetException(task.Exception!.InnerExceptions);
				break;

			case TaskStatus.Canceled:
				taskCompletionSource.SetCanceled();
				break;

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	/// <summary>
	///    Attempts to transfer the result of a <see cref="Task{TResult}" /> to a
	///    <see cref="TaskCompletionSource{TResult}" />.
	/// </summary>
	/// <remarks>
	///    <para>
	///       This method will return <see langword="false" /> if the <see cref="Task{TResult}" />
	///       provided by <paramref name="taskCompletionSource" /> is already in one of the three
	///       final states: <see cref="TaskStatus.RanToCompletion" />, <see cref="TaskStatus.Faulted" />,
	///       or <see cref="TaskStatus.Canceled" />. This method also returns <see langword="false" />
	///       if the underlying <see cref="Task{TResult}" /> has already been disposed.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.RanToCompletion" /> state,
	///       the result of the task is assigned to the <see cref="TaskCompletionSource{TResult}" />
	///       using the <see cref="TaskCompletionSource{TResult}.TrySetResult(TResult)" /> method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Faulted" /> state,
	///       the unwrapped exceptions are bound to the <see cref="TaskCompletionSource{TResult}" />
	///       using the <see cref="TaskCompletionSource{TResult}.TrySetException(IEnumerable{Exception})" />
	///       method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Canceled" /> state,
	///       the <see cref="TaskCompletionSource{TResult}" /> is transitioned to the
	///       <see cref="TaskStatus.Canceled" /> state using the
	///       <see cref="TaskCompletionSource{TResult}.TrySetCanceled" /> method.
	///    </para>
	/// </remarks>
	/// <typeparam name="TSource">Specifies the result type of the source <see cref="Task{TResult}" />.</typeparam>
	/// <typeparam name="TResult">Specifies the result type of the <see cref="TaskCompletionSource{TResult}" />.</typeparam>
	/// <param name="taskCompletionSource">The <see cref="TaskCompletionSource{TResult}" /> instance.</param>
	/// <param name="task">The result task whose completion results should be transferred.</param>
	/// <returns>
	///    <para><see langword="true" /> if the operation was successful.</para>
	///    <para>-or-</para>
	///    <para><see langword="false" /> if the operation was unsuccessful or the object has already been disposed.</para>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	///    <para>If <paramref name="taskCompletionSource" /> is <see langword="null" />.</para>
	///    <para>-or-</para>
	///    <para>If <paramref name="task" /> is <see langword="null" />.</para>
	/// </exception>
	public static bool _TrySetFromTask<TSource, TResult>(this TaskCompletionSource<TResult> taskCompletionSource,
		Task<TSource> task)
		where TSource : TResult
	{
		switch (task.Status)
		{
			case TaskStatus.RanToCompletion:
				return taskCompletionSource.TrySetResult(task.Result);

			case TaskStatus.Faulted:
				return taskCompletionSource.TrySetException(task.Exception.InnerExceptions);

			case TaskStatus.Canceled:
				return taskCompletionSource.TrySetCanceled();

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	public static bool _TrySetFromTask(this TaskCompletionSource taskCompletionSource, Task task)
	{
		switch (task.Status)
		{
			case TaskStatus.RanToCompletion:
				return taskCompletionSource.TrySetResult();

			case TaskStatus.Faulted:
				return taskCompletionSource.TrySetException(task.Exception.InnerExceptions);

			case TaskStatus.Canceled:
				return taskCompletionSource.TrySetCanceled();

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	/// <summary>
	///    Attempts to transfer the result of a <see cref="Task{TResult}" /> to a <see cref="TaskCompletionSource{TResult}" />,
	///    using a specified result value when the task is in the <see cref="TaskStatus.RanToCompletion" />
	///    state.
	/// </summary>
	/// <remarks>
	///    <para>
	///       This method will return <see langword="false" /> if the <see cref="Task{TResult}" />
	///       provided by <paramref name="taskCompletionSource" /> is already in one of the three
	///       final states: <see cref="TaskStatus.RanToCompletion" />, <see cref="TaskStatus.Faulted" />,
	///       or <see cref="TaskStatus.Canceled" />. This method also returns <see langword="false" />
	///       if the underlying <see cref="Task{TResult}" /> has already been disposed.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.RanToCompletion" /> state,
	///       the specified <paramref name="result" /> value is assigned to the
	///       <see cref="TaskCompletionSource{TResult}" /> using the
	///       <see cref="TaskCompletionSource{TResult}.TrySetResult(TResult)" /> method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Faulted" /> state,
	///       the unwrapped exceptions are bound to the <see cref="TaskCompletionSource{TResult}" />
	///       using the <see cref="TaskCompletionSource{TResult}.TrySetException(IEnumerable{Exception})" />
	///       method.
	///    </para>
	///    <para>
	///       If <paramref name="task" /> is in the <see cref="TaskStatus.Canceled" /> state,
	///       the <see cref="TaskCompletionSource{TResult}" /> is transitioned to the
	///       <see cref="TaskStatus.Canceled" /> state using the
	///       <see cref="TaskCompletionSource{TResult}.TrySetCanceled" /> method.
	///    </para>
	/// </remarks>
	/// <typeparam name="TResult">Specifies the result type of the <see cref="TaskCompletionSource{TResult}" />.</typeparam>
	/// <param name="taskCompletionSource">The <see cref="TaskCompletionSource{TResult}" /> instance.</param>
	/// <param name="task">The result task whose completion results should be transferred.</param>
	/// <param name="result">The result of the completion source when the specified task completed successfully.</param>
	/// <returns>
	///    <para><see langword="true" /> if the operation was successful.</para>
	///    <para>-or-</para>
	///    <para><see langword="false" /> if the operation was unsuccessful or the object has already been disposed.</para>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	///    <para>If <paramref name="taskCompletionSource" /> is <see langword="null" />.</para>
	///    <para>-or-</para>
	///    <para>If <paramref name="task" /> is <see langword="null" />.</para>
	/// </exception>
	public static bool _TrySetFromTask<TResult>(this TaskCompletionSource<TResult> taskCompletionSource, Task task,
		TResult result)
	{
		switch (task.Status)
		{
			case TaskStatus.RanToCompletion:
				return taskCompletionSource.TrySetResult(result);

			case TaskStatus.Faulted:
				return taskCompletionSource.TrySetException(task.Exception.InnerExceptions);

			case TaskStatus.Canceled:
				return taskCompletionSource.TrySetCanceled();

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	/// <summary>
	///    Transfers the result of a canceled or faulted <see cref="Task" /> to the
	///    <see cref="TaskCompletionSource{TResult}" />.
	/// </summary>
	/// <typeparam name="TResult">Specifies the type of the result.</typeparam>
	/// <param name="taskCompletionSource">The TaskCompletionSource.</param>
	/// <param name="task">The task whose completion results should be transferred.</param>
	public static void _SetFromFailedTask<TResult>(this TaskCompletionSource<TResult> taskCompletionSource, Task task)
	{
		switch (task.Status)
		{
			case TaskStatus.Faulted:
				taskCompletionSource.SetException(task.Exception.InnerExceptions);
				break;

			case TaskStatus.Canceled:
				taskCompletionSource.SetCanceled();
				break;

			case TaskStatus.RanToCompletion:
				throw new InvalidOperationException("Failed tasks must be in the Canceled or Faulted state.");

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}

	public static void _SetFromFailedTask(this TaskCompletionSource taskCompletionSource, Task task)
	{
		switch (task.Status)
		{
			case TaskStatus.Faulted:
				taskCompletionSource.SetException(task.Exception.InnerExceptions);
				break;

			case TaskStatus.Canceled:
				taskCompletionSource.SetCanceled();
				break;

			case TaskStatus.RanToCompletion:
				throw new InvalidOperationException("Failed tasks must be in the Canceled or Faulted state.");

			default:
				throw new InvalidOperationException("The task was not completed.");
		}
	}


	/// <summary>
	///    mark a task as complete when a cancellation token is cancelled
	///    optionally can instead set task to canceled when token is canceled.
	/// </summary>
	/// <param name="tcs"></param>
	/// <param name="cancellationToken"></param>
	/// <param name="cancelDoNotComplete">
	///    default false.  set to true to cancel the task when the ct cancels.  otherwise marks
	///    task as complete
	/// </param>
	/// <param name="useSynchronizationContext">default false</param>
	public static CancellationTokenRegistration _SetFromCancellationToken(this TaskCompletionSource tcs,
		CancellationToken cancellationToken,
		bool cancelDoNotComplete = false, bool useSynchronizationContext = false)
	{
		var ctr = cancellationToken.Register(() =>
		{
			if (cancelDoNotComplete)
			{
				tcs.TrySetCanceled(cancellationToken);
			}
			else
			{
				tcs.TrySetResult();
			}
		}, useSynchronizationContext);

		return ctr;
	}
}

/// <summary>
///    The included numeric extension methods utilize experimental CLR behavior to allow generic numerical operations.
///    Might work great, might have hidden perf costs?
/// </summary>
public static class zz_Extensions_Numeric
{
	//public static T _Max<T>(this T value, T other) where T : IComparable<T>
	//{
	//	return value.CompareTo(other) >= 0 ? value : other;

	//}
	//public static T _Min<T>(this T value, T other) where T : IComparable<T>
	//{
	//	return value.CompareTo(other) <= 0 ? value : other;

	//}


	public static bool _Between<T>(this T value, T lowerInclusive, T upperExclusive) where T : INumber<T>
	{
		return value >= lowerInclusive && value < upperExclusive;
	}
	public static bool _AproxEqual<T>(this T value, T other, T tolerance) where T : IFloatingPoint<T>
	{
		// Handle NaN (Not a Number)
		if (T.IsNaN(value) || T.IsNaN(other))
		{
			return false; // NaN is not equal to anything, including itself
		}

		// Handle infinity (both positive and negative)
		if (T.IsInfinity(value) || T.IsInfinity(other))
		{
			return value == other; // Only return true if both are exactly the same infinity
		}
		return T.Abs(value - other) <= tolerance;
	}
	public static bool _AproxEqual(this double value, double other)
	{
		var tolerance = Math.Max(Math.Max(Math.Abs(value), Math.Abs(other)), 1d) * double.Epsilon;
		return value._AproxEqual(other, tolerance);
	}
	public static bool _AproxEqual(this float value, float other)
	{
		var tolerance = Math.Max(Math.Max(Math.Abs(value), Math.Abs(other)), 1f) * float.Epsilon;
		return value._AproxEqual(other, tolerance);
	}
	public static T _Round<T>(this T value, int digits, MidpointRounding mode = MidpointRounding.AwayFromZero)
		where T : IFloatingPoint<T>
	{
		return T.Round(value, digits, mode);
	}

	/// <summary>
	/// Extension method to round the float to the nearest specified increment
	/// </summary>
	public static T _RoundToNearest<T>(this T number, T increment)
		where T : IFloatingPoint<T>
	{
		if (increment == T.Zero)
		{
			return T.Round(number);
		}

		T multiplier = T.One / increment;
		return T.Round(number * multiplier) / multiplier;
	}

	public static T _Abs<T>(this T value)
		where T : IFloatingPoint<T>
	{
		return T.Abs(value);
	}
	public static int _Sign<T>(this T value)
		where T : IFloatingPoint<T>
	{

		return T.Sign(value);
	}
	public static T _CopySign<T>(this T value, T other)
		where T : IFloatingPoint<T>
	{

		return T.CopySign(value, other);
	}

	public static T _Ceiling<T>(this T value)
		where T : IFloatingPoint<T>
	{

		return T.Ceiling(value);
	}
	public static T _Floor<T>(this T value)
		where T : IFloatingPoint<T>
	{


		return T.Floor(value);
	}
	public static T _Clamp<T>(this T value, T min, T max)
		where T : IFloatingPoint<T>
	{


		return T.Clamp(value, min, max);
	}
	public static T _Max<T>(this T value, T other) where T : IFloatingPoint<T>
	{
		return T.Max(value, other);

	}
	public static T _Min<T>(this T value, T other) where T : IFloatingPoint<T>
	{
		return T.Min(value, other);
	}

	public static bool _TryParse<T>(this string toParse, out T value) where T : IFloatingPoint<T>
	{
		return T.TryParse(toParse, null, out value);

	}
	public static T _Truncate<T>(this T value) where T : IFloatingPoint<T>
	{
		return T.Truncate(value);
	}

	public static float _AsFloat<T>(this T value) where T : IFloatingPoint<T>
	{
		// Check if T is a floating-point type and convert accordingly
		if (value is float f)
		{
			return f;
		}
		else if (value is double d)
		{
			return (float)d;
		}
		else if (value is decimal m)
		{
			return (float)m;
		}
		else
		{
			throw new InvalidOperationException("Unsupported floating-point type.");
		}
	}

	public static T _SubtractTowardsZero<T>(this T value, T amount) where T : INumber<T>
	{
		amount = T.Abs(amount);
		//if (Y < 0)
		//{
		//	throw new ArgumentException("Y must be positive", nameof(Y));
		//}

		if (value > T.Zero)
		{
			return T.Max(T.Zero, value - amount);
		}
		else if (value < T.Zero)
		{
			return T.Min(T.Zero, value + amount);
		}
		else
		{
			return T.Zero;
		}
	}
}

public static class zz_Extensions_IEnumerable
{

	public static bool _Find<TBase, TDerived>(this IEnumerable<TBase> enumerable, Predicate<TDerived> match) where TDerived : TBase
	{
		return enumerable.Any((value) =>
		{
			if (value is TDerived derived)
			{
				return match(derived);
			}
			return false;
		});
	}




	/// <summary>
	///    create a clone of this iEnumerable as a list.  individual value's should inherits from IClonable, or be structs with
	///    no references. (otherwise error)
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="source"></param>
	/// <returns></returns>
	public static List<TValue> _CloneElements<TValue>(this IEnumerable<TValue> source)
	{
		//try to clone all values
		var toReturn = new List<TValue>();

		foreach (var value in source)
		{
			__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);

			if (value is ICloneable cv)
			{
				toReturn.Add((TValue)cv.Clone());
			}
			else
			{
				toReturn.Add(value);
			}
		}

		return toReturn;
	}


	///// <summary>
	/////    executes and awaits sequentially
	///// </summary>
	//public static async ValueTask _ForEach<TValue>(this IEnumerable<TValue> enumerable,
	//	Func<TValue, ValueTask> asyncAction)
	//{
	//	foreach (var val in enumerable)
	//	{
	//		await asyncAction(val);
	//	}
	//}

	/// <summary>
	///    executes and awaits sequentially
	/// </summary>
	public static async Task _ForEach<TValue>(this IEnumerable<TValue> enumerable, Func<TValue, ValueTask> asyncAction)
	{
		foreach (var val in enumerable)
		{
			await asyncAction(val);
		}
	}

	//public static bool _TryGet<TBase, TDerived>(this IEnumerable<TBase> enumerable, Func<TDerived, bool> predicate,
	//   [NotNullWhen(true)] out TDerived? value)
	//   where TDerived : TBase
	//{
	//   foreach (var val in enumerable)
	//   {
	//      if (val is TDerived v && predicate(v))
	//      {
	//         value = v;
	//         return true;
	//      }
	//   }

	//   value = default!;
	//   return false;
	//}
	public static bool _TryGet<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, [NotNullWhen(true)] out T? value)
	{
		foreach (var val in enumerable)
		{
			if (predicate(val))
			{
				value = val!;
				return true;
			}
		}

		value = default!;
		return false;
	}


	public static Dictionary<TKey, TValue> _ToDictionary<TKey, TValue>(
		this IEnumerable<KeyValuePair<TKey, TValue>> source, bool tryCloneValues = false) where TKey : notnull
	{
		if (tryCloneValues)
		{
			//try to clone all values
			var dict = new Dictionary<TKey, TValue>();
			foreach (var kvp in source)
			{
				var val = kvp.Value;
				val = val is ICloneable c ? (TValue)c.Clone() : val;
				dict.Add(kvp.Key, val);
			}

			return dict;
		}

		return source.ToDictionary(x => x.Key, x => x.Value);
	}

	public static string _ToStringAll<T>(this IEnumerable<T> source, string? seperator = ", ")
	{
		var sb = __.pool.Get<StringBuilder>();
		__.GetLogger()._EzError(sb.Length == 0, "StringBuilder should be empty");

		var count = 0;
		foreach (var s in source)
		{
			count++;
			sb.Append(s);
			sb.Append(seperator);
		}

		sb.Append(']');

		var toReturn = $"count={count}[" + sb;
		sb.Clear();
		__.pool.Return(sb);
		return toReturn;
	}


	public static string _Join(this IEnumerable<string> source, string? seperator = null)
	{
		return string.Join(seperator, source);
	}


	/// <summary>
	///    wrapper over normal `.Select()`, but will return an empty collection if the target source is null.
	/// </summary>
	public static IEnumerable<TResult> _Select<TSource, TResult>(
		this IEnumerable<TSource> source, Func<TSource, TResult> selector)
	{
		__.GetLogger()._EzError(source is not null);
		if (source is null)
		{
			return Array.Empty<TResult>();
		}

		return source.Select(selector);
	}

	/// <summary>
	///    obtain a random number of elements from the target collection.   will not return dupes.
	/// </summary>
	public static List<T> _TakeRandom<T>(this IEnumerable<T> collection, int count)
	{
		var rand = new Random();
		var temp = collection.ToList();

		if (count > temp.Count())
		{
			throw new ArgumentOutOfRangeException(
				$"collection count is {temp.Count()} but you are trying to take {count}.");
		}

		List<T> toReturn = new();
		for (var i = 0; i < count; i++)
		{
			var index = rand.Next(temp.Count);
			toReturn.Add(temp[index]);
			temp.RemoveAt(index);
		}

		return toReturn;
	}

	public static T _Sum<T>(this IEnumerable<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
	{
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			toReturn += val;
		}

		return toReturn;
	}

	public static T _Avg<T>(this IEnumerable<T> values)
		where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, float, T>
	{
		var count = 0;
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			count++;
			toReturn += val;
		}

		return toReturn / count;
	}

	public static T _Min<T>(this IEnumerable<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T, bool>
	{
		var toReturn = T.MaxValue;

		foreach (var val in values)
		{
			if (toReturn > val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}

	public static T _Max<T>(this IEnumerable<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T, bool>
	{
		var toReturn = T.MinValue;

		foreach (var val in values)
		{
			if (toReturn < val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}


	public static TResult _Max<TSource, TResult>(this IEnumerable<TSource> values, Func<TSource, TResult> selector, TResult defaultValue = default)
		where TResult : IMinMaxValue<TResult>, IComparisonOperators<TResult, TResult, bool>
	{

		int results = 0;
		var toReturn = TResult.MinValue;

		foreach (var val in values)
		{
			results++;
			var result = selector(val);
			if (toReturn < result)
			{
				toReturn = result;
			}
		}

		if (results == 0)
		{
			return defaultValue;
		}

		return toReturn;
	}
	public static TResult _Min<TSource, TResult>(this IEnumerable<TSource> values, Func<TSource, TResult> selector, TResult defaultValue = default)
		where TResult : IMinMaxValue<TResult>, IComparisonOperators<TResult, TResult, bool>
	{

		int results = 0;
		var toReturn = TResult.MaxValue;

		foreach (var val in values)
		{
			results++;
			var result = selector(val);
			if (toReturn > result)
			{
				toReturn = result;
			}
		}

		if (results == 0)
		{
			return defaultValue;
		}

		return toReturn;
	}
}

public static class zz_Extensions_Dictionary
{
	/// <summary>
	///    create a clone of this dictionary and attempts to clone all key/values
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="source"></param>
	/// <returns></returns>
	public static Dictionary<TKey, TValue> _Clone<TKey, TValue>(this Dictionary<TKey, TValue> source)
	{
		//try to clone all values
		var toReturn = new Dictionary<TKey, TValue>(source.Count());
		foreach (var kvp in source)
		{
			var value = kvp.Value;

			__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);

			var key = kvp.Key is ICloneable ck ? (TKey)ck.Clone() : kvp.Key;
			var val = value is ICloneable cv ? (TValue)cv.Clone() : value;
			toReturn.Add(key, val);
		}

		return toReturn;
	}

	public static TDerived _Get<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key)
		where TDerived : TBase
		where TKey : notnull
	{
		return (TDerived)dict[key]!;
	}

	public static bool _TryGetValue<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key,
		out TDerived? value) where TDerived : TBase
		where TKey : notnull
	{
		if (dict.TryGetValue(key, out var baseValue))
		{
			value = (TDerived)baseValue;
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	/// get the value (if it exists) and remove it from the dictionary
	/// </summary>
	public static bool _Remove<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key,
		out TDerived? value) where TDerived : TBase
		where TKey : notnull
	{
		if (dict.Remove(key, out var baseValue))
		{
			value = (TDerived)baseValue;
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	///    returns string with count followed by contents in format:  "count=N [(key1,val1) (key2,val2) ]"
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="dict"></param>
	/// <returns></returns>
	public static string _ToStringAll<TKey, TValue>(this IDictionary<TKey, TValue> dict)
	{
		//var sb = new StringBuilder();//  __.pool.Get<StringBuilder>();
		var sb = __.pool.Get<StringBuilder>();
		sb.Append($"count={dict.Count} [");
		foreach (var pair in dict)
		{
			sb.Append($"({pair.Key},{pair.Value}) ");
		}

		sb.Append("]");

		var toReturn = sb.ToString();
		sb.Clear();
		__.pool.Return(sb);
		return toReturn;
	}

	//  public static TValue _GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> onAddNew)
	//     where TKey : notnull
	//  {
	//     if (!dict.TryGetValue(key, out var value))
	//     {
	//        value = onAddNew();
	//        dict.Add(key, value);
	//     }
	//	return value;
	//}

	public static TDerived _GetOrAdd<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key, Func<TDerived> onAddNew) where TDerived : TBase
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = onAddNew();
			dict.Add(key, value);
		}

		return (TDerived)value!;
	}
	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TValue _GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new()
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TValue();
			dict.Add(key, value);
		}

		return ((TValue)value!);
	}

	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TDerived _GetOrNew<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key) where TDerived : TBase, new()
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TDerived();
			dict.Add(key, value);
		}

		return ((TDerived)value!);
	}
	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TDerived _GetOrNew<TDerived>(this IDictionary<string, object> dict, string key) where TDerived : new()
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TDerived();
			dict.Add(key, value);
		}

		return ((TDerived)value!);
	}

	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TDerived _GetOrNew<TDerived>(this IDictionary<object, object> dict, object key) where TDerived : new()
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TDerived();
			dict.Add(key, value);
		}

		return ((TDerived)value!);
	}

	/// <summary>
	/// if key doesn't exist, returns default of TDerived without adding to the dict.
	/// <para>default value is determined by the TDerived generic you pass in</para>
	/// </summary>
	public static TDerived _GetOrDefault<TDerived>(this IDictionary<string, object> dict, string key)
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = default(TDerived);
		}

		return ((TDerived)value!);
	}

	/// <summary>
	/// if key doesn't exist, returns default of TDerived without adding to the dict.
	/// <para>default value is determined by the TDerived generic you pass in</para>
	/// </summary>
	public static TDerived _GetOrDefault<TDerived>(this IDictionary<object, object> dict, object key)
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = default(TDerived);
		}
		return ((TDerived)value!);
	}
	/// <summary>
	/// if key doesn't exist, returns default of TDerived without adding to the dict.
	/// <para>default value is defined by the func you pass in</para>
	/// </summary>
	public static TDerived _GetOrDefault<TKey, TValue, TDerived>(this IDictionary<TKey, TValue> dict, TKey key, Func<TDerived> _default) where TDerived : TValue
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = _default();
		}

		return ((TDerived)value!);
	}

	public static bool _TryRemove<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, out TValue value)
		where TKey : notnull
	{
		var toReturn = dict.TryGetValue(key, out value);
		if (toReturn)
		{
			dict.Remove(key);
		}

		return toReturn;
	}

	/// <summary>
	///    get by reference!   ref returns allow efficient storage of structs in dictionaries
	///    These are UNSAFE in that further modifying (adding/removing) the dictionary while using the ref return will break
	///    things!
	/// </summary>
	public static ref TValue _GetValueRef_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key,
		out bool exists)
		where TKey : notnull
		where TValue : struct
	{
		ref var toReturn = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
		exists = Unsafe.IsNullRef(ref toReturn) == false;
		return ref toReturn;
	}

	/// <summary>
	///    get by reference!   ref returns allow efficient storage of structs in dictionaries
	///    These are UNSAFE in that further modifying (adding/removing) the dictionary while using the ref return will break
	///    things!
	/// </summary>
	public static ref TValue _GetValueRef_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
		where TKey : notnull
		where TValue : struct
	{
		return ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
	}
	//public static ref TValue _GetValueRefOrAddDefault_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, out bool exists)
	//	where TKey : notnull
	//	where TValue : struct
	//{

	//	ref var toReturn = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out var existCopy);
	//	exists = existCopy;
	//	return ref toReturn;
	//}

	public static ref TValue _GetValueRefOrAddDefault_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dictionary,
		TKey key, out bool exists)
		where TKey : notnull
		where TValue : struct
	{
		return ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out exists);
	}

	public static unsafe ref TValue _GetValueRefOrAddDefault_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict,
		TKey key)
		where TKey : notnull
		where TValue : struct
	{
		bool exists;
		return ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out *&exists);
	}
	//	//below is bad pattern:  instead just set the ref returned value to the new.  (avoid struct copy)
	//	public static unsafe ref TValue _GetValueRefOrAdd_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func_Ref<TValue> onAddNew) 
	//		where TKey : notnull
	//		where TValue : struct
	//	{		
	//		bool exists;
	//		ref var toReturn = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out *&exists);
	//		if (exists != true)
	//		{
	//			ref var toAdd = ref onAddNew();
	//			dict.Add(key, toAdd);
	//#if DEBUG
	//			toReturn = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out *&exists);
	//			__.GetLogger()._EzError(exists);
	//#else
	//			toReturn = ref dict._GetValueRef_Unsafe(key);
	//#endif		
	//		}
	//		return ref toReturn;
	//	}
}

/// <summary>
///    The included numeric extension methods utilize experimental CLR behavior to allow generic numerical operations.
///    Might work great, might have hidden perf costs?
/// </summary>
public static class zz_Extensions_Span
{
	[ThreadStatic] private static Random _rand = new();

	public static bool _Overlaps<T>(this Span<T> span, HashSet<T> other)
	{
		foreach (var value in span)
		{
			if (other.Contains(value))
			{
				return true;
			}
		}
		return false;
	}

	public static Span<T> _Reverse<T>(this Span<T> span)
	{
		span.Reverse();
		return span;
	}
	public static List<T> _ToList<T>(this Span<T> span)
	{
		return span.ToArray().ToList();
	}


	public static TOut _Aggregate<TIn, TOut>(this Span<TIn> source, TOut seedValue,
		Func<TIn, TOut, TOut> handler)
	{
		return source._AsReadOnly()._Aggregate(seedValue, handler);
	}

	public static TOut _Aggregate<TIn, TOut>(this ReadOnlySpan<TIn> source, TOut seedValue,
		Func<TIn, TOut, TOut> accumFunc)
	{
		var accumulation = seedValue;
		foreach (var value in source)
		{
			accumulation = accumFunc(value, accumulation);
		}

		return accumulation;
	}


	/// <summary>
	/// like ._Randomize() but the swaps are deterministic psudorandom.
	/// <para>useful for rearanging values while still having a deterministic order</para>
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="target"></param>
	public static void _Swizzle<T>(this Span<T> target)//, long? primeToUse=null)
	{
		var length = target.Length;
		if (length <= 1) return;
		int prime = 3; //primeToUse ?? LoLo.PrimeFinder.FindLargestPrimeLessThan(length-1); //(long)Math.Pow(2 , 31) - 1;


		for (var index = 0; index < length; index++)
		{
			int swapIndex = (index + 1) * prime % length;// Deterministic swapping pattern
			(target[index], target[swapIndex]) = (target[swapIndex], target[index]);
		}

		//var index = 0;
		//for (var i = 0; i < length; i++)
		//{
		//   int swapIndex = (int)(((index +1) * prime) % length);// Deterministic swapping pattern
		//   (target[index], target[swapIndex]) = (target[swapIndex], target[index]);
		//   index = (swapIndex+i)%length;
		//}
	}

	//public static int[] _SwizzleTest(this int[] values)
	//{
	//   var prime = 2 ^ 31 - 1;
	//   var length = values.Length;
	//   //size in bits
	//   values = values._Clone().ToArray();
	//   var targetIndex = 0;
	//   for (int i = 0; i < length; i++)
	//   {
	//      var swapIndex = (((i + 1) * prime)) % length; // Deterministic swapping pattern

	//      (values[i], values[swapIndex]) = (values[swapIndex], values[i]);

	//      //// Swapping bits at i and swapIndex if they are different
	//      //if (((value >> i) & 1) != ((value >> swapIndex) & 1))
	//      //{
	//      //   // Toggle bits at i and swapIndex
	//      //   value ^= (1 << i) | (1 << swapIndex);
	//      //}

	//      targetIndex++;
	//   }
	//   return values;
	//}


	public static void _Randomize<T>(this Span<T> target)
	{
		if (target.Length <= 1) return;
		//lock (_rand)
		{
			for (var index = 0; index < target.Length; index++)
			{
				var swapIndex = _rand.Next(0, target.Length);
				(target[index], target[swapIndex]) = (target[swapIndex], target[index]);
			}
		}
	}

	public static bool _IsSorted<T>(this Span<T> target) where T : IComparable<T>
	{
		if (target.Length < 2)
		{
			return true;
		}

		var isSorted = true;

		ref var previous = ref target[0]!;
		for (var i = 1; i < target.Length; i++)
		{
			if (previous.CompareTo(target[i]) > 0) //ex: 1.CompareTo(2) == -1
			{
				return false;
			}

			previous = ref target[i]!;
		}

		return true;


		//using var temp= SpanGuard<T>.Allocate(target.Length);
		//var tempSpan = temp.Span;
		//tempSpan.Sort(target, (first, second) => {
		//	var result = first.CompareTo(second);
		//	if(result < 0)
		//	{
		//		isSorted = false;
		//	}
		//	return result;
		//});
		//return isSorted;		
	}


	/// <summary>
	///    returns true if both spans starting address in memory is the same.  Different length and/or type is ignored.
	/// </summary>
	public static unsafe bool _ReferenceEquals<T1, T2>(ref this Span<T1> target, ref Span<T2> other)
		where T1 : unmanaged where T2 : unmanaged
	{
		fixed (T1* pSpan1 = target)
		{
			fixed (T2* pSpan2 = other)
			{
				return pSpan1 == pSpan2;
			}
		}
	}

	/// <summary>
	///    cast this span as another.  Any extra bytes remaining are ignored (the number of bytes in the castTo may be smaller
	///    than the original)
	/// </summary>
	public static Span<TTo> _CastAs<TFrom, TTo>(ref this Span<TFrom> target)
		where TFrom : unmanaged where TTo : unmanaged
	{
		return MemoryMarshal.Cast<TFrom, TTo>(target);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<T> _AsReadOnly<T>(this Span<T> span)
	{
		return span;
	}


	/// <summary>
	///    important implementation notes, be sure to read
	///    https://docs.microsoft.com/en-us/windows/communitytoolkit/high-performance/parallelhelper
	/// </summary>
	/// <typeparam name="TData"></typeparam>
	private readonly unsafe struct _ParallelForEach_ActionHelper<TData> : IAction where TData : unmanaged
	{
		public readonly TData* pSpan;
		public readonly Action_Ref<TData, int> parallelAction;

		public _ParallelForEach_ActionHelper(TData* pSpan, Action_Ref<TData, int> parallelAction)
		{
			this.pSpan = pSpan;
			this.parallelAction = parallelAction;
		}

		public void Invoke(int index)
		{
			//Using delegate pointer invoke, Because action is a readonly field,
			//but Invoke is an interface method where the compiler can't see it's actually readonly in all implementing types,
			//so it emits a defensive copies. This skips that 
			Unsafe.AsRef(in parallelAction).Invoke(ref pSpan[index], ref index);
		}
	}

	private readonly unsafe struct _ParallelForEach_ActionHelper_OutputSpan<TData, TOutput> : IAction
		where TData : unmanaged where TOutput : unmanaged
	{
		public readonly TData* pSpan;
		public readonly TOutput* pOutput;
		public readonly Action_Ref<TData, TOutput, int> parallelAction;

		public _ParallelForEach_ActionHelper_OutputSpan(TData* pSpan, TOutput* pOutput,
			Action_Ref<TData, TOutput, int> parallelAction)
		{
			this.pSpan = pSpan;
			this.pOutput = pOutput;
			this.parallelAction = parallelAction;
		}

		public void Invoke(int index)
		{
			//Using delegate pointer invoke, Because action is a readonly field,
			//but Invoke is an interface method where the compiler can't see it's actually readonly in all implementing types,
			//so it emits a defensive copies. This skips that 
			Unsafe.AsRef(in parallelAction).Invoke(ref pSpan[index], ref pOutput[index], ref index);
		}
	}

	private readonly unsafe struct _ParallelForEach_ActionHelper_FunctionPtr<TData> : IAction where TData : unmanaged
	{
		public readonly TData* pSpan;
		public readonly delegate*<ref TData, ref int, void> parallelAction;

		public _ParallelForEach_ActionHelper_FunctionPtr(TData* pSpan, delegate*<ref TData, ref int, void> parallelAction)
		{
			this.pSpan = pSpan;
			this.parallelAction = parallelAction;
		}

		public void Invoke(int index)
		{
			//Using delegate pointer invoke, Because action is a readonly field,
			//but Invoke is an interface method where the compiler can't see it's actually readonly in all implementing types,
			//so it emits a defensive copies. This skips that 
			//Unsafe.AsRef(parallelAction).Invoke(ref pSpan[index], ref index);
			parallelAction(ref pSpan[index], ref index);
		}
	}

	public static unsafe void _ParallelForEach<TData>(this Span<TData> inputSpan, Action_Ref<TData, int> parallelAction)
		where TData : unmanaged
	{
		fixed (TData* pSpan = inputSpan)
		{
			var actionStruct = new _ParallelForEach_ActionHelper<TData>(pSpan, parallelAction);
			ParallelHelper.For(0, inputSpan.Length, in actionStruct);
		}
	}

	public static unsafe void _ParallelForEach<TData>(this Span<TData> inputSpan,
		delegate*<ref TData, ref int, void> parallelAction) where TData : unmanaged
	{
		fixed (TData* pSpan = inputSpan)
		{
			var actionStruct = new _ParallelForEach_ActionHelper_FunctionPtr<TData>(pSpan, parallelAction);
			ParallelHelper.For(0, inputSpan.Length, in actionStruct);
		}
	}

	public static unsafe void _ParallelForEach<TData, TOutput>(this Span<TData> inputSpan, Span<TOutput> outputSpan,
		Action_Ref<TData, TOutput, int> parallelAction) where TData : unmanaged where TOutput : unmanaged
	{
		fixed (TData* pSpan = inputSpan)
		{
			fixed (TOutput* pOutput = outputSpan)
			{
				var actionStruct =
					new _ParallelForEach_ActionHelper_OutputSpan<TData, TOutput>(pSpan, pOutput, parallelAction);
				ParallelHelper.For(0, inputSpan.Length, in actionStruct);
			}
		}
	}

	///////// <summary>
	///////// do work in parallel over the span.  each parallelAction will operate over a segment of the span
	///////// </summary>
	//////public static unsafe void _ParallelFor<TData>(this Span<TData> inputSpan, int parallelCount, Action_Span<TData> parallelAction) where TData : unmanaged
	//////{
	//////	var length = inputSpan.Length;
	//////	fixed (TData* p = inputSpan)
	//////	{
	//////		var pSpan = p; //need to stop compiler complaint

	//////		Parallel.For(0, parallelCount + 1, (index) => { //plus one to capture remainder

	//////			var count = length / parallelCount;
	//////			var startIndex = index * count;
	//////			var endIndex = startIndex + count;
	//////			if (endIndex > length)
	//////			{
	//////				endIndex = length;
	//////				count = endIndex - startIndex; //on last loop, only do remainder
	//////			}

	//////			var spanPart = new Span<TData>(&pSpan[startIndex], count);

	//////			parallelAction(spanPart);

	//////		});
	//////	}
	//////}
	///////// <summary>
	///////// do work in parallel over the span.  each parallelAction will operate over a segment of the span
	///////// </summary>
	//////public static unsafe void _ParallelForRange<TData>(this ReadOnlySpan<TData> inputSpan, int parallelCount, Action_RoSpan<TData> parallelAction) where TData : unmanaged
	//////{

	//////	var partition = System.Collections.Concurrent.Partitioner.Create(0, inputSpan.Length);

	//////	inputSpan.s


	//////	__.GetLogger()._EzError(false, "needs verification of algo.  probably doesn't partition properly");
	//////	var length = inputSpan.Length;
	//////	fixed (TData* p = inputSpan)
	//////	{
	//////		var pSpan = p;

	//////		Parallel.For(0, parallelCount + 1, (index) => { //plus one to capture remainder

	//////			var count = length / parallelCount;
	//////			var startIndex = index * count;
	//////			var endIndex = startIndex + count;
	//////			if (endIndex > length)
	//////			{
	//////				endIndex = length;
	//////				count = endIndex - startIndex; //on last loop, only do remainder
	//////			}

	//////			var spanPart = new ReadOnlySpan<TData>(&pSpan[startIndex], count);

	//////			parallelAction(spanPart);

	//////		});
	//////	}
	//////}

	///// <summary>
	///// get ref to item at index 0
	///// </summary>
	//public static ref T _GetRef<T>(this Span<T> span)
	//{		
	//	return System.Runtime.InteropServices.MemoryMarshal.GetReference(span);
	//}

#if GENERIC_MATH
	//// GENERIC MATH  requires System.Runtime.Experimental nuget package matching the current DotNet runtime.
	public static T _Sum<T>(this Span<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
	{
		return values._AsReadOnly()._Sum();
	}
	public static T _Sum<T>(this ReadOnlySpan<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
	{
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			toReturn += val;
		}
		return toReturn;
	}

	public static T _Avg<T>(this Span<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, float, T>
	{
		return values._AsReadOnly()._Avg();
	}
	public static T _Avg<T>(this ReadOnlySpan<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, float, T>
	{
		var count = 0;
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			count++;
			toReturn += val;
		}
		return toReturn / count;
	}
	public static T _Min<T>(this Span<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
	{
		return values._AsReadOnly()._Min();
	}
	public static T _Min<T>(this ReadOnlySpan<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
	{
		var toReturn = T.MaxValue;

		foreach (var val in values)
		{
			if (toReturn > val)
			{
				toReturn = val;
			}
		}
		return toReturn;
	}
	public static T _Max<T>(this Span<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
	{
		return values._AsReadOnly()._Max();
	}
	public static T _Max<T>(this ReadOnlySpan<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
	{
		var toReturn = T.MinValue;

		foreach (var val in values)
		{
			if (toReturn < val)
			{
				toReturn = val;
			}
		}
		return toReturn;
	}
	//MISSING GENERIC MATH  requires System.Runtime.Experimental nuget package matching the current DotNet runtime.
#endif
	public static float _Sum(this Span<float> values)
	{
		return values._AsReadOnly()._Sum();
	}

	public static float _Sum(this ReadOnlySpan<float> values)
	{
		float toReturn = 0;
		foreach (var val in values)
		{
			toReturn += val;
		}

		return toReturn;
	}

	public static TimeSpan _Sum(this Span<TimeSpan> values)
	{
		return values._AsReadOnly()._Sum();
	}

	public static TimeSpan _Sum(this ReadOnlySpan<TimeSpan> values)
	{
		TimeSpan toReturn = TimeSpan.Zero;
		foreach (var val in values)
		{
			toReturn += val;
		}

		return toReturn;
	}

	public static float _Avg(this Span<float> values)
	{
		return values._AsReadOnly()._Avg();
	}

	public static float _Avg(this ReadOnlySpan<float> values)
	{
		var count = 0;
		var toReturn = 0f;
		foreach (var val in values)
		{
			count++;
			toReturn += val;
		}

		return toReturn / count;
	}

	public static float _Min(this Span<float> values)
	{
		return values._AsReadOnly()._Min();
	}

	public static float _Min(this ReadOnlySpan<float> values)
	{
		var toReturn = float.MaxValue;

		foreach (var val in values)
		{
			if (toReturn > val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}

	public static float _Max(this Span<float> values)
	{
		return values._AsReadOnly()._Max();
	}

	public static float _Max(this ReadOnlySpan<float> values)
	{
		var toReturn = float.MinValue;

		foreach (var val in values)
		{
			if (toReturn < val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}

	public static bool _Contains<T>(this Span<T> values, T toFind) where T : class
	{
		foreach (var val in values)
		{
			if (val == toFind)
			{
				return true;
			}
		}

		return false;
	}

	public static bool _Contains<T>(this ReadOnlySpan<T> values, T toFind) where T : class
	{
		foreach (var val in values)
		{
			if (val == toFind)
			{
				return true;
			}
		}

		return false;
	}
}


public static class zz_Extensions_IntLong
{
	public static int _InterlockedIncrement(ref this int value)
	{
		return Interlocked.Increment(ref value);
	}

	public static long _InterlockedIncrement(ref this long value)
	{
		return Interlocked.Increment(ref value);
	}

	public static uint _InterlockedIncrement(ref this uint value)
	{
		return Interlocked.Increment(ref value);
	}

	public static ulong _InterlockedIncrement(ref this ulong value)
	{
		return Interlocked.Increment(ref value);
	}

	//public static void _Unpack(this long value, out int first, out int second)
	//{
	///////doesn't quite work with 2nd value.   need to look at bitmasking code
	//	first =(int)(value >> 32);
	//	second =(int)(value);
	//}

	/// <summary>
	/// rearange the bits in a deterministic way.  useful for spreading out significant bits so they are not clustered
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	public static long _Swizzle(this long value)
	{
		//size in bits
		var length = sizeof(long) * 8; //64
		var prime = 3;
		for (int i = 0; i < length; i++)
		{
			int swapIndex = (i + 1) * prime % length; // Deterministic swapping pattern

			// Swapping bits at i and swapIndex if they are different
			if ((value >> i & 1) != (value >> swapIndex & 1))
			{
				// Toggle bits at i and swapIndex
				value ^= 1L << i | 1L << swapIndex;
			}
		}
		return value;
	}

	/// <summary>
	/// rearange the bits in a deterministic way. useful for spreading out significant bits so they are not clustered
	/// </summary>
	public static long _Swizzle(this int value)
	{
		//size in bits
		var length = sizeof(int) * 8; //32
		var prime = 3;
		for (int i = 0; i < length; i++)
		{
			int swapIndex = (i + 1) * prime % length; // Deterministic swapping pattern

			// Swapping bits at i and swapIndex if they are different
			if ((value >> i & 1) != (value >> swapIndex & 1))
			{
				// Toggle bits at i and swapIndex
				value ^= 1 << i | 1 << swapIndex;
			}
		}
		return value;
	}

}

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_Random
{
	public static TimeSpan _NextTimeSpan(this Random rand, double maxSeconds)
	{
		return rand._NextTimeSpan(0, maxSeconds);
	}

	public static TimeSpan _NextTimeSpan(this Random rand, double minSeconds, double maxSeconds)
	{
		var seconds = rand.NextDouble() * (maxSeconds - minSeconds) + minSeconds;
		var toReturn = TimeSpan.FromSeconds(seconds);
		return toReturn;
	}

	/// <summary>
	///    Return a vector with each component ranging from 0f to 1f
	/// </summary>
	/// <param name="random"></param>
	/// <returns></returns>
	public static Vector3 _NextVector3(this Random random)
	{
		//return (float)random.NextDouble();
		return new Vector3 { X = random.NextSingle(), Y = random.NextSingle(), Z = random.NextSingle(), };
	}


	/// <summary>
	///    return boolean true or false
	/// </summary>
	/// <returns></returns>
	public static bool _NextBoolean(this Random random)
	{
		return random.Next(2) == 1;
	}
	public static float _Next(this Random random, float minInclusive, float maxExclusive)
	{
		if (minInclusive >= maxExclusive)
			throw new ArgumentException("minInclusive must be less than maxExclusive");

		double range = (double)maxExclusive - (double)minInclusive;
		double sample = random.NextDouble();
		double scaled = (sample * range) + minInclusive;
		return (float)scaled;
	}

	/// <summary>
	///    return a printable unicode character (letters, numbers, symbols, whiteSpace)
	///    <para>note: this includes whiteSpace</para>
	/// </summary>
	/// <param name="random"></param>
	/// <param name="onlyLowerAscii">true to return a printable ASCII character in the "lower" range (less than 127)</param>
	/// <returns></returns>
	public static char _NextChar(this Random random, bool symbolsOrWhitespace = false, bool unicodeOkay = false)
	{
		if (unicodeOkay)
		{
			while (true)
			{
				var c = (char)random.Next(0, ushort.MaxValue);
				if (symbolsOrWhitespace)
				{
					if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
					{
						return c;
					}
				}
				else
				{
					if (char.IsLetterOrDigit(c))
					{
						return c;
					}
				}
			}
		}

		//ascii only
		while (true)
		{
			var c = (char)random.Next(0, 127);
			if (symbolsOrWhitespace)
			{
				if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
				{
					return c;
				}
			}
			else
			{
				if (char.IsLetterOrDigit(c))
				{
					return c;
				}
			}
		}
	}

	/// <summary>
	///    return a printable unicode string (letters, numbers, symbols, whiteSpace)
	///    <para>note: this includes whiteSpace</para>
	/// </summary>
	/// <param name="random"></param>
	/// <param name="onlyLowerAscii">true to return a printable ASCII character in the "lower" range (less than 127)</param>
	/// <returns></returns>
	public static string _NextString(this Random random, int length, bool symbolsOrWhitespace = false,
		bool unicodeOkay = false)
	{
		StringBuilder sb = new(length);
		for (int i = 0; i < length; i++)
			sb.Append(random._NextChar(unicodeOkay, symbolsOrWhitespace));

		return sb.ToString();
	}


	//[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
	//static zz_Extensions_Random()
	//{
	//	List<char> valid = new List<char>();
	//	for (int i = 0; i < ushort.MaxValue; i++)
	//	{
	//		var c = Convert.ToChar(i);
	//		if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
	//		{
	//			valid.Add(c);
	//			if (c < 127)
	//			{
	//				//__.GetLogger()._EzError(c >= 0, "expect char to be unsigned");
	//				lowerAsciiLimitIndexExclusive = valid.Count;
	//			}
	//		}
	//	}
	//	var span = new Span<char>(valid.ToArray());
	//	printableUnicode = span.ToString();

	//	//printableUnicode = valid.ToArray();
	//}
	///// <summary>
	///// the exclusive bound of the lower ascii set in our <see cref="printableUnicode"/> characters array
	///// </summary>
	//static int lowerAsciiLimitIndexExclusive;
	///// <summary>
	///// a sorted array of all unicode characters that meet the following criteria:
	///// <para>
	///// (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
	///// </para>
	///// </summary>
	//static string printableUnicode;

	/// <summary>Roll</summary>
	/// <param name="diceNotation">string to be evaluated</param>
	/// <returns>result of evaluated string</returns>
	/// <remarks>
	///    <para>
	///       source taken from http://stackoverflow.com/questions/1031466/evaluate-dice-rolling-notation-strings and
	///       reformatted for greater readability
	///    </para>
	/// </remarks>
	public static int _NextDice(this Random rand, string diceNotation)
	{
		//ToDo.Anyone("improve performance of this dice parser. also add zero bias and open ended notation.  and consider other factors like conditional expressions");

		__.GetLogger()._EzError(diceNotation._ContainsOnly("d1234567890-+/* )("),
			"unexpected characters detected.  are you sure you are inputing dice notation?");

		__.GetLogger()._EzError(
			!(diceNotation.Contains("-") || diceNotation.Contains("/") || diceNotation.Contains("%") ||
			  diceNotation.Contains("(")),
			"this is a limited functionality dice parser.  please add this functionality (it's easy).  Also, remove the lock ");

		//lock (rand)
		{
			int total = 0;

			// Addition is lowest order of precedence
			var addGroups = diceNotation.Split('+');

			// Add results of each group
			if (addGroups.Length > 1)
			{
				foreach (var expression in addGroups)
				{
					total += rand._NextDice(expression);
				}
			}
			else
			{
				// Multiplication is next order of precedence
				var multiplyGroups = addGroups[0].Split('*');

				// Multiply results of each group
				if (multiplyGroups.Length > 1)
				{
					total = 1; // So that we don't zero-out our results...

					foreach (var expression in multiplyGroups)
					{
						total *= rand._NextDice(expression);
					}
				}
				else
				{
					// Die definition is our highest order of precedence
					var diceGroups = multiplyGroups[0].Split('d');

					// This operand will be our die count, static digits, or else something we don't understand
					if (!int.TryParse(diceGroups[0].Trim(), out total))
					{
						total = 0;
					}

					int faces;

					// Multiple definitions ("2d6d8") iterate through left-to-right: (2d6)d8
					for (int i = 1; i < diceGroups.Length; i++)
					{
						// If we don't have a right side (face count), assume 6
						if (!int.TryParse(diceGroups[i].Trim(), out faces))
						{
							faces = 6;
						}

						int groupOutcome = 0;

						// If we don't have a die count, use 1
						for (int j = 0; j < (total == 0 ? 1 : total); j++)
							groupOutcome += rand.Next(1, faces);

						total += groupOutcome;
					}
				}
			}

			return total;
		}
	}
}

/// <summary>
///    Extension methods for the TextReader class and its sub classes (StreamReader, StringReader)
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_TextReader
{
	/// <summary>
	///    The method provides an iterator through all lines of the text reader.
	/// </summary>
	/// <param name="reader">The text reader.</param>
	/// <returns>The iterator</returns>
	/// <example>
	///    <code>
	/// 		using(var reader = fileInfo.OpenText()) {
	/// 		foreach(var line in reader.IterateLines()) {
	/// 		// ...
	/// 		}
	/// 		}
	/// 	</code>
	/// </example>
	/// <remarks>
	///    Contributed by OlivierJ
	/// </remarks>
	public static IEnumerable<string> _IterateLines(this TextReader reader)
	{
		string line = null;
		while ((line = reader.ReadLine()) != null)
			yield return line;
	}

	/// <summary>
	///    The method executes the passed delegate /lambda expression) for all lines of the text reader.
	/// </summary>
	/// <param name="reader">The text reader.</param>
	/// <param name="action">The action.</param>
	/// <example>
	///    <code>
	/// 		using(var reader = fileInfo.OpenText()) {
	/// 		reader.IterateLines(l => Console.WriteLine(l));
	/// 		}
	/// 	</code>
	/// </example>
	/// <remarks>
	///    Contributed by OlivierJ
	/// </remarks>
	public static void _IterateLines(this TextReader reader, Action<string> action)
	{
		foreach (var line in reader._IterateLines())
		{
			action(line);
		}
	}
}

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_Boolean
{
	/// <summary>
	///    Converts the value of this instance to its equivalent string representation (either "Yes" or "No").
	/// </summary>
	/// <param name="boolean"></param>
	/// <returns>string</returns>
	public static string _ToString(this bool boolean, bool asYesNo)
	{
		if (asYesNo)
		{
			return boolean ? "Yes" : "No";
		}

		return boolean.ToString();
	}

	/// <summary>
	///    Converts the value in number format {1 , 0}.
	/// </summary>
	/// <param name="boolean"></param>
	/// <returns>int</returns>
	/// <example>
	///    <code>
	/// 		int result= default(bool).ToBinaryTypeNumber()
	/// 	</code>
	/// </example>
	/// <remarks>
	///    Contributed by Mohammad Rahman, http://mohammad-rahman.blogspot.com/
	/// </remarks>
	public static int _ToInt(this bool boolean)
	{
		return boolean ? 1 : 0;
	}
}

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_Object
{
	/// <summary>
	/// Ensures value is not null, also returning it.  If null, throws an exception.
	/// <para>shortcut to `__.NotNull(value);`</para>
	/// </summary>

	[return: NotNull]
	public static T _NotNull<T>([NotNull] this T? value, string? message = null, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0, [CallerArgumentExpression("value")] string valueName = "") where T : class
	{
		return __.NotNull(value, message, memberName, sourceFilePath, sourceLineNumber, valueName)!;
	}


	//	public static bool _Is<T>(this T value, params Type[] types)
	//{
	//	return types.Any(t => t.IsAssignableFrom(value.GetType()));
	//}

	///// <summary>
	///// if null, will return string.<see cref="String.Empty"/>.  otherwise returns the normal <see cref="ToString"/> 
	///// </summary>
	///// <typeparam name="T"></typeparam>
	///// <param name="target"></param>
	///// <returns></returns>
	//public static string ToStringOrEmpty<T>(this T target)
	//{
	//   if (ReferenceEquals(target, null))
	//   {
	//      return string.Empty;
	//   }
	//   return target.ToString();
	//}

	///// <summary>
	///// 	Determines whether the object is equal to any of the provided values.
	///// </summary>
	///// <typeparam name = "T"></typeparam>
	///// <param name = "obj">The object to be compared.</param>
	///// <param name = "values">The values to compare with the object.</param>
	///// <returns></returns>
	//public static bool Equals<T>(this T obj, params T[] values)
	//   {

	//	  return Array.IndexOf(values, obj) != -1;
	//   }


	///// <summary>
	///// is this value inside any of the given collections
	///// </summary>
	///// <typeparam name="T"></typeparam>
	///// <param name="source"></param>
	///// <param name="collections"></param>
	///// <returns></returns>
	//public static bool IsInAny<T>(this T source, params IEnumerable<T>[] collections)
	//{
	//   if (null == source) throw new ArgumentNullException("source");
	//   foreach (var collection in collections)
	//   {
	//      var iCollection = collection as ICollection<T>;
	//      if (iCollection != null)
	//      {
	//         if (iCollection.Contains(source))
	//         {
	//            return true;
	//         }
	//         continue;
	//      }
	//      if (collection.Contains(source))
	//      {
	//         return true;
	//      }
	//   }
	//   return false;
	//}

	///// <summary>
	///// 	Returns TRUE, if specified target reference is equals with null reference.
	///// 	Othervise returns FALSE.
	///// </summary>
	///// <typeparam name = "T">Type of target.</typeparam>
	///// <param name = "target">Target reference. Can be null.</param>
	///// <remarks>
	///// 	Some types has overloaded '==' and '!=' operators.
	///// 	So the code "null == ((MyClass)null)" can returns <c>false</c>.
	///// 	The most correct way how to test for null reference is using "System.Object.ReferenceEquals(object, object)" method.
	///// 	However the notation with ReferenceEquals method is long and uncomfortable - this extension method solve it.
	///// 
	///// 	Contributed by tencokacistromy, http://www.codeplex.com/site/users/view/tencokacistromy
	///// </remarks>
	///// <example>
	///// 	MyClass someObject = GetSomeObject();
	///// 	if ( someObject.IsNull() ) { /* the someObject is null */ }
	///// 	else { /* the someObject is not null */ }
	///// </example>
	//public static bool IsNull<T>(this T target) where T : class
	//{
	//   var result = ReferenceEquals(target, null);
	//   return result;
	//}

	//public static bool IsDefault<T>(this T target) where T : struct
	//{
	//   //if (ReferenceEquals(target, null))
	//   //{
	//   //   return true;
	//   //}
	//   return Equals(target, default(T));
	//}
}

public static class zz_Extensions_PropertyInfo
{
	/// <summary>
	/// determine if the property is nullable
	/// </summary>
	/// <param name="property"></param>
	/// <returns></returns>
	public static bool _IsNullable(this PropertyInfo property)
	{
		NullabilityInfoContext nullabilityInfoContext = new NullabilityInfoContext();
		var info = nullabilityInfoContext.Create(property);
		if (info.WriteState == NullabilityState.Nullable || info.ReadState == NullabilityState.Nullable)
		{
			return true;
		}

		return false;
	}
}

/// <summary>
///    Extension methods for the DateTimeOffset data type.
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_DateTime
{
	private const int EveningEnds = 2;
	private const int MorningEnds = 12;
	private const int AfternoonEnds = 6;
	private static readonly DateTime Date1970 = new(1970, 1, 1);

	/// <summary>
	///    Return System UTC Offset
	/// </summary>
#pragma warning disable NO1001 // Illegal use of local time
	public static TimeSpan _UtcOffset => DateTime.Now.Subtract(DateTime.UtcNow);
#pragma warning restore NO1001 // Illegal use of local time

	/// <summary>
	///    To Iso String, including timezone offset.
	///    format used for java libraries, omits trailing miliseconds
	///    <para>example output: 2012-01-04T19:20:00+07:00</para>
	/// </summary>
	/// <param name="dateTime"></param>
	/// <returns></returns>
	public static string _ToIso(this DateTime dateTime, bool includeMs = false)
	{
		if (dateTime.Kind == DateTimeKind.Utc)
		{
			//print with "Z" suffix
			if (includeMs)
			{
				return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
			}

			return dateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
		}
		else
		{
			//print with timezone offsets
			if (includeMs)
			{
				return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
			}

			return dateTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
		}
	}

	///// <summary>
	/////    To Iso string, in UTC format
	///// </summary>
	///// <param name="dateTime"></param>
	///// <returns></returns>
	//public static string _ToIsoUtc(this DateTime dateTime, bool includeMs = false)
	//{

	//   return dateTime.ToUniversalTime()._ToIso(includeMs);

	//   //// Ensure the DateTime is in UTC
	//   //DateTime utcDateTime = dateTime.Kind == DateTimeKind.Unspecified
	//   //   ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
	//   //   : dateTime.ToUniversalTime();

	//   //// Format to ISO 8601
	//   ////return utcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

	//   //if (includeMs)
	//   //{
	//   //   return utcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK", CultureInfo.InvariantCulture);
	//   //}

	//   //return utcDateTime.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
	//}

	/// <summary>
	///    Returns the number of days in the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The number of days.</returns>
	public static int _GetCountDaysOfMonth(this DateTime date)
	{
		var nextMonth = date.AddMonths(1);
		return new DateTime(nextMonth.Year, nextMonth.Month, 1).AddDays(-1).Day;
	}

	/// <summary>
	///    Returns the first day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The first day of the month</returns>
	public static DateTime _GetFirstDayOfMonth(this DateTime date)
	{
		return new DateTime(date.Year, date.Month, 1);
	}

	/// <summary>
	///    Returns the first day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="dayOfWeek">The desired day of week.</param>
	/// <returns>The first day of the month</returns>
	public static DateTime _GetFirstDayOfMonth(this DateTime date, DayOfWeek dayOfWeek)
	{
		var dt = date._GetFirstDayOfMonth();
		while (dt.DayOfWeek != dayOfWeek)
		{
			dt = dt.AddDays(1);
		}

		return dt;
	}

	/// <summary>
	///    Returns the last day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The last day of the month.</returns>
	public static DateTime _GetLastDayOfMonth(this DateTime date)
	{
		return new DateTime(date.Year, date.Month, date._GetCountDaysOfMonth());
	}

	/// <summary>
	///    Returns the last day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="dayOfWeek">The desired day of week.</param>
	/// <returns>The date time</returns>
	public static DateTime _GetLastDayOfMonth(this DateTime date, DayOfWeek dayOfWeek)
	{
		var dt = date._GetLastDayOfMonth();
		while (dt.DayOfWeek != dayOfWeek)
		{
			dt = dt.AddDays(-1);
		}

		return dt;
	}

	///// <summary>
	/////    Indicates whether the date is today.
	///// </summary>
	///// <param name="dt">The date.</param>
	///// <returns>
	/////    <c>true</c> if the specified date is today; otherwise, <c>false</c>.
	///// </returns>
	//public static bool _IsToday(this DateTime dt)
	//{
	//   return dt.Date == DateTime.Today;
	//}

	/// <summary>
	///    Sets the time on the specified DateTime value.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="hours">The hours to be set.</param>
	/// <param name="minutes">The minutes to be set.</param>
	/// <param name="seconds">The seconds to be set.</param>
	/// <returns>The DateTime including the new time value</returns>
	public static DateTime _SetTime(this DateTime date, int hours, int minutes, int seconds)
	{
		return date._SetTime(new TimeSpan(hours, minutes, seconds));
	}

	/// <summary>
	///    Sets the time on the specified DateTime value.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="time">The TimeSpan to be applied.</param>
	/// <returns>
	///    The DateTime including the new time value
	/// </returns>
	public static DateTime _SetTime(this DateTime date, TimeSpan time)
	{
		return date.Date.Add(time);
	}


	/// <summary>
	///    Gets the first day of the week using the current culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetFirstDayOfWeek(this DateTime date)
	{
		return date._GetFirstDayOfWeek(null);
	}

	/// <summary>
	///    Gets the first day of the week using the specified culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="cultureInfo">The culture to determine the first weekday of a week.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetFirstDayOfWeek(this DateTime date, CultureInfo cultureInfo)
	{
		cultureInfo = cultureInfo ?? CultureInfo.CurrentCulture;

		var firstDayOfWeek = cultureInfo.DateTimeFormat.FirstDayOfWeek;
		while (date.DayOfWeek != firstDayOfWeek)
		{
			date = date.AddDays(-1);
		}

		return date;
	}

	/// <summary>
	///    Gets the last day of the week using the current culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetLastDayOfWeek(this DateTime date)
	{
		return date._GetLastDayOfWeek(null);
	}

	/// <summary>
	///    Gets the last day of the week using the specified culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="cultureInfo">The culture to determine the first weekday of a week.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetLastDayOfWeek(this DateTime date, CultureInfo cultureInfo)
	{
		return date._GetFirstDayOfWeek(cultureInfo).AddDays(6);
	}

	/// <summary>
	///    Gets the next occurence of the specified weekday within the current week using the current culture.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var thisWeeksMonday = DateTime.Now.GetWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetWeeksWeekday(this DateTime date, DayOfWeek weekday)
	{
		return date._GetWeeksWeekday(weekday, null);
	}

	/// <summary>
	///    Gets the next occurence of the specified weekday within the current week using the specified culture.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <param name="cultureInfo">The culture to determine the first weekday of a week.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var thisWeeksMonday = DateTime.Now.GetWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetWeeksWeekday(this DateTime date, DayOfWeek weekday, CultureInfo cultureInfo)
	{
		var firstDayOfWeek = date._GetFirstDayOfWeek(cultureInfo);
		return firstDayOfWeek._GetNextWeekday(weekday);
	}

	/// <summary>
	///    Gets the next occurence of the specified weekday.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var lastMonday = DateTime.Now.GetNextWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetNextWeekday(this DateTime date, DayOfWeek weekday)
	{
		while (date.DayOfWeek != weekday)
		{
			date = date.AddDays(1);
		}

		return date;
	}

	/// <summary>
	///    Gets the previous occurence of the specified weekday.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var lastMonday = DateTime.Now.GetPreviousWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetPreviousWeekday(this DateTime date, DayOfWeek weekday)
	{
		while (date.DayOfWeek != weekday)
		{
			date = date.AddDays(-1);
		}

		return date;
	}

	/// <summary>
	///    Determines whether the date only part of twi DateTime values are equal.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="dateToCompare">The date to compare with.</param>
	/// <returns>
	///    <c>true</c> if both date values are equal; otherwise, <c>false</c>.
	/// </returns>
	public static bool _IsDateEqual(this DateTime date, DateTime dateToCompare)
	{
		return date.Date == dateToCompare.Date;
	}

	/// <summary>
	///    Determines whether the time only part of two DateTime values are equal.
	/// </summary>
	/// <param name="time">The time.</param>
	/// <param name="timeToCompare">The time to compare.</param>
	/// <returns>
	///    <c>true</c> if both time values are equal; otherwise, <c>false</c>.
	/// </returns>
	public static bool _IsTimeEqual(this DateTime time, DateTime timeToCompare)
	{
		return time.TimeOfDay == timeToCompare.TimeOfDay;
	}

	/// <summary>
	///    Get milliseconds of UNIX era. This is the milliseconds since 1/1/1970
	/// </summary>
	/// <param name="dateTime">Up to which time.</param>
	/// <returns>number of milliseconds.</returns>
	/// <remarks>
	///    Contributed by blaumeister, http://www.codeplex.com/site/users/view/blaumeiser
	/// </remarks>
	public static long _GetMillisecondsSince1970(this DateTime dateTime)
	{
		var ts = dateTime.Subtract(Date1970);
		return (long)ts.TotalMilliseconds;
	}

	/// <summary>
	///    Indicates whether the specified date is a weekend (Saturday or Sunday).
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>
	///    <c>true</c> if the specified date is a weekend; otherwise, <c>false</c>.
	/// </returns>
	public static bool _IsWeekend(this DateTime date)
	{
		return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

		// return date.DayOfWeek._EqualsAny(DayOfWeek.Saturday, DayOfWeek.Sunday);
	}

	/// <summary>
	///    Adds the specified amount of weeks (=7 days gregorian calendar) to the passed date value.
	/// </summary>
	/// <param name="date">The origin date.</param>
	/// <param name="value">The amount of weeks to be added.</param>
	/// <returns>The enw date value</returns>
	public static DateTime _AddWeeks(this DateTime date, int value)
	{
		return date.AddDays(value * 7);
	}

	/// <summary>
	///    Get the number of days within that year.
	/// </summary>
	/// <param name="year">The year.</param>
	/// <returns>the number of days within that year</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static int _GetDays(int year)
	{
		var first = new DateTime(year, 1, 1);
		var last = new DateTime(year + 1, 1, 1);
		return first._GetDays(last);
	}

	/// <summary>
	///    Get the number of days within that date year.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>the number of days within that year</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static int _GetDays(this DateTime date)
	{
		return _GetDays(date.Year);
	}

	/// <summary>
	///    Get the number of days between two dates.
	/// </summary>
	/// <param name="fromDate">The origin year.</param>
	/// <param name="toDate">To year</param>
	/// <returns>The number of days between the two years</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static int _GetDays(this DateTime fromDate, DateTime toDate)
	{
		return Convert.ToInt32(toDate.Subtract(fromDate).TotalDays);
	}

	/// <summary>
	///    Return a period "Morning", "Afternoon", or "Evening"
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The period "morning", "afternoon", or "evening"</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static string _GetPeriodOfDay(this DateTime date)
	{
		var hour = date.Hour;
		if (hour < EveningEnds)
		{
			return "evening";
		}

		if (hour < MorningEnds)
		{
			return "morning";
		}

		return hour < AfternoonEnds ? "afternoon" : "evening";
	}

	/// <summary>
	///    Gets the week number for a provided date time value based on the current culture settings.
	/// </summary>
	/// <param name="dateTime">The date time.</param>
	/// <returns>The week number</returns>
	public static int _GetWeekOfYear(this DateTime dateTime)
	{
		var culture = CultureInfo.CurrentUICulture;
		var calendar = culture.Calendar;
		var dateTimeFormat = culture.DateTimeFormat;

		return calendar.GetWeekOfYear(dateTime, dateTimeFormat.CalendarWeekRule, dateTimeFormat.FirstDayOfWeek);
	}

	/// <summary>
	///    Indicates whether the specified date is Easter in the Christian calendar.
	/// </summary>
	/// <param name="date">Instance value.</param>
	/// <returns>True if the instance value is a valid Easter Date.</returns>
	public static bool _IsEaster(this DateTime date)
	{
		int Y = date.Year;
		int a = Y % 19;
		int b = Y / 100;
		int c = Y % 100;
		int d = b / 4;
		int e = b % 4;
		int f = (b + 8) / 25;
		int g = (b - f + 1) / 3;
		int h = (19 * a + b - d - g + 15) % 30;
		int i = c / 4;
		int k = c % 4;
		int L = (32 + 2 * e + 2 * i - h - k) % 7;
		int m = (a + 11 * h + 22 * L) / 451;
		int Month = (h + L - 7 * m + 114) / 31;
		int Day = (h + L - 7 * m + 114) % 31 + 1;

		DateTime dtEasterSunday = new(Y, Month, Day);

		return date == dtEasterSunday;
	}

	/// <summary>
	///    Indicates whether the source DateTime is before the supplied DateTime.
	/// </summary>
	/// <param name="source">The source DateTime.</param>
	/// <param name="other">The compared DateTime.</param>
	/// <returns>True if the source is before the other DateTime, False otherwise</returns>
	public static bool _IsBefore(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) < 0;
	}

	/// <summary>
	///    Indicates whether the source DateTime is before the supplied DateTime.
	/// </summary>
	/// <param name="source">The source DateTime.</param>
	/// <param name="other">The compared DateTime.</param>
	/// <returns>True if the source is before the other DateTime, False otherwise</returns>
	public static bool _IsAfter(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) > 0;
	}

	/// <summary>
	///    returns the lower of the two values
	/// </summary>
	/// <returns></returns>
	public static DateTime _Min(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) < 0 ? source : other;
	}

	/// <summary>
	///    returns the higher of the two values
	/// </summary>
	/// <returns></returns>
	public static DateTime _Max(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) > 0 ? source : other;
	}

	public static double _DaysAgo(this DateTime source)
	{
		return (DateTime.UtcNow - source).TotalDays;
	}
}

/// <summary>
///    Extension methods for the reflection meta data type "Type"
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_Type
{
	//static zz__Type_Extensions()
	//{
	//   var rand = new Random();

	//   _obfuscationSuffix += rand.NextChar();
	//}
	public static string _GetReadableTypeName(this Type type)
	{
		if (!type.IsGenericType)
		{
			return type.Name;
		}

		StringBuilder sb = new StringBuilder();
		sb.Append(type.Name.Split('`')[0]); // Remove the arity indicator
		sb.Append('<');
		sb.Append(string.Join(", ", type.GetGenericArguments().Select(_GetReadableTypeName)));
		sb.Append('>');

		return sb.ToString();
	}
	public static string _GetReadableTypeNameFull(this Type type)
	{
		StringBuilder sb = new StringBuilder();

		// Include the namespace if available
		if (!string.IsNullOrEmpty(type.Namespace))
		{
			sb.Append(type.Namespace).Append('.');
		}

		sb.Append(GetTypeNameWithoutArity(type));

		if (type.IsGenericType)
		{
			sb.Append('<');
			sb.Append(string.Join(", ", type.GetGenericArguments().Select(_GetReadableTypeNameFull)));
			sb.Append('>');
		}

		return sb.ToString();
	}
	static string GetTypeNameWithoutArity(Type type)
	{
		string name = type.Name;
		int backtickIndex = name.IndexOf('`');
		return backtickIndex == -1 ? name : name.Substring(0, backtickIndex);
	}

	private static string _obfuscationSuffix = " " + new Random()._NextChar(); // " ";

	public static T _GetInstanceField<T>(this Type type, object instance, string fieldName)
	{
		var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		var fieldInfo = type.GetField(fieldName, bindingFlags);
		return (T)fieldInfo.GetValue(instance);
	}

	/// <summary>
	/// Gets the value of a property or field from an instance of an object using reflection.
	/// Works with both public and non-public (private, protected, internal) members.
	/// </summary>
	/// <typeparam name="T">The expected type of the property or field value.</typeparam>
	/// <param name="type">The type that contains the property or field definition.</param>
	/// <param name="instance">The object instance from which to get the property or field value.</param>
	/// <param name="memberName">The name of the property or field to get.</param>
	/// <returns>The value of the property or field cast to type T.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type, instance, or memberName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the member cannot be found or is not a property or field.</exception>
	/// <exception cref="InvalidCastException">Thrown when the member value cannot be cast to the expected type.</exception>
	public static T _GetInstanceMember<T>(this Type type, object instance, string memberName)
	{
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (instance == null) throw new ArgumentNullException(nameof(instance));
		if (string.IsNullOrEmpty(memberName)) throw new ArgumentNullException(nameof(memberName));

		var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		// First try to find a property with the given name
		var propertyInfo = type.GetProperty(memberName, bindingFlags);
		if (propertyInfo != null)
		{
			return (T)propertyInfo.GetValue(instance);
		}

		// If not a property, try to find a field with the given name
		var fieldInfo = type.GetField(memberName, bindingFlags);
		if (fieldInfo != null)
		{
			return (T)fieldInfo.GetValue(instance);
		}

		// If we reach here, no matching property or field was found
		throw new ArgumentException($"No property or field named '{memberName}' found on type '{type.FullName}'.", nameof(memberName));
	}

	/// <summary>
	/// Sets a property or field value on an instance of an object using reflection.
	/// Works with both public and non-public (private, protected, internal) members.
	/// </summary>
	/// <param name="type">The type that contains the property or field definition.</param>
	/// <param name="instance">The object instance on which to set the property or field.</param>
	/// <param name="memberName">The name of the property or field to set.</param>
	/// <param name="value">The value to set the property or field to.</param>
	/// <exception cref="ArgumentNullException">Thrown when type, instance, or memberName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the member cannot be found or is not a property or field.</exception>
	/// <exception cref="TargetException">Thrown when the instance doesn't match the target type.</exception>
	public static void _SetInstanceMember(this Type type, object instance, string memberName, object value)
	{
		// Validate inputs
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (instance == null) throw new ArgumentNullException(nameof(instance));
		if (string.IsNullOrEmpty(memberName)) throw new ArgumentNullException(nameof(memberName));

		// Define binding flags for instance members (both public and non-public)
		const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		// First try to find a property with the given name
		var propertyInfo = type.GetProperty(memberName, bindingFlags);
		if (propertyInfo != null)
		{
			// Check if the property is writable
			if (!propertyInfo.CanWrite)
			{
				throw new ArgumentException($"Property '{memberName}' on type '{type.FullName}' is read-only.", nameof(memberName));
			}

			// Set the property value
			propertyInfo.SetValue(instance, value);
			return;
		}

		// If not a property, try to find a field with the given name
		var fieldInfo = type.GetField(memberName, bindingFlags);
		if (fieldInfo != null)
		{
			// Set the field value
			fieldInfo.SetValue(instance, value);
			return;
		}

		// If we reach here, no matching property or field was found
		throw new ArgumentException($"No property or field named '{memberName}' found on type '{type.FullName}'.", nameof(memberName));
	}

	/// <summary>
	/// Invokes a method on a target object by name using reflection.
	/// </summary>
	/// <param name="type">The type that contains the method definition.</param>
	/// <param name="instance">The instance on which to invoke the method. Use null for static methods.</param>
	/// <param name="methodName">The name of the method to invoke.</param>
	/// <param name="parameters">Optional parameters to pass to the method.</param>
	/// <returns>The result of the method invocation.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type or methodName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the method cannot be found on the specified type.</exception>
	/// <exception cref="TargetException">Thrown when the instance doesn't match the target type for instance methods.</exception>
	public static object? _InvokeInstanceMethod(this Type type, object instance, string methodName, params object[] parameters)
	{
		// Validate inputs
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (methodName == null) throw new ArgumentNullException(nameof(methodName));

		// Determine binding flags based on whether we're invoking a static or instance method
		var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
		if (instance == null)
		{
			bindingFlags |= BindingFlags.Static;
		}
		else
		{
			bindingFlags |= BindingFlags.Instance;
		}

		// Find the method on the type
		var methodInfo = type.GetMethod(methodName, bindingFlags);
		if (methodInfo == null)
		{
			throw new ArgumentException($"Method '{methodName}' not found on type '{type.FullName}'.", nameof(methodName));
		}

		// Invoke the method and return the result
		return methodInfo.Invoke(instance, parameters);
	}

	/// <summary>
	/// Invokes a method on a target object by name using reflection with a specific parameter types signature.
	/// </summary>
	/// <param name="type">The type that contains the method definition.</param>
	/// <param name="instance">The instance on which to invoke the method. Use null for static methods.</param>
	/// <param name="methodName">The name of the method to invoke.</param>
	/// <param name="parameterTypes">An array of parameter types that define the method signature.</param>
	/// <param name="parameters">Parameters to pass to the method.</param>
	/// <returns>The result of the method invocation.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type or methodName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the method cannot be found on the specified type.</exception>
	public static object? _InvokeInstanceMethod(this Type type, object instance, string methodName, Type[] parameterTypes, params object[] parameters)
	{
		// Validate inputs
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (methodName == null) throw new ArgumentNullException(nameof(methodName));
		if (parameterTypes == null) throw new ArgumentNullException(nameof(parameterTypes));

		// Determine binding flags
		var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
		if (instance == null)
		{
			bindingFlags |= BindingFlags.Static;
		}
		else
		{
			bindingFlags |= BindingFlags.Instance;
		}

		// Get the method with the specific parameter types
		var methodInfo = type.GetMethod(methodName, bindingFlags, null, parameterTypes, null);
		if (methodInfo == null)
		{
			throw new ArgumentException($"Method '{methodName}' with the specified parameter types not found on type '{type.FullName}'.", nameof(methodName));
		}

		// Invoke the method and return the result
		return methodInfo.Invoke(instance, parameters);
	}

	/// <summary>
	/// Invokes a method on a target object by name using reflection and returns a strongly-typed result.
	/// </summary>
	/// <typeparam name="TResult">The expected return type of the method.</typeparam>
	/// <param name="type">The type that contains the method definition.</param>
	/// <param name="instance">The instance on which to invoke the method. Use null for static methods.</param>
	/// <param name="methodName">The name of the method to invoke.</param>
	/// <param name="parameters">Optional parameters to pass to the method.</param>
	/// <returns>The result of the method invocation cast to the specified type.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type or methodName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the method cannot be found on the specified type.</exception>
	/// <exception cref="InvalidCastException">Thrown when the result cannot be cast to the expected type.</exception>
	public static TResult _InvokeInstanceMethod<TResult>(this Type type, object instance, string methodName, params object[] parameters)
	{
		// Call the non-generic version and cast the result
		var result = _InvokeInstanceMethod(type, instance, methodName, parameters);

		// Handle null result for value types
		if (result == null && typeof(TResult).IsValueType)
		{
			return default;
		}

		// Cast to the expected return type
		return (TResult)result;
	}

	//[Conditional("DEBUG")]
	//public static void _AssertHasObfuscationAttribute(this Type type)
	//{
	//	if (typeof(ObfuscationAttribute).Name != "ObfuscationAttribute")
	//	{
	//		//this has been obfuscated, so ignore this assert check
	//		return;
	//	}
	//	__.ERROR.AssertOnce(type.HasObfuscationAttribute(), "use [System.Reflection.Obfuscation(Exclude = true, StripAfterObfuscation = false, ApplyToMembers = true)] attribute!  type={0}", type.GetName());
	//}

	/// <summary>
	///    returns the "runtime name" of a type.
	///    <para>
	///       if the type is marked by an [Obfuscation(exclude:true)] attribute then we will return the actual name.
	///       otherwise, we add a random unicode suffix to represent obfuscation workflows
	///    </para>
	/// </summary>
	/// <param name="memberInfo"></param>
	/// <returns></returns>
	public static string _GetName(this MemberInfo memberInfo)
	{
		//#if DEBUG
		//         if (methodInfo.HasObfuscationAttribute())
		//         {
		//            return type.Name + _obfuscationSuffix;
		//         }
		//#endif
#if DEBUG

		ObfuscationAttribute obf;
		if (memberInfo._TryGetAttribute(out obf))
		{
			if (obf.Exclude)
			{
				return memberInfo.Name;
			}
		}

		return memberInfo.Name + _obfuscationSuffix;
#else
		return memberInfo.Name;
#endif
	}

	public static bool HasObfuscationAttribute(this MemberInfo memberInfo)
	{
		if (typeof(ObfuscationAttribute).Name != "ObfuscationAttribute")
		{
			//this has been obfuscated, so our check will always be false
			return false;
		}

		ObfuscationAttribute attribute;
		return memberInfo._TryGetAttribute(out attribute, false);

		//foreach (var attribute in memberInfo.GetCustomAttributes(false))
		//{
		//   if (attribute is ObfuscationAttribute)
		//   {
		//      return true;
		//   }
		//}

		//var type = memberInfo as Type;
		//if (type != null)
		//{
		//   foreach (var interf in type.GetInterfaces())
		//   {
		//      foreach (var attribute in interf.GetCustomAttributes(false))
		//      {
		//         if (attribute is ObfuscationAttribute)
		//         {
		//            return true;
		//         }
		//      }
		//   }
		//}
		//return false;
	}

	/// <summary>
	///    returns the first found attribute
	/// </summary>
	/// <typeparam name="TAttribute"></typeparam>
	/// <param name="memberInfo"></param>
	/// <param name="attributeFound"></param>
	/// <param name="inherit"></param>
	/// <returns></returns>
	public static bool _TryGetAttribute<TAttribute>(this MemberInfo memberInfo, [NotNullWhen(true)] out TAttribute? attributeFound,
		bool inherit = true) where TAttribute : Attribute
	{
		//var found = memberInfo.GetCustomAttributes(typeof(TAttribute), inherit);
		//if (found == null || found.Length == 0)
		//{
		//   attribute = null;
		//   return false;
		//}
		//attribute = found[0] as TAttribute;
		//return true;


		var attributeType = typeof(TAttribute);
		foreach (var attribute in memberInfo.GetCustomAttributes(attributeType, inherit))
		{
			//if (attribute is TAttribute)
			{
				attributeFound = (TAttribute)attribute;
				return true;
			}
		}

		var type = memberInfo as Type;
		if (type != null)
		{
			foreach (var interf in type.GetInterfaces())
			{
				foreach (var attribute in interf.GetCustomAttributes(attributeType, inherit))
				{
					//if (attribute is TAttribute)
					{
						attributeFound = attribute as TAttribute;
						return true;
					}
				}
			}
		}

		attributeFound = null;
		return false;
	}

	//   public static string GetObfuscatedAssemblyQualifiedName(this Type type)
	//   {
	//#if DEBUG
	//      //if the obfuscation attribute isn't named, then we know obfuscation is turned on and we should return the actual type name
	//      //otherwise, if no obfusation, if we have the attribute, we return "the original" name
	//      if (typeof(ObfuscationAttribute).Name != "ObfuscationAttribute" || type.HasObfuscationAttribute())
	//      {
	//         return type.AssemblyQualifiedName;
	//      }
	//      //but if the attribute isn't turned on (and if we are not actually obfuscated) lets simulate obfuscation by adjusting our returned string
	//      return ParseHelper.FormatInvariant("{2}{0}_{1}", type.AssemblyQualifiedName.ToUpperInvariant(), random, enclosing);
	//#else
	//         //in release, we just return our normal name
	//         return type.AssemblyQualifiedName;
	//#endif
	//   }


	public static bool _IsAssignableTo<TOther>(this Type type)
	{
		return type.IsAssignableTo(typeof(TOther));
	}

	/// <summary>
	///    Creates and returns an instance of the desired type
	/// </summary>
	/// <param name="type">The type to be instanciated.</param>
	/// <param name="constructorParameters">Optional constructor parameters</param>
	/// <returns>The instanciated object</returns>
	/// <example>
	///    <code>
	/// 		var type = Type.GetType(".NET full qualified class Type")
	/// 		var instance = type.CreateInstance();
	/// 	</code>
	/// </example>
	public static object _CreateInstance(this Type type, params object[] constructorParameters)
	{
		return type._CreateInstance<object>(constructorParameters);
	}

	/// <summary>
	///    Creates and returns an instance of the desired type casted to the generic parameter type T
	/// </summary>
	/// <typeparam name="T">The data type the instance is casted to.</typeparam>
	/// <param name="type">The type to be instanciated.</param>
	/// <param name="constructorParameters">Optional constructor parameters</param>
	/// <returns>The instanciated object</returns>
	/// <example>
	///    <code>
	/// 		var type = Type.GetType(".NET full qualified class Type")
	/// 		var instance = type.CreateInstance&lt;IDataType&gt;();
	/// 	</code>
	/// </example>
	public static T _CreateInstance<T>(this Type type, params object[] constructorParameters)
	{
		var instance = Activator.CreateInstance(type, constructorParameters);
		return (T)instance;
	}

	/// <summary>
	///    Check if this is a base type
	/// </summary>
	/// <param name="type"></param>
	/// <param name="checkingType"></param>
	/// <returns></returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static bool _IsBaseType(this Type type, Type checkingType)
	{
		__.GetLogger()._EzError(type is not null);
		while (type != typeof(object))
		{
			if (type == null)
			{
				continue;
			}

			if (type == checkingType)
			{
				return true;
			}

			type = type.BaseType;
		}

		return false;
	}

	/// <summary>
	///    Check if this is a sub class generic type
	/// </summary>
	/// <param name="generic"></param>
	/// <param name="toCheck"></param>
	/// <returns></returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static bool _IsSubclassOfRawGeneric(this Type generic, Type toCheck)
	{
		__.GetLogger()._EzError(generic is not null);
		while (toCheck != typeof(object))
		{
			if (toCheck == null)
			{
				continue;
			}

			var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
			if (generic == cur)
			{
				return true;
			}

			toCheck = toCheck.BaseType;
		}

		return false;
	}

	/// <summary>
	///    Closes the passed generic type with the provided type arguments and returns an instance of the newly constructed
	///    type.
	/// </summary>
	/// <typeparam name="T">The typed type to be returned.</typeparam>
	/// <param name="genericType">The open generic type.</param>
	/// <param name="typeArguments">The type arguments to close the generic type.</param>
	/// <returns>An instance of the constructed type casted to T.</returns>
	public static T _CreateGenericTypeInstance<T>(this Type genericType, params Type[] typeArguments) where T : class
	{
		var constructedType = genericType.MakeGenericType(typeArguments);
		var instance = Activator.CreateInstance(constructedType);
		return instance as T;
	}

	//public static bool _IsUnmanagedStruct(this Type type)
	//{
	//	return System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences()
	//}
}

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_TimeSpan
{
	public static int _ToFps(this TimeSpan timeSpan)
	{
		var ms = timeSpan.TotalMilliseconds;
		return (int)(1000 / ms);
	}
	//public static TimeSpan Multiply(this TimeSpan timeSpan, double number)
	//{
	//	return TimeSpan.FromTicks((long)(timeSpan.Ticks * number));
	//}

	//public static TimeSpan Divide(this TimeSpan timeSpan, double number)
	//{
	//	return TimeSpan.FromTicks((long)(timeSpan.Ticks / number));
	//}
	///// <summary>
	///// 
	///// </summary>
	///// <param name="timeSpan"></param>
	///// <param name="other"></param>
	///// <returns>ratio</returns>
	//public static double Divide(this TimeSpan timeSpan, TimeSpan other)
	//{
	//	return timeSpan.Ticks / (double)other.Ticks;
	//}

	/// <summary>
	/// Returns the minimum value between the current TimeSpan and another TimeSpan.
	/// </summary>
	/// <param name="_this">The current TimeSpan instance</param>
	/// <param name="other">The TimeSpan to compare with</param>
	/// <returns>The minimum TimeSpan value</returns>
	public static TimeSpan _Min(this TimeSpan _this, TimeSpan other)
	{
		// Return the smaller TimeSpan using the conditional operator
		return _this < other ? _this : other;
	}


	public static TimeSpan _Max(this TimeSpan _this, TimeSpan other)
	{
		// Return the larger TimeSpan using the conditional operator
		return _this > other ? _this : other;
	}

	/// <summary>
	/// calculate mod, ie:  _this % other
	/// </summary>
	public static TimeSpan _Mod(this TimeSpan _this, TimeSpan other)
	{
		var thisTicks = _this.Ticks;
		var otherTicks = other.Ticks;

		var result = thisTicks % otherTicks;

		return TimeSpan.FromTicks(result);
	}


	/// <summary>
	///    given an interval, find the previous occurance of that interval's multiple. (prior to this timespan).
	///    <para>If This timespan is precisely a multiple of interval, itself will be returned.</para>
	/// </summary>
	public static TimeSpan _IntervalPrior(this TimeSpan target, TimeSpan interval)
	{
		var remainder = target.Ticks % interval.Ticks;
		return TimeSpan.FromTicks(target.Ticks - remainder);
	}

	/// <summary>
	///    given an interval, find the next occurance of that interval's multiple.
	/// </summary>
	public static TimeSpan _IntervalNext(this TimeSpan target, TimeSpan interval)
	{
		return target._IntervalPrior(interval) + interval;
	}


	private static Random _random = new();

	/// <summary>
	///    implementation of exponential backoff waiting
	/// </summary>
	/// <param name="initialValue">value of 0 is ok, next value will be at least 1</param>
	/// <param name="limit">the maximum time, excluding any random buffering via the <see cref="randomPadding" /> variable</param>
	/// <param name="multiplier">default is 2.  exponent used as y variable in power function</param>
	/// <param name="randomPadding">default is true.  if true, add up to 1 second (randomized) to aid server load balancing</param>
	/// <returns></returns>
	public static TimeSpan _ExponentialBackoff(this TimeSpan initialValue, TimeSpan limit, double multiplier = 2,
		bool randomPadding = true)
	{
		__.GetLogger()._EzError(initialValue >= TimeSpan.Zero && limit >= TimeSpan.Zero, "input must not be Timespan.Zero");


		var backoff = initialValue.Multiply(multiplier);
		backoff = backoff > limit ? limit : backoff;

		if (randomPadding)
		{
			backoff += TimeSpan.FromSeconds(_random.NextDouble());
		}

		return backoff;

		//shitty non-working way
		//var limitTicks = limit.Ticks;
		//var ticks = Math.Pow(initialValue.Ticks, exponent);
		//ticks = ticks > limitTicks ? limitTicks : ticks;

		//var toReturn = TimeSpan.FromTicks((long)ticks);
		//if (toReturn == TimeSpan.Zero)
		//{
		//	toReturn = TimeSpan.FromSeconds(1);
		//}
		//else if (toReturn <= TimeSpan.FromSeconds(1))
		//{
		//	toReturn = TimeSpan.FromSeconds(2);
		//}

		//if (randomPadding)
		//{
		//	double randomPercent;
		//	lock (_random)
		//	{
		//		randomPercent = _random.NextDouble();
		//	}
		//	return toReturn + TimeSpan.FromSeconds(randomPercent);
		//}
		//else
		//{
		//	return toReturn;
		//}
	}
}

//[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
//public static class zz__CharArray_Extensions
//{

//	/// <summary>
//	/// 	Converts the char[] to a byte-array using the supplied encoding
//	/// </summary>
//	/// <param name = "value">The input string.</param>
//	/// <param name = "encoding">The encoding to be used.  default UTF8</param>
//	/// <returns>The created byte array</returns>
//	/// <example>
//	/// 	<code>
//	/// 		var value = "Hello World";
//	/// 		var ansiBytes = value.ToBytes(Encoding.GetEncoding(1252)); // 1252 = ANSI
//	/// 		var utf8Bytes = value.ToBytes(Encoding.UTF8);
//	/// 	</code>
//	/// </example>
//	public static byte[] _ToBytes(this char[] array, Encoding encoding = null, bool withPreamble = false, int start = 0, int? count = null)
//	{

//		if (!count.HasValue)
//		{
//			count = array.Length - start;
//		}
//		encoding = (encoding ?? Encoding.UTF8);
//		if (withPreamble)
//		{
//			var preamble = encoding.GetPreamble();

//			var stringBytes = encoding.GetBytes(array, start, count.Value);
//			var bytes = preamble._Join(stringBytes);
//			__.GetLogger()._EzError(bytes.Compare(preamble) == 0);
//			return bytes;
//		}
//		else
//		{
//			return encoding.GetBytes(array, start, count.Value);
//		}
//	}
//	public static int GetHashUniversal(this char[] array, int start = 0, int? count = null)
//	{
//		var bytes = array.ToBytes(start: start, count: count);
//		return (int)HashAlgorithm.Hash(bytes);
//	}
//}
/// <summary>
///    Extension methods for the string data type
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_String
{
	/// <summary>
	/// Compresses the provided string using Brotli compression and encodes the result in Z85e.
	/// <para>If you need to use a dictionary, use ZstdSharp (already a referenced nuget package)</para>
	/// </summary>
	/// <param name="input">The string to compress</param>
	/// <returns>The Z85e encoded compressed string</returns>
	/// <exception cref="ArgumentNullException">Thrown when input is null</exception>
	public static string _Compress(this string input)
	{
		// Validate input: throw an exception if input is null
		if (input == null)
		{
			throw new ArgumentNullException(nameof(input), "Input string cannot be null.");
		}

		// Return an empty string for an empty input to avoid unnecessary processing
		if (input == "")
		{
			return "";
		}

		// Convert the input string to a byte array using UTF8 encoding
		byte[] inputBytes = Encoding.UTF8.GetBytes(input);


		//{
		//   //using zStd
		//   var c = new ZstdSharp.Compressor();
		//   var compressedBytes = c.Wrap(inputBytes);
		//   // Use the Z85e encoding method to encode the compressed bytes
		//   return SimpleBase.Base85.Z85.Encode(compressedBytes);
		//}

		// Using a memory stream to temporarily store the compressed data
		using (var outputStream = new MemoryStream())
		{
			// Compress the data using Brotli compression stream
			using (var compressionStream = new BrotliStream(outputStream, CompressionMode.Compress))
			{
				compressionStream.Write(inputBytes, 0, inputBytes.Length);
			}

			// Retrieve the compressed data
			byte[] compressedBytes = outputStream.ToArray();



			// Use the Z85e encoding method to encode the compressed bytes
			return SimpleBase.Base85.Z85.Encode(compressedBytes);


		}
	}

	/// <summary>
	/// Decompresses a Z85e encoded and Brotli compressed string back to its original form.
	/// <para>If you need to use a dictionary, use ZstdSharp (already a referenced nuget package)</para>
	/// </summary>
	/// <param name="input">The Z85e encoded and compressed string</param>
	/// <returns>The original uncompressed string</returns>
	/// <exception cref="ArgumentNullException">Thrown when input is null</exception>
	/// <exception cref="FormatException">Thrown when input is not a valid Z85e encoded string</exception>
	public static string _Decompress(this string input)
	{
		// Validate input: throw an exception if input is null
		if (input == null)
		{
			throw new ArgumentNullException(nameof(input), "Input string cannot be null.");
		}

		// Return an empty string for an empty input to avoid unnecessary processing
		if (input == "")
		{
			return "";
		}

		try
		{

			// Decode the input string from Z85e encoding
			byte[] decodedBytes = SimpleBase.Base85.Z85.Decode(input);


			//{
			//   //using zStd
			//   var d = new ZstdSharp.Decompressor();
			//   var decompressedBytes = d.Unwrap(decodedBytes);
			//   // Convert the decompressed byte array back to a string using UTF8 encoding
			//   return Encoding.UTF8.GetString(decompressedBytes);
			//}


			// Decompress the data using Brotli compression stream
			using (var inputStream = new MemoryStream(decodedBytes))
			using (var decompressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
			using (var resultStream = new MemoryStream())
			{
				decompressionStream.CopyTo(resultStream);

				// Convert the decompressed byte array back to a string using UTF8 encoding
				return Encoding.UTF8.GetString(resultStream.ToArray());
			}
		}
		catch (Exception ex) when (ex is FormatException || ex is InvalidDataException)
		{
			throw new FormatException("Input string is not a valid Z85e encoded or Brotli compressed string.", ex);
		}
	}


	public static string _FormatAppendArgs(this string message,
		object? objToLog0 = null, object? objToLog1 = null, object? objToLog2 = null,
		[CallerArgumentExpression("objToLog0")] string? objToLog0Name = null,
		[CallerArgumentExpression("objToLog1")] string? objToLog1Name = null,
		[CallerArgumentExpression("objToLog2")] string? objToLog2Name = null,
		string joinString = "\n\t"
	)
	{
		//store all (objToLog,objToLogName) pairs in a list, discarding any pairs with an objToLogName of "null"
		//create a finalLogMessage combining the message with the names from each pair, showing the values from each pair      
		//pass the finalLogMessage and all the values to the Microsoft.Extensions.Logging.ILogger.Log method
		//that ILogger.Log has the following signature: public static void Log(this ILogger logger, LogLevel logLevel, Exception? exception, string? message, params object?[] args)

		using (__.pool.GetUsing<List<(string name, object? value)>>(out var argPairs, list => list.Clear()))
		{
			if (argPairs.Count > 0)
			{
				throw new Exception("argPairs.Count > 0");
			}

			if (objToLog0 is not null || objToLog0Name is not null)
			{
				argPairs.Add((objToLog0Name, objToLog0));
			}

			if (objToLog1 is not null || objToLog1Name is not null)
			{
				argPairs.Add((objToLog1Name, objToLog1));
			}

			if (objToLog2 is not null || objToLog2Name is not null)
			{
				argPairs.Add((objToLog2Name, objToLog2));
			}

			////roundtrip argValues to json to avoid logger (serilog) max depth errors
			//{
			//   for (var i = 0; i < argPairs.Count; i++)
			//   {
			//      try
			//      {
			//         var obj = argPairs[i].value;

			//         if (obj is null)
			//         {
			//            continue;
			//         }

			//         argPairs[i] = (argPairs[i].name, SerializationHelper.ToPoCo(obj));
			//      }
			//      catch (Exception err)
			//      {
			//         __.GetLogger().LogError($"could not roundtrip {argPairs[i].name} due to error {argPairs[i].value}.", err);
			//         throw;
			//      }
			//   }
			//}


			//adjust our message to include all arg Name+Values
			for (var i = 0; i < argPairs.Count; i++)
			{
				var (argName, argValue) = argPairs[i];


				//sanitize argName            
				argName = argName._ConvertToAlphanumeric();

				//sanitize argValue
				if (argValue is null)
				{
					argValue = "null";
				}
				//else if (argValue is string str)
				//{
				//   argValue = str._ConvertToAlphanumeric();
				//}
				//else if (argValue is IEnumerable enumerable)
				//{
				//   argValue = enumerable.Cast<object>().Select(x => x.ToString()).Join()
				//}
				else
				{
					argValue = argValue.ToString()._Replace("[", '(')._Replace("]", ')');
				}

				message += $"{joinString}{argName} : {argValue}";
			}


			return message;
		}
	}


	public static string _AppendArgs(this string message, object? objToLog0 = null, object? objToLog1 = null, object? objToLog2 = null,
		[CallerArgumentExpression("objToLog0")] string? objToLog0Name = null,
		[CallerArgumentExpression("objToLog1")] string? objToLog1Name = null,
		[CallerArgumentExpression("objToLog2")] string? objToLog2Name = null
	)
	{
		if (string.IsNullOrWhiteSpace(objToLog0Name) is false)
		{
			message = $"{message}; {objToLog0Name}={objToLog0}";
		}
		if (string.IsNullOrWhiteSpace(objToLog1Name) is false)
		{
			message = $"{message}; {objToLog1Name}={objToLog1}";
		}
		if (string.IsNullOrWhiteSpace(objToLog2Name) is false)
		{
			message = $"{message}; {objToLog2Name}={objToLog2}";
		}

		return message;
	}

	/// <summary>
	///    extracts the suffix of this string once it no longer matches the other string
	/// </summary>
	public static string _GetUniqueSuffix(this string value, string other)
	{
		//find the point in both strings where they no longer match
		//return the remainder of the "value" string from that point onwards


		if (string.IsNullOrEmpty(value))
		{
			return value;
		}

		int minLength = Math.Min(value.Length, other.Length);
		int mismatchIndex = 0;

		while (mismatchIndex < minLength && value[mismatchIndex] == other[mismatchIndex])
			mismatchIndex++;

		return mismatchIndex >= value.Length ? "" : value.Substring(mismatchIndex);
	}

	/// <summary>
	///    Extracts the prefix of this string once it no longer matches the other string's ending.
	/// </summary>
	/// <param name="value">The string to extract the unique prefix from.</param>
	/// <param name="other">The string to compare against.</param>
	/// <returns>The beginning of the "value" string, before the non-matching point.</returns>
	public static string _GetUniquePrefix(this string value, string other)
	{
		if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(other))
		{
			return value;
		}

		int valueIndex = value.Length - 1;
		int otherIndex = other.Length - 1;

		// Searching from the end, find where they no longer match
		while (valueIndex >= 0 && otherIndex >= 0 && value[valueIndex] == other[otherIndex])
		{
			valueIndex--;
			otherIndex--;
		}

		// Return the beginning of the "value" string, before the non-matching point
		return value.Substring(0, valueIndex + 1);
	}

	/// <summary>
	///    ignores case, trims, etc and culture
	/// </summary>
	/// <param name="value"></param>
	/// <param name="other"></param>
	/// <returns></returns>
	public static bool _AproxEqual(this string value, string? other)
	{
		if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(other))
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(other))
		{
			return false;
		}

		return value._ConvertToAlphanumeric().Trim().Equals(other._ConvertToAlphanumeric().Trim(), StringComparison.InvariantCultureIgnoreCase);
	}

	///// <summary>
	///// Equality using OrdinalIgnoreCase
	///// </summary>
	//public static bool _Eq(this string first, string second)
	//{
	//	return first.Equals(second, StringComparison.OrdinalIgnoreCase);
	//}



	private static ConcurrentDictionary<string, Regex> _ToRegex_compiledCache = new();
	/// <summary>
	///    convert string to regex, with default options for performance.
	///    Future calls return the prior, compiled version
	/// </summary>
	public static Regex _ToRegex(this string regexp)
	{
		return _ToRegex_compiledCache.GetOrAdd(regexp, static (regexp) =>
		{
			RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.Compiled | RegexOptions.CultureInvariant;
			var toReturn = new Regex(regexp, options);
			return toReturn;
		});
	}

	public static bool _ToBool(this string? value, bool defaultIfNullOrInvalid = default)
	{
		if (bool.TryParse(value, out var result))
		{
			return result;
		}
		return defaultIfNullOrInvalid;
	}

	//public static double _ToDouble(this string? value, double defaultIfNullOrInvalid = default)
	//{
	//   if (double.TryParse(value, out var result))
	//   {
	//      return result;
	//   }
	//   return defaultIfNullOrInvalid;
	//}

	public static T _ToNumber<T>(this string? value, T defaultIfNullOrInvalid = default, IFormatProvider? formatProvider = default) where T : INumber<T>
	{
		formatProvider ??= CultureInfo.InvariantCulture;
		if (T.TryParse(value, formatProvider, out var result))
		{
			return result;
		}
		return defaultIfNullOrInvalid;
	}



	#region Bytes & Base64

	/// <summary>
	///    Converts the string directly into a byte-array using the supplied encoding (utf8 by default)
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="encoding">The encoding to be used.  default UTF8</param>
	/// <param name="withPreamble">default false.   if true, prepends a marker specifying encoding</param>
	/// <returns>The created byte array</returns>
	/// <example>
	///    <code>
	/// 		var value = "Hello World";
	/// 		var ansiBytes = value.ToBytes(Encoding.GetEncoding(1252)); // 1252 = ANSI
	/// 		var utf8Bytes = value.ToBytes(Encoding.UTF8);
	/// 	</code>
	/// </example>
	public static byte[] _ToBytes(this string value, Encoding encoding = null, bool withPreamble = false)
	{
		encoding = encoding ?? Encoding.UTF8;
		if (withPreamble)
		{
			var preamble = encoding.GetPreamble();
			var stringBytes = encoding.GetBytes(value);
			var bytes = preamble._Join(stringBytes);
			__.GetLogger()._EzError(bytes._Compare(preamble) == 0);
			return bytes;
		}

		return encoding.GetBytes(value);
	}



	/// <summary>
	/// convert base64 encoded binary back into a byte[]
	/// </summary>
	/// <param name="input"></param>
	/// <returns></returns>
	public static byte[] _FromBase64(this string input)
	{
		var requiredPadding = 4 - input.Length % 4;
		if (requiredPadding < 4)
		{
			input += new string('=', requiredPadding);
		}
		return Convert.FromBase64String(input);
	}

	#endregion

	#region globalization

	public static string _FormatInvariant(this string format, params object[] args)
	{
		return string.Format(CultureInfo.InvariantCulture, format, args);
	}

	public static int _CompareTo(this string strA, string strB, StringComparison comparison)
	{
		return string.Compare(strA, strB, comparison);
	}

	#endregion globalization

	#region Common string extensions

	/// <summary>
	///    returns true if string only contains <see cref="characters" /> from input paramaters.
	/// </summary>
	/// <param name="toEvaluate"></param>
	/// <param name="characters"></param>
	public static bool _ContainsOnly(this string toEvaluate, string characters)
	{
		foreach (var c in toEvaluate)
		{
			if (characters.IndexOf(c) >= 0)
			{
				continue;
			}

			return false;
		}

		return true;
	}

	/// <summary>
	///    returns true if string only contains <see cref="characters" /> from input paramaters.
	/// </summary>
	/// <param name="toEvaluate"></param>
	/// <param name="characters"></param>
	public static bool _ContainsOnly(this string toEvaluate, params char[] characters)
	{
		foreach (var c in toEvaluate)
		{
			if (characters.Contains(c))
			{
				continue;
			}

			return false;
		}

		return true;
	}

	/// <summary>
	///    returns true if string only contains <see cref="characters" /> from input paramaters.
	/// </summary>
	/// <param name="toEvaluate"></param>
	/// <param name="characters"></param>
	public static bool _ContainsOnly(this string toEvaluate, char only)
	{
		foreach (var c in toEvaluate)
		{
			if (c == only)
			{
				continue;
			}

			return false;
		}

		return true;
	}


	public static bool _EndsWith(this string value, char c)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		return value[value.Length - 1].Equals(c);
	}

	public static bool _StartsWith(this string value, char c)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		return value[0].Equals(c);
	}

	/// <summary>
	///    Determines whether the specified string is null or empty.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	public static bool _IsNullOrEmpty(this string value)
	{
		return string.IsNullOrEmpty(value);
	}


	/// <summary>
	///    Trims the text to a provided maximum length.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="maxLength">Maximum length.</param>
	/// <returns></returns>
	/// <remarks>
	///    Proposed by Rene Schulte
	/// </remarks>
	public static string _SetLength(this string value, int maxLength)
	{
		return value == null || value.Length <= maxLength ? value : value.Substring(0, maxLength);
	}


	/// <summary>
	///    Determines whether the comparison value strig is contained within the input value string
	/// </summary>
	/// <param name="inputValue">The input value.</param>
	/// <param name="comparisonValue">The comparison value.</param>
	/// <param name="comparisonType">Type of the comparison to allow case sensitive or insensitive comparison.</param>
	/// <returns>
	///    <c>true</c> if input value contains the specified value, otherwise, <c>false</c>.
	/// </returns>
	public static bool _Contains(this string inputValue, string comparisonValue, StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase)
	{
		return inputValue.IndexOf(comparisonValue, comparisonType) != -1;
	}

	public static bool _Contains(this string value, char toFind)
	{
		return value.IndexOf(toFind) != -1;
	}

	public static bool _Contains(this string value, params char[] toFind)
	{
		return value.IndexOfAny(toFind) != -1;
	}

	public static bool _ContainsAny(this string value, ReadOnlySpan<string> stringsToFind, StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase)
	{
		if (value is null || value.Length == 0)
		{
			return false;
		}
		foreach (var span in stringsToFind)
		{
			if (value.IndexOf(span, comparisonType) != -1)
			{
				return true;
			}
		}
		return false;
	}


	/// <summary>
	///    Centers a charters in this string, padding in both, left and right, by specified Unicode character,
	///    for a specified total lenght.
	/// </summary>
	/// <param name="value">Instance value.</param>
	/// <param name="width">
	///    The number of characters in the resulting string,
	///    equal to the number of original characters plus any additional padding characters.
	/// </param>
	/// <param name="padChar">A Unicode padding character.</param>
	/// <param name="truncate">
	///    Should get only the substring of specified width if string width is
	///    more than the specified width.
	/// </param>
	/// <returns>
	///    A new string that is equivalent to this instance,
	///    but center-aligned with as many paddingChar characters as needed to create a
	///    length of width paramether.
	/// </returns>
	public static string _PadBoth(this string value, int width, char padChar, bool truncate = false)
	{
		int diff = width - value.Length;
		if (diff == 0 || diff < 0 && !truncate)
		{
			return value;
		}

		if (diff < 0)
		{
			return value.Substring(0, width);
		}

		return value.PadLeft(width - diff / 2, padChar).PadRight(width, padChar);
	}


	/// <summary>
	///    Reverses / mirrors a string.
	/// </summary>
	/// <param name="value">The string to be reversed.</param>
	/// <returns>The reversed string</returns>
	public static string _Reverse(this string value)
	{
		if (value._IsNullOrEmpty() || value.Length == 1)
		{
			return value;
		}

		var chars = value.ToCharArray();
		Array.Reverse(chars);
		return new string(chars);
	}

	/// <summary>
	///    Ensures that a string starts with a given prefix.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="prefix">The prefix value to check for.</param>
	/// <returns>The string value including the prefix</returns>
	/// <example>
	///    <code>
	/// 		var extension = "txt";
	/// 		var fileName = string.Concat(file.Name, extension.EnsureStartsWith("."));
	/// 	</code>
	/// </example>
	public static string _EnsureStartsWith(this string value, string prefix,
		StringComparison compare = StringComparison.OrdinalIgnoreCase)
	{
		return value.StartsWith(prefix, compare) ? value : string.Concat(prefix, value);
	}

	/// <summary>
	///    Ensures that a string ends with a given suffix.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="suffix">The suffix value to check for.</param>
	/// <returns>The string value including the suffix</returns>
	/// <example>
	///    <code>
	/// 		var url = "http://www.pgk.de";
	/// 		url = url.EnsureEndsWith("/"));
	/// 	</code>
	/// </example>
	public static string _EnsureEndsWith(this string value, string suffix,
		StringComparison compare = StringComparison.OrdinalIgnoreCase)
	{
		return value.EndsWith(suffix, compare) ? value : string.Concat(value, suffix);
	}

	/// <summary>
	///    Ensures that a string ends with a given suffix.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="suffix">The suffix value to check for.</param>
	/// <returns>The string value including the suffix</returns>
	/// <example>
	///    <code>
	/// 		var url = "http://www.pgk.de";
	/// 		url = url.EnsureEndsWith("/"));
	/// 	</code>
	/// </example>
	public static string _EnsureEndsWith(this string value, char suffix)
	{
		return value.EndsWith(suffix) ? value : string.Concat(value, suffix);
	}

	/// <summary>
	///    Repeats the specified string value as provided by the repeat count.
	/// </summary>
	/// <param name="value">The original string.</param>
	/// <param name="repeatCount">The repeat count.</param>
	/// <returns>The repeated string</returns>
	public static string _Repeat(this string value, int repeatCount)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < repeatCount; i++)
			sb.Append(value);

		return sb.ToString();
	}

	/// <summary>
	///    Tests whether the contents of a string is a numeric value
	/// </summary>
	/// <param name="value">String to check</param>
	/// <returns>
	///    Boolean indicating whether or not the string contents are numeric
	/// </returns>
	/// <remarks>
	///    Contributed by Kenneth Scott
	/// </remarks>
	public static bool _IsNumeric(this string value)
	{
		float output;
		return float.TryParse(value, out output);
	}

	/// <summary>
	///    Extracts all digits from a string.
	/// </summary>
	/// <param name="value">String containing digits to extract</param>
	/// <returns>
	///    All digits contained within the input string
	/// </returns>
	/// <remarks>
	///    Contributed by Kenneth Scott
	/// </remarks>
	public static string _ExtractDigits(this string value)
	{
		return string.Join(null, Regex.Split(value, "[^\\d]"));
	}

	/// <summary>
	/// returns the index where the two strings become different
	/// </summary>
	public static int _IndexOfDifference(this string str1, string str2)
	{
		// If either string is null, return -1 to indicate an invalid comparison.
		if (str1 == null || str2 == null)
			return -1;

		// Find the length of the shorter string to avoid out-of-bounds errors.
		int minLength = Math.Min(str1.Length, str2.Length);

		// Iterate through each character in both strings up to the length of the shorter string.
		for (int i = 0; i < minLength; i++)
		{
			if (str1[i] != str2[i])
				return i; // Return the index where the characters differ.
		}

		// If no difference was found in the overlapping portion, check for length difference.
		if (str1.Length != str2.Length)
			return minLength; // Difference is at the end of the shorter string.

		// If strings are identical in both content and length, return -1.
		return -1;
	}

	/// <summary>
	///    gets the string after the first instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing"></param>
	/// <returns></returns>
	public static string _GetAfterFirst(this string value, string left, bool? fullIfLeftMissing = null)
	{
		var xPos = value.IndexOf(left, StringComparison.Ordinal);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				__.Throw($" '{left}' not found in '{value}'");
			}
			return fullIfLeftMissing.GetValueOrDefault() ? value : string.Empty;
		}

		var startIndex = xPos + left.Length;
		return startIndex >= value.Length ? string.Empty : value[startIndex..];
	}

	/// <summary>
	///    gets the string after the first instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing">if not set, will throw if missing</param>
	/// <returns></returns>
	public static string _GetAfterFirst(this string value, char left, bool? fullIfLeftMissing = null)
	{
		var xPos = value.IndexOf(left);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				__.Throw($" '{left}' not found in '{value}'");
			}
			return fullIfLeftMissing.GetValueOrDefault() ? value : string.Empty;
		}

		var startIndex = xPos + 1;
		return startIndex >= value.Length ? string.Empty : value.Substring(startIndex);
	}

	/// <summary>
	///    Gets the string before the first instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="right">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetBefore(this string value, string right, bool? fullIfRightMissing = null)
	{
		var xPos = value.IndexOf(right, StringComparison.Ordinal);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    Gets the string before the first instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="right">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetBefore(this string value, char right, bool? fullIfRightMissing = null)
	{
		var xPos = value.IndexOf(right);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    gets the string before the last instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing"></param>
	/// <returns></returns>
	public static string _GetBeforeLast(this string value, string right, bool? fullIfRightMissing = null)
	{
		var xPos = value.LastIndexOf(right, StringComparison.Ordinal);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    gets the string before the last instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing"></param>
	/// <returns></returns>
	public static string _GetBeforeLast(this string value, char right, bool? fullIfRightMissing = null)
	{
		var xPos = value.LastIndexOf(right);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    Gets the string between the first and last instance of the given parameters.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The left string parameter.</param>
	/// <param name="right">The right string parameter</param>
	/// <returns></returns>
	public static string _GetBetween(this string value, string left, string right, bool fullIfLeftMissing,
		bool fullIfRightMissing)
	{
		var xPos = value.IndexOf(left, StringComparison.Ordinal);
		var yPos = value.LastIndexOf(right, StringComparison.Ordinal);

		if (xPos == -1 && yPos == -1)
		{
			return fullIfLeftMissing && fullIfRightMissing ? value : string.Empty;
		}

		if (xPos == -1)
		{
			return fullIfLeftMissing ? value.Substring(0, yPos) : string.Empty;
		}

		if (yPos == -1)
		{
			var firstIndex = xPos + left.Length;
			return fullIfRightMissing ? value.Substring(firstIndex, value.Length - firstIndex) : string.Empty;
		}

		var startIndex = xPos + left.Length;
		return startIndex >= yPos ? string.Empty : value.Substring(startIndex, yPos - startIndex);
	}

	/// <summary>
	///    Gets the string between the first and last instance of the given parameters.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The left string parameter.</param>
	/// <param name="right">The right string parameter</param>
	/// <returns></returns>
	public static string _GetBetween(this string value, char left, char right, bool fullIfLeftMissing,
		bool fullIfRightMissing)
	{
		var xPos = value.IndexOf(left);
		var yPos = value.LastIndexOf(right);

		if (xPos == -1 && yPos == -1)
		{
			return fullIfLeftMissing && fullIfRightMissing ? value : string.Empty;
		}

		if (xPos == -1)
		{
			return fullIfLeftMissing ? value.Substring(0, yPos) : string.Empty;
		}

		if (yPos == -1)
		{
			var firstIndex = xPos + 1;
			return fullIfRightMissing ? value.Substring(firstIndex, value.Length - firstIndex) : string.Empty;
		}

		var startIndex = xPos + 1;
		return startIndex >= yPos ? string.Empty : value.Substring(startIndex, yPos - startIndex);
	}

	/// <summary>
	///    Gets the string after the last instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetAfter(this string value, string left, bool? fullIfLeftMissing = null,
		StringComparison comparison = StringComparison.OrdinalIgnoreCase)
	{
		var xPos = value.LastIndexOf(left, comparison);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				throw __.Throw("substring not found");
			}
			return fullIfLeftMissing.Value ? value : string.Empty;
		}

		var startIndex = xPos + left.Length;
		return startIndex >= value.Length ? string.Empty : value.Substring(startIndex);
		;
	}

	/// <summary>
	///    Gets the string after the last instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetAfter(this string value, char left, bool? fullIfLeftMissing = null)
	{
		var xPos = value.LastIndexOf(left);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				throw __.Throw("substring not found");
			}
			return fullIfLeftMissing.Value ? value : string.Empty;
		}

		var startIndex = xPos + 1;
		return startIndex >= value.Length ? string.Empty : value.Substring(startIndex);
	}


	/// <summary>
	///    Remove any instance of the given character from the current string.
	/// </summary>
	/// <param name="value">
	///    The input.
	/// </param>
	/// <param name="charactersToRemove">
	///    The remove char.
	/// </param>
	public static string _Remove(this string value, params char[] charactersToRemove)
	{
		__.GetLogger()._EzError(value is not null);
		var result = value;
		if (!string.IsNullOrEmpty(result) && charactersToRemove != null)
		{
			Array.ForEach(charactersToRemove, c => result = result._Remove(c.ToString()));
		}

		return result;
	}

	/// <summary>
	///    Remove any instance of the given string pattern from the current string.
	/// </summary>
	/// <param name="value">The input.</param>
	/// <param name="strings">The strings.</param>
	/// <returns></returns>
	public static string _Remove(this string value, params string[] strings)
	{
		__.GetLogger()._EzError(value is not null);
		return strings.Aggregate(value, (current, c) => current.Replace(c, string.Empty));
		//var result = value;
		//if (!string.IsNullOrEmpty(result) && removeStrings != null)
		//  Array.ForEach(removeStrings, s => result = result.Replace(s, string.Empty));

		//return result;
	}

	/// <summary>Finds out if the specified string contains null, empty or consists only of white-space characters</summary>
	/// <param name="value">The input string</param>
	public static bool _IsNullOrWhiteSpace(this string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			foreach (var c in value)
			{
				if (!char.IsWhiteSpace(c))
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	///    returns the acronym from the given sentence, with inclusion of camelCases
	///    <example>"The first SimpleExample   ... startsHere!" ==> "TfSEsH"</example>
	/// </summary>
	/// <param name="camelCaseSentence"></param>
	/// <returns></returns>
	public static string _ToAcronym(this string camelCaseSentence)
	{
		__.GetLogger()._EzError(camelCaseSentence is not null);
		if (camelCaseSentence == null)
		{
			return null;
		}

		camelCaseSentence = camelCaseSentence.Trim();

		string toReturn = string.Empty;

		foreach (var camelCaseWord in camelCaseSentence.Split(' '))
		{
			toReturn += camelCaseWord._GetAcronymHelper();
		}

		return toReturn;
	}

	private static string _GetAcronymHelper(this string camelCaseWord)
	{
		__.GetLogger()._EzError(camelCaseWord is not null);
		if (camelCaseWord == null)
		{
			return string.Empty;
		}

		camelCaseWord = camelCaseWord.Trim();

		if (camelCaseWord.Length == 0)
		{
			return string.Empty;
		}

		string toReturn = string.Empty;
		int firstFoundChar = 0;
		for (; firstFoundChar < camelCaseWord.Length; firstFoundChar++)
			if (char.IsLetter(camelCaseWord, firstFoundChar))
			{
				toReturn += camelCaseWord[firstFoundChar];
				break;
			}

		for (int i = firstFoundChar + 1; i < camelCaseWord.Length; i++)
			if (char.IsUpper(camelCaseWord, i))
			{
				toReturn += camelCaseWord[i];
			}

		return toReturn;
	}

	/// <summary>Uppercase First Letter</summary>
	/// <param name="value">The string value to process</param>
	public static string _ToUpperFirstLetter(this string value)
	{
		if (value._IsNullOrWhiteSpace())
		{
			return string.Empty;
		}

		char[] valueChars = value.ToCharArray();
		valueChars[0] = char.ToUpper(valueChars[0], CultureInfo.InvariantCulture);

		return new string(valueChars);
	}
	public static string _ToLowerFirstLetter(this string value)
	{
		if (value._IsNullOrWhiteSpace())
		{
			return string.Empty;
		}

		char[] valueChars = value.ToCharArray();
		valueChars[0] = char.ToLower(valueChars[0], CultureInfo.InvariantCulture);

		return new string(valueChars);
	}

	/// <summary>
	///    Returns the left part of the string.
	/// </summary>
	/// <param name="value">The original string.</param>
	/// <param name="characterCount">The character count to be returned.</param>
	/// <returns>The left part</returns>
	public static string _Left(this string value, int characterCount, bool throwIfTooShort = false)
	{
		if (throwIfTooShort)
		{
			return value.Substring(0, characterCount);
		}
		else
		{
			return value.Substring(0, Math.Min(characterCount, value.Length));
		}
	}

	/// <summary>
	///    Returns the Right part of the string.
	/// </summary>
	/// <param name="value">The original string.</param>
	/// <param name="characterCount">The character count to be returned.</param>
	/// <returns>The right part</returns>
	public static string _Right(this string value, int characterCount, bool throwIfTooShort = false)
	{
		if (throwIfTooShort)
		{
			return value.Substring(value.Length - characterCount);
		}
		else
		{
			return value.Substring(value.Length - Math.Min(characterCount, value.Length));
		}
	}

	/// <summary>Returns the right part of the string from index.</summary>
	/// <param name="value">The original value.</param>
	/// <param name="index">The start index for substringing.</param>
	/// <returns>The right part.</returns>
	public static string _SubstringFrom(this string value, int index)
	{
		return index < 0 ? value : value.Substring(index, value.Length - index);
	}


	public static string _ToPlural(this string singular)
	{
		// Multiple words in the form A of B : Apply the plural to the first word only (A)
		int index = singular.LastIndexOf(" of ", StringComparison.OrdinalIgnoreCase);
		if (index > 0)
		{
			return singular.Substring(0, index) + singular.Remove(0, index)._ToPlural();
		}

		// single Word rules
		//sibilant ending rule
		if (singular.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		if (singular.EndsWith("c", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		if (singular.EndsWith("us", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		if (singular.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		//-ies rule
		if (singular.EndsWith("y", StringComparison.OrdinalIgnoreCase))
		{
			return singular.Remove(singular.Length - 1, 1) + "ies";
		}

		// -oes rule
		if (singular.EndsWith("o", StringComparison.OrdinalIgnoreCase))
		{
			return singular.Remove(singular.Length - 1, 1) + "oes";
		}

		// -s suffix rule
		return singular + "s";
	}

	/// <summary>
	///    Makes the current instance HTML safe.
	/// </summary>
	/// <param name="s">The current instance.</param>
	/// <returns>An HTML safe string.</returns>
	public static string _ToHtmlSafe(this string s)
	{
		return s._ToHtmlSafe(false, false);
	}

	/// <summary>
	///    Makes the current instance HTML safe.
	/// </summary>
	/// <param name="s">The current instance.</param>
	/// <param name="all">Whether to make all characters entities or just those needed.</param>
	/// <returns>An HTML safe string.</returns>
	public static string _ToHtmlSafe(this string s, bool all)
	{
		return s._ToHtmlSafe(all, false);
	}

	/// <summary>
	///    Makes the current instance HTML safe.
	/// </summary>
	/// <param name="s">The current instance.</param>
	/// <param name="all">Whether to make all characters entities or just those needed.</param>
	/// <param name="replace">Whether or not to encode spaces and line breaks.</param>
	/// <returns>An HTML safe string.</returns>
	public static string _ToHtmlSafe(this string s, bool all, bool replace)
	{
		if (s._IsNullOrWhiteSpace())
		{
			return string.Empty;
		}

		var entities = new[]
		{
			0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 29,
			30, 31, 34, 39, 38, 60, 62, 123, 124, 125, 126, 127, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170,
			171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191,
			215, 247, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210,
			211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231,
			232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252,
			253, 254, 255, 256, 8704, 8706, 8707, 8709, 8711, 8712, 8713, 8715, 8719, 8721, 8722, 8727, 8730, 8733,
			8734, 8736, 8743, 8744, 8745, 8746, 8747, 8756, 8764, 8773, 8776, 8800, 8801, 8804, 8805, 8834, 8835, 8836,
			8838, 8839, 8853, 8855, 8869, 8901, 913, 914, 915, 916, 917, 918, 919, 920, 921, 922, 923, 924, 925, 926,
			927, 928, 929, 931, 932, 933, 934, 935, 936, 937, 945, 946, 947, 948, 949, 950, 951, 952, 953, 954, 955,
			956, 957, 958, 959, 960, 961, 962, 963, 964, 965, 966, 967, 968, 969, 977, 978, 982, 338, 339, 352, 353,
			376, 402, 710, 732, 8194, 8195, 8201, 8204, 8205, 8206, 8207, 8211, 8212, 8216, 8217, 8218, 8220, 8221,
			8222, 8224, 8225, 8226, 8230, 8240, 8242, 8243, 8249, 8250, 8254, 8364, 8482, 8592, 8593, 8594, 8595, 8596,
			8629, 8968, 8969, 8970, 8971, 9674, 9824, 9827, 9829, 9830
		};
		var sb = new StringBuilder();
		foreach (var c in s)
		{
			if (all || entities.Contains(c))
			{
				sb.Append("&#" + (int)c + ";");
			}
			else
			{
				sb.Append(c);
			}
		}

		return replace
			? sb.Replace("", "<br />").Replace("\n", "<br />").Replace(" ", "&nbsp;").ToString()
			: sb.ToString();
	}

	#endregion

	#region Regex based extension methods

	/// <summary>
	///    Uses regular expressions to determine if the string matches to a given regex pattern.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>
	///    <c>true</c> if the value is matching to the specified pattern; otherwise, <c>false</c>.
	/// </returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var isMatching = s.IsMatchingTo(@"^\d+$");
	/// 	</code>
	/// </example>
	public static bool _Equals(this string value, string regexPattern, RegexOptions options)
	{
		return Regex.IsMatch(value, regexPattern, options);
	}

	/// <summary>
	///    replace all instances of the given characters with the given value
	/// </summary>
	/// <param name="value"></param>
	/// <param name="toReplace"></param>
	/// <param name="newValue"></param>
	/// <returns></returns>
	public static string _Replace(this string value, string charsToReplace, char? replacementChar,
		StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
	{
		var sb = new StringBuilder(value.Length);
		foreach (var c in value)
		{
			if (charsToReplace.Contains(c, stringComparison))
			{
				if (replacementChar.HasValue)
				{
					//append replacement
					sb.Append(replacementChar.Value);
				}
				//do nothing
			}
			else
			{
				//char is okay
				sb.Append(c);
			}
		}

		return sb.ToString();
	}

	/// <summary>
	///    Uses regular expressions to replace parts of a string.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="replaceValue">The replacement value.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>The newly created string</returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var replaced = s.ReplaceWith(@"\d", m => string.Concat(" -", m.Value, "- "));
	/// 	</code>
	/// </example>
	public static string _Replace(this string value, string regexPattern, string replaceValue, RegexOptions options)
	{
		return Regex.Replace(value, regexPattern, replaceValue, options);
	}

	/// <summary>
	///    Uses regular expressions to replace parts of a string.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="evaluator">The replacement method / lambda expression.</param>
	/// <returns>The newly created string</returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var replaced = s.ReplaceWith(@"\d", m => string.Concat(" -", m.Value, "- "));
	/// 	</code>
	/// </example>
	public static string _Replace(this string value, string regexPattern, MatchEvaluator evaluator)
	{
		return value._Replace(regexPattern, RegexOptions.None, evaluator);
	}

	/// <summary>
	///    Uses regular expressions to replace parts of a string.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <param name="evaluator">The replacement method / lambda expression.</param>
	/// <returns>The newly created string</returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var replaced = s.ReplaceWith(@"\d", m => string.Concat(" -", m.Value, "- "));
	/// 	</code>
	/// </example>
	public static string _Replace(this string value, string regexPattern, RegexOptions options, MatchEvaluator evaluator)
	{
		return Regex.Replace(value, regexPattern, evaluator, options);
	}


	public static string _ReplaceFirst(this string text, string search, string replace)
	{
		int length = text.IndexOf(search);
		return length < 0 ? text : text.Substring(0, length) + replace + text.Substring(length + search.Length);
	}
	public static string _ReplaceLast(this string text, string search, string replace)
	{
		int length = text.LastIndexOf(search);
		return length < 0 ? text : text.Substring(0, length) + replace + text.Substring(length + search.Length);
	}


	/// <summary>
	///    Uses regular expressions to determine all matches of a given regex pattern.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <returns>A collection of all matches</returns>
	public static MatchCollection _GetMatches(this string value, string regexPattern)
	{
		return value._GetMatches(regexPattern, RegexOptions.None);
	}

	/// <summary>
	///    Uses regular expressions to determine all matches of a given regex pattern.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>A collection of all matches</returns>
	public static MatchCollection _GetMatches(this string value, string regexPattern, RegexOptions options)
	{
		return Regex.Matches(value, regexPattern, options);
	}
	/// <summary>
	/// a string extension method that splits a long string into substrings of a fixed length, and returns all as a List<string>.   any remainder is also returned.
	/// </summary>
	public static List<string> _Split(this string str, int chunkSize)
	{
		if (str is null)
		{
			throw new ArgumentNullException(nameof(str));
		}
		if (string.IsNullOrEmpty(str))
			return new List<string>();

		if (chunkSize <= 0)
			throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

		var chunks = new List<string>();

		for (int i = 0; i < str.Length; i += chunkSize)
		{
			if (i + chunkSize <= str.Length)
			{
				chunks.Add(str.Substring(i, chunkSize));
			}
			else
			{
				chunks.Add(str.Substring(i));
			}
		}

		return chunks;
	}

	/// <summary>
	///    Uses regular expressions to split a string into parts.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <returns>The splitted string array</returns>
	public static string[] _Split(this string value, string regexPattern)
	{
		return value._Split(regexPattern, RegexOptions.None);
	}

	/// <summary>
	///    Uses regular expressions to split a string into parts.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>The splitted string array</returns>
	public static string[] _Split(this string value, string regexPattern, RegexOptions options)
	{
		return Regex.Split(value, regexPattern, options);
	}

	/// <summary>
	///    Splits the given string into words and returns a string array.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <returns>The splitted string array</returns>
	public static string[] _GetWords(this string value)
	{
		return value.Split(@"\W");
	}

	/// <summary>
	///    Gets the nth "word" of a given string, where "words" are substrings separated by a given separator
	/// </summary>
	/// <param name="value">The string from which the word should be retrieved.</param>
	/// <param name="index">Index of the word (0-based).</param>
	/// <returns>
	///    The word at position n of the string.
	///    Trying to retrieve a word at a position lower than 0 or at a position where no word exists results in an exception.
	/// </returns>
	/// <remarks>
	///    Originally contributed by MMathews
	/// </remarks>
	public static string _GetWordByIndex(this string value, int index)
	{
		var words = value._GetWords();

		if (index < 0 || index > words.Length - 1)
		{
			throw new ArgumentOutOfRangeException("index", "The word number is out of range.");
		}

		return words[index];
	}


	/// <summary>
	///    converts a string to a stripped down version, only allowing alphaNumeric plus a single whiteSpace character
	///    (customizable with default being '_' )
	///    <para>
	///       note: leading and trailing whiteSpace is trimmed, and internal whiteSpace is truncated down to single
	///       characters
	///    </para>
	///    <para>This can be used to "safe encode" strings before use with xml</para>
	///    <para>example:  "Hello, World!" ==> "Hello_World"</para>
	/// </summary>
	/// <param name="toConvert">special case: if null, "null" is returned</param>
	/// <param name="whiteSpace">
	///    char to use as whiteSpace.  set to null to not write any whiteSpace (alphaNumeric chars only)
	///    default is underscore '_'
	/// </param>
	/// <returns></returns>
	public static string _ConvertToAlphanumeric(this string toConvert, char? whiteSpace = '_')
	{
		if (toConvert is null)
		{
			return "null";
		}
		var sb = new StringBuilder(toConvert.Length);

		bool includeWhitespace = whiteSpace.HasValue;
		bool isWhitespace = false;
		foreach (var c in toConvert)
		{
			if (c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
			{
				sb.Append(c);
				isWhitespace = false;
			}
			else
			{
				if (!isWhitespace && includeWhitespace)
				{
					sb.Append(whiteSpace.Value);
				}

				isWhitespace = true;
			}
		}

		string toReturn = sb.ToString();

		if (includeWhitespace)
		{
			return toReturn.Trim(whiteSpace.Value);
		}

		return toReturn;
	}

	/// <summary>
	///    converts a string to a stripped down version, only allowing alphaNumeric plus a single whiteSpace character
	///    (customizable with default being '_' )
	///    <para>
	///       note: leading and trailing whiteSpace is trimmed, and internal whiteSpace is truncated down to single
	///       characters
	///    </para>
	///    <para>This can be used to "safe encode" strings before use with xml</para>
	///    <para>example:  "Hello, World!" ==> "Hello_World"</para>
	/// </summary>
	/// <param name="toConvert"></param>
	/// <param name="whiteSpace">
	///    char to use as whiteSpace.  set to null to not write any whiteSpace (alphaNumeric chars only)
	///    default is underscore '_'
	/// </param>
	/// <returns></returns>
	public static string _ConvertToAlphanumericCapitalize(this string toConvert, char? whiteSpace = '_')
	{
		var toReturn = _ConvertToAlphanumeric(toConvert, whiteSpace);
		if (toReturn.Length > 0)
		{
			toReturn = char.ToUpper(toReturn[0]) + toReturn.Substring(1);
		}
		return toReturn;
		//var sb = new StringBuilder(toConvert.Length);

		//bool includeWhitespace = whiteSpace.HasValue;
		//bool isWhitespace = false;
		//foreach (var c in toConvert)
		//{
		//	if (c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
		//	{
		//		sb.Append(c);
		//		isWhitespace = false;
		//	}
		//	else
		//	{
		//		if (!isWhitespace && includeWhitespace)
		//		{
		//			sb.Append(whiteSpace.Value);
		//		}

		//		isWhitespace = true;
		//	}
		//}



		//string toReturn = sb.ToString();

		//if (includeWhitespace)
		//{
		//	toReturn = toReturn.Trim(whiteSpace.Value);
		//}
		//if (toReturn.Length > 0)
		//{
		//	toReturn = char.ToUpper(toReturn[0]) + toReturn.Substring(1);
		//}
		//return toReturn;
	}

	#endregion
}

public static class zz_Extensions_Assembly
{
	/// <summary>
	/// if you are using GitVersion, gets the sha shorthash (first 7 digits) from AssemblyInformationalVersionAttribute
	/// <para>otherwise returns "?"</para>
	/// </summary>
	public static string _GetGitShortHash(this Assembly assembly)
	{
		return assembly._GetGitHash()._Left(7);
	}
	/// <summary>
	/// if you are using GitVersion, gets the sha hash from AssemblyInformationalVersionAttribute
	/// <para>otherwise returns "?"</para>
	/// </summary>
	public static string _GetGitHash(this Assembly assembly)
	{
		var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

		var hash = @".*\.Sha\.([a-z\d]*).*"._ToRegex()._FirstMatch(info);
		if (string.IsNullOrWhiteSpace(hash))
		{
			return "?";
		}
		return hash;
	}

}

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_Array
{
	//public static bool Contains<TValue>(this TValue[] array, TValue value) where TValue : IEquatable<TValue>
	//{
	//   foreach (var item in array)
	//   {
	//      if (value.Equals(item))
	//      {
	//         return true;
	//      }
	//   }
	//   return false;
	//}

	//public static Span<T> _Reverse<T>(this T[] array)
	//{
	//	array.Reverse();
	//	return array;
	//}


	/// <summary>
	///    Find the first occurence of an byte[] in another byte[]
	/// </summary>
	/// <param name="toSearchInside">the byte[] to search in</param>
	/// <param name="toFind">the byte[] to find</param>
	/// <returns>the first position of the found byte[] or -1 if not found</returns>
	/// <remarks>
	///    Contributed by blaumeister, http://www.codeplex.com/site/users/view/blaumeiser
	/// </remarks>
	public static int _FindArrayInArray<T>(this T[] toSearchInside, T[] toFind)
	{
		int i, j;
		for (j = 0; j < toSearchInside.Length - toFind.Length; j++)
		{
			for (i = 0; i < toFind.Length; i++)
				if (!Equals(toSearchInside[j + i], toFind[i]))
				{
					break;
				}

			if (i == toFind.Length)
			{
				return j;
			}
		}

		return -1;
	}


	public static void _Fill<T>(this T[] array, T value)
	{
		array._Fill(value, 0, array.Length);
	}

	public static void _Fill<T>(this T[] array, T value, int index, int count)
	{
		for (int i = index; i < index + count; i++)
			array[i] = value;
	}

	/// <summary>
	///    creates a new array with the values from this and <see cref="other" />  (joins the two arrays together)
	///    <para>note: this is a simple copy, it does not skip empty elements, etc</para>
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="array"></param>
	/// <param name="other"></param>
	/// <returns></returns>
	public static T[] _Join<T>(this T[] array, params T[] other)
	{
		T[] toReturn = new T[array.Length + other.Length];
		Array.Copy(array, toReturn, array.Length);
		Array.Copy(other, 0, toReturn, array.Length, other.Length);
		return toReturn;
	}


	public static void _CopyTo<T>(this T[] array, T[] other)
	{
		__.GetLogger()._EzError(array.Length == other.Length);
		Array.Copy(array, other, array.Length);
	}

	public static T[] _Copy<T>(this T[] array, int index = 0, int? count = null)
	{
		if (!count.HasValue)
		{
			count = array.Length - index;
		}

		var toReturn = new T[count.Value];
		Array.Copy(array, index, toReturn, 0, count.Value);
		return toReturn;
	}

	/// <summary>
	///    quickly clears an array
	/// </summary>
	/// <param name="?"></param>
	public static void _Clear(this Array array)
	{
		array._Clear(0, array.Length);
	}

	public static void _Clear(this Array array, int offset, int count)
	{
		Array.Clear(array, offset, count);
	}


	/// <summary>
	///    invokes .Clone on all elements, only works when items are cloneable and class
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="array"></param>
	/// <returns></returns>
	public static T[] _Clone<T>(this T[] array)
		where T : ICloneable
	{
		var toReturn = new T[array.Length];
		for (int i = 0; i < array.Length; i++)
			//__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);
			//if (array[i] != null)
			toReturn[i] = (T)array[i].Clone();
		//__.GetLogger()._EzError(toReturn[i] != null);
		return toReturn;
	}

	///// <summary>
	///// 
	///// </summary>
	///// <typeparam name="T"></typeparam>
	///// <param name="array"></param>
	///// <param name="task">args = <see cref="array"/>, startInclusive, endExclusive</param>
	///// <param name="offset"></param>
	///// <param name="count"></param>
	//public static void _ParallelFor<T>(this T[] array, Action<T[], int, int> task, int offset = 0, int count = -1)
	//{
	//	count = count == -1 ? array.Length - offset : count;
	//	StormPool.instance.ParallelFor(array, offset, count, task);
	//}
}

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
public static class zz_Extensions_ByteArray
{

	/// <summary>
	/// convert into a base64 encoded string
	/// </summary>
	/// <param name="input"></param>
	/// <param name="stripPadding">default false.  if true, will remove the customary '=' padding (if any)</param>
	/// <returns></returns>
	public static string _ToBase64(this byte[] input, bool stripPadding = false)
	{
		var base64 = Convert.ToBase64String(input);

		if (stripPadding is true && base64.EndsWith('='))
		{
			return base64.TrimEnd('=');
		}
		return base64;
	}


	//[Conditional("TEST")]
	//public static void _UnitTests()
	//{
	//	//unit tests

	//	//string to bytes roundtrip
	//	string helloWorld = "hello, World!";
	//	var toBytes = helloWorld._ToBytes(Encoding.UTF8, true);
	//	string backToString;
	//	if (!toBytes._TryConvertToStringWithPreamble(out backToString))
	//	{
	//		__.GetLogger()._EzError(false);
	//	}
	//	__.GetLogger()._EzError(helloWorld == backToString);

	//	//compression roundtrip
	//	var originalBytes = helloWorld._ToBytes(Encoding.Unicode);
	//	var compressedBytes = new byte[0];
	//	int compressLength;
	//	originalBytes._Compress(ref compressedBytes, out compressLength);
	//	var decompressedBytes = new byte[0];
	//	int decompressLength;
	//	var result = compressedBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(result);
	//	var decompressedString = decompressedBytes._ToUnicodeString(Encoding.Unicode, 0, decompressLength);
	//	__.GetLogger()._EzError(helloWorld == decompressedString);

	//	//decompression safe errors (no exceptions)

	//	var emptyBytes = new byte[0];
	//	result = emptyBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(result, "0 bytes should decode to 0 len output");

	//	var bigEmptyBytes = new byte[1000];
	//	result = bigEmptyBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(!result);

	//	var bigJunkBytes = new byte[1000];
	//	bigJunkBytes.Fill(byte.MaxValue);
	//	result = bigJunkBytes._TryDecompress(ref decompressedBytes, out decompressLength);
	//	__.GetLogger()._EzError(!result);

	//}


	private static Encoding[] possibleEncodings = { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode };

	/// <summary>
	///  <para>Sometimes you need to convert a string directly into bytes.  This converts it back.  (NOT the same as base64 encoding)</para>
	///  convert a byte[] to a string (will detect byte encoding automatically)
	///    <para>Note: if no encoding preamble is detected, FALSE is returned.</para>
	/// </summary>
	/// <param name="input"></param>
	/// <returns></returns>
	/// <remarks>
	///    Note that ASCII does not include a preamble, so will always fail.  use .<see cref="ToUnicodeString" />
	///    (Encoding.ASCII) explicitly if you have ascii text
	/// </remarks>   
	public static bool _TryConvertToStringWithPreamble(this byte[] input, out string output, int start = 0,
		int? count = null)
	{
		count = count ?? input.Length - start;

		Encoding encoding = null;
		byte[] preamble = null;
		foreach (var possibleEncoding in possibleEncodings)
		{
			//var potentialEncoding = encodingInfo.GetEncoding();
			preamble = possibleEncoding.GetPreamble();

			if (preamble.Length > 0) //only allow encodings that use preambles
			{
				if (input._Compare(preamble, start, 0, preamble.Length) == 0)
				{
					encoding = possibleEncoding;
					break;
				}
			}
		}

		if (encoding == null)
		{
			////if no encoding detected, default fail
			output = null;
			return false;
		}

		output = encoding.GetString(input, start + preamble.Length, count.Value - preamble.Length);

		return true;
	}


	/// <summary>
	/// <para>Sometimes you need to convert a string directly into bytes.  This converts it back.  (NOT the same as base64 encoding)</para>
	///    convert a byte[] to a string.  no preamble is allowed, it just quickly converts the bytes to the given
	///    <see cref="encoding" /> (no safety checks!)
	///    <para>Note: if no encoding is specified, UTF8 is used</para>
	/// </summary>
	public static string _ToUnicodeString(this byte[] input)
	{
		return input._ToUnicodeString(Encoding.UTF8, 0, input.Length);
	}

	/// <summary>
	/// <para>Sometimes you need to convert a string directly into bytes.  This converts it back.  (NOT the same as base64 encoding)</para>
	///    convert a byte[] to a string.  no preamble is allowed, it just quickly converts the bytes to the given
	///    <see cref="encoding" /> (no safety checks!)
	///    <para>Note: if no encoding is specified, UTF8 is used</para>
	/// </summary>
	public static string _ToUnicodeString(this byte[] input, Encoding encoding, int index = 0, int? count = null)
	{
		count = count ?? input.Length - index;
		foreach (var possible in possibleEncodings)
		{
			__.GetLogger()._EzError(input._FindArrayInArray(possible.GetPreamble()) != 0,
				$"input starts with {possible}.Preamble, should use Preamble aware method instead");
		}

		return encoding.GetString(input, index, count.Value);
	}

	public static string _ToHex(this byte[] stringBytes)
	{
		StringBuilder outputString = new(stringBytes.Length * 2);
		foreach (var value in stringBytes)
		{
			outputString.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", value);
		}

		return outputString.ToString();
	}

	//public static string ToString(this byte[] stringBytes)
	//{

	//    var encoding = Encoding.GetEncoding()
	//    return Encoding.GetEncoding.GetString(unicodeStringBytes);


	//}

	public static void _ToArray(this byte[] bytes, int start, int count, out float[] floats)
	{
		//__.ERROR.AssertOnce("add unitTest for endianness");

		__.GetLogger()._EzError(count % 4 == 0, "count should be multiple of 4!!");
		__.GetLogger()._EzError(bytes.Length >= start + count, "byte array out of bounds!!");

		int floatCount = count / 4;
		int bytesPosition = start;

		floats = new float[floatCount];
		for (int i = 0; i < floatCount; i++)
		{
			floats[i] = BitConverter.ToSingle(bytes, bytesPosition);
			bytesPosition += 4;
		}
	}

	/// <summary>
	///    convert a byte array to an int array
	/// </summary>
	/// <param name="bytes"></param>
	/// <param name="start"></param>
	/// <param name="count"></param>
	/// <param name="intArray"></param>
	public static void _ToArray(this byte[] bytes, int start, int count, out int[] intArray)
	{
		//__.ERROR.AssertOnce("add unitTest for endianness");

		__.GetLogger()._EzError(count % 4 == 0, "count should be multiple of 4!!");
		__.GetLogger()._EzError(bytes.Length >= start + count, "byte array out of bounds!!");

		int floatCount = count / 4;
		int bytesPosition = start;

		intArray = new int[floatCount];
		for (int i = 0; i < floatCount; i++)
		{
			intArray[i] = BitConverter.ToInt32(bytes, bytesPosition);
			bytesPosition += 4;
		}
	}

	/// <summary>
	///    compare 2 arrays
	/// </summary>
	/// <param name="thisArray"></param>
	/// <param name="toCompare"></param>
	/// <param name="thisStartPosition"></param>
	/// <param name="compareStartPosition"></param>
	/// <param name="length"></param>
	/// <returns></returns>
	public static int _Compare(this byte[] thisArray, byte[] toCompare, int thisStartPosition, int compareStartPosition,
		int length)
	{
		var thisPos = thisStartPosition;
		var comparePos = compareStartPosition;

		if (length == -1)
		{
			length = toCompare.Length;
		}

		for (int i = 0; i < length; i++)
		{
			if (thisArray[thisPos] != toCompare[comparePos])
			{
				return toCompare[comparePos] - thisArray[thisPos];
			}

			thisPos++;
			comparePos++;
		}

		return 0;
	}

	public static int _Compare(this byte[] thisArray, byte[] toCompare)
	{
		return thisArray._Compare(toCompare, 0, 0, -1);
	}

	public static void _ToArray(this byte[] bytes, int start, int count, out short[] shorts)
	{
		__.GetLogger()._EzError(count % sizeof(short) == 0, "count should be multiple of shorts!!");
		__.GetLogger()._EzError(bytes.Length >= start + count, "byte array out of bounds!!");

		int shortCount = count / sizeof(short);
		int bytesPosition = start;

		shorts = new short[shortCount];
		for (int i = 0; i < shortCount; i++)
		{
			shorts[i] = BitConverter.ToInt16(bytes, bytesPosition);
			bytesPosition += sizeof(short);
		}
	}


	//public static void _Compress(this byte[] inputUncompressed, ref byte[] resizableOutputTarget, out int outputLength)
	//{
	//	inputUncompressed._Compress(0, inputUncompressed.Length, ref resizableOutputTarget, out outputLength);
	//}
	////[Placeholder("need snappy instead of this low perf zip stuff")]
	//[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
	//public static void _Compress(this byte[] inputUncompressed, int offset, int count, ref byte[] resizableOutputTarget, out int outputLength)
	//{
	//	//stupid Ionic.Zlib way, very object and performance wastefull
	//	{
	//		using (var ms = new MemoryStream())
	//		{
	//			Stream compressor =
	//				new ZlibStream(ms, CompressionMode.Compress, CompressionLevel.BestSpeed);

	//			using (compressor)
	//			{
	//				compressor.Write(inputUncompressed, offset, count);

	//			}

	//			resizableOutputTarget = ms.ToArray();
	//			outputLength = (int)resizableOutputTarget.Length;
	//		}
	//	}
	//	//below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//	{

	//		//object obj;
	//		//if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Compress", out obj))
	//		//{
	//		//   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Compress,
	//		//                                   Ionic.Zlib.CompressionLevel.BestSpeed);
	//		//   updateState.Tags.Add("Novaleaf.Byte[].Compress", obj);
	//		//}
	//		//var compressor = obj as Ionic.Zlib.ZlibStream;
	//		////reset our stream
	//		//compressor._baseStream.SetLength(0);
	//		//compressor.Write(inputUncompressed, offset, count);

	//		//compressor.Flush();

	//		//compressor.Close();

	//		//var memoryStream = compressor._baseStream._stream as MemoryStream;
	//		//__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");

	//		//outputLength = (int)memoryStream.Length;
	//		//outputCompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//	}
	//}

	//////public static void Compress(this byte[] inputUncompressed, int offset, int count, FrameState updateState, out byte[] outputCompressed_TEMP_SCRATCH, out int outputLength)
	//////{
	//////   updateState.AssertIsAlive();
	//////   //stupid Ionic.Zlib way, very object and performance wastefull
	//////   {
	//////      using (var ms = new MemoryStream())
	//////      {
	//////         Stream compressor =
	//////            new Ionic.Zlib.ZlibStream(ms, Ionic.Zlib.CompressionMode.Compress, Ionic.Zlib.CompressionLevel.BestSpeed);

	//////         using (compressor)
	//////         {
	//////            compressor.Write(inputUncompressed, offset, count);

	//////         }
	//////         outputCompressed_TEMP_SCRATCH = ms.ToArray();
	//////         outputLength = (int)outputCompressed_TEMP_SCRATCH.Length;
	//////      }
	//////   }
	//////   //below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//////   {

	//////      //object obj;
	//////      //if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Compress", out obj))
	//////      //{
	//////      //   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Compress,
	//////      //                                   Ionic.Zlib.CompressionLevel.BestSpeed);
	//////      //   updateState.Tags.Add("Novaleaf.Byte[].Compress", obj);
	//////      //}
	//////      //var compressor = obj as Ionic.Zlib.ZlibStream;
	//////      ////reset our stream
	//////      //compressor._baseStream.SetLength(0);
	//////      //compressor.Write(inputUncompressed, offset, count);

	//////      //compressor.Flush();

	//////      //compressor.Close();

	//////      //var memoryStream = compressor._baseStream._stream as MemoryStream;
	//////      //__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");

	//////      //outputLength = (int)memoryStream.Length;
	//////      //outputCompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//////   }
	//////}


	//	/// <summary>
	//	/// decompress bytes that were compressed using our <see cref="Compress"/> method.   
	//	/// if fails (data corruption, etc), the returning false and <see cref="outputLength"/> -1
	//	/// </summary>
	//	/// <param name="inputCompressed"></param>
	//	/// <param name="offset"></param>
	//	/// <param name="count"></param>
	//	/// <param name="updateState"></param>
	//	/// <param name="outputUncompressed_TEMP_SCRATCH"></param>
	//	/// <param name="outputLength"></param>
	//	public static bool TryDecompress(this byte[] inputCompressed, ref byte[] resizableOutputTarget, out int outputLength)
	//	{
	//		return inputCompressed.TryDecompress(0, inputCompressed.Length, ref resizableOutputTarget, out outputLength);
	//	}


	//	/// <summary>
	//	/// decompress bytes that were compressed using our <see cref="Compress"/> method.   
	//	/// if fails (data corruption, etc), the returning false and <see cref="outputLength"/> -1
	//	/// </summary>
	//	/// <param name="inputCompressed"></param>
	//	/// <param name="offset"></param>
	//	/// <param name="count"></param>
	//	/// <param name="updateState"></param>
	//	/// <param name="outputUncompressed_TEMP_SCRATCH"></param>
	//	/// <param name="outputLength"></param>
	//	[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "offset"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "count"),]
	//	[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
	//	//[Placeholder("need snappy instead of this low perf zip stuff")]
	//	public static bool TryDecompress(this byte[] inputCompressed, int offset, int count, ref byte[] resizableOutputTarget, out int outputLength)
	//	{
	//		//stupid Ionic.Zlib way, very object and performance wastefull
	//		{
	//			using (var input = new MemoryStream(inputCompressed))
	//			{
	//				Stream decompressor =
	//					new ZlibStream(input, CompressionMode.Decompress);

	//				// workitem 8460
	//				byte[] working = new byte[1024];
	//				using (var output = new MemoryStream())
	//				{
	//					using (decompressor)
	//					{
	//						int n;
	//						while ((n = decompressor.Read(working, 0, working.Length)) != 0)
	//						{
	//							if (n == ZlibConstants.Z_DATA_ERROR)
	//							{
	//								//error with output
	//#if DEBUG
	//								if (resizableOutputTarget != null)
	//								{
	//									resizableOutputTarget.Clear();
	//								}
	//#endif
	//								outputLength = -1;
	//								return false;
	//							}
	//							output.Write(working, 0, n);
	//						}
	//					}

	//					resizableOutputTarget = output.ToArray();
	//					outputLength = resizableOutputTarget.Length;
	//					return true;
	//				}
	//			}
	//		}
	//		//below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//		{

	//			//object obj;
	//			//if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Decompress", out obj))
	//			//{
	//			//   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Decompress);

	//			//   updateState.Tags.Add("Novaleaf.Byte[].Decompress", obj);
	//			//}
	//			//var decompressor = obj as Ionic.Zlib.ZlibStream;

	//			//decompressor._baseStream.SetLength(0);
	//			//decompressor.Write(inputCompressed, offset, count);

	//			//decompressor.Flush();

	//			//var memoryStream = decompressor._baseStream._stream as MemoryStream;
	//			//__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");


	//			//outputLength = (int)memoryStream.Length;
	//			//outputUncompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//		}

	//	}


	//	///// <summary>
	//	///// decompress bytes that were compressed using our <see cref="Compress"/> method.   
	//	///// if fails (data corruption, etc), the returning <see cref="outputUncompressed_TEMP_SCRATCH"/> will be null and <see cref="outputLength"/> -1
	//	///// </summary>
	//	///// <param name="inputCompressed"></param>
	//	///// <param name="offset"></param>
	//	///// <param name="count"></param>
	//	///// <param name="updateState"></param>
	//	///// <param name="outputUncompressed_TEMP_SCRATCH"></param>
	//	///// <param name="outputLength"></param>
	//	//public static void Decompress(this byte[] inputCompressed, int offset, int count, FrameState updateState, out byte[] outputUncompressed_TEMP_SCRATCH, out int outputLength)
	//	//{
	//	//   updateState.AssertIsAlive();

	//	//   //stupid Ionic.Zlib way, very object and performance wastefull
	//	//   {
	//	//      using (var input = new MemoryStream(inputCompressed))
	//	//      {
	//	//         Stream decompressor =
	//	//            new Ionic.Zlib.ZlibStream(input, Ionic.Zlib.CompressionMode.Decompress);

	//	//         // workitem 8460
	//	//         byte[] working = new byte[1024];
	//	//         using (var output = new MemoryStream())
	//	//         {
	//	//            using (decompressor)
	//	//            {
	//	//               int n;
	//	//               while ((n = decompressor.Read(working, 0, working.Length)) != 0)
	//	//               {
	//	//                  if (n == Ionic.Zlib.ZlibConstants.Z_DATA_ERROR)
	//	//                  {
	//	//                     //error with output
	//	//                     outputUncompressed_TEMP_SCRATCH = null;
	//	//                     outputLength = -1;
	//	//                  }
	//	//                  output.Write(working, 0, n);
	//	//               }
	//	//            }
	//	//            outputUncompressed_TEMP_SCRATCH = output.GetBuffer();
	//	//            outputLength = (int)output.Length;
	//	//         }

	//	//      }
	//	//   }
	//	//   //below should work, but doesn't because of stupid implementation of Ionic.Zlib not allowing object reuse
	//	//   {

	//	//      //object obj;
	//	//      //if (!updateState.Tags.TryGetValue("Novaleaf.Byte[].Decompress", out obj))
	//	//      //{
	//	//      //   obj = new Ionic.Zlib.ZlibStream(new MemoryStream(), Ionic.Zlib.CompressionMode.Decompress);

	//	//      //   updateState.Tags.Add("Novaleaf.Byte[].Decompress", obj);
	//	//      //}
	//	//      //var decompressor = obj as Ionic.Zlib.ZlibStream;

	//	//      //decompressor._baseStream.SetLength(0);
	//	//      //decompressor.Write(inputCompressed, offset, count);

	//	//      //decompressor.Flush();

	//	//      //var memoryStream = decompressor._baseStream._stream as MemoryStream;
	//	//      //__.GetLogger()._EzError(memoryStream.Length < int.MaxValue / 2, "output too big!");


	//	//      //outputLength = (int)memoryStream.Length;
	//	//      //outputUncompressed_TEMP_SCRATCH = memoryStream.GetBuffer();
	//	//   }

	//	//}
}

public static class zz_Extensions_IServiceProvider
{
	public static T _GetService<T>(this IServiceProvider serviceProvider)
	{
		return (T)serviceProvider.GetService(typeof(T));
	}
}

public static class zz_Extensions_ProcessStartInfo
{
	[Obsolete(
		"doesn't work, can't redirect output if using shell exec.  needs to be reworked to store env vars in a file then extract")]
	private static AsyncLazy<Dictionary<string, string>> _asyncShellEnvironmentVariables = new(async () =>
	{
		var startInfo = new ProcessStartInfo();
		var envVars = new Dictionary<string, string>();

		// Check if the current OS is Windows
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			startInfo.FileName = "cmd.exe";
			startInfo.Arguments = "/c set";
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			startInfo.FileName = "/bin/bash";
			startInfo.Arguments = "-c env";
		}
		else
		{
			throw new NotSupportedException("Unsupported operating system");
		}

		startInfo.UseShellExecute = true;
		startInfo.RedirectStandardOutput = true;

		using (var process = Process.Start(startInfo))
		{
			string output = await process.StandardOutput.ReadToEndAsync();
			string[] lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string line in lines)
			{
				string[] parts = line.Split(new[] { '=' }, 2);
				envVars[parts[0]] = parts[1];
			}
		}

		return envVars;
	});

	[Obsolete(
		"doesn't work, can't redirect output if using shell exec.  needs to be reworked to store env vars in a file then extract")]
	public static async Task _ExecUsingShellEnvVars(this ProcessStartInfo startInfo)
	{
		//append shell env vars to process env vars
		var shellEnvVars = await _asyncShellEnvironmentVariables;
		foreach (var kvp in shellEnvVars)
		{
			startInfo.Environment[kvp.Key] = kvp.Value;
		}

		using var process = new Process
		{
			StartInfo = startInfo,
		};

		process.Start();
		await process.WaitForExitAsync();
	}

	public static async Task<(string stdOut, string stdErr)> _ExecCaptureIO(this ProcessStartInfo startInfo)
	{
		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardError = true;
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardInput = true;
		startInfo.CreateNoWindow = true;

		using var process = new Process
		{
			StartInfo = startInfo,
		};

		process.Start();

		var stdOut = await process.StandardOutput.ReadToEndAsync();
		var stdErr = await process.StandardError.ReadToEndAsync();

		return (stdOut, stdErr);
	}
}
