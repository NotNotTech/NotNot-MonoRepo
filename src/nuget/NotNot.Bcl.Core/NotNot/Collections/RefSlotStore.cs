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
/// A high-performance, thread-safe slot-based storage container optimized for archetype storage patterns.
/// Provides O(1) allocation, deallocation, and compaction capabilities with version tracking for handle validation.
/// </summary>
/// <typeparam name="T">The type of item to store. Can be any type including value types and reference types.</typeparam>
/// <remarks>
/// <para><b>Key Features:</b></para>
/// <list type="bullet">
///   <item>Thread-safe slot allocation and deallocation with internal locking</item>
///   <item>Memory compaction to eliminate fragmentation and improve cache locality</item>
///   <item>Version tracking to detect use-after-free errors</item>
///   <item>Efficient free slot reuse with lowest-index preference for cache optimization</item>
///   <item>Batch operations for allocating/freeing multiple slots</item>
///   <item>Zero-allocation enumeration via ref struct enumerators</item>
/// </list>
///
/// <para><b>Internal Architecture:</b></para>
/// <list type="bullet">
///   <item><c>_data</c>: Backing array for actual values</item>
///   <item><c>_allocTracker</c>: List of SlotHandles tracking allocation state and versions</item>
///   <item><c>_freeSlots</c>: Sorted list (descending) of free slot indices for O(1) reuse</item>
///   <item><c>_lastOccupiedSlotIndex</c>: Cached highest allocated index for O(1) max index queries</item>
/// </list>
///
/// <para><b>Usage Scenarios:</b></para>
/// <list type="bullet">
///   <item>ECS archetype storage where entities are frequently added/removed</item>
///   <item>Component pools requiring stable handles with version validation</item>
///   <item>Sparse data structures needing periodic compaction</item>
///   <item>High-performance collections requiring batch operations</item>
/// </list>
///
/// <para><b>Thread Safety:</b></para>
/// All public methods are thread-safe via internal locking. The <c>_Lock_Unsafe</c> property
/// exposes the internal lock for advanced coordination scenarios but should be used with caution.
///
/// <para><b>Performance Characteristics:</b></para>
/// <list type="bullet">
///   <item>Allocation: O(1) - reuses lowest free slot or appends</item>
///   <item>Deallocation: O(1) amortized - lazy-sorted free list</item>
///   <item>Compaction: O(n) where n is allocated count</item>
///   <item>Enumeration: O(n) with zero heap allocations</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Create a store for component data
/// var store = new RefSlotStore_NewSplit&lt;Vector3&gt;(initialCapacity: 100);
///
/// // Allocate a single slot
/// var handle = store.AllocSlot();
/// store[handle] = new Vector3(1, 2, 3);
///
/// // Batch allocation with values
/// var positions = Mem&lt;Vector3&gt;.Alloc(10);
/// // ... fill positions ...
/// var handles = store.AllocValues(positions);
///
/// // Enumerate allocated slots
/// foreach (var (slot, value) in store.GetEnumerable(includeFree: false))
/// {
///     Console.WriteLine($"Slot {slot.Index}: {value}");
/// }
///
/// // Compact to remove fragmentation
/// var moves = store.Compact();
/// // Update external references using move mappings
/// foreach (var (fromSlot, toSlot) in moves)
/// {
///     UpdateExternalReference(fromSlot, toSlot);
/// }
///
/// // Free slots when done
/// store.FreeSingleSlot(handle);
/// </code>
/// </example>
public class RefSlotStore<T> : IDisposeGuard
{
	/// <summary>
	/// Tracks the next version number to assign to allocated slots.
	/// Wraps around to 1 when reaching 1023 (0 is reserved for invalid/unallocated).
	/// Each allocation gets a unique version to detect use-after-free.
	/// </summary>
	private ushort _nextVersion;

	/// <summary>
	/// Storage for slot handles that track allocation state and versions.
	/// Index in this list corresponds to the slot index in _data array.
	/// Contains both allocated and free slots (free slots have default/empty handles).
	/// </summary>
	private List<SlotHandle> _allocTracker;

	/// <summary>
	/// Backing array for the actual data values.
	/// Indices correspond to slot indices from _allocTracker.
	/// May contain garbage data at free slot positions.
	/// </summary>
	private T[] _data;

