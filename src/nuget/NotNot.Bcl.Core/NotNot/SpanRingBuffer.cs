// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NotNot.Collections;

/// <summary>
/// A circular buffer (ring buffer) backed by a caller-provided <see cref="Span{T}"/> buffer.
/// Provides efficient FIFO operations with wraparound for zero-allocation queue implementations.
/// </summary>
/// <typeparam name="T">The type of elements in the ring buffer.</typeparam>
/// <remarks>
/// SpanRingBuffer wraps a Span buffer with head/tail indices to create a circular view.
/// Elements are enqueued at the tail and dequeued from the head, with automatic wraparound
/// when indices reach the buffer end.
///
/// Key characteristics:
/// - Zero heap allocation (uses caller-provided buffer)
/// - Fixed capacity (determined by buffer size)
/// - Efficient FIFO operations without element shifting
/// - Throws on overflow (Enqueue when full) or underflow (Dequeue when empty)
/// - Aggressive inlining for minimal overhead
/// - Safe variants (TryEnqueue, TryDequeue) for defensive programming
///
/// Typical usage with stackalloc:
/// <code>
/// Span&lt;int&gt; buffer = stackalloc int[128];
/// var ring = new SpanRingBuffer&lt;int&gt;(buffer);
/// ring.Enqueue(42);
/// ring.Enqueue(100);
/// int first = ring.Dequeue(); // 42 (FIFO order)
/// </code>
///
/// Limitations:
/// - ref struct constraints apply (cannot be boxed, stored in fields, used in async methods)
/// - Fixed capacity (no dynamic growth)
/// - Buffer lifetime must exceed SpanRingBuffer usage (typically same stack frame)
///
/// IMPORTANT: SpanRingBuffer is a value type (ref struct). Passing by value creates an independent copy
/// with its own _head, _tail, and _count fields. Always pass by ref or use within a single scope to avoid state divergence.
/// <code>
/// void BadExample(SpanRingBuffer&lt;int&gt; ring) { ring.Enqueue(1); } // Copy, caller won't see enqueue
/// void GoodExample(ref SpanRingBuffer&lt;int&gt; ring) { ring.Enqueue(1); } // By ref, caller sees enqueue
/// </code>
/// </remarks>
public ref struct SpanRingBuffer<T>
{
	private Span<T> _buffer;
	private int _head;  // Index where next dequeue happens
	private int _tail;  // Index where next enqueue happens
	private int _count; // Number of elements currently in buffer

	/// <summary>
	/// Initializes a new SpanRingBuffer backed by the specified buffer.
	/// </summary>
	/// <param name="buffer">The buffer to use for ring buffer storage. Caller must ensure buffer lifetime exceeds SpanRingBuffer usage.</param>
	/// <remarks>
	/// The buffer capacity determines the maximum ring buffer size. Attempts to enqueue beyond this capacity will throw.
	/// The buffer should typically be allocated via stackalloc for zero-allocation semantics.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public SpanRingBuffer(Span<T> buffer)
	{
		_buffer = buffer;
		_head = 0;
		_tail = 0;
		_count = 0;
	}

	/// <summary>
	/// Gets the number of elements currently in the ring buffer.
	/// </summary>
	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count;
	}

	/// <summary>
	/// Gets the maximum capacity of the ring buffer (the buffer size).
	/// </summary>
	public int Capacity
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _buffer.Length;
	}

	/// <summary>
	/// Gets a value indicating whether the ring buffer is empty.
	/// </summary>
	public bool IsEmpty
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count == 0;
	}

	/// <summary>
	/// Gets a value indicating whether the ring buffer is at full capacity.
	/// </summary>
	public bool IsFull
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count >= _buffer.Length;
	}

	/// <summary>
	/// Enqueues an item at the tail of the ring buffer.
	/// </summary>
	/// <param name="item">The item to enqueue.</param>
	/// <exception cref="InvalidOperationException">Thrown when the ring buffer is at full capacity (overflow).</exception>
	/// <remarks>
	/// This method throws on overflow. For defensive programming, use <see cref="TryEnqueue"/> instead.
	/// The tail automatically wraps to index 0 when reaching buffer end.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Enqueue(T item)
	{
		if (_count >= _buffer.Length)
		{
			throw __.Throw($"SpanRingBuffer overflow: count {_count} equals capacity {_buffer.Length}. Consider increasing buffer size.");
		}
		_buffer[_tail] = item;
		_tail = (_tail + 1) % _buffer.Length; // Wraparound
		_count++;
	}

	/// <summary>
	/// Attempts to enqueue an item at the tail of the ring buffer.
	/// </summary>
	/// <param name="item">The item to enqueue.</param>
	/// <returns>True if the item was enqueued successfully; false if the ring buffer is at full capacity.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Enqueue"/> that returns false instead of throwing on overflow.
	/// Use this when overflow is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryEnqueue(T item)
	{
		if (_count >= _buffer.Length)
		{
			return false;
		}
		_buffer[_tail] = item;
		_tail = (_tail + 1) % _buffer.Length;
		_count++;
		return true;
	}

	/// <summary>
	/// Removes and returns the item at the head of the ring buffer.
	/// </summary>
	/// <returns>The item at the head of the ring buffer (oldest item).</returns>
	/// <exception cref="InvalidOperationException">Thrown when the ring buffer is empty (underflow).</exception>
	/// <remarks>
	/// This method throws on underflow. For defensive programming, use <see cref="TryDequeue"/> instead.
	/// The head automatically wraps to index 0 when reaching buffer end.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Dequeue()
	{
		__.ThrowIfNot(_count > 0, "SpanRingBuffer underflow: cannot dequeue from empty ring buffer.");
		T item = _buffer[_head];
		_head = (_head + 1) % _buffer.Length; // Wraparound
		_count--;
		return item;
	}

	/// <summary>
	/// Attempts to remove and return the item at the head of the ring buffer.
	/// </summary>
	/// <param name="item">
	/// When this method returns true, contains the item at the head of the ring buffer.
	/// When this method returns false, contains the default value of T.
	/// </param>
	/// <returns>True if an item was dequeued successfully; false if the ring buffer is empty.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Dequeue"/> that returns false instead of throwing on underflow.
	/// Use this when an empty ring buffer is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryDequeue([MaybeNullWhen(false)] out T item)
	{
		if (_count <= 0)
		{
			item = default!;
			return false;
		}
		item = _buffer[_head];
		_head = (_head + 1) % _buffer.Length;
		_count--;
		return true;
	}

	/// <summary>
	/// Returns the item at the head of the ring buffer without removing it.
	/// </summary>
	/// <returns>The item at the head of the ring buffer (oldest item).</returns>
	/// <exception cref="InvalidOperationException">Thrown when the ring buffer is empty.</exception>
	/// <remarks>
	/// This method throws when the ring buffer is empty. For defensive programming, use <see cref="TryPeek"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Peek()
	{
		__.ThrowIfNot(_count > 0, "SpanRingBuffer underflow: cannot peek at empty ring buffer.");
		return _buffer[_head];
	}

	/// <summary>
	/// Attempts to return the item at the head of the ring buffer without removing it.
	/// </summary>
	/// <param name="item">
	/// When this method returns true, contains the item at the head of the ring buffer.
	/// When this method returns false, contains the default value of T.
	/// </param>
	/// <returns>True if an item was peeked successfully; false if the ring buffer is empty.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Peek"/> that returns false instead of throwing when empty.
	/// Use this when an empty ring buffer is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryPeek([MaybeNullWhen(false)] out T item)
	{
		if (_count <= 0)
		{
			item = default!;
			return false;
		}
		item = _buffer[_head];
		return true;
	}

	/// <summary>
	/// Removes all items from the ring buffer, resetting it to empty state.
	/// </summary>
	/// <remarks>
	/// This does not clear the buffer contents (for performance), only resets head, tail, and count.
	/// Subsequent enqueues will overwrite previous values.
	///
	/// IMPORTANT: For pooled buffers (ArrayPool) or sensitive data, consider explicitly clearing
	/// the buffer contents before returning to pool: <c>ring.AsSpan().Clear()</c>
	/// Performance: O(1).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear()
	{
		_head = 0;
		_tail = 0;
		_count = 0;
	}

	/// <summary>
	/// Copies the ring buffer contents to the provided destination span in FIFO order (head to tail).
	/// </summary>
	/// <param name="destination">The destination span to copy elements into.</param>
	/// <returns>The number of elements copied.</returns>
	/// <exception cref="ArgumentException">Thrown if destination is too small to hold all elements.</exception>
	/// <remarks>
	/// This linearizes the circular buffer into sequential order, handling wraparound automatically.
	/// The destination must be at least Count elements long.
	/// Performance: O(n) where n is Count.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int CopyTo(Span<T> destination)
	{
		if (destination.Length < _count)
		{
			throw __.Throw($"Destination span too small: needs {_count} elements but has {destination.Length}.");
		}

		if (_count == 0)
		{
			return 0;
		}

		// Handle potential wraparound
		if (_head < _tail)
		{
			// No wraparound: simple slice copy
			_buffer.Slice(_head, _count).CopyTo(destination);
		}
		else
		{
			// Wraparound: copy head to end, then start to tail
			int firstPartLength = _buffer.Length - _head;
			_buffer.Slice(_head, firstPartLength).CopyTo(destination);
			_buffer.Slice(0, _tail).CopyTo(destination.Slice(firstPartLength));
		}

		return _count;
	}

	/// <summary>
	/// Returns a span view of the entire backing buffer (including unused slots).
	/// </summary>
	/// <returns>A span of the complete backing buffer.</returns>
	/// <remarks>
	/// CAUTION: This returns the entire buffer, not just active elements. Active elements are non-contiguous
	/// due to circular indexing. Use <see cref="CopyTo"/> to get elements in FIFO order.
	///
	/// The returned span remains valid as long as the underlying buffer exists, but reflects
	/// a snapshot of head/tail at call time. Enqueue/Dequeue operations change internal indices,
	/// making direct buffer access unreliable. This method is primarily for advanced scenarios
	/// like buffer hygiene (clearing sensitive data).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<T> AsSpan()
	{
		return _buffer;
	}
}
