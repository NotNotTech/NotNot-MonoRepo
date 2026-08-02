using System.Collections.Concurrent;
using NotNot.Concurrency;

namespace NotNot.Caching;

/// <summary>
/// Generic cached list with sequence-numbered refresh to prevent stale overwrites.
/// Thread-safe via SemaphoreSlim. Sequence guard ensures that if a newer full refresh
/// starts before an older one completes, the older result is discarded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Change channels (contract)</b>:
/// <list type="bullet">
///   <item><see cref="OnChanged"/> ⇔ list-SHAPE change. Raised after every
///   <see cref="RefreshAllAsync"/> commit, and after a <see cref="RefreshOneAsync"/> commit
///   that ADDED a new item. An update-in-place keyed refresh raises NO <see cref="OnChanged"/>.</item>
///   <item><see cref="OnItemChanged"/> ⇔ item commit. Raised with the key after EVERY
///   <see cref="RefreshOneAsync"/> commit (update-in-place or add).</item>
/// </list>
/// Whole-list views subscribe to BOTH channels (item edge = row data change; list edge =
/// shape change). Id-scoped views subscribe to <see cref="OnItemChanged"/> with a key filter.
/// </para>
/// <para>
/// <b>Invalidation debounce (opt-in)</b>: when constructed with <c>invalidateDebounceMs &gt; 0</c>,
/// <see cref="Invalidate"/> routes through a per-key trailing-edge <see cref="Debouncer"/>
/// (full-list invalidations get one dedicated debouncer). Trailing-edge = the final
/// invalidation in a burst is never dropped; bursts within the window coalesce into one fetch.
/// At the default <c>0</c>, <see cref="Invalidate"/> keeps the eager fire-and-forget fetch
/// behavior. Debouncer callbacks run on the ThreadPool — UI subscribers must marshal
/// (e.g. Blazor <c>InvokeAsync</c>). Dispose the cache to stop pending debounce timers.
/// </para>
/// <para>
/// <b>Reader guarantee (snapshot semantics)</b>: <see cref="Items"/> and the keyed index are
/// reference-swapped under lock — wholesale replace on full refresh, copy-on-write on keyed
/// commits. A lock-free reader (enumerating <see cref="Items"/> or calling
/// <see cref="GetById"/>) therefore observes a stable old-or-new snapshot, never a torn or
/// mid-mutation collection: a held <see cref="Items"/> reference is NEVER mutated in place, so
/// concurrent enumeration cannot throw <see cref="InvalidOperationException"/> during a keyed
/// add/update. Pinned by the <c>ListQueryCacheTests</c> copy-on-write snapshot test.
/// </para>
/// <para>
/// <b>Known race (last-write-wins, by design)</b>: <see cref="RefreshOneAsync"/> does not
/// participate in the full-refresh sequence guard. A <see cref="RefreshAllAsync"/> whose fetch
/// snapshot predates a concurrent keyed commit will overwrite that newer item value when it
/// commits. State is never torn (commits are lock-serialized); the stale value self-heals on
/// the next keyed refresh. Pinned by <c>ListQueryCacheTests</c> race-pin test.
/// </para>
/// </remarks>
/// <typeparam name="TItem">The item type stored in the cache.</typeparam>
/// <typeparam name="TKey">The key type for individual item lookup.</typeparam>
public class ListQueryCache<TItem, TKey> : IDisposable where TKey : notnull
{
	private readonly Func<CancellationToken, Task<List<TItem>>> _fetchAll;
	private readonly Func<TKey, CancellationToken, Task<TItem?>> _fetchOne;
	private readonly Func<TItem, TKey> _keySelector;
	private readonly SemaphoreSlim _lock = new(1, 1);

	private readonly int _invalidateDebounceMs;
	private readonly ConcurrentDictionary<TKey, Debouncer>? _keyDebouncers;
	private readonly Debouncer? _fullListDebouncer;
	private volatile bool _disposed;

