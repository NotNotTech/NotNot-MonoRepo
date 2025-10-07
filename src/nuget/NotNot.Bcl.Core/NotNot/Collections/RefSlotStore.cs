// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using NotNot.Advanced;
using NotNot.Collections;

namespace NotNot.Collections;


/// <summary>
/// A read-only handle for referencing an allocated slot in a `RefSlotStore{T}` collection.
/// Packed into 32 bits for maximum performance:
/// - Bit 31: IsAllocated (1 bit)
/// - Bits 8-30: Index (23 bits, max 8,388,607)
/// - Bits 0-7: Version (8 bits, max 255)
/// </summary>
public readonly record struct SlotHandle : IComparable<SlotHandle>
{
	public int CompareTo(SlotHandle other)
	{
		return _packedValue.CompareTo(other._packedValue);
	}

	/// <summary>
	/// Direct access to the packed value for performance-critical scenarios
	/// </summary>
	public readonly uint _packedValue;

	public static SlotHandle Empty { get; } = default;


	/// <summary>
	/// Creates a SlotHandle from a packed value
	/// </summary>
	public SlotHandle(uint packed)
	{
		_packedValue = packed;
		_AssertOk();
	}

	/// <summary>
	/// Creates a new SlotHandle with the specified values
	/// </summary>
	public SlotHandle(int index, byte version, bool isAllocated)
	{
		// Validate ranges
		__.AssertIfNot(index >= 0 && index <= 0x7FFFFF, "Index must fit in 23 bits");
		__.AssertIfNot(version <= 0xFF, "Version must fit in 8 bits");

		// Pack the values
		_packedValue = ((uint)(isAllocated ? 1 : 0) << 31) |
					 ((uint)(index & 0x7FFFFF) << 8) |
					 ((uint)version & 0xFF);

		_AssertOk();
	}

	/// <summary>
	/// for internal use only: internal reference to the array/list location of the data
	/// </summary>
	public int Index
	{
		get => (int)((_packedValue >> 8) & 0x7FFFFF);
	}

	/// <summary>
	/// for internal use only: ensures the handle is not reused after freeing
	/// </summary>
	public byte Version
	{
		get => (byte)(_packedValue & 0xFF);
	}

	/// <summary>
	/// mostly for internal use: if the handle was properly allocated by a collection
	/// </summary>
	public bool IsAllocated
	{
		get => (_packedValue & 0x80000000U) != 0;
	}

	[Conditional("DEBUG")]
	private void _AssertOk()
	{
		__.AssertIfNot(IsAllocated);
		__.AssertIfNot(Version > 0, "assume version is >=1.  will remove IsAllocated bit to use that instead");

	}
}

public abstract class RefSlotStore
{
	protected static byte _initialVersion = 1;
	
}
/// <summary>
/// provides a List backed storage where individual slots can be freed for reuse.
/// - When out of capacity, the backing List is automatically resized
/// Concurrent Reads/Writes/Free() are supported, but Alloc() must be done exclusively.
/// using the indexer returns a **ref** to the stored item.
/// for doing reads/edits in bulk, use the `_AsSpan_Unsafe()` method.
/// </summary>
/// <typeparam name="T">The type of item to store.</typeparam>
public class RefSlotStore<T> : RefSlotStore
{
	/// <summary>
	/// tracks allocations, to ensure a slot is not used after being freed.
	/// </summary>
	private byte _nextVersion;

	/// <summary>
	/// storage for the data and their handles (for tracking lifetime)
	/// </summary>
	private List<(SlotHandle handle, T slotData)> _storage;

	/// <summary>
	/// for use when reading/writing existing elements in bulk.  be sure not to allocate when using this (but free is ok)
	/// <para>skip items with `handle.IsAllocated==false</para>
	/// </summary>
	/// <returns></returns>
	public Span<(SlotHandle handle, T slotData)> _AsSpan_Unsafe() => _storage._AsSpan_Unsafe();

