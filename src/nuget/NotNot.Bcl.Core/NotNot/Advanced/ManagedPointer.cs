// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NotNot.Advanced;

/// <summary>
/// a lightweight handle to an owner object, up to int.maxValue targets.
/// useful for tracking hundreds/thousands of refs to a single object without gc overhead
/// <para>should NOT be used for 1:1 references, only many-one.</para>
/// <para> you should always clean this up, using destructor or other lifecycle workflows.   uncleaned up references will be considered memory leaks and asserts will trigger.</para>
/// </summary>
public record struct ManagedPointer<T> : IDisposable where T : class
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

			}
			else
			{
				//allocate new owner slot
				var ownerSlot = Interlocked.Increment(ref _nextNewTargetSlot);
				__.ThrowIfNot(ownerSlot > 0, "can not allocate a handle.  consumed all possible handle owners, need a different implementation");

				freeOwnerSlot = new()
				{
					_location = ownerSlot,
					_version = 1,
				};

			}

			ManagedPointer<T> toReturn = new()
			{
				_idVersion = freeOwnerSlot,
			};
			var result = _targets.TryAdd(toReturn, new WeakReference<T>(target));
			__.ThrowIfNot(result);


			_CheckForMemoryLeaks();

			return toReturn;






		}
	}

	/// <summary>
	/// checks 10 randomly picked items from _targets to see if they are still alive.
	/// if not, will assert and clean them up.
	/// </summary>
	[Conditional("DEBUG")]
	private static void _CheckForMemoryLeaks()
	{
		_targets._TakeRandom(10).ForEach(kvp =>
		{
			if (kvp.Value.TryGetTarget(out var target) is false)
			{
				//__.GetLogger()._EzWarn(true, "found a leaked handle owner, cleaning up", null, typeof(ManagedPointer<T>).FullName);
				__.Assert($"found a leaked handle owner, cleaning up: {typeof(ManagedPointer<T>).FullName}");
				_targets.TryRemove(kvp.Key, out _);
				_freePointers.Enqueue(kvp.Key._idVersion);
			}
		});

	}


	public static void UnregisterTarget(ManagedPointer<T> managedPointer)
	{
		//lock (_ownerLock)
		{
			var result = _targets.TryRemove(managedPointer, out _);
			__.ThrowIfNot(result, "did not unregister handle owner");
			_freePointers.Enqueue(managedPointer._idVersion);

			_CheckForMemoryLeaks();
		}
	}

	/// <summary>
	/// for internal use.   use `.GetOwner()`
	/// </summary>
	public required IdVersion _idVersion;


	public bool IsAllocated => _idVersion.id != 0;

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

		if (!_targets.TryGetValue(this, out var weakRef))
		{
			throw new ObjectDisposedException("owner no longer exists, likely disposed");
		}
		if (weakRef.TryGetTarget(out var toReturn))
		{
			_CheckForMemoryLeaks();
			return toReturn;
		}
		else
		{
			this.AssertIsAlive();
			throw new ObjectDisposedException("owner no longer exists, likely disposed");
		}

	}

	public bool TryGetTarget(out T target)
	{
		this.AssertIsAlive();

		if (!_targets.TryGetValue(this, out var weakRef))
		{
			target = default;
			return false;
		}

		var toReturn = weakRef.TryGetTarget(out target);
		_CheckForMemoryLeaks();
		return toReturn;
	}

	public void Dispose()
	{
		if (!IsAllocated)
		{
			return;
		}
		ManagedPointer<T>.UnregisterTarget(this);
	}
}
