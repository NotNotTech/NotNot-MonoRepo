// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Blake3;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Helpers;
// using Newtonsoft.Json.Linq; // Removed - no longer needed
using Nito.AsyncEx.Synchronous;
using NotNot;
using NotNot._internal.Threading;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;
using NotNot.Data;
using NotNot.Diagnostics;

//using Xunit.Sdk;

//using CommunityToolkit.HighPerformance;
//using DotNext;

public static class zz_Extensions_IntLong
{
	public static int _InterlockedIncrement(ref this int value)
	{
		return Interlocked.Increment(ref value);
	}

	public static long _InterlockedIncrement(ref this long value)
	{
		return Interlocked.Increment(ref value);
	}

	public static uint _InterlockedIncrement(ref this uint value)
	{
		return Interlocked.Increment(ref value);
	}

	public static ulong _InterlockedIncrement(ref this ulong value)
	{
		return Interlocked.Increment(ref value);
	}

	//public static void _Unpack(this long value, out int first, out int second)
	//{
	///////doesn't quite work with 2nd value.   need to look at bitmasking code
	//	first =(int)(value >> 32);
	//	second =(int)(value);
	//}

	/// <summary>
	/// rearange the bits in a deterministic way.  useful for spreading out significant bits so they are not clustered
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	public static long _Swizzle(this long value)
	{
		//size in bits
		var length = sizeof(long) * 8; //64
		var prime = 3;
		for (int i = 0; i < length; i++)
		{
			int swapIndex = (i + 1) * prime % length; // Deterministic swapping pattern

			// Swapping bits at i and swapIndex if they are different
			if ((value >> i & 1) != (value >> swapIndex & 1))
			{
				// Toggle bits at i and swapIndex
				value ^= 1L << i | 1L << swapIndex;
			}
		}
		return value;
	}

	/// <summary>
	/// rearange the bits in a deterministic way. useful for spreading out significant bits so they are not clustered
	/// </summary>
	public static long _Swizzle(this int value)
	{
		//size in bits
		var length = sizeof(int) * 8; //32
		var prime = 3;
		for (int i = 0; i < length; i++)
		{
			int swapIndex = (i + 1) * prime % length; // Deterministic swapping pattern

			// Swapping bits at i and swapIndex if they are different
			if ((value >> i & 1) != (value >> swapIndex & 1))
			{
				// Toggle bits at i and swapIndex
				value ^= 1 << i | 1 << swapIndex;
			}
		}
		return value;
	}

}

