// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot.Collections.Advanced;

namespace NotNot.Collections.SpanLike;

/// <summary>
/// Non-owning view into memory with same backing storage as Mem{T} but NO disposal/lifetime management.
/// Use for temporary access where caller holds ownership elsewhere and will dispose when callstack pops.
/// </summary>
/// <remarks>
/// <para>Unlike ref struct, this can be used in async methods and stored in fields.</para>
/// <para>Conversions:</para>
/// <para>- Implicit FROM: Mem{T}, RentedMem{T} - safe narrowing to non-owning view</para>
/// <para>- Implicit TO: Span{T}, ReadOnlySpan{T} - consistent with Mem{T}</para>
/// <para>- NO conversion TO Mem{T} or RentedMem{T} - cannot restore ownership metadata</para>
/// </remarks>
/// <typeparam name="T">Element type</typeparam>
public readonly struct EphemeralMem<T>
{

	// ========== Implicit conversions FROM owning types (safe narrowing) ==========

	public static implicit operator EphemeralMem<T>(T[] array) => new Mem<T>(array);
	public static implicit operator EphemeralMem<T>(ArraySegment<T> arraySegment) => new Mem<T>(arraySegment);
	public static implicit operator EphemeralMem<T>(List<T> list) => new Mem<T>(list);
	public static implicit operator EphemeralMem<T>(Memory<T> memory) => new Mem<T>(memory);
	public static implicit operator EphemeralMem<T>(Mem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);
	public static implicit operator EphemeralMem<T>(RentedMem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);

	// ========== Implicit conversions TO span types ==========

	/// <summary>
	/// Implicit conversion to Span{T}
	/// </summary>
	public static implicit operator Span<T>(EphemeralMem<T> mem) => mem.Span;

	/// <summary>
	/// Implicit conversion to ReadOnlySpan{T}
	/// </summary>
	public static implicit operator ReadOnlySpan<T>(EphemeralMem<T> mem) => mem.Span;

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
	/// </summary>
	internal readonly int _segmentOffset;

	/// <summary>
	/// Number of elements in this memory view
	/// </summary>
	internal readonly int _segmentCount;

	/// <summary>
	/// Represents an empty ephemeral view with zero elements
	/// </summary>
	public static readonly EphemeralMem<T> Empty = new(MemBackingStorageType.Empty, null!, 0, 0);

	// ========== Constructors ==========

	/// <summary>
	/// Internal constructor from backing storage components
	/// </summary>
	internal EphemeralMem(MemBackingStorageType backingStorageType, object backingStorage, int segmentOffset, int segmentCount)
	{
		_backingStorageType = backingStorageType;
		_backingStorage = backingStorage;
		_segmentOffset = segmentOffset;
		_segmentCount = segmentCount;
	}

	// ========== Core properties ==========

	/// <summary>
	/// Gets a Span{T} view over this memory
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

	/// <summary>
	/// Gets the number of elements in this memory view
	/// </summary>
	public int Length => _segmentCount;

	/// <summary>
	/// Returns true if this memory view has zero elements
	/// </summary>
	public bool IsEmpty => _segmentCount == 0;

	/// <summary>
	/// Gets a reference to the element at the specified index
	/// </summary>
	public ref T this[int index] => ref Span[index];

	// ========== Slicing ==========

	/// <summary>
	/// Creates a new ephemeral view that is a slice of this memory
	/// </summary>
	public EphemeralMem<T> Slice(int offset)
	{
		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
		return new EphemeralMem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, _segmentCount - offset);
	}

	/// <summary>
	/// Creates a new ephemeral view that is a slice of this memory
	/// </summary>
	public EphemeralMem<T> Slice(int offset, int count)
	{
		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
		__.ThrowIfNot(count >= 0 && offset + count <= _segmentCount);
		return new EphemeralMem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, count);
	}

	// ========== Enumeration ==========

	/// <summary>
	/// Returns an enumerator for iterating over the elements in this memory
	/// </summary>
	public Span<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

	// ========== Utility methods ==========

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
	public override string ToString() => $"EphemeralMem<{typeof(T).Name}>[{Length}]";




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
	public async ValueTask BatchMapWith<TOther>(EphemeralMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Func<EphemeralMem<T>, EphemeralMem<TOther>, ValueTask> worker)
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

///// <summary>
///// Static helper methods for creating EphemeralMem instances
///// </summary>
//public static class EphemeralMem
//{
//	/// <summary>
//	/// Wrap a Mem as an ephemeral view - strips ownership, caller retains responsibility for disposal
//	/// </summary>
//	public static EphemeralMem<T> Wrap<T>(Mem<T> mem) => mem;

//	/// <summary>
//	/// Wrap a RentedMem as an ephemeral view - strips ownership, caller retains responsibility for disposal
//	/// </summary>
//	public static EphemeralMem<T> Wrap<T>(RentedMem<T> mem) => mem;
//}
