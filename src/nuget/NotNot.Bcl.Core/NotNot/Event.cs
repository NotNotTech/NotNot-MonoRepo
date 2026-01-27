using NotNot.Advanced;

namespace NotNot;

/// <summary>
///    thread safe event registration and invoking.
///    subscriptions are stored as weakRefs so they can be garbage collected.
/// </summary>
public class Event<TEventArgs> where TEventArgs : EventArgs
{
	private bool _isInvoking;

	private List<WeakReference<EventHandler<TEventArgs>>> _storage = new();
	///// <summary>
	///// used in enumration when .Invoke() is called.
	///// </summary>
	//private List<WeakReference<EventHandler<TEventArgs>>> _storageTempCopy = new();

	/// <summary>
	///    (un)subscribe here
	/// </summary>
	public event EventHandler<TEventArgs> Handler
	{
		add
		{
			lock (_storage)
			{
				_storage.Add(new WeakReference<EventHandler<TEventArgs>>(value));
			}
		}
		remove
		{
			lock (_storage)
			{
				_storage._RemoveLast(x => x.TryGetTarget(out var target) && target == value);
			}
		}
	}

	/// <summary>
	///    only the owner should call this
	/// </summary>
	public void Invoke(object sender, TEventArgs args)
	{
		__.GetLogger()._EzError(_isInvoking is false, "multiple invokes occuring.  danger?  investigate.");
		_isInvoking = true;

		//__.assert.IsFalse(ThreadDiag.IsLocked(_storageTempCopy),"multiple invokes occuring.  danger?  investigate.");

		//lock (_storageTempCopy)
		using var _ = __.pool.Rent<List<WeakReference<EventHandler<TEventArgs>>>>(out var _storageTempCopy);
		__.GetLogger()._EzError(_storageTempCopy.Count == 0, "when recycling to pool, should always clear objects");
		{
			lock (_storage)
			{
				__.GetLogger()._EzError(_storageTempCopy.Count == 0);
				_storageTempCopy.AddRange(_storage);
			}

			var anyExpired = false;
			foreach (var weakRef in _storageTempCopy)
			{
				if (weakRef.TryGetTarget(out var handler))
				{
					handler(sender, args);
				}
				else
				{
					anyExpired = true;
				}
			}

			if (anyExpired)
			{
				_RemoveExpiredSubscriptions();
			}

			_storageTempCopy.Clear();
		}
		_isInvoking = false;
	}

	private void _RemoveExpiredSubscriptions()
	{
		lock (_storage)
		{
			//remove all expired weakrefs
			_storage.RemoveAll(weakRef => weakRef.TryGetTarget(out _) == false);
		}
	}

	////dispose removed as not needed (using weak ref)
	//private bool isDisposed;
	//public void Dispose()
	//{
	//	isDisposed = true;
	//	_storageTempCopy.Clear();
	//	_storage.Clear();
	//}
}

public class Event : Event<EventArgs>
{
}

/// <summary>
///    light weight event that doesn't pass sender
/// </summary>
public class ActionEvent<TArgs>
{
	private bool _isInvoking;

	private List<WeakReference<Action<TArgs>>> _storage = new();
	///// <summary>
	///// used in enumration when .Invoke() is called.
	///// </summary>
	//private List<WeakReference<EventHandler<TEventArgs>>> _storageTempCopy = new();

	/// <summary>
	///    (un)subscribe here
	/// </summary>
	public event Action<TArgs> Handler
	{
		add
		{
			lock (_storage)
			{
				_storage.Add(new WeakReference<Action<TArgs>>(value));
			}
		}
		remove
		{
			lock (_storage)
			{
				_storage._RemoveLast(x => x.TryGetTarget(out var target) && target == value);
			}
		}
	}

