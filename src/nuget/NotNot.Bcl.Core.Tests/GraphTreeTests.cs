using NotNot.Collections;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

/// <summary>
/// Unit tests for <see cref="GraphTree{TKey, TValue}"/>.
/// </summary>
public class GraphTreeTests
{
	#region Test Helper Type

	/// <summary>
	/// Test node implementing IComparable with tie-breaking (R2.5 compliance).
	/// </summary>
	/// <remarks>
	/// CompareTo returns standard comparison value. With GraphTree's OrderBy (ascending):
	/// - Lower Order values come FIRST (oldest-first internal order)
	/// - When Order is equal, lower Id comes first (stable tie-breaker)
	///
	/// OrderBy behavior: if a.CompareTo(b) &lt; 0, a comes BEFORE b.
	/// To get lower Order first: return negative when THIS.Order &lt; other.Order.
	/// </remarks>
	private record TestNode(string Id, string? ParentId, int Order) : IComparable<TestNode>
	{
		public int CompareTo(TestNode? other)
		{
			if (other is null) return 1;

			// Primary sort: lower Order comes first with OrderBy (ascending)
			// Return negative when this.Order < other.Order (so "this" comes first)
			var cmp = Order.CompareTo(other.Order);
			if (cmp != 0) return cmp;

			// Tie-breaker: lower Id comes first (stable ordering)
			// Return negative when this.Id < other.Id (so "this" comes first)
			return string.Compare(Id, other.Id, StringComparison.Ordinal);
		}
	}

	private static GraphTree<string, TestNode> CreateTree()
	{
		return new GraphTree<string, TestNode>(
			keySelector: n => n.Id,
			parentKeySelector: n => n.ParentId
		);
	}

	#endregion

	#region Add Tests

	[Fact]
	public void Add_NewNode_IncreasesCount()
	{
		var tree = CreateTree();

		tree.Add(new TestNode("A", null, 1));

		Assert.Equal(1, tree.Count);
	}

	[Fact]
	public void Add_NewNode_IncrementsVersion()
	{
		var tree = CreateTree();
		var initialVersion = tree.Version;

		tree.Add(new TestNode("A", null, 1));

		Assert.Equal(initialVersion + 1, tree.Version);
	}