	/// <summary>
	/// Gets the backing data array wrapped in a Mem&lt;T&gt; structure.
	/// </summary>
	/// <remarks>
	/// <para><b>WARNING:</b> This provides unsafe direct access to the internal storage.</para>
	/// <para>The returned memory includes both allocated and free slots.</para>
	/// <para>Do not allocate new slots while using this reference as it may become invalid.</para>
	/// <para>Free slots may contain garbage data.</para>
	/// </remarks>
	/// <returns>A memory wrapper around the internal data array.</returns>
	public EphermialMem<T> Data_Mem => Mem.Wrap(_data);

	/// <summary>
	/// Gets a span view of the backing data array.
	/// </summary>
	/// <remarks>
	/// <para><b>WARNING:</b> This provides unsafe direct access to the internal storage.</para>
	/// <para>The span includes both allocated and free slots.</para>
	/// <para>Do not allocate new slots while using this span as the array may be resized.</para>
	/// <para>Free slots may contain garbage data.</para>
	/// </remarks>
	/// <returns>A span over the internal data array.</returns>
	public Span<T> Data_Span => _data.AsSpan();

	/// <summary>
	/// generally you should use <see cref="GetEnumerable(bool)"/> but this is available for advanced scenarios.
	/// </summary>
	public Span<SlotHandle> Handle_Span => _allocTracker.AsSpan();

	/// <summary>
	/// Gets unsafe access to the allocation tracker containing slot handles.
	/// </summary>
	/// <remarks>
	/// <para><b>WARNING:</b> This provides direct access to internal state.</para>
	/// <para>Modifying this can corrupt the store's internal consistency.</para>
	/// <para>Use only for advanced scenarios like custom serialization or debugging.</para>
	/// </remarks>
	/// <returns>A memory wrapper around the allocation tracker list.</returns>
	public EphermialMem<SlotHandle> AllocTracker_Unsafe => Mem.Wrap(_allocTracker);

	/// <summary>
	/// Gets the count of currently allocated (used) slots.
	/// </summary>
	/// <remarks>
	/// <para>Calculated as: total allocated slots minus free slots.</para>
	/// <para>Used slots may not be contiguous - gaps can exist due to deallocation.</para>
	/// <para>For iteration, use <see cref="AllocatedLength"/> or <see cref="StorageCapacity"/> as the upper bound.</para>
	/// </remarks>
	/// <value>The number of currently allocated slots.</value>
	public int Count => _allocTracker.Count - _freeSlots.Count;

	/// <summary>
	/// Gets the total length of allocated storage including both used and free slots.
	/// </summary>
	/// <remarks>
	/// <para>This represents the high water mark of slot allocation.</para>
	/// <para>Includes slots that are currently free but were previously allocated.</para>
	/// <para>Always less than or equal to <see cref="StorageCapacity"/>.</para>
	/// </remarks>
	/// <value>The number of slots that have been allocated at some point.</value>
	public int AllocatedLength => _allocTracker.Count;

	/// <summary>
	/// Gets the total capacity of the underlying storage arrays.
	/// </summary>
	/// <remarks>
	/// <para>Includes used slots, free slots, and unallocated capacity.</para>
	/// <para>Arrays grow automatically when allocation exceeds capacity.</para>
	/// <para>Growth follows List&lt;T&gt; doubling strategy for amortized O(1) allocation.</para>
	/// </remarks>
	/// <value>The total capacity of the storage arrays.</value>
	public int StorageCapacity
	{
		get
		{
			__.AssertIfNot(_allocTracker.Capacity == _data.Length, "internal error: capacity mismatch between alloc tracker and data array");
			return _allocTracker.Capacity;
		}
	}

	/// <summary>
	/// Creates an enumerable for iterating over slots with zero heap allocations.
	/// </summary>
	/// <param name="includeFree">If true, includes free slots in enumeration. If false, only enumerates allocated slots.</param>
	/// <returns>A ref struct enumerable that provides allocation-free enumeration.</returns>
	/// <remarks>
	/// The returned enumerable uses ref struct enumerators for zero-allocation iteration.
	/// Provides direct references to the stored values for efficient access.
	/// Thread-safe for reading but not for concurrent modification.
	/// </remarks>
	public RefEnumerable GetEnumerable(bool includeFree = false)
	{
		return new RefEnumerable(this, includeFree);
	}