	/// <summary>
	///    only the owner should call this
	/// </summary>
	public void Raise(TArgs args)
	{
		if (_storage.Count == 0)
		{
			//nothing registered to listen
			return;
		}
		__.GetLogger()._EzError(_isInvoking is false, "multiple invokes occuring.  danger?  investigate.");
		_isInvoking = true;

		//__.assert.IsFalse(ThreadDiag.IsLocked(_storageTempCopy),"multiple invokes occuring.  danger?  investigate.");

		//lock (_storageTempCopy)
		using var _ = __.pool.Rent<List<WeakReference<Action<TArgs>>>>(out var _storageTempCopy);
		__.GetLogger()._EzError(_storageTempCopy.Count == 0, "when recycling to pool, should always clear objects");
		{
			lock (_storage)
			{
				__.GetLogger()._EzError(_storageTempCopy.Count == 0);
				_storageTempCopy.AddRange(_storage);
			}

			var anyExpired = false;
			foreach (var weakRef in _storageTempCopy)
			{
				if (weakRef.TryGetTarget(out var handler))
				{
					handler(args);
				}
				else
				{
					anyExpired = true;
				}
			}

			if (anyExpired)
			{
				_RemoveExpiredSubscriptions();
			}

			_storageTempCopy.Clear();
		}
		_isInvoking = false;
	}

	private void _RemoveExpiredSubscriptions()
	{
		lock (_storage)
		{
			//remove all expired weakrefs
			_storage.RemoveAll(weakRef => weakRef.TryGetTarget(out _) == false);
		}
	}
}


/// <summary>
///  light weight event that doesn't pass sender.  version of ActionEvent that takes a Span of args.
/// </summary>
/// <typeparam name="TArgs"></typeparam>
public class ActionEventSpan<TArgs> where TArgs : struct
{
	private bool _isInvoking;

	private List<WeakReference<Action<Span<TArgs>>>> _storage = new();
	///// <summary>
	///// used in enumration when .Invoke() is called.
	///// </summary>
	//private List<WeakReference<EventHandler<TEventArgs>>> _storageTempCopy = new();

	/// <summary>
	///    (un)subscribe here
	/// </summary>
	public event Action<Span<TArgs>> Handler
	{
		add
		{
			lock (_storage)
			{
				_storage.Add(new WeakReference<Action<Span<TArgs>>>(value));
			}
		}
		remove
		{
			lock (_storage)
			{
				_storage._RemoveLast(x => x.TryGetTarget(out var target) && target == value);
			}
		}
	}

	/// <summary>
	///    only the owner should call this
	/// </summary>
	public void Invoke(Span<TArgs> span_args)
	{
		if (_storage.Count == 0)
		{
			//nothing registered to listen
			return;
		}
		__.GetLogger()._EzError(_isInvoking is false, "multiple invokes occuring.  danger?  investigate.");
		_isInvoking = true;

		//__.assert.IsFalse(ThreadDiag.IsLocked(_storageTempCopy),"multiple invokes occuring.  danger?  investigate.");

		//lock (_storageTempCopy)
		using var _ = __.pool.Rent<List<WeakReference<Action<Span<TArgs>>>>>(out var _storageTempCopy);
		__.GetLogger()._EzError(_storageTempCopy.Count == 0, "when recycling to pool, should always clear objects");
		{
			lock (_storage)
			{
				__.GetLogger()._EzError(_storageTempCopy.Count == 0);
				_storageTempCopy.AddRange(_storage);
			}

			var anyExpired = false;
			foreach (var weakRef in _storageTempCopy)
			{
				if (weakRef.TryGetTarget(out var handler))
				{
					handler(span_args);
				}
				else
				{
					anyExpired = true;
				}
			}

			if (anyExpired)
			{
				_RemoveExpiredSubscriptions();
			}

			_storageTempCopy.Clear();
		}
		_isInvoking = false;
	}

	private void _RemoveExpiredSubscriptions()
	{
		lock (_storage)
		{
			//remove all expired weakrefs
			_storage.RemoveAll(weakRef => weakRef.TryGetTarget(out _) == false);
		}
	}
}


