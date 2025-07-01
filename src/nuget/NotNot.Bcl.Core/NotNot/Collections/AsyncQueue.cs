using NotNot.Advanced;
using System.Collections;
using System.Collections.Concurrent;

namespace NotNot.Collections;

/// <summary>
///    thread safe queue that allows `await` of dequeue call
/// </summary>
/// <typeparam name="T"></typeparam>
[ThreadSafe]
public class AsyncQueue<T> : IProducerConsumerCollection<T>, IReadOnlyCollection<T>, IDisposable
{
	private static int _increment;


#pragma warning disable CA2213 // Disposable fields should be disposed
	private SemaphoreSlim _slim = new(0);
#pragma warning restore CA2213 // Disposable fields should be disposed

	private ConcurrentQueue<T> _storage;
	public int Id = Interlocked.Increment(ref _increment);

	public AsyncQueue(ConcurrentQueue<T>? backingStorage = null)
	{
		backingStorage ??= new ConcurrentQueue<T>();
		_storage = backingStorage;
	}

	public void Dispose()
	{
		_storage.Clear();
		//_slim.Dispose(); DotNet bug: Dispose() is broken. https://github.com/dotnet/runtime/issues/59639
		_storage = null!;
		_slim = null!;
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _storage.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	void ICollection.CopyTo(Array array, int index)
	{
		(_storage as IProducerConsumerCollection<T>).CopyTo(array, index);
	}

	public int Count => _storage.Count;

	bool ICollection.IsSynchronized => true;
	object ICollection.SyncRoot { get; } = new();

	public void CopyTo(T[] array, int index)
	{
		_storage.CopyTo(array, index);
	}

	public T[] ToArray()
	{
		return _storage.ToArray();
	}

	bool IProducerConsumerCollection<T>.TryAdd(T item)
	{
		Enqueue(item);
		return true;
	}

	bool IProducerConsumerCollection<T>.TryTake(out T item)
	{
#pragma warning disable CS8601 // Possible null reference assignment.
		return TryDequeue(out item);
#pragma warning restore CS8601 // Possible null reference assignment.
	}


	public void Enqueue(T item)
	{
		_storage.Enqueue(item);
		_slim.Release();
	}

	public void Enqueue(IEnumerable<T> items)
	{
		var count = 0;
		foreach (var item in items)
		{
			_storage.Enqueue(item);
			count++;
		}

		if (count > 0)
		{
			_slim.Release(count);
		}
	}


	private T _DoUnlockedDequeue()
	{
		var result = _storage.TryDequeue(out var item);
		__.GetLogger()._EzError(result, "race condition?");
		return item;
	}

	public bool TryDequeue(out T? item)
	{
		if (_slim.Wait(0))
		{
			item = _DoUnlockedDequeue();
			return true;
		}

		item = default;
		return false;
	}

	public bool TryDequeue(out Mem<T> items)
	{
		var count = 0;
		while (_slim.Wait(0))
		{
			count++;
		}

		if (count == 0)
		{
			items = default;
			return false;
		}

		var mem = Mem<T>.Allocate(count);
		for (var i = 0; i < count; i++)
		{
			var result = _storage.TryDequeue(out var item);
			__.GetLogger()._EzError(result, "race condition?");
			mem[i] = item;
		}

		items = mem;
		return true;
	}

	public async Task<T> DequeueAsync()
	{
		await _slim.WaitAsync();
		return _DoUnlockedDequeue();
	}

	public async Task<T> DequeueAsync(CancellationToken ct)
	{
		await _slim.WaitAsync(ct);
		return _DoUnlockedDequeue();
	}

	public async Task<T> DequeueAsync(TimeSpan timeSpan)
	{
		await _slim.WaitAsync(timeSpan);
		return _DoUnlockedDequeue();
	}

	public async Task<T> DequeueAsync(int msTimeout)
	{
		await _slim.WaitAsync(msTimeout);
		return _DoUnlockedDequeue();
	}
}
