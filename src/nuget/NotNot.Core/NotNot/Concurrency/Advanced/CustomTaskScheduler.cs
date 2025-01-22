// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;

namespace NotNot.Concurrency.Advanced;

/// <summary>
///    A custom task scheduler, running tasks only on a set of fixed threads contained by this scheduler.
/// </summary>
/// <remarks>
///    from https://www.wisdomjobs.com/e-university/c-dot-net-tutorial-225/using-a-custom-task-schedular-542.html
/// </remarks>
public class CustomTaskScheduler : TaskScheduler, IDisposable
{
   private static int _counter;
   private readonly int _id = Interlocked.Increment(ref _counter);
   private BlockingCollection<Task> taskQueue;
   private Thread[] threads;

   public CustomTaskScheduler(string name, int concurrency)
   {
      _Init(name, concurrency);
   }

   private void _Init(string name, int concurrency)
   {
      if (concurrency < 1)
      {
         throw new LoLoDiagnosticsException("concurrency must be > 0");
      }

      // initialize the collection and the thread array
      taskQueue = new BlockingCollection<Task>();
      threads = new Thread[concurrency];
      Name = name;

      // create and start the threads
      for (int i = 0; i < threads.Length; i++)
      {
         threads[i] = new Thread(() =>
         {
            // loop while the blocking collection is not
            // complete and try to execute the next task
            foreach (Task t in taskQueue.GetConsumingEnumerable())
            {
               TryExecuteTask(t);
            }
         });
         threads[i].Name = $"{Name} (Cts{_id}, {i + 1}/{concurrency})";
         threads[i].Start();
      }
   }

   public string Name { get; private set; }

   public override int MaximumConcurrencyLevel => threads.Length;

   public void Dispose()
   {
      // mark the collection as complete
      taskQueue.CompleteAdding();
      // wait for each of the threads to finish
      foreach (Thread t in threads)
      {
         t.Join();
      }

      taskQueue.Dispose();
   }

   protected override void QueueTask(Task task)
   {
      if (task.CreationOptions.HasFlag(TaskCreationOptions.LongRunning))
      {
         // create a dedicated thread to execute this task
         var lrThread = new Thread(() => { TryExecuteTask(task); });

         lrThread.Name = $"{Name} (Cts{_id}, LongRunning)";

         lrThread.Start();
      }
      else
      {
         // add the task to the queue
         taskQueue.Add(task);
      }
   }

   protected override bool TryExecuteTaskInline(Task task,
      bool taskWasPreviouslyQueued)
   {
      // only allow inline execution if the executing thread is one
      // belonging to this scheduler
      if (threads.Contains(Thread.CurrentThread))
      {
         return TryExecuteTask(task);
      }

      return false;
   }

   protected override IEnumerable<Task> GetScheduledTasks()
   {
      return taskQueue.ToArray();
   }

   /// <summary>
   ///    from https://www.wisdomjobs.com/e-university/c-dot-net-tutorial-225/using-a-custom-task-schedular-542.html
   /// </summary>
   private static class Listing_22_ExampleUsage
   {
      private static void example_usage()
      {
         // get the processor count for the system
         int procCount = Environment.ProcessorCount;
         // create a custom scheduler
         CustomTaskScheduler scheduler = new("Cts Example", procCount);
         Console.WriteLine("Custom scheduler ID: {0}", scheduler.Id);
         Console.WriteLine("Default scheduler ID: {0}", Default.Id);
         // create a cancellation token source
         CancellationTokenSource tokenSource = new();
         // create a task
#pragma warning disable LL_R001 // Task is not awaited or used
         Task task1 = new(() =>
         {
            Console.WriteLine("Task {0} executed by scheduler {1}",
               Task.CurrentId, Current.Id);
            // create a child task - this will use the same
            // scheduler as its parent
            _ = Task.Factory.StartNew(() =>
            {
               Console.WriteLine("Task {0} executed by scheduler {1}",
                  Task.CurrentId, Current.Id);
            });
            // create a child and specify the default scheduler
            _ = Task.Factory.StartNew(() =>
            {
               Console.WriteLine("Task {0} executed by scheduler {1}",
                  Task.CurrentId, Current.Id);
            }, tokenSource.Token, TaskCreationOptions.None, Default);
         });
#pragma warning restore LL_R001 // Task is not awaited or used
         // start the task using the custom scheduler
         task1.Start(scheduler);
         // create a continuation - this will use the default scheduler
         _ = task1.ContinueWith(antecedent =>
         {
            Console.WriteLine("Task {0} executed by scheduler {1}",
               Task.CurrentId, Current.Id);
         });
         // create a continuation using the custom scheduler
         _ = task1.ContinueWith(antecedent =>
         {
            Console.WriteLine("Task {0} executed by scheduler {1}",
               Task.CurrentId, Current.Id);
         }, scheduler);
      }
   }
}