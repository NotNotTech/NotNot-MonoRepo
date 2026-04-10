// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot;

namespace NotNot.Collections.SpanLike;

public enum ShuffleType
{
   Random,
   BalancedDistribution,
   Sorted,
   ReverseSorted,
}

/// <summary>
/// Extension methods for Span and ReadOnlySpan providing zero-allocation mapping and batching operations.
/// These methods mirror Mem functionality but operate directly on spans without allocating new memory.
/// </summary>
public static class zz_Extensions_Span
{
   #region Span<T> Shuffle Methods

   /// <summary>
   /// Reorders the span in place using the requested shuffle strategy.
   /// For sort-based strategies, <typeparamref name="T"/> must be comparable or a comparer must be supplied.
   /// </summary>
   public static void _Shuffle<T>(this Span<T> source, ShuffleType shuffleType = ShuffleType.Random, IComparer<T>? comparer = null, Random? randomInstance = null)
   {
      if (source.Length <= 1)
      {
         return;
      }

      randomInstance ??= Random.Shared;

      switch (shuffleType)
      {
         case ShuffleType.Random:
            ShuffleRandom(source, randomInstance);
            return;
         case ShuffleType.BalancedDistribution:
            ShuffleBalancedDistribution(source, comparer, randomInstance);
            return;
         case ShuffleType.Sorted:
            source.Sort(comparer);
            return;
         case ShuffleType.ReverseSorted:
            source.Sort(comparer);
            source.Reverse();
            return;
         default:
            throw new ArgumentOutOfRangeException(nameof(shuffleType), shuffleType, "Unknown shuffle type.");
      }
   }

   private static void ShuffleRandom<T>(Span<T> source, Random randomInstance)
   {
      for (var index = source.Length - 1; index > 0; index--)
      {
         var swapIndex = randomInstance.Next(index + 1);
         (source[index], source[swapIndex]) = (source[swapIndex], source[index]);
      }
   }

   private static void ShuffleBalancedDistribution<T>(Span<T> source, IComparer<T>? comparer, Random randomInstance)
   {
      comparer ??= Comparer<T>.Default;
      source.Sort(comparer);

      using var outputOwner = Mem.Rent<T>(source.Length);
      using var groupNextIndexOwner = Mem.Rent<int>(source.Length);
      using var groupRemainingOwner = Mem.Rent<int>(source.Length);

      var output = outputOwner.GetSpan();
      var groupNextIndices = groupNextIndexOwner.GetSpan();
      var groupRemaining = groupRemainingOwner.GetSpan();

      var groupCount = 0;
      var groupStart = 0;
      while (groupStart < source.Length)
      {
         var groupEnd = groupStart + 1;
         while (groupEnd < source.Length && comparer.Compare(source[groupStart], source[groupEnd]) == 0)
         {
            groupEnd++;
         }

         groupNextIndices[groupCount] = groupStart;
         groupRemaining[groupCount] = groupEnd - groupStart;
         groupCount++;
         groupStart = groupEnd;
      }

      var previousGroup = -1;
      for (var outputIndex = 0; outputIndex < output.Length; outputIndex++)
      {
         var selectedGroup = SelectNextBalancedGroup(groupRemaining.Slice(0, groupCount), previousGroup, randomInstance);
         output[outputIndex] = source[groupNextIndices[selectedGroup]];
         groupNextIndices[selectedGroup]++;
         groupRemaining[selectedGroup]--;
         previousGroup = selectedGroup;
      }

      output.CopyTo(source);
   }

