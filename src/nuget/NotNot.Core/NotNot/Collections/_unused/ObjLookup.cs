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

using NotNot;

namespace NotNot.Collections._unused;

/// <summary>
///    allows finding an instance of an object by an identifier.
///    <para>Internally stores using a weak reference so it does not prevent garbage collection.</para>
/// </summary>
/// <typeparam name="T"></typeparam>
[ThreadSafety(ThreadSituation.Always)]
public class ObjLookup<T> where T : class
{
   private readonly object _lock = new();
   private int _lastId;

   public Dictionary<int, WeakReference<T>> _storage = new();

   public int Add(T item)
   {
      lock (_lock)
      {
         var toReturn = _lastId++;
         _storage.Add(toReturn, new WeakReference<T>(item));

         return toReturn;
      }
   }

   public void Remove(int id)
   {
      lock (_lock)
      {
         _storage.Remove(id);
      }
   }

   public T? Get(int id)
   {
      lock (_lock)
      {
         if (_storage.TryGetValue(id, out var weakRef))
         {
            if (weakRef.TryGetTarget(out var toReturn))
            {
               return toReturn;
            }
         }
      }

      return null;
   }
}