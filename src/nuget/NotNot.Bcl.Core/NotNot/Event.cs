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