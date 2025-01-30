using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// sample code from @WrayGizard on Godot discord #csharp-discuss  can investigate this if mine has a problem....
/// </summary>
internal static class DedicatedThreads
{

	public static readonly int MainThreadId = System.Environment.CurrentManagedThreadId;
	public static readonly TaskScheduler MainThreadTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

	public static readonly DedicatedThread DbThread = new();

	public sealed class DedicatedThread : IDisposable
	{

		public int DedicatedThreadId { get; init; }
		public TaskScheduler TaskScheduler { get => _taskScheduler; }

		private readonly DedicatedThreadTaskScheduler _taskScheduler;

		public DedicatedThread()
		{
			_taskScheduler = new();
			DedicatedThreadId = _taskScheduler.DedicatedThread.ManagedThreadId;
		}

		private sealed class DedicatedThreadTaskScheduler : TaskScheduler, IDisposable
		{

			public Thread DedicatedThread { get; private set; }

			private BlockingCollection<Task> _tasks;
			private DedicatedThreadSynchronizationContext _syncContext;

			public DedicatedThreadTaskScheduler()
			{
				DedicatedThread = new Thread(Execute)
				{
					IsBackground = true
				};
				_tasks = new(new ConcurrentQueue<Task>());
				_syncContext = new DedicatedThreadSynchronizationContext(this);
				DedicatedThread.Start();
			}

			public override int MaximumConcurrencyLevel => 1;

			protected override void QueueTask(Task task)
			{
				_tasks.Add(task);
			}

			protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
			{
				if (Thread.CurrentThread != DedicatedThread) return false;
				return TryExecuteTask(task);
			}

			[System.Diagnostics.DebuggerNonUserCode]
			protected override IEnumerable<Task>? GetScheduledTasks()
			{
				return _tasks.ToArray();
			}

			private void Execute()
			{
				SynchronizationContext.SetSynchronizationContext(_syncContext);
				foreach (var task in _tasks.GetConsumingEnumerable())
				{
					try
					{
						TryExecuteTask(task);
					}
					catch (InvalidOperationException)
					{
					}
				}
			}

			void IDisposable.Dispose()
			{
				if (DedicatedThread != null)
				{
					_tasks.CompleteAdding();
					DedicatedThread.Join();
					_tasks.Dispose();
					_tasks = default!;
					_syncContext = default!;
					DedicatedThread = default!;
				}
			}

		}

		private sealed class DedicatedThreadSynchronizationContext(DedicatedThreadTaskScheduler taskScheduler) : SynchronizationContext
		{

			private readonly DedicatedThreadTaskScheduler _taskScheduler = taskScheduler;
			private readonly int _dedicatedThreadId = taskScheduler.DedicatedThread.ManagedThreadId;

			public override void Post(SendOrPostCallback d, object? state)
			{
				Task.Factory.StartNew
				(
					 () => d(state),
					 CancellationToken.None,
					 TaskCreationOptions.HideScheduler | TaskCreationOptions.DenyChildAttach,
					 _taskScheduler
				);
			}

			public override void Send(SendOrPostCallback d, object? state)
			{
				if (System.Environment.CurrentManagedThreadId == _dedicatedThreadId) d(state);
				else Task.Factory.StartNew
				(
					 () => d(state),
					 CancellationToken.None,
					 TaskCreationOptions.HideScheduler | TaskCreationOptions.DenyChildAttach,
					 _taskScheduler
				).GetAwaiter().GetResult();
			}

			public override SynchronizationContext CreateCopy()
			{
				return new DedicatedThreadSynchronizationContext(_taskScheduler);
			}

			public override int GetHashCode()
			{
				return _taskScheduler.GetHashCode();
			}

			public override bool Equals(object? obj)
			{
				return obj is DedicatedThreadSynchronizationContext other && _taskScheduler == other._taskScheduler;
			}

		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task Run(Action action)
		{
			return Task.Factory.StartNew
			(
				 action,
				 CancellationToken.None,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task Run<TState>(Action<TState> action, TState state)
		{
			return Task.Factory.StartNew
			(
				 () =>
				 {
					 action(state);
				 },
				 CancellationToken.None,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task Run(Action<CancellationToken> action, CancellationToken cancellationToken)
		{
			return Task.Factory.StartNew
			(
				 () =>
				 {
					 cancellationToken.ThrowIfCancellationRequested();
					 action(cancellationToken);
				 },
				 cancellationToken,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task Run<TState>(Action<TState, CancellationToken> action, TState state, CancellationToken cancellationToken)
		{
			return Task.Factory.StartNew
			(
				 () =>
				 {
					 cancellationToken.ThrowIfCancellationRequested();
					 action(state, cancellationToken);
				 },
				 cancellationToken,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<TResult> Run<TResult>(Func<TResult> func)
		{
			return Task.Factory.StartNew
			(
				 func,
				 CancellationToken.None,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<TResult> Run<TState, TResult>(Func<TState, TResult> func, TState state)
		{
			return Task.Factory.StartNew
			(
				 () =>
				 {
					 return func(state);
				 },
				 CancellationToken.None,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<TResult> Run<TResult>(Func<CancellationToken, TResult> func, CancellationToken cancellationToken)
		{
			return Task.Factory.StartNew
			(
				 () =>
				 {
					 cancellationToken.ThrowIfCancellationRequested();
					 return func(cancellationToken);
				 },
				 cancellationToken,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<TResult> Run<TState, TResult>(Func<TState, CancellationToken, TResult> func, TState state, CancellationToken cancellationToken)
		{
			return Task.Factory.StartNew
			(
				 () =>
				 {
					 cancellationToken.ThrowIfCancellationRequested();
					 return func(state, cancellationToken);
				 },
				 cancellationToken,
				 TaskCreationOptions.HideScheduler,
				 _taskScheduler
			);
		}

		[Conditional("DEBUG")]
		[DebuggerHidden]
		public void ThrowIfThreadMismatch()
		{
			if (System.Environment.CurrentManagedThreadId != DedicatedThreadId)
				throw new Exception($"Invalid thread! Running thread #{System.Environment.CurrentManagedThreadId}, but expected thread #{DedicatedThreadId}.");
		}

		public void Dispose()
		{
			((IDisposable)_taskScheduler).Dispose();
		}

	}

}