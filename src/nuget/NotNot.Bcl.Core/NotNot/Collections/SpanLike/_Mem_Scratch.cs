// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]
#define CHECKED

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;

namespace NotNot.Collections.SpanLike;

/// <summary>
/// a `Mem` that you should dispose (use the `using` pattern) to return the memory to the pool.
/// doing this will create zero allocations for temporary memory usage.
/// </summary>
/// <typeparam name="T"></typeparam>
public ref struct ZeroAllocMem<T> : IDisposable
{
   public static implicit operator Span<T>(ZeroAllocMem<T> refMem) => refMem.Span;
   public static implicit operator ReadOnlySpan<T>(ZeroAllocMem<T> refMem) => refMem.Span;


   public SpanOwner<T> _poolOwner;
#if CHECKED
   private DisposeGuard _disposeGuard;
#endif


   public static ZeroAllocMem<T> Clone(UnifiedMem<T> toClone)
   {
      var copy = ZeroAllocMem<T>.Allocate(toClone.Length);
      toClone.Span.CopyTo(copy.Span);
      return copy;
   }

   public static ZeroAllocMem<T> Allocate(int size, AllocationMode allocationMode = AllocationMode.Default)
   {
      return new ZeroAllocMem<T>(SpanOwner<T>.Allocate(size, allocationMode));
   }

   public static ZeroAllocMem<T> Allocate(ReadOnlySpan<T> copyFrom)
   {
      var toReturn = Allocate(copyFrom.Length);
      copyFrom.CopyTo(toReturn.Span);
      return toReturn;
   }

   public static ZeroAllocMem<T> AllocateAndAssign(T singleItem)
   {
      var guard = Allocate(1);
      guard[0] = singleItem;
      return guard;
   }

   public ZeroAllocMem(SpanOwner<T> owner)
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
   public UnifiedMem<T> Slice(int offset, int count)
   {
      var slicedSpan = _poolOwner.Span.Slice(offset, count);
      return new UnifiedMem<T>(slicedSpan);
   }

   /// <summary>
   /// Applies the specified mapping function to each element of this ZeroAllocMem, writing results to the provided output buffer (zero-allocation version)
   /// </summary>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this ZeroAllocMem.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
   public void Map<TResult>(UnifiedMem<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
   {
      __.ThrowIfNot(toReturn.Length == Count, "toReturn must be the same length as this ZeroAllocMem");
      var thisSpan = Span;
      var toReturnSpan = toReturn.Span;
      for (var i = 0; i < Count; i++)
      {
         ref var r_mappedResult = ref mapFunc(ref thisSpan[i]);
         toReturnSpan[i] = r_mappedResult;
      }
   }

   /// <summary>
   /// Allocates a new pooled ZeroAllocMem by applying the specified mapping function to each element of this ZeroAllocMem
   /// </summary>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
   /// <returns>New pooled memory containing mapped results</returns>
   public ZeroAllocMem<TResult> Map<TResult>(Func_Ref<T, TResult> mapFunc)
   {
      var toReturn = ZeroAllocMem<TResult>.Allocate(Count);
      Map(toReturn, mapFunc);
      return toReturn;
   }

   /// <summary>
   /// Applies the specified mapping function to each element of this ZeroAllocMem, writing results to the provided output buffer (zero-allocation version)
   /// </summary>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this ZeroAllocMem.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
   public void Map<TResult>(UnifiedMem<TResult> toReturn, Func_RefArg<T, TResult> mapFunc)
   {
      __.ThrowIfNot(toReturn.Length == Count, "toReturn must be the same length as this ZeroAllocMem");
      var thisSpan = Span;
      var toReturnSpan = toReturn.Span;
      for (var i = 0; i < Count; i++)
      {
         var mappedResult = mapFunc(ref thisSpan[i]);
         toReturnSpan[i] = mappedResult;
      }
   }

   /// <summary>
   /// Allocates a new pooled ZeroAllocMem by applying the specified mapping function to each element of this ZeroAllocMem
   /// </summary>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
   /// <returns>New pooled memory containing mapped results</returns>
   public ZeroAllocMem<TResult> Map<TResult>(Func_RefArg<T, TResult> mapFunc)
   {
      var toReturn = ZeroAllocMem<TResult>.Allocate(Count);
      Map(toReturn, mapFunc);
      return toReturn;
   }

   /// <summary>
   /// Maps two memory instances in parallel using the specified function, writing results to the provided output buffer (zero-allocation version). All must have the same length.
   /// </summary>
   /// <typeparam name="TOther">Element type of the other memory</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this ZeroAllocMem.</param>
   /// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
   /// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
   public void MapWith<TOther, TResult>(UnifiedMem<TResult> toReturn, UnifiedMem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
   {
      __.ThrowIfNot(toReturn.Length == Count, "toReturn must be the same length as this ZeroAllocMem");
      __.ThrowIfNot(otherToMapWith.Length == Count, "otherToMapWith must be the same length as this ZeroAllocMem");
      var thisSpan = Span;
      var otherSpan = otherToMapWith.Span;
      var toReturnSpan = toReturn.Span;

      for (var i = 0; i < Count; i++)
      {
         ref var r_mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
         toReturnSpan[i] = r_mappedResult;
      }
   }

   /// <summary>
   /// Allocates a new pooled ZeroAllocMem by mapping two memory instances in parallel using the specified function. Both must have the same length.
   /// </summary>
   /// <typeparam name="TOther">Element type of the other memory</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
   /// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
   /// <returns>New pooled memory containing mapped results</returns>
   public ZeroAllocMem<TResult> MapWith<TOther, TResult>(UnifiedMem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
   {
      var toReturn = ZeroAllocMem<TResult>.Allocate(Count);
      MapWith(toReturn, otherToMapWith, mapFunc);
      return toReturn;
   }

   public void MapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
   {
      __.ThrowIfNot(otherToMapWith.Length == Count, "otherToMapWith must be the same length as this ZeroAllocMem");
      var thisSpan = Span;
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
   /// <param name="worker">Action executed for each contiguous batch, receiving a ZeroAllocMem slice.</param>
   public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>> worker)
   {
      if (Count == 0)
      {
         return;
      }
      var batchStart = 0;
      while (batchStart < Count)
      {
         var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
         worker(Slice(batchStart, batchEnd - batchStart));
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
   public void BatchMapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>, UnifiedMem<TOther>> worker)
   {
      __.ThrowIfNot(otherToMapWith.Length == Count, "otherToMapWith must be the same length as this ZeroAllocMem");

      if (Count == 0)
      {
         return;
      }
      var batchStart = 0;
      while (batchStart < Count)
      {
         var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
         worker(Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
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
   private static int _GetBatchEndExclusive(int start, ZeroAllocMem<T> thisGuard, Func_RefArg<T, T, bool> isSameBatch)
   {
      var span = thisGuard.Span;
      var length = thisGuard.Count;

      var end = start + 1;
      while (end < length)
      {
         ref var r_previous = ref span[end - 1];
         ref var r_current = ref span[end];
         if (!isSameBatch(ref r_previous, ref r_current))
         {
            break;
         }
         end++;
      }
      return end;
   }

   public ZeroAllocMem<T> Clone()
   {
      var copy = ZeroAllocMem<T>.Allocate(Count);
      Span.CopyTo(copy.Span);
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
      return $"ZeroAllocMem<{typeof(T).Name}>[{Count}]";
   }
}
