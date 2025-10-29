using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NotNot;

public static class ParallelFor
{
	private static ZeroAllocMem<(int startInclusive, int endExclusive)> _Range_ComputeBatches(int start, int length,
		float batchSizeMultipler)
	{
		__.GetLogger()._EzErrorThrow(batchSizeMultipler >= 0,
			$"{nameof(batchSizeMultipler)} should be greater or equal to than zero");
		var endExclusive = start + length;

		var didCount = 0;
		//number of batches we want
		var batchCount = Math.Min(length, Environment.ProcessorCount);

		//figure out batch size
		var batchSize = length / batchCount;

		batchSize = (int)(batchSize * batchSizeMultipler);
		batchSize = Math.Min(batchSize, length);
		batchSize = Math.Max(1, batchSize);

		//update batchCount bsed on actual batchSize
		if (length % batchSize == 0)
		{
			batchCount = length / batchSize;
		}
		else
		{
			batchCount = length / batchSize + 1;
		}


		var owner = ZeroAllocMem<(int startInclusive, int endExclusive)>.Allocate(batchCount);
		var span = owner.Span;

		//calculate batches and put into span
		{
			var batchStartInclusive = start;
			var batchEndExclusive = batchStartInclusive + batchSize;
			var loopIndex = 0;
			while (batchEndExclusive <= endExclusive)
			{
				var thisBatchLength = batchEndExclusive - batchStartInclusive;
				__.GetLogger()._EzErrorThrow(thisBatchLength == batchSize);
				//do work:  batchStartInclusive, batchSize
				didCount += batchSize;
				span[loopIndex] = (batchStartInclusive, batchEndExclusive);

				//increment
				batchStartInclusive += batchSize;
				batchEndExclusive += batchSize;
				loopIndex++;
			}

			var remainder = endExclusive - batchStartInclusive;
			batchEndExclusive = batchStartInclusive + remainder;
			__.GetLogger()._EzErrorThrow(remainder < batchSize);
			if (remainder > 0)
			{
				//do last part:   batchStartInclusive, remainder
				didCount += remainder;
				span[loopIndex] = (batchStartInclusive, batchEndExclusive);
			}

			__.GetLogger()._EzErrorThrow(didCount == length);
		}

		return owner;
	}

	/// <summary>
	///    Range is ideal for cache coherency and lowest overhead.  If you require an action per element, it can behave like
	///    `Parallel.For` (set batchSizeMultipler=0).
	/// </summary>
	/// <param name="action">(start,endExclusive)=>ValueTask</param>
	public static ValueTask RangeAsync(int start, int length, Func<int, int, ValueTask> action)
	{
		return RangeAsync(start, length, 1f, action);
	}

	/// <summary>
	///    Range is ideal for cache coherency and lowest overhead.  If you require an action per element, it can behave like
	///    `Parallel.For` (set batchSizeMultipler=0).
	/// </summary>
	/// <param name="start"></param>
	/// <param name="length"></param>
	/// <param name="batchSizeMultipler">
	///    The range is split into batches, with each batch being the total/cpu count.  The number of (and size of) batches can
	///    be modified by this parameter.
	///    <para>
	///       1 = The default. 1 batch per cpu.  Generally a good balance as it will utilize all cores if available, while
	///       not overwhelming the thread pool (allowing other work a fair chance in backlog situations)
	///    </para>
	///    <para>0.5 = 2 batches per cpu, with each batch half sized.   useful if the work required for each element is varied. </para>
	///    <para>
	///       0 = each batch is 1 element.  useful if parallelizing independent systems that are long running and use
	///       dissimilar regions of memory.
	///    </para>
	///    <para>
	///       2 = double sized batches, utilizing a maximum of half cpu cores.  Useful for offering parallel work while
	///       reducing multicore overhead.
	///    </para>
	///    <para>4 = quad size batches, use 1/4 cpu cores at max.</para>
	/// </param>
	/// <param name="action">(start,endExclusive)=>ValueTask</param>
	/// <returns></returns>
	public static ValueTask RangeAsync(int start, int length, float batchSizeMultipler, Func<int, int, ValueTask> action)
	{
		if (length == 0)
		{
			return ValueTask.CompletedTask;
		}

		using var owner = _Range_ComputeBatches(start, length, batchSizeMultipler);

		return _Range_ExecuteActionAsync(owner.DangerousGetArray(), action);
	}

	private static async ValueTask _Range_ExecuteActionAsync(
		ArraySegment<(int start, int endExclusive)> spanOwnerDangerousArray, Func<int, int, ValueTask> action)
	{
		await Parallel.ForEachAsync(spanOwnerDangerousArray,
			(batch, cancelToken) => Unsafe.AsRef(ref action).Invoke(batch.start, batch.endExclusive));
	}

