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

