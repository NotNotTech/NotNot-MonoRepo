using NotNot.SimStorm._scratch.Ecs.Allocation;
using System.Diagnostics;

namespace NotNot.SimStorm;

/// <summary>
///    internal helper used to ensure components are not read+write at the same time.   for better performance, only checks
///    during DEBUG builds
/// </summary>
public class AccessGuard
{
   internal bool _enabled = true;

   //TODO: Read/Write sentinels should just track when reads/writes are permitted.
   //if they occur outside of those times, assert.   This way we don't need to track who does all writes.
   private SimManager _simManager;

   public AccessGuard(SimManager simManager)
   {
      _simManager = simManager;
   }

   /// <summary>
   ///    for internal use only.  informs that a read is about to occur
   /// </summary>
   /// <typeparam name="TComponent"></typeparam>
   [Conditional("DEBUG")]
   public void ReadNotify<TComponent>()
   {
      if (_enabled == false)
      {
         return;
      }

      var type = typeof(TComponent);
      if (type == typeof(EntityMetadata))
      {
         //ignore entityMetadata special field
         return;
      }

      var errorMessage =
         $"Unregistered Component Access.  You are reading a '{type.Name}' component but have not registered your System for shared-read access. Add the Following to your System.OnInitialize():  RegisterReadLock<{type.Name}>();";

      __.GetLogger()._EzErrorThrow<SimStormException>(_simManager._resourceLocks.ContainsKey(type), errorMessage);
      var rwLock = _simManager._resourceLocks[type];
      __.GetLogger()._EzErrorThrow<SimStormException>(rwLock.IsReadHeld, errorMessage);
      __.GetLogger()._EzErrorThrow<SimStormException>(rwLock.IsWriteHeld == false, errorMessage);
   }

   /// <summary>
   ///    for internal use only.  informs that a write is about to occur
   /// </summary>
   /// <typeparam name="TComponent"></typeparam>
   [Conditional("DEBUG")]
   public void WriteNotify<TComponent>()
   {
      if (_enabled == false)
      {
         return;
      }

      var type = typeof(TComponent);
      if (type == typeof(EntityMetadata))
      {
         //ignore entityMetadata special field
         return;
      }

      var errorMessage =
         $"Unregistered Component Access.  You are writing to a '{type.Name}' component but have not registered your System for exclusive-write access. Add the Following to your System.OnInitialize():  RegisterWriteLock<{type.Name}>();";


      __.GetLogger()._EzErrorThrow<SimStormException>(_simManager._resourceLocks.ContainsKey(type), errorMessage);
      var rwLock = _simManager._resourceLocks[type];
      __.GetLogger()._EzErrorThrow<SimStormException>(rwLock.IsReadHeld == false, errorMessage);
      __.GetLogger()._EzErrorThrow<SimStormException>(rwLock.IsWriteHeld, errorMessage);
   }
}