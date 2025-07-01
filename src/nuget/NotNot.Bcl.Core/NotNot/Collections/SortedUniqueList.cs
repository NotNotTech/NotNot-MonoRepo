using System;
using System.Collections;
using System.Collections.Generic;

namespace NotNot.Collections;
/// <summary>
/// Represents a collection of sorted, unique elements.
///  <para>During enumeration This collection allows for safe removal of the current or already enumerated elements.</para>
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public class SortedUniqueList<T> : IEnumerable<T>
{
	private readonly HashSet<T> _set;
	private readonly List<T> _list;
	private readonly IComparer<T> _comparer;
	private bool _isDirty = false;

	/// <summary>
	/// Initializes a new instance of the <see cref="SortedUniqueList{T}"/> class that uses the default comparer for the type <typeparamref name="T"/>.
	/// </summary>
	public SortedUniqueList() : this(Comparer<T>.Default) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="SortedUniqueList{T}"/> class that uses the specified comparer.
	/// </summary>
	/// <param name="comparer">The <see cref="IComparer{T}"/> implementation to use when comparing elements.</param>
	public SortedUniqueList(IComparer<T> comparer)
	{
		_comparer = new ReverseComparer<T>(comparer);
		_set = new HashSet<T>();
		_list = new List<T>();
	}

	/// <summary>
	/// Gets the number of elements in the set.
	/// </summary>
	public int Count => _set.Count;

	/// <summary>
	/// Adds an element to the set.
	/// </summary>
	/// <param name="item">The element to add to the set.</param>
	/// <returns>true if the element is added to the set; false if the element is already present.</returns>
	public bool Add(T item)
	{
		if (_set.Add(item))
		{
			_list.Add(item);
			_isDirty = true;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Removes all elements from the set.
	/// </summary>
	public void Clear()
	{
		_set.Clear();
		_list.Clear();
		_isDirty = false;
	}

	/// <summary>
	/// Determines whether the set contains a specific element.
	/// </summary>
	/// <param name="item">The element to locate in the set.</param>
	/// <returns>true if the set contains the specified element; otherwise, false.</returns>
	public bool Contains(T item) => _set.Contains(item);

	/// <summary>
	/// Removes the specified element from the set.
	/// </summary>
	/// <param name="item">The element to remove.</param>
	/// <returns>true if the element is successfully found and removed; otherwise, false.</returns>
	public bool Remove(T item)
	{
		if (_set.Remove(item))
		{
			_list.Remove(item);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Returns an enumerator that iterates through the set.
	/// <para>the current or already enumerated items can be removed without problem</para>
	/// </summary>
	/// <returns>An enumerator that can be used to iterate through the set.</returns>
	public IEnumerator<T> GetEnumerator()
	{
		EnsureSorted();
		for (int i = _list.Count - 1; i >= 0; i--)
		{
			yield return _list[i];
		}
	}

	/// <summary>
	/// Returns an enumerator that iterates through the set.
	/// </summary>
	/// <returns>An <see cref="IEnumerator"/> object that can be used to iterate through the set.</returns>
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	private void EnsureSorted()
	{
		if (_isDirty)
		{
			_list.Sort(_comparer);
			_isDirty = false;
		}
	}

	private class ReverseComparer<TItem> : IComparer<TItem>
	{
		private readonly IComparer<TItem> _originalComparer;

		public ReverseComparer(IComparer<TItem> originalComparer)
		{
			_originalComparer = originalComparer;
		}

		public int Compare(TItem x, TItem y)
		{
			return _originalComparer.Compare(y, x);
		}
	}
}
