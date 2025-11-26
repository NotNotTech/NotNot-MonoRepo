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
public record struct WeakPointer<T> : IDisposable where T : class
{
	// RefSlotStore for storing WeakReference<T> - provides array-based access
	private static readonly RefSlotStore<WeakRef<T>> _store = new(initialCapacity: 100);

	// Lock for thread-safe allocation operations
	private static readonly Lock _allocLock = new();

	// Cursor for incremental cleanup - tracks position in array
	private static int _cleanupCursor = 0;

	public static WeakPointer<T> Alloc(T target)
	{
		lock (_allocLock)
		{
			// Create WeakReference for the target
			var weakRef = new WeakRef<T>(target);

			// Allocate slot in RefSlotStore
			var slotHandle = _store.AllocValue(ref weakRef);

			// Run incremental cleanup of 5 slots
			_GCNextSlots(5);

			// Return ManagedPointer with the SlotHandle
			return new WeakPointer<T>
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
		//var span = _store._AsSpan_Unsafe();

		if(_store.Count == 0) return;


		var dataSpan = _store.Data_Span;
		var handleSpan = _store.Handle_Span;


		for (int i = 0; i < count; i++)
		{
			// Wrap around at end of array
			if (_cleanupCursor >= dataSpan.Length)
			{
				_cleanupCursor = 0;
			}

			ref var r_weakRef = ref dataSpan[_cleanupCursor];
			ref var r_handle = ref handleSpan[_cleanupCursor];

			// Check if slot is allocated and WeakReference is dead
			if (r_handle.IsAllocated)
			{
				if (!r_weakRef.TryGetTarget(out _))
				{
					// Dead reference found - free the slot
					_store.FreeSingleSlot(r_handle);
				}
			}

			_cleanupCursor++;
		}
	}


	public static void Free(WeakPointer<T> managedPointer)
	{
		if (managedPointer._slotHandle.IsAllocated)
		{
			var weakRef = _store[managedPointer._slotHandle];
			_store.FreeSingleSlot(managedPointer._slotHandle);
			weakRef.Dispose();
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

		if (!weakRef.TryGetTarget(out var target))
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

		if (!_slotHandle.IsAllocated || !_store.IsHandleAlive(_slotHandle).isAlive)
		{
			target = default;
			return false;
		}

		// Get WeakReference from RefSlotStore
		var weakRef = _store[_slotHandle];

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
			Free(this);
		}
	}
}
