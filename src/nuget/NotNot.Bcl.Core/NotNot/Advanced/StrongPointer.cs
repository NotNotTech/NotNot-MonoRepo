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
/// a lightweight handle to an owner object.  Useful for 1-to-many references without object reference overhead.
/// <para>More lightweight than WeakPointer{T} but requires explicit disposal. You must dispose this when done with it, prior to the target being disposed.</para>
/// <para>Treat this struct as a move-only handle: copying (assignment, pass-by-value, closure captures, etc.) duplicates the SlotHandle but not the ownership. Disposing any copy invalidates every copy, so all other copies must be considered dead immediately.</para>
/// <para>Disposal is one-shot and not idempotent; double-dispose or using a stale copy will trigger the RefSlotStore guard. Call <see cref="CheckIsAlive"/> if you need to verify a handle before use.</para>
/// </summary>
/// <typeparam name="T"></typeparam>

public record struct StrongPointer<T> : IDisposable, IComparable<StrongPointer<T>> where T : DisposeGuard //class, IDisposeGuard
{
	private static readonly RefSlotStore<T> _store = new(initialCapacity: 100);

	// Lock for thread-safe allocation operations
	private static readonly Lock _allocLock = new();

	// Cursor for incremental cleanup - tracks position in array
	private static int _cleanupCursor = 0;

	public static StrongPointer<T> Alloc(T target)
	{
		lock (_allocLock)
		{

			// Allocate slot in RefSlotStore
			var slotHandle = _store.Alloc(target);

			// Run incremental cleanup of 5 slots
			_GCNextSlots(5);

			// Return ManagedPointer with the SlotHandle
			return new StrongPointer<T>
			{
				_slotHandle = slotHandle
			};
		}
	}
	public int CompareTo(StrongPointer<T> other)
	{
		return _slotHandle.CompareTo(other._slotHandle);
	}

	/// <summary>
	/// Incremental cleanup - checks next 'count' slots for dead WeakReferences
	/// </summary>
	[Conditional("CHECKED")]
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

			var target = slot.slotData;
			__.AssertIfNot(target != null, "StrongPointer target is null - was it disposed without freeing the StrongPointer?");
			__.AssertIfNot(target.IsDisposed == false, "StrongPointer target is disposed - was it disposed without freeing the StrongPointer?");
			if (target.IsDisposed)
			{
				// Disposed reference found - free the slot, even though this should not happen if used correctly
				_store.Free(slot.handle);
			}
			_cleanupCursor++;
		}
	}


	public static void OnFree(StrongPointer<T> managedPointer)
	{
		_store.Free(managedPointer._slotHandle);
	}

	/// <summary>
	/// Internal handle to the slot in RefSlotStore
	/// </summary>
	private SlotHandle _slotHandle;

	public bool IsAllocated => _slotHandle.IsAllocated;

	public int Index => _slotHandle.Index;

	/// <summary>
	/// checks if this handle is allocated (valid) and if so, will check the backing store to ensure it still actually is.
	/// </summary>
	public bool CheckIsAlive()
	{
		if (!IsAllocated) return false;
		//var response = _store._IsHandleValid(_slotHandle).isValid;
		return _store._AsSpan_Unsafe()[_slotHandle.Index].handle == _slotHandle;
	}

	[Conditional("DEBUG")]
	public void AssertIsAlive()
	{
		__.AssertIfNot(CheckIsAlive());

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
		var target = _store[_slotHandle];

		if (target.IsDisposed)
		{
			throw new ObjectDisposedException("Target has been disposed already");
		}

		// Run incremental cleanup of 3 slots (must be inside lock for thread safety)
		lock (_allocLock)
		{
			_GCNextSlots(3);
		}

		return target;
	}


	public void Dispose()
	{
		if (IsAllocated)
		{
			OnFree(this);
		}
	}
}
