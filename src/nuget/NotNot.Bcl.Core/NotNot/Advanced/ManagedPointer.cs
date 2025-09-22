// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NotNot.Collections;

namespace NotNot.Advanced;

/// <summary>
/// a lightweight handle to an owner object, up to int.maxValue targets.
/// useful for tracking hundreds/thousands of refs to a single object without gc overhead
/// <para>should NOT be used for 1:1 references, only many-one.</para>
/// <para> you should always clean this up, using destructor or other lifecycle workflows.   uncleaned up references will be considered memory leaks and asserts will trigger.</para>
/// </summary>
public record struct ManagedPointer<T> : IDisposable where T : class
{
	// RefSlotStore for storing WeakReference<T> - provides array-based access
	private static readonly RefSlotStore<WeakReference<T>> _store = new(initialCapacity: 100);

	// Lock for thread-safe allocation operations
	private static readonly object _allocLock = new();

	// Cursor for incremental cleanup - tracks position in array
	private static int _cleanupCursor = 0;

	public static ManagedPointer<T> RegisterTarget(T target)
	{
		lock (_allocLock)
		{
			// Create WeakReference for the target
			var weakRef = new WeakReference<T>(target);

			// Allocate slot in RefSlotStore
			var slotHandle = _store.Alloc(weakRef);

			// Run incremental cleanup of 5 slots
			_GCNextSlots(5);

			// Return ManagedPointer with the SlotHandle
			return new ManagedPointer<T>
			{
				_slotHandle = slotHandle
			};
		}
	}

	/// <summary>
	/// Incremental cleanup - checks next 'count' slots for dead WeakReferences
	/// </summary>
	private static void _GCNextSlots(int count)
	{
		// Must be called within _allocLock to ensure thread safety with _AsSpan_Unsafe
		var span = _store._AsSpan_Unsafe();
		if (span.Length == 0) return;

		for (int i = 0; i < count; i++)
		{
			// Wrap around at end of array
			if (_cleanupCursor >= span.Length)
			{
				_cleanupCursor = 0;
			}

			var slot = span[_cleanupCursor];

			// Check if slot is allocated and WeakReference is dead
			if (slot.handle.IsAllocated && slot.slotData != null)
			{
				if (!slot.slotData.TryGetTarget(out _))
				{
					// Dead reference found - free the slot
					_store.Free(slot.handle);
				}
			}

			_cleanupCursor++;
		}
	}


	public static void UnregisterTarget(ManagedPointer<T> managedPointer)
	{
		if (managedPointer._slotHandle.IsAllocated)
		{
			_store.Free(managedPointer._slotHandle);
		}
	}

	/// <summary>
	/// Internal handle to the slot in RefSlotStore
	/// </summary>
	private SlotHandle _slotHandle;

	///// <summary>
	///// Compatibility shim for code that uses _idVersion
	///// </summary>
	//public IdVersion _idVersion => new IdVersion
	//{
	//	_location = (uint)_slotHandle.Index,
	//	_version = _slotHandle.Version
	//};

	public bool IsAllocated => _slotHandle.IsAllocated;

	[Conditional("DEBUG")]
	public void AssertIsAlive()
	{
		__.AssertIfNot(IsAllocated);
		
		//if (_targets.TryGetValue(this, out var weakRef) is false)
		//{
		//	throw new ObjectDisposedException("owner no longer exists, likely disposed");
		//}
		//if (weakRef.TryGetTarget(out var toReturn) is false)
		//{
		//	throw new ObjectDisposedException("owner no longer exists, likely disposed");
		//}
	}

	public T GetTarget()
	{
		this.AssertIsAlive();

		if (!_slotHandle.IsAllocated)
		{
			throw new ObjectDisposedException("Handle not allocated");
		}

		// Get WeakReference from RefSlotStore
		var weakRef = _store[_slotHandle];

		if (weakRef == null || !weakRef.TryGetTarget(out var target))
		{
			throw new ObjectDisposedException("Target has been garbage collected");
		}

		// Run incremental cleanup of 3 slots (must be inside lock for thread safety)
		lock (_allocLock)
		{
			_GCNextSlots(3);
		}

		return target;
	}

	public bool TryGetTarget(out T target)
	{
		this.AssertIsAlive();

		if (!_slotHandle.IsAllocated || !_store._IsHandleValid(_slotHandle).isValid)
		{
			target = default;
			return false;
		}

		// Get WeakReference from RefSlotStore
		var weakRef = _store[_slotHandle];

		if (weakRef == null)
		{
			target = default;
			return false;
		}

		// Run incremental cleanup of 3 slots (must be inside lock for thread safety)
		lock (_allocLock)
		{
			_GCNextSlots(3);
		}

		return weakRef.TryGetTarget(out target);
	}

	public void Dispose()
	{
		if (IsAllocated)
		{
			UnregisterTarget(this);
		}
	}
}
