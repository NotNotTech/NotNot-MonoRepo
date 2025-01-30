using System.Threading.Channels;

namespace NotNot.Concurrency;

/// <summary>
///    implement the Channel, using a "run once" workflow, with a single producer and consumer method.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class SimpleChannel<T>
{
   public SimpleChannel(int channelStorageCapacity = 1)
   {
      _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(channelStorageCapacity)
      {
         AllowSynchronousContinuations = false,
         SingleReader = true,
         SingleWriter = false,
         FullMode = BoundedChannelFullMode.Wait
      });
   }

   private Channel<T> _channel;

   private TaskCompletionSource? _tcs;

   private AsyncLock _lock = new();

   public bool IsStarted => _tcs != null;
   public bool IsFinished => _tcs?.Task.IsCompleted ?? false;

   public async Task Run(CancellationToken ct)
   {
      try
      {
         using (await _lock.LockAsync(ct))
         {
            if (_tcs != null)
            {
               return;
            }
            else
            {
               _tcs = new TaskCompletionSource();
            }
         }


         //var producer = Produce(ct, _channel);
#pragma warning disable PH_S014
         //var producer = Task.Factory.StartNew(() => Produce(ct, _channel), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

         var producer = __.Async.LongRun(() => Produce(ct, _channel), ct);

         //var consumer = Consume(ct, _channel);
         //var consumer = Task.Factory.StartNew(() => Consume(ct, _channel), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
         var consumer = __.Async.LongRun(() => Consume(ct, _channel), ct);
#pragma warning restore PH_S014
         await Task.WhenAll(producer, consumer);
         _tcs.SetResult();
      }
      finally
      {
         await _tcs.Task;
      }
   }

   /// <summary>
   ///    produce items to the channel.  call channel.Writer.Complete(); when done.
   /// </summary>
   protected abstract Task Produce(CancellationToken ct, Channel<T> channel);

   /// <summary>
   ///    consume items from the channel
   /// </summary>
   /// <param name="ct"></param>
   /// <param name="channel"></param>
   /// <returns></returns>
   protected abstract Task Consume(CancellationToken ct, Channel<T> channel);
}
