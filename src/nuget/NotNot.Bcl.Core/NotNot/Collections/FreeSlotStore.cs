// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;

namespace NotNot.Collections
{
	/// <summary>
	/// **FreeSlotStore** provides an array‐backed storage where individual slots can be freed for reuse.
	/// - When out of capacity, the backing array is **resized**.
	/// - **Thread safe writes** are ensured via locking.
	/// - **Non-blocking reads** are available via the indexer.
	/// </summary>
	/// <typeparam name="T">The type of item to store.</typeparam>
	public class FreeSlotStore<T>
	{
		private ResizableArray<T> _storage; // **Underlying resizable storage**

		/// <summary>
		/// **Used slots count**: calculated as total allocated minus free slots.
		/// </summary>
		public int Count => _storage.Length - _freeSlots.Count;

		/// <summary>
		/// **Count of free slots**.
		/// </summary>
		public int FreeCount => _freeSlots.Count;

		/// <summary>
		/// **Version** is incremented on every allocation or deallocation.
		/// </summary>
		public int Version { get; private set; }

#if DEBUG
		// **Tracks allocated slots** for debugging purposes.
		private ConcurrentDictionary<int, bool> _CHECKED_allocationTracker;
#endif

		private readonly Stack<int> _freeSlots; // **Stack to track free slot indices**
		private readonly object _lock = new();  // **Lock for thread safety**

		public FreeSlotStore(int initialCapacity = 10)
		{
			// **Initialize the underlying storage** with the initial capacity.
			_storage = new ResizableArray<T>(initialCapacity);
			_freeSlots = new Stack<int>(initialCapacity);
#if DEBUG
			_CHECKED_allocationTracker = new ConcurrentDictionary<int, bool>();
#endif
			// **Push all slots** into the free slots stack in reverse order.
			for (var i = initialCapacity - 1; i >= 0; i--)
			{
				_freeSlots.Push(i);
			}
		}

		/// <summary>
		/// **Indexer** to access elements by slot index.
		/// - **DEBUG check:** logs an error if the slot is not allocated.
		/// </summary>
		public T this[int slot]
		{
			get
			{
#if DEBUG
				// **DEBUG check:** verify that the slot is allocated.
				__.GetLogger()._EzError(_CHECKED_allocationTracker.ContainsKey(slot),
					"slot is not allocated and you are using it");
#endif
				return _storage[slot]; // **Non-blocking read**
			}
			set
			{
				lock (_lock)
				{
					_storage.Set(slot, value); // **Thread-safe write**
				}
			}
		}

		/// <summary>
		/// **Alloc** a free slot and set it with **data**.
		/// </summary>
		public int Alloc(T data)
		{
			var slot = Alloc();
			_storage.Set(slot, data);
			return slot;
		}

		/// <summary>
		/// **Alloc** a free slot and set it with **data** (by reference).
		/// </summary>
		public int Alloc(ref T data)
		{
			var slot = Alloc();
			_storage.Set(slot, data);
			return slot;
		}

		/// <summary>
		/// **Alloc** a free slot.
		/// - Increases **Version**.
		/// - If no free slot is available, expands the storage.
		/// - **DEBUG mode:** tracks allocation via _CHECKED_allocationTracker.
		/// </summary>
		public int Alloc()
		{
			lock (_lock)
			{
				Version++; // **Increment version for allocation**
				int slot;
				if (_freeSlots.Count > 0)
				{
					slot = _freeSlots.Pop(); // **Reuse a free slot**
#if DEBUG
					// **DEBUG:** Track the allocation in both branches.
					var added = _CHECKED_allocationTracker.TryAdd(slot, true);
					__.GetLogger()._EzError(added, "slot already allocated");
#endif
				}
				else
				{
					// **Grow storage:** no free slot available, so allocate a new slot.
					slot = _storage.Grow(1);
#if DEBUG
					var added = _CHECKED_allocationTracker.TryAdd(slot, true);
					__.GetLogger()._EzError(added, "slot already allocated");
#endif
				}
				return slot;
			}
		}

		/// <summary>
		/// **Frees** a previously allocated slot.
		/// - Increases **Version**.
		/// - **DEBUG mode:** validates that the slot is currently allocated.
		/// </summary>
		public void Free(int slot)
		{
			lock (_lock)
			{
				Version++; // **Increment version for deallocation**
#if DEBUG
				// **DEBUG:** Remove the slot from the allocation tracker.
				__.GetLogger()._EzError(_CHECKED_allocationTracker.TryRemove(slot, out var temp),
					"slot is not allocated but trying to remove");
#endif
				_freeSlots.Push(slot); // **Mark slot as free**
				_storage.Set(slot, default); // **Clear the slot's data**
			}
		}
	}
}
