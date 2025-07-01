namespace NotNot.Collections.Specialized;

/// <summary>
///    wraps an AsyncQueue with a single thread in charge of dequeuing when there are items available.
/// </summary>
/// <typeparam name="T"></typeparam>
public class AsyncQueueWorker<T>
{
	private Func<T, CancellationToken, Task> _dequeueWorker;
	private AsyncQueue<T> _storage = new();
	private Task _workerThread;

	public AsyncQueueWorker(Func<T, CancellationToken, Task> dequeueWorker)
	{
		_dequeueWorker = dequeueWorker;

		_workerThread = __.Async.LongRun(_processQueueWorker);
	}

	public CancellationToken Ct { get; init; } = CancellationToken.None;

	public void Enqueue(T item)
	{
		_storage.Enqueue(item);
	}

	public void Enqueue(IEnumerable<T> items)
	{
		_storage.Enqueue(items);
	}


	private async Task _processQueueWorker()
	{
		while (true)
		{
			if (Ct.IsCancellationRequested)
			{
				_dequeueWorker = null;
				Ct.ThrowIfCancellationRequested();
				return;
			}

			var item = await _storage.DequeueAsync(Ct);
			await _dequeueWorker(item, Ct);
		}
	}
}
