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
using NotNot.Collections.Advanced;

namespace NotNot;

/// <summary>
/// Identifies which type of backing store is being used by Mem{T} and ReadMem{T}
/// </summary>
internal enum MemBackingStorageType
{
	/// <summary>
	/// not initialized.  an error if being used.
	/// </summary>
	None = 0,

	/// <summary>
	/// if pooled (Mem.Alloc()), this will be set. a reference to the pooled location so it can be recycled
	/// while this will naturally be GC'd when all referencing Mem{T}'s go out-of-scope, you can manually do so by calling Dispose or the using pattern
	/// </summary>
	MemoryOwner_Custom,
	/// <summary>
	/// manually constructed Mem using your own List. not disposed of when out-of-scope
	/// </summary>
	List,
	/// <summary>
	/// manually constructed Mem using your own Array. not disposed of when out-of-scope
	/// </summary>
	Array,
	/// <summary>
	/// manually constructed Mem using your own Memory. not disposed of when out-of-scope
	/// </summary>
	Memory,
}

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
		return Mem<T>.Wrap(backingStore);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(T[] array)
	{
		return Mem<T>.Wrap(array);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(Memory<T> memory)
	{
		return Mem<T>.Wrap(memory);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(List<T> list)
	{
		return Mem<T>.Wrap(list);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> AllocateAndAssign<T>(T singleItem)
	{
		return Mem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(int count)
	{
		return Mem<T>.Alloc(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return Mem<T>.Allocate(span);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(Mem<T> writeMem)
	{
		return writeMem;
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(ReadMem<T> readMem)
	{
		return Mem<T>.Wrap(readMem);
	}
}

/// <summary>
///    helpers to allocate a ReadMem instance
/// </summary>
public static class ReadMem
{
	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(ArraySegment<T> backingStore)
	{
		return ReadMem<T>.Wrap(backingStore);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(T[] array)
	{
		return ReadMem<T>.Wrap(array);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(Memory<T> memory)
	{
		return ReadMem<T>.Wrap(memory);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(List<T> list)
	{
		return ReadMem<T>.Wrap(list);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> AllocateAndAssign<T>(T singleItem)
	{
		return ReadMem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> Allocate<T>(int count)
	{
		return ReadMem<T>.Allocate(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return ReadMem<T>.Allocate(span);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(Mem<T> writeMem)
	{
		return ReadMem<T>.Wrap(writeMem);
	}
}

/// <summary>
/// A universal, write-capable view into a wrapped array/list/memory backing storage, with support for pooled allocation (renting) for temporary collections (see <see cref="Alloc(int)"/>).
/// Supports implicit casting from array/list/memory along with explicit via Mem.Wrap() methods.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
public readonly struct Mem<T> : IDisposable
{
	//implicit operators
	public static implicit operator Mem<T>(T[] array) => new Mem<T>(array);
	public static implicit operator Mem<T>(ArraySegment<T> arraySegment) => new Mem<T>(arraySegment);
	public static implicit operator Mem<T>(List<T> list) => new Mem<T>(list);
	public static implicit operator Mem<T>(Memory<T> memory) => new Mem<T>(memory);
	public static implicit operator Mem<T>(MemoryOwner_Custom<T> owner) => new Mem<T>(owner);




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
	public static readonly Mem<T> Empty = new(ArraySegment<T>.Empty, 0, 0);

	/// <summary>
	/// Cached reflection info for accessing List{T}'s internal array field
	/// </summary>
	private static readonly FieldInfo? _listItemsField = typeof(List<T>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);





	/// <summary>
	/// Creates a memory view backed by an array
	/// </summary>
	internal Mem(T[] array) : this(new ArraySegment<T>(array), 0, array.Length) { }

	/// <summary>
	/// Creates a memory view backed by a pooled memory owner
	/// </summary>
	internal Mem(MemoryOwner_Custom<T> owner) : this(owner, 0, owner.Length) { }

	/// <summary>
	/// Creates a memory view backed by an array segment
	/// </summary>
	internal Mem(ArraySegment<T> owner) : this(owner, 0, owner.Count) { }

	/// <summary>
	/// Creates a memory view backed by a List
	/// </summary>
	internal Mem(List<T> owner) : this(owner, 0, owner.Count) { }

	/// <summary>
	/// Creates a memory view backed by Memory{T}
	/// </summary>
	internal Mem(Memory<T> owner) : this(owner, 0, owner.Length) { }




	/// <summary>
	/// Creates a sliced memory view from a pooled memory owner
	/// </summary>
	/// <param name="owner">Pooled memory owner</param>
	/// <param name="sliceOffset">Offset within the owner to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem(MemoryOwner_Custom<T> owner, int sliceOffset, int sliceCount)
	{
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
	internal Mem(ArraySegment<T> ownerArraySegment, int sliceOffset, int sliceCount)
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
	internal Mem(T[] ownerArray, int sliceOffset, int sliceCount) : this(new ArraySegment<T>(ownerArray), sliceOffset, sliceCount) { }

	/// <summary>
	/// Creates a sliced memory view from a List
	/// </summary>
	/// <param name="list">List to slice from</param>
	/// <param name="sliceOffset">Offset within the list to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem(List<T> list, int sliceOffset, int sliceCount)
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
	internal Mem(Memory<T> ownerMemory, int sliceOffset, int sliceCount)
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
	internal Mem(Mem<T> parentMem, int sliceOffset, int sliceCount)
	{
		_backingStorageType = parentMem._backingStorageType;
		_backingStorage = parentMem._backingStorage;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Length);
		_segmentOffset = parentMem._segmentOffset + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced writable memory view from a ReadMem{T} instance
	/// </summary>
	/// <param name="parentMem">Parent ReadMem to slice from</param>
	/// <param name="sliceOffset">Offset within the parent to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal Mem(ReadMem<T> parentMem, int sliceOffset, int sliceCount)
	{
		_backingStorageType = parentMem._backingStorageType;
		_backingStorage = parentMem._backingStorage;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Length);
		_segmentOffset = parentMem._segmentOffset + sliceOffset;
		_segmentCount = sliceCount;
	}



	/// <summary>
	///    allocate memory from the shared pool.
	///    If your Type is a reference type or contains references, be sure to use clearOnDispose otherwise you will have
	///    memory leaks.
	///    also note that the memory is not cleared by default.
	/// </summary>
	public static Mem<T> Alloc(int size)
	{
		//__.AssertOnce(RuntimeHelpers.IsReferenceOrContainsReferences<T>() == false || clearOnDispose, "alloc of classes via memPool can/will cause leaks");
		var mo = MemoryOwner_Custom<T>.Allocate(size, AllocationMode.Clear);
		//mo.ClearOnDispose = clearOnDispose;
		return new Mem<T>(mo);
	}

	/// <summary>
	///    allocate memory from the shared pool and copy the contents of the specified span into it
	/// </summary>
	public static Mem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Alloc(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	/// <summary>
	/// Allocates a single-element memory from the pool and assigns the specified value
	/// </summary>
	/// <param name="singleItem">Item to store in the allocated memory</param>
	/// <returns>Pooled memory containing the single item</returns>
	public static Mem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Alloc(1);
		mem[0] = singleItem;
		return mem;
	}

	/// <summary>
	/// Creates a non-pooled memory view using an existing array
	/// </summary>
	/// <param name="array">Array to wrap</param>
	/// <returns>Memory view over the entire array</returns>
	public static Mem<T> Wrap(T[] array)
	{
		return new Mem<T>(new ArraySegment<T>(array));
	}


	public static Mem<T> Wrap(List<T> list)
	{
		return new Mem<T>(list);
	}
	public static Mem<T> Wrap(Memory<T> memory)
	{
		return new Mem<T>(memory);
	}


	/// <summary>
	/// Creates a non-pooled memory view using a slice of an existing array
	/// </summary>
	/// <param name="array">Array to wrap</param>
	/// <param name="offset">Starting index in the array</param>
	/// <param name="count">Number of elements</param>
	/// <returns>Memory view over the specified array slice</returns>
	public static Mem<T> Wrap(T[] array, int offset, int count)
	{
		return new Mem<T>(new ArraySegment<T>(array, offset, count));
	}

	/// <summary>
	/// Creates a non-pooled memory view using an existing array segment
	/// </summary>
	/// <param name="backingStore">Array segment to wrap</param>
	/// <returns>Memory view over the array segment</returns>
	public static Mem<T> Wrap(ArraySegment<T> backingStore)
	{
		return new Mem<T>(backingStore);
	}

	/// <summary>
	/// Creates a memory view using an existing pooled memory owner
	/// </summary>
	/// <param name="MemoryOwnerNew">Pooled memory owner to wrap</param>
	/// <returns>Memory view over the pooled memory</returns>
	internal static Mem<T> Wrap(MemoryOwner_Custom<T> MemoryOwnerNew)
	{
		return new Mem<T>(MemoryOwnerNew);
	}

	/// <summary>
	/// Creates a writable memory view from a ReadMem{T}
	/// </summary>
	/// <param name="readMem">Read-only memory to convert</param>
	/// <returns>Writable memory view</returns>
	public static Mem<T> Wrap(ReadMem<T> readMem)
	{
		return readMem.AsWriteMem();
	}


	/// <summary>
	/// Creates a new memory view that is a slice of this memory
	/// </summary>
	/// <param name="offset">Starting offset within this memory</param>
	/// <param name="count">Number of elements in the slice</param>
	/// <returns>New memory view representing the slice</returns>
	public Mem<T> Slice(int offset, int count)
	{
		var toReturn = new Mem<T>(this, offset, count);
		return toReturn;
	}

	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public Mem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = Mem<TResult>.Alloc(Length);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Length; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public Mem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = Mem<TResult>.Alloc(Length);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Length; i++)
		{
			var mappedResult = mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}
	/// <summary>
	/// Allocates a new pooled Mem by mapping two Mem instances in parallel using the specified function. Both must have the same length.
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public Mem<TResult> MapWith<TOther, TResult>(Mem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Length, "otherToMapWith must be the same length as this Mem");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;
		var toReturn = Mem<TResult>.Alloc(Length);
		var toReturnSpan = toReturn.Span;

		for (var i = 0; i < Length; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	/// <summary>
	/// Maps two Mem instances in parallel using the specified action, modifying elements in place. Both must have the same length.
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Action that processes pairs of elements by reference</param>
	public void MapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Length, "otherToMapWith must be the same length as this Mem");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;

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
	public async ValueTask BatchMap(Func_RefArg<T, T, bool> isSameBatch, Func<Mem<T>, ValueTask> worker)
	{
		if (this.Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			await worker(this.Slice(batchStart, batchEnd - batchStart));
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
	private static int _GetBatchEndExclusive(int start, Mem<T> thisMem, Func_RefArg<T, T, bool> isSameBatch)
	{
		var span = thisMem.Span;
		var length = thisMem.Length;

		var end = start + 1;
		while (end < length)
		{
			ref var previous = ref span[end - 1];
			ref var current = ref span[end];
			if (!isSameBatch(ref previous, ref current))
			{
				break;
			}
			end++;
		}
		return end;
	}

	public async ValueTask BatchMapWith<TOther>(Mem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Func<Mem<T>, Mem<TOther>, ValueTask> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Length, "otherToMapWith must be the same length as this Mem");

		if (this.Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			await worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}
	/// <summary>
	/// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per range (synchronous version).
	/// Use this to process subgroups without extra allocations.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a Mem slice that references this instance's backing store.</param>
	public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>> worker)
	{
		if (this.Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(this.Slice(batchStart, batchEnd - batchStart));
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
	public void BatchMapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>, UnifiedMem<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Length, "otherToMapWith must be the same length as this Mem");

		if (this.Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Length)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Creates a deep copy of this Mem with contents copied to new pool-backed storage
	/// </summary>
	/// <returns>New pooled memory containing a copy of this memory's contents</returns>
	public Mem<T> Clone()
	{
		var copy = Mem<T>.Alloc(Length);
		this.Span.CopyTo(copy.Span);
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
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertNotNull(owner, "storage is null, was it already disposed?");
					if (owner is not null)
					{
						owner.Dispose();
					}
				}
				break;
			case MemBackingStorageType.Array:
			case MemBackingStorageType.List:
			case MemBackingStorageType.Memory:
				//do nothing, let the GC handle backing.
				break;
			case MemBackingStorageType.None:
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
				//do nothing, let the GC handle backing.
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
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Converts this writable memory view to a read-only memory view
	/// </summary>
	/// <returns>Read-only memory view over the same backing storage</returns>
	public ReadMem<T> AsReadMem()
	{
		AssertNotDisposed();
		return new ReadMem<T>(this, 0, _segmentCount);
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

/// <summary>
/// A universal, read-only view into a wrapped array/list/memory backing storage, with support for pooled allocation (renting) for temporary collections.
/// Supports implicit casting from array/list/memory along with explicit via ReadMem.Wrap() methods.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
//[DebuggerTypeProxy(typeof(NotNot.Bcl.Collections.Advanced.CollectionDebugView<>))]
//[DebuggerDisplay("{ToString(),raw}")]
//[DebuggerDisplay("{ToString(),nq}")]
public readonly struct ReadMem<T> : IDisposable
{
	//implicit operators
	public static implicit operator ReadMem<T>(T[] array) => new ReadMem<T>(array);
	public static implicit operator ReadMem<T>(ArraySegment<T> arraySegment) => new ReadMem<T>(arraySegment);
	public static implicit operator ReadMem<T>(List<T> list) => new ReadMem<T>(list);
	public static implicit operator ReadMem<T>(Memory<T> memory) => new ReadMem<T>(memory);
	public static implicit operator ReadMem<T>(MemoryOwner_Custom<T> owner) => new ReadMem<T>(owner);
	public static implicit operator ReadMem<T>(Mem<T> mem) => mem.AsReadMem();



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
	/// Cached reflection info for accessing List{T}'s internal array field
	/// </summary>
	private static readonly FieldInfo? _listItemsField = typeof(List<T>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

	/// <summary>
	/// Represents an empty memory view with zero elements
	/// </summary>
	public static readonly ReadMem<T> Empty = new(ArraySegment<T>.Empty, 0, 0);

	/// <summary>
	/// Creates a read-only memory view backed by a pooled memory owner
	/// </summary>
	internal ReadMem(MemoryOwner_Custom<T> owner) : this(owner, 0, owner.Length) { }

	/// <summary>
	/// Creates a read-only memory view backed by an array segment
	/// </summary>
	internal ReadMem(ArraySegment<T> owner) : this(owner, 0, owner.Count) { }

	/// <summary>
	/// Creates a read-only memory view backed by an array
	/// </summary>
	internal ReadMem(T[] array) : this(new ArraySegment<T>(array), 0, array.Length) { }

	/// <summary>
	/// Creates a read-only memory view backed by a List
	/// </summary>
	internal ReadMem(List<T> owner) : this(owner, 0, owner.Count) { }

	/// <summary>
	/// Creates a read-only memory view backed by Memory{T}
	/// </summary>
	internal ReadMem(Memory<T> owner) : this(owner, 0, owner.Length) { }

	/// <summary>
	/// Creates a sliced read-only memory view from a pooled memory owner
	/// </summary>
	/// <param name="owner">Pooled memory owner</param>
	/// <param name="sliceOffset">Offset within the owner to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(MemoryOwner_Custom<T> owner, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.MemoryOwner_Custom;
		_backingStorage = owner;
		var ownerArraySegment = owner.DangerousGetArray();
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced read-only memory view from an array segment
	/// </summary>
	/// <param name="ownerArraySegment">Array segment to slice from</param>
	/// <param name="sliceOffset">Offset within the segment to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(ArraySegment<T> ownerArraySegment, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.Array;
		_backingStorage = ownerArraySegment.Array ?? Array.Empty<T>();
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = ownerArraySegment.Offset + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced read-only memory view from an array
	/// </summary>
	/// <param name="ownerArray">Array to slice from</param>
	/// <param name="sliceOffset">Offset within the array to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(T[] ownerArray, int sliceOffset, int sliceCount) : this(new ArraySegment<T>(ownerArray), sliceOffset, sliceCount) { }

	/// <summary>
	/// Creates a sliced read-only memory view from a List
	/// </summary>
	/// <param name="list">List to slice from</param>
	/// <param name="sliceOffset">Offset within the list to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(List<T> list, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.List;
		_backingStorage = list;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= list.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= list.Count);
		_segmentOffset = 0 + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced read-only memory view from Memory{T}
	/// </summary>
	/// <param name="ownerMemory">Memory to slice from</param>
	/// <param name="sliceOffset">Offset within the memory to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(Memory<T> ownerMemory, int sliceOffset, int sliceCount)
	{
		_backingStorageType = MemBackingStorageType.Memory;
		_backingStorage = ownerMemory.Slice(sliceOffset, sliceCount);
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerMemory.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerMemory.Length);
		_segmentOffset = 0;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced read-only memory view from another ReadMem{T} instance
	/// </summary>
	/// <param name="parentMem">Parent ReadMem to slice from</param>
	/// <param name="sliceOffset">Offset within the parent to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(ReadMem<T> parentMem, int sliceOffset, int sliceCount)
	{
		_backingStorageType = parentMem._backingStorageType;
		_backingStorage = parentMem._backingStorage;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Length);
		_segmentOffset = parentMem._segmentOffset + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Creates a sliced read-only memory view from a Mem{T} instance
	/// </summary>
	/// <param name="parentMem">Parent Mem to slice from</param>
	/// <param name="sliceOffset">Offset within the parent to start</param>
	/// <param name="sliceCount">Number of elements in the slice</param>
	internal ReadMem(Mem<T> parentMem, int sliceOffset, int sliceCount)
	{
		_backingStorageType = parentMem._backingStorageType;
		_backingStorage = parentMem._backingStorage;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Length);
		_segmentOffset = parentMem._segmentOffset + sliceOffset;
		_segmentCount = sliceCount;
	}


	/// <summary>
	///    allocate memory from the shared pool.
	///    If your Type is a reference type or contains references, be sure to use clearOnDispose otherwise you will have
	///    memory leaks.
	///    also note that the memory is not cleared by default.
	/// </summary>
	public static ReadMem<T> Allocate(int size)
	{
		//__.AssertOnce(RuntimeHelpers.IsReferenceOrContainsReferences<T>() == false || , "alloc of classes via memPool can/will cause leaks");
		var mo = MemoryOwner_Custom<T>.Allocate(size, AllocationMode.Clear);
		//mo.ClearOnDispose = clearOnDispose;
		return new ReadMem<T>(mo);
	}

	/// <summary>
	///    allocate memory from the shared pool and copy the contents of the specified span into it
	/// </summary>
	public static ReadMem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.AsWriteSpan());
		return toReturn;
	}

	public static ReadMem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Mem<T>.Alloc(1);
		mem[0] = singleItem;
		return Wrap(mem);
	}

	public static ReadMem<T> Wrap(T[] array)
	{
		return new ReadMem<T>(new ArraySegment<T>(array));
	}


	public static ReadMem<T> Wrap(List<T> list)
	{
		return new ReadMem<T>(list);
	}
	public static ReadMem<T> Wrap(Memory<T> memory)
	{
		return new ReadMem<T>(memory);
	}

	public static ReadMem<T> Wrap(T[] array, int offset, int count)
	{
		return new ReadMem<T>(new ArraySegment<T>(array, offset, count));
	}

	public static ReadMem<T> Wrap(ArraySegment<T> backingStore)
	{
		return new ReadMem<T>(backingStore);
	}

	internal static ReadMem<T> Wrap(MemoryOwner_Custom<T> MemoryOwnerNew)
	{
		return new ReadMem<T>(MemoryOwnerNew);
	}

	public static ReadMem<T> Wrap(Mem<T> mem)
	{
		return mem.AsReadMem();
	}

	/// <summary>
	/// Creates a new memory view that is a slice of this read-only memory, returned as writable Mem{T}
	/// </summary>
	/// <param name="offset">Starting offset within this memory</param>
	/// <param name="count">Number of elements in the slice</param>
	/// <returns>New writable memory view representing the slice</returns>
	public ReadMem<T> Slice(int offset, int count)
	{
		return new ReadMem<T>(this, offset, count);
	}


	/// <summary>
	/// DANGEROUS: Gets the underlying array segment. The array may be larger than this view and may be pooled. Use with caution.
	/// </summary>
	/// <returns>Array segment representing this memory's backing storage</returns>
	public ArraySegment<T> DangerousGetArray()
	{
		AssertNotDisposed();

		switch (_backingStorageType)
		{
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
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Gets a ReadOnlySpan{T} view over this memory. The span provides direct read-only access to the underlying data.
	/// </summary>
	public ReadOnlySpan<T> Span
	{
		get
		{
			switch (_backingStorageType)
			{
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
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	/// <summary>
	/// Gets a writable Span{T} view over this read-only memory. Use with caution as this bypasses read-only semantics.
	/// </summary>
	/// <returns>Writable span over the backing storage</returns>
	public Span<T> AsWriteSpan()
	{
		switch (_backingStorageType)
		{
			case MemBackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					return owner.Span.Slice(_segmentOffset, _segmentCount);
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
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Gets a Memory{T} view over this read-only memory
	/// </summary>
	public Memory<T> Memory
	{
		get
		{
			switch (_backingStorageType)
			{
				case MemBackingStorageType.MemoryOwner_Custom:
					{
						var owner = (MemoryOwner_Custom<T>)_backingStorage;
						return owner.Memory.Slice(_segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.Array:
					{
						var array = (T[])_backingStorage;
						return new Memory<T>(array, _segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.List:
					{
						// Memory<T> doesn't support List<T> directly, convert via array
						var items = _GetListItemsArray((List<T>)_backingStorage);
						return new Memory<T>(items, _segmentOffset, _segmentCount);
					}
				case MemBackingStorageType.Memory:
					{
						var memory = (Memory<T>)_backingStorage;
						return memory.Slice(_segmentOffset, _segmentCount);
					}
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}


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
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertNotNull(owner, "storage is null, was it already disposed?");
					if (owner is not null)
					{
						owner.Dispose();
					}
				}
				break;
			case MemBackingStorageType.Array:
			case MemBackingStorageType.List:
			case MemBackingStorageType.Memory:
				//do nothing, let the GC handle backing.
				break;
			case MemBackingStorageType.None:
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
				//do nothing, let the GC handle backing.
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Gets the value of the element at the specified index
	/// </summary>
	/// <param name="index">Zero-based index of the element</param>
	/// <returns>Value of the element at the specified index</returns>
	public T this[int index]
	{
		get
		{
			AssertNotDisposed();
			return Span[index];
		}
	}

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	/// <returns>ReadOnlySpan enumerator</returns>
	public ReadOnlySpan<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	/// <summary>
	/// Gets an enumerable view of this memory
	/// </summary>
	public IEnumerable<T> Enumerable
	{
		get
		{
			// Convert to array for IEnumerable compatibility
			var result = new T[_segmentCount];
			Span.CopyTo(result);
			return result;
		}
	}

	/// <summary>
	/// Converts this read-only memory view to a writable memory view
	/// </summary>
	/// <returns>Writable memory view over the same backing storage</returns>
	public Mem<T> AsWriteMem()
	{
		AssertNotDisposed();
		return new Mem<T>(this, 0, _segmentCount);
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
	/// <returns>String in format "ReadMem&lt;Type&gt;[Count]"</returns>
	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{Length}]";
	}
}

/// <summary>
/// Identifies which type of backing store is being used by RefMem{T}
/// </summary>
internal enum RefMemBackingStorageType
{
	/// <summary>
	/// Stack-allocated Span{T} (zero GC pressure)
	/// </summary>
	Span = 0,

	/// <summary>
	/// Heap/pooled Mem{T} (low GC pressure, flexible backing)
	/// </summary>
	Mem,

	/// <summary>
	/// Pooled SpanGuard{T} with dispose protection (zero GC pressure, auto-return to pool)
	/// </summary>
	SpanGuard,
}

/// <summary>
/// A ref struct that provides unified Mem-like API for both stack-allocated Span and heap/pooled Mem scenarios.
/// Can wrap either a Span{T} for zero-allocation stack usage, or a Mem{T} for heap/pooled usage.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
/// <remarks>
/// USAGE MODES:
/// - Span Mode: Wraps Span{T} for pure stack allocation (zero GC pressure)
/// - Mem Mode: Wraps Mem{T} for heap/pooled allocation (delegates all operations)
///
/// SPAN MODE LIMITATIONS:
/// - DangerousGetArray(): NotSupportedException (span may not be array-backed)
/// - Clone(): NotSupportedException (would allocate, defeats stack-only purpose)
/// - Map/MapWith(): NotSupportedException (allocates new Mem, defeats purpose)
/// - BatchMap/BatchMapWith(): NotSupportedException (async incompatible with ref struct lifetime)
/// - AsReadMem(): NotSupportedException (cannot create ReadMem from raw Span)
///
/// EXAMPLES:
/// // Span mode (stack allocation)
/// Span{int} buffer = stackalloc int[128];
/// RefMem{int} refMem = buffer;
/// refMem[0] = 42;
///
/// // Mem mode (heap/pooled)
/// var mem = Mem.Allocate{int}(128);
/// RefMem{int} refMem = mem;
/// refMem[0] = 42;
/// refMem.Dispose(); // Returns to pool
/// </remarks>
public ref struct UnifiedMem<T> : IDisposable
{
	/// <summary>
	/// Span storage when in Span mode
	/// </summary>
	private Span<T> _span;

	/// <summary>
	/// Mem storage when in Mem mode
	/// </summary>
	private Mem<T> _mem;

	/// <summary>
	/// SpanGuard storage when in SpanGuard mode
	/// </summary>
	private ZeroAllocMem<T> _spanGuard;

	/// <summary>
	/// Identifies which backing storage type is active
	/// </summary>
	private readonly RefMemBackingStorageType _backingStorageType;

#if CHECKED
	/// <summary>
	/// Dispose guard to ensure proper cleanup when wrapping SpanGuard (CHECKED mode only)
	/// </summary>
	private DisposeGuard _disposeGuard;
#endif

	/// <summary>
	/// Creates a RefMem wrapping a Span (stack mode)
	/// </summary>
	/// <param name="span">Span to wrap (typically stackalloc)</param>
	public UnifiedMem(Span<T> span)
	{
		_span = span;
		_mem = default;
		_spanGuard = default;
		_backingStorageType = RefMemBackingStorageType.Span;
#if CHECKED
		_disposeGuard = default;
#endif
	}

	/// <summary>
	/// Creates a RefMem wrapping a Mem (heap/pooled mode)
	/// </summary>
	/// <param name="mem">Mem to wrap</param>
	public UnifiedMem(Mem<T> mem)
	{
		_span = default;
		_mem = mem;
		_spanGuard = default;
		_backingStorageType = RefMemBackingStorageType.Mem;
#if CHECKED
		_disposeGuard = default;
#endif
	}
	/// <summary>
	/// Creates a RefMem wrapping a SpanGuard (pooled with dispose protection)
	/// </summary>
	/// <param name="spanGuard">SpanGuard to wrap</param>
	public UnifiedMem(ZeroAllocMem<T> spanGuard)
	{
		_span = default;
		_mem = default;
		_spanGuard = spanGuard;
		_backingStorageType = RefMemBackingStorageType.SpanGuard;
#if CHECKED
		_disposeGuard = new();
#endif
	}


	/// <summary>
	/// Implicit conversion from Span to RefMem (stack mode)
	/// </summary>
	public static implicit operator UnifiedMem<T>(Span<T> span) => new UnifiedMem<T>(span);

	/// <summary>
	/// Implicit conversion from Mem to RefMem (heap/pooled mode)
	/// </summary>
	public static implicit operator UnifiedMem<T>(Mem<T> mem) => new UnifiedMem<T>(mem);

	/// <summary>
	/// Implicit conversion from SpanGuard to RefMem (pooled with dispose protection)
	/// </summary>
	public static implicit operator UnifiedMem<T>(ZeroAllocMem<T> spanGuard) => new UnifiedMem<T>(spanGuard);

	/// <summary>
	/// Gets a Span view over this memory
	/// </summary>
	public Span<T> Span
	{
		get
		{
			switch (_backingStorageType)
			{
				case RefMemBackingStorageType.Span:
					return _span;
				case RefMemBackingStorageType.Mem:
					return _mem.Span;
				case RefMemBackingStorageType.SpanGuard:
					return _spanGuard.Span;
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	/// <summary>
	/// Gets the number of slots in this memory view
	/// </summary>
	public int Length
	{
		get
		{
			switch (_backingStorageType)
			{
				case RefMemBackingStorageType.Span:
					return _span.Length;
				case RefMemBackingStorageType.Mem:
					return _mem.Length;
				case RefMemBackingStorageType.SpanGuard:
					return _spanGuard.Count;
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	///// <summary>
	///// Gets the number of elements in this memory view. Obsolete: use Count instead.
	///// </summary>
	//[Obsolete("use .Count")]
	//public int Length
	//{
	//	get
	//	{
	//		switch (_backingStorageType)
	//		{
	//			case RefMemBackingStorageType.Span:
	//				return _span.Length;
	//			case RefMemBackingStorageType.Mem:
	//				return _mem.Length;
	//			case RefMemBackingStorageType.SpanGuard:
	//				return _spanGuard.Length;
	//			default:
	//				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
	//		}
	//	}
	//}

	/// <summary>
	/// Gets a reference to the element at the specified index
	/// </summary>
	/// <param name="index">Zero-based index of the element</param>
	/// <returns>Reference to the element at the specified index</returns>
	public ref T this[int index]
	{
		get
		{
			switch (_backingStorageType)
			{
				case RefMemBackingStorageType.Span:
					return ref _span[index];
				case RefMemBackingStorageType.Mem:
					return ref _mem[index];
				case RefMemBackingStorageType.SpanGuard:
					return ref _spanGuard[index];
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	/// <summary>
	/// Creates a new memory view that is a slice of this memory
	/// </summary>
	/// <param name="offset">Starting offset within this memory</param>
	/// <param name="count">Number of elements in the slice</param>
	/// <returns>New memory view representing the slice</returns>
	public UnifiedMem<T> Slice(int offset, int count)
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				return new UnifiedMem<T>(_span.Slice(offset, count));
			case RefMemBackingStorageType.Mem:
				return new UnifiedMem<T>(_mem.Slice(offset, count));
			case RefMemBackingStorageType.SpanGuard:
				return _spanGuard.Slice(offset, count);
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem.
	/// Span mode: NotSupportedException (allocates new Mem, defeats stack-only purpose).
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public Mem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				throw new NotSupportedException("Map() not supported in Span mode (allocates new Mem). Use Mem mode or operate on Span directly.");
			case RefMemBackingStorageType.Mem:
				return _mem.Map(mapFunc);
			case RefMemBackingStorageType.SpanGuard:
				throw new NotSupportedException("Map() not supported for SpanGuard backing (returns Mem). Use SpanGuard<T>.Map() directly or convert to Mem.");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Allocates a new pooled Mem by applying the specified mapping function to each element of this Mem.
	/// Span mode: NotSupportedException (allocates new Mem, defeats stack-only purpose).
	/// </summary>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public Mem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				throw new NotSupportedException("Map() not supported in Span mode (allocates new Mem). Use Mem mode or operate on Span directly.");
			case RefMemBackingStorageType.Mem:
				return _mem.Map(mapFunc);
			case RefMemBackingStorageType.SpanGuard:
				throw new NotSupportedException("Map() not supported for SpanGuard backing (returns Mem). Use SpanGuard<T>.Map() directly or convert to Mem.");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Allocates a new pooled Mem by mapping two Mem instances in parallel using the specified function.
	/// Span mode: NotSupportedException (allocates new Mem, defeats stack-only purpose).
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <typeparam name="TResult">Result element type</typeparam>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
	/// <returns>New pooled memory containing mapped results</returns>
	public Mem<TResult> MapWith<TOther, TResult>(Mem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				throw new NotSupportedException("MapWith() not supported in Span mode (allocates new Mem). Use Mem mode or operate on Span directly.");
			case RefMemBackingStorageType.Mem:
				return _mem.MapWith(otherToMapWith, mapFunc);
			case RefMemBackingStorageType.SpanGuard:
				throw new NotSupportedException("MapWith() not supported for SpanGuard backing (returns Mem). Use SpanGuard<T>.MapWith() directly or convert to Mem.");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified action, modifying elements in place.
	/// Works in all modes by operating on underlying Span data.
	/// </summary>
	/// <typeparam name="TOther">Element type of the other memory</typeparam>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="mapFunc">Action that processes pairs of elements by reference</param>
	public void MapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Length, "otherToMapWith must be the same length as this RefMem");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;

		for (var i = 0; i < Length; i++)
		{
			mapFunc(ref thisSpan[i], ref otherSpan[i]);
		}
	}



	/// <summary>
	/// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per range (synchronous version).
	/// Use this to process subgroups without extra allocations.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a RefMem slice that references this instance's backing store.</param>
	public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>> worker)
	{
		if (this.Length == 0)
		{
			return;
		}

		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				// Inline implementation for Span mode
				var span = this.Span;
				var batchStart = 0;
				while (batchStart < this.Length)
				{
					var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
					worker(this.Slice(batchStart, batchEnd - batchStart));
					batchStart = batchEnd;
				}
				break;
			case RefMemBackingStorageType.Mem:
				// Delegate to _mem.BatchMap (implicit conversion Mem<T> -> RefMem<T>)
				_mem.BatchMap(isSameBatch, worker);
				break;
			case RefMemBackingStorageType.SpanGuard:
				// Delegate to _spanGuard.BatchMap
				_spanGuard.BatchMap(isSameBatch, worker);
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Walks contiguous batches with parallel memory and calls worker once per range (synchronous version).
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
	/// <param name="worker">Action executed for each contiguous batch</param>
	public void BatchMapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>, UnifiedMem<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Length, "otherToMapWith must be the same length as this memory");

		if (this.Length == 0)
		{
			return;
		}

		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				// Inline implementation for Span mode
				var span = this.Span;
				var batchStart = 0;
				while (batchStart < this.Length)
				{
					var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
					worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
					batchStart = batchEnd;
				}
				break;
			case RefMemBackingStorageType.Mem:
				// Delegate to _mem.BatchMapWith (implicit conversions Mem<T> -> RefMem<T>)
				_mem.BatchMapWith(otherToMapWith, isSameBatch, worker);
				break;
			case RefMemBackingStorageType.SpanGuard:
				_spanGuard.BatchMapWith(otherToMapWith, isSameBatch, worker);
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}
	/// <summary>
	/// Local synchronous scanner that finds the end of a contiguous batch for RefMem
	/// </summary>
	/// <param name="start">Starting index for batch scan</param>
	/// <param name="thisRefMem">Memory to scan</param>
	/// <param name="isSameBatch">Function determining if adjacent elements belong to same batch</param>
	/// <returns>Exclusive end index of the batch</returns>
	private static int _GetBatchEndExclusive(int start, UnifiedMem<T> thisRefMem, Func_RefArg<T, T, bool> isSameBatch)
	{
		var span = thisRefMem.Span;
		var length = thisRefMem.Length;

		var end = start + 1;
		while (end < length)
		{
			ref var previous = ref span[end - 1];
			ref var current = ref span[end];
			if (!isSameBatch(ref previous, ref current))
			{
				break;
			}
			end++;
		}
		return end;
	}



	/// <summary>
	/// Creates a deep copy of this Mem with contents copied to new pool-backed storage.
	/// Span mode: NotSupportedException (allocates new Mem, defeats stack-only purpose).
	/// </summary>
	/// <returns>New pooled memory containing a copy of this memory's contents</returns>
	public Mem<T> Clone()
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				throw new NotSupportedException("Clone() not supported in Span mode (allocates new Mem, defeats stack-only purpose). Use Mem.Allocate(span) to copy to pooled memory.");
			case RefMemBackingStorageType.Mem:
				return _mem.Clone();
			case RefMemBackingStorageType.SpanGuard:
				throw new NotSupportedException("Clone() not supported for SpanGuard backing (returns SpanGuard). Use SpanGuard<T>.Clone() directly.");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// DANGEROUS: Gets the underlying array segment.
	/// Span mode: NotSupportedException (span may not be array-backed).
	/// Mem mode: The array may be larger than this view and may be pooled. Use with caution.
	/// </summary>
	/// <returns>Array segment representing this memory's backing storage</returns>
	public ArraySegment<T> DangerousGetArray()
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				throw new NotSupportedException("DangerousGetArray() not supported in Span mode (span may not be array-backed). Use Mem mode if array access required.");
			case RefMemBackingStorageType.Mem:
				return _mem.DangerousGetArray();
			case RefMemBackingStorageType.SpanGuard:
				return _spanGuard.DangerousGetArray();
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Converts this writable memory view to a read-only memory view.
	/// Span mode: NotSupportedException (cannot create ReadMem from raw Span).
	/// </summary>
	/// <returns>Read-only memory view over the same backing storage</returns>
	public ReadMem<T> AsReadMem()
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				throw new NotSupportedException("AsReadMem() not supported in Span mode (cannot create ReadMem from raw Span). Use Mem mode.");
			case RefMemBackingStorageType.Mem:
				return _mem.AsReadMem();
			case RefMemBackingStorageType.SpanGuard:
				throw new NotSupportedException("AsReadMem() not supported for SpanGuard backing. Use Mem mode.");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// if owned by a pool, Disposes so the backing array can be recycled.
	/// Span mode: No-op (stack lifetime managed automatically).
	/// Mem mode: Delegates to wrapped Mem.Dispose().
	/// </summary>
	public void Dispose()
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				// No-op: stack lifetime managed automatically
				break;
			case RefMemBackingStorageType.Mem:
				_mem.Dispose();
				break;
			case RefMemBackingStorageType.SpanGuard:
				_spanGuard.Dispose();
#if CHECKED
				_disposeGuard.Dispose();
#endif
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	/// <returns>Span enumerator</returns>
	public Span<T>.Enumerator GetEnumerator()
	{
		switch (_backingStorageType)
		{
			case RefMemBackingStorageType.Span:
				return _span.GetEnumerator();
			case RefMemBackingStorageType.Mem:
				return _mem.GetEnumerator();
			case RefMemBackingStorageType.SpanGuard:
				return _spanGuard.GetEnumerator();
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	/// <summary>
	/// Returns a string representation of this memory view showing type and count
	/// </summary>
	/// <returns>String in format "RefMem&lt;Type&gt;[Count] (Span|Mem mode)"</returns>
	public override string ToString()
	{
		var mode = _backingStorageType switch
		{
			RefMemBackingStorageType.Span => "Span",
			RefMemBackingStorageType.Mem => "Mem",
			RefMemBackingStorageType.SpanGuard => "SpanGuard",
			_ => "Unknown"
		};
		return $"RefMem<{typeof(T).Name}>[{Length}] ({mode} mode)";
	}
}

/// <summary>
/// a `Mem` that you should dispose (use the `using` pattern) to return the memory to the pool.
/// doing this will create zero allocations for temporary memory usage.
/// </summary>
/// <typeparam name="T"></typeparam>
public ref struct ZeroAllocMem<T> : IDisposable
{
	public SpanOwner<T> _poolOwner;
#if CHECKED
	private DisposeGuard _disposeGuard;
#endif

	public static ZeroAllocMem<T> Allocate(int size, AllocationMode allocationMode = AllocationMode.Default)
	{
		return new ZeroAllocMem<T>(SpanOwner<T>.Allocate(size, allocationMode));
	}

	public static ZeroAllocMem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	public static ZeroAllocMem<T> AllocateAndAssign(T singleItem)
	{
		var guard = Allocate(1);
		guard[0] = singleItem;
		return guard;
	}

	public ZeroAllocMem(SpanOwner<T> owner)
	{
		_poolOwner = owner;
#if CHECKED
		_disposeGuard = new();
#endif
	}

	public Span<T> Span => _poolOwner.Span;
	public int Count => _poolOwner.Length;
	[Obsolete("use .Count")]
	public int Length => _poolOwner.Length;

	public ref T this[int index]
	{
		get => ref _poolOwner.Span[index];
	}

	/// <summary>
	/// Creates a RefMem view over a slice of this memory.
	/// Returns a zero-allocation view (Span slice) referencing the original pooled data.
	/// </summary>
	/// <param name="offset">Starting offset within this memory</param>
	/// <param name="count">Number of elements in the slice</param>
	/// <returns>RefMem wrapping a Span slice over the original data</returns>
	public UnifiedMem<T> Slice(int offset, int count)
	{
		var slicedSpan = _poolOwner.Span.Slice(offset, count);
		return new UnifiedMem<T>(slicedSpan);
	}

	public ZeroAllocMem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = ZeroAllocMem<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	public ZeroAllocMem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = ZeroAllocMem<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
		{
			var mappedResult = mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	public ZeroAllocMem<TResult> MapWith<TOther, TResult>(UnifiedMem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Count, "otherToMapWith must be the same length as this SpanGuard");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;
		var toReturn = ZeroAllocMem<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;

		for (var i = 0; i < Count; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	public void MapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Count, "otherToMapWith must be the same length as this SpanGuard");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;

		for (var i = 0; i < Count; i++)
		{
			mapFunc(ref thisSpan[i], ref otherSpan[i]);
		}
	}

	/// <summary>
	/// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per range.
	/// Use this to process subgroups without extra allocations.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a SpanGuard slice.</param>
	public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>> worker)
	{
		if (this.Count == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Count)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(this.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Walks contiguous batches with parallel memory and calls worker once per range.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
	/// <param name="worker">Action executed for each contiguous batch</param>
	public void BatchMapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>, UnifiedMem<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == this.Count, "otherToMapWith must be the same length as this SpanGuard");

		if (this.Count == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Count)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Local synchronous scanner that finds the end of a contiguous batch
	/// </summary>
	/// <param name="start">Starting index for batch scan</param>
	/// <param name="thisGuard">Memory to scan</param>
	/// <param name="isSameBatch">Function determining if adjacent elements belong to same batch</param>
	/// <returns>Exclusive end index of the batch</returns>
	private static int _GetBatchEndExclusive(int start, ZeroAllocMem<T> thisGuard, Func_RefArg<T, T, bool> isSameBatch)
	{
		var span = thisGuard.Span;
		var length = thisGuard.Count;

		var end = start + 1;
		while (end < length)
		{
			ref var previous = ref span[end - 1];
			ref var current = ref span[end];
			if (!isSameBatch(ref previous, ref current))
			{
				break;
			}
			end++;
		}
		return end;
	}

	public ZeroAllocMem<T> Clone()
	{
		var copy = ZeroAllocMem<T>.Allocate(Count);
		this.Span.CopyTo(copy.Span);
		return copy;
	}

	public ArraySegment<T> DangerousGetArray()
	{
		return _poolOwner.DangerousGetArray();
	}

	public Span<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	public void Dispose()
	{
		_poolOwner.Span.Clear();
		_poolOwner.Dispose();

#if CHECKED
		_disposeGuard.Dispose();
#endif
	}

	public override string ToString()
	{
		return $"SpanGuard<{typeof(T).Name}>[{Count}]";
	}
}
