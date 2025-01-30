using System.Collections.Concurrent;
using NotNot.Advanced;

namespace NotNot._internal;

/// <summary>
///    simple Object Pool implementation.  each instance of this class owns it's own, separate pool of objects.
/// </summary>
[ThreadSafe]
public class ObjectPool
{
   private ConcurrentDictionary<Type, ConcurrentQueue<object>> _itemStorage = new();


   private ConcurrentQueue<object> _GetItemTypePool<T>()
   {
      var type = typeof(T);
      var queue = _itemStorage.GetOrAdd(type, _type => new ConcurrentQueue<object>())!;

      //if (!_itemStorage.TryGetValue(type, out var queue))
      //{
      //	lock (_itemStorage) //be the only one to add at the time
      //	{
      //		if (!_itemStorage.TryGetValue(type, out queue))
      //		{
      //			queue = new ConcurrentQueue<object>();
      //			var result = _itemStorage.TryAdd(type, queue);
      //			__.GetLogger()._EzError(result);
      //		}
      //	}
      //}
      return queue;
   }

   /// <summary>
   ///    stores recycled arrays of precise length
   /// </summary>
   private static ConcurrentDictionary<Type, ConcurrentDictionary<int, ConcurrentQueue<object>>> _arrayStore = new();

   /// <summary>
   ///    stores arrays of the given length
   /// </summary>
   private ConcurrentQueue<object> _GetTypeArrayPool<T>(int length)
   {
      var type = typeof(T);

      var typeArrayStore =
         _arrayStore.GetOrAdd(type, _type => new ConcurrentDictionary<int, ConcurrentQueue<object>>());

      var queue = typeArrayStore.GetOrAdd(length, _len => new ConcurrentQueue<object>());

      return queue;
   }


   public T Get<T>() where T : class, new()
   {
      var queue = _GetItemTypePool<T>();

      if (queue.TryDequeue(out var item))
      {
         return (T)item;
      }

      return new T();
   }

   public void Return<T>(T item)
   {
      if (item is null) { return; }

      var queue = _GetItemTypePool<T>();
      queue.Enqueue(item);
   }

   /// <summary>
   ///    obtain an array of T of the exact length requested
   /// </summary>
   public T[] GetArray<T>(int length)
   {
      var queue = _GetTypeArrayPool<T>(length);

      if (queue.TryDequeue(out var item))
      {
         return (T[])item;
      }

      return new T[length];
   }

   /// <summary>
   ///    recycle the array.   will automatically clear the array unless told otherwise
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="item"></param>
   public void ReturnArray<T>(T[] item, bool preserveContents = false)
   {
      if (item is null) { return; }

      var length = item.Length;
      if (!preserveContents)
      {
         item._Clear();
      }

      var queue = _GetTypeArrayPool<T>(length);
      queue.Enqueue(item);
   }
}

/// <summary>
///    a global ObjectPool.    all instances of this class share the same common object pool.
///    If you want a separate pool per-instance, use <see cref="ObjectPool" />
/// </summary>
[ThreadSafe]
public class StaticPool
{
   public struct UsingDisposable<T> : IDisposable where T : class
   {
      private readonly StaticPool _pool;
      public readonly T Item;
      private Action<T>? _clearAction;

      public UsingDisposable(StaticPool pool, T item, Action<T>? clearAction = null)
      {
         _pool = pool;
         Item = item;
         _clearAction = clearAction;
      }

      public void Dispose()
      {
         if (_clearAction != null)
         {
            _clearAction(Item);
         }

         _pool.Return(Item);
      }
   }

   /// <summary>
   ///    Get but can be wrapped in a using block.  will be returned to the pool when the using block is exited.
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="item"></param>
   /// <param name="clearAction">optional, for clearing the item before returning to the pool</param>
   /// <returns></returns>
   public UsingDisposable<T> GetUsing<T>(out T item, Action<T>? clearAction = null) where T : class, new()
   {
      item = Get<T>();
      return new UsingDisposable<T>(this, item, clearAction);
   }

   public T Get<T>() where T : class, new()
   {
      if (Storage<T>._itemQueue.TryDequeue(out var item))
      {
         return item;
      }

      return new T();
   }


   public void Return<T>(T item)
   {
      if (item is null) { return; }

      Storage<T>._itemQueue.Enqueue(item);
   }

   /// <summary>
   ///    obtain an array of T of the exact length requested
   /// </summary>
   public T[] GetArray<T>(int length)
   {
      var queue = Storage<T>._arrayStore.GetOrAdd(length, _len => new ConcurrentQueue<T[]>());

      if (queue.TryDequeue(out var item))
      {
         return item;
      }

      return new T[length];
   }

   /// <summary>
   ///    recycle the array.   will automatically clear the array unless told otherwise
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="item"></param>
   public void ReturnArray<T>(T[] item, bool preserveContents = false)
   {
      if (item is null) { return; }

      var length = item.Length;
      if (!preserveContents)
      {
         item._Clear();
      }

      var queue = Storage<T>._arrayStore.GetOrAdd(length, _len => new ConcurrentQueue<T[]>());
      queue.Enqueue(item);
   }

   /// <summary>
   ///    fast type lookup/queue storage
   /// </summary>
   private static class Storage<T>
   {
      /// <summary>
      ///    stores recycled items
      /// </summary>
      public static ConcurrentQueue<T> _itemQueue = new();

      /// <summary>
      ///    stores recycled arrays of precise length
      /// </summary>
      public static ConcurrentDictionary<int, ConcurrentQueue<T[]>> _arrayStore = new();
   }
}