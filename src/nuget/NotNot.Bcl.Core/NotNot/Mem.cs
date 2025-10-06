// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
#define CHECKED

using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot.Collections.Advanced;

namespace NotNot;

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
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(ArraySegment<T> backingStore)
	{
		return Mem<T>.CreateUsing(backingStore);
	}

	//public static WriteMem<T> Allocate<T>(MemoryOwnerCustom<T> MemoryOwnerNew) => WriteMem<T>.Allocate(MemoryOwnerNew);
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(T[] array)
	{
		return Mem<T>.CreateUsing(array);
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
		return Mem<T>.Allocate(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return Mem<T>.Allocate(span);
	}

	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(Mem<T> writeMem)
	{
		return writeMem;
	}

	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(ReadMem<T> readMem)
	{
		return Mem<T>.CreateUsing(readMem);
	}
}

/// <summary>
///    helpers to allocate a ReadMem instance
/// </summary>
public static class ReadMem
{
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static ReadMem<T> CreateUsing<T>(ArraySegment<T> backingStore)
	{
		return ReadMem<T>.CreateUsing(backingStore);
	}

	//public static ReadMem<T> Allocate<T>(MemoryOwnerCustom<T> MemoryOwnerNew) => ReadMem<T>.Allocate(MemoryOwnerNew);
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static ReadMem<T> CreateUsing<T>(T[] array)
	{
		return ReadMem<T>.CreateUsing(array);
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
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static ReadMem<T> CreateUsing<T>(Mem<T> writeMem)
	{
		return ReadMem<T>.CreateUsing(writeMem);
	}
}

/// <summary>
///    a write capable view into an array/span
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct Mem<T> : IDisposable
{
	///// <summary>
	/////    if pooled, this will be set.  a reference to the pooled location so it can be recycled
	/////    while this will naturally be GC'd when all referencing Mem{T}'s go out-of-scope, you can manually do so by calling <see cref="Dispose"/> or the `using` pattern
	///// </summary>
	//private readonly MemoryOwner_Custom<T>? _poolOwner;

	private enum BackingStorageType
	{
		/// <summary>
		/// if pooled (Mem.Alloc()), this will be set.  a reference to the pooled location so it can be recycled
		///    while this will naturally be GC'd when all referencing Mem{T}'s go out-of-scope, you can manually do so by calling <see cref="Dispose"/> or the `using` pattern
		/// </summary>
		MemoryOwner_Custom,
		/// <summary>
		/// manually constructed Mem using your own List.  not disposed of when out-of-scope
		/// </summary>
		List,
		/// <summary>
		/// manually constructed Mem using your own List.  not disposed of when out-of-scope
		/// </summary>
		Array,
		/// <summary>
		/// manually constructed Mem using your own Memory.  not disposed of when out-of-scope
		/// </summary>
		Memory,
	}

	private readonly BackingStorageType _backingStorageType;
	private readonly object _backingStorage;
	private readonly int _segmentCount;
	private readonly int _segmentOffset;

	///// <summary>
	/////    details the backing storage
	///// </summary>
	//private readonly ArraySegment<T> _segment;

	//private readonly T[] _array;
	//private readonly int _offset;
	//public readonly int length;

	public static readonly Mem<T> Empty = new(ArraySegment<T>.Empty,0,0);





	internal Mem(T[] array) : this(new ArraySegment<T>(array), 0, array.Length) { }
	internal Mem(MemoryOwner_Custom<T> owner) : this(owner, 0, owner.Length) { }
	internal Mem(ArraySegment<T> owner) : this(owner, 0, owner.Count) { }
	internal Mem(List<T> owner) : this(owner, 0, owner.Count) { }
	internal Mem(Memory<T> owner) : this(owner, 0, owner.Length) { }



	internal Mem(MemoryOwner_Custom<T> owner, int sliceOffset, int sliceCount)
	{
		_backingStorageType = BackingStorageType.MemoryOwner_Custom;
		_backingStorage = owner;
		var ownerArraySegment = owner.DangerousGetArray();
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count );
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = ownerArraySegment.Offset + sliceOffset;
		_segmentCount = sliceCount;
	}
	internal Mem(ArraySegment<T> ownerArraySegment, int sliceOffset, int sliceCount)
	{
		_backingStorageType = BackingStorageType.Array;
		_backingStorage = ownerArraySegment.Array;		
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = ownerArraySegment.Offset + sliceOffset;
		_segmentCount = sliceCount;
	}
	internal Mem(T[] ownerArray, int sliceOffset, int sliceCount) : this(new ArraySegment<T>(ownerArray), sliceOffset, sliceCount) { }

