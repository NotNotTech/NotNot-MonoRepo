
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;


namespace NotNot.Collections;

public class AsyncDequeueDictionary<TKey, TValue> // : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable
where TKey : notnull
{
	/// <summary>
	/// store of truth
	/// </summary>
	private ConcurrentDictionary<TKey, TValue> dictionary = new();
	/// <summary>
	/// used for dequeue enumeration, not "store of truth", as pairs may be removed from dictionary before dequeued
	/// </summary>
	private ConcurrentQueue<TKey> keyQueue = new();
	private AsyncAutoResetEvent autoResetEvent = new(false); // Initially non-signaled
	private CancellationTokenSource cts = new();
	private bool isDisposed = false;

	public bool TryAdd(TKey key, TValue value)
	{
		_AssertNotDisposed();


		if (dictionary.TryAdd(key, value))
		{
			keyQueue.Enqueue(key);
			autoResetEvent.Set(); // Signal that an item is available
			return true;
		}
		return false;
	}

	private void _AssertNotDisposed()
	{
		if (isDisposed) throw new ObjectDisposedException(nameof(AsyncDequeueDictionary<TKey, TValue>));
	}

	public async Task<KeyValuePair<TKey, TValue>> DequeueAsync(CancellationToken ct)
	{
		_AssertNotDisposed();

		CancellationToken linkedToken = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token).Token;

		while (true)
		{
			await autoResetEvent.WaitAsync(linkedToken).ConfigureAwait(false);
			if (TryDequeue(out var pair))
			{
				return pair;
			}
		}
	}
	public bool TryDequeue(TKey key, [NotNullWhen(true)] out TValue? value)
	{
		_AssertNotDisposed();
		if (dictionary.TryRemove(key, out value))
		{
			return true;
		}
		return false;
	}
	public bool TryDequeue([NotNullWhen(true)] out KeyValuePair<TKey, TValue> pair)
	{
		_AssertNotDisposed();

		while (keyQueue.TryDequeue(out TKey key))
		{
			if (dictionary.TryRemove(key, out TValue value))
			{
				pair = new KeyValuePair<TKey, TValue>(key, value);
#if DEBUG
				if (dictionary.Count > 0)
				{
					__.GetLogger()._EzError(keyQueue.Count > 0 && autoResetEvent.IsSet, "if something in dictionary, should be able to dequeue");
				}
#endif

				return true;
			}
		}

#if DEBUG
		//debug logic, if we can't dequeue, then should not be anything in dictionary
		var dictCount = dictionary.Count;
		if (dictCount > 0 && (keyQueue.Count == 0 || !autoResetEvent.IsSet)) //double-check keyQueue in case race condition
		{
			var newDictCount = dictionary.Count;
			__.GetLogger()._EzError(false, "if we can't dequeue, then should not be anything in dictionary");
		}

#endif

		pair = default;
		return false;
	}


	public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
	{
		_AssertNotDisposed();

		var isAdded = false;
		var result = dictionary.GetOrAdd(key, (key) =>
		{
			isAdded = true;
			return valueFactory(key);
		});
		if (isAdded)
		{
			keyQueue.Enqueue(key);
			autoResetEvent.Set(); // Signal that an item is available
		}
		return result;

	}

	public bool TryGet(TKey key, out TValue value)
	{
		_AssertNotDisposed();

		return dictionary.TryGetValue(key!, out value);
	}
	public void AddOrUpdate(TKey key, Func<TValue> addValue, Func<TKey, TValue, TValue> updateValueFactory)
	{
		_AssertNotDisposed();

		var isAdded = false;
		var result = dictionary.AddOrUpdate(key, (key) =>
		{
			isAdded = true;
			return addValue();
		}, updateValueFactory);
		if (isAdded)
		{
			keyQueue.Enqueue(key);
			autoResetEvent.Set(); // Signal that an item is available
		}
	}
	public void AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
	{
		AddOrUpdate(key, () => addValue, updateValueFactory);
	}


	public async ValueTask DisposeAsync()
	{
		Dispose(disposing: true);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!isDisposed)
		{
			isDisposed = true;


			if (disposing)
			{
				cts.Cancel();
				autoResetEvent.Set(); // Release any waiting operations
				cts.Dispose();
				GC.SuppressFinalize(this);
			}

		}
	}

	~AsyncDequeueDictionary()
	{
		Dispose(disposing: false);
	}
	// IEnumerable implementation...
}
