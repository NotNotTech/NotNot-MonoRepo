namespace NotNot.Caching;

/// <summary>
/// Windowed paginated cache with auto-purge for bounded memory usage.
/// Designed for 100K+ item collections with lazy-loading and client-side eviction.
/// </summary>
/// <typeparam name="TItem">The item type stored in the cache.</typeparam>
public class PagedQueryCache<TItem>
{
	private readonly Func<int?, int, CancellationToken, Task<PageResult<TItem>>> _fetchPage;
	private readonly int _windowSize;
	private readonly SortedDictionary<int, TItem> _cache = new();

	/// <summary>
	/// Creates a new paged cache.
	/// </summary>
	/// <param name="fetchPage">Fetches a page: (afterLine, count, ct) => PageResult.</param>
	/// <param name="windowSize">Maximum number of items to keep in memory. Auto-purge removes items outside the current window.</param>
	public PagedQueryCache(
		Func<int?, int, CancellationToken, Task<PageResult<TItem>>> fetchPage,
		int windowSize = 500)
	{
		_fetchPage = fetchPage;
		_windowSize = windowSize;
	}

	/// <summary>
	/// Total item count as reported by the last fetch. -1 if unknown.
	/// </summary>
	public int TotalCount { get; private set; } = -1;

	/// <summary>
	/// Whether a fetch operation is currently in progress.
	/// </summary>
	public bool IsLoading { get; private set; }

	/// <summary>
	/// Fired after the cache is updated.
	/// </summary>
	public event Action? OnChanged;

	/// <summary>
	/// Gets a range of items, fetching from the data source if not cached.
	/// Auto-purges items outside the current window after loading.
	/// </summary>
	/// <param name="startLine">0-based start line number.</param>
	/// <param name="count">Number of items to retrieve.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Items in the requested range that are available.</returns>
	public async Task<List<TItem>> GetRangeAsync(int startLine, int count, CancellationToken ct = default)
	{
		// Check which items we already have
		var missing = new List<int>();
		for (var i = startLine; i < startLine + count; i++)
		{
			if (!_cache.ContainsKey(i))
				missing.Add(i);
		}

		if (missing.Count > 0)
		{
			IsLoading = true;
			try
			{
				// Fetch from the earliest missing item
				int? afterLine = missing[0] > 0 ? missing[0] - 1 : null;
				var result = await _fetchPage(afterLine, count, ct);
				TotalCount = result.TotalCount;

				// Store fetched items by their index
				for (var i = 0; i < result.Items.Count; i++)
				{
					var lineIndex = (afterLine.HasValue ? afterLine.Value + 1 : 0) + i;
					_cache[lineIndex] = result.Items[i];
				}
			}
			finally
			{
				IsLoading = false;
			}

			// Auto-purge: keep only current window + 1 buffer page on each side
			var centerLine = startLine + count / 2;
			PurgeOutsideWindow(centerLine);

			OnChanged?.Invoke();
		}

		// Return available items in range
		var result2 = new List<TItem>();
		for (var i = startLine; i < startLine + count; i++)
		{
			if (_cache.TryGetValue(i, out var item))
				result2.Add(item);
		}
		return result2;
	}

	/// <summary>
	/// Removes all cached items outside a window centered on <paramref name="centerLine"/>.
	/// Window size is determined by the <c>windowSize</c> constructor parameter.
	/// </summary>
	public void PurgeOutsideWindow(int centerLine)
	{
		var halfWindow = _windowSize / 2;
		var minKeep = Math.Max(0, centerLine - halfWindow);
		var maxKeep = centerLine + halfWindow;

		var toRemove = new List<int>();
		foreach (var key in _cache.Keys)
		{
			if (key < minKeep || key > maxKeep)
				toRemove.Add(key);
		}
		foreach (var key in toRemove)
			_cache.Remove(key);
	}

	/// <summary>
	/// Clears all cached data and resets TotalCount.
	/// </summary>
	public void Clear()
	{
		_cache.Clear();
		TotalCount = -1;
		OnChanged?.Invoke();
	}
}
