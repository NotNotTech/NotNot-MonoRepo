// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
#define CHECKED

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot;
using NotNot.Collections.Advanced;

namespace NotNot.Collections.SpanLike;


/// <summary>
/// A "rented" memory wrapper that MUST be disposed by you, the caller.  This will return memory to the pool for reuse without GC pressure.
/// <para>Use the `using` pattern to ensure proper cleanup and zero allocations for temporary memory usage.</para>/// 
/// <para> failure to return will cause an assert in #DEBUG mode.  </para>
/// </summary>
/// <typeparam name="T">Element type</typeparam>
/// <remarks>
/// <para>Unlike <see cref="Mem{T}"/>, this type ONLY supports rented/pooled backing storage.</para>
/// BACKING STORAGE: Only pooled types are supported:
/// - MemoryOwner_Custom (ArrayPool-backed)
/// - RentedArray (ObjectPool-backed)
/// - RentedList (ObjectPool-backed)
///
/// CONVERSIONS:
/// - Implicit to Mem{T}: Always allowed
/// - Explicit from Mem{T}: Only for pooled backing types; throws for non-pooled
/// </remarks>
public readonly struct RentedMem<T> : IDisposable
{
	public static readonly RentedMem<T> Empty = new(MemBackingStorageType.Empty, null);

	// Implicit conversion to Span - useful when assigning to Span variables or passing to Span parameters
	public static implicit operator Span<T>(RentedMem<T> mem) => mem.GetSpan();
	public static implicit operator ReadOnlySpan<T>(RentedMem<T> mem) => mem.GetSpan();


	/// <summary>
	/// Identifies which type of pooled backing store is being used
	/// </summary>
	internal readonly MemBackingStorageType _backingStorageType;

	/// <summary>
	/// Reference to the actual backing storage object (MemoryOwner_Custom, RentedArray, or RentedList)
	/// </summary>
	internal readonly object _backingStorage;

#if CHECKED
	/// <summary>
	/// Provides a guard object to ensure that resources are disposed of correctly when the containing object is disposed.
	/// </summary>
	/// <remarks>This field is used to help manage the disposal pattern and prevent resource leaks. It is only
	/// included when the CHECKED compilation symbol is defined.</remarks>
	private readonly DisposeGuard _disposeGuard;
#endif


	/// <summary>
	/// Checks if a backing storage type is pooled/rented
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool _IsPooledType(MemBackingStorageType type) => type is
		MemBackingStorageType.MemoryOwner_Custom or
		MemBackingStorageType.RentedArray or
		MemBackingStorageType.RentedList or
		MemBackingStorageType.Empty;


	/// <summary>
	/// used if the backing storage is a list, to ensure it's not modified
	/// </summary>
	private readonly int _listLength;


	/// <summary>
	/// Creates a RentedMem backed by a pooled memory owner
	/// </summary>
	internal RentedMem(MemBackingStorageType backingStorageType, object backingStorage)
	{
		__.AssertIfNot(_IsPooledType(backingStorageType), "RentedMem only supports pooled backing types");

		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;

#if CHECKED
		//list handling
		switch (_backingStorageType)
		{
			case MemBackingStorageType.List:
			case MemBackingStorageType.RentedList:
				switch (backingStorage)
				{
					case List<T> list:
						_listLength = list.Count;
						break;
					case NotNot._internal.ObjectPool.Rented<List<T>> rentedList:
						_listLength = rentedList.Value.Count;
						break;
				}
				break;
		}
		//disposal tracker
		switch (_backingStorageType)
		{
			case MemBackingStorageType.Empty:
				//nothing to dispose
				break;
			default:
				_disposeGuard = new DisposeGuard();
				break;
		}
#endif

	}

	/// <summary>
	/// Clones the contents of a Mem into a new pooled RentedMem
	/// </summary>
	public static RentedMem<T> Clone(Mem<T> toClone)
	{
		var copy = RentedMem<T>.Allocate(toClone.Length);
		toClone.CopyTo(copy);
		return copy;
	}

	/// <summary>
	/// Allocates memory from the shared pool. <see cref="MemoryOwner_Custom{T}"/>
	/// Memory is cleared by default.
	/// </summary>
	/// <param name="allowGCReclaim">set to TRUE to not get asserts raised if you don't .Dispose() of this properly
	/// <para>useful for when you have explicitly "fire and forget" versions of objects, but should be avoided as an antipattern.</para></param>
	public static RentedMem<T> Allocate(int size, bool allowGCReclaim = false, AllocationMode allocationMode = AllocationMode.Clear)
	{
		var mo = MemoryOwner_Custom<T>.Allocate(size, allocationMode);
		var toReturn = new RentedMem<T>(MemBackingStorageType.MemoryOwner_Custom, mo);
#if CHECKED
		if (allowGCReclaim)
		{
			toReturn._disposeGuard._suppress = true;
		}
#endif
		return toReturn;
	}

	/// <summary>
	/// Allocates memory from the shared pool and copies the contents of the specified span into it
	/// </summary>
	public static RentedMem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn);
		return toReturn;
	}

	/// <summary>
	/// Allocates a single-element memory from the pool and assigns the specified value
	/// </summary>
	public static RentedMem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Allocate(1);
		mem.GetSpan()[0] = singleItem;
		return mem;
	}

	/// <summary>
	/// Gets a Span{T} slice of this memory. Does NOT create a new RentedMem.  need to dispose this one when done.
	/// Use this for zero-allocation access to subranges.
	/// </summary>
	public Mem<T> Slice(int offset, int count)
	{
		//return Span.Slice(offset, count);
		return this.CastEphermial().Slice(offset, count);
	}

	/// <summary>
	/// Gets the number of elements in this memory view.
	/// Computed directly from backing storage without calling GetSpan().
	/// </summary>
	public int Length => _GetLength();

	private int _GetLength()
	{
		return _backingStorageType switch
		{
			MemBackingStorageType.Empty => 0,
			MemBackingStorageType.MemoryOwner_Custom => ((MemoryOwner_Custom<T>)_backingStorage).Length,
			MemBackingStorageType.RentedArray => ((NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage).Value.Length,
			MemBackingStorageType.RentedList => ((NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage).Value.Count,
			_ => throw __.Throw($"RentedMem does not support backing type {_backingStorageType}")
		};
	}

	/// <summary>
	/// Gets a Span{T} view over this memory.
	/// <para>IMPORTANT: This method involves switch dispatch, validation, and potential slicing.
	/// Call once and store the result for iteration rather than calling repeatedly.</para>
	/// </summary>
	public Span<T> GetSpan()
	{
		Span<T> toReturn;
		switch (_backingStorageType)
		{
			case MemBackingStorageType.Empty:
				toReturn = Span<T>.Empty;
				break;
			case MemBackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					var span = owner.Span;
					toReturn = span;
					break;
				}
			case MemBackingStorageType.RentedArray:
				{
					var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
					var span = rentedArray.Value;
					toReturn = span;
					break;
				}
			case MemBackingStorageType.RentedList:
				{
					var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
					var span = rentedList.Value._AsSpan();
					toReturn = span;
					break;
				}
			default:
				throw __.Throw($"RentedMem does not support backing type {_backingStorageType}");
		}

#if CHECKED
		switch (_backingStorageType)
		{
			case MemBackingStorageType.List:
			case MemBackingStorageType.RentedList:
				__.AssertIfNot(_listLength == toReturn.Length, "backing list size was modified since this Mem was created.  Mem/RentedMem sizes should be immutable");
				break;
		}
#endif

		return toReturn;
	}

	// NOTE: Indexer this[int index] removed intentionally.
	// Indexer usage in loops causes repeated GetSpan() calls.
	// Callers should use: var span = mem.GetSpan(); then span[index]

	//	/// <summary>
	//	/// RentedMem is meant to be disposed in the method scope it is created in.
	//	/// <para>you can avoid this by converting to a Mem instead, which lets you hold onto it longer term.  
	//	/// after converting to a Mem, you should still dispose when done, but if you don't, it'll automatically get recycled when it exits GC scope.</para>
	//	/// </summary>
	//	/// <returns></returns>
	//	public Mem<T> Persist()
	//	{
	//		var toReturn = _backingStorageType switch
	//		{
	//			MemBackingStorageType.MemoryOwner_Custom => new Mem<T>((MemoryOwner_Custom<T>)_backingStorage, _segmentOffset, _segmentCount, _isTrueOwner),
	//			MemBackingStorageType.RentedArray => new Mem<T>((NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage, _segmentOffset, _segmentCount, _isTrueOwner),
	//			MemBackingStorageType.RentedList => new Mem<T>((NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage, _segmentOffset, _segmentCount, _isTrueOwner),
	//			MemBackingStorageType.Empty => Mem<T>.Empty,
	//			_ => throw __.Throw($"RentedMem does not support backing type {_backingStorageType}")
	//		};
	//#if CHECKED
	//		//disable the dispose guard alert.
	//		this._disposeGuard?.Dispose();
	//#endif
	//		return toReturn;
	//	}

	///// <summary>
	///// Explicit conversion to Mem{T}
	///// <para>IMPORTANT: this RentedMem will still be the storage owner, so it will still trigger the dispose guard unless you <see cref="Dispose"/> or unless <see cref="Persist"/>() is called</para>
	///// </summary>
	///// <returns></returns>
	//public Mem<T> Cast()
	//{
	//	return new Mem<T>(this._backingStorageType, this._backingStorage, this._segmentCount, this._segmentCount, false);
	//}

	//public EphemeralReadMem<T> CastEphermialRO()
	//{
	//	//need to create an EphermialMem<T> variant of RentedMem
	//	return new EphemeralReadMem<T>(this._backingStorageType, this._backingStorage, this._segmentCount, this._segmentCount);
	//}

	/// <summary>
	/// explicitly cast to an ephermial <see cref="Mem"/>.  Note that you still must call <see cref="Dispose"/> on this when done to recycle properly.
	/// <para>This "explicit cast" method is useful because sometimes the implicit casting isn't enough for .NET</para>
	/// </summary>
	/// <returns></returns>
	public Mem<T> CastEphermial()
	{
		//need to create an EphermialMem<T> variant of RentedMem
		return new Mem<T>(this._backingStorageType, this._backingStorage, 0, Length);
	}


	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Span<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		var thisSpan = GetSpan();
		var toReturnSpan = toReturn;
		for (var i = 0; i < thisSpan.Length; i++)
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
		return toReturn;
	}

	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Span<TResult> toReturn, Func_RefArg<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		var thisSpan = GetSpan();
		var toReturnSpan = toReturn;
		for (var i = 0; i < thisSpan.Length; i++)
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
		return toReturn;
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified function, writing results to the output buffer
	/// </summary>
	public void MapWith<TOther, TResult>(Span<TResult> toReturn, Span<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");
		var thisSpan = GetSpan();
		var otherSpan = otherToMapWith;
		var toReturnSpan = toReturn;

		for (var i = 0; i < thisSpan.Length; i++)
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
		return toReturn;
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified action, modifying elements in place
	/// </summary>
	public void MapWith<TOther>(Span<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");
		var thisSpan = GetSpan();
		var otherSpan = otherToMapWith;

		for (var i = 0; i < thisSpan.Length; i++)
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
		var span = GetSpan();
		var batchStart = 0;
		while (batchStart < span.Length)
		{
			var batchEnd = _GetBatchEndExclusive(span, batchStart, isSameBatch);
			worker(span.Slice(batchStart, batchEnd - batchStart));
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
		var thisSpan = GetSpan();
		var otherSpan = otherToMapWith;
		var batchStart = 0;
		while (batchStart < thisSpan.Length)
		{
			var batchEnd = _GetBatchEndExclusive(thisSpan, batchStart, isSameBatch);
			worker(thisSpan.Slice(batchStart, batchEnd - batchStart), otherSpan.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}
	public async ValueTask BatchMapWith<TOther>(PinnedMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Func<PinnedMem<T>, PinnedMem<TOther>, ValueTask> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this Mem");

		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			// Re-acquire span each iteration since Span cannot cross await boundary
			var batchEnd = _GetBatchEndExclusive(GetSpan(), batchStart, isSameBatch);
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

	/// <summary>
	/// Creates a deep copy of this RentedMem with contents copied to new pool-backed storage
	/// </summary>
	public RentedMem<T> Clone()
	{
		var copy = RentedMem<T>.Allocate(Length);
		GetSpan().CopyTo(copy);
		return copy;
	}

	/// <summary>
	/// Gets the underlying storage as an <see cref="ArraySegment{T}"/>
	/// <para>Generally you should use the <see cref="Span"/> instead.</para>
	/// <para>IMPORTANT:  Use with caution. The array may be larger than this view and may be pooled, and if you keep a ref to the array it may become out-of-date with the backing storage</para>
	/// </summary>
	/// <returns>Array segment representing this memory's backing storage</returns>
	public ArraySegment<T> DangerousGetArray()
	{
		AssertNotDisposed();

		return this.CastEphermial().DangerousGetArray();

	}

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	public Span<T>.Enumerator GetEnumerator()
	{
		return GetSpan().GetEnumerator();
	}

	/// <summary>
	/// Disposes the rented memory, returning the backing storage to the pool.
	/// Only the true owner disposes; slices do nothing.
	/// </summary>
	public void Dispose()
	{
#if CHECKED
		_disposeGuard?.Dispose();
#endif

		switch (_backingStorageType)
		{
			case MemBackingStorageType.MemoryOwner_Custom:
				{
					var owner = (MemoryOwner_Custom<T>)_backingStorage;
					__.AssertNotNull(owner, "storage is null, was it already disposed?");
					owner?.Dispose();
				}
				break;
			case MemBackingStorageType.RentedArray:
				{
					__.AssertNotNull(_backingStorage, "storage is null, was it already disposed?");
					var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
					rentedArray.Dispose();
				}
				break;
			case MemBackingStorageType.RentedList:
				{
					__.AssertNotNull(_backingStorage, "storage is null, was it already disposed?");
					var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
					rentedList.Dispose();
				}
				break;
			case MemBackingStorageType.Empty:
				// Empty/default - nothing to dispose
				break;
			default:
				throw __.Throw($"RentedMem does not support backing type {_backingStorageType}");
		}
	}

	/// <summary>
	/// Asserts that this memory has not been disposed. Only executes in CHECKED builds.
	/// </summary>
	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		if (_backingStorageType == MemBackingStorageType.Empty)
		{
			return; // Empty is always valid
		}
		__.AssertNotNull(_backingStorage, "storage is null, should never be");
		if (_backingStorageType == MemBackingStorageType.MemoryOwner_Custom)
		{
			var owner = (MemoryOwner_Custom<T>)_backingStorage;
			__.AssertIfNot(owner.IsDisposed is false, "storage is disposed, cannot use");
		}
	}


	/// <summary>
	/// Returns a string representation of this memory view showing type and count
	/// </summary>
	public override string ToString()
	{
		return $"RentedMem<{typeof(T).Name}>[{Length}]";
	}
}
