// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using NotNot.Collections.Advanced;

namespace NotNot.Collections.SpanLike;

/// <summary>
/// <para>A "pinned" version of `<see cref="Mem{T}"/>.  can implicitly cast between the two, or can call <see cref="Mem{T}.Pin"/></para>
/// <para><see cref="Mem{T}"/> is a "stack only" ref struct to discourage taking long-term references.  However the CLR disallows utilizing ref structs in many scenarios (e.g. async methods, fields in classes, boxing, etc).
/// so this type provides a way to circumvent.   cast to <see cref="PinnedMem{T}"/> when you need to hold onto a memory view for longer periods or across async calls."/></para>
/// <para>under unusual circumstances you may need to take a long-term reference to a <see cref="Mem{T}"/>.  This allows you to do so.</para>
/// </summary>
public readonly struct PinnedMem<T>
{
	//========== Implicit conversions FROM owning types (safe narrowing) ==========
	public static implicit operator PinnedMem<T>(T[] array) => Mem.Wrap(array);
	public static implicit operator PinnedMem<T>(ArraySegment<T> arraySegment) => Mem.Wrap(arraySegment);
	public static implicit operator PinnedMem<T>(List<T> list) => Mem.Wrap(list);
	public static implicit operator PinnedMem<T>(Memory<T> memory) => Mem.Wrap(memory);


	//allow casting to/from Mem types
	public static implicit operator PinnedMem<T>(Mem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);
	public static implicit operator Mem<T>(PinnedMem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);

	//cast from Rented
	public static implicit operator PinnedMem<T>(RentedMem<T> mem) => new(mem._backingStorageType, mem._backingStorage);

	// Implicit conversion to Span - useful when assigning to Span variables or passing to Span parameters
	public static implicit operator Span<T>(PinnedMem<T> mem) => mem.GetSpan();
	public static implicit operator ReadOnlySpan<T>(PinnedMem<T> mem) => mem.GetSpan();




	// ========== Internal fields - mirrors PinnedMem<T> structure ==========

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
	public static readonly PinnedMem<T> Empty = new(MemBackingStorageType.Empty, null!, 0, 0);

	// ========== Constructors ==========

	internal PinnedMem(MemBackingStorageType backingStorageType, object backingStorage)
	{
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		var rawSpan = _GetRawSpan();
		_segmentOffset = 0;
		_segmentCount = rawSpan.Length;

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
#endif
	}
	/// <summary>
	/// Internal constructor from backing storage components
	/// </summary>
	internal PinnedMem(MemBackingStorageType backingStorageType, object backingStorage, int segmentOffset, int segmentCount)
	{
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		_segmentOffset = segmentOffset;
		_segmentCount = segmentCount;

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
#endif
	}

	// ========== Core properties ==========

	private Span<T> _GetCurrentSegment(Span<T> backingSpan)
	{
		return backingSpan.Slice(_segmentOffset, _segmentCount);
	}

	/// <summary>
	/// Gets a Span{T} view over this memory.
	/// <para>IMPORTANT: This method involves switch dispatch, validation, and potential slicing.
	/// Call once and store the result for iteration rather than calling repeatedly.</para>
	/// </summary>
	public Span<T> GetSpan()
	{
		if (_segmentCount == 0) return Span<T>.Empty;
		var rawSpan = _GetRawSpan();

#if CHECKED
		switch (_backingStorageType)
		{
			case MemBackingStorageType.List:
			case MemBackingStorageType.RentedList:
				__.AssertIfNot(_listLength == rawSpan.Length, "backing list size was modified since this PinnedMem was created.  PinnedMem/RentedMem sizes should be immutable");
				break;
		}
#endif

		var toReturn = _GetCurrentSegment(rawSpan);
		__.DebugAssertIfNot(toReturn.Length == Length);
		return toReturn;
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
						var span = list._AsSpan();
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
						var span = rentedList.Value._AsSpan();
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

	// NOTE: Indexer this[int index] removed intentionally.
	// Indexer usage in loops causes repeated GetSpan() calls.
	// Callers should use: var span = mem.GetSpan(); then span[index]

	// ========== Slicing ==========

	/// <summary>
	/// Creates a new ephemeral view that is a slice of this memory
	/// </summary>
	public PinnedMem<T> Slice(int offset)
	{
		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
		return new PinnedMem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, _segmentCount - offset);
	}

	/// <summary>
	/// Creates a new ephemeral view that is a slice of this memory
	/// </summary>
	public PinnedMem<T> Slice(int offset, int count)
	{
		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
		__.ThrowIfNot(count >= 0 && offset + count <= _segmentCount);
		return new PinnedMem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, count);
	}

	// ========== Enumeration ==========

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	public Span<T>.Enumerator GetEnumerator() => GetSpan().GetEnumerator();

	// ========== Utility methods ==========

	/// <summary>
	/// Creates a deep copy of this PinnedMem with contents copied to new pool-backed storage
	/// </summary>
	/// <returns>New pooled memory containing a copy of this memory's contents</returns>
	public RentedMem<T> Clone()
	{
		var copy = Mem.Rent<T>(Length);
		GetSpan().CopyTo(copy);
		return copy;
	}

	/// <summary>
	/// Copies the contents of this memory into a destination span
	/// </summary>
	public void CopyTo(Span<T> destination) => GetSpan().CopyTo(destination);

	/// <summary>
	/// Attempts to copy the contents of this memory into a destination span
	/// </summary>
	public bool TryCopyTo(Span<T> destination) => GetSpan().TryCopyTo(destination);

	/// <summary>
	/// Fills the memory with the specified value
	/// </summary>
	public void Fill(T value) => GetSpan().Fill(value);

	/// <summary>
	/// Clears the memory (sets all elements to default)
	/// </summary>
	public void Clear() => GetSpan().Clear();

	/// <summary>
	/// Converts to array by copying contents
	/// </summary>
	public T[] ToArray() => GetSpan().ToArray();

	/// <summary>
	/// Returns a string representation of this memory view
	/// </summary>
	public override string ToString() => $"PinnedMem<{typeof(T).Name}>[{Length}]";




	/// <summary>
	/// Applies the specified mapping function to each element, writing results to the provided output buffer
	/// </summary>
	public void Map<TResult>(Span<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
	{
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this PinnedMem");
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
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this PinnedMem");
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
		__.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this PinnedMem");
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this PinnedMem");
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
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this PinnedMem");
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
	/// <para>Walks contiguous batches where `<paramref name="isSameBatch"/>` returns true for each previous/current pair and calls `<paramref name="worker"/>` once per range.</para>
	/// <para>Use this to process subgroups without extra allocations.</para>
	/// <para>IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.</para>
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a `PinnedMem` slice that references this instance's backing store.</param>
	/// <returns>A completed task once all batches have been processed.</returns>
	public async ValueTask BatchMap(Func_RefArg<T, T, bool> isSameBatch, Func<PinnedMem<T>, ValueTask> worker)
	{
		if (Length == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < Length)
		{
			// Re-acquire span each iteration since Span cannot cross await boundary
			var batchEnd = _GetBatchEndExclusive(GetSpan(), batchStart, isSameBatch);
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
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this PinnedMem");

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
		__.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this PinnedMem");

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
	/// Uses reflection to access the internal array backing a List{T}
	/// </summary>
	/// <param name="list">List to extract internal array from</param>
	/// <returns>Internal array backing the list</returns>
	private static T[] _GetListItemsArray(List<T> list)
	{
		if (Mem<T>._listItemsField is null)
		{
			throw __.Throw("List<T> layout not supported; backing _items field missing");
		}

		return (T[]?)Mem<T>._listItemsField.GetValue(list) ?? Array.Empty<T>();
	}

	/// <summary>
	/// Gets the underlying storage as an <see cref="ArraySegment{T}"/>
	/// <para>Generally you should use <see cref="GetSpan"/> instead.</para>
	/// <para>IMPORTANT:  Use with caution. The array may be larger than this view and may be pooled, and if you keep a ref to the array it may become out-of-date with the backing storage</para>
	/// </summary>
	/// <returns>Array segment representing this memory's backing storage</returns>
	public ArraySegment<T> DangerousGetArray()
	{
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
				throw __.Throw("SingleItem backing storage does not support DangerousGetArray - use GetSpan() method instead");
			default:
				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
		}
	}
}