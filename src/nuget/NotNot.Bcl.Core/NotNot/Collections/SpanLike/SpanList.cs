// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NotNot;
using NotNot.Collections;
using NotNot.Collections.SpanLike;

namespace NotNot.Collections.SpanLike;

/// <summary>
/// A dynamic list backed by a caller-provided <see cref="Span{T}"/> buffer.
/// Supports adding, inserting, and removing elements at arbitrary positions.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
/// <remarks>
/// SpanList provides list semantics (Add, Insert, RemoveAt, indexer access) with zero heap allocation.
/// Elements are stored contiguously from index 0 to Count-1.
///
/// Key characteristics:
/// - Zero heap allocation (uses caller-provided buffer)
/// - Fixed capacity (determined by buffer size)
/// - Random access via indexer: O(1)
/// - Add at end: O(1)
/// - Insert/RemoveAt: O(n) due to element shifting
/// - Aggressive inlining for minimal overhead
/// - Safe variants (TryAdd, TryInsert) for defensive programming
///
/// Typical usage with stackalloc:
/// <code>
/// Span&lt;int&gt; buffer = stackalloc int[128];
/// var list = new SpanList&lt;int&gt;(buffer);
/// list.Add(42);
/// list.Add(100);
/// list.Insert(1, 50); // [42, 50, 100]
/// list.RemoveAt(0);   // [50, 100]
/// int value = list[0]; // 50
/// </code>
///
/// Limitations:
/// - ref struct constraints apply (cannot be boxed, stored in fields, used in async methods)
/// - Fixed capacity (no dynamic growth)
/// - Buffer lifetime must exceed SpanList usage (typically same stack frame)
/// - Insert/RemoveAt requires shifting elements (O(n) worst case)
///
/// IMPORTANT: SpanList is a value type (ref struct). Passing by value creates an independent copy
/// with its own _count field. Always pass by ref or use within a single scope to avoid state divergence.
/// <code>
/// void BadExample(SpanList&lt;int&gt; list) { list.Add(1); } // Copy, caller won't see add
/// void GoodExample(ref SpanList&lt;int&gt; list) { list.Add(1); } // By ref, caller sees add
/// </code>
/// </remarks>
public ref struct SpanList<T>
{
	private Span<T> _buffer;
	private int _count;

	/// <summary>
	/// Initializes a new SpanList backed by the specified buffer.
	/// </summary>
	/// <param name="buffer">The buffer to use for list storage. Caller must ensure buffer lifetime exceeds SpanList usage.</param>
	/// <remarks>
	/// The buffer capacity determines the maximum list size. Attempts to add beyond this capacity will throw.
	/// The buffer should typically be allocated via stackalloc for zero-allocation semantics.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public SpanList(Span<T> buffer)
	{
		_buffer = buffer;
		_count = 0;
	}

	/// <summary>
	/// Gets the number of elements currently in the list.
	/// </summary>
	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count;
	}

	/// <summary>
	/// Gets the maximum capacity of the list (the buffer size).
	/// </summary>
	public int Capacity
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _buffer.Length;
	}

	/// <summary>
	/// Gets a value indicating whether the list is empty.
	/// </summary>
	public bool IsEmpty
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count == 0;
	}

	/// <summary>
	/// Gets a value indicating whether the list is at full capacity.
	/// </summary>
	public bool IsFull
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count >= _buffer.Length;
	}

	/// <summary>
	/// Gets or sets the element at the specified index.
	/// </summary>
	/// <param name="index">The zero-based index of the element to get or set.</param>
	/// <returns>The element at the specified index.</returns>
	/// <exception cref="IndexOutOfRangeException">Thrown when index is negative or >= Count.</exception>
	/// <remarks>
	/// Random access is O(1).
	/// </remarks>
	public ref T this[int index]
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			__.ThrowIfNot(index >= 0 && index < _count, $"Index {index} out of range [0, {_count}).");
			return ref _buffer[index];
		}
	}

	/// <summary>
	/// Adds an item to the end of the list.
	/// </summary>
	/// <param name="item">The item to add.</param>
	/// <exception cref="InvalidOperationException">Thrown when the list is at full capacity (overflow).</exception>
	/// <remarks>
	/// This method throws on overflow. For defensive programming, use <see cref="TryAdd"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Add(T item)
	{
		if (_count >= _buffer.Length)
		{
			throw __.Throw($"SpanList overflow: count {_count} equals capacity {_buffer.Length}. Consider increasing buffer size.");
		}
		_buffer[_count++] = item;
	}

	/// <summary>
	/// Attempts to add an item to the end of the list.
	/// </summary>
	/// <param name="item">The item to add.</param>
	/// <returns>True if the item was added successfully; false if the list is at full capacity.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Add"/> that returns false instead of throwing on overflow.
	/// Use this when overflow is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryAdd(T item)
	{
		if (_count >= _buffer.Length)
		{
			return false;
		}
		_buffer[_count++] = item;
		return true;
	}

	/// <summary>
	/// Inserts an item at the specified index, shifting subsequent elements to the right.
	/// </summary>
	/// <param name="index">The zero-based index at which to insert the item. Must be in range [0, Count].</param>
	/// <param name="item">The item to insert.</param>
	/// <exception cref="IndexOutOfRangeException">Thrown when index is negative or > Count.</exception>
	/// <exception cref="InvalidOperationException">Thrown when the list is at full capacity (overflow).</exception>
	/// <remarks>
	/// This method throws on overflow or invalid index. For defensive programming, use <see cref="TryInsert"/> instead.
	/// Performance: O(n) due to shifting elements [index, Count-1] one position to the right.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Insert(int index, T item)
	{
		__.ThrowIfNot(index >= 0 && index <= _count, $"Index {index} out of range [0, {_count}].");
		if (_count >= _buffer.Length)
		{
			throw __.Throw($"SpanList overflow: count {_count} equals capacity {_buffer.Length}. Consider increasing buffer size.");
		}

		// Shift elements [index, Count-1] one position to the right
		if (index < _count)
		{
			_buffer.Slice(index, _count - index).CopyTo(_buffer.Slice(index + 1));
		}

		_buffer[index] = item;
		_count++;
	}

	/// <summary>
	/// Attempts to insert an item at the specified index.
	/// </summary>
	/// <param name="index">The zero-based index at which to insert the item. Must be in range [0, Count].</param>
	/// <param name="item">The item to insert.</param>
	/// <returns>True if the item was inserted successfully; false if the list is at full capacity or index is invalid.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Insert"/> that returns false instead of throwing.
	/// Use this when overflow or invalid index is a valid condition that should be handled gracefully.
	/// Performance: O(n) due to shifting elements.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryInsert(int index, T item)
	{
		if (index < 0 || index > _count || _count >= _buffer.Length)
		{
			return false;
		}

		// Shift elements [index, Count-1] one position to the right
		if (index < _count)
		{
			_buffer.Slice(index, _count - index).CopyTo(_buffer.Slice(index + 1));
		}

		_buffer[index] = item;
		_count++;
		return true;
	}

	/// <summary>
	/// Removes the item at the specified index, shifting subsequent elements to the left.
	/// </summary>
	/// <param name="index">The zero-based index of the item to remove. Must be in range [0, Count).</param>
	/// <exception cref="IndexOutOfRangeException">Thrown when index is negative or >= Count.</exception>
	/// <remarks>
	/// This method throws on invalid index. For defensive programming, use <see cref="TryRemoveAt"/> instead.
	/// Performance: O(n) due to shifting elements [index+1, Count-1] one position to the left.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RemoveAt(int index)
	{
		__.ThrowIfNot(index >= 0 && index < _count, $"Index {index} out of range [0, {_count}).");

		// Shift elements [index+1, Count-1] one position to the left
		if (index < _count - 1)
		{
			_buffer.Slice(index + 1, _count - index - 1).CopyTo(_buffer.Slice(index));
		}

		_count--;
	}

	/// <summary>
	/// Attempts to remove the item at the specified index.
	/// </summary>
	/// <param name="index">The zero-based index of the item to remove.</param>
	/// <returns>True if the item was removed successfully; false if index is invalid.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="RemoveAt"/> that returns false instead of throwing.
	/// Use this when invalid index is a valid condition that should be handled gracefully.
	/// Performance: O(n) due to shifting elements.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryRemoveAt(int index)
	{
		if (index < 0 || index >= _count)
		{
			return false;
		}

		// Shift elements [index+1, Count-1] one position to the left
		if (index < _count - 1)
		{
			_buffer.Slice(index + 1, _count - index - 1).CopyTo(_buffer.Slice(index));
		}

		_count--;
		return true;
	}

	/// <summary>
	/// Determines whether the list contains a specific item.
	/// </summary>
	/// <param name="item">The item to locate in the list.</param>
	/// <returns>True if the item is found in the list; otherwise, false.</returns>
	/// <remarks>
	/// Uses <see cref="EqualityComparer{T}.Default"/> for comparison.
	/// Performance: O(n) linear search.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(T item)
	{
		return IndexOf(item) >= 0;
	}

	/// <summary>
	/// Searches for the specified item and returns the zero-based index of the first occurrence.
	/// </summary>
	/// <param name="item">The item to locate in the list.</param>
	/// <returns>The zero-based index of the first occurrence of item, or -1 if not found.</returns>
	/// <remarks>
	/// Uses <see cref="EqualityComparer{T}.Default"/> for comparison.
	/// Performance: O(n) linear search.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int IndexOf(T item)
	{
		var comparer = EqualityComparer<T>.Default;
		for (var i = 0; i < _count; i++)
		{
			if (comparer.Equals(_buffer[i], item))
			{
				return i;
			}
		}
		return -1;
	}

	/// <summary>
	/// Removes all items from the list, resetting it to empty state.
	/// </summary>
	/// <remarks>
	/// This does not clear the buffer contents (for performance), only resets the count.
	/// Subsequent adds will overwrite previous values.
	///
	/// IMPORTANT: For pooled buffers (ArrayPool) or sensitive data, consider explicitly clearing
	/// the buffer contents before returning to pool: <c>list.AsSpan().Clear()</c>
	/// Performance: O(1).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear()
	{
		_count = 0;
	}

	/// <summary>
	/// Returns a span view of the current list contents.
	/// </summary>
	/// <returns>A span containing list elements from index 0 to Count-1.</returns>
	/// <remarks>
	/// This provides direct access to the underlying buffer contents (active elements only).
	/// Modifying the returned span will affect the list contents.
	///
	/// The returned span remains valid as long as the underlying buffer exists, but reflects
	/// a snapshot of Count at call time. Add/Insert/RemoveAt operations change Count, making the span
	/// show stale bounds (too few/many elements). Re-call AsSpan() after mutations for current view.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<T> AsSpan()
	{
		return _buffer.Slice(0, _count);
	}
}
