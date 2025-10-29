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
/// A stack data structure backed by a caller-provided <see cref="Span{T}"/> buffer.
/// This enables zero-allocation stack operations using stack-allocated or pooled memory.
/// </summary>
/// <typeparam name="T">The type of elements in the stack.</typeparam>
/// <remarks>
/// SpanStack is a ref struct that wraps a Span buffer, providing traditional stack semantics
/// (Push, Pop, Peek) without heap allocation. The buffer is provided by the caller, typically
/// via stackalloc or ArrayPool.
///
/// Key characteristics:
/// - Zero heap allocation (uses caller-provided buffer)
/// - Fixed capacity (determined by buffer size)
/// - Throws on overflow (Push when full) or underflow (Pop/Peek when empty)
/// - Aggressive inlining for minimal overhead
/// - Safe variants (TryPop, TryPeek) for defensive programming
///
/// Typical usage with stackalloc:
/// <code>
/// Span&lt;int&gt; buffer = stackalloc int[128];
/// var stack = new SpanStack&lt;int&gt;(buffer);
/// stack.Push(42);
/// int value = stack.Pop();
/// </code>
///
/// Limitations:
/// - ref struct constraints apply (cannot be boxed, stored in fields, used in async methods)
/// - Fixed capacity (no dynamic growth)
/// - Buffer lifetime must exceed SpanStack usage (typically same stack frame)
///
/// IMPORTANT: SpanStack is a value type (ref struct). Passing by value creates an independent copy
/// with its own _count field. Always pass by ref or use within a single scope to avoid state divergence.
/// <code>
/// void BadExample(SpanStack&lt;int&gt; stack) { stack.Push(1); } // Copy, caller won't see push
/// void GoodExample(ref SpanStack&lt;int&gt; stack) { stack.Push(1); } // By ref, caller sees push
/// </code>
/// </remarks>
public ref struct SpanStack<T>
{
	private Span<T> _buffer;
	private int _count;

	/// <summary>
	/// Initializes a new SpanStack backed by the specified buffer.
	/// </summary>
	/// <param name="buffer">The buffer to use for stack storage. Caller must ensure buffer lifetime exceeds SpanStack usage.</param>
	/// <remarks>
	/// The buffer capacity determines the maximum stack depth. Attempts to push beyond this capacity will throw.
	/// The buffer should typically be allocated via stackalloc for zero-allocation semantics.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public SpanStack(Span<T> buffer)
	{
		_buffer = buffer;
		_count = 0;
	}

	/// <summary>
	/// Gets the number of elements currently in the stack.
	/// </summary>
	public int Count
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count;
	}

	/// <summary>
	/// Gets the maximum capacity of the stack (the buffer size).
	/// </summary>
	public int Capacity
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _buffer.Length;
	}

	/// <summary>
	/// Gets a value indicating whether the stack is empty.
	/// </summary>
	public bool IsEmpty
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count == 0;
	}

	/// <summary>
	/// Gets a value indicating whether the stack is at full capacity.
	/// </summary>
	public bool IsFull
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _count >= _buffer.Length;
	}

	/// <summary>
	/// Pushes an item onto the stack.
	/// </summary>
	/// <param name="item">The item to push.</param>
	/// <exception cref="InvalidOperationException">Thrown when the stack is at full capacity (overflow).</exception>
	/// <remarks>
	/// This method throws on overflow. For defensive programming, use <see cref="TryPush"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Push(T item)
	{
		if (_count >= _buffer.Length)
		{
			throw __.Throw($"SpanStack overflow: depth {_count} exceeds buffer capacity {_buffer.Length}. Consider increasing buffer size or checking for pathological depth.");
		}
		_buffer[_count++] = item;
	}

	/// <summary>
	/// Attempts to push an item onto the stack.
	/// </summary>
	/// <param name="item">The item to push.</param>
	/// <returns>True if the item was pushed successfully; false if the stack is at full capacity.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Push"/> that returns false instead of throwing on overflow.
	/// Use this when overflow is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryPush(T item)
	{
		if (_count >= _buffer.Length)
		{
			return false;
		}
		_buffer[_count++] = item;
		return true;
	}

	/// <summary>
	/// Removes and returns the item at the top of the stack.
	/// </summary>
	/// <returns>The item at the top of the stack.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the stack is empty (underflow).</exception>
	/// <remarks>
	/// This method throws on underflow. For defensive programming, use <see cref="TryPop"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Pop()
	{
		__.ThrowIfNot(_count > 0, "SpanStack underflow: cannot pop from empty stack.");
		return _buffer[--_count];
	}

	/// <summary>
	/// Attempts to remove and return the item at the top of the stack.
	/// </summary>
	/// <param name="item">
	/// When this method returns true, contains the item at the top of the stack.
	/// When this method returns false, contains the default value of T.
	/// </param>
	/// <returns>True if an item was popped successfully; false if the stack is empty.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Pop"/> that returns false instead of throwing on underflow.
	/// Use this when an empty stack is a valid condition that should be handled gracefully.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryPop([MaybeNullWhen(false)] out T item)
	{
		if (_count <= 0)
		{
			item = default!;
			return false;
		}
		item = _buffer[--_count];
		return true;
	}

	/// <summary>
	/// Returns the item at the top of the stack without removing it.
	/// </summary>
	/// <returns>The item at the top of the stack.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the stack is empty.</exception>
	/// <remarks>
	/// This method throws when the stack is empty. For defensive programming, use <see cref="TryPeek"/> instead.
	/// Performance: O(1) with aggressive inlining.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Peek()
	{
		__.ThrowIfNot(_count > 0, "SpanStack underflow: cannot peek at empty stack.");
		return _buffer[_count - 1];
	}

	/// <summary>
	/// Attempts to return the item at the top of the stack without removing it.
	/// </summary>
	/// <param name="item">
	/// When this method returns true, contains the item at the top of the stack.
	/// When this method returns false, contains the default value of T.
	/// </param>
	/// <returns>True if an item was peeked successfully; false if the stack is empty.</returns>
	/// <remarks>
	/// This is the safe variant of <see cref="Peek"/> that returns false instead of throwing when empty.
	/// Use this when an empty stack is a valid condition that should be handled gracefully.
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
		item = _buffer[_count - 1];
		return true;
	}

	/// <summary>
	/// Removes all items from the stack, resetting it to empty state.
	/// </summary>
	/// <remarks>
	/// This does not clear the buffer contents (for performance), only resets the count.
	/// Subsequent pushes will overwrite previous values.
	///
	/// IMPORTANT: For pooled buffers (ArrayPool) or sensitive data, consider explicitly clearing
	/// the buffer contents before returning to pool: <c>stack.AsSpan().Clear()</c>
	/// Performance: O(1).
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear()
	{
		_count = 0;
	}

	/// <summary>
	/// Returns a span view of the current stack contents in bottom-to-top order.
	/// </summary>
	/// <returns>A span containing stack elements from bottom (index 0) to top (index Count-1).</returns>
	/// <remarks>
	/// This provides direct access to the underlying buffer contents.
	/// Modifying the returned span will affect the stack contents.
	///
	/// The returned span remains valid as long as the underlying buffer exists, but reflects
	/// a snapshot of Count at call time. Push/Pop operations change Count, making the span
	/// show stale bounds (too few/many elements). Re-call AsSpan() after mutations for current view.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<T> AsSpan()
	{
		return _buffer.Slice(0, _count);
	}
}

