// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;

namespace NotNot.Collections._unused;

/// <summary>
///    An array backed storage where you can free up individual slots for reuse.    When it runs out of capacity, the
///    backing array will be resized.
///    <para>thread safe writes and non-blocking reads if not using `ref return` accessors</para>
/// </summary>
/// <typeparam name="T"></typeparam>
public class SlotStore<T>
{
   private ResizableArray<T> _storage;

   public int Count => _storage.Length - _freeSlots.Count;

   //public int CurrentCapacity => this._storage.Length;
   public int FreeCount => _freeSlots.Count;


   /// <summary>
   ///    can be used to detect when items are alloc or dealloc
   /// </summary>
   public int Version { get; private set; }

#if CHECKED
		private ConcurrentDictionary<int, bool> _CHECKED_allocationTracker;
#endif

   private readonly Stack<int> _freeSlots;

   private readonly object _lock = new();


   public SlotStore(int initialCapacity = 10)
   {
      _storage = new ResizableArray<T>(initialCapacity);
      _storage.Clear();
      _freeSlots = new Stack<int>(initialCapacity);
#if CHECKED
			this._CHECKED_allocationTracker = new();
#endif
      for (var i = 0; i < initialCapacity; i++)
      {
         _freeSlots.Push(initialCapacity - i);
      }
   }

   //public T this[int slot]
   //{
   //	get
   //	{
   //		__.CHECKED.Throw(this._CHECKED_allocationTracker.ContainsKey(slot), "slot is not allocated and you are using it");
   //		return _storage[slot];
   //	}
   //	set
   //	{
   //		lock (this._lock)
   //		{
   //			__.CHECKED.Throw(this._CHECKED_allocationTracker.ContainsKey(slot), "slot is not allocated and you are using it");
   //			_storage[slot] = value;
   //		}
   //	}
   //}
   public T this[int slot]
   {
      get
      {
#if CHECKED
        __.GetLogger()._EzError(_CHECKED_allocationTracker.ContainsKey(slot),
            "slot is not allocated and you are using it");
#endif
         return _storage[slot];
      }
      set
      {
         lock (_lock)
         {
            _storage.Set(slot, value);
         }
      }
   }


   public int Alloc(T data)
   {
      var slot = Alloc();
      _storage.Set(slot, data);
      return slot;
   }

   public int Alloc(ref T data)
   {
      var slot = Alloc();
      _storage.Set(slot, data);
      return slot;
   }

   public int Alloc()
   {
      lock (_lock)
      {
         Version++;

         int slot;
         if (_freeSlots.Count > 0)
         {
            slot = _freeSlots.Pop();
#if CHECKED
           __.GetLogger()._EzError(_CHECKED_allocationTracker.TryAdd(slot, true), "slot already allocated");
#endif
         }
         else
         {
            //need to allocate a new slot
            slot = _storage.Grow(1);
         }

         return slot;
      }
   }

   public void Free(int slot)
   {
      lock (_lock)
      {
         Version++;
#if CHECKED
        __.GetLogger()._EzError(_CHECKED_allocationTracker.TryRemove(slot, out var temp),
            "slot is not allocated but trying to remove");
#endif
         _freeSlots.Push(slot);
         _storage.Set(slot, default);
      }
   }
}