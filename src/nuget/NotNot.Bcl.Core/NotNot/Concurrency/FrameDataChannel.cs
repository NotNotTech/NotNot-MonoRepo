// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections.Concurrent;

namespace NotNot.Concurrency;

/// <summary>
///    an async Message Channel specialized for sending inter-system messages, aggregrated by simulation frame
///    <para>allows multiple Writers, Single Reader.  Producer/Consumer pattern.</para>
/// </summary>
/// <typeparam name="T"></typeparam>
public class FrameDataChannel<T> : DisposeGuard
{
	/// <summary>
	///    debug logic: checks to make sure a thread isn't writing while the end frame swap is occuring.
	/// </summary>
	private bool _allowWriteWhileEndingFrame;

	private ConcurrentQueue<T> _currentFramePacket = new();


	private RecycleChannel<_FramePacketWrapper<T>> _recycleChannel;
	private object _writeLock = new();

	/// <summary>
	/// </summary>
	/// <param name="maxFrames">
	///    how many simulation frames worth of data to keep, if the reader systems don't process them in a timely fashion.
	///    <para>
	///       for example, a value of 1 means that only 1 FramePacket (queue) is stored, allowing reader systems to never be
	///       more than 1 frame out of data from the writer systems.
	///       If the reader gets further out of date and 2 frames finish, the oldest frame will be discarded
	///    </para>
	/// </param>
	/// <param name="allowWriteWhileEndingFrame"></param>
	public FrameDataChannel(int maxFrames = 1,
		bool allowWriteWhileEndingFrame = false) //, int maxPacketsPerFrame = int.MaxValue)
	{
		_allowWriteWhileEndingFrame = allowWriteWhileEndingFrame;
		_recycleChannel = new RecycleChannel<_FramePacketWrapper<T>>(maxFrames,
			newFactory: () => new _FramePacketWrapper<T>(new ConcurrentQueue<T>()),
			recycleHelper: framePacket =>
			{
				//this FramePacketChannel class should already clear, so lets just check to verify it's clear
				__.GetLogger()._EzError(framePacket.getQueue().Count == 0);
				//framePacket.Clear();
				return framePacket;
			},
			disposeHelper: framePacket =>
			{
				framePacket.Clear();
			}
		);
	}

	/// <summary>
	///    For the current frame being written, how many data items are written to the FramePacket (queue)
	/// </summary>
	public int CurrentFramePacketDataCount => _currentFramePacket.Count;


	/// <summary>
	///    write data items associated with the current frame.  these will be bundled together as a FramePacket (queue) and
	///    sent to the reader system
	/// </summary>
	/// <param name="dataItem"></param>
	public void WriteFramePacketData(T dataItem)
	{
		lock (_writeLock)
		{
			_currentFramePacket.Enqueue(dataItem);
		}
	}

	/// <summary>
	///    signal that current frame is finished, moving current FramePacket into the channel for reading by consumer systems.
	/// </summary>
	public void EndFrameAndEnqueue()
	{
		if (_allowWriteWhileEndingFrame == false)
		{
			if (Monitor.TryEnter(_writeLock) == false)
			{
				__.GetLogger()._EzErrorThrow(false, "could not enter write lock, another thread is writing");
			}
		}
		else
		{
			Monitor.Enter(_writeLock);
		}

		try
		{
			_FramePacketWrapper<T> toEnqueue = new(_currentFramePacket);
			_recycleChannel.WriteAndSwap(toEnqueue, out var recycledPacket);
			_currentFramePacket = recycledPacket.getQueue();
			__.GetLogger()._EzError(_currentFramePacket.Count == 0);
		}
		finally
		{
			Monitor.Exit(_writeLock);
		}
	}

	/// <summary>
	///    Read the FramePacket (queue) for a previous frame.  The consumer system should call this
	///    <para>Async, blocks until complete</para>
	/// </summary>
	/// <param name="doneFramePacketToRecycle">recycle the queue for reuse, to avoid GC pressure</param>
	/// <returns></returns>
	public async ValueTask<ConcurrentQueue<T>> ReadFrame(ConcurrentQueue<T> doneFramePacketToRecycle)
	{
		//if (doneFramePacketToRecycle == null)
		//{
		//	doneFramePacketToRecycle = new();
		//}

		__.GetLogger()._EzErrorThrow(doneFramePacketToRecycle.Count == 0,
			"expect queue being recycled to be clear/unused");
		var recyclePacket = new _FramePacketWrapper<T>(doneFramePacketToRecycle);
		var dequeuedPacket = await _recycleChannel.ReadAndSwap(recyclePacket);
		return dequeuedPacket.getQueue();
	}

	public bool TryReadFrame(out ConcurrentQueue<T> framePacket)
	{
		if (_recycleChannel.TryRead(out var queueWrapper))
		{
			framePacket = queueWrapper.getQueue();
			return true;
		}

		framePacket = null;
		return false;
	}

	/// <summary>
	///    If you have a FramePacket(queue) that can be recycled can put it here
	/// </summary>
	/// <param name="doneFramePacketToRecycle"></param>
	public void Recycle(ConcurrentQueue<T> doneFramePacketToRecycle)
	{
		__.GetLogger()._EzErrorThrow(doneFramePacketToRecycle.Count == 0,
			"expect queue being recycled to be clear/unused");
		_recycleChannel.Recycle(new _FramePacketWrapper<T>(doneFramePacketToRecycle));
	}

	/// <summary>
	///    PRIVATE helper: wraps the queue with some simple validation logic (make sure count doesn't change when inside the
	///    channel)
	/// </summary>
	/// <typeparam name="T"></typeparam>
	private struct _FramePacketWrapper<T>
	{
		private ConcurrentQueue<T> framePacket;
		private int queueCount;


		public _FramePacketWrapper(ConcurrentQueue<T> framePacket)
		{
			this.framePacket = framePacket;
			queueCount = framePacket.Count;
		}

		public ConcurrentQueue<T> getQueue()
		{
			//__.GetLogger()._EzError(_frameVersion == currentFrameVersion,"race condition, frames do not match.  is this framePacket being used improperly?  use-after-enqueue or use-after-recycle");
			VerifyPacket();
			return framePacket;
		}

		public void VerifyPacket()
		{
			__.GetLogger()._EzError(framePacket != null, "disposed or not initalized");
			__.GetLogger()._EzError(framePacket.Count == queueCount,
				"race condition, queue count at dequeue time does not match count when created.  is this framePacket being used improperly?  use-after-enqueue or use-after-recycle");
		}

		public void Clear()
		{
			VerifyPacket();
			//_frameVersion = -1;
			queueCount = 0;
			framePacket.Clear();
		}
	}
}