	/// <summary>
	/// Used slots count: calculated as total allocated minus free slots.
	/// </summary>
	public int Count => _storage.Count - _freeSlots.Count;

	/// <summary>
	/// Total capacity of the storage (used and free slots).
	/// </summary>
	public int Capacity => _storage.Capacity;


	/// <summary>
	/// Count of free slots
	/// </summary>
	public int FreeCount => _freeSlots.Count;


	private readonly Stack<int> _freeSlots; // **Stack to track free slot indices**

	/// <summary>
	/// Lock for thread safety when allocating/freeing slots.
	/// </summary>
	private readonly Lock _lock = new();

	public RefSlotStore(int initialCapacity = 10)
	{
		_nextVersion = _initialVersion++;



		// **Initialize the underlying storage** with the initial capacity.
		_storage = new(initialCapacity);
		_freeSlots = new Stack<int>(initialCapacity);
	}




	/// <summary>
	/// **Indexer** to access elements by slot index.
	/// - **Thread-safe:** Uses lock to ensure safe access during concurrent operations.
	/// </summary>
	public ref T this[SlotHandle slot]
	{
		get
		{
			// Thread-safe validation and access
			lock (_lock)
			{
				var validResult = _IsHandleValid_Unsafe(slot);
				__.DebugAssertIfNot(validResult.isValid, $"Invalid slot access: {validResult.invalidReason}");

				if (!validResult.isValid)
				{
					throw new InvalidOperationException($"Invalid slot access: {validResult.invalidReason}");
				}

				return ref _storage._AsSpan_Unsafe()[slot.Index].slotData;
			}
		}
	}


	/// <summary>
	/// **Alloc** a free slot and set it with **data**.
	/// </summary>
	public SlotHandle Alloc(T data)
	{
		return Alloc(ref data);
		//lock (_lock)
		//	{
		//		var toReturn = Alloc();
		//		_storage[toReturn.Index] = (toReturn, data);


		//		if (OnAlloc is not null)
		//		{
		//			OnAlloc.Invoke(toReturn);
		//		}

		//		return toReturn;
		//	}
	}
	public SlotHandle Alloc(ref T data)
	{
		lock (_lock)
		{
			var toReturn = Alloc();
			ref var p_element = ref _storage._AsSpan_Unsafe()[toReturn.Index];
			//p_element.handle = handle;
			p_element.slotData = data;


			OnAlloc.Invoke(toReturn);

			return toReturn;
		}
	}



	/// <summary>
	/// **Alloc** a free slot.
	/// - Increases **Version**.
	/// - If no free slot is available, expands the storage.
	/// - **DEBUG mode:** tracks allocation via _CHECKED_allocationTracker.
	/// </summary>
	private SlotHandle Alloc()
	{
		lock (_lock)
		{
			var version = _nextVersion++;
			if (_nextVersion <= 0)
			{
				_nextVersion = 1;
			}
			int index;
			if (_freeSlots.Count > 0)
			{
				index = _freeSlots.Pop(); // **Reuse a free slot**
			}
			else
			{

				// **Grow storage:** no free slot available, so allocate a new slot.
				index = _storage.Count;
				_storage.Add(default);
				__.DebugAssertIfNot(index == _storage.Count - 1, "race condition: something else allocating even though we are in a lock?");
			}
			ref var p_element = ref _storage._AsSpan_Unsafe()[index];



			//verify allocated but unused
			__.DebugAssertIfNot(_storage.Count > index && _storage[index].handle.IsAllocated is false);


			var toReturn = new SlotHandle(index, version, true);

			p_element.handle = toReturn;

			//verify allocated and ref ok
			__.DebugAssertIfNot(_storage[index].handle.IsAllocated);


			return toReturn;
		}
	}

	/// <summary>
	/// invoked immediately after slot is alloc'd.
	/// <para>The slot is fully allocated (and populated), and the callback occurs within the lock.</para>
	/// </summary>
	public ActionEvent<SlotHandle> OnAlloc = new();
	/// <summary>
	/// invoked immediately before slot is freed
	/// <para>The slot is still fully allocated (and populated), and the callback occurs within the lock.</para>
	/// </summary>
	public ActionEvent<SlotHandle> OnFree = new();

