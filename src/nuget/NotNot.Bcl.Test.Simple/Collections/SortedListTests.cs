using Xunit;
using NotNot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NotNot.Bcl.Test.Simple.Collections;

/// <summary>
/// Tests for SortedList{T} lazy-sorting collection.
/// Covers construction, lazy sorting, custom comparers, and edge cases.
/// </summary>
public class SortedListTests
{
	#region Constructor Tests

	[Fact]
	public void Constructor_Default_InitializesEmpty()
	{
		// Arrange & Act
		var list = new SortedList<int>();

		// Assert
		Assert.Equal(0, list.Count);
	}

	[Fact]
	public void Constructor_WithComparer_UsesProvidedComparer()
	{
		// Arrange
		var comparer = Comparer<int>.Default;

		// Act
		var list = new SortedList<int>(comparer);

		// Assert
		Assert.Equal(0, list.Count);
	}

	[Fact]
	public void Constructor_WithNullComparer_ThrowsArgumentNullException()
	{
		// Arrange & Act & Assert
		Assert.Throws<ArgumentNullException>(() => new SortedList<int>(null!));
	}

	#endregion

	#region Add and TryTakeLast Tests

	[Fact]
	public void Add_SingleItem_IncreasesCount()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		list.Add(42);

		// Assert
		Assert.Equal(1, list.Count);
	}

	[Fact]
	public void Add_MultipleItemsInOrder_DoesNotMarkDirty()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		list.Add(1);
		list.Add(2);
		list.Add(3);

		// Assert - items should be retrievable in order without sorting
		Assert.True(list.TryTakeLast(out var last));
		Assert.Equal(3, last);
		Assert.True(list.TryTakeLast(out last));
		Assert.Equal(2, last);
		Assert.True(list.TryTakeLast(out last));
		Assert.Equal(1, last);
	}

	[Fact]
	public void Add_OutOfOrderItems_MarksDirtyAndSortsOnRead()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act - add out of order
		list.Add(3);
		list.Add(1);
		list.Add(2);

		// Assert - should be sorted when retrieved
		Assert.True(list.TryTakeLast(out var last));
		Assert.Equal(3, last);
		Assert.True(list.TryTakeLast(out last));
		Assert.Equal(2, last);
		Assert.True(list.TryTakeLast(out last));
		Assert.Equal(1, last);
	}

	[Fact]
	public void TryTakeLast_EmptyList_ReturnsFalse()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		bool result = list.TryTakeLast(out var value);

		// Assert
		Assert.False(result);
		Assert.Equal(default, value);
	}

	[Fact]
	public void TryTakeLast_WithItems_ReturnsLastAndRemoves()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(10);
		list.Add(20);
		list.Add(30);

		// Act
		bool result = list.TryTakeLast(out var value);

		// Assert
		Assert.True(result);
		Assert.Equal(30, value);
		Assert.Equal(2, list.Count);
	}

	[Fact]
	public void TryTakeLast_RemovesItemsInDescendingOrder()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(5);
		list.Add(3);
		list.Add(7);
		list.Add(1);

		// Act & Assert - should come out in descending order
		Assert.True(list.TryTakeLast(out var val));
		Assert.Equal(7, val);
		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(5, val);
		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(3, val);
		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(1, val);
		Assert.False(list.TryTakeLast(out val));
	}

	#endregion

	#region Clear Tests

	[Fact]
	public void Clear_EmptyList_RemainsEmpty()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		list.Clear();

		// Assert
		Assert.Equal(0, list.Count);
	}

	[Fact]
	public void Clear_WithItems_RemovesAllItems()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(1);
		list.Add(2);
		list.Add(3);

		// Act
		list.Clear();

		// Assert
		Assert.Equal(0, list.Count);
		Assert.False(list.TryTakeLast(out _));
	}

	#endregion

	#region Contains Tests

	[Fact]
	public void Contains_ItemExists_ReturnsTrue()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(10);
		list.Add(20);
		list.Add(30);

		// Act & Assert
		Assert.True(list.Contains(20));
	}

	[Fact]
	public void Contains_ItemDoesNotExist_ReturnsFalse()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(10);
		list.Add(20);

		// Act & Assert
		Assert.False(list.Contains(15));
	}

	[Fact]
	public void Contains_EmptyList_ReturnsFalse()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act & Assert
		Assert.False(list.Contains(42));
	}

	[Fact]
	public void Contains_WithCustomComparer_UsesComparer()
	{
		// Arrange - case-insensitive string comparer
		var list = new SortedList<string>(StringComparer.OrdinalIgnoreCase);
		list.Add("apple");
		list.Add("banana");
		list.Add("cherry");

		// Act & Assert - should find with different case
		Assert.True(list.Contains("BANANA"));
		Assert.True(list.Contains("Apple"));
		Assert.False(list.Contains("grape"));
	}

	#endregion

	#region Remove Tests

	[Fact]
	public void Remove_ExistingItem_ReturnsTrue()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(10);
		list.Add(20);
		list.Add(30);

		// Act
		bool result = list.Remove(20);

		// Assert
		Assert.True(result);
		Assert.Equal(2, list.Count);
		Assert.False(list.Contains(20));
	}

	[Fact]
	public void Remove_NonExistingItem_ReturnsFalse()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(10);
		list.Add(20);

		// Act
		bool result = list.Remove(15);

		// Assert
		Assert.False(result);
		Assert.Equal(2, list.Count);
	}

	[Fact]
	public void Remove_WithCustomComparer_UsesComparer()
	{
		// Arrange - case-insensitive string comparer
		var list = new SortedList<string>(StringComparer.OrdinalIgnoreCase);
		list.Add("apple");
		list.Add("banana");
		list.Add("cherry");

		// Act - remove with different case
		bool result = list.Remove("BANANA");

		// Assert
		Assert.True(result);
		Assert.Equal(2, list.Count);
		Assert.False(list.Contains("banana"));
	}

	[Fact]
	public void Remove_FromEmptyList_ReturnsFalse()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		bool result = list.Remove(42);

		// Assert
		Assert.False(result);
	}

	#endregion

	#region Enumeration Tests

	[Fact]
	public void GetEnumerator_EmptyList_ReturnsNoItems()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		var items = list.ToList();

		// Assert
		Assert.Empty(items);
	}

	[Fact]
	public void GetEnumerator_WithItems_ReturnsSortedOrder()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(30);
		list.Add(10);
		list.Add(20);

		// Act
		var items = list.ToList();

		// Assert
		Assert.Equal(3, items.Count);
		Assert.Equal(10, items[0]);
		Assert.Equal(20, items[1]);
		Assert.Equal(30, items[2]);
	}

	[Fact]
	public void GetEnumerator_TriggersSort_OnDirtyList()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(5);
		list.Add(1);
		list.Add(9);
		list.Add(3);

		// Act - enumeration should trigger sort
		var items = list.ToList();

		// Assert
		Assert.Equal(new[] { 1, 3, 5, 9 }, items);
	}

	[Fact]
	public void GetEnumerator_WithDescendingComparer_ReturnsSortedDescending()
	{
		// Arrange
		var list = new SortedList<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
		list.Add(10);
		list.Add(30);
		list.Add(20);

		// Act
		var items = list.ToList();

		// Assert - should be in descending order
		Assert.Equal(new[] { 30, 20, 10 }, items);
	}

	#endregion

	#region Custom Comparer Tests

	[Fact]
	public void CustomComparer_DescendingIntegers_SortsCorrectly()
	{
		// Arrange
		var descendingComparer = Comparer<int>.Create((a, b) => b.CompareTo(a));
		var list = new SortedList<int>(descendingComparer);

		// Act
		list.Add(1);
		list.Add(5);
		list.Add(3);

		// Assert - TryTakeLast should return smallest (last in descending order)
		Assert.True(list.TryTakeLast(out var val));
		Assert.Equal(1, val);
		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(3, val);
		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(5, val);
	}

	[Fact]
	public void CustomComparer_CaseInsensitiveStrings_SortsCorrectly()
	{
		// Arrange
		var list = new SortedList<string>(StringComparer.OrdinalIgnoreCase);

		// Act
		list.Add("Zebra");
		list.Add("apple");
		list.Add("BANANA");

		// Assert
		var items = list.ToList();
		Assert.Equal("apple", items[0]);
		Assert.Equal("BANANA", items[1]);
		Assert.Equal("Zebra", items[2]);
	}

	#endregion

	#region Lazy Sorting Behavior Tests

	[Fact]
	public void LazySorting_AddInOrder_NeverSorts()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act - add in perfect ascending order
		for (int i = 0; i < 100; i++)
		{
			list.Add(i);
		}

		// Assert - should be able to take last efficiently without sorting
		Assert.True(list.TryTakeLast(out var val));
		Assert.Equal(99, val);
	}

	[Fact]
	public void LazySorting_AddOutOfOrder_SortsOnFirstRead()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(5);
		list.Add(3);
		list.Add(7);

		// Act - first read operation should trigger sort
		bool hasItem = list.Contains(5);

		// Assert
		Assert.True(hasItem);
		// After sort, should be able to enumerate in order
		Assert.Equal(new[] { 3, 5, 7 }, list.ToList());
	}

	[Fact]
	public void LazySorting_MixedAdditions_SortsCorrectly()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act - add in order, then out of order
		list.Add(1);
		list.Add(2);
		list.Add(3);
		list.Add(10); // out of order for what comes next
		list.Add(5);
		list.Add(7);

		// Assert
		var items = list.ToList();
		Assert.Equal(new[] { 1, 2, 3, 5, 7, 10 }, items);
	}

	#endregion

	#region Edge Cases

	[Fact]
	public void SingleItem_AllOperations_WorkCorrectly()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(42);

		// Act & Assert
		Assert.Equal(1, list.Count);
		Assert.True(list.Contains(42));
		var items = list.ToList();
		Assert.Single(items);
		Assert.Equal(42, items[0]);
	}

	[Fact]
	public void DuplicateItems_Allowed()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act
		list.Add(5);
		list.Add(5);
		list.Add(5);

		// Assert
		Assert.Equal(3, list.Count);
		var items = list.ToList();
		Assert.Equal(new[] { 5, 5, 5 }, items);
	}

	[Fact]
	public void LargeDataSet_WorksCorrectly()
	{
		// Arrange
		var list = new SortedList<int>();
		var random = new Random(42); // Fixed seed for reproducibility
		var values = Enumerable.Range(0, 1000).Select(_ => random.Next(10000)).ToList();

		// Act
		foreach (var val in values)
		{
			list.Add(val);
		}

		// Assert
		var sorted = list.ToList();
		Assert.Equal(1000, sorted.Count);
		// Verify actually sorted
		for (int i = 1; i < sorted.Count; i++)
		{
			Assert.True(sorted[i] >= sorted[i - 1], $"Item at index {i} is not sorted correctly");
		}
	}

	#endregion

	#region String Type Tests

	[Fact]
	public void SortedList_WithStrings_WorksCorrectly()
	{
		// Arrange
		var list = new SortedList<string>();

		// Act
		list.Add("zebra");
		list.Add("apple");
		list.Add("mango");
		list.Add("banana");

		// Assert
		var items = list.ToList();
		Assert.Equal(new[] { "apple", "banana", "mango", "zebra" }, items);
	}

	[Fact]
	public void SortedList_WithNullStrings_HandlesGracefully()
	{
		// Arrange
		var list = new SortedList<string?>();

		// Act
		list.Add("apple");
		list.Add(null);
		list.Add("banana");

		// Assert - null should sort first
		var items = list.ToList();
		Assert.Equal(3, items.Count);
		Assert.Null(items[0]);
		Assert.Equal("apple", items[1]);
		Assert.Equal("banana", items[2]);
	}

	#endregion

	#region Integration Tests

	[Fact]
	public void ComplexScenario_AddRemoveEnumerate_WorksCorrectly()
	{
		// Arrange
		var list = new SortedList<int>();

		// Act - complex sequence of operations
		list.Add(10);
		list.Add(5);
		list.Add(15);
		list.Remove(10);
		list.Add(3);
		list.Add(20);

		// Assert
		Assert.Equal(4, list.Count);
		var items = list.ToList();
		Assert.Equal(new[] { 3, 5, 15, 20 }, items);
	}

	[Fact]
	public void ComplexScenario_TakeLastMultipleTimes_WorksCorrectly()
	{
		// Arrange
		var list = new SortedList<int>();
		list.Add(50);
		list.Add(10);
		list.Add(30);
		list.Add(20);
		list.Add(40);

		// Act & Assert - take last 3 times
		Assert.True(list.TryTakeLast(out var val));
		Assert.Equal(50, val);
		Assert.Equal(4, list.Count);

		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(40, val);
		Assert.Equal(3, list.Count);

		Assert.True(list.TryTakeLast(out val));
		Assert.Equal(30, val);
		Assert.Equal(2, list.Count);

		// Remaining should be 10, 20
		var remaining = list.ToList();
		Assert.Equal(new[] { 10, 20 }, remaining);
	}

	#endregion
}
