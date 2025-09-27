// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
#if NETCORE_RUNTIME || NET5_0
using System.Runtime.InteropServices;
#endif


namespace NotNot.Collections.Advanced;

/// <summary>
///    An <see cref="IMemoryOwner{T}" /> implementation with an embedded length and a fast <see cref="Span{T}" /> accessor.
///    <para>
///       This <see cref="MemoryOwner_Custom{T}" /> is different from <see cref="MemoryOwner{T}" /> in that this adds a
///       <see cref="ClearOnDispose" /> property.
///    </para>
/// <para>use the `.Allocate()` static method </para>
/// </summary>
/// <typeparam name="T">The type of items to store in the current instance.</typeparam>
//[DebuggerTypeProxy(typeof(CollectionDebugView<>))]
//[DebuggerDisplay("{ToString(),raw}")]
//[DebuggerDisplay("{raw}")]
public sealed class MemoryOwner_Custom<T> : IMemoryOwner<T>, IEnumerable<T>
{
#pragma warning disable IDE0032
	/// <summary>
	///    The usable length within <see cref="array" /> (starting from <see cref="start" />).
	/// </summary>
	private readonly int length;
#pragma warning restore IDE0032

	/// <summary>
	///    The <see cref="ArrayPool{T}" /> instance used to rent <see cref="array" />.
	/// </summary>
	private readonly ArrayPool<T> pool;

	/// <summary>
	///    The starting offset within <see cref="array" />.
	/// </summary>
	private readonly int start;

	/// <summary>
	///    The underlying <typeparamref name="T" /> array.
	/// </summary>
	private T[]? array;

	/// <summary>
	///    Initializes a new instance of the <see cref="MemoryOwner_Custom{T}" /> class with the specified parameters.
	/// </summary>
	/// <param name="length">The length of the new memory buffer to use.</param>
	/// <param name="pool">The <see cref="ArrayPool{T}" /> instance to use.</param>
	/// <param name="mode">Indicates the allocation mode to use for the new buffer to rent.</param>
	private MemoryOwner_Custom(int length, ArrayPool<T> pool, AllocationMode mode)
	{
		start = 0;
		this.length = length;
		this.pool = pool;
		array = pool.Rent(length);

		if (mode == AllocationMode.Clear)
		{
			array.AsSpan(0, length).Clear();
		}
	}

	/// <summary>
	///    Initializes a new instance of the <see cref="MemoryOwner_Custom{T}" /> class with the specified parameters.
	/// </summary>
	/// <param name="start">The starting offset within <paramref name="array" />.</param>
	/// <param name="length">The length of the array to use.</param>
	/// <param name="pool">The <see cref="ArrayPool{T}" /> instance currently in use.</param>
	/// <param name="array">The input <typeparamref name="T" /> array to use.</param>
	private MemoryOwner_Custom(int start, int length, ArrayPool<T> pool, T[] array)
	{
		this.start = start;
		this.length = length;
		this.pool = pool;
		this.array = array;
	}

	private static bool _clearOnDisposeDefault = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

	/// <summary>
	///    set to true if you want the memory cleared upon disposal/collection.
	/// <para>defaults to true if T is a reference type or contains references.</para>
	/// </summary>
	public bool ClearOnDispose { get; set; } = _clearOnDisposeDefault;

	/// <summary>
	///    Gets an empty <see cref="MemoryOwner_Custom{T}" /> instance.
	/// </summary>
	[Pure]
	public static MemoryOwner_Custom<T> Empty
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => new(0, ArrayPool<T>.Shared, AllocationMode.Default);
	}

	/// <summary>
	///    Gets the number of items in the current instance
	/// </summary>
	public int Length
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => length;
	}

	/// <summary>
	///    Gets a <see cref="Span{T}" /> wrapping the memory belonging to the current instance.
	/// </summary>
	public Span<T> Span
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			AssertNotDisposed();
			T[]? array = this.array;

			if (array is null)
			{
				ThrowObjectDisposedException();
			}

#if NETCORE_RUNTIME || NET5_0
				ref T r0 = ref array!.DangerousGetReferenceAt(this.start);

				// On .NET Core runtimes, we can manually create a span from the starting reference to
				// skip the argument validations, which include an explicit null check, covariance check
				// for the array and the actual validation for the starting offset and target length. We
				// only do this on .NET Core as we can leverage the runtime-specific array layout to get
				// a fast access to the initial element, which makes this trick worth it. Otherwise, on
				// runtimes where we would need to at least access a static field to retrieve the base
				// byte offset within an SZ array object, we can get better performance by just using the
				// default Span<T> constructor and paying the cost of the extra conditional branches,
				// especially if T is a value type, in which case the covariance check is JIT removed.
				return MemoryMarshal.CreateSpan(ref r0, this.length);