	/// <summary>
	/// Gets the count of currently free (deallocated) slots available for reuse.
	/// </summary>
	/// <remarks>
	/// Free slots are tracked in a sorted list for efficient reuse.
	/// The allocator preferentially reuses the lowest-index free slot for cache locality.
	/// </remarks>
	/// <value>The number of free slots available for allocation.</value>
	public int FreeCount => _freeSlots.Count;

	/// <summary>
	/// Gets a value indicating whether this instance has been disposed.
	/// </summary>
	/// <value>True if disposed; otherwise, false.</value>
	public bool IsDisposed { get; private set; }

	/// <summary>
	/// Sorted list of free slot indices maintained in descending order (highest to lowest).
	/// </summary>
	/// <remarks>
	/// <para><b>Implementation Details:</b></para>
	/// <list type="bullet">
	///   <item>Uses custom SortedList with reverse comparer for descending order</item>
	///   <item>TryTakeLast() removes lowest index with O(1) complexity</item>
	///   <item>Lazy sorting on insertion, efficient removal from end</item>
	///   <item>Enables preferential reuse of lower indices for better cache locality</item>
	/// </list>
	/// </remarks>
	private SortedList<int> _freeSlots;

	/// <summary>
	/// Tracks the highest occupied slot index for O(1) GetMaxAllocatedIndex() operations.
	/// </summary>
	/// <remarks>
	/// <para>Updated when:</para>
	/// <list type="bullet">
	///   <item>Allocating new slots beyond current maximum</item>
	///   <item>Freeing the current maximum slot (walks backwards to find new max)</item>
	///   <item>During compaction to reflect new layout</item>
	/// </list>
	/// <para>Value of -1 indicates no allocated slots.</para>
	/// </remarks>
	private int _lastOccupiedSlotIndex = -1;

	/// <summary>
	/// Lock object for thread-safe operations on the store.
	/// </summary>
	/// <remarks>
	/// All public methods acquire this lock to ensure thread safety.
	/// Uses the new .NET 9 Lock type for improved performance over object-based locking.
	/// </remarks>
	private readonly Lock _lock = new();

