// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using NotNot.Advanced;
using NotNot.Collections;

namespace NotNot.Collections
{

	/// <summary>
	/// A read-only handle for referencing an allocated slot in a `RefSlotStore{T}` collection.
	/// Packed into 64 bits for maximum performance:
	/// - Bit 63: IsAllocated (1 bit)
	/// - Bits 40-62: CollectionId (23 bits, max 8,388,607)
	/// - Bits 32-39: Version (8 bits, max 255)
	/// - Bits 0-31: Index (32 bits, max 4,294,967,295)
	/// </summary>
	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
	public readonly record struct SlotHandle
	{
		/// <summary>
		/// Direct access to the packed value for performance-critical scenarios
		/// </summary>
		public readonly BitFlags64 _packedValue;

		public static SlotHandle Empty { get; } = default;


		/// <summary>
		/// Creates a SlotHandle from a packed value
		/// </summary>
		public SlotHandle(ulong packed)
		{
			_packedValue = packed;
			_AssertOk();
		}

		/// <summary>
		/// Creates a new SlotHandle with the specified values
		/// </summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		public SlotHandle(int index, byte version, int collectionId, bool isAllocated)
		{
			// Validate ranges
			__.AssertIfNot(collectionId >= 0 && collectionId <= 0x7FFFFF, "CollectionId must fit in 23 bits");
			__.AssertIfNot(version <= 0xFF, "Version must fit in 8 bits");

			// Pack the values
			_packedValue = ((ulong)(isAllocated ? 1 : 0) << 63) |
					  ((ulong)(collectionId & 0x7FFFFF) << 40) |
					  ((ulong)(version & 0xFF) << 32) |
					  ((ulong)index & 0xFFFFFFFF);

			_AssertOk();
		}

		/// <summary>
		/// for internal use only: internal reference to the array/list location of the data
		/// </summary>
		public int Index
		{
			get => (int)(_packedValue & 0xFFFFFFFF);
		}

		/// <summary>
		/// for internal use only: ensures the handle is not reused after freeing
		/// </summary>
		public byte Version
		{
			get => (byte)((_packedValue >> 32) & 0xFF);
		}

		/// <summary>
		/// for internal use only: ensures this handle is not used across different collections
		/// </summary>
		public int CollectionId
		{
			get => (int)((_packedValue >> 40) & 0x7FFFFF);
		}

		/// <summary>
		/// mostly for internal use: if the handle was properly allocated by a collection
		/// </summary>
		public bool IsAllocated
		{
			get => (_packedValue & 0x8000000000000000UL) != 0;
		}

		[Conditional("DEBUG")]
		private void _AssertOk()
		{
			__.AssertIfNot(IsAllocated);
			__.AssertIfNot(Version > 0, "assume version is >=1.  will remove IsAllocated bit to use that instead");

		}

		public RefSlotStore<TSlotStore> GetBackingCollection<TSlotStore>()
		{
			RefSlotStore._allStoresByCollectionId.TryGetValue(CollectionId, out var weakRef);
			if (weakRef is null || weakRef.TryGetTarget(out var store) is false)
			{
				throw new InvalidOperationException("Backing collection for SlotHandle not found (it may have been disposed)");
			}
			return (RefSlotStore<TSlotStore>)store;
		}
	}

	public abstract class RefSlotStore
	{

		/// <summary>
		/// used to ensure SlotHandle not used across different collections
		/// </summary>
		protected static int _nextCollectionId = 1;

		/// <summary>
		/// Stack of freed CollectionIds available for reuse
		/// we also track version to avoid collection id reuse also reusing the same default version (1)
		/// </summary>
		protected internal static readonly ConcurrentQueue<(int collectionId, byte nextVersion)> _freeCollectionIds = new();

		/// <summary>
		/// the collection id for this store
		/// </summary>
		public int CollectionId { get; protected set; }

		public static readonly ConcurrentDictionary<int, WeakReference<RefSlotStore>> _allStoresByCollectionId = new();

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
		private byte _nextVersion = 1;

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


			//"Maximum number of RefSlotStore instances exceeded (8,388,607)"
			//out of collection ids, so reuse freed ones
			if (_freeCollectionIds.TryDequeue(out var tuple))
			{
				CollectionId = tuple.collectionId;
				this._nextVersion = tuple.nextVersion;
			}
			else
			{
				// Allocate CollectionId (reuse freed ones or get new one)
				CollectionId = _nextCollectionId++;
				// Ensure we don't exceed 23-bit limit
				if (_nextCollectionId > 0x7FFFFF)
				{
					throw new InvalidOperationException("Maximum number of RefSlotStore instances exceeded (8,388,607) and no freed CollectionIds available");
				}
			}



			// **Initialize the underlying storage** with the initial capacity.
			_storage = new(initialCapacity);
			_freeSlots = new Stack<int>(initialCapacity);

			//below NOT NEEDED: list has Count=0 now, so can let it grow/freSlot organically
			//// **Push all slots** into the free slots stack in reverse order.
			//for (var i = initialCapacity - 1; i >= 0; i--)
			//{
			//	_freeSlots.Push(i);
			//}

			// Register this store in the global dictionary for handle validation
			if (_allStoresByCollectionId.TryGetValue(CollectionId, out var existingStoreWeakRef))
			{
				if (existingStoreWeakRef.TryGetTarget(out var existingStore))
				{
					throw new InvalidOperationException("CollectionId reuse detected while previous store is still alive. This should not happen.");
				}
			}
			_allStoresByCollectionId[CollectionId] = new WeakReference<RefSlotStore>(this);
		}


		/// <summary>
		/// Destructor that returns the CollectionId to the free pool
		/// </summary>
		~RefSlotStore()
		{
			// Return the CollectionId to the free pool for reuse
			_freeCollectionIds.Enqueue((CollectionId, _nextVersion));

			// Remove this store from the global dictionary
			_allStoresByCollectionId._TryRemoveIf(CollectionId, (key, existingStoreWeakRef) => existingStoreWeakRef._TargetRefEquals(this));
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
			lock (_lock)
			{
				var toReturn = Alloc();
				_storage[toReturn.Index] = (toReturn, data);


				if (OnAlloc is not null)
				{
					OnAlloc.Invoke(toReturn);
				}

				return toReturn;
			}
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


				var toReturn = new SlotHandle(index, version, CollectionId, true);

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
			if (slot.CollectionId != CollectionId)
			{
				return (false, "wrong CollectionId");
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
}

/// <summary>
/// For advanced performance situations only.
/// Provides a tuple-like ref struct that groups a reference to a slot handle and a reference to a value of type
/// <typeparamref name="T"/>. Designed for scenarios where both references need to be passed or manipulated together
/// without heap allocation.
/// </summary>
/// <remarks>Because <see langword="ref struct"/> types cannot be boxed or captured by closures, <see
/// cref="RefTuple{T}"/> is suitable for stack-only usage and cannot be stored in fields of classes or used across
/// async/await boundaries. This type is typically used in performance-critical or low-level APIs where passing
/// references together is required.</remarks>
/// <typeparam name="T">The type of the value referenced by the <see cref="data"/> field.</typeparam>
public ref struct RefTuple<T>
{
	public ref SlotHandle handle;
	public ref T data;
}