	internal Mem(List<T> list, int sliceOffset, int sliceCount)
	{
		_backingStorageType = BackingStorageType.List;
		_backingStorage = list;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= list.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= list.Count);
		_segmentOffset = 0 + sliceOffset;
		_segmentCount = sliceCount;
	}

	internal Mem(Memory<T> ownerMemory, int sliceOffset, int sliceCount)
	{
		_backingStorageType = BackingStorageType.Memory;
		_backingStorage = ownerMemory.Slice(sliceOffset,sliceCount);
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerMemory.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerMemory.Length);
		_segmentOffset = 0;
		_segmentCount = sliceCount;
	}

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
	///    allocate memory from the shared pool.
	///    If your Type is a reference type or contains references, be sure to use clearOnDispose otherwise you will have
	///    memory leaks.
	///    also note that the memory is not cleared by default.
	/// </summary>
	public static Mem<T> Allocate(int size)
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
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	public static Mem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Allocate(1);
		mem[0] = singleItem;
		return mem;
	}

	public static Mem<T> CreateUsing(T[] array)
	{
		return new Mem<T>(new ArraySegment<T>(array));
	}

	public static Mem<T> CreateUsing(T[] array, int offset, int count)
	{
		return new Mem<T>(new ArraySegment<T>(array, offset, count));
	}

	public static Mem<T> CreateUsing(ArraySegment<T> backingStore)
	{
		return new Mem<T>(backingStore);
	}

	internal static Mem<T> CreateUsing(MemoryOwner_Custom<T> MemoryOwnerNew)
	{
		return new Mem<T>(MemoryOwnerNew);
	}

	public static Mem<T> CreateUsing(ReadMem<T> readMem)
	{
		return readMem.AsWriteMem();
	}


	public Mem<T> Slice(int offset, int count)
	{
		var toReturn = new Mem<T>(this, offset, count);
		return toReturn;
	}

	/// <summary>
	/// allocate a new Mem by applying the specified "Pick" mapping function to each element of this Mem
	/// </summary>
	public Mem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = Mem<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}
	public Mem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = Mem<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
		{
			var mappedResult = mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}
	/// <summary>
	/// like .Map() but allows mapping using two Mem instances in parallel.  must be the same length.
	/// </summary>
	/// <typeparam name="TOther"></typeparam>
	/// <typeparam name="TResult"></typeparam>
	/// <param name="otherToMapWith"></param>
	/// <param name="mapFunc"></param>
	/// <returns></returns>
	public Mem<TResult> MapWith<TOther, TResult>(Mem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this Mem");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;
		var toReturn = Mem<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;

		for (var i = 0; i < Count; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}
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
	public async ValueTask BatchProcess(Func_RefArg<T, T, bool> isSameBatch, Func<Mem<T>, ValueTask> worker)
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
	/// Local synchronous scanner; no awaits here, so using Span<T> is safe
	/// </summary>
	/// <param name="start"></param>
	/// <param name="thisMem"></param>
	/// <param name="isSameBatch"></param>
	/// <returns></returns>
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

	/// <summary>
	/// create a copy of the Mem (contents view coppied to new pool backed storage)
	/// </summary>
	/// <returns></returns>
	public Mem<T> Clone()
	{
		var copy = Mem<T>.Allocate(Count);
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

	public Span<T> Span
	{
		get
		{
			switch (_backingStorageType)
			{
				case BackingStorageType.MemoryOwner_Custom:
					{
						var owner = (MemoryOwner_Custom<T>)_backingStorage;

						var span = owner.Span;
						return span.Slice(_segmentOffset, _segmentCount);
					}
				case BackingStorageType.Array:
					{
						var array = (T[])_backingStorage;
						return new Span<T>(array, _segmentOffset, _segmentCount);
					}
				case BackingStorageType.List:
					{
						var list = (List<T>)_backingStorage;
						return CollectionsMarshal.AsSpan(list).Slice(_segmentOffset, _segmentCount);
					}
					case BackingStorageType.Memory:
					{
						var memory = (Memory<T>)_backingStorage;
						return memory.Span.Slice(_segmentOffset,_segmentCount);
					}
				default:
					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
			}
		}
	}

	//public Memory<T> Memory =>
	//	//return new Memory<T>(_array, _offset, length);
	//	_segment.AsMemory();

	public int Count => _segmentCount;
	[Obsolete("use .Count")]
	public int Length => _segmentCount;

	/// <summary>
	///    if owned by a pool, Disposes (usually including clear) so the backing array can be recycled.   DANGER: any other references to the same backing pool slot are also disposed at this
	///    time!
	///    <para>for non-pooled, just makes this struct disposed, not touching the backing collection (not even clearing it).</para>
	/// </summary>
	public void Dispose()
	{		
		//only do work if backed by an owner, and if so, recycle
		switch(_backingStorageType)
		{
			case BackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertNotNull(owner, "storage is null, was it already disposed?");
					if (owner is not null)
					{
						owner.Dispose();
					}
				}
				break;
			case BackingStorageType.Array:
			case BackingStorageType.List:
			case BackingStorageType.Memory:
				//do nothing, let the GC handle backing.
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		__.AssertNotNull(_backingStorage, "storage is null, should never be");
		switch (_backingStorageType)
		{
			case BackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertIfNot(owner.IsDisposed is false, "storage is disposed, cannot use");
				}
				break;
			case BackingStorageType.Array:
			case BackingStorageType.List:
			case BackingStorageType.Memory:
				//do nothing, let the GC handle backing.
				break;
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}

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

	public Span<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	//public IEnumerable<T> Enumerable => Span;

	public ReadMem<T> AsReadMem()
	{
		return new ReadMem<T>(_poolOwner, _segment);
	}

	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{_segment.Count}]";
	}
}

