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
///    Use an array of {T} from the ArrayPool, without allocating any objects upon use. (no gc pressure)
///    use this instead of <see cref="SpanOwner{T}" />.  This will alert you if you do not dispose properly.
/// </summary>
/// <typeparam name="T"></typeparam>
public ref struct SpanGuard<T> : IDisposable
{
	/// <summary>
	///    should dispose prior to exit function.   easiest way is to ex:  `using var spanGuard = SpanGuard{int}(42)
	/// </summary>
	/// <param name="size"></param>
	/// <param name="allocationMode">if you only use NotNot based pooling (SpanGuard and Mem) you can leave this as default, because pooled arrays are cleared upon .Dispose().</param>
	/// <returns></returns>
	public static SpanGuard<T> Allocate(int size, AllocationMode allocationMode = AllocationMode.Default)
	{
		return new SpanGuard<T>(SpanOwner<T>.Allocate(size, allocationMode));
	}

	public SpanGuard(SpanOwner<T> owner)
	{
		_poolOwner = owner;
#if CHECKED
		_disposeGuard = new();
#endif
	}

	public SpanOwner<T> _poolOwner;

	public Span<T> Span => _poolOwner.Span;

	public ArraySegment<T> DangerousGetArray()
	{
		return _poolOwner.DangerousGetArray();
	}


#if CHECKED
	private DisposeGuard _disposeGuard;
#endif
	public void Dispose()
	{
		_poolOwner.Span.Clear();
		_poolOwner.Dispose();

#if CHECKED
		_disposeGuard.Dispose();
#endif
	}
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
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Count);
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
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Count);
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
		var toReturn = Mem<TResult>.Alloc(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
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
		var toReturn = Mem<TResult>.Alloc(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
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
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this Mem");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;
		var toReturn = Mem<TResult>.Alloc(Count);
		var toReturnSpan = toReturn.Span;

		for (var i = 0; i < Count; i++)
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
	public void MapWith<TOther>(Mem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this Mem");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;

		for (var i = 0; i < Count; i++)
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
		if (this.Count == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Count)
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
		var length = thisMem.Count;

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
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this Mem");

		if (this.Count == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Count)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			await worker(this.Slice(batchStart, batchEnd - batchStart),otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Creates a deep copy of this Mem with contents copied to new pool-backed storage
	/// </summary>
	/// <returns>New pooled memory containing a copy of this memory's contents</returns>
	public Mem<T> Clone()
	{
		var copy = Mem<T>.Alloc(Count);
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
	/// Gets the number of elements in this memory view
	/// </summary>
	public int Count => _segmentCount;

	/// <summary>
	/// Gets the number of elements in this memory view. Obsolete: use Count instead.
	/// </summary>
	[Obsolete("use .Count")]
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
		return $"{GetType().Name}<{typeof(T).Name}>[{Count}]";
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
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Count);
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
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parentMem.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parentMem.Count);
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
	/// Gets the number of elements in this memory view
	/// </summary>
	public int Length => _segmentCount;

	/// <summary>
	/// Gets the number of elements in this memory view
	/// </summary>
	public int Count => _segmentCount;

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
		return $"{GetType().Name}<{typeof(T).Name}>[{Count}]";
	}
}
