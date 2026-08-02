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

public static class zz_Extensions_IList
{
	/// <summary>
	/// allows adding new, or removing the current item while enumerating.  Does not impact the enumeration.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <returns></returns>
	public static IEnumerable<T> _ForEachReverse<T>(this IList<T> list)
	{
		if (list is null)
		{
			yield break;
		}
		for (var i = list.Count - 1; i >= 0; i--)
		{
			yield return list[i];
		}
	}



	/// <summary>
	/// remove the first occurance and return it
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <returns></returns>
	public static T _Pop<T>(this IList<T> list)
	{
		var toReturn = list[0];
		list.RemoveAt(0);
		return toReturn;
	}
	public static bool _Contains<T>(this IList<T> list, Predicate<T> predicate) where T : class
	{
		foreach (var item in list)
		{
			if (predicate(item))
			{
				return true;
			}
		}

		return false;
	}

	public static bool _AddIfNotNull<T>(this IList<T> list, T? value) where T : class
	{
		if (value != null)
		{
			list.Add(value);
			return true;
		}

		return false;
	}

	public static T _GetOrCreate<T>(this IList<T> list, Func<T, bool> findPredicate,
		Func<T> createFunc)
	{
		T? value;
		if (list._TryGet(findPredicate, out value))
		{
			return value;
		}

		value = createFunc();
		list.Add(value);
		return value;
	}


	public static int _RemoveAll<T>(this IList<T> list, Func<T, bool> predicate)
	{
		var removeCount = 0;
		for (var i = list.Count - 1; i >= 0; i--)
			if (predicate(list[i]))
			{
				list.RemoveAt(i);
				removeCount++;
			}

		return removeCount;
	}

	public static bool _RemoveLast<T>(this IList<T> list, Func<T, bool> predicate)
	{
		for (var i = list.Count - 1; i >= 0; i--)
			if (predicate(list[i]))
			{
				list.RemoveAt(i);
				return true;
			}

		return false;
	}

	public static void _Shuffle<T>(this IList<T> target, ShuffleType shuffleType = ShuffleType.Random, IComparer<T>? comparer = null, Random? randomInstance = null)
	{
		if (target is List<T> list)
		{
			list._AsSpan()._Shuffle(shuffleType, comparer, randomInstance);
			return;
		}

		if (target.Count <= 1)
		{
			return;
		}

		using var buffer = Mem.Rent<T>(target.Count);
		var bufferSpan = buffer.GetSpan();

		for (var i = 0; i < target.Count; i++)
		{
			bufferSpan[i] = target[i];
		}

		bufferSpan._Shuffle(shuffleType, comparer, randomInstance);

		for (var i = 0; i < target.Count; i++)
		{
			target[i] = bufferSpan[i];
		}
	}
}

