using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace NotNot.Collections;

/// <summary>
/// Generic tree collection with O(1) lookup, cached ordered traversal, and thread-safe external API.
/// <para><b>Threading</b>: All public methods are thread-safe. Concurrent reads and writes from
/// different threads are safe. Read methods return immutable snapshots that callers can iterate
/// without holding any lock.</para>
/// <para><b>Ordering</b>: Nodes are traversed in DFS pre-order, with siblings sorted
/// by <see cref="IComparable{T}"/> (ascending for oldest-first internal order).
/// Consumers wanting newest-first display should use a reverse view adapter.</para>
/// <para><b>CRITICAL - Tie-Breaking Requirement</b>: Consumer's <see cref="IComparable{T}"/>
/// implementation MUST include a tie-breaker (e.g., stable ID) when the primary sort key
/// can have duplicates. Without a tie-breaker, nodes with equal CompareTo results will have
/// non-deterministic order across re-renders.</para>
/// <para><b>Scale</b>: Supports 10,000+ nodes with iterative DFS traversal (no stack overflow)
/// and incremental cache maintenance for efficient updates.</para>
/// <para><b>Append Optimization</b>: Sequential additions of newer items (higher IComparable values)
/// use O(1) cache append instead of O(n) rebuild. This optimizes real-time streaming where
/// new events arrive chronologically. Use <see cref="AddRange"/> for batched operations.</para>
/// <para><b>Snapshot Pattern</b>: Read methods (<see cref="GetOrderedNodes"/>, <see cref="GetChildren"/>)
/// return <c>T[]</c> snapshots that are immutable once created. Snapshots are lazily built and cached
/// until the next mutation invalidates them. The cost is one <c>.ToArray()</c> copy per invalidation,
/// amortized across all subsequent reads.</para>
/// </summary>
/// <typeparam name="TKey">The type of keys identifying nodes. Must be non-null.</typeparam>
/// <typeparam name="TValue">The type of node values. Must implement IComparable for ordering.
/// See remarks for tie-breaking requirement.</typeparam>
public class GraphTree<TKey, TValue>
	where TKey : notnull
	where TValue : IComparable<TValue>
{
	private readonly Func<TValue, TKey> _keySelector;
	private readonly Func<TValue, TKey?> _parentKeySelector;

	// Coarse-grained lock protecting ALL internal state
	private readonly Lock _lock = new();

	// Primary storage - O(1) access
	private readonly Dictionary<TKey, TValue> _nodes = new();
	private readonly Dictionary<TKey, TKey?> _parentMap = new();

	// Derived storage - permanent incremental (except ordered cache)
	private readonly Dictionary<TKey, List<TKey>> _childrenKeysMap = new();
	private readonly Dictionary<TKey, List<TValue>> _sortedChildrenCache = new();
	private readonly HashSet<TKey> _rootKeys = new();

	// Internal mutable cache (for stable-append optimization)
	private List<TValue>? _orderedNodesCache;

	// External immutable snapshots (returned to callers)
	private TValue[]? _orderedNodesSnapshot;
	private readonly Dictionary<TKey, TValue[]> _childrenSnapshots = new();

	// Stable-append optimization: track if cache can accept appends
	private bool _cacheIsAppendable;
	private TKey? _lastCacheRootKey; // Last root in sorted order (for append detection)

	// Mutation tracking — Volatile.Read for lockless getter, Interlocked.Increment for writes
	private int _version;

	// Empty children sentinel
	private static readonly TValue[] EmptyChildren = Array.Empty<TValue>();

	/// <summary>
	/// Creates a new GraphTree with specified key and parent extractors.
	/// </summary>
	/// <param name="keySelector">Extracts unique key from node value.</param>
	/// <param name="parentKeySelector">Extracts parent key (null = root node).</param>
	/// <exception cref="ArgumentNullException">Thrown when keySelector or parentKeySelector is null.</exception>
	public GraphTree(
		Func<TValue, TKey> keySelector,
		Func<TValue, TKey?> parentKeySelector)
	{
		_keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
		_parentKeySelector = parentKeySelector ?? throw new ArgumentNullException(nameof(parentKeySelector));
	}

	/// <summary>Gets the number of nodes in the tree. Thread-safe.</summary>
	public int Count
	{
		get
		{
			using (_lock.EnterScope())
				return _nodes.Count;
		}
	}

	/// <summary>
	/// Version counter incremented on every mutation (Add, Remove, Clear).
	/// Use for cheap change detection without acquiring the lock.
	/// Thread-safe via volatile read — always sees the latest value.
	/// </summary>
	public int Version => Volatile.Read(ref _version);

	/// <summary>
	/// Adds or updates a node in the tree. O(1) amortized. Thread-safe.
	/// <para>If the key already exists, the node is replaced and <see cref="GraphTreeChangeType.Updated"/>
	/// is raised. Otherwise, <see cref="GraphTreeChangeType.Added"/> is raised.</para>
	/// <para>Parent does not need to exist yet (forward references OK).</para>
	/// <para><b>Cache behavior</b>: Incrementally updates children maps. Only invalidates
	/// the flat ordered cache (unavoidable for DFS ordering).</para>
	/// <para><b>Events</b>: <see cref="OnChanged"/> fires AFTER the lock is released.
	/// Handlers may safely read the tree without deadlock.</para>
	/// </summary>
	/// <param name="node">The node to add or update.</param>
	public void Add(TValue node)
	{
		GraphTreeChange<TKey, TValue> changeEvent;

		using (_lock.EnterScope())
		{
			var key = _keySelector(node);
			var parentKey = _parentKeySelector(node);

			var isUpdate = _nodes.TryGetValue(key, out var oldValue);
			TKey? oldParentKey = isUpdate && _parentMap.TryGetValue(key, out var op) ? op : default;

			_nodes[key] = node;
			_parentMap[key] = parentKey;

			// Incremental children map update
			if (isUpdate && oldParentKey is not null && !EqualityComparer<TKey?>.Default.Equals(oldParentKey, parentKey))
			{
				// Parent changed - remove from old parent's children
				if (_childrenKeysMap.TryGetValue(oldParentKey, out var oldSiblings))
					oldSiblings.Remove(key);
				_sortedChildrenCache.Remove(oldParentKey); // Mark old parent dirty
				_childrenSnapshots.Remove(oldParentKey);
			}

			if (parentKey is not null)
			{
				if (!_childrenKeysMap.TryGetValue(parentKey, out var siblings))
				{
					siblings = new List<TKey>();
					_childrenKeysMap[parentKey] = siblings;
				}
				if (!siblings.Contains(key))
					siblings.Add(key);
				_sortedChildrenCache.Remove(parentKey); // Mark parent dirty
				_childrenSnapshots.Remove(parentKey);
			}

			// Root tracking
			var isRoot = parentKey is null || !_nodes.ContainsKey(parentKey);
			if (isRoot)
				_rootKeys.Add(key);
			else
				_rootKeys.Remove(key);

			// Forward reference handling - if this node is now someone's parent, demote them from roots
			var hadForwardReferences = false;
			if (_childrenKeysMap.TryGetValue(key, out var existingChildren) && existingChildren.Count > 0)
			{
				hadForwardReferences = true;
				foreach (var childKey in existingChildren)
					_rootKeys.Remove(childKey);
			}

			// Stable-append optimization: try to append to existing cache instead of full invalidation
			// Conditions: cache exists, appendable, new node (not update), is a root, no forward references resolved
			var didAppendToCache = false;
			if (_orderedNodesCache is not null && _cacheIsAppendable && !isUpdate && isRoot && !hadForwardReferences)
			{
				// Check if new root sorts AFTER the last root (ascending order, so CompareTo > 0)
				if (_lastCacheRootKey is null ||
					(_nodes.TryGetValue(_lastCacheRootKey, out var lastRootValue) &&
					 node.CompareTo(lastRootValue) > 0))
				{
					// New root goes at the end of traversal - just append
					_orderedNodesCache.Add(node);
					_lastCacheRootKey = key;
					didAppendToCache = true;
					// _cacheIsAppendable remains true
				}
			}

			if (!didAppendToCache)
			{
				// Fall back to full invalidation
				InvalidateDerivedState();
			}
			else
			{
				// Internal cache was appended to — snapshot must be invalidated
				_orderedNodesSnapshot = null;
			}

			Interlocked.Increment(ref _version);

			// Capture event details inside lock
			changeEvent = new GraphTreeChange<TKey, TValue>(
				key,
				node,
				isUpdate ? oldValue : default,
				isUpdate ? GraphTreeChangeType.Updated : GraphTreeChangeType.Added,
				parentKey
			);
		}

		// Fire OUTSIDE lock — handlers can safely read the tree
		OnChanged?.Invoke(changeEvent);
	}

	/// <summary>
	/// Adds or updates multiple nodes in the tree with batched notification. Thread-safe.
	/// <para>Fires <see cref="OnChanged"/> ONCE after the lock is released with all changes.
	/// This provides O(1) amortized notification cost instead of O(n) for batch operations.</para>
	/// <para><b>Performance</b>: For bulk operations, significantly faster than repeated
	/// <see cref="Add"/> calls due to single notification.</para>
	/// <para><b>Ordering</b>: For best cache efficiency, pass nodes in ascending sort order
	/// (chronological for timestamps). This enables stable-append optimization.</para>
	/// </summary>
	/// <param name="nodes">The nodes to add or update.</param>
	/// <returns>The number of nodes processed.</returns>
	public int AddRange(IEnumerable<TValue> nodes)
	{
		int count;
		GraphTreeChange<TKey, TValue>? batchEvent = null;

		using (_lock.EnterScope())
		{
			count = 0;
			var changes = new List<GraphTreeChange<TKey, TValue>>();

			foreach (var node in nodes)
			{
				var key = _keySelector(node);
				var parentKey = _parentKeySelector(node);

				var isUpdate = _nodes.TryGetValue(key, out var oldValue);
				TKey? oldParentKey = isUpdate && _parentMap.TryGetValue(key, out var op) ? op : default;

				_nodes[key] = node;
				_parentMap[key] = parentKey;

				// Incremental children map update
				if (isUpdate && oldParentKey is not null && !EqualityComparer<TKey?>.Default.Equals(oldParentKey, parentKey))
				{
					if (_childrenKeysMap.TryGetValue(oldParentKey, out var oldSiblings))
						oldSiblings.Remove(key);
					_sortedChildrenCache.Remove(oldParentKey);
					_childrenSnapshots.Remove(oldParentKey);
				}

				if (parentKey is not null)
				{
					if (!_childrenKeysMap.TryGetValue(parentKey, out var siblings))
					{
						siblings = new List<TKey>();
						_childrenKeysMap[parentKey] = siblings;
					}
					if (!siblings.Contains(key))
						siblings.Add(key);
					_sortedChildrenCache.Remove(parentKey);
					_childrenSnapshots.Remove(parentKey);
				}

				// Root tracking
				var isRoot = parentKey is null || !_nodes.ContainsKey(parentKey);
				if (isRoot)
					_rootKeys.Add(key);
				else
					_rootKeys.Remove(key);

				// Forward reference handling
				var hadForwardReferences = false;
				if (_childrenKeysMap.TryGetValue(key, out var existingChildren) && existingChildren.Count > 0)
				{
					hadForwardReferences = true;
					foreach (var childKey in existingChildren)
						_rootKeys.Remove(childKey);
				}

				// Stable-append optimization for batch: try to append to cache
				var didAppendToCache = false;
				if (_orderedNodesCache is not null && _cacheIsAppendable && !isUpdate && isRoot && !hadForwardReferences)
				{
					if (_lastCacheRootKey is null ||
						(_nodes.TryGetValue(_lastCacheRootKey, out var lastRootValue) &&
						 node.CompareTo(lastRootValue) > 0))
					{
						_orderedNodesCache.Add(node);
						_lastCacheRootKey = key;
						didAppendToCache = true;
					}
				}

				if (!didAppendToCache)
				{
					InvalidateDerivedState();
				}
				else
				{
					_orderedNodesSnapshot = null;
				}

				// Collect granular change for deferred notification
				changes.Add(new GraphTreeChange<TKey, TValue>(
					key,
					node,
					isUpdate ? oldValue : default,
					isUpdate ? GraphTreeChangeType.Updated : GraphTreeChangeType.Added,
					parentKey
				));

				count++;
			}

			if (count > 0)
			{
				Interlocked.Increment(ref _version);

				batchEvent = new GraphTreeChange<TKey, TValue>(
					default!,
					default,
					default,
					GraphTreeChangeType.BatchComplete,
					default
				) { BatchChanges = changes };
			}
		}

		// Fire OUTSIDE lock
		if (batchEvent is not null)
			OnChanged?.Invoke(batchEvent);

		return count;
	}

	/// <summary>
	/// Removes a node and all its descendants from the tree. Thread-safe.
	/// <para>Fires <see cref="GraphTreeChangeType.Removed"/> for each removed node,
	/// after the lock is released.</para>
	/// <para><b>Scale</b>: Uses iterative traversal - safe for deep trees (10,000+ nodes).</para>
	/// </summary>
	/// <param name="key">The key of the node to remove.</param>
	/// <returns>True if the node existed and was removed; false otherwise.</returns>
	public bool Remove(TKey key)
	{
		GraphTreeChange<TKey, TValue>? changeEvent;

		using (_lock.EnterScope())
		{
			if (!_nodes.TryGetValue(key, out var value))
				return false;

			// Phase 0: Capture ALL parent keys and values BEFORE any state modification
			var keysToRemove = new List<TKey>();
			var visited = new HashSet<TKey>();
			CollectDescendants(key, keysToRemove, visited);

			var capturedParentKeys = new Dictionary<TKey, TKey?>();
			var removedNodes = new List<(TKey Key, TValue Value)>();
			foreach (var k in keysToRemove)
			{
				capturedParentKeys[k] = _parentMap.TryGetValue(k, out var pk) ? pk : default;
				if (_nodes.TryGetValue(k, out var removedValue))
					removedNodes.Add((k, removedValue));
			}

			// Phase 1: Incremental cache cleanup for EACH key being removed
			var keysToRemoveSet = keysToRemove.ToHashSet();
			foreach (var k in keysToRemove)
			{
				var kParent = capturedParentKeys[k];

				// Remove this node's entry from children maps
				_childrenKeysMap.Remove(k);
				_sortedChildrenCache.Remove(k);
				_childrenSnapshots.Remove(k);

				// Remove from parent's child list (if parent exists and isn't also being deleted)
				if (kParent is not null && !keysToRemoveSet.Contains(kParent))
				{
					if (_childrenKeysMap.TryGetValue(kParent, out var parentChildren))
						parentChildren.Remove(k);
					_sortedChildrenCache.Remove(kParent); // Mark parent dirty
					_childrenSnapshots.Remove(kParent);
				}

				// Remove from roots
				_rootKeys.Remove(k);
			}

			// Phase 2: Remove from primary storage
			foreach (var k in keysToRemove)
			{
				_nodes.Remove(k);
				_parentMap.Remove(k);
			}

			// Phase 3: Invalidate ordered cache and version
			InvalidateDerivedState();
			Interlocked.Increment(ref _version);

			// Phase 4: Build notification event
			if (removedNodes.Count == 1)
			{
				var (k, removedValue) = removedNodes[0];
				changeEvent = new GraphTreeChange<TKey, TValue>(
					k,
					default,
					removedValue,
					GraphTreeChangeType.Removed,
					capturedParentKeys[k]
				);
			}
			else
			{
				var changes = removedNodes.Select(r => new GraphTreeChange<TKey, TValue>(
					r.Key,
					default,
					r.Value,
					GraphTreeChangeType.Removed,
					capturedParentKeys[r.Key]
				)).ToList();

				changeEvent = new GraphTreeChange<TKey, TValue>(
					default!,
					default,
					default,
					GraphTreeChangeType.BatchComplete,
					default
				) { BatchChanges = changes };
			}
		}

		// Fire OUTSIDE lock
		OnChanged?.Invoke(changeEvent);

		return true;
	}

	/// <summary>
	/// Removes all nodes from the tree. Thread-safe.
	/// <para>Fires <see cref="GraphTreeChangeType.Cleared"/> once, after the lock is released.</para>
	/// </summary>
	public void Clear()
	{
		bool hadNodes;

		using (_lock.EnterScope())
		{
			hadNodes = _nodes.Count > 0;
			if (!hadNodes)
				return;

			// Clear all storage
			_nodes.Clear();
			_parentMap.Clear();
			_childrenKeysMap.Clear();
			_sortedChildrenCache.Clear();
			_childrenSnapshots.Clear();
			_rootKeys.Clear();
			InvalidateDerivedState();
			Interlocked.Increment(ref _version);
		}

		// Fire OUTSIDE lock
		if (hadNodes)
		{
			OnChanged?.Invoke(new GraphTreeChange<TKey, TValue>(
				default!,
				default,
				default,
				GraphTreeChangeType.Cleared,
				default
			));
		}
	}

	/// <summary>Gets a node by key. O(1). Thread-safe.</summary>
	/// <param name="key">The key of the node to retrieve.</param>
	/// <returns>The node value, or default if not found.</returns>
	public TValue? GetNode(TKey key)
	{
		using (_lock.EnterScope())
			return _nodes.TryGetValue(key, out var value) ? value : default;
	}

	/// <summary>Gets the parent of a node. O(1). Thread-safe.</summary>
	/// <param name="key">The key of the node whose parent to retrieve.</param>
	/// <returns>The parent node value, or default if root or not found.</returns>
	public TValue? GetParent(TKey key)
	{
		using (_lock.EnterScope())
		{
			if (!_parentMap.TryGetValue(key, out var parentKey) || parentKey is null)
				return default;
			return _nodes.TryGetValue(parentKey, out var parent) ? parent : default;
		}
	}

	/// <summary>
	/// Gets the direct children of a node. Thread-safe.
	/// <para>Returns an immutable <c>T[]</c> snapshot that callers can iterate freely without
	/// holding any lock. The snapshot is cached and reused until the parent's children change.</para>
	/// <para><b>Performance Note</b>: First call per parent after a mutation triggers sort + snapshot
	/// build (O(k log k) where k = child count). Subsequent calls return cached snapshot in O(1).</para>
	/// </summary>
	/// <param name="key">The key of the parent node.</param>
	/// <returns>Read-only array of child nodes sorted by IComparable (ascending, oldest first), empty if none.</returns>
	public IReadOnlyList<TValue> GetChildren(TKey key)
	{
		using (_lock.EnterScope())
		{
			// Fast path: snapshot already built
			if (_childrenSnapshots.TryGetValue(key, out var snapshot))
				return snapshot;

			if (!_childrenKeysMap.TryGetValue(key, out var childKeys) || childKeys.Count == 0)
				return EmptyChildren;

			// Ensure sorted internal cache exists
			if (!_sortedChildrenCache.TryGetValue(key, out var sorted))
			{
				sorted = childKeys
					.Select(k => _nodes[k])
					.OrderBy(v => v)
					.ToList();
				_sortedChildrenCache[key] = sorted;
			}

			// Build immutable snapshot
			snapshot = sorted.ToArray();
			_childrenSnapshots[key] = snapshot;
			return snapshot;
		}
	}

	/// <summary>
	/// Gets all nodes in DFS pre-order traversal. Thread-safe.
	/// <para>Returns an immutable <c>T[]</c> snapshot that callers can iterate freely without
	/// holding any lock. The snapshot is cached and reused until any mutation invalidates it.</para>
	/// <para><b>Performance</b>: First call after mutation triggers O(n) DFS traversal + snapshot copy.
	/// Subsequent calls return cached snapshot in O(1).</para>
	/// <para><b>Scale</b>: Uses iterative DFS - safe for deep trees (10,000+ nodes).</para>
	/// </summary>
	/// <returns>Read-only array of all nodes in traversal order (oldest first).</returns>
	public IReadOnlyList<TValue> GetOrderedNodes()
	{
		using (_lock.EnterScope())
		{
			if (_orderedNodesSnapshot is not null)
				return _orderedNodesSnapshot;

			// Ensure internal mutable cache is built
			if (_orderedNodesCache is null)
				_orderedNodesCache = ComputeOrderedNodes();

			_orderedNodesSnapshot = _orderedNodesCache.ToArray();
			return _orderedNodesSnapshot;
		}
	}

	/// <summary>
	/// Fired after any mutation with change details.
	/// <para><b>Contract</b>: Fired ONCE per mutation operation, AFTER the lock is released.
	/// Handlers may safely call read methods on this tree without deadlock.</para>
	/// <para>For batch operations, <see cref="GraphTreeChange{TKey, TValue}.BatchChanges"/>
	/// contains individual changes.</para>
	/// <para>Consumers that don't need change details can simply trigger re-render:
	/// <c>tree.OnChanged += _ => StateHasChanged();</c></para>
	/// <para><b>Threading</b>: Handlers execute on the caller's thread (the thread that
	/// performed the mutation). Handlers needing a specific context (e.g., Blazor circuit)
	/// must marshal themselves via <c>InvokeAsync</c> or equivalent.</para>
	/// </summary>
	public event Action<GraphTreeChange<TKey, TValue>>? OnChanged;

	#region Private Implementation

	private void InvalidateDerivedState()
	{
		// Only ordered cache needs full invalidation
		// Children maps are maintained incrementally
		_orderedNodesCache = null;
		_orderedNodesSnapshot = null;
		_cacheIsAppendable = false;
		_lastCacheRootKey = default;
	}

	private List<TValue> ComputeOrderedNodes()
	{
		if (_nodes.Count == 0)
		{
			_cacheIsAppendable = true; // Empty cache can accept appends
			_lastCacheRootKey = default;
			return new List<TValue>();
		}

		// Use cached root keys instead of O(n) discovery (ascending for oldest-first)
		var sortedRoots = _rootKeys
			.Select(k => _nodes[k])
			.OrderBy(v => v)
			.ToList();

		var result = new List<TValue>(_nodes.Count);
		var visited = new HashSet<TKey>();
		var stack = new Stack<TKey>();

		// Push roots in reverse order (last pushed = first processed for correct order)
		for (int i = sortedRoots.Count - 1; i >= 0; i--)
			stack.Push(_keySelector(sortedRoots[i]));

		while (stack.Count > 0)
		{
			var key = stack.Pop();
			if (!visited.Add(key))
				continue; // Cycle protection

			result.Add(_nodes[key]);

			// Use internal GetChildrenInternal to avoid snapshot overhead during traversal
			var children = GetChildrenInternal(key);
			for (int i = children.Count - 1; i >= 0; i--)
				stack.Push(_keySelector(children[i]));
		}

		// Track state for stable-append optimization
		// Cache is appendable if we completed a full rebuild
		_cacheIsAppendable = true;
		_lastCacheRootKey = sortedRoots.Count > 0 ? _keySelector(sortedRoots[^1]) : default;

		return result;
	}

	/// <summary>
	/// Internal children access for use within locked sections (no snapshot overhead).
	/// Returns the internal sorted list directly.
	/// </summary>
	private IReadOnlyList<TValue> GetChildrenInternal(TKey key)
	{
		if (_sortedChildrenCache.TryGetValue(key, out var cached))
			return cached;

		if (!_childrenKeysMap.TryGetValue(key, out var childKeys) || childKeys.Count == 0)
			return EmptyChildren;

		// Rebuild only this parent's sorted children (ascending for oldest-first)
		var sorted = childKeys
			.Select(k => _nodes[k])
			.OrderBy(v => v)
			.ToList();

		_sortedChildrenCache[key] = sorted;
		return sorted;
	}

	private void CollectDescendants(TKey key, List<TKey> keys, HashSet<TKey> visited)
	{
		// Iterative traversal to avoid stack overflow on deep trees
		var stack = new Stack<TKey>();
		stack.Push(key);

		while (stack.Count > 0)
		{
			var current = stack.Pop();
			if (!visited.Add(current))
				continue; // Cycle protection

			keys.Add(current);

			if (_childrenKeysMap.TryGetValue(current, out var childKeys))
			{
				// Push in reverse order to preserve original DFS traversal order
				for (int i = childKeys.Count - 1; i >= 0; i--)
					stack.Push(childKeys[i]);
			}
		}
	}

	/// <summary>
	/// Debug-only validation that incremental state matches what a full rebuild would produce.
	/// </summary>
	[Conditional("DEBUG")]
	internal void DebugValidateIntegrity()
	{
		using (_lock.EnterScope())
		{
			// Rebuild children map from scratch
			var rebuiltChildren = new Dictionary<TKey, List<TKey>>();
			foreach (var (nodeKey, parentKey) in _parentMap)
			{
				if (parentKey is not null && _nodes.ContainsKey(parentKey))
				{
					if (!rebuiltChildren.TryGetValue(parentKey, out var list))
						rebuiltChildren[parentKey] = list = new List<TKey>();
					list.Add(nodeKey);
				}
			}

			// Rebuild root keys from scratch
			var rebuiltRoots = _nodes.Keys
				.Where(k => !_parentMap.TryGetValue(k, out var p) || p is null || !_nodes.ContainsKey(p))
				.ToHashSet();

			// Assert equality
			__.AssertIfNot(_childrenKeysMap.Count == rebuiltChildren.Count,
				$"Children map count mismatch: incremental={_childrenKeysMap.Count}, rebuilt={rebuiltChildren.Count}");
			__.AssertIfNot(_rootKeys.SetEquals(rebuiltRoots),
				$"Root keys mismatch: incremental={string.Join(",", _rootKeys)}, rebuilt={string.Join(",", rebuiltRoots)}");
		}
	}

	#endregion
}

