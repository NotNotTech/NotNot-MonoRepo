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
using NotNot.Collections.SpanLike;

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

   /// <summary>
   /// Sorted list of free slot indices, maintained in ascending order (lowest first).
   /// Enables allocation to preferentially fill lowest gaps for better cache locality.
   /// </summary>
   private List<int> _freeSlots;

   /// <summary>
   /// Tracks the highest occupied slot index for O(1) GetMaxAllocatedIndex() operations.
   /// Updated when allocating new slots or freeing the last occupied slot.
   /// Value of -1 indicates no allocated slots.
   /// </summary>
   private int _lastOccupiedSlotIndex = -1;

   /// <summary>
   /// Lock for thread safety when allocating/freeing slots.
   /// </summary>
   private readonly Lock _lock = new();

   /// <summary>
   /// Internal lock for advanced scenarios requiring coordination with store operations.
   /// WARNING: Only use if you understand the locking contract. Improper use can cause deadlocks.
   /// </summary>
   [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
   public Lock _Lock_Unsafe => _lock;

   public RefSlotStore_NewSplit(int initialCapacity = 10)
   {

      _nextVersion = RefSlotStore_NewSplit_VersionTracker._initialVersion++;
      if (_nextVersion == 0)
      {
         _nextVersion = RefSlotStore_NewSplit_VersionTracker._initialVersion++;
      }




      // **Initialize the underlying storage** with the initial capacity.
      _allocTracker = new(initialCapacity);
      _data = new T[initialCapacity];
      _freeSlots = new List<int>(initialCapacity);
      _lastOccupiedSlotIndex = -1; // No allocated slots initially
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
   /// Preferentially reuses lowest available free slot index for improved cache locality.
   /// </summary>
   public SlotHandle AllocSlot()
   {
      lock (_lock)
      {
         var version = _nextVersion++;
         if (version == 0)
         {
            version = _nextVersion++;
         }
         int index;
         if (_freeSlots.Count > 0)
         {
            // **Reuse lowest free slot** for better cache locality
            index = _freeSlots[0];  // O(1) access
            _freeSlots.RemoveAt(0); // O(n) removal acceptable for small n
            __.DebugAssertIfNot(_freeSlots.Count == 0 || _freeSlots[0] > index, "free slots must remain sorted ascending");

            // Update last occupied index if reusing a slot beyond current max
            if (index > _lastOccupiedSlotIndex)
            {
               _lastOccupiedSlotIndex = index;
            }
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

            // Update last occupied index when growing storage
            if (index > _lastOccupiedSlotIndex)
            {
               _lastOccupiedSlotIndex = index;
            }
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

   /// <summary>
   /// Frees a single slot, inserting it into the sorted free list and updating last occupied index if needed.
   /// </summary>
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

         // Insert into sorted free list using binary search
         InsertSorted(slot.Index);

         _allocTracker[slot.Index] = default; // **Clear the slot's handle**
         _data[slot.Index] = default; // **Clear the slot's data**

         // Update _lastOccupiedSlotIndex if we freed the last occupied slot
         if (slot.Index == _lastOccupiedSlotIndex)
         {
            UpdateLastOccupiedIndex();
         }
      }
   }

   /// <summary>
   /// Inserts a free slot index into the sorted free list maintaining ascending order.
   /// Uses binary search for O(log n) search + O(n) insertion.
   /// </summary>
   private void InsertSorted(int index)
   {
      int pos = _freeSlots.BinarySearch(index);
      if (pos < 0)
      {
         pos = ~pos; // Convert to insertion index
      }
      _freeSlots.Insert(pos, index);

      __.DebugAssertIfNot(pos == 0 || _freeSlots[pos - 1] < index, "free list must be sorted ascending (pre)");
      __.DebugAssertIfNot(pos == _freeSlots.Count - 1 || _freeSlots[pos + 1] > index, "free list must be sorted ascending (post)");
   }

   /// <summary>
   /// Updates _lastOccupiedSlotIndex after freeing the last occupied slot.
   /// Walks backwards through sorted free list to find new last occupied index.
   /// </summary>
   private void UpdateLastOccupiedIndex()
   {
      int potentialLastOccupied = _lastOccupiedSlotIndex - 1;
      int freeIdx = _freeSlots.Count - 1; // Last free index (highest value in ascending list)

      while (freeIdx >= 0 && potentialLastOccupied >= 0)
      {
         int currentFree = _freeSlots[freeIdx];

         if (currentFree < potentialLastOccupied)
         {
            // Found new last occupied slot
            _lastOccupiedSlotIndex = potentialLastOccupied;
            return;
         }
         else if (currentFree == potentialLastOccupied)
         {
            // This slot is also free, keep searching backwards
            potentialLastOccupied--;
            freeIdx--;
         }
         else // currentFree > potentialLastOccupied
         {
            // Semi-dirty state (shouldn't happen but handle gracefully)
            _lastOccupiedSlotIndex = potentialLastOccupied;
            return;
         }
      }

      if (potentialLastOccupied < 0)
      {
         _lastOccupiedSlotIndex = -1; // No allocated slots remain
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

   /// <summary>
   /// Returns the maximum allocated index (last valid slot index).
   /// Returns -1 if no slots are allocated.
   /// Now O(1) via _lastOccupiedSlotIndex tracking.
   /// </summary>
   public int GetMaxAllocatedIndex()
   {
      lock (_lock)
      {
         return _lastOccupiedSlotIndex;
      }
   }

   /// <summary>
   /// Finds the last allocated (non-free) slot starting from startIndex and searching backwards.
   /// Returns -1 if no allocated slot found.
   /// </summary>
   public int FindLastAllocatedSlot(int startIndex)
   {
      lock (_lock)
      {
         for (int i = startIndex; i >= 0; i--)
         {
            if (i < _allocTracker.Count && _allocTracker[i].IsAllocated)
            {
               return i;
            }
         }
         return -1;
      }
   }

	/// <summary>
	/// Compacts the memory by relocating slots, moving free slots to the end.
	/// Returns a list of moved (allocated) slots with their new handles.
	/// After compaction, all allocated slots are contiguous from index 0, and all free slots are trailing.
	/// </summary>
	/// <returns>Mappings of (fromSlot, toSlot) for all relocated slots. Caller must update external references.</returns>
	/// <remarks>
	/// VIBEGUIDE: Compact() relocates allocated slots, invalidating external indices.
	/// - Callers MUST update external references using returned move mappings
	/// - ComponentPartition example: update component arrays in sync
	/// - NewArchetype example: update EntityPin.partitionSlotHandle
	/// - Thread safety: Caller should coordinate locking across systems
	/// - Version tracking: SlotHandle versions preserved during move
	/// Post-compaction: _freeSlots is cleared (trailing free slots don't need tracking)
	/// </remarks>
	public Mem<(SlotHandle fromSlot, SlotHandle toSlot)> Compact()
   {
      lock (_lock)
      {
         var moves = new List<(SlotHandle, SlotHandle)>();

         // _freeSlots already sorted ascending (lowest first)
         int lastAllocIndex = _lastOccupiedSlotIndex;

         foreach (var freeIndex in _freeSlots.ToList())  // ToList to avoid modifying during iteration
         {
            // Find next allocated slot from end (single backwards walk, no repeated searches)
            while (lastAllocIndex >= 0 && !_allocTracker[lastAllocIndex].IsAllocated)
            {
               lastAllocIndex--;
            }

            // Stop if free slot already at end or no more allocated slots
            if (lastAllocIndex < 0 || freeIndex >= lastAllocIndex)
            {
               break;
            }

            // Perform swap
            var fromSlot = _allocTracker[lastAllocIndex];
            var toSlot = new SlotHandle(freeIndex, fromSlot.Version, true);

            SwapSlots(fromSlot, toSlot);
            moves.Add((fromSlot, toSlot));

            // Update tracking
            _allocTracker[freeIndex] = toSlot;
            _allocTracker[lastAllocIndex] = default;

            // Move to next allocated slot
            lastAllocIndex--;
         }

         // Optimization: Clear free slots - all trailing slots are free but don't need tracking
         // Future allocations will grow storage from _lastOccupiedSlotIndex + 1
         _freeSlots.Clear();
         _lastOccupiedSlotIndex = lastAllocIndex;

         // DEBUG: Verify all slots after lastAllocIndex are free
         for (int i = lastAllocIndex + 1; i < _allocTracker.Count; i++)
         {
            __.DebugAssertIfNot(!_allocTracker[i].IsAllocated,
               "Expected trailing free slot but found allocated");
         }

         return Mem.Wrap(moves.ToArray());
      }
   }

   /// <summary>
   /// Swaps the data and handle between two slots atomically.
   /// Both slots must be valid and allocated.
   /// </summary>
   public void SwapSlots(SlotHandle fromSlot, SlotHandle toSlot)
   {
      lock (_lock)
      {
         var fromValidation = _IsHandleValid_Unsafe(fromSlot);
         var toValidation = _IsHandleValid_Unsafe(toSlot);

         __.DebugAssertIfNot(fromValidation.isValid, $"Invalid fromSlot: {fromValidation.invalidReason}");
         __.DebugAssertIfNot(toValidation.isValid, $"Invalid toSlot: {toValidation.invalidReason}");

         if (!fromValidation.isValid || !toValidation.isValid)
         {
            throw new InvalidOperationException($"Cannot swap invalid slots: from={fromValidation.invalidReason}, to={toValidation.invalidReason}");
         }

         int fromIndex = fromSlot.Index;
         int toIndex = toSlot.Index;

         // Swap data
         var tempData = _data[fromIndex];
         _data[fromIndex] = _data[toIndex];
         _data[toIndex] = tempData;

         // Swap handles in allocTracker
         var tempHandle = _allocTracker[fromIndex];
         _allocTracker[fromIndex] = _allocTracker[toIndex];
         _allocTracker[toIndex] = tempHandle;
      }
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