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
/// A universal, read-only view into a wrapped array/list/memory backing storage, with support for pooled allocation (renting) for temporary collections.
/// Supports implicit casting from array/list/memory along with explicit via ReadMem.Wrap() methods.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
//[DebuggerTypeProxy(typeof(NotNot.Bcl.Collections.Advanced.CollectionDebugView<>))]
//[DebuggerDisplay("{ToString(),raw}")]
//[DebuggerDisplay("{ToString(),nq}")]
[Obsolete("use UnifiedReadMem<T> instead")]
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
		var mem = Mem<T>.Allocate(1);
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