   private static int SelectNextBalancedGroup(ReadOnlySpan<int> remainingCounts, int previousGroup, Random randomInstance)
   {
      var hasAlternativeGroup = false;
      if (previousGroup >= 0)
      {
         for (var i = 0; i < remainingCounts.Length; i++)
         {
            if (i != previousGroup && remainingCounts[i] > 0)
            {
               hasAlternativeGroup = true;
               break;
            }
         }
      }

      var maxRemaining = 0;
      var candidateCount = 0;

      for (var i = 0; i < remainingCounts.Length; i++)
      {
         var remaining = remainingCounts[i];
         if (remaining <= 0)
         {
            continue;
         }

         if (hasAlternativeGroup && i == previousGroup)
         {
            continue;
         }

         if (remaining > maxRemaining)
         {
            maxRemaining = remaining;
            candidateCount = 1;
         }
         else if (remaining == maxRemaining)
         {
            candidateCount++;
         }
      }

      __.ThrowIfNot(candidateCount > 0, "Balanced shuffle expected at least one remaining group.");

      var selectedOrdinal = randomInstance.Next(candidateCount);
      for (var i = 0; i < remainingCounts.Length; i++)
      {
         var remaining = remainingCounts[i];
         if (remaining != maxRemaining)
         {
            continue;
         }

         if (hasAlternativeGroup && i == previousGroup)
         {
            continue;
         }

         if (selectedOrdinal == 0)
         {
            return i;
         }

         selectedOrdinal--;
      }

      throw new InvalidOperationException("Balanced shuffle failed to select a group.");
   }

   #endregion

   #region Span<T> Map Methods

   /// <summary>
   /// Maps each element of the source span using the specified function, writing results to the output span.
   /// Zero-allocation operation that requires pre-allocated output buffer.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="source">Source span to map from</param>
   /// <param name="output">Output span to write results to. Must have same length as source.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
   public static void _Map<T, TResult>(this Span<T> source, Span<TResult> output, Func_Ref<T, TResult> mapFunc)
   {
      __.ThrowIfNot(output.Length == source.Length, "output must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         ref var r_mappedResult = ref mapFunc(ref source[i]);
         output[i] = r_mappedResult;
      }
   }

   /// <summary>
   /// Maps each element of the source span using the specified function, writing results to the output span.
   /// Zero-allocation operation that requires pre-allocated output buffer.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="source">Source span to map from</param>
   /// <param name="output">Output span to write results to. Must have same length as source.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
   public static void _Map<T, TResult>(this Span<T> source, Span<TResult> output, Func_RefArg<T, TResult> mapFunc)
   {
      __.ThrowIfNot(output.Length == source.Length, "output must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         var mappedResult = mapFunc(ref source[i]);
         output[i] = mappedResult;
      }
   }
	public static RentedMem<TResult> _Map<T,TResult>(this Span<T> source, Func_RefArg<T, TResult> mapFunc)
	{
		var toReturn = Mem.Rent<TResult>(source.Length);
		source._Map(toReturn, mapFunc);
		return toReturn;
	}

	#endregion

	#region Span<T> MapWith Methods

	/// <summary>
	/// Maps two spans in parallel using the specified action, modifying elements in place.
	/// Zero-allocation operation that modifies source span elements directly.
	/// </summary>
	/// <typeparam name="T">Source element type</typeparam>
	/// <typeparam name="TOther">Other span element type</typeparam>
	/// <param name="source">Source span to map (can be modified in place)</param>
	/// <param name="other">Other span to map in parallel with source</param>
	/// <param name="mapFunc">Action that processes pairs of elements by reference</param>
	public static void _MapWith<T, TOther>(this Span<T> source, Span<TOther> other, Action_Ref<T, TOther> mapFunc)
   {
      __.ThrowIfNot(other.Length == source.Length, "other must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         mapFunc(ref source[i], ref other[i]);
      }
   }

   /// <summary>
   /// Maps two spans in parallel using the specified function, writing results to the output span.
   /// Zero-allocation operation that requires pre-allocated output buffer.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TOther">Other span element type</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="source">Source span to map from</param>
   /// <param name="other">Other span to map in parallel with source</param>
   /// <param name="output">Output span to write results to. Must have same length as source.</param>
   /// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
   public static void _MapWith<T, TOther, TResult>(this Span<T> source, Span<TOther> other, Span<TResult> output, Func_Ref<T, TOther, TResult> mapFunc)
   {
      __.ThrowIfNot(output.Length == source.Length, "output must be the same length as source");
      __.ThrowIfNot(other.Length == source.Length, "other must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         ref var r_mappedResult = ref mapFunc(ref source[i], ref other[i]);
         output[i] = r_mappedResult;
      }
   }

