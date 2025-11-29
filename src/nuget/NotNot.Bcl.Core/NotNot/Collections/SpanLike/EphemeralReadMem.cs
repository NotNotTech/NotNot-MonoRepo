//// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
//// [!!] Copyright ©️ NotNot Project and Contributors.
//// [!!] This file is licensed to you under the MPL-2.0.
//// [!!] See the LICENSE.md file in the project root for more info.
//// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

//using System.Runtime.InteropServices;
//using CommunityToolkit.HighPerformance.Buffers;
//using NotNot.Collections.Advanced;

//namespace NotNot.Collections.SpanLike;

///// <summary>
///// Non-owning READ-ONLY view into memory with same backing storage as Mem{T} but NO disposal/lifetime management.
///// Use for temporary read-only access where caller holds ownership elsewhere and will dispose when callstack pops.
///// </summary>
///// <remarks>
///// <para>Unlike ref struct, this can be used in async methods and stored in fields.</para>
///// <para>Conversions:</para>
///// <para>- Implicit FROM: Mem{T}, RentedMem{T}, ReadMem{T}, EphemeralMem{T}, T[], List, Memory - safe narrowing to non-owning read-only view</para>
///// <para>- Implicit TO: ReadOnlySpan{T} only - read-only access</para>
///// <para>- NO conversion TO Mem{T}, RentedMem{T}, or EphemeralMem{T} - cannot restore write access</para>
///// </remarks>
///// <typeparam name="T">Element type</typeparam>
//public readonly struct EphemeralReadMem<T>
//{

//	// ========== Implicit conversions FROM other types (safe narrowing to read-only) ==========

//	public static implicit operator EphemeralReadMem<T>(T[] array) => new Mem<T>(array);
//	public static implicit operator EphemeralReadMem<T>(ArraySegment<T> arraySegment) => new Mem<T>(arraySegment);
//	public static implicit operator EphemeralReadMem<T>(List<T> list) => new Mem<T>(list);
//	public static implicit operator EphemeralReadMem<T>(Memory<T> memory) => new Mem<T>(memory);
//	public static implicit operator EphemeralReadMem<T>(Mem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);
//	public static implicit operator EphemeralReadMem<T>(RentedMem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);
//	//public static implicit operator EphemeralReadMem<T>(ReadMem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);
//	public static implicit operator EphemeralReadMem<T>(Mem<T> mem) => new(mem._backingStorageType, mem._backingStorage, mem._segmentOffset, mem._segmentCount);

//	// ========== Implicit conversions TO span types ==========

//	/// <summary>
//	/// Implicit conversion to ReadOnlySpan{T}
//	/// </summary>
//	public static implicit operator ReadOnlySpan<T>(EphemeralReadMem<T> mem) => mem.Span;

//	// ========== Internal fields - mirrors Mem<T> structure ==========

//	/// <summary>
//	/// Identifies which type of backing store is being used
//	/// </summary>
//	internal readonly MemBackingStorageType _backingStorageType;

//	/// <summary>
//	/// Reference to the actual backing storage object
//	/// </summary>
//	internal readonly object _backingStorage;

//	/// <summary>
//	/// Offset into the backing storage where this view begins
//	/// </summary>
//	internal readonly int _segmentOffset;

//	/// <summary>
//	/// Number of elements in this memory view
//	/// </summary>
//	internal readonly int _segmentCount;

//	/// <summary>
//	/// Represents an empty ephemeral read-only view with zero elements
//	/// </summary>
//	public static readonly EphemeralReadMem<T> Empty = new(MemBackingStorageType.Empty, null!, 0, 0);

//	// ========== Constructors ==========

//	/// <summary>
//	/// Internal constructor from backing storage components
//	/// </summary>
//	internal EphemeralReadMem(MemBackingStorageType backingStorageType, object backingStorage, int segmentOffset, int segmentCount)
//	{
//		_backingStorageType = backingStorageType;
//		_backingStorage = backingStorage;
//		_segmentOffset = segmentOffset;
//		_segmentCount = segmentCount;
//	}

//	// ========== Core properties ==========

