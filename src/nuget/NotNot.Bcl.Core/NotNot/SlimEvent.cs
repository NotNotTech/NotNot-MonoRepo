using System.Collections.Generic;

namespace NotNot;

/// <summary>
/// Strong-reference event handler for parameterless events.
/// Unlike Event&lt;TEventArgs&gt; which uses WeakReferences, SlimEvent uses strong references for better performance.
/// TSender must implement IDisposeGuard for disposal validation in CHECKED builds.
/// </summary>
/// <typeparam name="TSender">The sender type, must implement IDisposeGuard for disposal checking</typeparam>
/// <remarks>
/// Thread-safe for concurrent subscription, unsubscription, and invocation.
/// In CHECKED builds, detects disposed recipients still subscribed (memory leak detection).
/// Exceptions thrown by handlers bubble to caller and stop invocation.
/// Use Clear() for bulk unsubscribe.
/// </remarks>
public class SlimEvent<TSender> where TSender : IDisposeGuard
{
	private List<Action<TSender>> _storage = new();

	/// <summary>
	/// Subscribe/unsubscribe to this event
	/// </summary>
	public event Action<TSender> Handler
	{
		add
		{
			lock (_storage)
			{
				_storage.Add(value);
			}
		}
		remove
		{
			lock (_storage)
			{
				var index = _storage.FindLastIndex(x => x == value);
				if (index >= 0)
				{
					_storage.RemoveAt(index);
				}
			}
		}
	}

	/// <summary>
	/// Invoke all subscribed handlers. Only the owner should call this.
	/// Exceptions thrown by handlers bubble to caller.
	/// In CHECKED builds, detects and removes disposed recipients.
	/// </summary>
	/// <param name="sender">The event sender</param>
	public void Invoke(TSender sender)
	{
		__.AssertIfNot(sender != null, "Sender cannot be null");
		__.AssertIfNot(!sender.IsDisposed, "Sender is disposed");

		if (_storage.Count == 0) return;

		var tempList = __.pool.Get<List<Action<TSender>>>();
		var disposedHandlers = __.pool.Get<List<Action<TSender>>>();

		try
		{
			lock (_storage)
			{
				tempList.AddRange(_storage);
			}

			foreach (var handler in tempList)
			{
				// Check if recipient is disposed
				if (handler.Target is IDisposeGuard guard && guard.IsDisposed)
				{
					disposedHandlers.Add(handler);
					continue;
				}

				// Exception bubbles - stops invocation
				handler(sender);
			}

			// Auto-remove disposed recipients
			if (disposedHandlers.Count > 0)
			{
				lock (_storage)
				{
					foreach (var disposed in disposedHandlers)
					{
						_storage.Remove(disposed);
					}
				}

				// Assert after auto-removal so cleanup happens even in DEBUG builds
				__.AssertIfNot(false, "Disposed recipients still subscribed - memory leak detected",
					"Count", disposedHandlers.Count);
			}
		}
		finally
		{
			tempList.Clear();
			__.pool.Return(tempList);
			disposedHandlers.Clear();
			__.pool.Return(disposedHandlers);
		}
	}

	/// <summary>
	/// Remove all subscribed handlers
	/// </summary>
	public void Clear()
	{
		lock (_storage)
		{
			_storage.Clear();
		}
	}
}

/// <summary>
/// Strong-reference event handler with typed arguments.
/// Unlike Event&lt;TEventArgs&gt; which uses WeakReferences, SlimEvent uses strong references for better performance.
/// TSender must implement IDisposeGuard for disposal validation in CHECKED builds.
/// TArgs does NOT require EventArgs inheritance - can be any type (value types, custom DTOs, etc).
/// </summary>
/// <typeparam name="TSender">The sender type, must implement IDisposeGuard for disposal checking</typeparam>
/// <typeparam name="TArgs">The event arguments type (no constraints)</typeparam>
/// <remarks>
/// Thread-safe for concurrent subscription, unsubscription, and invocation.
/// In CHECKED builds, detects disposed recipients still subscribed (memory leak detection).
/// Exceptions thrown by handlers bubble to caller and stop invocation.
/// Use Clear() for bulk unsubscribe.
/// </remarks>
public class SlimEvent<TSender, TArgs> where TSender : IDisposeGuard
{
	private List<Action<TSender, TArgs>> _storage = new();

	/// <summary>
	/// Subscribe/unsubscribe to this event
	/// </summary>
	public event Action<TSender, TArgs> Handler
	{
		add
		{
			lock (_storage)
			{
				_storage.Add(value);
			}
		}
		remove
		{
			lock (_storage)
			{
				var index = _storage.FindLastIndex(x => x == value);
				if (index >= 0)
				{
					_storage.RemoveAt(index);
				}
			}
		}
	}

	/// <summary>
	/// Invoke all subscribed handlers. Only the owner should call this.
	/// Exceptions thrown by handlers bubble to caller.
	/// In CHECKED builds, detects and removes disposed recipients.
	/// </summary>
	/// <param name="sender">The event sender</param>
	/// <param name="args">The event arguments</param>
	public void Invoke(TSender sender, TArgs args)
	{
		__.AssertIfNot(sender != null, "Sender cannot be null");
		__.AssertIfNot(!sender.IsDisposed, "Sender is disposed");

		if (_storage.Count == 0) return;

		var tempList = __.pool.Get<List<Action<TSender, TArgs>>>();
		var disposedHandlers = __.pool.Get<List<Action<TSender, TArgs>>>();

		try
		{
			lock (_storage)
			{
				tempList.AddRange(_storage);
			}

			foreach (var handler in tempList)
			{
				// Check if recipient is disposed
				if (handler.Target is IDisposeGuard guard && guard.IsDisposed)
				{
					disposedHandlers.Add(handler);
					continue;
				}

				// Exception bubbles - stops invocation
				handler(sender, args);
			}

			// Auto-remove disposed recipients
			if (disposedHandlers.Count > 0)
			{
				lock (_storage)
				{
					foreach (var disposed in disposedHandlers)
					{
						_storage.Remove(disposed);
					}
				}

				// Assert after auto-removal so cleanup happens even in DEBUG builds
				__.AssertIfNot(false, "Disposed recipients still subscribed - memory leak detected",
					"Count", disposedHandlers.Count);
			}
		}
		finally
		{
			tempList.Clear();
			__.pool.Return(tempList);
			disposedHandlers.Clear();
			__.pool.Return(disposedHandlers);
		}
	}

	/// <summary>
	/// Remove all subscribed handlers
	/// </summary>
	public void Clear()
	{
		lock (_storage)
		{
			_storage.Clear();
		}
	}
}
