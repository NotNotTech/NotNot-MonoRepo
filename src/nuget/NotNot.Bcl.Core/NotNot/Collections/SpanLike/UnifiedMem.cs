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
/// A ref struct that provides unified Mem-like API for both stack-allocated Span and heap/pooled Mem scenarios.
/// Can wrap either a Span{T} for zero-allocation stack usage, or a Mem{T} for heap/pooled usage.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
/// <remarks>
/// USAGE MODES:
/// - Span Mode: Wraps Span{T} for pure stack allocation (zero GC pressure)
/// - Mem Mode: Wraps Mem{T} for heap/pooled allocation (delegates all operations)
///
/// SPAN MODE LIMITATIONS:
/// - DangerousGetArray(): NotSupportedException (span may not be array-backed)
/// - Clone(): NotSupportedException (would allocate, defeats stack-only purpose)
/// - Map/MapWith(): NotSupportedException (allocates new Mem, defeats purpose)
/// - BatchMap/BatchMapWith(): NotSupportedException (async incompatible with ref struct lifetime)
/// - AsReadMem(): NotSupportedException (cannot create ReadMem from raw Span)
///
/// EXAMPLES:
/// // Span mode (stack allocation)
/// Span{int} buffer = stackalloc int[128];
/// RefMem{int} refMem = buffer;
/// refMem[0] = 42;
///
/// // Mem mode (heap/pooled)
/// var mem = Mem.Allocate{int}(128);
/// RefMem{int} refMem = mem;
/// refMem[0] = 42;
/// refMem.Dispose(); // Returns to pool
/// </remarks>
[Obsolete("just have methods take Span<T>, each Mem<T> type is implicitly convertable to Span")]
public ref struct UnifiedMem<T> : IDisposable
{
   /// <summary>
   /// Span storage when in Span mode
   /// </summary>
   internal Span<T> _span;

   /// <summary>
   /// Mem storage when in Mem mode
   /// </summary>
   internal Mem<T> _mem;

   /// <summary>
   /// ZeroAllocMem storage when in ZeroAllocMem mode
   /// </summary>
   internal ZeroAllocMem<T> _zeroAllocMem;

   /// <summary>
   /// Identifies which backing storage type is active
   /// </summary>
   internal readonly RefMemBackingStorageType _backingStorageType;

#if CHECKED
   /// <summary>
   /// Dispose guard to ensure proper cleanup when wrapping ZeroAllocMem (CHECKED mode only)
   /// </summary>
   private DisposeGuard _disposeGuard;
#endif

   /// <summary>
   /// Creates a RefMem wrapping a Span (stack mode)
   /// </summary>
   /// <param name="span">Span to wrap (typically stackalloc)</param>
   public UnifiedMem(Span<T> span)
   {
      _span = span;
      _mem = default;
      _zeroAllocMem = default;
      _backingStorageType = RefMemBackingStorageType.Span;
#if CHECKED
      _disposeGuard = default;
#endif
   }

   /// <summary>
   /// Creates a RefMem wrapping a Mem (heap/pooled mode)
   /// </summary>
   /// <param name="mem">Mem to wrap</param>
   public UnifiedMem(Mem<T> mem)
   {
      _span = default;
      _mem = mem;
      _zeroAllocMem = default;
      _backingStorageType = RefMemBackingStorageType.Mem;
#if CHECKED
      _disposeGuard = default;
#endif
   }
   /// <summary>
   /// Creates a RefMem wrapping a ZeroAllocMem (pooled with dispose protection)
   /// </summary>
   /// <param name="zeroAllocMem">ZeroAllocMem to wrap</param>
   public UnifiedMem(ZeroAllocMem<T> zeroAllocMem)
   {
      _span = default;
      _mem = default;
      _zeroAllocMem = zeroAllocMem;
      _backingStorageType = RefMemBackingStorageType.ZeroAllocMem;
#if CHECKED
      _disposeGuard = new();
#endif
   }


   /// <summary>
   /// Implicit conversion from Span to RefMem (stack mode)
   /// </summary>
   public static implicit operator UnifiedMem<T>(Span<T> span) => new UnifiedMem<T>(span);

   /// <summary>
   /// Implicit conversion from Mem to RefMem (heap/pooled mode)
   /// </summary>
   public static implicit operator UnifiedMem<T>(Mem<T> mem) => new UnifiedMem<T>(mem);

   /// <summary>
   /// Implicit conversion from ZeroAllocMem to RefMem (pooled with dispose protection)
   /// </summary>
   public static implicit operator UnifiedMem<T>(ZeroAllocMem<T> zeroAllocMem) => new UnifiedMem<T>(zeroAllocMem);


   public static implicit operator UnifiedMem<T>(List<T> toWrap) => new Mem<T>(toWrap);
   public static implicit operator UnifiedMem<T>(ArraySegment<T> toWrap) => new Mem<T>(toWrap);
   public static implicit operator UnifiedMem<T>(T[] toWrap) => new Mem<T>(toWrap);
   public static implicit operator UnifiedMem<T>(Memory<T> toWrap) => new Mem<T>(toWrap);
   public static implicit operator UnifiedMem<T>(MemoryOwner_Custom<T> toWrap) => new Mem<T>(toWrap);

   public static implicit operator Span<T>(UnifiedMem<T> refMem) => refMem.Span;
   public static implicit operator ReadOnlySpan<T>(UnifiedMem<T> refMem) => refMem.Span;

   /// <summary>
   /// Gets a Span view over this memory
   /// </summary>
   public Span<T> Span
   {
      get
      {
         switch (_backingStorageType)
         {
            case RefMemBackingStorageType.Span:
               return _span;
            case RefMemBackingStorageType.Mem:
               return _mem.Span;
            case RefMemBackingStorageType.ZeroAllocMem:
               return _zeroAllocMem.Span;
            default:
               throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
         }
      }
   }

   /// <summary>
   /// Gets the number of slots in this memory view
   /// </summary>
   public int Length
   {
      get
      {
         switch (_backingStorageType)
         {
            case RefMemBackingStorageType.Span:
               return _span.Length;
            case RefMemBackingStorageType.Mem:
               return _mem.Length;
            case RefMemBackingStorageType.ZeroAllocMem:
               return _zeroAllocMem.Count;
            default:
               throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
         }
      }
   }

   ///// <summary>
   ///// Gets the number of elements in this memory view. Obsolete: use Count instead.
   ///// </summary>
   //[Obsolete("use .Count")]
   //public int Length
   //{
   //	get
   //	{
   //		switch (_backingStorageType)
   //		{
   //			case RefMemBackingStorageType.Span:
   //				return _span.Length;
   //			case RefMemBackingStorageType.Mem:
   //				return _mem.Length;
   //			case RefMemBackingStorageType.ZeroAllocMem:
   //				return _zeroAllocMem.Length;
   //			default:
   //				throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
   //		}
   //	}
   //}

   /// <summary>
   /// Gets a reference to the element at the specified index
   /// </summary>
   /// <param name="index">Zero-based index of the element</param>
   /// <returns>Reference to the element at the specified index</returns>
   public ref T this[int index]
   {
      get
      {
         switch (_backingStorageType)
         {
            case RefMemBackingStorageType.Span:
               return ref _span[index];
            case RefMemBackingStorageType.Mem:
               return ref _mem[index];
            case RefMemBackingStorageType.ZeroAllocMem:
               return ref _zeroAllocMem[index];
            default:
               throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
         }
      }
   }

   /// <summary>
   /// Creates a new memory view that is a slice of this memory
   /// </summary>
   /// <param name="offset">Starting offset within this memory</param>
   /// <param name="count">Number of elements in the slice</param>
   /// <returns>New memory view representing the slice</returns>
   public UnifiedMem<T> Slice(int offset, int count)
   {
      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            return new UnifiedMem<T>(_span.Slice(offset, count));
         case RefMemBackingStorageType.Mem:
            return new UnifiedMem<T>(_mem.Slice(offset, count));
         case RefMemBackingStorageType.ZeroAllocMem:
            return _zeroAllocMem.Slice(offset, count);
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Applies the specified mapping function to each element of this memory, writing results to the provided output buffer (zero-allocation version)
   /// </summary>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this memory.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by reference</param>
   public void Map<TResult>(UnifiedMem<TResult> toReturn, Func_Ref<T, TResult> mapFunc)
   {
      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            __.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this memory");
            var thisSpan = Span;
            var toReturnSpan = toReturn.Span;
            for (var i = 0; i < Length; i++)
            {
               ref var mappedResult = ref mapFunc(ref thisSpan[i]);
               toReturnSpan[i] = mappedResult;
            }
            break;
         case RefMemBackingStorageType.Mem:
            _mem.Map(toReturn, mapFunc);
            break;
         case RefMemBackingStorageType.ZeroAllocMem:
            _zeroAllocMem.Map(toReturn, mapFunc);
            break;
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Applies the specified mapping function to each element of this memory, writing results to the provided output buffer (zero-allocation version)
   /// </summary>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this memory.</param>
   /// <param name="mapFunc">Function that maps each element by reference, returning result by value</param>
   public void Map<TResult>(UnifiedMem<TResult> toReturn, Func_RefArg<T, TResult> mapFunc)
   {
      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            __.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this memory");
            var thisSpan = Span;
            var toReturnSpan = toReturn.Span;
            for (var i = 0; i < Length; i++)
            {
               var mappedResult = mapFunc(ref thisSpan[i]);
               toReturnSpan[i] = mappedResult;
            }
            break;
         case RefMemBackingStorageType.Mem:
            _mem.Map(toReturn, mapFunc);
            break;
         case RefMemBackingStorageType.ZeroAllocMem:
            _zeroAllocMem.Map(toReturn, mapFunc);
            break;
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Maps two memory instances in parallel using the specified function, writing results to the provided output buffer (zero-allocation version). All must have the same length.
   /// </summary>
   /// <typeparam name="TOther">Element type of the other memory</typeparam>
   /// <typeparam name="TResult">Result element type</typeparam>
   /// <param name="toReturn">Output buffer to write mapped results to. Must have same length as this memory.</param>
   /// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
   /// <param name="mapFunc">Function that maps pairs of elements by reference, returning result by reference</param>
   public void MapWith<TOther, TResult>(UnifiedMem<TResult> toReturn, UnifiedMem<TOther> otherToMapWith, Func_Ref<T, TOther, TResult> mapFunc)
   {
      __.ThrowIfNot(toReturn.Length == Length, "toReturn must be the same length as this memory");
      __.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this memory");

      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            {
               var thisSpan = Span;
               var otherSpan = otherToMapWith.Span;
               var toReturnSpan = toReturn.Span;
               for (var i = 0; i < Length; i++)
               {
                  ref var mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
                  toReturnSpan[i] = mappedResult;
               }
            }
            break;
         case RefMemBackingStorageType.Mem:
            {
               // For Mem backing, extract Mem<TOther> if available, otherwise use inline implementation
               if (otherToMapWith._backingStorageType == RefMemBackingStorageType.Mem)
               {
                  _mem.MapWith(toReturn, otherToMapWith._mem, mapFunc);
               }
               else
               {
                  // Fallback to Span-based implementation
                  var thisSpan = Span;
                  var otherSpan = otherToMapWith.Span;
                  var toReturnSpan = toReturn.Span;
                  for (var i = 0; i < Length; i++)
                  {
                     ref var mappedResult = ref mapFunc(ref thisSpan[i], ref otherSpan[i]);
                     toReturnSpan[i] = mappedResult;
                  }
               }
            }
            break;
         case RefMemBackingStorageType.ZeroAllocMem:
            _zeroAllocMem.MapWith(toReturn, otherToMapWith, mapFunc);
            break;
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Maps two memory instances in parallel using the specified action, modifying elements in place.
   /// Works in all modes by operating on underlying Span data.
   /// </summary>
   /// <typeparam name="TOther">Element type of the other memory</typeparam>
   /// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
   /// <param name="mapFunc">Action that processes pairs of elements by reference</param>
   public void MapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Action_Ref<T, TOther> mapFunc)
   {
      __.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this RefMem");
      var thisSpan = Span;
      var otherSpan = otherToMapWith.Span;

      for (var i = 0; i < Length; i++)
      {
         mapFunc(ref thisSpan[i], ref otherSpan[i]);
      }
   }



   /// <summary>
   /// Walks contiguous batches where isSameBatch returns true for each previous/current pair and calls worker once per range (synchronous version).
   /// Use this to process subgroups without extra allocations.
   /// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
   /// </summary>
   /// <param name="isSameBatch">Returns true when the second item should stay in the current batch; return false to start a new batch.</param>
   /// <param name="worker">Action executed for each contiguous batch, receiving a RefMem slice that references this instance's backing store.</param>
   public void BatchMap(Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>> worker)
   {
      if (Length == 0)
      {
         return;
      }

      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            // Inline implementation for Span mode
            var span = Span;
            var batchStart = 0;
            while (batchStart < Length)
            {
               var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
               worker(Slice(batchStart, batchEnd - batchStart));
               batchStart = batchEnd;
            }
            break;
         case RefMemBackingStorageType.Mem:
            // Delegate to _mem.BatchMap (implicit conversion Mem<T> -> RefMem<T>)
            _mem.BatchMap(isSameBatch, worker);
            break;
         case RefMemBackingStorageType.ZeroAllocMem:
            // Delegate to _zeroAllocMem.BatchMap
            _zeroAllocMem.BatchMap(isSameBatch, worker);
            break;
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Walks contiguous batches with parallel memory and calls worker once per range (synchronous version).
   /// IMPORTANT: we assume the underlying data is sorted so that your batching delegate is effective.
   /// </summary>
   /// <param name="otherToMapWith">Other memory to map in parallel with this one</param>
   /// <param name="isSameBatch">Returns true when the second item should stay in the current batch</param>
   /// <param name="worker">Action executed for each contiguous batch</param>
   public void BatchMapWith<TOther>(UnifiedMem<TOther> otherToMapWith, Func_RefArg<T, T, bool> isSameBatch, Action<UnifiedMem<T>, UnifiedMem<TOther>> worker)
   {
      __.ThrowIfNot(otherToMapWith.Length == Length, "otherToMapWith must be the same length as this memory");

      if (Length == 0)
      {
         return;
      }

      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            // Inline implementation for Span mode
            var span = Span;
            var batchStart = 0;
            while (batchStart < Length)
            {
               var batchEnd = _GetBatchEndExclusive(batchStart, this, isSameBatch);
               worker(Slice(batchStart, batchEnd - batchStart), otherToMapWith.Slice(batchStart, batchEnd - batchStart));
               batchStart = batchEnd;
            }
            break;
         case RefMemBackingStorageType.Mem:
            // Delegate to _mem.BatchMapWith (implicit conversions Mem<T> -> RefMem<T>)
            _mem.BatchMapWith(otherToMapWith, isSameBatch, worker);
            break;
         case RefMemBackingStorageType.ZeroAllocMem:
            _zeroAllocMem.BatchMapWith(otherToMapWith, isSameBatch, worker);
            break;
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }
   /// <summary>
   /// Local synchronous scanner that finds the end of a contiguous batch for RefMem
   /// </summary>
   /// <param name="start">Starting index for batch scan</param>
   /// <param name="thisRefMem">Memory to scan</param>
   /// <param name="isSameBatch">Function determining if adjacent elements belong to same batch</param>
   /// <returns>Exclusive end index of the batch</returns>
   private static int _GetBatchEndExclusive(int start, UnifiedMem<T> thisRefMem, Func_RefArg<T, T, bool> isSameBatch)
   {
      var span = thisRefMem.Span;
      var length = thisRefMem.Length;

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



   /// <summary>
   /// if owned by a pool, Disposes so the backing array can be recycled.
   /// Span mode: No-op (stack lifetime managed automatically).
   /// Mem mode: Delegates to wrapped Mem.Dispose().
   /// </summary>
   public void Dispose()
   {
      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            // No-op: stack lifetime managed automatically
            break;
         case RefMemBackingStorageType.Mem:
            _mem.Dispose();
            break;
         case RefMemBackingStorageType.ZeroAllocMem:
            _zeroAllocMem.Dispose();
#if CHECKED
            _disposeGuard.Dispose();
#endif
            break;
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Returns an enumerator for iterating over the elements in this memory
   /// </summary>
   /// <returns>Span enumerator</returns>
   public Span<T>.Enumerator GetEnumerator()
   {
      switch (_backingStorageType)
      {
         case RefMemBackingStorageType.Span:
            return _span.GetEnumerator();
         case RefMemBackingStorageType.Mem:
            return _mem.GetEnumerator();
         case RefMemBackingStorageType.ZeroAllocMem:
            return _zeroAllocMem.GetEnumerator();
         default:
            throw __.Throw($"unknown _backingStorageType {_backingStorageType}");
      }
   }

   /// <summary>
   /// Returns a string representation of this memory view showing type and count
   /// </summary>
   /// <returns>String in format "RefMem&lt;Type&gt;[Count] (Span|Mem mode)"</returns>
   public override string ToString()
   {
      var mode = _backingStorageType switch
      {
         RefMemBackingStorageType.Span => "Span",
         RefMemBackingStorageType.Mem => "Mem",
         RefMemBackingStorageType.ZeroAllocMem => "ZeroAllocMem",
         _ => "Unknown"
      };
      return $"RefMem<{typeof(T).Name}>[{Length}] ({mode} mode)";
   }
}
