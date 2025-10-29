// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
#define CHECKED

using CommunityToolkit.HighPerformance.Buffers;

namespace NotNot;

public ref struct SpanGuard<T> : IDisposable
{
	public SpanOwner<T> _poolOwner;
#if CHECKED
	private DisposeGuard _disposeGuard;
#endif

	public static SpanGuard<T> Allocate(int size, AllocationMode allocationMode = AllocationMode.Default)
	{
		return new SpanGuard<T>(SpanOwner<T>.Allocate(size, allocationMode));
	}

	public static SpanGuard<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	public static SpanGuard<T> AllocateAndAssign(T singleItem)
	{
		var guard = Allocate(1);
		guard[0] = singleItem;
		return guard;
	}

	public SpanGuard(SpanOwner<T> owner)
	{
		_poolOwner = owner;
#if CHECKED
		_disposeGuard = new();
#endif
	}

	public Span<T> Span => _poolOwner.Span;
	public int Count => _poolOwner.Length;
	[Obsolete("use .Count")]
	public int Length => _poolOwner.Length;

	public ref T this[int index]
	{
		get => ref _poolOwner.Span[index];
	}

	/// <summary>
	/// Creates a RefMem view over a slice of this memory.
	/// Returns a zero-allocation view (Span slice) referencing the original pooled data.
	/// </summary>
	/// <param name="offset">Starting offset within this memory</param>
	/// <param name="count">Number of elements in the slice</param>
	/// <returns>RefMem wrapping a Span slice over the original data</returns>
	public RefMem<T> Slice(int offset, int count)
	{
		var slicedSpan = _poolOwner.Span.Slice(offset, count);
		return new RefMem<T>(slicedSpan);
	}

	public SpanGuard<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = SpanGuard<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	public SpanGuard<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
	{
		var thisSpan = this.Span;
		var toReturn = SpanGuard<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;
		for (var i = 0; i < Count; i++)
		{
			var mappedResult = mapFunc(ref thisSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	public SpanGuard<TResult> MapWith<TOther, TResult>(SpanGuard<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this SpanGuard");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;
		var toReturn = SpanGuard<TResult>.Allocate(Count);
		var toReturnSpan = toReturn.Span;

		for (var i = 0; i < Count; i++)
		{
			ref var mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
			toReturnSpan[i] = mappedResult;
		}
		return toReturn;
	}

	public void MapWith<TOther>(SpanGuard<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
	{
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this SpanGuard");
		var thisSpan = this.Span;
		var otherSpan = otherToMapWith.Span;

		for (var i = 0; i < Count; i++)
		{
			mapFunc(ref thisSpan[i], ref otherSpan[i]);
		}
	}

	/// <summary>
	/// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per range.
	/// Use this to process subgroups without extra allocations.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
	/// <param name="worker">Action executed for each contiguous batch, receiving a SpanGuard slice.</param>
	public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<RefMem<T>> worker)
	{
		if (this.Count == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Count)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(this.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Walks contiguous batches with parallel memory and calls worker once per range.
	/// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
	/// </summary>
	/// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
	/// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
	/// <param name="worker">Action executed for each contiguous batch</param>
	public void BatchMapWith<TOther>(SpanGuard<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<RefMem<T>, RefMem<TOther>> worker)
	{
		__.ThrowIfNot(otherToMapWith.Count == this.Count, "otherToMapWith must be the same length as this SpanGuard");

		if (this.Count == 0)
		{
			return;
		}
		var batchStart = 0;
		while (batchStart < this.Count)
		{
			var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
			worker(this.Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
			batchStart = batchEnd;
		}
	}

	/// <summary>
	/// Local synchronous scanner that finds the end of a contiguous batch
	/// </summary>
	/// <param name="start">Starting index for batch scan</param>
	/// <param name="thisGuard">Memory to scan</param>
	/// <param name="isSameBatch">Function determining if adjacent elements belong to same batch</param>
	/// <returns>Exclusive end index of the batch</returns>
	private static int _GetBatchEndExclusive(int start, SpanGuard<T> thisGuard, Func_RefArg<T, T, bool> isSameBatch)
	{
		var span = thisGuard.Span;
		var length = thisGuard.Count;

		var end = start + 1;
		while (end < length)
		{
			ref var previous = ref span[end - 1];
			ref var current = ref span[end];
			if (!isSameBatch(ref previous, ref current))
			{
				break;
			}
			end++;
		}
		return end;
	}

	public SpanGuard<T> Clone()
	{
		var copy = SpanGuard<T>.Allocate(Count);
		this.Span.CopyTo(copy.Span);
		return copy;
	}

	public ArraySegment<T> DangerousGetArray()
	{
		return _poolOwner.DangerousGetArray();
	}

	public Span<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	public void Dispose()
	{
		_poolOwner.Span.Clear();
		_poolOwner.Dispose();

#if CHECKED
		_disposeGuard.Dispose();
#endif
	}

	public override string ToString()
	{
		return $"SpanGuard<{typeof(T).Name}>[{Count}]";
	}
}