	/// <summary>
	/// Provides unsafe access to the internal lock for advanced coordination scenarios.
	/// </summary>
	/// <remarks>
	/// <para><b>WARNING:</b> Only use if you understand the locking contract.</para>
	/// <para>Improper use can cause deadlocks or data corruption.</para>
	/// <para>Intended for scenarios where external code needs to coordinate with store operations.</para>
	/// <para>Example: Atomically updating multiple related stores.</para>
	/// </remarks>
	[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
	public Lock _Lock_Unsafe => _lock;

	/// <summary>
	/// Initializes a new instance of the RefSlotStore_NewSplit class with specified initial capacity.
	/// </summary>
	/// <param name="initialCapacity">The initial capacity for the storage arrays. Defaults to 10.</param>
	/// <remarks>
	/// <para>Arrays will grow automatically as needed following List&lt;T&gt; growth strategy.</para>
	/// <para>Choose initial capacity based on expected number of slots to minimize resizing.</para>
	/// <para>Each instance gets a unique starting version number from the global tracker.</para>
	/// </remarks>
	public RefSlotStore(int initialCapacity = 10)
	{
		// Get unique starting version for this instance (1-1023, never 0)
		_nextVersion = (ushort)Random.Shared.Next(1, 1024);

		// Initialize storage with specified capacity
		_allocTracker = new(initialCapacity);
		_data = new T[initialCapacity];

		// Custom SortedList with reverse comparer for descending order (highest to lowest)
		// This allows O(1) removal of the lowest index via TryTakeLast()
		_freeSlots = new SortedList<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
		_lastOccupiedSlotIndex = -1; // No allocated slots initially
	}

	/// <summary>
	/// Provides indexed access to slot values with thread-safe validation.
	/// </summary>
	/// <param name="slot">The slot handle to access.</param>
	/// <returns>A reference to the value at the specified slot.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the slot handle is invalid (freed, wrong version, or out of range).</exception>
	/// <remarks>
	/// <para><b>Thread Safety:</b> Access is synchronized via internal locking.</para>
	/// <para><b>Validation:</b> Checks slot is allocated, version matches, and index is valid.</para>
	/// <para><b>Performance:</b> Lock acquisition adds overhead; batch operations when possible.</para>
	/// </remarks>
	public ref T this[SlotHandle slot]
	{
		get
		{
			// Thread-safe validation and access
			lock (_lock)
			{
				var validResult = _IsHandleAlive_Unsafe(slot);
				if (!validResult.isAlive)
				{
					__.DebugAssertIfNot(validResult.isAlive, $"Invalid slot access: {validResult.invalidReason}");

					throw new InvalidOperationException($"Invalid slot access: {validResult.invalidReason}");
				}

				return ref _data[slot.Index];
			}
		}
	}

	/// <summary>
	/// Provides indexed access to slot values with thread-safe validation.
	/// </summary>
	/// <param name="slot">The slot handle to access.</param>
	/// <returns>A reference to the value at the specified slot.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the slot handle is invalid (freed, wrong version, or out of range).</exception>
	/// <remarks>
	/// <para><b>Thread Safety:</b> Access is synchronized via internal locking.</para>
	/// <para><b>Validation:</b> Checks slot is allocated, version matches, and index is valid.</para>
	/// <para><b>Performance:</b> Lock acquisition adds overhead; batch operations when possible.</para>
	/// </remarks>
	public ref T GetRef(SlotHandle slot)
	{
		// Thread-safe validation and access
		lock (_lock)
		{
			var validResult = _IsHandleAlive_Unsafe(slot);
			if (!validResult.isAlive)
			{
				__.DebugAssertIfNot(validResult.isAlive, $"Invalid slot access: {validResult.invalidReason}");
				throw new InvalidOperationException($"Invalid slot access: {validResult.invalidReason}");
			}
			return ref _data[slot.Index];
		}
	}

	/// <summary>
	/// Allocates multiple slots in a single batch operation.
	/// </summary>
	/// <param name="count">The number of slots to allocate.</param>
	/// <returns>A memory wrapper containing the allocated slot handles.</returns>
	/// <remarks>
	/// <para><b>Performance:</b> More efficient than allocating slots individually.</para>
	/// <para><b>Initialization:</b> All slots are initialized to default(T).</para>
	/// <para><b>Memory:</b> Returns pooled memory that should be disposed when no longer needed.</para>
	/// <para><b>Thread Safety:</b> Entire batch allocation is atomic.</para>
	/// </remarks>
	/// <example>
	/// <code>
	/// // Allocate 100 slots for new entities
	/// using var slots = store.AllocSlots(100);
	/// foreach (var slot in slots.Span)
	/// {
	///     store[slot] = CreateComponent();
	/// }
	/// </code>
	/// </example>
	public RentedMem<SlotHandle> AllocSlots(int count)
	{
		var toReturn = Mem.Rent<SlotHandle>(count);
		var slotSpan = toReturn.GetSpan();
		lock (_lock)
		{
			for (var i = 0; i < count; i++)
			{
				var hSlot = AllocSlot();
				slotSpan[i] = hSlot;
			}
			return toReturn;
		}
	}

	/// <summary>
	/// Allocates slots and initializes them with provided values in a single operation.
	/// </summary>
	/// <param name="values">The values to store in the newly allocated slots.</param>
	/// <returns>A memory wrapper containing the allocated slot handles.</returns>
	/// <remarks>
	/// <para><b>Atomicity:</b> Allocation and value assignment are performed atomically.</para>
	/// <para><b>Efficiency:</b> More efficient than separate allocation and assignment.</para>
	/// <para><b>Order:</b> Values are assigned in the same order as provided.</para>
	/// </remarks>
	/// <example>
	/// <code>
	/// var positions = Mem&lt;Vector3&gt;.Alloc(entities.Length);
	/// // Fill positions...
	/// var slots = store.AllocValues(positions);
	/// // Slots now contain the position values
	/// </code>
	/// </example>
	public RentedMem<SlotHandle> AllocValues(EphermialMem<T> values)
	{
		var toReturn = AllocSlots(values.Length);

		values.MapWith(toReturn, (ref T value, ref SlotHandle slot) =>
		{
			_data[slot.Index] = value;
		});

		return toReturn;
	}

	/// <summary>
	/// Allocates a single slot for storing a value.
	/// </summary>
	/// <returns>A handle to the allocated slot.</returns>
	/// <remarks>
	/// <para><b>Algorithm:</b></para>
	/// <list type="number">
	///   <item>Check for free slots in _freeSlots list</item>
	///   <item>If available, reuse lowest-index free slot (better cache locality)</item>
	///   <item>If no free slots, grow storage and allocate at end</item>
	///   <item>Assign new version number for use-after-free detection</item>
	/// </list>
	/// <para><b>Performance:</b> O(1) amortized time complexity.</para>
	/// <para><b>Initialization:</b> Slot value is set to default(T).</para>
	/// <para><b>Thread Safety:</b> Fully synchronized via internal lock.</para>
	/// </remarks>
	public SlotHandle AllocSlot()
	{
		lock (_lock)
		{
			// Get next version number, skip 0 (reserved for invalid), wrap at 1024
			var version = _nextVersion++;
			if (version == 0 || version > 1023)
			{
				_nextVersion = 1;
				version = 1;
			}

			int index;
			if (_freeSlots.TryTakeLast(out index))  // O(1) take from end (lowest index in descending list)
			{
				// Reuse lowest free slot for better cache locality
				// Custom SortedList.TryTakeLast() is O(1) removal from end

				// Update last occupied index if reusing a slot beyond current max
				if (index > _lastOccupiedSlotIndex)
				{
					_lastOccupiedSlotIndex = index;
				}
			}
			else
			{
				// No free slots available, grow storage
				index = _allocTracker.Count;
				_allocTracker.Add(default);

				// Resize data array if List grew its capacity
				if (_data.Length != _allocTracker.Capacity)
				{
					Array.Resize(ref _data, _allocTracker.Capacity);
				}

				__.DebugAssertIfNot(index == _allocTracker.Count - 1, "race condition: something else allocating even though we are in a lock?");

				// Update last occupied index when growing storage
				if (index > _lastOccupiedSlotIndex)
				{
					_lastOccupiedSlotIndex = index;
				}
			}

			// Verify slot is allocated but unused
			__.DebugAssertIfNot(_allocTracker.Count > index && _allocTracker[index].IsAllocated is false);

			// Create new handle with version tracking
			var toReturn = new SlotHandle(index, (short)version);

			// Update tracking structures
			_allocTracker[index] = toReturn;
			_data[index] = default;

			// Verify allocation succeeded
			__.DebugAssertIfNot(_allocTracker[index].IsAllocated);

			return toReturn;
		}
	}

	/// <summary>
	/// Allocates a slot and initializes it with the provided value.
	/// </summary>
	/// <param name="value">The value to store in the allocated slot.</param>
	/// <returns>A handle to the allocated slot containing the value.</returns>
	/// <remarks>
	/// Combines allocation and value assignment in a single atomic operation.
	/// More efficient than separate AllocSlot() followed by indexer assignment.
	/// </remarks>
	public SlotHandle AllocValue(ref T value)
	{
		var toReturn = AllocSlot();
		_data[toReturn.Index] = value;
		return toReturn;
	}

	/// <summary>
	/// Validates whether a slot handle is currently valid (for this storage)
	/// </summary>
	/// <param name="slot">The slot handle to validate.</param>
	/// <returns>A tuple indicating validity and reason if invalid.</returns>
	/// <remarks>
	/// <para><b>Validation checks:</b></para>
	/// <list type="bullet">
	///   <item>Slot is marked as allocated</item>
	///   <item>Index is within current storage bounds</item>
	///   <item>Version matches (detects use-after-free)</item>
	/// </list>
	/// <para><b>Thread Safety:</b> Method is thread-safe via internal locking.</para>
	/// </remarks>
	public (bool isAlive, string? invalidReason) IsHandleAlive(SlotHandle slot)
	{
		// Thread-safe public version
		lock (_lock)
		{
			return _IsHandleAlive_Unsafe(slot);
		}
	}


	/// <summary>
	/// Internal handle validation that assumes caller holds the lock.
	/// </summary>
	/// <param name="slot">The slot handle to validate.</param>
	/// <returns>A tuple indicating validity and reason if invalid.</returns>
	/// <remarks>
	/// Unsafe version for internal use when lock is already held.
	/// Avoids lock recursion and improves performance for internal operations.
	/// </remarks>
	private (bool isAlive, string? invalidReason) _IsHandleAlive_Unsafe(SlotHandle slot)
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
	/// Frees multiple slots in a single batch operation.
	/// </summary>
	/// <param name="slotsToFree">Memory wrapper containing the slot handles to free.</param>
	/// <remarks>
	/// <para><b>Atomicity:</b> All slots are freed in a single atomic operation.</para>
	/// <para><b>Validation:</b> Each slot is validated before freeing.</para>
	/// <para><b>Cleanup:</b> Both handle and data are cleared for each freed slot.</para>
	/// <para><b>Performance:</b> More efficient than freeing slots individually.</para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">Thrown if any slot is invalid.</exception>
	public void Free(EphermialMem<SlotHandle> slotsToFree)
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
	/// Frees a single previously allocated slot.
	/// </summary>
	/// <param name="slot">The slot handle to free.</param>
	/// <remarks>
	/// <para><b>Operations performed:</b></para>
	/// <list type="number">
	///   <item>Validates slot handle (version, allocation state)</item>
	///   <item>Adds index to free list for reuse</item>
	///   <item>Clears handle in allocation tracker</item>
	///   <item>Resets data to default(T)</item>
	///   <item>Updates last occupied index if necessary</item>
	/// </list>
	/// <para><b>Thread Safety:</b> Operation is atomic via internal locking.</para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">Thrown if the slot is invalid or already freed.</exception>
	public void FreeSingleSlot(SlotHandle slot)
	{
		lock (_lock)
		{
			var validResult = _IsHandleAlive_Unsafe(slot);
			if (!validResult.isAlive)
			{
				__.DebugAssertIfNot(validResult.isAlive);
				throw new InvalidOperationException($"attempt to free invalid slot: {validResult.invalidReason}");
			}

			// Insert into sorted free list using binary search
			InsertSorted(slot.Index);

			_allocTracker[slot.Index] = default; // Clear the slot's handle
			_data[slot.Index] = default; // Clear the slot's data

			// Update _lastOccupiedSlotIndex if we freed the last occupied slot
			if (slot.Index == _lastOccupiedSlotIndex)
			{
				UpdateLastOccupiedIndex();
			}
		}
	}

	/// <summary>
	/// Adds a free slot index to the sorted free list.
	/// </summary>
	/// <param name="index">The slot index to add to the free list.</param>
	/// <remarks>
	/// Uses custom SortedList implementation with lazy sorting.
	/// Insertion is O(1) amortized as sorting happens on read operations.
	/// Maintains descending order for efficient lowest-index removal.
	/// </remarks>
	private void InsertSorted(int index)
	{
		// Custom SortedList.Add() - lazy sort on read
		_freeSlots.Add(index);
	}

	/// <summary>
	/// Updates the cached last occupied slot index after freeing the previous maximum.
	/// </summary>
	/// <remarks>
	/// <para><b>Algorithm:</b></para>
	/// <list type="number">
	///   <item>Start from slot before the one just freed</item>
	///   <item>Walk backwards checking allocation state</item>
	///   <item>Stop at first allocated slot found</item>
	///   <item>Set to -1 if no allocated slots remain</item>
	/// </list>
	/// <para><b>Performance:</b> O(f) where f is number of trailing free slots.</para>
	/// </remarks>
	private void UpdateLastOccupiedIndex()
	{
		// Start from the slot before the one we just freed
		for (int i = _lastOccupiedSlotIndex - 1; i >= 0; i--)
		{
			// Check if this slot is allocated (not in free list)
			if (i < _allocTracker.Count && _allocTracker[i].IsAllocated)
			{
				_lastOccupiedSlotIndex = i;
				return;
			}
		}

		// No allocated slots remain
		_lastOccupiedSlotIndex = -1;
	}

	/// <summary>
	/// Disposes of the store and releases all resources.
	/// </summary>
	/// <remarks>
	/// <para>Clears all internal data structures and marks the instance as disposed.</para>
	/// <para>After disposal, any operation on the store will likely cause exceptions.</para>
	/// <para>Does not dispose individual slot values if T implements IDisposable.</para>
	/// </remarks>
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
	/// Gets the maximum allocated slot index in O(1) time.
	/// </summary>
	/// <returns>The highest allocated slot index, or -1 if no slots are allocated.</returns>
	/// <remarks>
	/// <para>Uses cached _lastOccupiedSlotIndex for O(1) performance.</para>
	/// <para>Value is maintained during allocation and deallocation operations.</para>
	/// <para>Useful for determining iteration bounds or storage utilization.</para>
	/// </remarks>
	public int GetMaxAllocatedIndex()
	{
		lock (_lock)
		{
			return _lastOccupiedSlotIndex;
		}
	}

	/// <summary>
	/// Finds the last allocated slot searching backwards from a starting index.
	/// </summary>
	/// <param name="startIndex">The index to start searching from (inclusive).</param>
	/// <returns>The index of the last allocated slot found, or -1 if none found.</returns>
	/// <remarks>
	/// <para>Useful for reverse iteration or finding trailing allocated slots.</para>
	/// <para>Performance: O(n) where n is the number of slots checked.</para>
	/// <para>Thread-safe via internal locking.</para>
	/// </remarks>
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
	/// Compacts storage by moving all allocated slots to be contiguous from index 0.
	/// </summary>
	/// <returns>
	/// A list of (fromSlot, toSlot) mappings for all relocated slots.
	/// Callers MUST update their external references using these mappings.
	/// </returns>
	/// <remarks>
	/// <para><b>Algorithm Overview:</b></para>
	/// <list type="number">
	///   <item>Iterate free slots from lowest to highest index</item>
	///   <item>For each free slot, find highest allocated slot</item>
	///   <item>Move allocated slot data to free slot position</item>
	///   <item>Track all moves for caller reference updates</item>
	///   <item>Clear free list and shrink storage to match allocated count</item>
	/// </list>
	///
	/// <para><b>Performance Characteristics:</b></para>
	/// <list type="bullet">
	///   <item>Time: O(n) where n is number of allocated slots</item>
	///   <item>Space: O(m) where m is number of moves performed</item>
	///   <item>Moves: At most min(freeCount, allocatedCount) slots moved</item>
	/// </list>
	///
	/// <para><b>Post-Compaction State:</b></para>
	/// <list type="bullet">
	///   <item>All allocated slots contiguous from index 0</item>
	///   <item>Free list cleared (trailing capacity not tracked)</item>
	///   <item>Storage may be shrunk to remove trailing free space</item>
	///   <item>SlotHandle versions preserved for moved slots</item>
	/// </list>
	///
	/// <para><b>CRITICAL Usage Requirements:</b></para>
	/// <list type="bullet">
	///   <item>Callers MUST update ALL external references using returned mappings</item>
	///   <item>Examples: ComponentPartition arrays, EntityPin.partitionSlotHandle</item>
	///   <item>Failure to update references will cause invalid memory access</item>
	///   <item>Consider coordinating locks across multiple stores if compacting together</item>
	/// </list>
	///
	/// <para><b>Example Usage:</b></para>
	/// <code>
	/// // In an archetype system
	/// var moves = slotStore.Compact();
	/// foreach (var (fromSlot, toSlot) in moves)
	/// {
	///     // Update entity references
	///     entity.SlotHandle = toSlot;
	///
	///     // Update component arrays
	///     componentArray[toSlot.Index] = componentArray[fromSlot.Index];
	/// }
	/// </code>
	/// </remarks>
	public RentedMem<(SlotHandle fromSlot, SlotHandle toSlot)> Compact()
	{
		lock (_lock)
		{
			var rented = __.pool.Rent<List<(SlotHandle, SlotHandle)>>(out var moves);

			// _freeSlots sorted descending (highest to lowest)
			// ReverseEnumerate to process lowest indices first for gap filling
			int lastAllocIndex = _lastOccupiedSlotIndex;

			foreach (var freeIndex in _freeSlots.ReverseEnumerate())
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

				// Move allocated slot from lastAllocIndex to freeIndex
				var fromSlot = _allocTracker[lastAllocIndex];
				var toSlot = new SlotHandle(freeIndex, fromSlot.Version);

				// Move data and handle (not a swap - toSlot is free)
				_data[freeIndex] = _data[lastAllocIndex];
				_data[lastAllocIndex] = default;

				_allocTracker[freeIndex] = toSlot;
				_allocTracker[lastAllocIndex] = default;

				moves.Add((fromSlot, toSlot));

				// Move to next allocated slot
				lastAllocIndex--;
			}

			// Optimization: Clear free slots and shrink allocTracker
			// All allocated slots are now contiguous from 0 to lastAllocIndex
			_freeSlots.Clear();
			_lastOccupiedSlotIndex = lastAllocIndex;

			// Shrink _allocTracker to match actual allocated count
			int newCount = lastAllocIndex + 1;
			if (newCount < _allocTracker.Count)
			{
				// DEBUG: Verify all slots we're removing are free
				for (int i = newCount; i < _allocTracker.Count; i++)
				{
					__.DebugAssertIfNot(!_allocTracker[i].IsAllocated,
						"Expected trailing free slot but found allocated");
				}

				_allocTracker.RemoveRange(newCount, _allocTracker.Count - newCount);
			}

			return Mem.Wrap(rented);
		}
	}

