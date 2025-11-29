// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
#define CHECKED

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;

namespace NotNot.Collections.SpanLike;



/// <summary>
///    helpers to allocate a WriteMem instance
/// </summary>
public static class Mem
{
	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(ArraySegment<T> backingStore)
	{
		return new Mem<T>(MemBackingStorageType.Array,backingStore.Array,backingStore.Offset,backingStore.Count);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(T[] array)
	{
		return Wrap(new ArraySegment<T>(array));
	}


	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(Memory<T> memory)
	{
		return new Mem<T>(MemBackingStorageType.Memory, memory);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(List<T> list)
	{
		return new Mem<T>(MemBackingStorageType.List, list);
	}

	/// <summary>
	///   Wrap a rented array from ObjectPool as the backing storage
	/// </summary>
	public static RentedMem<T> Wrap<T>(NotNot._internal.ObjectPool.RentedArray<T> rentedArray)
	{
		return new RentedMem<T>(rentedArray, isTrueOwner: true);
	}

	/// <summary>
	///   Wrap a rented list from ObjectPool as the backing storage
	/// </summary>
	public static RentedMem<T> Wrap<T>(NotNot._internal.ObjectPool.Rented<List<T>> rentedList)
	{
		return new RentedMem<T>(rentedList, isTrueOwner: true);
	}




	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static RentedMem<T> RentSingle<T>(T singleItem)
	{
		return RentedMem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	/// <param name="allowGCReclaim">default false.   pass true to not get asserts raised if you don't .Dispose() of this properly.
	/// <para>useful for when you have explicitly "fire and forget" versions of objects, but should be avoided as an antipattern.</para></param>
	public static RentedMem<T> Rent<T>(int count, bool allowGCReclaim = false, AllocationMode allocationMode = AllocationMode.Clear)
	{
		return RentedMem<T>.Allocate(count, allowGCReclaim,allocationMode);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static RentedMem<T> Clone<T>(ReadOnlySpan<T> span)
	{
		var toReturn = Mem.Rent<T>(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	/// <summary>
	///  legacy conversion method.
	/// </summary>
	public static Mem<T> Wrap<T>(Mem<T> writeMem)
	{
		return writeMem;
	}


	/// <summary>
	///   Wrap a single item as the backing storage. Creates a span-accessible single-element Mem.
	/// </summary>
	public static Mem<T> WrapSingle<T>(T singleItem)
	{
		return new Mem<T>(MemBackingStorageType.SingleItem, singleItem);
	}
}


/// <summary>
/// Non-owning view into memory.  NO disposal/lifetime management.
/// Use for temporary access where caller holds ownership elsewhere and will dispose when callstack pops.
/// </summary>
/// <remarks>
/// <para>Unlike ref struct, this can be used in async methods and stored in fields.</para>
/// <para>Conversions:</para>
/// <para>- Implicit FROM: RentedMem{T} - safe narrowing to non-owning view</para>
/// <para>- Implicit TO: Span{T}, ReadOnlySpan{T} - performant, consistent access the backing storage</para>
/// <para>- NO conversion TO RentedMem{T} - cannot restore ownership metadata</para>
/// </remarks>
/// <typeparam name="T">Element type</typeparam>
public readonly struct Mem<T>
{

	// ========== Implicit conversions FROM owning types (safe narrowing) ==========

	public static implicit operator Mem<T>(T[] array) =>Mem.Wrap(array);
	public static implicit operator Mem<T>(ArraySegment<T> arraySegment) => Mem.Wrap(arraySegment);
	public static implicit operator Mem<T>(List<T> list) => Mem.Wrap(list);
	public static implicit operator Mem<T>(Memory<T> memory) => Mem.Wrap(memory);
	//allow casting from rented structures (no ownership)
	public static implicit operator Mem<T>(_internal.ObjectPool.Rented<List<T>> rented) => new(MemBackingStorageType.RentedList, rented);
	public static implicit operator Mem<T>(_internal.ObjectPool.RentedArray<T> rented) => new(MemBackingStorageType.RentedArray, rented);
	public static implicit operator Mem<T>(RentedMem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);

	// ========== Implicit conversions TO span types ==========

	/// <summary>
	/// Implicit conversion to Span{T}
	/// </summary>
	public static implicit operator Span<T>(Mem<T> mem) => mem.Span;

	/// <summary>
	/// Implicit conversion to ReadOnlySpan{T}
	/// </summary>
	public static implicit operator ReadOnlySpan<T>(Mem<T> mem) => mem.Span;

	// ========== Internal fields - mirrors Mem<T> structure ==========

	/// <summary>
	/// Identifies which type of backing store is being used
	/// </summary>
	internal readonly MemBackingStorageType _backingStorageType;

	/// <summary>
	/// Reference to the actual backing storage object
	/// </summary>
	internal readonly object _backingStorage;

	/// <summary>
	/// Offset into the backing storage where this view begins
	/// <para>if null, use backing storage default</para>
	/// </summary>
	internal readonly int _segmentOffset;

	/// <summary>
	/// Number of elements in this memory view
	/// <para>if null, use backing storage default</para>
	/// </summary>
	internal readonly int _segmentCount;

	/// <summary>
	/// used if the backing storage is a list, to ensure it's not modified
	/// </summary>
	private readonly int _listLength;

	/// <summary>
	/// Represents an empty ephemeral view with zero elements
	/// </summary>
	public static readonly Mem<T> Empty = new(MemBackingStorageType.Empty, null!, 0, 0);

	// ========== Constructors ==========

	internal Mem(MemBackingStorageType backingStorageType, object backingStorage)
	{
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		var rawSpan = _GetRawSpan();
		_segmentOffset = 0;
		_segmentCount = rawSpan.Length;

#if CHECKED
		switch (_backingStorageType)
		{
			case MemBackingStorageType.List:
			case MemBackingStorageType.RentedList:
				_listLength = rawSpan.Length;
				break;
		}
#endif
	}
	/// <summary>
	/// Internal constructor from backing storage components
	/// </summary>
	internal Mem(MemBackingStorageType backingStorageType, object backingStorage, int segmentOffset, int segmentCount)
	{
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		_segmentOffset = segmentOffset;
		_segmentCount = segmentCount;

#if CHECKED
		switch (_backingStorageType)
		{
			case MemBackingStorageType.List:
			case MemBackingStorageType.RentedList:
				_listLength = _GetRawSpan().Length;
				break;
		}
#endif
	}

	// ========== Core properties ==========

	private Span<T> _GetCurrentSegment(Span<T> backingSpan)
	{
		return backingSpan.Slice(_segmentOffset, _segmentCount);
	}

	/// <summary>
	/// Gets a Span{T} view over this memory
	/// </summary>
	public Span<T> Span
	{
		get
		{

			if (_segmentCount == 0) return Span<T>.Empty;
			var rawSpan = _GetRawSpan();


#if CHECKED
			switch (_backingStorageType)
			{
				case MemBackingStorageType.List:
				case MemBackingStorageType.RentedList:
					__.AssertIfNot(_listLength == rawSpan.Length);
					break;
			}
#endif

			var toReturn = _GetCurrentSegment(rawSpan);
			__.DebugAssertIfNot(toReturn.Length == Length);
			return toReturn;
		}
	}

	private Span<T> _GetRawSpan()
	{
		{
			switch (_backingStorageType)
			{
				case MemBackingStorageType.Empty:
					return Span<T>.Empty;
				case MemBackingStorageType.MemoryOwner_Custom:
					{
						var owner = (MemoryOwner_Custom<T>)_backingStorage;
						var span = owner.Span;
						return span;
					}
				case MemBackingStorageType.Array:
					{
						var array = (T[])_backingStorage;
						return array;
					}
				case MemBackingStorageType.List:
					{
						var list = (List<T>)_backingStorage;
						var span = list._AsSpan_Unsafe();
						return span;
					}
				case MemBackingStorageType.Memory:
					{
						var memory = (Memory<T>)_backingStorage;
						var span = memory.Span;
						return span;
					}
				case MemBackingStorageType.RentedArray:
					{
						var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
						var span = rentedArray.Value;
						return span;
					}
				case MemBackingStorageType.RentedList:
					{
						var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
						var span = rentedList.Value._AsSpan_Unsafe();
						return span;
					}
				case MemBackingStorageType.SingleItem:
					{
						var storage = (SingleItemStorage<T>)_backingStorage;
						var span = MemoryMarshal.CreateSpan(ref storage.Value, 1);
						return span;
					}
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	/// <summary>
	/// Gets the number of elements in this memory view
	/// </summary>
	public int Length => _segmentCount;

	///// <summary>
	///// Returns true if this memory view has zero elements
	///// </summary>
	//public bool IsEmpty => _segmentCount == 0;

	/// <summary>
	/// Gets a reference to the element at the specified index
	/// </summary>
	public ref T this[int index] => ref Span[index];

	// ========== Slicing ==========

	/// <summary>
	/// Creates a new ephemeral view that is a slice of this memory
	/// </summary>
	public Mem<T> Slice(int offset)
	{
		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
		return new Mem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, _segmentCount - offset);
	}

	/// <summary>
	/// Creates a new ephemeral view that is a slice of this memory
	/// </summary>
	public Mem<T> Slice(int offset, int count)
	{
		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
		__.ThrowIfNot(count >= 0 && offset + count <= _segmentCount);
		return new Mem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, count);
	}

	// ========== Enumeration ==========

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	public Span<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

	// ========== Utility methods ==========

	/// <summary>
	/// Creates a deep copy of this Mem with contents copied to new pool-backed storage
	/// </summary>
	/// <returns>New pooled memory containing a copy of this memory's contents</returns>
	public RentedMem<T> Clone()
	{
		var copy = Mem.Rent<T>(Length);
		Span.CopyTo(copy.Span);
		return copy;
	}

	/// <summary>
	/// Copies the contents of this memory into a destination span
	/// </summary>
	public void CopyTo(Span<T> destination) => Span.CopyTo(destination);

	/// <summary>
	/// Attempts to copy the contents of this memory into a destination span
	/// </summary>
	public bool TryCopyTo(Span<T> destination) => Span.TryCopyTo(destination);

	/// <summary>
	/// Fills the memory with the specified value
	/// </summary>
	public void Fill(T value) => Span.Fill(value);

	/// <summary>
	/// Clears the memory (sets all elements to default)
	/// </summary>
	public void Clear() => Span.Clear();

	/// <summary>
	/// Converts to array by copying contents
	/// </summary>
	public T[] ToArray() => Span.ToArray();

	/// <summary>
	/// Returns a string representation of this memory view
	/// </summary>
	public override string ToString() => $"Mem<{typeof(T).Name}>[{Length}]";




	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Span<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		var thisSpan = Span;
		var toReturnSpan = toReturn;
		for (var i = 0; i < Length; i++)
		{
			ref var r_mappedResult = ref mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = r_mappedResult;
		}
	}

	/// <summary>
	/// Allocates a new pooled RentedMem by applying the specified mapping function to each element
	/// </summary>
	public RentedMem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		Map(toReturn, mapFunc);
		return toReturn;// RentedMem<TResult>.FromMem(toReturn);
	}

	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Span<TResult> toReturn, Func_RefArg<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		var thisSpan = Span;
		var toReturnSpan = toReturn;
		for (var i = 0; i < Length; i++)
		{
			var mappedResult = mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
	}

	/// <summary>
	/// Allocates a new pooled RentedMem by applying the specified mapping function to each element
	/// </summary>
	public RentedMem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		Map(toReturn, mapFunc);
		return toReturn;// RentedMem<TResult>.FromMem(toReturn);
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified function, writing results to the output buffer
	/// </summary>
	public void MapWith<TOther, TResult>(Span<TResult> toReturn, Span<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");
		var thisSpan = Span;
		var otherSpan = otherToMapWith;
		var toReturnSpan = toReturn;

		for (var i = 0; i < Length; i++)
		{
			ref var r_mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
			toReturnSpan[i] = r_mappedResult;
		}
	}

	/// <summary>
	/// Allocates a new pooled RentedMem by mapping two memory instances in parallel
	/// </summary>
	public RentedMem<TResult> MapWith<TOther, TResult>(Span<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		MapWith(toReturn, otherToMapWith, mapFunc);
		return toReturn;// RentedMem<TResult>.FromMem(toReturn);
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified action, modifying elements in place
	/// </summary>
	public void MapWith<TOther>(Span<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");
		var thisSpan = Span;
		var otherSpan = otherToMapWith;

		for (var i = 0; i < Length; i++)
		{
			mapFunc(ref thisSpan[i], ref otherSpan[i]);
		}
	}

	/// <summary>
	/// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per batch.
	/// Worker receives a Span slice - no allocation.
	/// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
	/// </summary>
	public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>> worker)
	{
		if (Length == 0)
		{
			return;
		}
		var span = Span;
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(span, batchStart, isSameBatch);
			worker(span.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}
	/// <summary>
	/// <para>Walks contiguous batches where `<paramref name="isSameBatch"/>` returns true for each previous/current pair and calls `<paramref name="worker"/>` once per range.</para>
	/// <para>Use this to process subgroups without extra allocations.</para>
	/// <para>IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.</para>
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a `Mem` slice that references this instance's backing store.</param>
	/// <returns>A completed task once all batches have been processed.</returns>
	public async ValueTask BatchMap(Func_RefArg<T, T, bool> isSameBatch, Func<Mem<T>, ValueTask> worker)
	{
		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(this, batchStart, isSameBatch);
			await worker(Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Walks contiguous batches with parallel memory and calls worker once per batch.
	/// Workers receive Span slices - no allocation.
	/// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
	/// </summary>
	public void BatchMapWith<TOther>(Span<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>, Span<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");

		if (Length == 0)
		{
			return;
		}
		var thisSpan = Span;
		var otherSpan = otherToMapWith;//.Span;
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(thisSpan, batchStart, isSameBatch);
			worker(thisSpan.Slice(batchStart, batchEnd - batchStart), otherSpan.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}
	public async ValueTask BatchMapWith<TOther>(Mem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Func<Mem<T>, Mem<TOther>, ValueTask> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this Mem");

		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(this, batchStart, isSameBatch);
			await worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Local synchronous scanner that finds the end of a contiguous batch
	/// </summary>
	private static int _GetBatchEndExclusive(Span<T> span, int start, Func_RefArg<T, T, bool> isSameBatch)
	{
		var end = start + 1;
		while (end < span.Length)
		{
			ref var r_previous = ref span[end - 1];
			ref var r_current = ref span[end];
			if (!isSameBatch(ref r_previous, ref r_current))
			{
				break;
			}
			end++;
		}
		return end;
	}
}