/// <summary>
///    a read-only capable view into an array/span
/// </summary>
/// <typeparam name="T"></typeparam>
//[DebuggerTypeProxy(typeof(NotNot.Bcl.Collections.Advanced.CollectionDebugView<>))]
//[DebuggerDisplay("{ToString(),raw}")]
//[DebuggerDisplay("{ToString(),nq}")]
public readonly struct ReadMem<T> : IDisposable
{
	/// <summary>
	///    if pooled, this will be set.  a reference to the pooled location so it can be recycled
	/// </summary>
	private readonly MemoryOwner_Custom<T>? _poolOwner;

	/// <summary>
	///    details the backing storage
	/// </summary>
	private readonly ArraySegment<T> _segment;

	//private readonly T[] _array;
	//private readonly int _offset;
	//public readonly int length;
	//public int Length => _segment.Count;

	public static readonly ReadMem<T> Empty = new(null, ArraySegment<T>.Empty);
	internal ReadMem(MemoryOwner_Custom<T> owner) : this(owner, owner.DangerousGetArray()) { }
	internal ReadMem(ArraySegment<T> segment) : this(null, segment) { }

	internal ReadMem(MemoryOwner_Custom<T> owner, ArraySegment<T> segment, int subOffset, int length) : this(owner,
		new ArraySegment<T>(segment.Array, segment.Offset + subOffset, length))
	{
		__.GetLogger()._EzError(subOffset + segment.Offset + length < segment.Count);
		__.GetLogger()._EzError(length <= segment.Count);
	}

	internal ReadMem(T[] array, int offset, int length) : this(null, new ArraySegment<T>(array, offset, length)) { }

	internal ReadMem(T[] array) : this(null, new ArraySegment<T>(array)) { }

	internal ReadMem(MemoryOwner_Custom<T> owner, ArraySegment<T> segment)
	{
		_poolOwner = owner;
		_segment = segment;
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
		var mem = Mem<T>.Allocate(1);
		mem[0] = singleItem;
		return CreateUsing(mem);
	}

	public static ReadMem<T> CreateUsing(T[] array)
	{
		return new ReadMem<T>(new ArraySegment<T>(array));
	}

	public static ReadMem<T> CreateUsing(T[] array, int offset, int count)
	{
		return new ReadMem<T>(new ArraySegment<T>(array, offset, count));
	}

	public static ReadMem<T> CreateUsing(ArraySegment<T> backingStore)
	{
		return new ReadMem<T>(backingStore);
	}

	internal static ReadMem<T> CreateUsing(MemoryOwner_Custom<T> MemoryOwnerNew)
	{
		return new ReadMem<T>(MemoryOwnerNew);
	}

	public static ReadMem<T> CreateUsing(Mem<T> mem)
	{
		return mem.AsReadMem();
	}


	public Mem<T> Slice(int offset, int count)
	{
		//var toReturn = new Mem<T>(_poolOwner, new(_array, _offset + offset, count), _array, _offset + offset, count);
		var toReturn = new Mem<T>(_poolOwner, _segment, offset, count);
		return toReturn;
	}


	/// <summary>
	///    beware: the size of the array allocated may be larger than the size requested by this Mem.
	///    As such, beware if using the backing Array directly.  respect the offset+length described in this segment.
	/// </summary>
	public ArraySegment<T> DangerousGetArray()
	{
		return _segment;
	}

	public ReadOnlySpan<T> Span =>
		//return new Span<T>(_array, _offset, length);
		_segment.AsSpan();

	public Span<T> AsWriteSpan()
	{
		return _segment.AsSpan();
	}

	public Memory<T> Memory =>
		//return new Memory<T>(_array, _offset, length);
		_segment.AsMemory();

	public int Length => _segment.Count;

	/// <summary>
	///    if owned by a pook, recycles.   DANGER: any other references to the same backing pool slot are also disposed at this
	///    time!
	/// </summary>
	public void Dispose()
	{
		//only do work if backed by an owner, and if so, recycle
		if (_poolOwner != null)
		{
			AssertNotDisposed();
			__.GetLogger()._EzError(_poolOwner.IsDisposed, "backing _poolOwner is already disposed!");

			var array = _segment.Array;
			Array.Clear(array, 0, array.Length);
			_poolOwner.Dispose();
		}

		//#if DEBUG
		//		Array.Clear(_array, _offset, Length);
		//#endif
	}

	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		__.GetLogger()._EzError(_poolOwner?.IsDisposed != true, "disposed while in use");
	}

	public T this[int index]
	{
		get
		{
			AssertNotDisposed();
			return _segment[index];
			//return ref Span[index];
			//__.GetLogger()._EzError(index >= 0 && index < length);
			//return ref _array[_offset + index];
		}
	}

	public ReadOnlySpan<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	public IEnumerable<T> Enumerable => _segment;

	public Mem<T> AsWriteMem()
	{
		return new Mem<T>(_poolOwner, _segment);
	}

	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{_segment.Count}]";
	}
}
