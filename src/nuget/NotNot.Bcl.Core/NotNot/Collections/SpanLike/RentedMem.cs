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

public static class RentedMem
{

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



}

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
public readonly ref struct RentedMem<T> : IDisposable
{
	/// <summary>
	/// Implicit conversion to Span for easy use in APIs expecting spans
	/// </summary>
	public static implicit operator Span<T>(RentedMem<T> rentedMem) => rentedMem.Span;

	///// <summary>
	///// Implicit conversion to ReadOnlySpan
	///// </summary>
	//public static implicit operator ReadOnlySpan<T>(RentedMem<T> rentedMem) => rentedMem.Span;



	///// <summary>
	///// Implicit conversion to Mem{T} - always safe since RentedMem is a subset of Mem
	///// </summary>
	//public static implicit operator Mem<T>(RentedMem<T> rentedMem) => rentedMem.AsMem();

	/// <summary>
	/// Explicit conversion from Mem{T} - only valid for pooled backing types
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when Mem has non-pooled backing storage</exception>
	public static explicit operator RentedMem<T>(Mem<T> mem) => FromMem(mem);

	/// <summary>
	/// Identifies which type of pooled backing store is being used
	/// </summary>
	internal readonly MemBackingStorageType _backingStorageType;

	/// <summary>
	/// Reference to the actual backing storage object (MemoryOwner_Custom, RentedArray, or RentedList)
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
	/// If true, disposal will return the backing storage to the pool; if false, it's a slice and should not dispose
	/// </summary>
	private readonly bool _isTrueOwner;
#if CHECKED
	/// <summary>
	/// Provides a guard object to ensure that resources are disposed of correctly when the containing object is disposed.
	/// </summary>
	/// <remarks>This field is used to help manage the disposal pattern and prevent resource leaks. It is only
	/// included when the CHECKED compilation symbol is defined.</remarks>
	private readonly DisposeGuard _disposeGuard;
#endif
	///// <summary>
	///// Represents an empty rented memory view with zero elements
	///// </summary>
	//public static readonly RentedMem<T> Empty = default;

	/// <summary>
	/// Cached reflection info for accessing List{T}'s internal array field
	/// </summary>
	private static readonly FieldInfo? _listItemsField = typeof(List<T>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

	/// <summary>
	/// Checks if a backing storage type is pooled/rented
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool _IsPooledType(MemBackingStorageType type) => type is
		MemBackingStorageType.MemoryOwner_Custom or
		MemBackingStorageType.RentedArray or
		MemBackingStorageType.RentedList;

	/// <summary>
	/// Creates a RentedMem from a Mem, validating that the backing is pooled.
	/// Preserves the source Mem's ownership flag.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when Mem has non-pooled backing storage</exception>
	public static RentedMem<T> FromMem(Mem<T> mem)
	{
		if (!_IsPooledType(mem._backingStorageType))
		{
			__.Throw($"Cannot create RentedMem from non-pooled Mem. Backing type '{mem._backingStorageType}' is not pool-backed. " +
				"RentedMem only supports MemoryOwner_Custom, RentedArray, or RentedList backing.");
		}
		return new RentedMem<T>(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount, isTrueOwner: mem._isTrueOwner);
	}

	/// <summary>
	/// Creates a RentedMem backed by a pooled memory owner
	/// </summary>
	internal RentedMem(MemoryOwner_Custom<T> owner, bool isTrueOwner) : this(owner, 0, owner.Length, isTrueOwner) { }

	/// <summary>
	/// Creates a RentedMem backed by ObjectPool rented array
	/// </summary>
	internal RentedMem(NotNot._internal.ObjectPool.RentedArray<T> rentedArray, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.RentedArray;
		_backingStorage = rentedArray;
		__.ThrowIfNot(rentedArray.Value != null);
		_segmentOffset = 0;
		_segmentCount = rentedArray.Value.Length;
#if CHECKED
		if (_isTrueOwner)
		{
			_disposeGuard = new DisposeGuard();
		}
#endif
	}

	/// <summary>
	/// Creates a RentedMem backed by ObjectPool rented list
	/// </summary>
	internal RentedMem(NotNot._internal.ObjectPool.Rented<List<T>> rentedList, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.RentedList;
		_backingStorage = rentedList;
		__.ThrowIfNot(rentedList.Value != null);
		_segmentOffset = 0;
		_segmentCount = rentedList.Value.Count;
#if CHECKED
		if (_isTrueOwner)
		{
			_disposeGuard = new DisposeGuard();
		}
#endif
	}

	/// <summary>
	/// Creates a sliced RentedMem from a pooled memory owner
	/// </summary>
	internal RentedMem(MemoryOwner_Custom<T> owner, int sliceOffset, int sliceCount, bool isTrueOwner)
	{
		_isTrueOwner = isTrueOwner;
		_backingStorageType = MemBackingStorageType.MemoryOwner_Custom;
		_backingStorage = owner;
		var ownerArraySegment = owner.DangerousGetArray();
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= ownerArraySegment.Count);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= ownerArraySegment.Count);
		_segmentOffset = sliceOffset;
		_segmentCount = sliceCount;
#if CHECKED
		if (_isTrueOwner)
		{
			_disposeGuard = new DisposeGuard();
		}
#endif
	}

	/// <summary>
	/// Internal constructor for creating from validated backing storage
	/// </summary>
	private RentedMem(MemBackingStorageType backingStorageType, object backingStorage, int segmentOffset, int segmentCount, bool isTrueOwner)
	{
		__.AssertIfNot(_IsPooledType(backingStorageType), "RentedMem only supports pooled backing types");
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		_segmentOffset = segmentOffset;
		_segmentCount = segmentCount;
		_isTrueOwner = isTrueOwner;
#if CHECKED
		if (_isTrueOwner)
		{
			_disposeGuard = new DisposeGuard();
		}
#endif
	}

	/// <summary>
	/// Creates a sliced RentedMem from another RentedMem (non-owning)
	/// </summary>
	internal RentedMem(RentedMem<T> parent, int sliceOffset, int sliceCount)
	{
		_isTrueOwner = false; // Slices never own the backing storage
		_backingStorageType = parent._backingStorageType;
		_backingStorage = parent._backingStorage;
		__.ThrowIfNot(sliceOffset >= 0 && sliceOffset <= parent.Length);
		__.ThrowIfNot(sliceCount >= 0 && sliceCount + sliceOffset <= parent.Length);
		_segmentOffset = parent._segmentOffset + sliceOffset;
		_segmentCount = sliceCount;
	}

	/// <summary>
	/// Clones the contents of a Mem into a new pooled RentedMem
	/// </summary>
	public static RentedMem<T> Clone(Mem<T> toClone)
	{
		var copy = RentedMem<T>.Allocate(toClone.Length);
		toClone.Span.CopyTo(copy.Span);
		return copy;
	}

	/// <summary>
	/// Allocates memory from the shared pool.
	/// Memory is cleared by default.
	/// </summary>
	public static RentedMem<T> Allocate(int size, AllocationMode allocationMode = AllocationMode.Clear)
	{
		var mo = MemoryOwner_Custom<T>.Allocate(size, allocationMode);
		return new RentedMem<T>(mo, isTrueOwner: true);
	}

	/// <summary>
	/// Allocates memory from the shared pool and copies the contents of the specified span into it
	/// </summary>
	public static RentedMem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	/// <summary>
	/// Allocates a single-element memory from the pool and assigns the specified value
	/// </summary>
	public static RentedMem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Allocate(1);
		mem.Span[0] = singleItem;
		return mem;
	}

	/// <summary>
	/// Gets a Span{T} slice of this memory. Does NOT create a new RentedMem.
	/// Use this for zero-allocation access to subranges.
	/// </summary>
	public Span<T> Slice(int offset, int count)
	{
		return Span.Slice(offset, count);
	}

	/// <summary>
	/// Gets the number of elements in this memory view
	/// </summary>
	public int Length => _segmentCount;

	/// <summary>
	/// Gets a Span{T} view over this memory
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
				case MemBackingStorageType.None:
					return Span<T>.Empty;
				default:
					throw __.Throw($"RentedMem does not support backing type {_backingStorageType}");
			}
		}
	}

	/// <summary>
	/// Gets a reference to the element at the specified index
	/// </summary>
	public ref T this[int index]
	{
		get
		{
			AssertNotDisposed();
			return ref Span[index];
		}
	}

	/// <summary>
	/// RentedMem is meant to be disposed in the method scope it is created in.
	/// <para>you can avoid this by converting to a Mem instead, which lets you hold onto it longer term.  
	/// You should still dispose when done, but if you don't, it'll automatically get recycled when it exits GC scope.</para>
	/// </summary>
	/// <returns></returns>
	public Mem<T> ConvertToMemAndPersist()
	{
		var toReturn = _backingStorageType switch
		{
			MemBackingStorageType.MemoryOwner_Custom => new Mem<T>((MemoryOwner_Custom<T>)_backingStorage, _segmentOffset, _segmentCount, _isTrueOwner),
			MemBackingStorageType.RentedArray => new Mem<T>((NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage, _segmentOffset, _segmentCount, _isTrueOwner),
			MemBackingStorageType.RentedList => new Mem<T>((NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage, _segmentOffset, _segmentCount, _isTrueOwner),
			MemBackingStorageType.None => Mem<T>.Empty,
			_ => throw __.Throw($"RentedMem does not support backing type {_backingStorageType}")
		};
#if CHECKED
		//disable the dispose guard alert.
		this._disposeGuard?.Dispose();
#endif
		return toReturn;
	}

	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Mem<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		var thisSpan = Span;
		var toReturnSpan = toReturn.Span;
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
		var toReturn = Mem<TResult>.Allocate(Length);
		Map(toReturn, mapFunc);
		return RentedMem<TResult>.FromMem(toReturn);
	}

	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Mem<TResult> toReturn, Func_RefArg<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		var thisSpan = Span;
		var toReturnSpan = toReturn.Span;
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
		var toReturn = Mem<TResult>.Allocate(Length);
		Map(toReturn, mapFunc);
		return RentedMem<TResult>.FromMem(toReturn);
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified function, writing results to the output buffer
	/// </summary>
	public void MapWith<TOther, TResult>(Mem<TResult> toReturn, Mem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this RentedMem");
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");
		var thisSpan = Span;
		var otherSpan = otherToMapWith.Span;
		var toReturnSpan = toReturn.Span;

		for (var i = 0; i < Length; i++)
		{
			ref var r_mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
			toReturnSpan[i] = r_mappedResult;
		}
	}

	/// <summary>
	/// Allocates a new pooled RentedMem by mapping two memory instances in parallel
	/// </summary>
	public RentedMem<TResult> MapWith<TOther, TResult>(Mem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		var toReturn = Mem<TResult>.Allocate(Length);
		MapWith(toReturn, otherToMapWith, mapFunc);
		return RentedMem<TResult>.FromMem(toReturn);
	}

	/// <summary>
	/// Maps two memory instances in parallel using the specified action, modifying elements in place
	/// </summary>
	public void MapWith<TOther>(Mem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");
		var thisSpan = Span;
		var otherSpan = otherToMapWith.Span;

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
	/// Walks contiguous batches with parallel memory and calls worker once per batch.
	/// Workers receive Span slices - no allocation.
	/// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
	/// </summary>
	public void BatchMapWith<TOther>(Mem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>, Span<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RentedMem");

		if (Length == 0)
		{
			return;
		}
		var thisSpan = Span;
		var otherSpan = otherToMapWith.Span;
		var batchStart = 0;
		while (batchStart < Length)
		{
			var batchEnd = _GetBatchEndExclusive(thisSpan, batchStart, isSameBatch);
			worker(thisSpan.Slice(batchStart, batchEnd - batchStart), otherSpan.Slice(batchStart, batchEnd - batchStart));
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
		Span.CopyTo(copy.Span);
		return copy;
	}

	/// <summary>
	/// DANGEROUS: Gets the underlying array segment. The array may be larger than this view and is pooled.
	/// </summary>
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
			default:
				throw __.Throw($"RentedMem does not support backing type {_backingStorageType}");
		}
	}

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	public Span<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	/// <summary>
	/// Disposes the rented memory, returning the backing storage to the pool.
	/// Only the true owner disposes; slices do nothing.
	/// </summary>
	public void Dispose()
	{
		if (!_isTrueOwner)
		{
			return;
		}
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
			case MemBackingStorageType.None:
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
		if (_backingStorageType == MemBackingStorageType.None)
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
	/// Uses reflection to access the internal array backing a List{T}
	/// </summary>
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
	public override string ToString()
	{
		return $"RentedMem<{typeof(T).Name}>[{Length}]";
	}
}

///// <summary>
///// Delegate for actions that take a Span by value
///// </summary>
//public delegate void Action_Span<T>(Span<T> span);

///// <summary>
///// Delegate for actions that take a ReadOnlySpan by value
///// </summary>
//public delegate void Action_RoSpan<T>(ReadOnlySpan<T> span);
