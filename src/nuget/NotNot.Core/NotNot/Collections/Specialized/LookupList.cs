using System.Collections;

namespace NotNot.Collections.Specialized;

/// <summary>
///    A list and dictionary combined:  list for fast iteration, dictionary for lookup.
/// </summary>
public class LookupList<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, ICollection, ICloneable
{
   public Dictionary<TKey, TValue> Dictionary = new();
   public List<KeyValuePair<TKey, TValue>> List = new();

   public TValue this[int i] => List[i].Value;

   public TValue this[TKey key] => Dictionary[key];

   object ICloneable.Clone()
   {
      return Clone();
   }

   public void CopyTo(Array array, int index)
   {
      ((ICollection)List).CopyTo(array, index);
   }

   public int Count => List.Count;

   public bool IsSynchronized => ((ICollection)List).IsSynchronized;

   public object SyncRoot => ((ICollection)List).SyncRoot;


   public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
   {
      return List.GetEnumerator();
   }

   IEnumerator IEnumerable.GetEnumerator()
   {
      return ((IEnumerable)List).GetEnumerator();
   }

   public bool TryGet(TKey key, out TValue value)
   {
      return Dictionary.TryGetValue(key, out value);
   }

   public void Add(TKey key, TValue value)
   {
      List.Add(new KeyValuePair<TKey, TValue>(key, value));
      Dictionary.Add(key, value);
   }

   public void Remove(TKey key, TValue value)
   {
      List.Remove(new KeyValuePair<TKey, TValue>(key, value));
      Dictionary.Remove(key);
   }

   public void RemoveAt(int index)
   {
      var pair = List[index];
      List.RemoveAt(index);
      Dictionary.Remove(pair.Key);
   }

   public void Clear()
   {
      List.Clear();
      Dictionary.Clear();
   }

   public LookupList<TKey, TValue> Clone()
   {
      var toReturn = new LookupList<TKey, TValue>();
      foreach (var pair in List)
      {
         if (pair.Value is ICloneable clonable)
         {
            toReturn.Add(pair.Key, (TValue)clonable.Clone());
         }
         else
         {
            toReturn.Add(pair.Key, pair.Value);
         }
      }

      return toReturn;
   }
}