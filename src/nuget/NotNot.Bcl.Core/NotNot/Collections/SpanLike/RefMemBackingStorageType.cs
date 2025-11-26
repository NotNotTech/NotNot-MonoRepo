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
/// Identifies which type of backing store is being used by RefMem{T}
/// </summary>
internal enum RefMemBackingStorageType
{
	/// <summary>
	/// Stack-allocated Span{T} (zero GC pressure)
	/// </summary>
	Span = 0,

	/// <summary>
	/// Heap/pooled Mem{T} (low GC pressure, flexible backing)
	/// </summary>
	Mem,

	/// <summary>
	/// Pooled ZeroAllocMem{T} with dispose protection (zero GC pressure, auto-return to pool)
	/// </summary>
	ZeroAllocMem,
}