/// <summary>
/// Async event with WeakReference storage and sequential await for backpressure control.
/// Like <see cref="ActionEvent{TArgs}"/>, handlers can be garbage collected without explicit unsubscription.
/// </summary>
/// <remarks>
/// <para>
/// <b>WeakReference semantics:</b> Handlers are stored as weak references. Once the subscriber holding
/// the delegate is garbage collected, the handler is automatically cleaned up on next invocation.
/// Explicit unsubscription is optional but allows deterministic cleanup.
/// </para>
/// <para>
/// <b>Backpressure:</b> All handlers are awaited sequentially. A slow handler blocks subsequent handlers.
/// </para>
/// <para>
/// <b>Thread safety:</b> Subscribe/unsubscribe are thread-safe. Concurrent RaiseAsync calls are allowed
/// but handlers execute in subscription order within each call.
/// </para>
/// <para>
/// <b>Exception handling:</b> First exception bubbles to caller and stops invocation of remaining handlers.
/// </para>
/// <para>
/// <b>Lambda caution:</b> Anonymous lambdas not stored elsewhere may be collected before invocation.
/// For reliable delivery, use instance method groups or store lambda delegates in subscriber fields.
/// </para>
/// </remarks>
/// <typeparam name="TArgs">The type of argument passed to handlers.</typeparam>
public class AsyncActionEvent<TArgs>
{
	private readonly Lock _lock = new();
	private readonly List<WeakReference<Func<TArgs, ValueTask>>> _storage = new();

	/// <summary>
	/// Gets the number of registered handlers (may include expired weak references).
	/// </summary>
	public int Count
	{
		get
		{
			using (_lock.EnterScope())
				return _storage.Count;
		}
	}

	/// <summary>
	/// Subscribe or unsubscribe handlers. Unsubscription is optional - handlers are automatically
	/// cleaned up when the subscriber is garbage collected.
	/// </summary>
	public event Func<TArgs, ValueTask> Handler
	{
		add
		{
			value._NotNull();
			using (_lock.EnterScope())
			{
				_storage.Add(new WeakReference<Func<TArgs, ValueTask>>(value));
			}
		}
		remove
		{
			using (_lock.EnterScope())
			{
				_storage._RemoveLast(x => x.TryGetTarget(out var target) && target == value);
			}
		}
	}

	/// <summary>
	/// Sequentially awaits all live handlers with backpressure control.
	/// Expired handlers are skipped and cleaned up after iteration.
	/// </summary>
	/// <param name="args">The argument to pass to each handler.</param>
	/// <param name="ct">Optional cancellation token checked between handler invocations.</param>
	/// <returns>A task that completes when all handlers have completed.</returns>
	/// <exception cref="OperationCanceledException">Thrown if cancellation is requested between handlers.</exception>
	public async ValueTask RaiseAsync(TArgs args, CancellationToken ct = default)
	{
		if (_storage.Count == 0)
			return;

		// Rent a list to minimize allocations during high-frequency invocations
		using var _ = __.pool.Rent<List<WeakReference<Func<TArgs, ValueTask>>>>(out var snapshot);
		__.GetLogger()._EzError(snapshot.Count == 0, "when recycling to pool, should always clear objects");

		using (_lock.EnterScope())
		{
			snapshot.AddRange(_storage);
		}

		var anyExpired = false;
		try
		{
			foreach (var weakRef in snapshot)
			{
				ct.ThrowIfCancellationRequested();
				if (weakRef.TryGetTarget(out var handler))
				{
					await handler(args);
				}
				else
				{
					anyExpired = true;
				}
			}
		}
		finally
		{
			if (anyExpired)
			{
				_RemoveExpiredSubscriptions();
			}
			snapshot.Clear();
		}
	}

	private void _RemoveExpiredSubscriptions()
	{
		using (_lock.EnterScope())
		{
			_storage.RemoveAll(weakRef => !weakRef.TryGetTarget(out _));
		}
	}

	/// <summary>
	/// Removes all handlers. Use during owner disposal for deterministic cleanup.
	/// </summary>
	public void Clear()
	{
		using (_lock.EnterScope())
		{
			_storage.Clear();
		}
	}
}


