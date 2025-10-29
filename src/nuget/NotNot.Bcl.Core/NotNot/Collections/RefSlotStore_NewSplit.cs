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
/// for internal use only, tracking allocations for debug assistance.
/// </summary>
internal class RefSlotStore_NewSplit_VersionTracker
{
	internal static byte _initialVersion = 1;

}
/// <summary>
/// a prototype version of RefSlotStore that is optimized for archetype storage.
/// <para>optimized for batch operations, not setting value upon allocation</para>
/// </summary>
/// <typeparam name="T">The type of item to store.</typeparam>
public class RefSlotStore_NewSplit<T> : IDisposeGuard
{
	/// <summary>
	/// tracks allocations, to ensure a slot is not used after being freed.
	/// </summary>
	private byte _nextVersion;

	/// <summary>
	/// storage for the data handles (for tracking lifetime)
	/// </summary>
	private List<SlotHandle> _allocTracker;

	/// <summary>
	/// storage for the slot data
	/// </summary>
	private T[] _data;

	/// <summary>
	/// obtain the backing data (allocated +free slots)
	/// <para>Unsafe:  be sure you do not allocate while using this.</para>
	/// </summary>
	public Mem<T> Data_Mem => _data;

	/// <summary>
	/// obtain the backing data (allocated +free slots)
	/// <para>Unsafe:  be sure you do not allocate while using this.</para>
	/// </summary>
	public Span<T> Data_Span => _data.AsSpan();


	/// <summary>
	/// obtain the backing data (allocated +free slots)
	/// <para>Unsafe:  be sure you do not allocate while using this.</para>
	/// </summary>
	public Mem<SlotHandle> AllocTracker_Unsafe => _allocTracker;

	/// <summary>
	/// Used slots count: calculated as total allocated minus free slots.
	/// <para>important: used slots may not be contiguous, so use <see cref="StorageCapacity"/> for looping </para>
	/// <para>see <see cref="AllocatedLength"/>, <see cref="Count"/> <see cref="FreeCount"/>, and <see cref="StorageCapacity"/> </para>
	/// </summary>
	public int Count => _allocTracker.Count - _freeSlots.Count;

	/// <summary>
	/// length of the allocated storage (used + free slots)
	/// <para>see <see cref="AllocatedLength"/>, <see cref="Count"/> <see cref="FreeCount"/>, and <see cref="StorageCapacity"/> </para>
	/// </summary>
	public int AllocatedLength => _allocTracker.Count;

	/// <summary>
	/// Total capacity of the storage (used and free slots, AND unallocated capacity).
	/// <para>see <see cref="AllocatedLength"/>, <see cref="Count"/> <see cref="FreeCount"/>, and <see cref="StorageCapacity"/> </para>
	/// </summary>
	public int StorageCapacity
	{
		get
		{
			__.AssertIfNot(_allocTracker.Capacity == _data.Length, "internal error: capacity mismatch between alloc tracker and data array");
			return _allocTracker.Capacity;
		}
	}

	public RefEnumerable GetEnumerable(bool includeFree = false)
	{
		return new RefEnumerable(this, includeFree);
	}

	/// <summary>
	/// Count of free slots
	/// <para>see <see cref="AllocatedLength"/>, <see cref="Count"/> <see cref="FreeCount"/>, and <see cref="StorageCapacity"/> </para>
	/// </summary>
	public int FreeCount => _freeSlots.Count;

	public bool IsDisposed { get; private set; }

	private Stack<int> _freeSlots; // **Stack to track free slot indices**

	/// <summary>
	/// Lock for thread safety when allocating/freeing slots.
	/// </summary>
	private readonly Lock _lock = new();

