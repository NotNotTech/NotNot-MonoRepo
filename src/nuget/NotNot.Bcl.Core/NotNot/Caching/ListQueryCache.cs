namespace NotNot.Caching;

/// <summary>
/// Generic cached list with sequence-numbered refresh to prevent stale overwrites.
/// Thread-safe via SemaphoreSlim. Sequence guard ensures that if a newer refresh
/// starts before an older one completes, the older result is discarded.
/// </summary>
/// <typeparam name="TItem">The item type stored in the cache.</typeparam>
/// <typeparam name="TKey">The key type for individual item lookup.</typeparam>
public class ListQueryCache<TItem, TKey> where TKey : notnull
{
	private readonly Func<CancellationToken, Task<List<TItem>>> _fetchAll;
	private readonly Func<TKey, CancellationToken, Task<TItem?>> _fetchOne;
	private readonly Func<TItem, TKey> _keySelector;
	private readonly SemaphoreSlim _lock = new(1, 1);

	private List<TItem> _items = [];

	/// <summary>
	/// Key→item index over <see cref="_items"/>. Rebuilt wholesale on full refresh and
	/// copy-on-write updated on single-item refresh, then reference-swapped — readers
	/// (<see cref="GetById"/>/<see cref="TryGetById"/>) never observe a partially-built
	/// dictionary. Gives consumers O(1) keyed lookup instead of O(N) Items scans.
	/// </summary>
	private Dictionary<TKey, TItem> _index = new();
	private long _refreshSequence;

	/// <summary>
	/// Creates a new cache with fetch delegates and a key selector.
	/// </summary>
	/// <param name="fetchAll">Delegate to fetch all items from the data source.</param>
	/// <param name="fetchOne">Delegate to fetch a single item by key.</param>
	/// <param name="keySelector">Extracts the key from an item.</param>
	public ListQueryCache(
		Func<CancellationToken, Task<List<TItem>>> fetchAll,
		Func<TKey, CancellationToken, Task<TItem?>> fetchOne,
		Func<TItem, TKey> keySelector)
	{
		_fetchAll = fetchAll;
		_fetchOne = fetchOne;
		_keySelector = keySelector;
	}

	/// <summary>
	/// Current cached items. Empty until first <see cref="RefreshAllAsync"/> completes.
	/// </summary>
	public IReadOnlyList<TItem> Items => _items;

	/// <summary>
	/// Whether a refresh operation is currently in progress.
	/// </summary>
	public bool IsLoading { get; private set; }

	/// <summary>
	/// Fired after the cache is updated (full refresh or single-item refresh).
	/// </summary>
	public event Action? OnChanged;

	/// <summary>
	/// Fetches all items from the data source and replaces the cache.
	/// Sequence-guarded: if a newer refresh starts before this one completes,
	/// this result is discarded to prevent stale overwrites.
	/// </summary>
	public async Task RefreshAllAsync(CancellationToken ct = default)
	{
		var seq = Interlocked.Increment(ref _refreshSequence);
		IsLoading = true;
		try
		{
			var fresh = await _fetchAll(ct);
			await _lock.WaitAsync(ct);
			try
			{
				if (seq < Interlocked.Read(ref _refreshSequence)) return; // stale
				_items = fresh;
				_index = BuildIndex(fresh);
			}
			finally { _lock.Release(); }
			OnChanged?.Invoke();
		}
		finally
		{
			IsLoading = false;
		}
	}

	/// <summary>
	/// Fetches a single item and updates or adds it in the cache.
	/// </summary>
	public async Task RefreshOneAsync(TKey key, CancellationToken ct = default)
	{
		var item = await _fetchOne(key, ct);
		if (item is null) return;

		await _lock.WaitAsync(ct);
		try
		{
			var idx = _items.FindIndex(i => EqualityComparer<TKey>.Default.Equals(_keySelector(i), key));
			if (idx >= 0)
				_items[idx] = item;
			else
				_items.Add(item);
			// Copy-on-write index update — lock-free readers see old-or-new, never torn.
			_index = new Dictionary<TKey, TItem>(_index) { [key] = item };
		}
		finally { _lock.Release(); }
		OnChanged?.Invoke();
	}

	/// <summary>
	/// O(1) keyed lookup into the cached list. Returns the item with the given key, or
	/// <c>default</c> when absent. Lock-free read against the reference-swapped index —
	/// same consistency semantics as reading <see cref="Items"/>.
	/// </summary>
	public TItem? GetById(TKey key)
		=> _index.TryGetValue(key, out var item) ? item : default;

	/// <summary>
	/// O(1) keyed lookup into the cached list. See <see cref="GetById"/>.
	/// </summary>
	public bool TryGetById(TKey key, out TItem? item)
	{
		if (_index.TryGetValue(key, out var found))
		{
			item = found;
			return true;
		}
		item = default;
		return false;
	}

	private Dictionary<TKey, TItem> BuildIndex(List<TItem> items)
	{
		var index = new Dictionary<TKey, TItem>(items.Count);
		foreach (var item in items)
			index[_keySelector(item)] = item; // last-wins on duplicate keys — same item the keyed fetchOne path would have written last
		return index;
	}

	/// <summary>
	/// Triggers a refresh. If <paramref name="key"/> is provided, refreshes only that item.
	/// Otherwise refreshes all items.
	/// </summary>
	public void Invalidate(TKey? key = default)
	{
		if (key is not null && !EqualityComparer<TKey>.Default.Equals(key, default!))
			_ = RefreshOneAsync(key);
		else
			_ = RefreshAllAsync();
	}
}
