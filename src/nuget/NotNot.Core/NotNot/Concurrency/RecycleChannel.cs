using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NotNot.Concurrency;

/// <summary>
///    A custom System.Threading.Channel that recycles the data objects for reuse
/// </summary>
/// <typeparam name="T"></typeparam>
public class RecycleChannel<T> : DisposeGuard
{
   //public ChannelWriter<T> _writer;
   //public ChannelReader<T> _reader;
   /// <summary>
   ///    How many pending items this channel can hold.  If more than this are enqueued, the oldest is replaced.
   /// </summary>
   public int _capacity;

   public Channel<T> _channel;

   /// <summary>
   ///    helper to dispose of data objects stored internally.  called when disposed and items still exist in the channel.
   /// </summary>
   public Action<T> _disposeHelper;

   /// <summary>
   ///    a helper to create new data items.  These helpers are needed because we allow custom generic data items, so we don't
   ///    know their interface.
   /// </summary>
   public Func<T> _newFactory;


   /// <summary>
   ///    unused data items
   /// </summary>
   public ConcurrentQueue<T> _recycled = new();

   /// <summary>
   ///    a custom callback to recycle the data object.  usually just .Recycle() it
   /// </summary>
   private Func<T, T> _recycleHelper;


   private object _writeLock = new();

   public RecycleChannel(int capacity, Func<T> newFactory, Func<T, T> recycleHelper, Action<T> disposeHelper)
   {
      _newFactory = newFactory;
      _recycleHelper = recycleHelper;
      _capacity = capacity;
      _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
      {
         AllowSynchronousContinuations = true,
         FullMode = BoundedChannelFullMode.Wait,
         SingleReader = true,
         SingleWriter = true,
      });
      _disposeHelper = disposeHelper;
   }

   public void WriteAndSwap(T toEnqueue, out T recycled)
   {
      lock (_writeLock)
      {
         if (IsDisposed)
         {
            recycled = _recycleHelper(toEnqueue);
            return;
         }

         if (_channel.Writer.TryWrite(toEnqueue))
         {
            //something to return
            if (_recycled.TryDequeue(out recycled))
            {
               return;
            }

            recycled = _newFactory();
            return;
         }

         //could not write.  channel is full. need to dequeue one and try again
         {
            //get something to return
            if (_channel.Reader.TryRead(out var toReturn))
            {
               //sacrificing oldest enqueued so clean it before returning it
               _recycleHelper(toReturn);
            }
            else
            {
               //a consumer thread may have depleted our channel
               toReturn = _newFactory();
            }

            var result = _channel.Writer.TryWrite(toEnqueue);
            __.GetLogger()._EzErrorThrow(result,
               "error in this class workflow.   we should never fail writing because this is exculsive write");
            recycled = toReturn;
         }
      }
   }

   protected override void OnDispose(bool managedDisposing)
   {
      base.OnDispose(managedDisposing);
      lock (_writeLock)
      {
         _channel.Writer.Complete();
         _recycled.Clear();
         while (_channel.Reader.TryRead(out var enqueued))
         {
            _recycleHelper(enqueued);
            _disposeHelper(enqueued);
         }

         //_cleanHelper = null;
         _newFactory = null;
      }
   }

   /// <summary>
   ///    blocks until a data item is available to read
   /// </summary>
   /// <param name="toRecycle"></param>
   /// <returns></returns>
   public ValueTask<T> ReadAndSwap(T toRecycle)
   {
      Recycle(toRecycle);
      return _channel.Reader.ReadAsync();
   }


   /// <summary>
   ///    use if you want to return a value without getting a new one
   /// </summary>
   public void Recycle(T toRecycle)
   {
      //clean it first
      _recycleHelper(toRecycle);
      _recycled.Enqueue(toRecycle);
   }

   /// <summary>
   ///    get a value without returning one
   /// </summary>
   /// <param name="freshValue"></param>
   /// <returns></returns>
   public bool TryRead(out T freshValue)
   {
      return _channel.Reader.TryRead(out freshValue);
   }
}