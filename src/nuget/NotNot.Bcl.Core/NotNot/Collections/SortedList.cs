using System;
using System.Collections;
using System.Collections.Generic;

namespace NotNot.Collections;

/// <summary>
/// Represents a lazily-sorted list that defers sorting until read operations.
/// <para>Optimized for adding items in ascending order and retrieving the last item via <see cref="TryTakeLast"/>.</para>
/// <para>NOTE: Dirty detection assumes ascending order. For descending or custom-ordered comparers, the list may sort more frequently than necessary.</para>
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class SortedList<T> : IEnumerable<T>
{
	private readonly List<T> _list;
	private readonly IComparer<T> _comparer;
	private bool _isDirty = false;

	/// <summary>
	/// Initializes a new instance of the <see cref="SortedList{T}"/> class that uses the default comparer for the type <typeparamref name="T"/>.
	/// </summary>
	public SortedList() : this(Comparer<T>.Default) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="SortedList{T}"/> class that uses the specified comparer.
	/// </summary>
	/// <param name="comparer">The <see cref="IComparer{T}"/> implementation to use when comparing elements.</param>
	public SortedList(IComparer<T> comparer)
	{
		_comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
		_list = new List<T>();
	}

	/// <summary>
	/// Gets the number of elements in the list.
	/// </summary>
	public int Count => _list.Count;

	/// <summary>
	/// Adds an element to the list.
	/// <para>Items are added to the end of the backing storage. If the item is not in sorted order, the list is marked dirty and will be sorted on the next read operation.</para>
	/// <para>NOTE: Dirty detection assumes ascending order (new item >= last item). Custom descending comparers will trigger more frequent sorting.</para>
	/// </summary>
	/// <param name="item">The element to add to the list.</param>
	public void Add(T item)
	{
		if (_list.Count > 0)
		{
			var lastItem = _list[_list.Count - 1];
			// NOTE: This assumes ascending order - for descending comparers, this will always mark dirty
			// If new item is not greater than or equal to last item (ascending order), mark dirty
			if (_comparer.Compare(item, lastItem) < 0)
			{
				_isDirty = true;
			}
		}
		_list.Add(item);
	}

	/// <summary>
	/// Attempts to remove and return the last element from the sorted list.
	/// <para>This is the primary and most efficient way to retrieve values from the list.</para>
	/// </summary>
	/// <param name="value">When this method returns, contains the last element if the list is not empty; otherwise, the default value for the type.</param>
	/// <returns>true if an element was successfully removed and returned; false if the list is empty.</returns>
	public bool TryTakeLast(out T value)
	{
		EnsureSorted();

		if (_list.Count > 0)
		{
			var lastIndex = _list.Count - 1;
			value = _list[lastIndex];
			_list.RemoveAt(lastIndex);
			return true;
		}

		value = default!;
		return false;
	}

	/// <summary>
	/// Removes all elements from the list.
	/// </summary>
	public void Clear()
	{
		_list.Clear();
		_isDirty = false;
	}

	/// <summary>
	/// Determines whether the list contains a specific element using the comparer.
	/// <para>This operation triggers sorting if the list is dirty.</para>
	/// </summary>
	/// <param name="item">The element to locate in the list.</param>
	/// <returns>true if the list contains the specified element; otherwise, false.</returns>
	public bool Contains(T item)
	{
		EnsureSorted();
		// Use BinarySearch with comparer for correct behavior with custom comparers
		return _list.BinarySearch(item, _comparer) >= 0;
	}

	/// <summary>
	/// Removes the first occurrence of a specific element from the list using the comparer.
	/// <para>This operation triggers sorting if the list is dirty.</para>
	/// </summary>
	/// <param name="item">The element to remove.</param>
	/// <returns>true if the element is successfully found and removed; otherwise, false.</returns>
	public bool Remove(T item)
	{
		EnsureSorted();
		var index = _list.BinarySearch(item, _comparer);
		if (index >= 0)
		{
			_list.RemoveAt(index);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Returns an enumerator that iterates through the list in sorted order (ascending).
	/// <para>This operation triggers sorting if the list is dirty.</para>
	/// </summary>
	/// <returns>An enumerator that can be used to iterate through the list.</returns>
	public IEnumerator<T> GetEnumerator()
	{
		EnsureSorted();
		return _list.GetEnumerator();
	}

	/// <summary>
	/// Returns an enumerator that iterates through the list.
	/// </summary>
	/// <returns>An <see cref="IEnumerator"/> object that can be used to iterate through the list.</returns>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Enumerates the list in reverse sorted order (descending, from end to beginning).
	/// <para>This operation triggers sorting if the list is dirty.</para>
	/// <para>Safe for removal: You can safely remove the current item or any items at higher indices during enumeration.</para>
	/// </summary>
	/// <returns>An enumerable that iterates through the list in reverse order.</returns>
	public IEnumerable<T> ReverseEnumerate()
	{
		EnsureSorted();
		for (int i = _list.Count - 1; i >= 0; i--)
		{
			yield return _list[i];
		}
	}

	private void EnsureSorted()
	{
		if (_isDirty)
		{
			_list.Sort(_comparer);
			_isDirty = false;
		}
	}
}