#else
			return new Span<T>(array!, start, length);
#endif
		}
	}
	/// <summary>
	/// if disposed we will throw if you try to access the memory/span/array
	/// </summary>
	public bool IsDisposed { get; private set; }

	/// <inheritdoc />
	public Memory<T> Memory
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			AssertNotDisposed();
			T[]? array = this.array;

			if (array is null)
			{
				ThrowObjectDisposedException();
			}

			return new Memory<T>(array!, start, length);
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (IsDisposed)
		{
			return;
		}

		IsDisposed = true;

		T[]? array = this.array;

		if (array is null)
		{
			return;
		}

		GC.SuppressFinalize(this);

		this.array = null;
		if (ClearOnDispose) { array.AsSpan(0, length).Clear(); }

		pool.Return(array);
	}

	public IEnumerator<T> GetEnumerator()
	{
		return this.DangerousGetArray().GetEnumerator();
	}

	/// <summary>
	///    Finalizes an instance of the <see cref="MemoryOwner_Custom{T}" /> class.
	/// </summary>
	~MemoryOwner_Custom()
	{
		Dispose();
	}

	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		__.ThrowIfNot(!IsDisposed, "The current buffer has already been disposed");
	}

	/// <summary>
	///    Creates a new <see cref="MemoryOwner_Custom{T}" /> instance with the specified parameters.
	/// </summary>
	/// <param name="size">The length of the new memory buffer to use.</param>
	/// <returns>A <see cref="MemoryOwner_Custom{T}" /> instance of the requested length.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size" /> is not valid.</exception>
	/// <remarks>This method is just a proxy for the <see langword="private" /> constructor, for clarity.</remarks>
	[Pure]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static MemoryOwner_Custom<T> Allocate(int size)
	{
		return new MemoryOwner_Custom<T>(size, ArrayPool<T>.Shared, AllocationMode.Default);
	}

	/// <summary>
	///    Creates a new <see cref="MemoryOwner_Custom{T}" /> instance with the specified parameters.
	/// </summary>
	/// <param name="size">The length of the new memory buffer to use.</param>
	/// <param name="pool">The <see cref="ArrayPool{T}" /> instance currently in use.</param>
	/// <returns>A <see cref="MemoryOwner_Custom{T}" /> instance of the requested length.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size" /> is not valid.</exception>
	/// <remarks>This method is just a proxy for the <see langword="private" /> constructor, for clarity.</remarks>
	[Pure]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static MemoryOwner_Custom<T> Allocate(int size, ArrayPool<T> pool)
	{
		return new MemoryOwner_Custom<T>(size, pool, AllocationMode.Default);
	}

	/// <summary>
	///    Creates a new <see cref="MemoryOwner_Custom{T}" /> instance with the specified parameters.
	/// </summary>
	/// <param name="size">The length of the new memory buffer to use.</param>
	/// <param name="mode">Indicates the allocation mode to use for the new buffer to rent.</param>
	/// <returns>A <see cref="MemoryOwner_Custom{T}" /> instance of the requested length.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size" /> is not valid.</exception>
	/// <remarks>This method is just a proxy for the <see langword="private" /> constructor, for clarity.</remarks>
	[Pure]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static MemoryOwner_Custom<T> Allocate(int size, AllocationMode mode)
	{
		return new MemoryOwner_Custom<T>(size, ArrayPool<T>.Shared, mode);
	}

	/// <summary>
	///    Creates a new <see cref="MemoryOwner_Custom{T}" /> instance with the specified parameters.
	/// </summary>
	/// <param name="size">The length of the new memory buffer to use.</param>
	/// <param name="pool">The <see cref="ArrayPool{T}" /> instance currently in use.</param>
	/// <param name="mode">Indicates the allocation mode to use for the new buffer to rent.</param>
	/// <returns>A <see cref="MemoryOwner_Custom{T}" /> instance of the requested length.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size" /> is not valid.</exception>
	/// <remarks>This method is just a proxy for the <see langword="private" /> constructor, for clarity.</remarks>
	[Pure]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static MemoryOwner_Custom<T> Allocate(int size, ArrayPool<T> pool, AllocationMode mode)
	{
		return new MemoryOwner_Custom<T>(size, pool, mode);
	}

	/// <summary>
	///    Returns a reference to the first element within the current instance, with no bounds check.
	/// </summary>
	/// <returns>A reference to the first element within the current instance.</returns>
	/// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
	/// <remarks>
	///    This method does not perform bounds checks on the underlying buffer, but does check whether
	///    the buffer itself has been disposed or not. This check should not be removed, and it's also
	///    the reason why the method to get a reference at a specified offset is not present.
	/// </remarks>
	[Pure]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T DangerousGetReference()
	{
		AssertNotDisposed();
		T[]? array = this.array;

		if (array is null)
		{
			ThrowObjectDisposedException();
		}

		return ref array!.DangerousGetReferenceAt(start);
	}

	/// <summary>
	///    Gets an <see cref="ArraySegment{T}" /> instance wrapping the underlying <typeparamref name="T" /> array in use.
	/// </summary>
	/// <returns>An <see cref="ArraySegment{T}" /> instance wrapping the underlying <typeparamref name="T" /> array in use.</returns>
	/// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
	/// <remarks>
	///    This method is meant to be used when working with APIs that only accept an array as input, and should be used with
	///    caution.
	///    In particular, the returned array is rented from an array pool, and it is responsibility of the caller to ensure
	///    that it's
	///    not used after the current <see cref="MemoryOwner_Custom{T}" /> instance is disposed. Doing so is considered
	///    undefined behavior,
	///    as the same array might be in use within another <see cref="MemoryOwner_Custom{T}" /> instance.
	/// </remarks>
	[Pure]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ArraySegment<T> DangerousGetArray()
	{
		AssertNotDisposed();
		T[]? array = this.array;

		if (array is null)
		{
			ThrowObjectDisposedException();
		}

		return new ArraySegment<T>(array!, start, length);
	}

	/// <summary>
	///    Slices the buffer currently in use and returns a new <see cref="MemoryOwner_Custom{T}" /> instance.
	/// </summary>
	/// <param name="start">The starting offset within the current buffer.</param>
	/// <param name="length">The length of the buffer to use.</param>
	/// <returns>A new <see cref="MemoryOwner_Custom{T}" /> instance using the target range of items.</returns>
	/// <exception cref="ObjectDisposedException">Thrown when the buffer in use has already been disposed.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///    Thrown when <paramref name="start" /> or <paramref name="length" /> are
	///    not valid.
	/// </exception>
	/// <remarks>
	///    Using this method will dispose the current instance, and should only be used when an oversized
	///    buffer is rented and then adjusted in size, to avoid having to rent a new buffer of the new
	///    size and copy the previous items into the new one, or needing an additional variable/field
	///    to manually handle to track the used range within a given <see cref="MemoryOwner_Custom{T}" /> instance.
	/// </remarks>
	public MemoryOwner_Custom<T> Slice(int start, int length)
	{
		AssertNotDisposed();
		T[]? array = this.array;

		if (array is null)
		{
			ThrowObjectDisposedException();
		}

		this.array = null;

		if ((uint)start > this.length)
		{
			ThrowInvalidOffsetException();
		}

		if ((uint)length > this.length - start)
		{
			ThrowInvalidLengthException();
		}

		// We're transferring the ownership of the underlying array, so the current
		// instance no longer needs to be disposed. Because of this, we can manually
		// suppress the finalizer to reduce the overhead on the garbage collector.
		GC.SuppressFinalize(this);

		return new MemoryOwner_Custom<T>(start, length, pool, array!);
	}

	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{length}]";
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
	///// <inheritdoc/>
	//[Pure]
	//public override string ToString()
	//{
	//	// Normally we would throw if the array has been disposed,
	//	// but in this case we'll just return the non formatted
	//	// representation as a fallback, since the ToString method
	//	// is generally expected not to throw exceptions.
	//	if (typeof(T) == typeof(char) &&
	//		this.array is char[] chars)
	//	{
	//		return new string(chars, this.start, this.length);
	//	}

	//	// Same representation used in Span<T>
	//	return $"CommunityToolkit.HighPerformance.Buffers.MemoryOwner<{typeof(T)}>[{this.length}]";
	//}

	/// <summary>
	///    Throws an <see cref="ObjectDisposedException" /> when <see cref="array" /> is <see langword="null" />.
	/// </summary>
	private static void ThrowObjectDisposedException()
	{
		throw new ObjectDisposedException(nameof(MemoryOwner_Custom<T>), "The current buffer has already been disposed");
	}

	/// <summary>
	///    Throws an <see cref="ArgumentOutOfRangeException" /> when the <see cref="start" /> is invalid.
	/// </summary>
	private static void ThrowInvalidOffsetException()
	{
		throw new ArgumentOutOfRangeException(nameof(start), "The input start parameter was not valid");
	}

	/// <summary>
	///    Throws an <see cref="ArgumentOutOfRangeException" /> when the <see cref="length" /> is invalid.
	/// </summary>
	private static void ThrowInvalidLengthException()
	{
		throw new ArgumentOutOfRangeException(nameof(length), "The input length parameter was not valid");
	}
}
