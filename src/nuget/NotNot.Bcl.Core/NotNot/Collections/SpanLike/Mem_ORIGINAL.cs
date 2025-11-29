// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;

namespace NotNot.Collections.SpanLike;
/// <summary>
/// Wrapper class for single-item storage in Mem&lt;T&gt;. Provides a stable managed reference for Span creation.
/// </summary>
internal struct SingleItemStorage<T>
{
	public T Value;
	public SingleItemStorage(T value) => Value = value;
}

/// <summary>
/// A universal, write-capable view into a wrapped array/list/memory backing storage, with support for pooled allocation (renting) for temporary collections (see <see cref="Allocate(int)"/>).
/// Supports implicit casting from array/list/memory along with explicit via Mem.Wrap() methods.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
public readonly struct Mem_ORIGINAL<T> : IDisposable
{
	////implicit operators
	public static implicit operator Mem_ORIGINAL<T>(T[] array) => new Mem_ORIGINAL<T>(array);
	public static implicit operator Mem_ORIGINAL<T>(ArraySegment<T> arraySegment) => new Mem_ORIGINAL<T>(arraySegment);
	public static implicit operator Mem_ORIGINAL<T>(List<T> list) => new Mem_ORIGINAL<T>(list);
	public static implicit operator Mem_ORIGINAL<T>(Memory<T> memory) => new Mem_ORIGINAL<T>(memory);
	/// <summary>
	/// disabling because of ambiguity of who the owner should be.   can do wrapping
	/// </summary>
	//public static implicit operator Mem_ORIGINAL<T>(MemoryOwner_Custom<T> owner) => new Mem_ORIGINAL<T>(owner);
	public static implicit operator Span<T>(Mem_ORIGINAL<T> refMem) => refMem.Span;
	public static implicit operator ReadOnlySpan<T>(Mem_ORIGINAL<T> refMem) => refMem.Span;


	/// <summary>
	/// Identifies which type of backing store is being used
	/// </summary>
	internal readonly MemBackingStorageType _backingStorageType;

	/// <summary>
	/// Reference to the actual backing storage object (Array, List, Memory, or MemoryOwner_Custom)
	/// </summary>
	internal readonly object _backingStorage;

	/// <summary>
	/// Number of elements in this memory view
	/// </summary>
	internal readonly int _segmentCount;

	/// <summary>
	/// Offset into the backing storage where this view begins
	/// </summary>
	internal readonly int _segmentOffset;

	/// <summary>
	/// if true, it's disposal will return a `MemoryOwner_Custom` to the pool; if false, it's a slice and should not dispose the owner
	/// </summary>
	internal readonly bool _isTrueOwner = false;

	///// <summary>
	/////    details the backing storage
	///// </summary>
	//private readonly ArraySegment<T> _segment;

	//private readonly T[] _array;
	//private readonly int _offset;
	//public readonly int length;

	/// <summary>
	/// Represents an empty memory view with zero elements
	/// </summary>
	public static readonly Mem_ORIGINAL<T> Empty = new(MemBackingStorageType.Empty, null, 0, 0, false);

	/// <summary>
	/// Cached reflection info for accessing List{T}'s internal array field
	/// </summary>
	private static readonly FieldInfo? _listItemsField = typeof(List<T>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);





	/// <summary>
	/// Creates a memory view backed by an array
	/// </summary>
	internal Mem_ORIGINAL(T[] array) : this(new ArraySegment<T>(array), 0, array.Length) { }

	/// <summary>
	/// Creates a memory view backed by a pooled memory owner
	/// </summary>
	internal Mem_ORIGINAL(MemoryOwner_Custom<T> owner, bool isTrueOwner) : this(owner, 0, owner.Length, isTrueOwner) { }

	/// <summary>
	/// Creates a memory view backed by an array segment
	/// </summary>
	internal Mem_ORIGINAL(ArraySegment<T> owner) : this(owner, 0, owner.Count) { }

	/// <summary>
	/// Creates a memory view backed by a List
	/// </summary>
	internal Mem_ORIGINAL(List<T> owner) : this(owner, 0, owner.Count) { }

	/// <summary>
	/// Creates a memory view backed by Memory{T}
	/// </summary>
	internal Mem_ORIGINAL(Memory<T> owner) : this(owner, 0, owner.Length) { }

	/// <summary>
	/// Internal constructor for creating from validated backing storage
	/// </summary>
	internal Mem_ORIGINAL(MemBackingStorageType backingStorageType, object backingStorage, int segmentOffset, int segmentCount, bool isTrueOwner)
	{
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		_segmentOffset = segmentOffset;
		_segmentCount = segmentCount;
		_isTrueOwner = isTrueOwner;
	}

	/// <summary>
	/// Creates a memory view backed by ObjectPool rented array
	/// </summary>
	internal Mem_ORIGINAL(NotNot._internal.ObjectPool.RentedArray<T> rentedArray, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.RentedArray;
		_backingStorage = rentedArray; // Will be boxed
		__.ThrowIfNot(rentedArray.Value != null);
		_segmentOffset = 0;
		_segmentCount = rentedArray.Value.Length;
	}

	/// <summary>
	/// Creates a sliced memory view backed by ObjectPool rented array
	/// </summary>
	internal Mem_ORIGINAL(NotNot._internal.ObjectPool.RentedArray<T> rentedArray, int sliceOffset, int sliceCount, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.RentedArray;
		_backingStorage = rentedArray;
		__.ThrowIfNot(rentedArray.Value != null);
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= rentedArray.Value.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= rentedArray.Value.Length);
		_segmentOffset = sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a memory view backed by ObjectPool rented list
	/// </summary>
	internal Mem_ORIGINAL(NotNot._internal.ObjectPool.Rented<List<T>> rentedList, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.RentedList;
		_backingStorage = rentedList; // Will be boxed
		__.ThrowIfNot(rentedList.Value != null);
		_segmentOffset = 0;
		_segmentCount = rentedList.Value.Count;
	}

	/// <summary>
	/// Creates a sliced memory view backed by ObjectPool rented list
	/// </summary>
	internal Mem_ORIGINAL(NotNot._internal.ObjectPool.Rented<List<T>> rentedList, int sliceOffset, int sliceCount, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.RentedList;
		_backingStorage = rentedList;
		__.ThrowIfNot(rentedList.Value != null);
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= rentedList.Value.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= rentedList.Value.Count);
		_segmentOffset = sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a memory view backed by a single item wrapped in SingleItemStorage
	/// </summary>
	internal Mem_ORIGINAL(T singleItem)
	{
		_isTrueOwner = false; // GC handles SingleItemStorage naturally
		_backingStorageType = MemBackingStorageType.SingleItem;
		_backingStorage = new SingleItemStorage<T>(singleItem);
		_segmentOffset = 0;
		_segmentCount = 1;
	}




	/// <summary>
	/// Creates a sliced memory view from a pooled memory owner
	/// </summary>
	/// <param name="owner">Pooled memory owner</param>
	/// <param name="sliceOffset">Offset within the owner to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	/// <param name="isTrueOwner">if true, it's disposal will return a `MemoryOwner_Custom` to the pool; if false, it's a slice and should not dispose the owner</param>
	internal Mem_ORIGINAL(MemoryOwner_Custom<T> owner, int sliceOffset, int sliceCount, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.MemoryOwner_Custom;
		_backingStorage = owner;
		var ownerArraySegment = owner.DangerousGetArray();
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced memory view from an array segment
	/// </summary>
	/// <param name="ownerArraySegment">Array segment to slice from</param>
	/// <param name="sliceOffset">Offset within the segment to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem_ORIGINAL(ArraySegment<T> ownerArraySegment, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.Array;
		_backingStorage = ownerArraySegment.Array ?? Array.Empty<T>();
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = ownerArraySegment.Offset + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced memory view from an array
	/// </summary>
	/// <param name="ownerArray">Array to slice from</param>
	/// <param name="sliceOffset">Offset within the array to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem_ORIGINAL(T[] ownerArray, int sliceOffset, int sliceCount) : this(new ArraySegment<T>(ownerArray), sliceOffset, sliceCount) { }

	/// <summary>
	/// Creates a sliced memory view from a List
	/// </summary>
	/// <param name="list">List to slice from</param>
	/// <param name="sliceOffset">Offset within the list to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem_ORIGINAL(List<T> list, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.List;
		_backingStorage = list;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= list.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= list.Count);
		_segmentOffset = 0 + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced memory view from Memory{T}
	/// </summary>
	/// <param name="ownerMemory">Memory to slice from</param>
	/// <param name="sliceOffset">Offset within the memory to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem_ORIGINAL(Memory<T> ownerMemory, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.Memory;
		_backingStorage = ownerMemory.Slice(sliceOffset, sliceCount);
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerMemory.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerMemory.Length);
		_segmentOffset = 0;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced memory view from another Mem{T} instance
	/// </summary>
	/// <param name="parentMem">Parent Mem to slice from</param>
	/// <param name="sliceOffset">Offset within the parent to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem_ORIGINAL(Mem_ORIGINAL<T> parentMem, int sliceOffset, int sliceCount)
	{
		_isTrueOwner = false;
		_backingStorageType = parentMem._backingStorageType;
		_backingStorage = parentMem._backingStorage;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Length);
		_segmentOffset = parentMem._segmentOffset + sliceOffset;
		_segmentCount = sliceCount;
	}

	public static RentedMem<T> Clone(Mem_ORIGINAL<T> toClone)
	{
		var copy = Mem.Rent<T>(toClone.Length);
		toClone.Span.CopyTo(copy.Span);
		return copy;
	}

	///// <summary>
	/////    allocate memory from the shared pool.
	/////    If your Type is a reference type or contains references, be sure to use clearOnDispose otherwise you will have
	/////    memory leaks.
	/////    also note that the memory is not cleared by default.
	///// </summary>
	//public static Mem_ORIGINAL<T> Allocate(int size)
	//{
	//	//__.AssertOnce(RuntimeHelpers.IsReferenceOrContainsReferences<T>() == false || clearOnDispose, "alloc of classes via memPool can/will cause leaks");
	//	var mo = MemoryOwner_Custom<T>.Allocate(size, AllocationMode.Clear);
	//	//mo.ClearOnDispose = clearOnDispose;
	//	return new Mem_ORIGINAL<T>(mo, isTrueOwner: true);
	//}

	///// <summary>
	/////    allocate memory from the shared pool and copy the contents of the specified span into it
	///// </summary>
	//public static Mem_ORIGINAL<T> Allocate(ReadOnlySpan<T> span)
	//{
	//	var toReturn = Allocate(span.Length);
	//	span.CopyTo(toReturn.Span);
	//	return toReturn;
	//}

	///// <summary>
	///// Allocates a single-element memory from the pool and assigns the specified value
	///// </summary>
	///// <param name="singleItem">Item to store in the allocated memory</param>
	///// <returns>Pooled memory containing the single item</returns>
	//public static Mem_ORIGINAL<T> AllocateAndAssign(T singleItem)
	//{
	//	var mem = Allocate(1);
	//	mem[0] = singleItem;
	//	return mem;
	//}



	/// <summary>
	/// Creates a new memory view that is a slice of this memory
	/// <para>the original is the "owner" and the underlying storage is deallocated when it's disposed</para>
	/// </summary>
	/// <param name="offset">Starting offset within this memory</param>
	/// <param name="count">Number of elements in the slice</param>
	/// <returns>New memory view representing the slice</returns>
	public Mem_ORIGINAL<T> Slice(int offset, int count)
	{
		var toReturn = new Mem_ORIGINAL<T>(this, offset, count);
		return toReturn;
	}

	/// <summary>
	/// Applies the specified mapping function to each element of this Mem, writing results to the provided output buffer (zero-allocation version)
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this Mem.</param>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
	public void Map<TResult>(Span<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this Mem");
		var thisSpan = Span;
		var toReturnSpan = toReturn;
		for (var i = 0; i < Length; i++)
		{
			ref var r_mappedResult = ref mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = r_mappedResult;
		}
	}

	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public RentedMem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		Map(toReturn, mapFunc);
		return toReturn;
	}

	/// <summary>
	/// Applies the specified mapping function to each element of this Mem, writing results to the provided output buffer (zero-allocation version)
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this Mem.</param>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
	public void Map<TResult>(Span<TResult> toReturn, Func_RefArg<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this Mem");
		var thisSpan = Span;
		var toReturnSpan = toReturn;
		for (var i = 0; i < Length; i++)
		{
			var mappedResult = mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
	}

	/// <summary>
	/// Applies the specified mapping function to each element of this Mem, writing results to the provided output buffer (zero-allocation version)
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this Mem.</param>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
	public void Map<TResult>(Span<TResult> toReturn, Func<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this Mem");
		var thisSpan = Span;
		var toReturnSpan = toReturn;
		for (var i = 0; i < Length; i++)
		{
			var mappedResult = mapFunc(thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
	}

	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public RentedMem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		Map(toReturn, mapFunc);
		return toReturn;
	}
	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public RentedMem<TResult> Map<TResult>(Func<T, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		Map(toReturn, mapFunc);
		return toReturn;
	}

	/// <summary>
	/// Maps two Mem instances in parallel using the specified function, writing results to the provided output buffer (zero-allocation version). All must have the same length.
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this Mem.</param>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
	public void MapWith<TOther, TResult>(Span<TResult> toReturn, Span<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this Mem");
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this Mem");
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
	/// Allocates a new pooled Mem by mapping two Mem instances in parallel using the specified function. Both must have the same length.
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public RentedMem<TResult> MapWith<TOther, TResult>(Span<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(Length);
		MapWith(toReturn, otherToMapWith, mapFunc);
		return toReturn;
	}

	/// <summary>
	/// Maps two Mem instances in parallel using the specified action, modifying elements in place. Both must have the same length.
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Action that processes pairs of elements by reference</param>
	public void MapWith<TOther>(Span<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this Mem");
		var thisSpan = Span;
		var otherSpan = otherToMapWith;//.Span;

		for (var i = 0; i < Length; i++)
		{
			mapFunc(ref thisSpan[i], ref otherSpan[i]);
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
	public async ValueTask BatchMap(Func_RefArg<T, T, bool> isSameBatch, Func<Span<T>, ValueTask> worker)
	{
		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			await worker(Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}



	/// <summary>
	/// Local synchronous scanner that finds the end of a contiguous batch; no awaits here, so using Span{T} is safe
	/// </summary>
	/// <param name="start">Starting index for batch scan</param>
	/// <param name="thisMem">Memory to scan</param>
	/// <param name="isSameBatch">Function determining if adjacent elements belong to same batch</param>
	/// <returns>Exclusive end index of the batch</returns>
	private static int _GetBatchEndExclusive(int start, Mem_ORIGINAL<T> thisMem, Func_RefArg<T, T, bool> isSameBatch)
	{
		var span = thisMem.Span;
		var length = thisMem.Length;

		var end = start + 1;
		while (end < length)
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

	//public async ValueTask BatchMapWith<TOther>(Mem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Func<Mem<T>, Mem<TOther>, ValueTask> worker)
	//{
	//	__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this Mem");

	//	if (Length == 0)
	//	{
	//		return;
	//	}
	//	var batchStart = 0;
	//	while (batchStart < Length)
	//	{
	//		var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
	//		await worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
	//		batchStart = batchEnd;
	//	}
	//}
	/// <summary>
	/// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per range (synchronous version).
	/// Use this to process subgroups without extra allocations.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a Mem slice that references this instance's backing store.</param>
	public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>> worker)
	{
		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Walks contiguous batches with parallel memory and calls worker once per range (synchronous version).
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
	/// <param name="worker">Action executed for each contiguous batch</param>
	public void BatchMapWith<TOther>(Span<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>, Span<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this Mem");

		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

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

	///// <summary>
	/////    beware: the size of the array allocated may be larger than the size requested by this Mem.
	/////    As such, beware if using the backing Array directly.  respect the offset+length described in this segment.
	///// </summary>
	//public ArraySegment<T> DangerousGetArray()
	//{
	//	return _segment;
	//}

	/// <summary>
	/// Gets a Span{T} view over this memory. The span provides direct access to the underlying data.
	/// </summary>
	public Span<T> Span
	{
		get
		{

			switch (_backingStorageType)
			{
				case MemBackingStorageType.Empty:
					return Span<T>.Empty;
				case MemBackingStorageType.MemoryOwner_Custom:
					{
						var owner = (MemoryOwner_Custom<T>)_backingStorage;

						var span = owner.Span;
						return span.Slice(_segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.Array:
					{
						var array = (T[])_backingStorage;
						return new Span<T>(array, _segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.List:
					{
						var list = (List<T>)_backingStorage;
						return CollectionsMarshal.AsSpan(list).Slice(_segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.Memory:
					{
						var memory = (Memory<T>)_backingStorage;
						return memory.Span.Slice(_segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.RentedArray:
					{
						var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
						return new Span<T>(rentedArray.Value, _segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.RentedList:
					{
						var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
						return CollectionsMarshal.AsSpan(rentedList.Value).Slice(_segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.SingleItem:
					{
						if (_segmentCount == 0) return Span<T>.Empty;
						var storage = (SingleItemStorage<T>)_backingStorage;
						return MemoryMarshal.CreateSpan(ref storage.Value, 1);
					}
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	//public Memory<T> Memory =>
	//	//return new Memory<T>(_array, _offset, length);
	//	_segment.AsMemory();

	/// <summary>
	/// Gets the number of slots in this memory view
	/// </summary>
	public int Length => _segmentCount;


	/// <summary>
	/// if owned by a pool, Disposes so the backing array can be recycled. DANGER: any other references to the same backing pool slot are also disposed at this time!
	/// <para>For non-pooled, just makes this struct disposed, not touching the backing collection.</para>
	/// <para>NOT-REENTRY SAFE: Disposal only impacts MemoryOwner backing stores, but when called, the MemoryOwner will be disposed, which will impact other Mem's using the same MemoryOwner (such as a .Slice()). You can instead not dispose, and let the GC recycle when the MemoryOwner goes out of scope.</para>
	/// </summary>
	public void Dispose()
	{
		//only do work if backed by an owner, and if so, recycle
		switch (_backingStorageType)
		{
			case MemBackingStorageType.MemoryOwner_Custom:
				{
					if (_isTrueOwner is false)
					{
						break;
					}
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertNotNull(owner, "storage is null, was it already disposed?");
					if (owner is not null)
					{
						owner.Dispose();
					}
				}
				break;
			case MemBackingStorageType.RentedArray:
				{
					if (_isTrueOwner is false)
					{
						break;
					}
					__.AssertNotNull(_backingStorage, "storage is null, was it already disposed?");

					var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
					rentedArray.Dispose();
				}
				break;
			case MemBackingStorageType.RentedList:
				{
					if (_isTrueOwner is false)
					{
						break;
					}
					__.AssertNotNull(_backingStorage, "storage is null, was it already disposed?");

					var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
					rentedList.Dispose();
				}
				break;
			case MemBackingStorageType.Array:
			case MemBackingStorageType.List:
			case MemBackingStorageType.Memory:
			case MemBackingStorageType.SingleItem:
				//do nothing, let the GC handle backing.
				break;
			case MemBackingStorageType.Empty:
				//disposal of non-initialized/used storage.  ignore
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}


	/// <summary>
	/// Asserts that this memory has not been disposed. Only executes in CHECKED builds.
	/// </summary>
	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		__.AssertNotNull(_backingStorage, "storage is null, should never be");
		switch (_backingStorageType)
		{
			case MemBackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertIfNot(owner.IsDisposed is false, "storage is disposed, cannot use");
				}
				break;
			case MemBackingStorageType.Array:
			case MemBackingStorageType.List:
			case MemBackingStorageType.Memory:
			case MemBackingStorageType.SingleItem:
				//do nothing, let the GC handle backing.
				break;
			case MemBackingStorageType.RentedArray:
			case MemBackingStorageType.RentedList:
				// Rented wrappers become null when disposed
				__.AssertIfNot(_backingStorage is not null, "storage is disposed, cannot use");
				break;

			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Gets a reference to the element at the specified index
	/// </summary>
	/// <param name="index">Zero-based index of the element</param>
	/// <returns>Reference to the element at the specified index</returns>
	public ref T this[int index]
	{
		get
		{
			AssertNotDisposed();
			return ref Span[index];
			//__.GetLogger()._EzError(index >= 0 && index < length);
			//return ref _array[_offset + index];
		}
	}

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	/// <returns>Span enumerator</returns>
	public Span<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	//public IEnumerable<T> Enumerable => Span;

	/// <summary>
	/// DANGEROUS: Gets the underlying array segment. The array may be larger than this view and may be pooled. Use with caution.
	/// </summary>
	/// <returns>Array segment representing this memory's backing storage</returns>
	public ArraySegment<T> DangerousGetArray()
	{
		AssertNotDisposed();

		switch (_backingStorageType)
		{
			case MemBackingStorageType.Empty:
				return Array.Empty<T>();
			case MemBackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					var ownerSegment = owner.DangerousGetArray();
					__.ThrowIfNot(ownerSegment.Array is not null, "owner must expose an array");
					__.ThrowIfNot(_segmentOffset >= 0 && _segmentOffset + _segmentCount <= ownerSegment.Count);
					var absoluteOffset = ownerSegment.Offset + _segmentOffset;
					return new ArraySegment<T>(ownerSegment.Array, absoluteOffset, _segmentCount);
				}
			case MemBackingStorageType.Array:
				{
					var array = (T[])_backingStorage;
					return new ArraySegment<T>(array, _segmentOffset, _segmentCount);
				}
			case MemBackingStorageType.List:
				{
					var list = (List<T>)_backingStorage;
					__.ThrowIfNot(_segmentOffset + _segmentCount <= list.Count);
					var items = _GetListItemsArray(list);
					__.ThrowIfNot(_segmentOffset + _segmentCount <= items.Length);
					return new ArraySegment<T>(items, _segmentOffset, _segmentCount);
				}
			case MemBackingStorageType.Memory:
				{
					var memory = (Memory<T>)_backingStorage;
					if (MemoryMarshal.TryGetArray((ReadOnlyMemory<T>)memory, out var arraySegment) && arraySegment.Array is not null)
					{
						var offset = arraySegment.Offset + _segmentOffset;
						__.ThrowIfNot(offset >= arraySegment.Offset && offset + _segmentCount <= arraySegment.Offset + arraySegment.Count);
						return new ArraySegment<T>(arraySegment.Array, offset, _segmentCount);
					}

					throw __.Throw("Cannot expose array for memory that is not array-backed");
				}
			case MemBackingStorageType.RentedArray:
				{
					var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
					var array = rentedArray.Value;
					__.ThrowIfNot(array is not null, "RentedArray must have non-null array");
					return new ArraySegment<T>(array, _segmentOffset, _segmentCount);
				}
			case MemBackingStorageType.RentedList:
				{
					var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
					var list = rentedList.Value;
					__.ThrowIfNot(list is not null, "RentedList must have non-null list");
					__.ThrowIfNot(_segmentOffset + _segmentCount <= list.Count);
					var items = _GetListItemsArray(list);
					__.ThrowIfNot(_segmentOffset + _segmentCount <= items.Length);
					return new ArraySegment<T>(items, _segmentOffset, _segmentCount);
				}
			case MemBackingStorageType.SingleItem:
				throw __.Throw("SingleItem backing storage does not support DangerousGetArray - use Span property instead");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}


	/// <summary>
	/// Uses reflection to access the internal array backing a List{T}
	/// </summary>
	/// <param name="list">List to extract internal array from</param>
	/// <returns>Internal array backing the list</returns>
	private static T[] _GetListItemsArray(List<T> list)
	{
		if (_listItemsField is null)
		{
			throw __.Throw("List<T> layout not supported; backing _items field missing");
		}

		return (T[]?)_listItemsField.GetValue(list) ?? Array.Empty<T>();
	}

	/// <summary>
	/// Returns a string representation of this memory view showing type and count
	/// </summary>
	/// <returns>String in format "Mem&lt;Type&gt;[Count]"</returns>
	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{Length}]";
	}
}
