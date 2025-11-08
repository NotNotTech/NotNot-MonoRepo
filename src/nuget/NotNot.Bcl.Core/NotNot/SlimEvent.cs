using System.Collections.Generic;

namespace NotNot;

/// <summary>
/// Strong-reference event handler for parameterless events.
/// Unlike Event&lt;TEventArgs&gt; which uses WeakReferences, SlimEvent uses strong references for better performance.
/// TSender must implement IDisposeGuard for disposal validation in CHECKED builds.
/// </summary>
/// <typeparam name="TSender">The sender type, must implement IDisposeGuard for disposal checking</typeparam>
/// <remarks>
/// Thread-safe for concurrent subscription, unsubscription, and invocation.
/// In CHECKED builds, detects disposed recipients still subscribed (memory leak detection).
/// Exceptions thrown by handlers bubble to caller and stop invocation.
/// Use Clear() for bulk unsubscribe.
/// </remarks>
public class SlimEvent<TSender> : DisposeGuard
{
   private List<Action<TSender>> _storage = new();

   /// <summary>
   /// Subscribe/unsubscribe to this event
   /// </summary>
   public event Action<TSender> Handler
   {
      add
      {
         __.AssertIfNot(value.Target != null && value.Target is IDisposeGuard, $"SlimEvent subscriber target ({value.Target?.GetType().Name}) must implement IDisposeGuard for disposal validation");
         lock (_storage)
         {
            if (IsDisposed)
            {
               __.Assert($"Cannot subscribe to disposed SlimEvent<{typeof(TSender).Name}>"); 
               return;
            }
            _storage.Add(value);
         }
      }
      remove
      {
         lock (_storage)
         {
            var index = _storage.FindLastIndex(x => x == value);
            if (index >= 0)
            {
               _storage.RemoveAt(index);
            }
         }
      }
   }

	protected override void OnDispose(bool managedDisposing)
	{
		base.OnDispose(managedDisposing);
		if (managedDisposing)
		{
         _storage.Clear();
		}
	}

   /// <summary>
   /// Invoke all subscribed handlers. Only the owner should call this.
   /// Exceptions thrown by handlers bubble to caller.
   /// In CHECKED builds, detects and removes disposed recipients.
   /// </summary>
   /// <param name="sender">The event sender</param>
   public void Raise(TSender sender)
   {
      __.AssertIfNot(sender != null, "Sender cannot be null");
      //__.AssertIfNot(!sender.IsDisposed, "Sender is disposed");

      if (_storage.Count == 0) return;

      using (__.pool.Rent<List<Action<TSender>>>(out var tempList))
      {
         lock (_storage)
         {
            tempList.AddRange(_storage);
         }

         foreach (var handler in tempList)
         {
            // Check if recipient is disposed
            if (handler.Target is IDisposeGuard guard && guard.IsDisposed)
            {
               _storage.Remove(handler); // Remove BEFORE asserting so cleanup happens even when assertion throws
               __.Assert($"SlimEvent auto-removing disposed recipient.  you need to detach from the handler yourself! ({handler.Target.GetType().Name})");
               continue;
            }

            // Exception bubbles - stops invocation
            handler(sender);
         }
      }
   
   }

   /// <summary>
   /// Remove all subscribed handlers
   /// </summary>
   public void Clear()
   {
      lock (_storage)
      {
         _storage.Clear();
      }
   }
}

/// <summary>
/// Strong-reference event handler with typed arguments.
/// Unlike Event&lt;TEventArgs&gt; which uses WeakReferences, SlimEvent uses strong references for better performance.
/// TSender must implement IDisposeGuard for disposal validation in CHECKED builds.
/// TArgs does NOT require EventArgs inheritance - can be any type (value types, custom DTOs, etc).
/// </summary>
/// <typeparam name="TSender">The sender type, must implement IDisposeGuard for disposal checking</typeparam>
/// <typeparam name="TArgs">The event arguments type (no constraints)</typeparam>
/// <remarks>
/// Thread-safe for concurrent subscription, unsubscription, and invocation.
/// In CHECKED builds, detects disposed recipients still subscribed (memory leak detection).
/// Exceptions thrown by handlers bubble to caller and stop invocation.
/// Use Clear() for bulk unsubscribe.
/// </remarks>
public class SlimEvent<TSender, TArgs> : DisposeGuard
{
   private List<Action<TSender, TArgs>> _storage = new();

   /// <summary>
   /// Subscribe/unsubscribe to this event
   /// </summary>
   public event Action<TSender, TArgs> Handler
   {
      add
      {
         __.AssertIfNot(value.Target != null && value.Target is IDisposeGuard, $"SlimEvent subscriber target ({value.Target?.GetType().Name}) must implement IDisposeGuard for disposal validation");
         lock (_storage)
         {
            if (IsDisposed)
            {
               __.Assert($"Cannot subscribe to disposed SlimEvent<{typeof(TSender).Name}>");
               return;
            }
            _storage.Add(value);
         }
      }
      remove
      {
         lock (_storage)
         {
            var index = _storage.FindLastIndex(x => x == value);
            if (index >= 0)
            {
               _storage.RemoveAt(index);
            }
         }
      }
   }

   protected override void OnDispose(bool managedDisposing)
   {
      base.OnDispose(managedDisposing);
      if (managedDisposing)
      {
         _storage.Clear();
      }
   }


   /// <summary>
   /// Invoke all subscribed handlers. Only the owner should call this.
   /// Exceptions thrown by handlers bubble to caller.
   /// In CHECKED builds, detects and removes disposed recipients.
   /// </summary>
   /// <param name="sender">The event sender</param>
   /// <param name="args">The event arguments</param>
   public void Raise(TSender sender, TArgs args)
   {
      __.AssertIfNot(sender != null, "Sender cannot be null");

      if (_storage.Count == 0) return;

      using (__.pool.Rent<List<Action<TSender, TArgs>>>(out var tempList))
      {
         lock (_storage)
         {
            tempList.AddRange(_storage);
         }

         foreach (var handler in tempList)
         {
            // Check if recipient is disposed
            if (handler.Target is IDisposeGuard guard && guard.IsDisposed)
            {
               _storage.Remove(handler); // Remove BEFORE asserting so cleanup happens even when assertion throws
               __.Assert($"SlimEvent auto-removing disposed recipient.  you need to detach from the handler yourself! ({handler.Target.GetType().Name})");
               continue;
            }

            // Exception bubbles - stops invocation
            handler(sender, args);
         }
      }

   }

   /// <summary>
   /// Remove all subscribed handlers
   /// </summary>
   public void Clear()
   {
      lock (_storage)
      {
         _storage.Clear();
      }
   }
}
