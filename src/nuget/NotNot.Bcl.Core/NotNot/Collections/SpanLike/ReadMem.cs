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
///    helpers to allocate a ReadMem instance
/// </summary>
public static class ReadMem
{
	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(ArraySegment<T> backingStore)
	{
		return ReadMem<T>.Wrap(backingStore);
	}

	/// <summary>
	///   use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(T[] array)
	{
		return ReadMem<T>.Wrap(array);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(Memory<T> memory)
	{
		return ReadMem<T>.Wrap(memory);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(List<T> list)
	{
		return ReadMem<T>.Wrap(list);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> AllocateAndAssign<T>(T singleItem)
	{
		return ReadMem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> Allocate<T>(int count)
	{
		return ReadMem<T>.Allocate(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return ReadMem<T>.Allocate(span);
	}

	/// <summary>
	///    use an existing collection as the backing storage
	/// </summary>
	public static ReadMem<T> Wrap<T>(Mem<T> writeMem)
	{
		return ReadMem<T>.Wrap(writeMem);
	}

	/// <summary>
	///   Wrap a single item as the backing storage. Creates a span-accessible single-element ReadMem.
	/// </summary>
	public static ReadMem<T> WrapSingle<T>(T singleItem)
	{
		return new ReadMem<T>(singleItem);
	}
}
