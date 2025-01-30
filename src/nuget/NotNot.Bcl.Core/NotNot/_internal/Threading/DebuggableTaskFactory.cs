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

using NotNot.Concurrency.Advanced;

namespace NotNot._internal.Threading;

/// <summary>
///    allows toggling the boolean singleThreaded static variable, which will then cause calls to .Run() to run tasks
///    one-at-a-time (synchronously) allowing easier single-thread debugging
/// </summary>
public class DebuggableTaskFactory
{
   public DebuggableTaskFactory(bool singleThreaded)
   {
      SingleThreaded = singleThreaded;

      if (singleThreaded)
      {
         //From https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskscheduler?view=net-6.0
         //LimitedConcurrencyLevelTaskScheduler lcts = new(1);
         CustomTaskScheduler lcts = new("DebuggableTaskFactory", 1);
         ResetScheduler(lcts);
      }
      else
      {
         Factory = Task.Factory;
      }
   }

   /// <summary>
   ///    set to TRUE to run tasks synchronously.
   /// </summary>
   public bool SingleThreaded { get; private set; }

   /// <summary>
   ///    the underlying Task Factory that the .Run() method points to.   (Single Threaded if the singleThreaded==true, or the
   ///    default Task.Factory otherwise)
   /// </summary>
   public TaskFactory Factory { get; private set; }

   public void ResetScheduler(TaskScheduler? scheduler = null)
   {
      lock (this)
      {
         var oldFactory = Factory;

         if (scheduler is not null)
         {
            Factory = new TaskFactory(scheduler);
         }
         else
         {
            Factory = Task.Factory;
         }

         if (oldFactory is IDisposable d)
         {
            d.Dispose();
         }
         else if (oldFactory is IAsyncDisposable ad)
         {
#pragma warning disable CA2012 // Use ValueTasks correctly
            _ = ad.DisposeAsync();
#pragma warning restore CA2012 // Use ValueTasks correctly
         }
      }
   }


   ///// <remarks>From https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskscheduler?view=net-6.0</remarks>
   //LimitedConcurrencyLevelTaskScheduler lcts = new(1);
   //  static TaskFactory singleThreadedFactory = new(lcts);

   //  public static TaskFactory Factory
   //  {
   //      get
   //      {

   //          if (SingleThreaded)
   //          {
   //              return singleThreadedFactory;
   //          }
   //          else
   //          {
   //              return Task.Factory;
   //          }
   //      }
   //  }

   //public static Task Run(Action action, CancellationToken ct = default)
   //{
   //    return Factory.Run(action, ct);
   //}
   //public static Task Run<TResult>(Func<TResult> action, CancellationToken ct = default)
   //{
   //    return Factory.Run(action, ct);
   //}
   //public static Task Run(Func<Task> action, CancellationToken ct = default)
   //{
   //    return Factory.Run(action, ct);
   //}
   //public static Task Run<TResult>(Func<Task<TResult>> action, CancellationToken ct = default)
   //{
   //    return Factory.Run(action,ct);


   //}
}