	private List<TItem> _items = [];

	/// <summary>
	/// Key→item index over <see cref="_items"/>. Rebuilt wholesale on full refresh and
	/// copy-on-write updated on single-item refresh, then reference-swapped — readers
	/// (<see cref="GetById"/>/<see cref="TryGetById"/>) never observe a partially-built
	/// dictionary. Gives consumers O(1) keyed lookup instead of O(N) Items scans.
	/// </summary>
	private Dictionary<TKey, TItem> _index = new();
	private long _refreshSequence;
	private long _refreshAllCount;
	private long _refreshOneCount;

	/// <summary>
	/// Creates a new cache with fetch delegates and a key selector.
	/// </summary>
	/// <param name="fetchAll">Delegate to fetch all items from the data source.</param>
	/// <param name="fetchOne">Delegate to fetch a single item by key.</param>
	/// <param name="keySelector">Extracts the key from an item.</param>
	/// <param name="invalidateDebounceMs">
	/// Opt-in trailing-edge debounce window for <see cref="Invalidate"/>, in milliseconds.
	/// <c>0</c> (default) = eager: every invalidation starts a fetch immediately.
	/// <c>&gt; 0</c> = per-key coalescing: invalidation bursts within the window collapse
	/// into a single trailing fetch per key (one dedicated debouncer for full-list invalidations).
	/// </param>
	public ListQueryCache(
		Func<CancellationToken, Task<List<TItem>>> fetchAll,
		Func<TKey, CancellationToken, Task<TItem?>> fetchOne,
		Func<TItem, TKey> keySelector,
		int invalidateDebounceMs = 0)
	{
		_fetchAll = fetchAll;
		_fetchOne = fetchOne;
		_keySelector = keySelector;
		_invalidateDebounceMs = invalidateDebounceMs;
		if (invalidateDebounceMs > 0)
		{
			_keyDebouncers = new ConcurrentDictionary<TKey, Debouncer>();
			_fullListDebouncer = new Debouncer(
				() => _disposed ? Task.CompletedTask : RefreshAllAsync(),
				invalidateDebounceMs);
		}
	}

	/// <summary>
	/// Current cached items. Empty until first <see cref="RefreshAllAsync"/> completes.
	/// Snapshot semantics: the backing list is reference-swapped on every commit (never mutated
	/// in place), so a held reference is enumeration-stable — see class remarks (reader guarantee).
	/// </summary>
	public IReadOnlyList<TItem> Items => _items;

	/// <summary>
	/// Whether a refresh operation is currently in progress.
	/// </summary>
	public bool IsLoading { get; private set; }

	/// <summary>
	/// Fired after a list-SHAPE change commit: every full refresh, plus a single-item
	/// refresh that ADDED a new item. Update-in-place keyed refreshes do NOT raise this —
	/// subscribe to <see cref="OnItemChanged"/> for per-item data changes.
	/// </summary>
	public event Action? OnChanged;

	/// <summary>
	/// Fired with the item key after EVERY <see cref="RefreshOneAsync"/> commit
	/// (update-in-place or add). Post-commit edge: <see cref="GetById"/> for the key
	/// returns the fresh item by the time handlers run.
	/// </summary>
	public event Action<TKey>? OnItemChanged;

	/// <summary>Total <see cref="RefreshAllAsync"/> executions (diagnostics; fetch-rate metering).</summary>
	public long RefreshAllCount => Interlocked.Read(ref _refreshAllCount);

	/// <summary>Total <see cref="RefreshOneAsync"/> executions (diagnostics; fetch-rate metering).</summary>
	public long RefreshOneCount => Interlocked.Read(ref _refreshOneCount);

	/// <summary>Current <see cref="OnChanged"/> subscriber count (diagnostics; fan-out metering).</summary>
	public int OnChangedSubscriberCount => OnChanged?.GetInvocationList().Length ?? 0;