	public (bool isValid, string? invalidReason) _IsHandleValid(SlotHandle slot)
	{
		// Thread-safe public version
		lock (_lock)
		{
			return _IsHandleValid_Unsafe(slot);
		}
	}

	/// <summary>
	/// Internal version of handle validation that assumes caller holds the lock.
	/// </summary>
	private (bool isValid, string? invalidReason) _IsHandleValid_Unsafe(SlotHandle slot)
	{

		if (!slot.IsAllocated)
		{
			return (false, "slot.IsEmpty");
		}

		if (_storage.Count <= slot.Index)
		{
			return (false, "storage not long enough");
		}

		if (_storage[slot.Index].handle.Version == slot.Version)
		{
			return (true, null);
		}
		else
		{
			return (false, "version mismatch");
		}
	}


	/// <summary>
	/// **Frees** a previously allocated slot.
	/// - Increases **Version**.
	/// - **DEBUG mode:** validates that the slot is currently allocated.
	/// </summary>
	public void Free(SlotHandle slot)
	{
		lock (_lock)
		{
			var validResult = _IsHandleValid_Unsafe(slot);

			if (!validResult.isValid)
			{
				__.DebugAssertIfNot(validResult.isValid);
				throw new InvalidOperationException($"attempt to free invalid slot: {validResult.invalidReason}");
			}


			OnFree.Invoke(slot);

			_freeSlots.Push(slot.Index); // **Mark slot as free**
			_storage[slot.Index] = default; // **Clear the slot's data**
		}
	}


}



/// <summary>
/// a prototype version of RefSlotStore that is optimized for archetype storage.
/// <para>optimized for batch operations, not setting value upon allocation</para>
/// </summary>
/// <typeparam name="T">The type of item to store.</typeparam>
public class RefSlotStore_ArchetypeOptimized<T> : RefSlotStore, IDisposable
{



	/// <summary>
	/// tracks allocations, to ensure a slot is not used after being freed.
	/// </summary>
	private byte _nextVersion;

	/// <summary>
	/// storage for the data and their handles (for tracking lifetime)
	/// </summary>
	private List<(SlotHandle handle, T slotData)> _storage;

	/// <summary>
	/// for use when reading/writing existing elements in bulk.  be sure not to allocate when using this (but free is ok)
	/// <para>skip items with `handle.IsAllocated==false</para>
	/// </summary>
	/// <returns></returns>
	public Span<(SlotHandle handle, T slotData)> _AsSpan_Unsafe() => _storage._AsSpan_Unsafe();

	/// <summary>
	/// Used slots count: calculated as total allocated minus free slots.
	/// </summary>
	public int Count => _storage.Count - _freeSlots.Count;

	/// <summary>
	/// Total capacity of the storage (used and free slots).
	/// </summary>
	public int Capacity => _storage.Capacity;


	/// <summary>
	/// Count of free slots
	/// </summary>
	public int FreeCount => _freeSlots.Count;


	private readonly Stack<int> _freeSlots; // **Stack to track free slot indices**

	/// <summary>
	/// Lock for thread safety when allocating/freeing slots.
	/// </summary>
	private readonly Lock _lock = new();

	public RefSlotStore_ArchetypeOptimized(int initialCapacity = 10)
	{

		_nextVersion = _initialVersion++;

		


		// **Initialize the underlying storage** with the initial capacity.
		_storage = new(initialCapacity);
		_freeSlots = new Stack<int>(initialCapacity);		
	}


