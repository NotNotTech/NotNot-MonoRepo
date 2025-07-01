// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]


// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Collections._unused;

/// <summary>
///    array optimized for struct data storage. thread safe alloc/dealloc.
///    <para>If you want to store objects, see the SlotStore class</para>
/// </summary>
/// <typeparam name="TData"></typeparam>
[ThreadSafety(ThreadSituation.Query, ThreadSituation.ReadExisting, ThreadSituation.Remove)]
public class StructSlotArray<TData> //where TData : struct
{
	public int Count => Capacity - _freeSlots.Count;
	public int Capacity => _storage.Length;
	public int FreeCount => _freeSlots.Count;

	/// <summary>
	///    can be used to detect when items are alloc or dealloc
	/// </summary>
	public int Version { get; private set; }


#if CHECKED
		private System.Collections.Concurrent.ConcurrentDictionary<int, bool> _CHECKED_allocationTracker;
#endif

	/// <summary>
	///    available for direct access to the storage array.
	///    if you can ensure no other threads are writing, this can be resized manually.
	/// </summary>
	public TData[] _storage;

	private readonly Stack<int> _freeSlots;
	//private readonly MinHeap _freeSlots;

	private readonly object _lock = new();


	public StructSlotArray(int capacity)
	{
		_storage = new TData[capacity];
		_freeSlots = new Stack<int>(capacity);
#if CHECKED
			this._CHECKED_allocationTracker = new();
#endif
		for (var i = 0; i < capacity; i++)
		{
			//this._freeSlots.Push(capacity - i);
			_freeSlots.Push(i);
		}
	}

	public ref TData GetRef(int slot)
	{
#if CHECKED
      __.GetLogger()._EzErrorThrow(this._CHECKED_allocationTracker.ContainsKey(slot), "slot is not allocated and you are using it");
#endif

		return ref _storage[slot];
	}

	public ref TData this[int slot] => ref GetRef(slot);


	public int Alloc(ref TData data)
	{
		lock (_lock)
		{
			__.GetLogger()._EzError(FreeCount > 0, "no capacity remaining");

			Version++;
			var slot = _freeSlots.Pop();
#if CHECKED
				__.GetLogger()._EzErrorThrow(this._CHECKED_allocationTracker.TryAdd(slot, true), "slot already allocated");
#endif
			_storage[slot] = data;
			return slot;
		}
	}

	public int Alloc()
	{
		lock (_lock)
		{
			__.GetLogger()._EzError(FreeCount > 0, "no capacity remaining");

			Version++;
			var slot = _freeSlots.Pop();
#if CHECKED
				__.GetLogger()._EzErrorThrow(this._CHECKED_allocationTracker.TryAdd(slot, true), "slot already allocated");
#endif
			return slot;
		}
	}

	public void Alloc(Span<int> toFill)
	{
#if CHECKED
			toFill.Clear();
#endif
		lock (_lock)
		{
			__.GetLogger()._EzError(FreeCount >= toFill.Length, "no capacity remaining");

			Version++;

			for (var i = 0; i < toFill.Length; i++)
			{
				var slot = _freeSlots.Pop();
#if CHECKED
					__.GetLogger()._EzErrorThrow(this._CHECKED_allocationTracker.TryAdd(slot, true), "slot already allocated");
#endif
				toFill[i] = slot;
			}
		}
	}


	public void Free(int slot)
	{
		lock (_lock)
		{
			Version++;
#if CHECKED
				__.GetLogger()._EzErrorThrow(this._CHECKED_allocationTracker.TryRemove(slot, out var temp), "slot is not allocated but trying to remove");
#endif
			_freeSlots.Push(slot);
			_storage[slot] = default!;
		}
	}

	public void Free(Span<int> toFree)
	{
		lock (_lock)
		{
			Version++;
			for (var i = 0; i < toFree.Length; i++)
			{
				var slot = toFree[i];

#if CHECKED
					__.GetLogger()._EzErrorThrow(this._CHECKED_allocationTracker.TryRemove(slot, out var temp), "slot is not allocated but trying to remove");
#endif
				_freeSlots.Push(slot);
				_storage[slot] = default!;
			}
		}
	}
}
//

//If I need to write it myself, I am thinking I need to use a normal `List<int>` that's sorted, and wrap it in a stack-like class.   When a item is pulled off the top I decrement a `int topIndex`  and when items are put back on, they go above that, incrementing a `int addCount` parameter..   Next time an item is pulled off I just sort from `topIndex` to `topIndex+addedCount` before I give an item back.  I
