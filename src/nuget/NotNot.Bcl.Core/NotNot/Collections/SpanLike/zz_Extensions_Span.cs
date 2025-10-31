// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot;

namespace NotNot.Collections.SpanLike;

/// <summary>
/// Extension methods for Span and ReadOnlySpan providing zero-allocation mapping and batching operations.
/// These methods mirror UnifiedMem functionality but operate directly on spans without allocating new memory.
/// </summary>
public static class zz_Extensions_Span
{
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
         ref var mappedResult = ref mapFunc(ref source[i]);
         output[i] = mappedResult;
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
         ref var mappedResult = ref mapFunc(ref source[i], ref other[i]);
         output[i] = mappedResult;
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
   public static void _BatchMap<T>(this Span<T> source, Func_RefArg<T, T, bool> isSameBatch, Action_Span<T> worker)
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
         ref readonly var sourceRef = ref source[i];
         var mappedResult = mapFunc(ref System.Runtime.CompilerServices.Unsafe.AsRef(in sourceRef));
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
         ref readonly var sourceRef = ref source[i];
         ref readonly var otherRef = ref other[i];
         var mappedResult = mapFunc(ref System.Runtime.CompilerServices.Unsafe.AsRef(in sourceRef), ref System.Runtime.CompilerServices.Unsafe.AsRef(in otherRef));
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
         ref readonly var previous = ref span[end - 1];
         ref readonly var current = ref span[end];
         if (!isSameBatch(ref System.Runtime.CompilerServices.Unsafe.AsRef(in previous), ref System.Runtime.CompilerServices.Unsafe.AsRef(in current)))
         {
            break;
         }
         end++;
      }
      return end;
   }

   #endregion
}
