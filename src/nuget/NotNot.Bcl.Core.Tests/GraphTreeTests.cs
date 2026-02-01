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
	/// CompareTo returns value such that when used with OrderByDescending:
	/// - Higher Order values come FIRST (newest-first display pattern)
	/// - When Order is equal, lower Id comes first (stable tie-breaker)
	///
	/// OrderByDescending behavior: if a.CompareTo(b) > 0, a comes BEFORE b.
	/// To get higher Order first: return positive when THIS.Order > other.Order.
	/// </remarks>
	private record TestNode(string Id, string? ParentId, int Order) : IComparable<TestNode>
	{
		public int CompareTo(TestNode? other)
		{
			if (other is null) return 1;

			// Primary sort: higher Order comes first when used with OrderByDescending
			// Return positive when this.Order > other.Order (so "this" comes first)
			var cmp = Order.CompareTo(other.Order);
			if (cmp != 0) return cmp;

			// Tie-breaker: lower Id comes first (stable ordering)
			// Return negative when this.Id < other.Id (so "this" comes first)
			return -string.Compare(Id, other.Id, StringComparison.Ordinal);
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
		tree.OnChangeGranular += change => capturedChange = change;

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
		tree.OnChangeGranular += change =>
		{
			if (change.ChangeType == GraphTreeChangeType.Removed)
				removedKeys.Add(change.Key);
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
		tree.OnChangeGranular += change =>
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
		// Descending order: C (3) before B (2)
		Assert.Equal("C", children[0].Id);
		Assert.Equal("B", children[1].Id);
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
	public void GetOrderedNodes_MultipleRoots_SortedDescending()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));
		tree.Add(new TestNode("C", null, 3));

		var ordered = tree.GetOrderedNodes();

		Assert.Equal(3, ordered.Count);
		// Higher Order values come first (descending by Order)
		Assert.Equal("C", ordered[0].Id); // Order 3
		Assert.Equal("B", ordered[1].Id); // Order 2
		Assert.Equal("A", ordered[2].Id); // Order 1
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

		// Root first (DFS pre-order), then children in descending order by Order
		Assert.Equal(4, ordered.Count);
		Assert.Equal("Root", ordered[0].Id);     // Order 10, parent
		Assert.Equal("Child2", ordered[1].Id);   // Order 8 (highest child)
		Assert.Equal("Child1", ordered[2].Id);   // Order 5
		Assert.Equal("Child3", ordered[3].Id);   // Order 3 (lowest child)
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
		tree.Add(new TestNode("A", null, 1));

		var ordered1 = tree.GetOrderedNodes();
		tree.Add(new TestNode("B", null, 2));
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
		tree.OnChangeGranular += change => changes.Add(change);

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
		tree.OnChanged += () => fireCount++;

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
		tree.OnChanged += () => fireCount++;

		tree.Remove("A"); // Removes A and B

		Assert.Equal(1, fireCount); // Only one OnChanged for the entire operation
	}

	[Fact]
	public void OnChangeGranular_VersionIncrementedBeforeNotification()
	{
		var tree = CreateTree();
		var capturedVersions = new List<int>();

		tree.OnChangeGranular += change => capturedVersions.Add(tree.Version);

		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", null, 2));

		// Version should be 1 when first notification fires, 2 when second fires
		Assert.Equal(2, capturedVersions.Count);
		Assert.Equal(1, capturedVersions[0]); // Version was 1 during first notification
		Assert.Equal(2, capturedVersions[1]); // Version was 2 during second notification
		Assert.Equal(2, tree.Version); // Final version matches
	}

	[Fact]
	public void OnChangeGranular_Remove_NodesAlreadyRemovedWhenNotificationFires()
	{
		var tree = CreateTree();
		tree.Add(new TestNode("A", null, 1));
		tree.Add(new TestNode("B", "A", 2));
		tree.Add(new TestNode("C", "A", 3));

		var nodesDuringNotifications = new List<int>();
		tree.OnChangeGranular += change =>
		{
			// During notification, the nodes should already be removed
			nodesDuringNotifications.Add(tree.Count);
		};

		tree.Remove("A"); // Removes A, B, and C

		// All 3 nodes should be removed BEFORE any notification fires
		Assert.Equal(3, nodesDuringNotifications.Count);
		Assert.All(nodesDuringNotifications, count => Assert.Equal(0, count));
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
		// Descending order by Order value: C(3), B(2)
		Assert.Equal("C", children[0].Id);
		Assert.Equal("B", children[1].Id);
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

		// Force cache invalidation
		tree.Add(new TestNode("D", null, 2));
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
}