   /// <summary>
   /// Maps two spans in parallel using the specified function, writing results to the output span.
   /// Zero-allocation operation that requires pre-allocated output buffer.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TOther">Other span element type</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="source">Source span to map from</param>
   /// <param name="other">Other span to map in parallel with source</param>
   /// <param name="output">Output span to write results to. Must have same length as source.</param>
   /// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by value</param>
   public static void _MapWith<T, TOther, TResult>(this Span<T> source, Span<TOther> other, Span<TResult> output, Func_RefArg<T, TOther, TResult> mapFunc)
   {
      __.ThrowIfNot(output.Length == source.Length, "output must be the same length as source");
      __.ThrowIfNot(other.Length == source.Length, "other must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         var mappedResult = mapFunc(ref source[i], ref other[i]);
         output[i] = mappedResult;
      }
   }

   #endregion

   #region Span<T> Batch Methods

   /// <summary>
   /// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per batch.
   /// Zero-allocation operation for processing subgroups without extra allocations.
   /// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
   /// </summary>
   /// <typeparam name="T">Element type</typeparam>
   /// <param name="source">Source span to batch</param>
   /// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
   /// <param name="worker">Action executed for each contiguous batch, receiving a span slice</param>
   public static void _BatchMap<T>(this Span<T> source, Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>> worker)
   {
      if (source.Length == 0)
      {
         return;
      }

      var batchStart = 0;
      while (batchStart < source.Length)
      {
         var batchEnd = GetBatchEndExclusive(source, batchStart, isSameBatch);
         worker(source.Slice(batchStart, batchEnd - batchStart));
         batchStart = batchEnd;
      }
   }

   /// <summary>
   /// Walks contiguous batches with parallel span and calls worker once per batch.
   /// Zero-allocation operation for processing parallel subgroups without extra allocations.
   /// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TOther">Other span element type</typeparam>
   /// <param name="source">Source span to batch</param>
   /// <param name="other">Other span to batch in parallel with source</param>
   /// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
   /// <param name="worker">Action executed for each contiguous batch, receiving span slices</param>
   public static void _BatchMapWith<T, TOther>(this Span<T> source, Span<TOther> other, Func_RefArg<T, T, bool> isSameBatch, Action<Span<T>, Span<TOther>> worker)
   {
      __.ThrowIfNot(other.Length == source.Length, "other must be the same length as source");

      if (source.Length == 0)
      {
         return;
      }

      var batchStart = 0;
      while (batchStart < source.Length)
      {
         var batchEnd = GetBatchEndExclusive(source, batchStart, isSameBatch);
         worker(source.Slice(batchStart, batchEnd - batchStart), other.Slice(batchStart, batchEnd - batchStart));
         batchStart = batchEnd;
      }
   }

