// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using NotNot.Collections;

namespace NotNot.Advanced;

/// <summary>
/// A lightweight handle to an owner object. Useful for 1-to-many references without object reference overhead.
/// <para>More lightweight than WeakPointer{T} but requires explicit disposal. You must dispose this when done with it, prior to the target being disposed.</para>
/// <para>Treat this struct as a move-only handle: copying (assignment, pass-by-value, closure captures, etc.) duplicates the SlotHandle but not the ownership. Disposing any copy invalidates every copy, so all other copies must be considered dead immediately.</para>
/// <para>Disposal checks handle validity before freeing, making it safe to call Dispose on stale copies (though using stale copies for other operations will throw).</para>
/// <para>In CHECKED builds, incremental validation runs during allocation to detect targets disposed without disposing their StrongPointer.</para>
/// </summary>
/// <typeparam name="T">The target type, must be a class implementing IDisposeGuard</typeparam>
public record struct StrongPointer<T> : IDisposable, IComparable<StrongPointer<T>> where T : class, IDisposeGuard
{
	private static readonly RefSlotStore<T> _store = new(initialCapacity: 100);
	
	// Cursor for incremental validation - tracks position in store for next check
	private static int _validationCursor = 0;

	/// <summary>
	/// Allocates a new StrongPointer handle for the given target.
	/// In CHECKED builds, also performs incremental validation to detect improperly disposed targets.
	/// </summary>
	/// <param name="target">The target object to create a pointer to</param>
	/// <returns>A new StrongPointer handle</returns>
	public static StrongPointer<T> Alloc(T target)
	{
		__.AssertIfNot(target != null, "Cannot create StrongPointer to null target");
		__.AssertIfNot(!target.IsDisposed, "Cannot create StrongPointer to already-disposed target");

		var slotHandle = _store.AllocValue(ref target);

		// Run incremental validation in CHECKED builds to detect usage errors
		_ValidateNextSlots(5);

		return new StrongPointer<T>
		{
			_slotHandle = slotHandle
		};
	}

	/// <summary>
	/// Incremental validation - checks next 'count' slots for targets disposed without disposing their StrongPointer.
	/// Only runs in CHECKED builds. Uses unsafe store access under store's lock to coordinate with allocations.
	/// </summary>
	/// <param name="count">Number of slots to check this iteration</param>
	[Conditional("CHECKED")]
	private static void _ValidateNextSlots(int count)
	{
		lock (_store._Lock_Unsafe)
		{
			// Access store's internal data for validation under store's lock
			// This coordinates with allocations/frees to prevent concurrent modifications
			var allocTracker = _store.AllocTracker_Unsafe;
			var dataSpan = _store.Data_Span;
			
			var len = Math.Min(allocTracker.Length, dataSpan.Length);
			if (len == 0) return;

			for (int i = 0; i < count; i++)
			{
				// Wrap around at end of array
				if (_validationCursor >= len)
				{
					_validationCursor = 0;
				}

				var handle = allocTracker.GetSpan()[_validationCursor];
				
				// Only check allocated slots
				if (handle.IsAllocated)
				{
					var target = dataSpan[_validationCursor];
					
					// Check for the error condition: target disposed but StrongPointer not disposed
					if (target != null && target.IsDisposed)
					{
						__.AssertIfNot(false, 
							$"StrongPointer ERROR: Target at index {_validationCursor} is disposed but its StrongPointer was not disposed. " +
							$"Always dispose StrongPointer before disposing the target, or use CheckIsAlive() before accessing.");
					}
				}

				_validationCursor++;
			}
		}
	}

	/// <summary>
	/// Compares this handle to another based on internal slot handle value.
	/// </summary>
	/// <param name="other">The other handle to compare to</param>
	/// <returns>Comparison result following IComparable contract</returns>
	public int CompareTo(StrongPointer<T> other)
	{
		return _slotHandle.CompareTo(other._slotHandle);
	}

	/// <summary>
	/// Internal handle to the slot in RefSlotStore
	/// </summary>
	private SlotHandle _slotHandle;

	/// <summary>
	/// Indicates whether this handle has been allocated and not yet disposed.
	/// </summary>
	public bool IsAllocated => _slotHandle.IsAllocated;

	/// <summary>
	/// The internal index of this handle in the backing store.
	/// </summary>
	public int Index => _slotHandle.Index;

	public override string ToString()
	{
		if (IsAllocated && TryGetTarget(out var target))
		{
			return $"[{Index}]{target.GetType().Name}";
		}
		return "NOT_ALLOC";
	}

	/// <summary>
	/// Checks if this handle is allocated and valid in the backing store.
	/// Thread-safe.
	/// </summary>
	/// <returns>True if the handle is valid and alive, false otherwise</returns>
	public bool CheckIsAlive()
	{
		//if (!IsAllocated) return false;
		return _store.IsHandleAlive(_slotHandle);
	}

	/// <summary>
	/// Debug-only assertion that this handle is alive.
	/// Throws if handle is invalid.
	/// </summary>
	[Conditional("DEBUG")]
	public void AssertIsAlive()
	{
		__.AssertIfNot(CheckIsAlive(), "StrongPointer handle is not alive");
	}

	/// <summary>
	/// Attempts to get the target object referenced by this handle.
	/// </summary>
	/// <param name="target">The target if handle is valid; default if not. Do NOT use target when returns false.</param>
	/// <returns>True if handle is valid and target is alive; false otherwise.</returns>
	public bool TryGetTarget(out T target)
	{
		if (!CheckIsAlive())
		{
			target = default!;
			return false;
		}

		target = _store[_slotHandle];

		if (target.IsDisposed)
		{
			target = default!;
			return false;
		}

		return true;
	}

	/// <summary>
	/// Gets the target object referenced by this handle.
	/// Throws if handle is invalid or target has been disposed.
	/// </summary>
	/// <returns>The target object</returns>
	/// <exception cref="ObjectDisposedException">If handle is invalid or target is disposed</exception>
	public T GetTarget()
	{
		// Runtime validation (not debug-only) - critical for release builds
		if (!CheckIsAlive())
		{
			throw new ObjectDisposedException("StrongPointer handle is not alive - was it disposed or is it a stale copy?");
		}

		var target = _store[_slotHandle];

		if (target.IsDisposed)
		{
			throw new ObjectDisposedException("Target has been disposed already");
		}

		return target;
	}

	/// <summary>
	/// Disposes this handle, freeing its slot in the backing store.
	/// Safe to call on stale copies - will validate handle before freeing.
	/// After disposal, IsAllocated will return false on the copy that called Dispose,
	/// but other copies will still have the old (now invalid) handle value.
	/// </summary>
	public void Dispose()
	{
		// Check if handle is still valid before attempting to free
		// This makes Dispose safe to call on stale copies (from struct copying)
		if (CheckIsAlive())
		{
			_store.FreeSingleSlot(_slotHandle);
		}
		
		// Clear handle on this copy to make IsAllocated accurate
		// Note: other struct copies will still have the old handle value
		_slotHandle = default;


      // Run incremental validation in CHECKED builds to detect usage errors
      _ValidateNextSlots(5);
   
	}
}