/// <summary>
/// Async event with sender parameter, WeakReference storage, and sequential await for backpressure control.
/// Like <see cref="Event{TEventArgs}"/>, handlers can be garbage collected without explicit unsubscription.
/// </summary>
/// <remarks>
/// <para>
/// <b>WeakReference semantics:</b> Handlers are stored as weak references. Once the subscriber holding
/// the delegate is garbage collected, the handler is automatically cleaned up on next invocation.
/// Explicit unsubscription is optional but allows deterministic cleanup.
/// </para>
/// <para>
/// <b>Backpressure:</b> All handlers are awaited sequentially. A slow handler blocks subsequent handlers.
/// </para>
/// <para>
/// <b>Thread safety:</b> Subscribe/unsubscribe are thread-safe. Concurrent InvokeAsync calls are allowed
/// but handlers execute in subscription order within each call.
/// </para>
/// <para>
/// <b>Exception handling:</b> First exception bubbles to caller and stops invocation of remaining handlers.
/// </para>
/// <para>
/// <b>Lambda caution:</b> Anonymous lambdas not stored elsewhere may be collected before invocation.
/// For reliable delivery, use instance method groups or store lambda delegates in subscriber fields.
/// </para>
/// </remarks>
/// <typeparam name="TEventArgs">The type of event arguments, must derive from EventArgs.</typeparam>
public class AsyncEvent<TEventArgs> where TEventArgs : EventArgs
{
	private readonly Lock _lock = new();
	private readonly List<WeakReference<Func<object, TEventArgs, ValueTask>>> _storage = new();

	/// <summary>
	/// Gets the number of registered handlers (may include expired weak references).
	/// </summary>
	public int Count
	{
		get
		{
			using (_lock.EnterScope())
				return _storage.Count;
		}
	}

	/// <summary>
	/// Subscribe or unsubscribe handlers. Unsubscription is optional - handlers are automatically
	/// cleaned up when the subscriber is garbage collected.
	/// </summary>
	public event Func<object, TEventArgs, ValueTask> Handler
	{
		add
		{
			value._NotNull();
			using (_lock.EnterScope())
			{
				_storage.Add(new WeakReference<Func<object, TEventArgs, ValueTask>>(value));
			}
		}
		remove
		{
			using (_lock.EnterScope())
			{
				_storage._RemoveLast(x => x.TryGetTarget(out var target) && target == value);
			}
		}
	}

	/// <summary>
	/// Sequentially awaits all live handlers with backpressure control.
	/// Expired handlers are skipped and cleaned up after iteration.
	/// </summary>
	/// <param name="sender">The event sender.</param>
	/// <param name="args">The event arguments to pass to each handler.</param>
	/// <param name="ct">Optional cancellation token checked between handler invocations.</param>
	/// <returns>A task that completes when all handlers have completed.</returns>
	/// <exception cref="OperationCanceledException">Thrown if cancellation is requested between handlers.</exception>
	public async ValueTask InvokeAsync(object sender, TEventArgs args, CancellationToken ct = default)
	{
		if (_storage.Count == 0)
			return;

		// Rent a list to minimize allocations during high-frequency invocations
		using var _ = __.pool.Rent<List<WeakReference<Func<object, TEventArgs, ValueTask>>>>(out var snapshot);
		__.GetLogger()._EzError(snapshot.Count == 0, "when recycling to pool, should always clear objects");

		using (_lock.EnterScope())
		{
			snapshot.AddRange(_storage);
		}

		var anyExpired = false;
		try
		{
			foreach (var weakRef in snapshot)
			{
				ct.ThrowIfCancellationRequested();
				if (weakRef.TryGetTarget(out var handler))
				{
					await handler(sender, args);
				}
				else
				{
					anyExpired = true;
				}
			}
		}
		finally
		{
			if (anyExpired)
			{
				_RemoveExpiredSubscriptions();
			}
			snapshot.Clear();
		}
	}

	private void _RemoveExpiredSubscriptions()
	{
		using (_lock.EnterScope())
		{
			_storage.RemoveAll(weakRef => !weakRef.TryGetTarget(out _));
		}
	}

	/// <summary>
	/// Removes all handlers. Use during owner disposal for deterministic cleanup.
	/// </summary>
	public void Clear()
	{
		using (_lock.EnterScope())
		{
			_storage.Clear();
		}
	}
}