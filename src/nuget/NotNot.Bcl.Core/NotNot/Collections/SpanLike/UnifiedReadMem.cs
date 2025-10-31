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
/// A ref struct that provides unified read-only memory API by wrapping UnifiedMem{T} with read-only semantics.
/// Also supports ReadOnlySpan{T} mode which UnifiedMem{T} does not support.
/// </summary>
/// <typeparam name="T">Element type</typeparam>
/// <remarks>
/// DESIGN: Thin wrapper over UnifiedMem{T} to eliminate code duplication.
/// - UnifiedMem{T} handles Span, Mem, and ZeroAllocMem modes
/// - UnifiedReadMem{T} adds ReadOnlySpan mode and enforces read-only semantics
///
/// USAGE MODES:
/// - ReadOnlySpan Mode: Wraps ReadOnlySpan{T} for pure stack allocation (zero GC pressure)
/// - Writable Modes: Wraps UnifiedMem{T} with read-only view (Span/Mem/ZeroAllocMem)
///
/// READ-ONLY SEMANTICS:
/// - No ref indexer (returns value, not reference)
/// - No Map/MapWith methods (cannot write results)
/// - Span property returns ReadOnlySpan{T}
/// - Can convert to writable UnifiedMem{T} via AsWriteMem()
///
/// EXAMPLES:
/// // ReadOnlySpan mode (stack allocation)
/// ReadOnlySpan{int} buffer = stackalloc int[128];
/// UnifiedReadMem{int} readMem = buffer;
/// int value = readMem[0]; // read-only access
///
/// // Mem mode (heap/pooled, read-only view)
/// var mem = Mem{int}.Alloc(128);
/// UnifiedReadMem{int} readMem = mem;
/// int value = readMem[0]; // read-only access
/// </remarks>
[Obsolete("just have methods take Span<T>, each Mem<T> type is implicitly convertable to Span")]
public readonly ref struct UnifiedReadMem<T>
{
   /// <summary>
   /// UnifiedMem storage for writable backing modes (Span, Mem, ZeroAllocMem)
   /// </summary>
   private readonly UnifiedMem<T> _unifiedMem;

   /// <summary>
   /// ReadOnlySpan storage when in ReadOnlySpan mode (UnifiedMem doesn't support ReadOnlySpan)
   /// </summary>
   private readonly ReadOnlySpan<T> _readOnlySpan;

   /// <summary>
   /// True if this wraps a ReadOnlySpan, false if it wraps a UnifiedMem
   /// </summary>
   private readonly bool _isReadOnlySpanMode;

   /// <summary>
   /// Creates a UnifiedReadMem wrapping a ReadOnlySpan (stack mode)
   /// </summary>
   /// <param name="readOnlySpan">ReadOnlySpan to wrap (typically stackalloc)</param>
   public UnifiedReadMem(ReadOnlySpan<T> readOnlySpan)
   {
      _readOnlySpan = readOnlySpan;
      _unifiedMem = default;
      _isReadOnlySpanMode = true;
   }

   /// <summary>
   /// Creates a UnifiedReadMem wrapping a UnifiedMem (writable modes with read-only view)
   /// </summary>
   /// <param name="unifiedMem">UnifiedMem to wrap with read-only semantics</param>
   public UnifiedReadMem(UnifiedMem<T> unifiedMem)
   {
      _unifiedMem = unifiedMem;
      _readOnlySpan = default;
      _isReadOnlySpanMode = false;
   }

   /// <summary>
   /// Implicit conversion from ReadOnlySpan to UnifiedReadMem (stack mode)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(ReadOnlySpan<T> readOnlySpan) => new UnifiedReadMem<T>(readOnlySpan);

   /// <summary>
   /// Implicit conversion from Span to UnifiedReadMem (stack mode, read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(Span<T> span) => new UnifiedReadMem<T>(span);

   /// <summary>
   /// Implicit conversion from UnifiedMem to UnifiedReadMem (read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(UnifiedMem<T> unifiedMem) => new UnifiedReadMem<T>(unifiedMem);

   /// <summary>
   /// Implicit conversion from ReadMem to UnifiedReadMem (heap/pooled read-only mode)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(ReadMem<T> readMem) => new UnifiedReadMem<T>(new UnifiedMem<T>(readMem.AsWriteMem()));

   /// <summary>
   /// Implicit conversion from Mem to UnifiedReadMem (heap/pooled mode, read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(Mem<T> mem) => new UnifiedReadMem<T>(new UnifiedMem<T>(mem));

   /// <summary>
   /// Implicit conversion from ZeroAllocMem to UnifiedReadMem (pooled mode, read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(ZeroAllocMem<T> zeroAllocMem) => new UnifiedReadMem<T>(new UnifiedMem<T>(zeroAllocMem));

   /// <summary>
   /// Implicit conversion from List to UnifiedReadMem (read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(List<T> toWrap) => new UnifiedReadMem<T>(new UnifiedMem<T>(toWrap));

   /// <summary>
   /// Implicit conversion from ArraySegment to UnifiedReadMem (read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(ArraySegment<T> toWrap)
   {
      Mem<T> mem = toWrap;
      return new UnifiedReadMem<T>(new UnifiedMem<T>(mem));
   }

   /// <summary>
   /// Implicit conversion from array to UnifiedReadMem (read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(T[] toWrap) => new UnifiedReadMem<T>(new UnifiedMem<T>(toWrap));

   /// <summary>
   /// Implicit conversion from Memory to UnifiedReadMem (read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(Memory<T> toWrap) => new UnifiedReadMem<T>(new UnifiedMem<T>(toWrap));

   /// <summary>
   /// Implicit conversion from MemoryOwner_Custom to UnifiedReadMem (read-only view)
   /// </summary>
   public static implicit operator UnifiedReadMem<T>(MemoryOwner_Custom<T> toWrap) => new UnifiedReadMem<T>(new UnifiedMem<T>(toWrap));


   public static implicit operator ReadOnlySpan<T>(UnifiedReadMem<T> refMem) => refMem.Span;

   /// <summary>
   /// Gets a ReadOnlySpan view over this memory
   /// </summary>
   public ReadOnlySpan<T> Span =>
      _isReadOnlySpanMode
         ? _readOnlySpan
         : _unifiedMem.Span;

   /// <summary>
   /// Gets the number of elements in this memory view
   /// </summary>
   public int Length =>
      _isReadOnlySpanMode
         ? _readOnlySpan.Length
         : _unifiedMem.Length;

   /// <summary>
   /// Gets the value of the element at the specified index (read-only)
   /// </summary>
   /// <param name="index">Zero-based index of the element</param>
   /// <returns>Value of the element at the specified index</returns>
   public T this[int index] =>
      _isReadOnlySpanMode
         ? _readOnlySpan[index]
         : _unifiedMem[index];

   /// <summary>
   /// Creates a new read-only memory view that is a slice of this memory
   /// </summary>
   /// <param name="offset">Starting offset within this memory</param>
   /// <param name="count">Number of elements in the slice</param>
   /// <returns>New read-only memory view representing the slice</returns>
   public UnifiedReadMem<T> Slice(int offset, int count) =>
      _isReadOnlySpanMode
         ? new UnifiedReadMem<T>(_readOnlySpan.Slice(offset, count))
         : new UnifiedReadMem<T>(_unifiedMem.Slice(offset, count));

   /// <summary>
   /// Converts this read-only memory view to a writable memory view.
   /// ReadOnlySpan mode: NotSupportedException (cannot convert ReadOnlySpan to writable without backing storage).
   /// </summary>
   /// <returns>Writable memory view over the same backing storage</returns>
   public UnifiedMem<T> AsWriteMem()
   {
      if (_isReadOnlySpanMode)
      {
         throw new NotSupportedException("Cannot convert ReadOnlySpan-backed UnifiedReadMem to writable UnifiedMem. Use Mem-backed storage for read/write access.");
      }
      return _unifiedMem;
   }

   /// <summary>
   /// Returns an enumerator for iterating over the elements in this memory
   /// </summary>
   /// <returns>ReadOnlySpan enumerator</returns>
   public ReadOnlySpan<T>.Enumerator GetEnumerator() =>
      _isReadOnlySpanMode
         ? _readOnlySpan.GetEnumerator()
         : ((ReadOnlySpan<T>)_unifiedMem.Span).GetEnumerator();

   /// <summary>
   /// Returns a string representation of this memory view showing type and count
   /// </summary>
   /// <returns>String in format "UnifiedReadMem&lt;Type&gt;[Count] (mode)"</returns>
   public override string ToString()
   {
      if (_isReadOnlySpanMode)
      {
         return $"UnifiedReadMem<{typeof(T).Name}>[{Length}] (ReadOnlySpan mode)";
      }
      return $"UnifiedReadMem<{typeof(T).Name}>[{Length}] ({_unifiedMem.ToString()})";
   }
}