	public RefSlotStore_NewSplit(int initialCapacity = 10)
	{

		_nextVersion = RefSlotStore_NewSplit_VersionTracker._initialVersion++;





		// **Initialize the underlying storage** with the initial capacity.
		_allocTracker = new(initialCapacity);
		_data = new T[initialCapacity];
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

				return ref _data[slot.Index];
			}
		}
	}



	/// <summary>
	/// allocate slots, all will be uninitialized ("default")
	/// </summary>
	/// <param name="count"></param>
	/// <returns></returns>
	public Mem<SlotHandle> AllocSlots(int count)
	{




		var toReturn = Mem<SlotHandle>.Alloc(count);
		var slotSpan = toReturn.Span;
		lock (_lock)
		{
			for (var i = 0; i < count; i++)
			{
				var hSlot = AllocSlot();
				slotSpan[i] = hSlot;
			}

			return toReturn;//.AsReadMem();
		}
	}
	/// <summary>
	/// allocate slots and set their values
	/// </summary>
	/// <param name="values"></param>
	/// <returns></returns>
	public Mem<SlotHandle> AllocValues(Mem<T> values)
	{
		var toReturn = AllocSlots(values.Length);

		values.MapWith(toReturn, (ref T value, ref SlotHandle slot) =>
		{
			_data[slot.Index] = value;
		});

		return toReturn;
	}



	/// <summary>
	/// **Alloc** a free slot.  data value will be `default`.
	/// </summary>
	public SlotHandle AllocSlot()
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
				index = _allocTracker.Count;
				_allocTracker.Add(default);
				if (_data.Length != _allocTracker.Capacity)
				{
					// resize data array to match list capacity
					Array.Resize(ref _data, _allocTracker.Capacity);
				}
				__.DebugAssertIfNot(index == _allocTracker.Count - 1, "race condition: something else allocating even though we are in a lock?");
			}

			//verify allocated but unused
			__.DebugAssertIfNot(_allocTracker.Count > index && _allocTracker[index].IsAllocated is false);

			var toReturn = new SlotHandle(index, version, true);

			_allocTracker[index] = toReturn;
			_data[index] = default;


			//verify allocated and ref ok
			__.DebugAssertIfNot(_allocTracker[index].IsAllocated);


			return toReturn;
		}
	}

	public SlotHandle AllocValue(ref T value)
	{
		var toReturn = AllocSlot();
		_data[toReturn.Index] = value;
		return toReturn;
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

		if (_allocTracker.Count <= slot.Index)
		{
			return (false, "storage not long enough");
		}

		if (_allocTracker[slot.Index].Version == slot.Version)
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
				FreeSingleSlot(slot);
			}
		}
	}

	public void FreeSingleSlot(SlotHandle slot)
	{
		lock (_lock)
		{
			var validResult = _IsHandleValid_Unsafe(slot);
			if (!validResult.isValid)
			{
				__.DebugAssertIfNot(validResult.isValid);
				throw new InvalidOperationException($"attempt to free invalid slot: {validResult.invalidReason}");
			}
			_freeSlots.Push(slot.Index); // **Mark slot as free**
			_allocTracker[slot.Index] = default; // **Clear the slot's handle**
			_data[slot.Index] = default; // **Clear the slot's data**
		}
	}

	public void Dispose()
	{
		IsDisposed = true;
		_allocTracker.Clear();
		_allocTracker = null;
		_data._Clear();
		_data = null;
		_freeSlots.Clear();
		_freeSlots = null;
	}


	#region Enumeration
	public readonly ref struct RefEnumerable
	{
		private readonly RefSlotStore_NewSplit<T> _store;
		private readonly bool _includeFree;

		public RefEnumerable(RefSlotStore_NewSplit<T> store, bool includeFree)
		{
			_store = store;
			_includeFree = includeFree;
		}

		public RefEnumerator GetEnumerator()
		{
			return new RefEnumerator(_store, _includeFree);
		}
	}

	public ref struct RefEnumerator
	{
		private readonly Span<SlotHandle> _allocTrackerSpan;
		private readonly Span<T> _dataSpan;
		private readonly bool _includeFree;
		private int _index;

		public RefEnumerator(RefSlotStore_NewSplit<T> store, bool includeFree)
		{
			_allocTrackerSpan = CollectionsMarshal.AsSpan(store._allocTracker);
			_dataSpan = store._data.AsSpan();
			_includeFree = includeFree;
			_index = -1;
		}

		public bool MoveNext()
		{
			while (++_index < _allocTrackerSpan.Length)
			{
				if (_includeFree || _allocTrackerSpan[_index].IsAllocated)
				{
					return true;
				}
			}
			return false;
		}



		public RefValueTuple<SlotHandle, T> Current
		{
			get
			{
				//RefValueTuple<SlotHandle, T> unused = default;
				return new RefValueTuple<SlotHandle, T>(ref _allocTrackerSpan[_index], ref _dataSpan[_index]);
			}
		}
	}
	#endregion

}