	/// <summary>
	///    Range is ideal for cache coherency and lowest overhead.  If you require an action per element, it can behave like
	///    `Parallel.For` (set batchSizeMultipler=0).
	/// </summary>
	/// <param name="action">(start,endExclusive)=>ValueTask</param>
	public static void Range(int start, int length, Action<int, int> action)
	{
		Range(start, length, 1f, action);
	}

	/// <summary>
	///    Range is ideal for cache coherency and lowest overhead.  If you require an action per element, it can behave like
	///    `Parallel.For` (set batchSizeMultipler=0).
	/// </summary>
	/// <param name="start"></param>
	/// <param name="length"></param>
	/// <param name="batchSizeMultipler">
	///    The range is split into batches, with each batch being the total/cpu count.  The number of (and size of) batches can
	///    be modified by this parameter.
	///    <para>
	///       1 = The default. 1 batch per cpu.  Generally a good balance as it will utilize all cores if available, while
	///       not overwhelming the thread pool (allowing other work a fair chance in backlog situations)
	///    </para>
	///    <para>0.5 = 2 batches per cpu, with each batch half sized.   useful if the work required for each element is varied. </para>
	///    <para>
	///       0 = each batch is 1 element.  useful if parallelizing independent systems that are long running and use
	///       dissimilar regions of memory.
	///    </para>
	///    <para>
	///       2 = double sized batches, utilizing a maximum of half cpu cores.  Useful for offering parallel work while
	///       reducing multicore overhead.
	///    </para>
	///    <para>4 = quad size batches, use 1/4 cpu cores at max.</para>
	/// </param>
	/// <param name="action">(start,endExclusive)=>ValueTask</param>
	/// <returns></returns>
	public static void Range(int start, int length, float batchSizeMultipler, Action<int, int> action)
	{
		if (length == 0)
		{
			return;
		}

		using var owner = _Range_ComputeBatches(start, length, batchSizeMultipler);
		var span = owner.Span;
		var array = owner.DangerousGetArray().Array!;

		Parallel.For(0, span.Length,
			index => Unsafe.AsRef(ref action).Invoke(array[index].startInclusive, array[index].endExclusive));
	}


	public static Task EachAsync<T>(IEnumerable<T> source, Func<T, CancellationToken, ValueTask> action)
	{
		return Parallel.ForEachAsync(source, (item, cancelToken) => Unsafe.AsRef(ref action).Invoke(item, cancelToken));
	}

	public static Task EachAsync<T>(IEnumerable<T> source, ParallelOptions options, Func<T, CancellationToken, ValueTask> action)
	{
		return Parallel.ForEachAsync(source, options, (item, cancelToken) => Unsafe.AsRef(ref action).Invoke(item, cancelToken));
	}

	public static Task EachAsync<T>(IEnumerable<T> source, Func<T, ValueTask> action)
	{
		return Parallel.ForEachAsync(source, (item, cancelToken) => Unsafe.AsRef(ref action).Invoke(item));
	}


	/// <summary>
	///    allows parallel operation, pausing periotically to allow a synchronized accumulation helper process incremental
	///    results
	/// </summary>
	/// <param name="source">
	///    collection to process items of.  internally this method coppies these items to a list for
	///    processing
	/// </param>
	/// <param name="accumulateEvery">once this many items have been processed, pauses and runs the accumulator</param>
	/// <param name="options">allow tweaking parallel execution</param>
	/// <param name="parallelAction">
	///    async method used to process each item.  be sure to save results into the concurrentQueue
	///    for the accumulator to process.
	/// </param>
	/// <param name="periodicAccumulator">
	///    process results incrementally.   if you leave items in the queue, they will be returned as
	///    "final results"
	/// </param>
	/// <returns>queue used for accumulation.  this will be empty if your accumulator function proceesses all items</returns>
	public static async Task<ConcurrentQueue<TResult>> EachAccumulateIncremental<TItem, TResult>(IEnumerable<TItem> source, int accumulateEvery
		, ParallelOptions options
		, Func<TItem, ConcurrentQueue<TResult>, CancellationToken, ValueTask> parallelAction
		, Func<ConcurrentQueue<TResult>, CancellationToken, ValueTask> periodicAccumulator)
	{
		var remaining = source.ToList();

		var queue = new ConcurrentQueue<TResult>();
		var batch = __.pool.Get<List<TItem>>();

		while (remaining.Count > 0)
		{
			batch.Clear();
			batch._TakeFrom(remaining, accumulateEvery);

			await EachAsync(batch, options, async (item, ct) => { await parallelAction(item, queue, ct); });
			await periodicAccumulator(queue, options.CancellationToken);
		}

		batch.Clear();
		__.pool.Return(batch);

		return queue;
	}
}
