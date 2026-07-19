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

public static class zz_Extensions_List
{
	/// <summary>
	/// optimal resizing of a list to a target count.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <param name="count"></param>
	public static void _Resize<T>(this List<T> list, int count, bool doNotInitialize = false)
	{
		var originalCount = list.Count;
		System.Runtime.InteropServices.CollectionsMarshal.SetCount(list, count);
		var span = list._AsSpan();
		if (doNotInitialize is false)
		{
			if (count > originalCount)
			{
				span.Slice(originalCount, count - originalCount).Clear();
			}
		}
	}

	/// <summary>
	/// get a copy of the list in a MemoryOwner_Custom, used to reduce allocations
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="list"></param>
	/// <returns></returns>
	public static MemoryOwner_Custom<T> _MemoryOwnerCopy<T>(this List<T> list)
	{
		if (list is null)
		{
			return MemoryOwner_Custom<T>.Empty;
		}
		var toReturn = MemoryOwner_Custom<T>.Allocate(list.Count);
		list._AsSpan().CopyTo(toReturn.Span);
		return toReturn;
	}

	private static ThreadLocal<Random> _rand = new(() => new Random());

	public static T _PickRandom<T>(this IList<T> target, Random? randomInstance = null)
	{
		// _rand's factory always creates a non-null Random per thread.
		randomInstance ??= _rand.Value!;
		return target[randomInstance.Next(target.Count)];
	}

	public static bool _TryRemoveRandom<T>(this IList<T> target, [MaybeNullWhen(false)] out T value)
	{
		if (target.Count == 0)
		{
			value = default;
			return false;
		}

		var index = -1;
		//lock (_rand)
		{
			// _rand's factory always creates a non-null Random per thread.
			index = _rand.Value!.Next(0, target.Count);
		}

		value = target[index];
		target.RemoveAt(index);
		return true;
	}

	public static List<T> _TakeAndRemove<T>(this List<T> list, int maxCount, bool takeFromStart = false)
	{
		maxCount = Math.Min(maxCount, list.Count);
		if (takeFromStart)
		{
			var toReturn = list.GetRange(0, maxCount).ToList();
			list.RemoveRange(0, maxCount);
			return toReturn;
		}
		else
		{
			var toReturn = list.GetRange(list.Count - maxCount, maxCount);
			list.RemoveRange(list.Count - maxCount, maxCount);
			return toReturn;
		}
	}

	public static void _TakeFrom<T>(this List<T> list, List<T> other, int maxCount, bool takeFromStart = false)
	{
		maxCount = Math.Min(maxCount, other.Count);
		if (takeFromStart)
		{
			for (var i = 0; i < maxCount; i++)
			{
				list.Add(other[i]);
			}

			other.RemoveRange(0, maxCount);
		}
		else
		{
			for (var i = other.Count - maxCount; i < other.Count; i++)
			{
				list.Add(other[i]);
			}

			other.RemoveRange(other.Count - maxCount, maxCount);
		}
	}

	public static bool _TryTakeLast<T>(this IList<T> target, [MaybeNullWhen(false)] out T value)
	{
		if (target.Count == 0)
		{
			value = default;
			return false;
		}

		var index = target.Count - 1;
		value = target[index];
		target.RemoveAt(index);
		return true;
	}

	public static void _RemoveLast<T>(this IList<T> target)
	{
		target.RemoveAt(target.Count - 1);
	}

	/// <summary>
	///    create a clone of this list.  individual value's should inherits from IClonable, or be structs with no references.
	///    (otherwise error)
	/// </summary>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="source"></param>
	/// <returns></returns>
	public static List<TValue> _Clone<TValue>(this List<TValue> source)
	{
		//try to clone all values
		var toReturn = new List<TValue>(source.Count);

		foreach (var value in source)
		{
			__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);

			if (value is ICloneable cv)
			{
				toReturn.Add((TValue)cv.Clone());
			}
			else
			{
				toReturn.Add(value);
			}
		}

		return toReturn;
	}

	/// <summary>
	///    expands the list to the target capacity if it's not already, then sets the value at that index
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="target"></param>
	public static void _ExpandAndSet<T>(this IList<T> target, int index, T value)
	{
		// Intentional: pad the list with default(T) up to the target index.
		while (target.Count <= index)
			target.Add(default!);

		target[index] = value;
	}

	public static void _Randomize<T>(this IList<T> target)
	{
		target._Shuffle(ShuffleType.Random, randomInstance: _rand.Value);
	}

	/// <summary>
	///    true if all elements of the lists match.  can be out of order.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="target"></param>
	/// <param name="other"></param>
	/// <returns></returns>
	public static bool _ContainsIdential<T>(this List<T> target, List<T>? other)
	{
		__.GetLogger()._EzError(other is not null && target is not null);
		if (target is null || other is null || target.Count != other.Count)
		{
			return false;
		}

		var span1 = target._AsSpan();
		var span2 = other._AsSpan();


		//look through all span1 for all matches
		for (var i = 0; i < span1.Length; i++)
		{
			var found = false;
			for (var j = 0; j < span2.Length; j++)
				if (Equals(span1[j], other[j]))
				{
					found = true;
					break;
				}

			if (found == false)
			{
				return false;
			}
		}


		//look through all span2 for all matches
		for (var i = 0; i < span2.Length; i++)
		{
			var found = false;
			for (var j = 0; j < span1.Length; j++)
				if (Equals(span1[j], other[j]))
				{
					found = true;
					break;
				}

			if (found == false)
			{
				return false;
			}
		}

		return true;
	}


	/// <summary>
	///    warning: do not modify list while enumerating span
	/// </summary>
	public static Span<T> _AsSpan<T>(this List<T> list)
	{
		return CollectionsMarshal.AsSpan(list);
	}

	public static void _Shuffle<T>(this List<T> target, ShuffleType shuffleType = ShuffleType.Random, IComparer<T>? comparer = null, Random? randomInstance = null)
	{
		target._AsSpan()._Shuffle(shuffleType, comparer, randomInstance);
	}

	/// <summary>
	/// return a ref var.  Only useful with structs.   UNSAFE warning: do not modify list while using ref
	/// </summary>
	public static ref T _RefGet<T>(this List<T> list, int index)
	{
		var span = list._AsSpan();
		return ref span[index];
	}
}

// Extension methods for TaskCompletionSource{TResult}.
// threadsafety: static=true, instance=false
// from:
// https://github.com/tunnelvisionlabs/dotnet-threading/blob/3e99a9d13476a1e8224d81f282f3cedad143c1bc/Rackspace.Threading/TaskCompletionSourceExtensions.cs

