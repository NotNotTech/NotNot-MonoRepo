using NotNot.Advanced;
using System.Collections.Concurrent;

namespace NotNot;

/// <summary>
///    thread safe pubsub
/// </summary>
[ThreadSafe]
public class PubSub<TItem>
{
	private List<WeakReference<IProducerConsumerCollection<TItem>>> _storage = new();

	/// <summary>
	///    subscribe to events
	/// </summary>
	/// <param name="target">for example, a ConcurrentQueue or AsyncQueue</param>
	public void Subscribe(IProducerConsumerCollection<TItem> target)
	{
		lock (_storage)
		{
			_storage.Add(new WeakReference<IProducerConsumerCollection<TItem>>(target));
			__.GetLogger()._EzError(_storage.Find(x => x.TryGetTarget(out var item) && item == target) != null,
				"subscribe already exists.  did you subscribe twice?");
		}
	}

	public void Unsubscribe(IProducerConsumerCollection<TItem> target)
	{
		lock (_storage)
		{
			var result = _storage._RemoveLast(x => x.TryGetTarget(out var item) && item == target);
			__.GetLogger()._EzError(result, "unsubscribe failed.  did you subscribe?");
		}
	}


	/// <summary>
	///    submit an item to all subscribers
	/// </summary>
	public void Publish(TItem item)
	{
		lock (_storage)
		{
			var anyExpired = false;
			foreach (var weakRef in _storage)
			{
				if (weakRef.TryGetTarget(out var target))
				{
					var result = target.TryAdd(item);
					__.GetLogger()._EzError(result, "the collection is not allowing Adding");
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
		}
	}

	public void Publish(IEnumerable<TItem> items)
	{
		lock (_storage)
		{
			if (_storage.Count == 0)
			{
				return;
			}

			var anyExpired = false;


			foreach (var item in items)
			{
				foreach (var weakRef in _storage)
				{
					if (weakRef.TryGetTarget(out var target))
					{
						var result = target.TryAdd(item);
						__.GetLogger()._EzError(result, "the collection is not allowing Adding");

					}
					else
					{
						anyExpired = true;
					}
				}
			}

			if (anyExpired)
			{
				_RemoveExpiredSubscriptions();
			}
		}
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
