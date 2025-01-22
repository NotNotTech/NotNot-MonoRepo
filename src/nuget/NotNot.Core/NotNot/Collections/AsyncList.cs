using NotNot.Advanced;
using Nito.AsyncEx;
using System.Collections;

namespace NotNot.Collections;

/// <summary>
///    thread safe list
/// </summary>
/// <typeparam name="T"></typeparam>
[ThreadSafe]
public class AsyncList<T> : IList<T>
{
   private AsyncReaderWriterLock _Lock = new();
   private IList<T> Storage;

   public AsyncList(IList<T>? backingStorage = null)
   {
      backingStorage ??= new List<T>();
      Storage = backingStorage;
   }

   public IEnumerator<T> GetEnumerator()
   {
      using (_Lock.ReaderLock())
      {
         return Storage.ToList().GetEnumerator();
      }
   }

   IEnumerator IEnumerable.GetEnumerator()
   {
      return GetEnumerator();
   }

   public void Add(T item)
   {
      using (_Lock.WriterLock())
      {
         Storage.Add(item);
      }
   }

   public void Clear()
   {
      using (_Lock.WriterLock())
      {
         Storage.Clear();
      }
   }

   public bool Contains(T item)
   {
      using (_Lock.ReaderLock())
      {
         return Storage.Contains(item);
      }
   }

   public void CopyTo(T[] array, int arrayIndex)
   {
      using (_Lock.ReaderLock())
      {
         Storage.CopyTo(array, arrayIndex);
      }
   }

   public bool Remove(T item)
   {
      using (_Lock.WriterLock())
      {
         return Storage.Remove(item);
      }
   }

   public int Count
   {
      get
      {
         using (_Lock.ReaderLock())
         {
            return Storage.Count;
         }
      }
   }

   bool ICollection<T>.IsReadOnly
   {
      get
      {
         using (_Lock.ReaderLock())
         {
            return Storage.IsReadOnly;
         }
      }
   }

   public int IndexOf(T item)
   {
      using (_Lock.ReaderLock())
      {
         return Storage.IndexOf(item);
      }
   }

   public void Insert(int index, T item)
   {
      using (_Lock.WriterLock())
      {
         Storage.Insert(index, item);
      }
   }

   public void RemoveAt(int index)
   {
      using (_Lock.WriterLock())
      {
         Storage.RemoveAt(index);
      }
   }

   public T this[int index]
   {
      get
      {
         using (_Lock.ReaderLock())
         {
            return Storage[index];
         }
      }
      set
      {
         if (typeof(T).IsClass)
         {
            //T is a class.   replacing ref is atomic
            using (_Lock.ReaderLock())
            {
               Storage[index] = value;
            }
         }
         else
         {
            //T is a struct
            using (_Lock.WriterLock())
            {
               Storage[index] = value;
            }
         }
      }
   }
}