   /// <summary>
   /// Helper method to find the exclusive end index of a batch
   /// </summary>
   private static int GetBatchEndExclusive<T>(Span<T> span, int start, Func_RefArg<T, T, bool> isSameBatch)
   {
      var end = start + 1;
      while (end < span.Length)
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

   #endregion

   #region ReadOnlySpan<T> Map Methods

   /// <summary>
   /// Maps each element of the readonly source span using the specified function, writing results to the output span.
   /// Zero-allocation operation that requires pre-allocated output buffer.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="source">Readonly source span to map from</param>
   /// <param name="output">Output span to write results to. Must have same length as source.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
   public static void _Map<T, TResult>(this ReadOnlySpan<T> source, Span<TResult> output, Func_RefArg<T, TResult> mapFunc)
   {
      __.ThrowIfNot(output.Length == source.Length, "output must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         ref readonly var r_sourceRef = ref source[i];
         var mappedResult = mapFunc(ref System.Runtime.CompilerServices.Unsafe.AsRef(in r_sourceRef));
         output[i] = mappedResult;
      }
   }

   #endregion

   #region ReadOnlySpan<T> MapWith Methods

   /// <summary>
   /// Maps two readonly spans in parallel using the specified function, writing results to the output span.
   /// Zero-allocation operation that requires pre-allocated output buffer.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TOther">Other span element type</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="source">Readonly source span to map from</param>
   /// <param name="other">Readonly other span to map in parallel with source</param>
   /// <param name="output">Output span to write results to. Must have same length as source.</param>
   /// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by value</param>
   public static void _MapWith<T, TOther, TResult>(this ReadOnlySpan<T> source, ReadOnlySpan<TOther> other, Span<TResult> output, Func_RefArg<T, TOther, TResult> mapFunc)
   {
      __.ThrowIfNot(output.Length == source.Length, "output must be the same length as source");
      __.ThrowIfNot(other.Length == source.Length, "other must be the same length as source");

      for (var i = 0; i < source.Length; i++)
      {
         ref readonly var r_sourceRef = ref source[i];
         ref readonly var r_otherRef = ref other[i];
         var mappedResult = mapFunc(ref System.Runtime.CompilerServices.Unsafe.AsRef(in r_sourceRef), ref System.Runtime.CompilerServices.Unsafe.AsRef(in r_otherRef));
         output[i] = mappedResult;
      }
   }

   #endregion

   #region ReadOnlySpan<T> Batch Methods

   /// <summary>
   /// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per batch.
   /// Zero-allocation operation for processing readonly subgroups without extra allocations.
   /// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
   /// </summary>
   /// <typeparam name="T">Element type</typeparam>
   /// <param name="source">Readonly source span to batch</param>
   /// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
   /// <param name="worker">Action executed for each contiguous batch, receiving a readonly span slice</param>
   public static void _BatchMap<T>(this ReadOnlySpan<T> source, Func_RefArg<T, T, bool> isSameBatch, Action_RoSpan<T> worker)
   {
      if (source.Length == 0)
      {
         return;
      }

      var batchStart = 0;
      while (batchStart < source.Length)
      {
         var batchEnd = GetBatchEndExclusive(source, batchStart, isSameBatch);
         worker(source.Slice(batchStart, batchEnd - batchStart));
         batchStart = batchEnd;
      }
   }

   /// <summary>
   /// Walks contiguous batches with parallel readonly span and calls worker once per batch.
   /// Zero-allocation operation for processing parallel readonly subgroups without extra allocations.
   /// IMPORTANT: Assumes the underlying data is sorted so that batching delegate is effective.
   /// </summary>
   /// <typeparam name="T">Source element type</typeparam>
   /// <typeparam name="TOther">Other span element type</typeparam>
   /// <param name="source">Readonly source span to batch</param>
   /// <param name="other">Readonly other span to batch in parallel with source</param>
   /// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
   /// <param name="worker">Action executed for each contiguous batch, receiving readonly span slices</param>
   public static void _BatchMapWith<T, TOther>(this ReadOnlySpan<T> source, ReadOnlySpan<TOther> other, Func_RefArg<T, T, bool> isSameBatch, Action<ReadOnlySpan<T>, ReadOnlySpan<TOther>> worker)
   {
      __.ThrowIfNot(other.Length == source.Length, "other must be the same length as source");

      if (source.Length == 0)
      {
         return;
      }

      var batchStart = 0;
      while (batchStart < source.Length)
      {
         var batchEnd = GetBatchEndExclusive(source, batchStart, isSameBatch);
         worker(source.Slice(batchStart, batchEnd - batchStart), other.Slice(batchStart, batchEnd - batchStart));
         batchStart = batchEnd;
      }
   }

   /// <summary>
   /// Helper method to find the exclusive end index of a batch for readonly spans
   /// </summary>
   private static int GetBatchEndExclusive<T>(ReadOnlySpan<T> span, int start, Func_RefArg<T, T, bool> isSameBatch)
   {
      var end = start + 1;
      while (end < span.Length)
      {
         ref readonly var r_previous = ref span[end - 1];
         ref readonly var r_current = ref span[end];
         if (!isSameBatch(ref System.Runtime.CompilerServices.Unsafe.AsRef(in r_previous), ref System.Runtime.CompilerServices.Unsafe.AsRef(in r_current)))
         {
            break;
         }
         end++;
      }
      return end;
   }

   #endregion
}
