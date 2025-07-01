using NotNot;

namespace NotNot.Collections;

/// <summary>
///    allows tracking if an object has been seen.
///    does not stop garbage collection of the object.
/// </summary>
[ThreadSafety(ThreadSituation.Always)]
public class ObjectTrackingCollection<T> where T : class
{
	private List<WeakReference<T>> _storage = new();

	private object _lock = new();

	public bool TryAdd(T item)
	{
		lock (_lock)
		{
			for (var i = _storage.Count - 1; i >= 0; i--)
			{
				var wr = _storage[i];


				if (wr.TryGetTarget(out var t))
				{
					if (t.Equals(item))
					{
						return false;
					}

					continue;
				}

				//not alive
				_storage.RemoveAt(i);
			}

			_storage.Add(new WeakReference<T>(item));
			return true;
		}
	}

	public bool Contains(T item)
	{
		lock (_lock)
		{
			for (var i = _storage.Count - 1; i >= 0; i--)
			{
				var wr = _storage[i];


				if (wr.TryGetTarget(out var t))
				{
					if (t.Equals(item))
					{
						return true;
					}

					continue;
				}

				//not alive
				_storage.RemoveAt(i);
			}

			return false;
		}
	}

	public bool TryRemove(T item)
	{
		lock (_lock)
		{
			for (var i = _storage.Count - 1; i >= 0; i--)
			{
				var wr = _storage[i];

				if (wr.TryGetTarget(out var t))
				{
					if (t.Equals(item))
					{
						_storage.RemoveAt(i);
						return true;
					}

					continue;
				}

				//not alive
				_storage.RemoveAt(i);
			}

			return false;
		}
	}
}