	/// <summary>Current <see cref="OnItemChanged"/> subscriber count (diagnostics; fan-out metering).</summary>
	public int OnItemChangedSubscriberCount => OnItemChanged?.GetInvocationList().Length ?? 0;

	/// <summary>
	/// Fetches all items from the data source and replaces the cache.
	/// Sequence-guarded: if a newer refresh starts before this one completes,
	/// this result is discarded to prevent stale overwrites.
	/// Raises <see cref="OnChanged"/> after commit.
	/// </summary>
	public async Task RefreshAllAsync(CancellationToken ct = default)
	{
		Interlocked.Increment(ref _refreshAllCount);
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
	/// Raises <see cref="OnItemChanged"/> after commit; additionally raises
	/// <see cref="OnChanged"/> IFF the item was ADDED (list-shape change).
	/// NOT sequence-guarded against concurrent full refreshes — see class remarks
	/// for the documented last-write-wins semantics.
	/// </summary>
	public async Task RefreshOneAsync(TKey key, CancellationToken ct = default)
	{
		Interlocked.Increment(ref _refreshOneCount);
		var item = await _fetchOne(key, ct);
		if (item is null) return;

		bool added;
		await _lock.WaitAsync(ct);
		try
		{
			// Copy-on-write list + index — both reference-swapped under lock so lock-free
			// readers (Items enumerators / GetById) see old-or-new snapshots, never a
			// mid-mutation collection (an in-place List.Add would throw
			// InvalidOperationException under a concurrent enumeration on server circuits).
			var newItems = new List<TItem>(_items);
			var idx = newItems.FindIndex(i => EqualityComparer<TKey>.Default.Equals(_keySelector(i), key));
			if (idx >= 0)
			{
				newItems[idx] = item;
				added = false;
			}
			else
			{
				newItems.Add(item);
				added = true;
			}
			_items = newItems;
			_index = new Dictionary<TKey, TItem>(_index) { [key] = item };
		}
		finally { _lock.Release(); }
		OnItemChanged?.Invoke(key);
		if (added)
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
	/// With <c>invalidateDebounceMs &gt; 0</c>, the refresh is trailing-edge debounced per key
	/// (bursts coalesce; the final invalidation is never dropped). Otherwise the refresh
	/// starts eagerly, fire-and-forget. No-op after <see cref="Dispose"/>.
	/// </summary>
	public void Invalidate(TKey? key = default)
	{
		if (_disposed) return;

		var hasKey = key is not null && !EqualityComparer<TKey>.Default.Equals(key, default!);

		if (_invalidateDebounceMs <= 0)
		{
			if (hasKey)
				_ = RefreshOneAsync(key!);
			else
				_ = RefreshAllAsync();
			return;
		}

		if (hasKey)
		{
			var k = key!;
			if (!_keyDebouncers!.TryGetValue(k, out var debouncer))
			{
				// Create-then-GetOrAdd so a losing racer's Timer is disposed, never leaked.
				var created = new Debouncer(
					() => _disposed ? Task.CompletedTask : RefreshOneAsync(k),
					_invalidateDebounceMs);
				debouncer = _keyDebouncers.GetOrAdd(k, created);
				if (!ReferenceEquals(debouncer, created))
					created.Dispose();
				else if (_disposed)
				{
					// Lost a race with Dispose's drain — clean up the late-added debouncer.
					created.Dispose();
					return;
				}
			}
			debouncer.Trigger();
		}
		else
		{
			_fullListDebouncer!.Trigger();
		}
	}

	/// <summary>
	/// Disposes all pending invalidation debounce timers. Subsequent <see cref="Invalidate"/>
	/// calls are no-ops; already-running refreshes complete normally.
	/// </summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_fullListDebouncer?.Dispose();
		if (_keyDebouncers is not null)
		{
			foreach (var debouncer in _keyDebouncers.Values)
				debouncer.Dispose();
			_keyDebouncers.Clear();
		}
		GC.SuppressFinalize(this);
	}
}
