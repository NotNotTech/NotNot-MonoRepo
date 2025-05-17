using NotNot.Advanced;

namespace NotNot.Concurrency._dubious;

/// <summary>
///    a worker thread that consumes items from a queue
/// </summary>
/// <typeparam name="TItem"></typeparam>
[ThreadSafe]
public class ConsumerThread<TItem>
{
   private AsyncProducerConsumerQueue<TItem> _queue = new();


   public required Func<TItem, CancellationToken, Task<TItem>> dequeueAction;

   public Task? startTask;

   public CancellationToken CT { get; private set; }

   public bool IsAborted =>  CT.IsCancellationRequested;

   public bool IsStarted => startTask is not null;


   public void Enqueue(TItem item)
   {
      _queue.Enqueue(item, CT);
   }

   /// <summary>
   ///    runs forever (until canceled), dequeueing and processing items sequentially on a worker thread
   /// </summary>
   public void Start(CancellationToken ct)
   {
      __.GetLogger()._EzError(IsStarted is false);
      CT = ct;
      __.placeholder.Later("fix up task pool for long running, instead of hack below");
      startTask = Task.Factory.Run(async () =>
      {
         while (IsAborted is false)
         {
            var nextItem = await _queue.DequeueAsync(CT);
            if (IsAborted)
            {
               return;
            }

            await dequeueAction(nextItem, CT);
         }
      }, CT);
   }
}
