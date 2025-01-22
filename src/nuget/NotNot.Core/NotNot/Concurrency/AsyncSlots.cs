using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Nito.AsyncEx.Synchronous;
using Nito.Disposables;

namespace NotNot.Concurrency;

/// <summary>
/// Allows a dynamic number of slots to be in use at a time.  requests exceeding capacity are queued until a slot is available.
/// <para>Useful for when you want a SemaphoreSlim but want to adjust the maxCount dynamically</para>
/// </summary>
public class AsyncSlots
{
   private AsyncAutoResetEvent _signal = new(false);

   private volatile int _slotsAvailable;
   private volatile int _slotsUsed;
   private volatile int _maxSlots;
   /// <summary>
   /// Maximum slots allowed to execute concurrently.   additional is queued until a slot is available.
   /// <para>can be adjusted dynamically by calling ChangeMax()</para>
   /// </summary>
   public int Max => _maxSlots;
   /// <summary>
   /// number of slots currently in use
   /// </summary>
   public int Used => _slotsUsed;
   /// <summary>
   /// number of slots currently available
   /// </summary>
   public int Available => _slotsAvailable;

   private readonly object _lock = new();

   public AsyncSlots(int initialAvailable)
   {
      __.Throw(initialAvailable >= 0);
      _slotsAvailable = initialAvailable;
      _maxSlots = initialAvailable;

      if (initialAvailable > 0)
      {
         _signal.Set();
      }
   }

   private void _RebalanceSlots()
   {
      lock (_lock)
      {
         _slotsAvailable = _maxSlots - _slotsUsed;
         if (_slotsAvailable > 0)
         {
            _signal.Set();
         }

      }
   }
   private async Task _Checkout(CancellationToken ct = default)
   {

      await _signal.WaitAsync(ct);
      lock (_lock)
      {
         // __.Test.Write($"CHECKOUT {_slotsAvailable},{_slotsUsed},{_maxSlots}");
         _slotsAvailable--;
         _slotsUsed++;
         _RebalanceSlots();
      }
   }


   private void _Return()
   {
      lock (_lock)
      {
         //__.Test.Write($"RETURN {_slotsAvailable},{_slotsUsed},{_maxSlots}");
         _slotsAvailable++;
         _slotsUsed--;
         _RebalanceSlots();
      }
   }

   /// <summary>
   /// adjusts the max slots available.  does not abort currently active slots if less than the Used amount, 
   /// but will prevent new slots from being checked out.
   /// </summary>
   /// <param name="newMax"></param>
   public void ChangeMax(int newMax)
   {
      __.Throw(newMax >= 0);
      lock (_lock)
      {
         //__.Test.Write($"ChangeMaxSlots {_maxSlots},{newMax}");
         _maxSlots = newMax;
         _RebalanceSlots();
      }
   }

   /// <summary>
   /// use a slot, automatic return by disposing: `using(await _slots.Lock()){ /** your code here */ }`
   /// </summary>
   public AwaitableDisposable<IDisposable> Lock(CancellationToken ct = default)
   {
      return new AwaitableDisposable<IDisposable>(LockAsync(ct));
   }

   private async Task<IDisposable> LockAsync(CancellationToken ct)
   {
      await _Checkout(ct);
      bool isDisposed = false;
      return Disposable.Create(() =>
      {
         if (isDisposed is false)
         {
            isDisposed = true;
            _Return();
         }
      });
   }
}