	/// <summary>
	/// **Indexer** to access elements by slot index.
	/// - **Thread-safe:** Uses lock to ensure safe access during concurrent operations.
	/// </summary>
	public ref T this[SlotHandle slot]
	{
		get
		{
			// Thread-safe validation and access
			lock (_lock)
			{
				var validResult = _IsHandleValid_Unsafe(slot);
				__.DebugAssertIfNot(validResult.isValid, $"Invalid slot access: {validResult.invalidReason}");

				if (!validResult.isValid)
				{
					throw new InvalidOperationException($"Invalid slot access: {validResult.invalidReason}");
				}

				return ref _storage._AsSpan_Unsafe()[slot.Index].slotData;
			}
		}
	}


	/// <summary>
	/// allocate slots, all will be uninitialized ("default")
	/// </summary>
	/// <param name="count"></param>
	/// <returns></returns>
	public Mem<SlotHandle> Alloc(int count)
	{
		var toReturn = Mem<SlotHandle>.Alloc(count);
		var slotSpan = toReturn.Span;
		lock (_lock)
		{
			for (var i = 0; i < count; i++)
			{
				var hSlot = _AllocSlot();
				slotSpan[i] = hSlot;
			}

			return toReturn;//.AsReadMem();
		}
	}



	/// <summary>
	/// **Alloc** a free slot.
	/// - Increases **Version**.
	/// - If no free slot is available, expands the storage.
	/// - **DEBUG mode:** tracks allocation via _CHECKED_allocationTracker.
	/// </summary>
	private SlotHandle _AllocSlot()
	{
		lock (_lock)
		{
			var version = _nextVersion++;
			if (_nextVersion <= 0)
			{
				_nextVersion = 1;
			}
			int index;
			if (_freeSlots.Count > 0)
			{
				index = _freeSlots.Pop(); // **Reuse a free slot**
			}
			else
			{

				// **Grow storage:** no free slot available, so allocate a new slot.
				index = _storage.Count;
				_storage.Add(default);
				__.DebugAssertIfNot(index == _storage.Count - 1, "race condition: something else allocating even though we are in a lock?");
			}
			ref var p_element = ref _storage._AsSpan_Unsafe()[index];



			//verify allocated but unused
			__.DebugAssertIfNot(_storage.Count > index && _storage[index].handle.IsAllocated is false);

			var toReturn = new SlotHandle(index, version, true);

			p_element.handle = toReturn;

			//verify allocated and ref ok
			__.DebugAssertIfNot(_storage[index].handle.IsAllocated);


			return toReturn;
		}
	}


	public (bool isValid, string? invalidReason) _IsHandleValid(SlotHandle slot)
	{
		// Thread-safe public version
		lock (_lock)
		{
			return _IsHandleValid_Unsafe(slot);
		}
	}

	/// <summary>
	/// Internal version of handle validation that assumes caller holds the lock.
	/// </summary>
	private (bool isValid, string? invalidReason) _IsHandleValid_Unsafe(SlotHandle slot)
	{

		if (!slot.IsAllocated)
		{
			return (false, "slot.IsEmpty");
		}

		if (_storage.Count <= slot.Index)
		{
			return (false, "storage not long enough");
		}

		if (_storage[slot.Index].handle.Version == slot.Version)
		{
			return (true, null);
		}
		else
		{
			return (false, "version mismatch");
		}
	}


	/// <summary>
	/// **Frees** a previously allocated slot.
	/// - Increases **Version**.
	/// - **DEBUG mode:** validates that the slot is currently allocated.
	/// </summary>
	public void Free(Mem<SlotHandle> slotsToFree)
	{
		lock (_lock)
		{
			foreach (var slot in slotsToFree)
			{
				var validResult = _IsHandleValid_Unsafe(slot);
				if (!validResult.isValid)
				{
					__.DebugAssertIfNot(validResult.isValid);
					throw new InvalidOperationException($"attempt to free invalid slot: {validResult.invalidReason}");
				}
				_freeSlots.Push(slot.Index); // **Mark slot as free**
				_storage[slot.Index] = default; // **Clear the slot's data**
			}
		}
	}

	public void Dispose()
	{
		_storage.Clear();
		_freeSlots.Clear();
	}

}
