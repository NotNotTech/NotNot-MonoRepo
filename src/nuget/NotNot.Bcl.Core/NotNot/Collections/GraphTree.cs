using System;
using System.Collections.Generic;
using System.Linq;

namespace NotNot.Collections;

/// <summary>
/// Generic tree collection with O(1) lookup and cached ordered traversal.
/// <para><b>Threading</b>: NOT thread-safe. All operations must occur on a single
/// execution context (e.g., Blazor circuit synchronization context).</para>
/// <para><b>Ordering</b>: Nodes are traversed in DFS pre-order, with siblings sorted
/// by <see cref="IComparable{T}"/> (descending for newest-first display).</para>
/// <para><b>CRITICAL - Tie-Breaking Requirement</b>: Consumer's <see cref="IComparable{T}"/>
/// implementation MUST include a tie-breaker (e.g., stable ID) when the primary sort key
/// can have duplicates. Without a tie-breaker, nodes with equal CompareTo results will have
/// non-deterministic order across re-renders.</para>
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

	// Primary storage - O(1) access
	private readonly Dictionary<TKey, TValue> _nodes = new();
	private readonly Dictionary<TKey, TKey?> _parentMap = new();

	// Derived storage - built lazily, invalidated on mutation
	private Dictionary<TKey, List<TKey>>? _childrenKeysMap;
	private Dictionary<TKey, List<TValue>>? _sortedChildrenCache;
	private List<TValue>? _orderedNodesCache;

	// Mutation tracking
	private int _version;

	// Empty children sentinel
	private static readonly IReadOnlyList<TValue> EmptyChildren = Array.Empty<TValue>();

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

	/// <summary>Gets the number of nodes in the tree.</summary>
	public int Count => _nodes.Count;

	/// <summary>
	/// Version counter incremented on every mutation (Add, Remove, Clear).
	/// Use for cheap change detection without re-reading the full tree.
	/// </summary>
	public int Version => _version;

	/// <summary>
	/// Adds or updates a node in the tree. O(1).
	/// <para>If the key already exists, the node is replaced and <see cref="GraphTreeChangeType.Updated"/>
	/// is raised. Otherwise, <see cref="GraphTreeChangeType.Added"/> is raised.</para>
	/// <para>Parent does not need to exist yet (forward references OK).</para>
	/// </summary>
	/// <param name="node">The node to add or update.</param>
	public void Add(TValue node)
	{
		var key = _keySelector(node);
		var parentKey = _parentKeySelector(node);

		var isUpdate = _nodes.TryGetValue(key, out var oldValue);

		_nodes[key] = node;
		_parentMap[key] = parentKey;

		// CRITICAL: Invalidate caches and increment version BEFORE firing notifications
		InvalidateDerivedState();
		_version++;

		// Granular notification
		OnChangeGranular?.Invoke(new GraphTreeChange<TKey, TValue>(
			key,
			node,
			isUpdate ? oldValue : default,
			isUpdate ? GraphTreeChangeType.Updated : GraphTreeChangeType.Added
		));

		// Coarse notification
		OnChanged?.Invoke();
	}

	/// <summary>
	/// Removes a node and all its descendants from the tree.
	/// <para>Fires <see cref="GraphTreeChangeType.Removed"/> for each removed node.</para>
	/// </summary>
	/// <param name="key">The key of the node to remove.</param>
	/// <returns>True if the node existed and was removed; false otherwise.</returns>
	public bool Remove(TKey key)
	{
		if (!_nodes.TryGetValue(key, out var value))
			return false;

		// Collect all keys to remove (node + descendants) with cycle protection
		var keysToRemove = new List<TKey>();
		var visited = new HashSet<TKey>();
		CollectDescendants(key, keysToRemove, visited);

		// Phase 1: Remove all nodes and collect their values for notifications
		var removedNodes = new List<(TKey Key, TValue Value)>();
		foreach (var k in keysToRemove)
		{
			if (_nodes.TryGetValue(k, out var removedValue))
			{
				_nodes.Remove(k);
				_parentMap.Remove(k);
				removedNodes.Add((k, removedValue));
			}
		}

		// CRITICAL: Invalidate caches and increment version BEFORE firing notifications
		// This ensures handlers see consistent state (all nodes already removed)
		InvalidateDerivedState();
		_version++;

		// Phase 2: Fire notifications AFTER all mutations complete
		foreach (var (k, removedValue) in removedNodes)
		{
			OnChangeGranular?.Invoke(new GraphTreeChange<TKey, TValue>(
				k,
				default,
				removedValue,
				GraphTreeChangeType.Removed
			));
		}

		// Coarse notification (once for the entire operation)
		OnChanged?.Invoke();

		return true;
	}

	/// <summary>
	/// Removes all nodes from the tree.
	/// <para>Fires <see cref="GraphTreeChangeType.Cleared"/> once.</para>
	/// </summary>
	public void Clear()
	{
		if (_nodes.Count == 0)
			return;

		// CRITICAL: Invalidate caches and increment version BEFORE firing notifications
		_nodes.Clear();
		_parentMap.Clear();
		InvalidateDerivedState();
		_version++;

		// Single notification for clear
		OnChangeGranular?.Invoke(new GraphTreeChange<TKey, TValue>(
			default!,
			default,
			default,
			GraphTreeChangeType.Cleared
		));

		OnChanged?.Invoke();
	}

	/// <summary>Gets a node by key. O(1).</summary>
	/// <param name="key">The key of the node to retrieve.</param>
	/// <returns>The node value, or default if not found.</returns>
	public TValue? GetNode(TKey key)
	{
		return _nodes.TryGetValue(key, out var value) ? value : default;
	}

	/// <summary>Gets the parent of a node. O(1).</summary>
	/// <param name="key">The key of the node whose parent to retrieve.</param>
	/// <returns>The parent node value, or default if root or not found.</returns>
	public TValue? GetParent(TKey key)
	{
		if (!_parentMap.TryGetValue(key, out var parentKey) || parentKey is null)
			return default;

		return _nodes.TryGetValue(parentKey, out var parent) ? parent : default;
	}

	/// <summary>
	/// Gets the direct children of a node. True O(1) after cache population (R2.6).
	/// <para>Returns cached sorted list directly - zero allocation per call.</para>
	/// <para><b>Performance Note</b>: FIRST call after mutation triggers full cache rebuild (O(n log k)).
	/// Subsequent calls are true O(1).</para>
	/// </summary>
	/// <param name="key">The key of the parent node.</param>
	/// <returns>Read-only list of child nodes sorted by IComparable (descending), empty if none.</returns>
	public IReadOnlyList<TValue> GetChildren(TKey key)
	{
		EnsureSortedChildrenCache();

		if (_sortedChildrenCache!.TryGetValue(key, out var children))
			return children;

		return EmptyChildren;
	}

	/// <summary>
	/// Gets all nodes in DFS pre-order traversal. O(n), cached.
	/// <para>Cache is invalidated on any mutation. Siblings are sorted by
	/// <see cref="IComparable{T}"/> (typically descending for newest-first).</para>
	/// </summary>
	/// <returns>Read-only list of all nodes in traversal order.</returns>
	public IReadOnlyList<TValue> GetOrderedNodes()
	{
		if (_orderedNodesCache is not null)
			return _orderedNodesCache;

		_orderedNodesCache = ComputeOrderedNodes();
		return _orderedNodesCache;
	}

	/// <summary>
	/// Fired for each granular change (Add, Remove, Update, Clear).
	/// <para><b>Contract</b>: Notifications fire synchronously during mutation.
	/// Consumers should not mutate the tree during notification callbacks.
	/// Exceptions from callbacks terminate the notification chain.</para>
	/// </summary>
	public event Action<GraphTreeChange<TKey, TValue>>? OnChangeGranular;

	/// <summary>
	/// Fired after any mutation. Coarse notification for simple consumers.
	/// <para><b>Contract</b>: Fired once per mutation operation, after all granular notifications.</para>
	/// </summary>
	public event Action? OnChanged;

	#region Private Implementation

	private void InvalidateDerivedState()
	{
		_childrenKeysMap = null;
		_sortedChildrenCache = null;
		_orderedNodesCache = null;
	}

	private void EnsureChildrenKeysMap()
	{
		if (_childrenKeysMap is not null)
			return;

		_childrenKeysMap = new Dictionary<TKey, List<TKey>>();

		foreach (var (key, parentKey) in _parentMap)
		{
			if (parentKey is null)
				continue;

			if (!_childrenKeysMap.TryGetValue(parentKey, out var children))
			{
				children = new List<TKey>();
				_childrenKeysMap[parentKey] = children;
			}

			children.Add(key);
		}
	}

	private void EnsureSortedChildrenCache()
	{
		if (_sortedChildrenCache is not null)
			return;

		EnsureChildrenKeysMap();

		_sortedChildrenCache = new Dictionary<TKey, List<TValue>>();

		foreach (var (parentKey, childKeys) in _childrenKeysMap!)
		{
			// Sort once per parent, cache forever (until invalidation)
			var sortedChildren = childKeys
				.Select(k => _nodes[k])
				.OrderByDescending(v => v)
				.ToList();

			_sortedChildrenCache[parentKey] = sortedChildren;
		}
	}

	private List<TValue> ComputeOrderedNodes()
	{
		if (_nodes.Count == 0)
			return new List<TValue>();

		EnsureSortedChildrenCache();

		// Find roots (nodes with null parent OR parent not in tree)
		var roots = _nodes.Keys
			.Where(k => !_parentMap.TryGetValue(k, out var p) ||
						p is null ||
						!_nodes.ContainsKey(p))
			.Select(k => _nodes[k])
			.OrderByDescending(v => v)
			.ToList();

		var result = new List<TValue>(_nodes.Count);
		var visited = new HashSet<TKey>();

		foreach (var root in roots)
		{
			DfsTraverse(_keySelector(root), result, visited);
		}

		return result;
	}

	private void DfsTraverse(TKey key, List<TValue> result, HashSet<TKey> visited)
	{
		if (!visited.Add(key))
			return; // Cycle protection

		result.Add(_nodes[key]);

		// Use pre-sorted cache (R2.6) - no per-call sorting
		if (_sortedChildrenCache!.TryGetValue(key, out var sortedChildren))
		{
			foreach (var child in sortedChildren)
			{
				DfsTraverse(_keySelector(child), result, visited);
			}
		}
	}

	private void CollectDescendants(TKey key, List<TKey> keys, HashSet<TKey> visited)
	{
		// Cycle protection - prevent infinite recursion if graph has cycles
		if (!visited.Add(key))
			return;

		keys.Add(key);

		// Need to find children - build map if not exists
		EnsureChildrenKeysMap();

		if (_childrenKeysMap!.TryGetValue(key, out var childKeys))
		{
			foreach (var childKey in childKeys)
			{
				CollectDescendants(childKey, keys, visited);
			}
		}
	}

	#endregion
}

/// <summary>
/// Describes a change to a <see cref="GraphTree{TKey, TValue}"/>.
/// </summary>
/// <typeparam name="TKey">The type of keys in the tree.</typeparam>
/// <typeparam name="TValue">The type of values in the tree.</typeparam>
/// <param name="Key">The key of the affected node. <b>Warning</b>: For <see cref="GraphTreeChangeType.Cleared"/>,
/// this is <c>default(TKey)</c> which may be null for reference types - do not access without checking ChangeType first.</param>
/// <param name="Value">The current value (null for Removed/Cleared).</param>
/// <param name="OldValue">The previous value (null for Added/Cleared).</param>
/// <param name="ChangeType">The type of change.</param>
public record GraphTreeChange<TKey, TValue>(
	TKey Key,
	TValue? Value,
	TValue? OldValue,
	GraphTreeChangeType ChangeType
) where TKey : notnull;

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
	Cleared
}
