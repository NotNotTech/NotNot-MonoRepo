using System.Collections;

namespace NotNot.Collections.Specialized;

/// <summary>
///    A list that also adds/removes items from a backing master list
///    used so that this is a partial view of the master list.
/// </summary>
/// <typeparam name="TPartialView"></typeparam>
public class PartialViewList<TPartialView, TBackingMaster> : IList<TPartialView> where TPartialView : TBackingMaster
{
   private IList<TPartialView> _PartialViewStorage;
   public IList<TBackingMaster> BackingMasterStorage;


   public PartialViewList(IList<TBackingMaster>? backingMasterStorage = null,
      IList<TPartialView>? partialViewStorage = null)
   {
      backingMasterStorage ??= new List<TBackingMaster>();
      BackingMasterStorage = backingMasterStorage;

      partialViewStorage ??= new List<TPartialView>();
      _PartialViewStorage = partialViewStorage;
   }

   public IEnumerator<TPartialView> GetEnumerator()
   {
      return _PartialViewStorage.GetEnumerator();
   }

   IEnumerator IEnumerable.GetEnumerator()
   {
      return ((IEnumerable)_PartialViewStorage).GetEnumerator();
   }

   public void Add(TPartialView item)
   {
      _PartialViewStorage.Add(item);
      BackingMasterStorage.Add(item);
   }

   public void Clear()
   {
      foreach (var item in _PartialViewStorage)
      {
         var removed = BackingMasterStorage.Remove(item);
         __.GetLogger()._EzError(removed,
            "backing storage out of sync.   items in the sub should be add/removed from this only.");
      }

      _PartialViewStorage.Clear();
   }

   public bool Contains(TPartialView item)
   {
      var contains = _PartialViewStorage.Contains(item);
      __.GetLogger()._EzError(contains == BackingMasterStorage.Contains(item),
         "backing storage out of sync.   items in the sub should be add/removed from this only.");
      return contains;
   }

   public void CopyTo(TPartialView[] array, int arrayIndex)
   {
      _PartialViewStorage.CopyTo(array, arrayIndex);
   }

   public bool Remove(TPartialView item)
   {
      var removed = _PartialViewStorage.Remove(item);

      if (removed)
      {
         var backingRemoved = BackingMasterStorage.Remove(item);
         __.GetLogger()._EzError(removed == backingRemoved,
            "backing storage out of sync.   items in the sub should be add/removed from this only.");
      }

      return removed;
   }

   public int Count => _PartialViewStorage.Count;

   public bool IsReadOnly => _PartialViewStorage.IsReadOnly;

   public int IndexOf(TPartialView item)
   {
      return _PartialViewStorage.IndexOf(item);
   }

   public void Insert(int index, TPartialView item)
   {
      _PartialViewStorage.Insert(index, item);
   }

   public void RemoveAt(int index)
   {
      _PartialViewStorage.RemoveAt(index);
   }

   public TPartialView this[int index]
   {
      get => _PartialViewStorage[index];
      set => _PartialViewStorage[index] = value;
   }
}