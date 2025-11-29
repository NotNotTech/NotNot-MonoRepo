// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance.Buffers;
using NotNot.Collections.Advanced;

namespace NotNot.Collections.SpanLike;


///// <summary>
///// Static helper methods for creating Mem instances
///// </summary>
//public static class Mem
//{
//	/// <summary>
//	/// Wrap a Mem as an ephemeral view - strips ownership, caller retains responsibility for disposal
//	/// </summary>
//	public static Mem<T> Wrap<T>(Mem<T> mem) => mem;

//	/// <summary>
//	/// Wrap a RentedMem as an ephemeral view - strips ownership, caller retains responsibility for disposal
//	/// </summary>
//	public static Mem<T> Wrap<T>(RentedMem<T> mem) => mem;
//}