/// <summary>
/// Describes a change to a <see cref="GraphTree{TKey, TValue}"/>.
/// <para>For single-item changes (Add, Update, Remove), access Key/Value/OldValue/ParentKey directly.</para>
/// <para>For batch changes (AddRange), check <see cref="ChangeType"/> == <see cref="GraphTreeChangeType.BatchComplete"/>
/// and access <see cref="BatchChanges"/> for the individual changes.</para>
/// <para>Consumers that don't care about change details can simply trigger re-render on any notification.</para>
/// </summary>
/// <typeparam name="TKey">The type of keys in the tree.</typeparam>
/// <typeparam name="TValue">The type of values in the tree.</typeparam>
/// <param name="Key">The key of the affected node. <b>Warning</b>: For <see cref="GraphTreeChangeType.Cleared"/>
/// and <see cref="GraphTreeChangeType.BatchComplete"/>, this is <c>default(TKey)</c>.</param>
/// <param name="Value">The current value (null for Removed/Cleared/BatchComplete).</param>
/// <param name="OldValue">The previous value (null for Added/Cleared/BatchComplete).</param>
/// <param name="ChangeType">The type of change.</param>
/// <param name="ParentKey">The parent key of the affected node (null for roots, Cleared, or BatchComplete).
/// For <see cref="GraphTreeChangeType.Removed"/> notifications, this is the parent key captured BEFORE deletion.</param>
public record GraphTreeChange<TKey, TValue>(
	TKey Key,
	TValue? Value,
	TValue? OldValue,
	GraphTreeChangeType ChangeType,
	TKey? ParentKey
) where TKey : notnull
{
	/// <summary>
	/// For <see cref="GraphTreeChangeType.BatchComplete"/>, contains the individual changes in the batch.
	/// Null for all other change types.
	/// </summary>
	public IReadOnlyList<GraphTreeChange<TKey, TValue>>? BatchChanges { get; init; }
}

/// <summary>
/// Types of changes that can occur in a <see cref="GraphTree{TKey, TValue}"/>.
/// </summary>
public enum GraphTreeChangeType
{
	/// <summary>A new node was added.</summary>
	Added,

	/// <summary>An existing node was replaced.</summary>
	Updated,

	/// <summary>A node (and its descendants) was removed.</summary>
	Removed,

	/// <summary>All nodes were removed via Clear().</summary>
	Cleared,

	/// <summary>A batch operation (AddRange) completed. Check <see cref="GraphTreeChange{TKey,TValue}.BatchChanges"/> for details.</summary>
	BatchComplete
}