//	/// <summary>
//	/// Gets a ReadOnlySpan{T} view over this memory
//	/// </summary>
//	public ReadOnlySpan<T> Span
//	{
//		get
//		{
//			switch (_backingStorageType)
//			{
//				case MemBackingStorageType.Empty:
//					return ReadOnlySpan<T>.Empty;
//				case MemBackingStorageType.MemoryOwner_Custom:
//					{
//						var owner = (MemoryOwner_Custom<T>)_backingStorage;
//						var span = owner.Span;
//						return span.Slice(_segmentOffset, _segmentCount);
//					}
//				case MemBackingStorageType.Array:
//					{
//						var array = (T[])_backingStorage;
//						return new ReadOnlySpan<T>(array, _segmentOffset, _segmentCount);
//					}
//				case MemBackingStorageType.List:
//					{
//						var list = (List<T>)_backingStorage;
//						return CollectionsMarshal.AsSpan(list).Slice(_segmentOffset, _segmentCount);
//					}
//				case MemBackingStorageType.Memory:
//					{
//						var memory = (Memory<T>)_backingStorage;
//						return memory.Span.Slice(_segmentOffset, _segmentCount);
//					}
//				case MemBackingStorageType.RentedArray:
//					{
//						var rentedArray = (NotNot._internal.ObjectPool.RentedArray<T>)_backingStorage;
//						return new ReadOnlySpan<T>(rentedArray.Value, _segmentOffset, _segmentCount);
//					}
//				case MemBackingStorageType.RentedList:
//					{
//						var rentedList = (NotNot._internal.ObjectPool.Rented<List<T>>)_backingStorage;
//						return CollectionsMarshal.AsSpan(rentedList.Value).Slice(_segmentOffset, _segmentCount);
//					}
//				case MemBackingStorageType.SingleItem:
//					{
//						if (_segmentCount == 0) return ReadOnlySpan<T>.Empty;
//						var storage = (SingleItemStorage<T>)_backingStorage;
//						return MemoryMarshal.CreateReadOnlySpan(ref storage.Value, 1);
//					}
//				default:
//					throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
//			}
//		}
//	}

//	/// <summary>
//	/// Gets the number of elements in this memory view
//	/// </summary>
//	public int Length => _segmentCount;

//	/// <summary>
//	/// Returns true if this memory view has zero elements
//	/// </summary>
//	public bool IsEmpty => _segmentCount == 0;

//	/// <summary>
//	/// Gets the value of the element at the specified index (read-only, returns by value)
//	/// </summary>
//	public T this[int index] => Span[index];

//	// ========== Slicing ==========

//	/// <summary>
//	/// Creates a new ephemeral read-only view that is a slice of this memory
//	/// </summary>
//	public EphemeralReadMem<T> Slice(int offset)
//	{
//		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
//		return new EphemeralReadMem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, _segmentCount - offset);
//	}

//	/// <summary>
//	/// Creates a new ephemeral read-only view that is a slice of this memory
//	/// </summary>
//	public EphemeralReadMem<T> Slice(int offset, int count)
//	{
//		__.ThrowIfNot(offset >= 0 && offset <= _segmentCount);
//		__.ThrowIfNot(count >= 0 && offset + count <= _segmentCount);
//		return new EphemeralReadMem<T>(_backingStorageType, _backingStorage, _segmentOffset + offset, count);
//	}

//	// ========== Enumeration ==========

//	/// <summary>
//	/// Returns an enumerator for iterating over the elements in this memory
//	/// </summary>
//	public ReadOnlySpan<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

//	// ========== Utility methods ==========

//	/// <summary>
//	/// Copies the contents of this memory into a destination span
//	/// </summary>
//	public void CopyTo(Span<T> destination) => Span.CopyTo(destination);

//	/// <summary>
//	/// Attempts to copy the contents of this memory into a destination span
//	/// </summary>
//	public bool TryCopyTo(Span<T> destination) => Span.TryCopyTo(destination);

//	/// <summary>
//	/// Converts to array by copying contents
//	/// </summary>
//	public T[] ToArray() => Span.ToArray();

//	/// <summary>
//	/// Returns a string representation of this memory view
//	/// </summary>
//	public override string ToString() => $"EphemeralReadMem<{typeof(T).Name}>[{Length}]";
//}