	[Fact]
	public void Add_UpsertExisting_ReplacesNode()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));

		tree.Add(new TestNode("A", null, 2)); // Same Id, different Order

		Assert.Equal(1, tree.Count);
		var node = tree.GetNode("A");
		Assert.NotNull(node);
		Assert.Equal(2, node.Order);
	}

	[Fact]
	public void Add_UpsertExisting_FiresUpdatedNotification()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));

		GraphTreeChange<string, TestNode>? capturedChange = null;
		tree.OnChanged += change => capturedChange = change;

		tree.Add(new TestNode("A", null, 2));

		Assert.NotNull(capturedChange);
		Assert.Equal("A", capturedChange.Key);
		Assert.Equal(GraphTreeChangeType.Updated, capturedChange.ChangeType);
		Assert.Equal(1, capturedChange.OldValue?.Order);
		Assert.Equal(2, capturedChange.Value?.Order);
	}

	#endregion

	#region Remove Tests

	[Fact]
	public void Remove_SingleNode_DecreasesCount()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));

		var result = tree.Remove("A");

		Assert.True(result);
		Assert.Equal(0, tree.Count);
	}

	[Fact]
	public void Remove_WithSubtree_RemovesAllDescendants()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("C", "B", 3));

		tree.Remove("A");

		Assert.Equal(0, tree.Count);
		Assert.Null(tree.GetNode("A"));
		Assert.Null(tree.GetNode("B"));
		Assert.Null(tree.GetNode("C"));
	}

	[Fact]
	public void Remove_NonExistentKey_ReturnsFalse()
	{
		var tree = CreateTree();

		var result = tree.Remove("NonExistent");

		Assert.False(result);
	}

	[Fact]
	public void Remove_FiresRemovedForEachNode()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));

		var removedKeys = new List<string>();
		tree.OnChanged += change =>
		{
			// Multi-node cascade removes fire as BatchComplete with individual Removed changes inside
			if (change.ChangeType == GraphTreeChangeType.BatchComplete && change.BatchChanges != null)
			{
				foreach (var c in change.BatchChanges)
					if (c.ChangeType == GraphTreeChangeType.Removed)
						removedKeys.Add(c.Key);
			}
			else if (change.ChangeType == GraphTreeChangeType.Removed)
			{
				removedKeys.Add(change.Key);
			}
		};

		tree.Remove("A");

		Assert.Equal(2, removedKeys.Count);
		Assert.Contains("A", removedKeys);
		Assert.Contains("B", removedKeys);
	}

	#endregion

	#region Clear Tests

	[Fact]
	public void Clear_RemovesAllNodes()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));

		tree.Clear();

		Assert.Equal(0, tree.Count);
	}

	[Fact]
	public void Clear_FiresClearedOnce()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));

		var clearCount = 0;
		tree.OnChanged += change =>
		{
			if (change.ChangeType == GraphTreeChangeType.Cleared)
				clearCount++;
		};

		tree.Clear();

		Assert.Equal(1, clearCount);
	}

	#endregion

	#region GetNode Tests

	[Fact]
	public void GetNode_Exists_ReturnsNode()
	{
		var tree = CreateTree();
		var node = new TestNode("A", null, 1);
		tree.Add(node);

		var result = tree.GetNode("A");

		Assert.Equal(node, result);
	}

	[Fact]
	public void GetNode_NotFound_ReturnsDefault()
	{
		var tree = CreateTree();

		var result = tree.GetNode("NonExistent");

		Assert.Null(result);
	}

	#endregion

	#region GetParent Tests

	[Fact]
	public void GetParent_HasParent_ReturnsParent()
	{
		var tree = CreateTree();
		var parent = new TestNode("A", null, 1);
		var child = new TestNode("B", "A", 2);
		tree.Add(parent);
		tree.Add(child);

		var result = tree.GetParent("B");

		Assert.Equal(parent, result);
	}

	[Fact]
	public void GetParent_RootNode_ReturnsDefault()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));

		var result = tree.GetParent("A");

		Assert.Null(result);
	}

	[Fact]
	public void GetParent_ParentNotInTree_ReturnsDefault()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("B", "A", 2)); // Parent "A" not added

		var result = tree.GetParent("B");

		Assert.Null(result);
	}

	#endregion

	#region GetChildren Tests

	[Fact]
	public void GetChildren_HasChildren_ReturnsSortedChildren()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("C", "A", 3));

		var children = tree.GetChildren("A");

		Assert.Equal(2, children.Count);
		// Ascending order: B (2) before C (3)
		Assert.Equal("B", children[0].Id);
		Assert.Equal("C", children[1].Id);
	}

	[Fact]
	public void GetChildren_LeafNode_ReturnsEmpty()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));

		var children = tree.GetChildren("A");

		Assert.Empty(children);
	}

	[Fact]
	public void GetChildren_NonExistentKey_ReturnsEmpty()
	{
		var tree = CreateTree();

		var children = tree.GetChildren("NonExistent");

		Assert.Empty(children);
	}

	[Fact]
	public void GetChildren_TrueO1AfterFirstCall()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));

		// First call builds cache
		var children1 = tree.GetChildren("A");
		// Second call should return same cached instance
		var children2 = tree.GetChildren("A");

		Assert.Same(children1, children2);
	}

	#endregion

	#region GetOrderedNodes Tests

	[Fact]
	public void GetOrderedNodes_SingleRoot_ReturnsDfsPreOrder()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("C", "B", 3));

		var ordered = tree.GetOrderedNodes();

		Assert.Equal(3, ordered.Count);
		Assert.Equal("A", ordered[0].Id);
		Assert.Equal("B", ordered[1].Id);
		Assert.Equal("C", ordered[2].Id);
	}

	[Fact]
	public void GetOrderedNodes_MultipleRoots_SortedAscending()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));
		tree.Add(new TestNode("C", null, 3));

		var ordered = tree.GetOrderedNodes();

		Assert.Equal(3, ordered.Count);
		// Lower Order values come first (ascending by Order)
		Assert.Equal("A", ordered[0].Id); // Order 1
		Assert.Equal("B", ordered[1].Id); // Order 2
		Assert.Equal("C", ordered[2].Id); // Order 3
	}

	[Fact]
	public void GetOrderedNodes_OrderingByIComparable()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("Root", null, 10));
		tree.Add(new TestNode("Child1", "Root", 5));
		tree.Add(new TestNode("Child2", "Root", 8));
		tree.Add(new TestNode("Child3", "Root", 3));

		var ordered = tree.GetOrderedNodes();

		// Root first (DFS pre-order), then children in ascending order by Order
		Assert.Equal(4, ordered.Count);
		Assert.Equal("Root", ordered[0].Id);     // Order 10, parent
		Assert.Equal("Child3", ordered[1].Id);   // Order 3 (lowest child)
		Assert.Equal("Child1", ordered[2].Id);   // Order 5
		Assert.Equal("Child2", ordered[3].Id);   // Order 8 (highest child)
	}

	[Fact]
	public void GetOrderedNodes_Cached_ReturnsSameInstance()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));

		var ordered1 = tree.GetOrderedNodes();
		var ordered2 = tree.GetOrderedNodes();

		Assert.Same(ordered1, ordered2);
	}

	[Fact]
	public void GetOrderedNodes_CacheInvalidatedOnMutation()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("B", null, 2));

		var ordered1 = tree.GetOrderedNodes();
		// Add node that sorts BEFORE existing (lower Order in ascending) to invalidate cache
		tree.Add(new TestNode("A", null, 1));
		var ordered2 = tree.GetOrderedNodes();

		Assert.NotSame(ordered1, ordered2);
	}

	#endregion

	#region Version Tests

	[Fact]
	public void Version_IncrementOnAdd()
	{
		var tree = CreateTree();
		var v0 = tree.Version;

		tree.Add(new TestNode("A", null, 1));

		Assert.Equal(v0 + 1, tree.Version);
	}

	[Fact]
	public void Version_IncrementOnRemove()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		var v1 = tree.Version;

		tree.Remove("A");

		Assert.Equal(v1 + 1, tree.Version);
	}

	[Fact]
	public void Version_IncrementOnClear()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		var v1 = tree.Version;

		tree.Clear();

		Assert.Equal(v1 + 1, tree.Version);
	}

	[Fact]
	public void Version_NoIncrementOnEmptyClear()
	{
		var tree = CreateTree();
		var v0 = tree.Version;

		tree.Clear();

		Assert.Equal(v0, tree.Version);
	}

	#endregion

	#region Notification Tests

	[Fact]
	public void OnChangeGranular_FiresCorrectly()
	{
		var tree = CreateTree();
		var changes = new List<GraphTreeChange<string, TestNode>>();
		tree.OnChanged += change => changes.Add(change);

		tree.Add(new TestNode("A", null, 1));

		Assert.Single(changes);
		Assert.Equal(GraphTreeChangeType.Added, changes[0].ChangeType);
		Assert.Equal("A", changes[0].Key);
	}

	[Fact]
	public void OnChanged_FiresCorrectly()
	{
		var tree = CreateTree();
		var fireCount = 0;
		tree.OnChanged += _ => fireCount++;

		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));

		Assert.Equal(2, fireCount);
	}

	[Fact]
	public void OnChanged_FiresOncePerRemoveOperation()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));

		var fireCount = 0;
		tree.OnChanged += _ => fireCount++;

		tree.Remove("A"); // Removes A and B

		Assert.Equal(1, fireCount); // Only one OnChanged for the entire operation
	}

	[Fact]
	public void OnChangeGranular_VersionIncrementedBeforeNotification()
	{
		var tree = CreateTree();
		var capturedVersions = new List<int>();

		tree.OnChanged += change => capturedVersions.Add(tree.Version);

		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));

		// Version should be 1 when first notification fires, 2 when second fires
		Assert.Equal(2, capturedVersions.Count);
		Assert.Equal(1, capturedVersions[0]); // Version was 1 during first notification
		Assert.Equal(2, capturedVersions[1]); // Version was 2 during second notification
		Assert.Equal(2, tree.Version); // Final version matches
	}

	[Fact]
	public void OnChanged_Remove_NodesAlreadyRemovedWhenNotificationFires()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("C", "A", 3));

		var nodesDuringNotifications = new List<int>();
		tree.OnChanged += change =>
		{
			// During notification, the nodes should already be removed
			nodesDuringNotifications.Add(tree.Count);
		};

		tree.Remove("A"); // Removes A, B, and C

		// Single BatchComplete notification fired AFTER all 3 nodes removed
		Assert.Single(nodesDuringNotifications);
		Assert.Equal(0, nodesDuringNotifications[0]);
	}

	#endregion

	#region Cycle Protection Tests

	[Fact]
	public void GetOrderedNodes_CycleProtection_DoesNotInfiniteLoop()
	{
		var tree = CreateTree();
		// Create a pure cycle: A -> B -> C -> A (no real root)
		tree.Add(new TestNode("A", "C", 1)); // A's parent is C
		tree.Add(new TestNode("B", "A", 2)); // B's parent is A
		tree.Add(new TestNode("C", "B", 3)); // C's parent is B

		// Pure cycles have no roots, so traversal returns empty
		// This is expected - real trees should not have cycles
		var ordered = tree.GetOrderedNodes();

		// No roots means no traversal - the DFS visited set protects against loops
		// but there's no entry point since all nodes have in-tree parents
		Assert.Empty(ordered);
	}

	[Fact]
	public void Remove_CycleProtection_DoesNotInfiniteLoop()
	{
		var tree = CreateTree();
		// Create a pure cycle: A -> B -> C -> A
		// This is an invalid tree structure, but Remove must not crash
		tree.Add(new TestNode("A", "C", 1));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("C", "B", 3));

		// Remove should handle cycle gracefully without StackOverflow
		var removed = tree.Remove("A");

		// The node exists, so removal should succeed
		// Cycle protection prevents infinite recursion in CollectDescendants
		Assert.True(removed);
	}

	#endregion

	#region Forward Reference Tests

	[Fact]
	public void Add_ChildBeforeParent_Works()
	{
		var tree = CreateTree();

		// Add child before parent
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("A", null, 1));

		Assert.Equal(2, tree.Count);
		var parent = tree.GetParent("B");
		Assert.NotNull(parent);
		Assert.Equal("A", parent.Id);
	}

	[Fact]
	public void Add_ChildBeforeParent_GetChildrenWorks()
	{
		var tree = CreateTree();

		// Add child before parent (forward reference)
		tree.Add(new TestNode("C", "A", 3));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("A", null, 1));

		// GetChildren should work correctly after parent is added
		var children = tree.GetChildren("A");
		Assert.Equal(2, children.Count);
		// Ascending order by Order value: B(2), C(3)
		Assert.Equal("B", children[0].Id);
		Assert.Equal("C", children[1].Id);
	}

	#endregion

	#region Tie-Breaking Tests (R2.5)

	[Fact]
	public void GetOrderedNodes_TieBreakingStability()
	{
		var tree = CreateTree();

		// Add nodes with same Order (primary sort key)
		tree.Add(new TestNode("B", null, 1));
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("C", null, 1));

		var ordered1 = tree.GetOrderedNodes();

		// Force cache invalidation by adding and removing node with different Order
		tree.Add(new TestNode("D", null, 0)); // Order 0, sorts before Order 1
		tree.Remove("D");

		var ordered2 = tree.GetOrderedNodes();

		// Order should be deterministic despite same primary key
		// Due to tie-breaker: lower Id comes first (A < B < C)
		Assert.Equal(ordered1[0].Id, ordered2[0].Id);
		Assert.Equal(ordered1[1].Id, ordered2[1].Id);
		Assert.Equal(ordered1[2].Id, ordered2[2].Id);

		// Verify tie-breaker produces stable order: A, B, C (alphabetical)
		Assert.Equal("A", ordered1[0].Id);
		Assert.Equal("B", ordered1[1].Id);
		Assert.Equal("C", ordered1[2].Id);
	}

	#endregion

	#region Scale Tests (P0 Stack Overflow Prevention)

	[Fact]
	public void GetOrderedNodes_DeepChain_DoesNotStackOverflow()
	{
		var tree = CreateTree();

		// Build 15,000-node linear chain (depth = count)
		for (int i = 0; i < 15000; i++)
		{
			var parentId = i == 0 ? null : $"node-{i - 1}";
			tree.Add(new TestNode($"node-{i}", parentId, i));
		}

		var ordered = tree.GetOrderedNodes();
		Assert.Equal(15000, ordered.Count);
		Assert.Equal("node-0", ordered[0].Id);
		Assert.Equal("node-14999", ordered[14999].Id);
	}

	[Fact]
	public void Remove_DeepChain_DoesNotStackOverflow()
	{
		var tree = CreateTree();

		for (int i = 0; i < 15000; i++)
		{
			var parentId = i == 0 ? null : $"node-{i - 1}";
			tree.Add(new TestNode($"node-{i}", parentId, i));
		}

		// Remove root should cascade delete all 15k nodes
		var removed = tree.Remove("node-0");
		Assert.True(removed);
		Assert.Equal(0, tree.Count);
	}

	[Fact]
	public void Remove_WideTree_DoesNotExhibitQuadraticBehavior()
	{
		var tree = CreateTree();

		// Build 15k-node wide tree (all children of root)
		tree.Add(new TestNode("root", null, 0));
		for (int i = 1; i < 15000; i++)
			tree.Add(new TestNode($"node-{i}", "root", i));

		// Removing root should be O(N), not O(N^2)
		var sw = System.Diagnostics.Stopwatch.StartNew();
		tree.Remove("root");
		sw.Stop();

		Assert.Equal(0, tree.Count);
		// O(N) should complete in <500ms, O(N^2) would be >10s
		Assert.True(sw.ElapsedMilliseconds < 2000, $"Removal took {sw.ElapsedMilliseconds}ms - possible O(N^2)");
	}

	#endregion

	#region Incremental Cache Tests (P1)

	[Fact]
	public void Add_ForwardReference_DemotesChildFromRoots()
	{
		var tree = CreateTree();

		// Add child first (forward reference - parent doesn't exist yet)
		tree.Add(new TestNode("child", "parent", 2));

		// Child should be root (parent doesn't exist)
		var roots1 = tree.GetOrderedNodes();
		Assert.Single(roots1);
		Assert.Equal("child", roots1[0].Id);

		// Add parent
		tree.Add(new TestNode("parent", null, 1));

		// Now parent should be root, child demoted
		var roots2 = tree.GetOrderedNodes();
		Assert.Equal(2, roots2.Count);
		Assert.Equal("parent", roots2[0].Id);
		Assert.Equal("child", roots2[1].Id);
	}

	[Fact]
	public void Add_ParentChange_UpdatesChildrenMaps()
	{
		var tree = CreateTree();

		tree.Add(new TestNode("parent1", null, 1));
		tree.Add(new TestNode("parent2", null, 2));
		tree.Add(new TestNode("child", "parent1", 3));

		Assert.Single(tree.GetChildren("parent1"));
		Assert.Empty(tree.GetChildren("parent2"));

		// Re-add child with different parent
		tree.Add(new TestNode("child", "parent2", 3));

		Assert.Empty(tree.GetChildren("parent1"));
		Assert.Single(tree.GetChildren("parent2"));
	}

	[Fact]
	public void IncrementalState_RemainsConsistentAfterComplexOperations()
	{
		var tree = CreateTree();

		// Build tree with various operations (binary tree-like structure)
		for (int i = 0; i < 100; i++)
			tree.Add(new TestNode($"node-{i}", i == 0 ? null : $"node-{(i - 1) / 2}", i));

		// Perform updates and removals
		tree.Add(new TestNode("node-50", "node-10", 50)); // Re-parent

		// Verify re-parent worked
		var children10 = tree.GetChildren("node-10");
		Assert.Contains(children10, c => c.Id == "node-50");

		// node-50's old parent (node-24) should no longer have node-50
		var children24 = tree.GetChildren("node-24");
		Assert.DoesNotContain(children24, c => c.Id == "node-50");

		tree.Remove("node-25"); // Remove subtree

		// Verify removal - node-25 and its descendants should be gone
		Assert.Null(tree.GetNode("node-25"));
		Assert.Null(tree.GetNode("node-51")); // Child of node-25
		Assert.Null(tree.GetNode("node-52")); // Child of node-25

		// Tree should still be traversable
		var ordered = tree.GetOrderedNodes();
		Assert.DoesNotContain(ordered, n => n.Id == "node-25");
		Assert.DoesNotContain(ordered, n => n.Id == "node-51");
	}

	#endregion

	#region ParentKey Notification Tests (REQ-3)

	[Fact]
	public void GraphTreeChange_Add_ContainsParentKey()
	{
		var tree = CreateTree();
		GraphTreeChange<string, TestNode>? capturedChange = null;
		tree.OnChanged += change => capturedChange = change;

		tree.Add(new TestNode("parent", null, 1));
		tree.Add(new TestNode("child", "parent", 2));

		Assert.NotNull(capturedChange);
		Assert.Equal("child", capturedChange.Key);
		Assert.Equal("parent", capturedChange.ParentKey);
	}

	[Fact]
	public void GraphTreeChange_AddRoot_ParentKeyIsNull()
	{
		var tree = CreateTree();
		GraphTreeChange<string, TestNode>? capturedChange = null;
		tree.OnChanged += change => capturedChange = change;

		tree.Add(new TestNode("root", null, 1));

		Assert.NotNull(capturedChange);
		Assert.Equal("root", capturedChange.Key);
		Assert.Null(capturedChange.ParentKey);
	}

	[Fact]
	public void GraphTreeChange_Remove_ContainsPreCapturedParentKey()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("parent", null, 1));
		tree.Add(new TestNode("child", "parent", 2));

		var removedChanges = new List<GraphTreeChange<string, TestNode>>();
		tree.OnChanged += change =>
		{
			// Cascade removes fire BatchComplete containing individual Removed changes
			if (change.ChangeType == GraphTreeChangeType.BatchComplete && change.BatchChanges != null)
			{
				foreach (var c in change.BatchChanges)
					if (c.ChangeType == GraphTreeChangeType.Removed)
						removedChanges.Add(c);
			}
			else if (change.ChangeType == GraphTreeChangeType.Removed)
			{
				removedChanges.Add(change);
			}
		};

		tree.Remove("parent");

		// Both parent and child should be removed
		Assert.Equal(2, removedChanges.Count);

		// Parent's ParentKey should be null (it was a root)
		var parentChange = removedChanges.First(c => c.Key == "parent");
		Assert.Null(parentChange.ParentKey);

		// Child's ParentKey should be "parent" (captured BEFORE deletion)
		var childChange = removedChanges.First(c => c.Key == "child");
		Assert.Equal("parent", childChange.ParentKey);
	}

	#endregion

	#region AddRange and Stable-Append Tests

	[Fact]
	public void AddRange_FiresSingleOnChangedNotification()
	{
		var tree = CreateTree();
		var onChangedCount = 0;
		GraphTreeChange<string, TestNode>? lastChange = null;
		tree.OnChanged += change =>
		{
			onChangedCount++;
			lastChange = change;
		};

		var nodes = Enumerable.Range(0, 100)
			.Select(i => new TestNode($"node-{i}", null, i))
			.ToList();

		var added = tree.AddRange(nodes);

		Assert.Equal(100, added);
		Assert.Equal(1, onChangedCount); // Single notification for entire batch
		Assert.NotNull(lastChange);
		Assert.Equal(GraphTreeChangeType.BatchComplete, lastChange.ChangeType);
		Assert.NotNull(lastChange.BatchChanges);
		Assert.Equal(100, lastChange.BatchChanges.Count); // All changes in batch
	}

	[Fact]
	public void AddRange_VersionIncrementedOnce()
	{
		var tree = CreateTree();
		var initialVersion = tree.Version;

		var nodes = Enumerable.Range(0, 50)
			.Select(i => new TestNode($"node-{i}", null, i))
			.ToList();

		tree.AddRange(nodes);

		Assert.Equal(initialVersion + 1, tree.Version);
	}

	[Fact]
	public void StableAppend_SequentialRootAdditionsUseCacheAppend()
	{
		var tree = CreateTree();

		// Prime the cache with lowest Order (oldest first in ascending order)
		tree.Add(new TestNode("node-0", null, 0)); // Sorts first (lowest value, ascending order)
		var ordered1 = tree.GetOrderedNodes(); // Build cache
		Assert.Single(ordered1);

		// Add nodes with higher Order (newer = higher value) - should use stable-append
		// In ascending sort, higher values come AFTER lower values
		// So node-1 with Order=1 should sort AFTER node-0 (since 1 > 0)
		tree.Add(new TestNode("node-1", null, 1));

		var ordered2 = tree.GetOrderedNodes();
		Assert.Equal(2, ordered2.Count);
		Assert.Equal("node-0", ordered2[0].Id); // Lower value first (ascending)
		Assert.Equal("node-1", ordered2[1].Id); // Higher value last
	}

	[Fact]
	public void StableAppend_OutOfOrderAdditionInvalidatesCache()
	{
		var tree = CreateTree();

		// Add first node and prime cache
		tree.Add(new TestNode("node-1", null, 1));
		var ordered1 = tree.GetOrderedNodes();

		// Add node that sorts BEFORE existing (lower value in ascending order)
		// This should NOT use stable-append, must invalidate cache
		tree.Add(new TestNode("node-0", null, 0));

		var ordered2 = tree.GetOrderedNodes();
		Assert.Equal(2, ordered2.Count);
		Assert.Equal("node-0", ordered2[0].Id); // Lower value first (ascending)
		Assert.Equal("node-1", ordered2[1].Id); // Higher value last
	}

	[Fact]
	public void StableAppend_ForwardReferenceResolutionInvalidatesCache()
	{
		var tree = CreateTree();

		// Add child with missing parent (forward reference)
		tree.Add(new TestNode("child", "parent", 2));
		var ordered1 = tree.GetOrderedNodes(); // Cache built with child as temporary root

		// Add parent - this resolves forward reference and must invalidate cache
		tree.Add(new TestNode("parent", null, 1));

		var ordered2 = tree.GetOrderedNodes();
		Assert.Equal(2, ordered2.Count);
		Assert.Equal("parent", ordered2[0].Id); // Parent is visited first in DFS
		Assert.Equal("child", ordered2[1].Id); // Child follows parent
	}

	[Fact]
	public void AddRange_WithAscendingOrder_UsesStableAppend()
	{
		var tree = CreateTree();

		// Prime the cache
		tree.GetOrderedNodes();

		// Add nodes in ascending order (should all use stable-append)
		// In ascending sort: 0 < 1 < 2 < ... < 99, so each new node sorts AFTER previous
		var nodes = Enumerable.Range(0, 100)
			.Select(i => new TestNode($"node-{i}", null, i)) // Positive = higher values = sort later
			.ToList();

		var added = tree.AddRange(nodes);

		Assert.Equal(100, added);

		var ordered = tree.GetOrderedNodes();
		Assert.Equal(100, ordered.Count);
		// First added sorts first (lowest value = 0)
		Assert.Equal("node-0", ordered[0].Id);
		// Last added sorts last (highest value = 99)
		Assert.Equal("node-99", ordered[99].Id);
	}

	[Fact]
	public void AddRange_WithMixedOrder_FallsBackToRebuild()
	{
		var tree = CreateTree();

		// Add some initial nodes and prime cache
		tree.Add(new TestNode("existing", null, 50));
		tree.GetOrderedNodes();

		// Add nodes that include one that sorts BEFORE existing (ascending: lower values first)
		var nodes = new[]
		{
			new TestNode("before", null, 25),  // Sorts before existing (25 < 50)
			new TestNode("after1", null, 75),  // Sorts after existing (75 > 50)
			new TestNode("after2", null, 100), // Sorts after existing (100 > 50)
		};

		tree.AddRange(nodes);

		var ordered = tree.GetOrderedNodes();
		Assert.Equal(4, ordered.Count);
		Assert.Equal("before", ordered[0].Id);   // 25 (lowest)
		Assert.Equal("existing", ordered[1].Id); // 50
		Assert.Equal("after1", ordered[2].Id);   // 75
		Assert.Equal("after2", ordered[3].Id);   // 100 (highest)
	}

	#endregion
}
