// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;

namespace NotNot.Collections
{

	/// <summary>
	/// A read-only handle for referencing an allocated slot in a `RefSlotStore{T}` collection.
	/// </summary>
	public record struct SlotHandle
	{
		public required int SlotId { get; init; }
		public required int Version { get; init; }

		public required int CollectionId { get; init; }

		public bool IsEmpty => Version == 0;

	}


	/// <summary>
	/// provides a List backed storage where individual slots can be freed for reuse.
	/// - When out of capacity, the backing List is automatically resized
	/// Concurrent Reads/Writes/Free() are supported, but Alloc() must be done exclusively.
	/// using the indexer returns a **ref** to the stored item.
	/// for doing reads/edits in bulk, use the `_AsSpan_Unsafe()` method.
	/// </summary>
	/// <typeparam name="T">The type of item to store.</typeparam>
	public class RefSlotStore<T>
	{
		/// <summary>
		/// used to ensure SlotHandle not used across different collections
		/// </summary>
		private static int _nextCollectionId = 1;

		/// <summary>
		/// the collection id for this store
		/// </summary>
		public int CollectionId { get; } = _nextCollectionId++;

		/// <summary>
		/// tracks allocations, to ensure a slot is not used after being freed.
		/// </summary>
		private int _nextVersion = 1;

		/// <summary>
		/// storage for the data and their handles (for tracking lifetime)
		/// </summary>
		private List<(SlotHandle handle, T data)> _storage;

		/// <summary>
		/// for use when reading/writing existing elements in bulk.  be sure not to allocate when using this (but free is ok)
		/// </summary>
		/// <returns></returns>
		public Span<(SlotHandle handle, T data)> _AsSpan_Unsafe() => _storage._AsSpan_Unsafe();

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
			// **Initialize the underlying storage** with the initial capacity.
			_storage = new(initialCapacity);
			_freeSlots = new Stack<int>(initialCapacity);


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
		public ref T this[SlotHandle slot]
		{
			get
			{
				var currentVersion = _nextVersion;
				__.DebugAssert(_VerifyHandle(slot).isValid);
				ref var toReturn  = ref _storage._AsSpan_Unsafe()[slot.SlotId].data; // **Non-blocking read**
				__.DebugAssert(currentVersion==_nextVersion,"race condition: an allocation occured during entity read/write.  Don't do this, as the array could be resized during allocation, causing you to loose write data");

				return ref toReturn;

			}
		}


		/// <summary>
		/// **Alloc** a free slot and set it with **data**.
		/// </summary>
		public SlotHandle Alloc(T data)
		{
			var slot = Alloc();
			_storage[slot.SlotId] = (slot, data);
			return slot;
		}
		public SlotHandle Alloc(ref T data)
		{
			var handle = Alloc();
			ref var p_element = ref _storage._AsSpan_Unsafe()[handle.SlotId];
			//p_element.handle = handle;
			p_element.data = data;

			return handle;
		}



		/// <summary>
		/// **Alloc** a free slot.
		/// - Increases **Version**.
		/// - If no free slot is available, expands the storage.
		/// - **DEBUG mode:** tracks allocation via _CHECKED_allocationTracker.
		/// </summary>
		public SlotHandle Alloc()
		{
			lock (_lock)
			{
				var version = _nextVersion++;
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
					__.DebugAssert(index == _storage.Count - 1, "race condition: something else allocating even though we are in a lock?");
				}
				ref var p_element = ref _storage._AsSpan_Unsafe()[index];


				//verify allocated but unused
				__.DebugAssert(_storage.Count > index && p_element.handle.IsEmpty);



				var toReturn = new SlotHandle()
				{
					SlotId = index,
					CollectionId = CollectionId,
					Version = version,
				};

				p_element.handle = toReturn;
				return toReturn;
			}
		}

		private (bool isValid, string? invalidReason) _VerifyHandle(SlotHandle slot)
		{

			if (slot.IsEmpty)
			{
				return (false, "slot.IsEmpty");
			}
			if (slot.CollectionId != CollectionId)
			{
				return (false, "wrong CollectionId");
			}

			if (_storage.Count <= slot.SlotId)
			{
				return (false, "storage not long enough");
			}

			if (_storage[slot.SlotId].handle.Version == slot.Version)
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
			__.DebugAssert(_VerifyHandle(slot).isValid);

			lock (_lock)
			{
				_freeSlots.Push(slot.SlotId); // **Mark slot as free**
				_storage[slot.SlotId] = default; // **Clear the slot's data**
			}
		}
	}
}
