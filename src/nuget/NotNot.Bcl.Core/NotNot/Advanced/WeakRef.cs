// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime.InteropServices;

namespace NotNot.Advanced;

/// <summary>
///    struct version of WeakReference.
///    <para>
///       This is useful in internal tracking collections where you don't want to impact an objects lifetime, and where
///       you can be sure to call the WeakRef.Dispose() method when done.
///    </para>
/// </summary>
/// <typeparam name="T"></typeparam>
public struct WeakRef<T> where T : class
{
   /// <summary>
   /// </summary>
   /// <remarks>
   ///    how weak ref works: http://reedcopsey.com/2009/07/08/systemweakreference-internals-and-side-effects/
   ///    <para>
   ///       when you create a short weak reference, a few things happen.  First, a new object (the WeakReference) class is
   ///       constructed with your object.  The WeakReference instance internally stores an IntPtr to a GCHandle which is
   ///       allocated with GCHandleType.Weak (or WeakTrackRessurection for long weak references).  The WeakReference then
   ///       drops the strong handle to your object.
   ///       This is where the magic happens…
   ///       The CLR takes this GCHandle, and maintains an internal table of weak references.  This is a separately maintained
   ///       list of handles in the runtime.  When a garbage collection happens, the GC builds a full graph of the objects
   ///       rooted within your application.  Prior to doing any cleanup, the weak reference table is scanned, and any
   ///       references found which point to an object outside of the GC graph are marked as null.  However, your
   ///       WeakReference instance still points to this same location in the WeakReference table.
   ///    </para>
   ///    <para></para>
   /// </remarks>
   private GCHandle _handle;

   public WeakRef(T obj)
   {
      _handle = GCHandle.Alloc(obj, GCHandleType.Weak);
   }

   public bool IsAlive => _handle.IsAllocated;

   public bool TryGetTarget(out T obj)
   {
      obj = _handle.Target as T;
      return obj is not null;
   }

   public void SetTarget(T obj)
   {
      _handle.Target = obj;
   }

   /// <summary>
   ///    <para>For optimal performance, dispose when done.  There is overhead inside the GC to keep these tracked</para>
   /// </summary>
   public void Dispose()
   {
      _handle.Free();
   }
}