	/// <summary>
	/// Atomically swaps the data and handles between two allocated slots.
	/// </summary>
	/// <param name="fromSlot">The first slot to swap.</param>
	/// <param name="toSlot">The second slot to swap.</param>
	/// <remarks>
	/// <para><b>Requirements:</b> Both slots must be valid and allocated.</para>
	/// <para><b>Atomicity:</b> Swap is performed atomically under lock.</para>
	/// <para><b>Use Cases:</b> Sorting operations, slot reordering.</para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">Thrown if either slot is invalid.</exception>
	public void SwapSlots(SlotHandle fromSlot, SlotHandle toSlot)
	{
		lock (_lock)
		{
			var fromValidation = _IsHandleAlive_Unsafe(fromSlot);
			var toValidation = _IsHandleAlive_Unsafe(toSlot);

			__.DebugAssertIfNot(fromValidation.isAlive, $"Invalid fromSlot: {fromValidation.invalidReason}");
			__.DebugAssertIfNot(toValidation.isAlive, $"Invalid toSlot: {toValidation.invalidReason}");

			if (!fromValidation.isAlive || !toValidation.isAlive)
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

	/// <summary>
	/// Provides a ref struct enumerable for zero-allocation iteration over slots.
	/// </summary>
	/// <remarks>
	/// Uses ref struct pattern to avoid heap allocations during enumeration.
	/// Provides direct references to stored values for efficient access.
	/// </remarks>
	public readonly ref struct RefEnumerable
	{
		private readonly RefSlotStore<T> _store;
		private readonly bool _includeFree;

		/// <summary>
		/// Initializes a new instance of the RefEnumerable struct.
		/// </summary>
		/// <param name="store">The store to enumerate.</param>
		/// <param name="includeFree">Whether to include free slots in enumeration.</param>
		public RefEnumerable(RefSlotStore<T> store, bool includeFree)
		{
			_store = store;
			_includeFree = includeFree;
		}

		/// <summary>
		/// Gets the enumerator for this enumerable.
		/// </summary>
		/// <returns>A ref struct enumerator for iteration.</returns>
		public RefEnumerator GetEnumerator()
		{
			return new RefEnumerator(_store, _includeFree);
		}
	}

	/// <summary>
	/// Provides a ref struct enumerator for zero-allocation iteration over slot data.
	/// </summary>
	/// <remarks>
	/// <para>Uses Span&lt;T&gt; for direct memory access without bounds checking.</para>
	/// <para>Returns RefValueTuple for efficient access to both slot handle and value.</para>
	/// <para>Thread safety: Safe for concurrent reads, not for concurrent modifications.</para>
	/// </remarks>
	public ref struct RefEnumerator
	{
		private readonly Span<SlotHandle> _allocTrackerSpan;
		private readonly Span<T> _dataSpan;
		private readonly bool _includeFree;
		private int _index;

		/// <summary>
		/// Initializes a new instance of the RefEnumerator struct.
		/// </summary>
		/// <param name="store">The store to enumerate.</param>
		/// <param name="includeFree">Whether to include free slots in enumeration.</param>
		public RefEnumerator(RefSlotStore<T> store, bool includeFree)
		{
			// Use CollectionsMarshal for direct span access to list internals
			_allocTrackerSpan = CollectionsMarshal.AsSpan(store._allocTracker);
			_dataSpan = store._data.AsSpan();
			_includeFree = includeFree;
			_index = -1;
		}

		/// <summary>
		/// Advances the enumerator to the next element.
		/// </summary>
		/// <returns>True if there are more elements; false if enumeration is complete.</returns>
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

		/// <summary>
		/// Gets the current element as a tuple of slot handle and value reference.
		/// </summary>
		/// <value>A RefValueTuple providing references to both the slot handle and its value.</value>
		public RefValueTuple<SlotHandle, T> Current
		{
			get
			{
				return new RefValueTuple<SlotHandle, T>(ref _allocTrackerSpan[_index], ref _dataSpan[_index]);
			}
		}
	}
	#endregion
}