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
/// Identifies which type of backing store is being used by Mem{T} and ReadMem{T}
/// </summary>
internal enum MemBackingStorageType
{
	/// <summary>
	/// not initialized.  an error if being used.
	/// </summary>
	None = 0,

	/// <summary>
	/// if pooled (Mem.Alloc()), this will be set. a reference to the pooled location so it can be recycled
	/// while this will naturally be GC'd when all referencing Mem{T}'s go out-of-scope, you can manually do so by calling Dispose or the using pattern
	/// </summary>
	MemoryOwner_Custom,
	/// <summary>
	/// manually constructed Mem using your own List. not disposed of when out-of-scope
	/// </summary>
	List,
	/// <summary>
	/// manually constructed Mem using your own Array. not disposed of when out-of-scope
	/// </summary>
	Array,
	/// <summary>
	/// manually constructed Mem using your own Memory. not disposed of when out-of-scope
	/// </summary>
	Memory,
	/// <summary>
	/// Mem backed by ObjectPool.RentedArray. Disposed via wrapper's Dispose() method when owner is disposed.
	/// </summary>
	RentedArray,
	/// <summary>
	/// Mem backed by ObjectPool.Rented&lt;List&lt;T&gt;&gt;. Disposed via wrapper's Dispose() method when owner is disposed.
	/// </summary>
	RentedList,
}
