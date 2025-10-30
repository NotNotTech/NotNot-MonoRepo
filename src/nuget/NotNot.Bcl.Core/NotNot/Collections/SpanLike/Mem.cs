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
///    helpers to allocate a WriteMem instance
/// </summary>
public static class Mem
{
	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(ArraySegment<T> backingStore)
	{
		return Mem<T>.Wrap(backingStore);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(T[] array)
	{
		return Mem<T>.Wrap(array);
	}

	public static Mem<T> Clone<T>(UnifiedMem<T> toClone)
	{
		var copy = Mem<T>.Alloc(toClone.Length);
		toClone.Span.CopyTo(copy.Span);
		return copy;
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(Memory<T> memory)
	{
		return Mem<T>.Wrap(memory);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(List<T> list)
	{
		return Mem<T>.Wrap(list);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> AllocateAndAssign<T>(T singleItem)
	{
		return Mem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(int count)
	{
		return Mem<T>.Alloc(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return Mem<T>.Allocate(span);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(Mem<T> writeMem)
	{
		return writeMem;
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static Mem<T> Wrap<T>(ReadMem<T> readMem)
	{
		return Mem<T>.Wrap(readMem);
	}
}
