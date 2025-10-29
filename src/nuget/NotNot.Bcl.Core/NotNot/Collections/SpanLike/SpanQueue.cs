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
/// A first-in-first-out (FIFO) queue backed by a caller-provided <see cref="Span{T}"/> buffer.
/// Uses <see cref="SpanRingBuffer{T}"/> internally for efficient circular buffer operations.
/// </summary>
/// <typeparam name="T">The type of elements in the queue.</typeparam>
/// <remarks>
/// SpanQueue provides traditional queue semantics (Enqueue, Dequeue) with zero heap allocation.
/// Internally uses a ring buffer for efficient FIFO operations without element shifting.
///
/// Key characteristics:
/// - Zero heap allocation (uses caller-provided buffer)
/// - Fixed capacity (determined by buffer size)
/// - Efficient FIFO operations via ring buffer
/// - Throws on overflow (Enqueue when full) or underflow (Dequeue when empty)
/// - Aggressive inlining for minimal overhead
/// - Safe variants (TryEnqueue, TryDequeue) for defensive programming
///
/// Typical usage with stackalloc:
/// <code>
/// Span&lt;int&gt; buffer = stackalloc int[128];
/// var queue = new SpanQueue&lt;int&gt;(buffer);
/// queue.Enqueue(42);
/// queue.Enqueue(100);
/// int first = queue.Dequeue(); // 42 (FIFO order)
/// </code>
///
/// Limitations:
/// - ref struct constraints apply (cannot be boxed, stored in fields, used in async methods)
/// - Fixed capacity (no dynamic growth)
/// - Buffer lifetime must exceed SpanQueue usage (typically same stack frame)
///
/// IMPORTANT: SpanQueue is a value type (ref struct). Passing by value creates an independent copy
/// with its own internal state. Always pass by ref or use within a single scope to avoid state divergence.
/// <code>
/// void BadExample(SpanQueue&lt;int&gt; queue) { queue.Enqueue(1); } // Copy, caller won't see enqueue
/// void GoodExample(ref SpanQueue&lt;int&gt; queue) { queue.Enqueue(1); } // By ref, caller sees enqueue
/// </code>
/// </remarks>
public ref struct SpanQueue<T>
{
	private SpanRingBuffer<T> _ring;

	/// <summary>
	/// Initializes a new SpanQueue backed by the specified buffer.
	/// </summary>
	/// <param name="buffer">The buffer to use for queue storage. Caller must ensure buffer lifetime exceeds SpanQueue usage.</param>
	/// <remarks>
	/// The buffer capacity determines the maximum queue size. Attempts to enqueue beyond this capacity will throw.
	/// The buffer should typically be allocated via stackalloc for zero-allocation semantics.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public SpanQueue(Span<T> buffer)
	{
		_ring = new SpanRingBuffer<T>(buffer);
	}

	/// <summary>
	/// Gets the number of elements currently in the queue.
	/// </summary>
	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _ring.Count;
	}

	/// <summary>
	/// Gets the maximum capacity of the queue (the buffer size).
	/// </summary>
	public int Capacity
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _ring.Capacity;
	}

	/// <summary>
	/// Gets a value indicating whether the queue is empty.
	/// </summary>
	public bool IsEmpty
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _ring.IsEmpty;
	}

	/// <summary>
	/// Gets a value indicating whether the queue is at full capacity.
	/// </summary>
	public bool IsFull
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _ring.IsFull;
	}

	/// <summary>
	/// Adds an item to the end of the queue.
	/// </summary>
	/// <param name="item">The item to add.</param>
	/// <exception cref="InvalidOperationException">Thrown when the queue is at full capacity (overflow).</exception>
	/// <remarks>
	/// This method throws on overflow. For defensive programming, use <see cref="TryEnqueue"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Enqueue(T item)
	{
		_ring.Enqueue(item);
	}

	/// <summary>
	/// Attempts to add an item to the end of the queue.
	/// </summary>
	/// <param name="item">The item to add.</param>
	/// <returns>True if the item was enqueued successfully; false if the queue is at full capacity.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Enqueue"/> that returns false instead of throwing on overflow.
	/// Use this when overflow is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryEnqueue(T item)
	{
		return _ring.TryEnqueue(item);
	}

	/// <summary>
	/// Removes and returns the item at the beginning of the queue.
	/// </summary>
	/// <returns>The item at the beginning of the queue (oldest item).</returns>
	/// <exception cref="InvalidOperationException">Thrown when the queue is empty (underflow).</exception>
	/// <remarks>
	/// This method throws on underflow. For defensive programming, use <see cref="TryDequeue"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Dequeue()
	{
		return _ring.Dequeue();
	}

	/// <summary>
	/// Attempts to remove and return the item at the beginning of the queue.
	/// </summary>
	/// <param name="item">
	/// When this method returns true, contains the item at the beginning of the queue.
	/// When this method returns false, contains the default value of T.
	/// </param>
	/// <returns>True if an item was dequeued successfully; false if the queue is empty.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Dequeue"/> that returns false instead of throwing on underflow.
	/// Use this when an empty queue is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryDequeue([MaybeNullWhen(false)] out T item)
	{
		return _ring.TryDequeue(out item);
	}

	/// <summary>
	/// Returns the item at the beginning of the queue without removing it.
	/// </summary>
	/// <returns>The item at the beginning of the queue (oldest item).</returns>
	/// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
	/// <remarks>
	/// This method throws when the queue is empty. For defensive programming, use <see cref="TryPeek"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Peek()
	{
		return _ring.Peek();
	}

	/// <summary>
	/// Attempts to return the item at the beginning of the queue without removing it.
	/// </summary>
	/// <param name="item">
	/// When this method returns true, contains the item at the beginning of the queue.
	/// When this method returns false, contains the default value of T.
	/// </param>
	/// <returns>True if an item was peeked successfully; false if the queue is empty.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Peek"/> that returns false instead of throwing when empty.
	/// Use this when an empty queue is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryPeek([MaybeNullWhen(false)] out T item)
	{
		return _ring.TryPeek(out item);
	}

	/// <summary>
	/// Removes all items from the queue, resetting it to empty state.
	/// </summary>
	/// <remarks>
	/// This does not clear the buffer contents (for performance), only resets internal state.
	/// Subsequent enqueues will overwrite previous values.
	///
	/// IMPORTANT: For pooled buffers (ArrayPool) or sensitive data, consider explicitly clearing
	/// the buffer contents before returning to pool via the underlying ring buffer.
	/// Performance: O(1).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear()
	{
		_ring.Clear();
	}

	/// <summary>
	/// Copies the queue contents to the provided destination span in FIFO order.
	/// </summary>
	/// <param name="destination">The destination span to copy elements into.</param>
	/// <returns>The number of elements copied.</returns>
	/// <exception cref="ArgumentException">Thrown if destination is too small to hold all elements.</exception>
	/// <remarks>
	/// This copies elements in FIFO order (oldest to newest), handling internal circular buffer wraparound.
	/// The destination must be at least Count elements long.
	/// Performance: O(n) where n is Count.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CopyTo(Span<T> destination)
	{
		return _ring.CopyTo(destination);
	}
}
