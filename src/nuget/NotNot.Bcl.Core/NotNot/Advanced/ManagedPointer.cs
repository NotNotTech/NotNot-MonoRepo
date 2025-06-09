// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;

namespace NotNot.Advanced;

/// <summary>
/// a lightweight handle to an owner object, up to int.maxValue targets.
/// useful for tracking hundreds/thousands of refs to a single object without gc overhead
/// <para>should NOT be used for 1:1 references, only many-one.</para>
/// </summary>
public record struct ManagedPointer<T> where T : class
{
	//private static Lock _ownerLock = new();
	private static ConcurrentDictionary<ManagedPointer<T>, WeakReference<T>> _targets = new();
	private static uint _nextNewTargetSlot = 1;
	private static ConcurrentQueue<IdVersion> _freePointers = new();

	public static ManagedPointer<T> RegisterTarget(T target)
	{
		//lock (_ownerLock)
		{
			//try to reuse a previously freed owner slot
			if (_freePointers.TryDequeue(out var freeOwnerSlot))
			{
				freeOwnerSlot._version++;
				if (freeOwnerSlot._version == 0)
				{
					freeOwnerSlot._version = 1;
				}
				ManagedPointer<T> toReturn = new()
				{
					_idVersion = freeOwnerSlot,
				};
				var result = _targets.TryAdd(toReturn, new WeakReference<T>(target));
				__.AssertIfNot(result);
				return toReturn;
			}

			//allocate new owner slot
			{
				var ownerSlot = Interlocked.Increment(ref _nextNewTargetSlot);
				__.ThrowIfNot(ownerSlot > 0, "can not allocate a handle.  consumed all possible handle owners, need a different implementation");

				ManagedPointer<T> toReturn = new()
				{
					_idVersion = new()
					{
						_location = ownerSlot,
						_version = 1,
					},
				};
				var result = _targets.TryAdd(toReturn, new WeakReference<T>(target));
				__.AssertIfNot(result);
				return toReturn;
			}
		}
	}
	public static void UnregisterTarget(ManagedPointer<T> managedPointer)
	{
		//lock (_ownerLock)
		{
			var result = _targets.TryRemove(managedPointer, out _);
			__.ThrowIfNot(result, "did not unregister handle owner");
			_freePointers.Enqueue(managedPointer._idVersion);
		}
	}

	/// <summary>
	/// for internal use.   use `.GetOwner()`
	/// </summary>
	public required IdVersion _idVersion;


	public bool IsAllocated => _idVersion.id != 0;

	public void AssertIsAlive()
	{
		__.AssertIfNot(IsAllocated);

		if (_targets.TryGetValue(this, out var weakRef) is false)
		{
			throw new ObjectDisposedException("owner no longer exists, likely disposed");
		}
		if (weakRef.TryGetTarget(out var toReturn) is false)
		{
			throw new ObjectDisposedException("owner no longer exists, likely disposed");
		}
	}

	public T GetTarget()
	{
		var weakRef = _targets[this];
		if (weakRef.TryGetTarget(out var toReturn))
		{
			return toReturn;
		}
		else
		{
			throw new ObjectDisposedException("owner no longer exists, likely disposed");
		}
	}

	public bool TryGetTarget(T target)
	{
		var weakRef = _targets[this];

		return weakRef.TryGetTarget(out target);
	}




}