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

public static class zz_Extensions_IEnumerable
{

	public static bool _Find<TBase, TDerived>(this IEnumerable<TBase> enumerable, Predicate<TDerived> match) where TDerived : TBase
	{
		return enumerable.Any((value) =>
		{
			if (value is TDerived derived)
			{
				return match(derived);
			}
			return false;
		});
	}




	/// <summary>
	///    create a clone of this iEnumerable as a list.  individual value's should inherits from IClonable, or be structs with
	///    no references. (otherwise error)
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="source"></param>
	/// <returns></returns>
	public static List<TValue> _CloneElements<TValue>(this IEnumerable<TValue> source)
	{
		//try to clone all values
		var toReturn = new List<TValue>();

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


	///// <summary>
	/////    executes and awaits sequentially
	///// </summary>
	//public static async ValueTask _ForEach<TValue>(this IEnumerable<TValue> enumerable,
	//	Func<TValue, ValueTask> asyncAction)
	//{
	//	foreach (var val in enumerable)
	//	{
	//		await asyncAction(val);
	//	}
	//}

	/// <summary>
	///    executes and awaits sequentially
	/// </summary>
	public static async Task _ForEach<TValue>(this IEnumerable<TValue> enumerable, Func<TValue, ValueTask> asyncAction)
	{
		foreach (var val in enumerable)
		{
			await asyncAction(val);
		}
	}

	//public static bool _TryGet<TBase, TDerived>(this IEnumerable<TBase> enumerable, Func<TDerived, bool> predicate,
	//   [NotNullWhen(true)] out TDerived? value)
	//   where TDerived : TBase
	//{
	//   foreach (var val in enumerable)
	//   {
	//      if (val is TDerived v && predicate(v))
	//      {
	//         value = v;
	//         return true;
	//      }
	//   }

	//   value = default!;
	//   return false;
	//}
	public static bool _TryGet<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, [NotNullWhen(true)] out T? value)
	{
		foreach (var val in enumerable)
		{
			if (predicate(val))
			{
				value = val!;
				return true;
			}
		}

		value = default!;
		return false;
	}


	public static Dictionary<TKey, TValue> _ToDictionary<TKey, TValue>(
		this IEnumerable<KeyValuePair<TKey, TValue>> source, bool tryCloneValues = false) where TKey : notnull
	{
		if (tryCloneValues)
		{
			//try to clone all values
			var dict = new Dictionary<TKey, TValue>();
			foreach (var kvp in source)
			{
				var val = kvp.Value;
				val = val is ICloneable c ? (TValue)c.Clone() : val;
				dict.Add(kvp.Key, val);
			}

			return dict;
		}

		return source.ToDictionary(x => x.Key, x => x.Value);
	}

	public static string _ToStringAll<T>(this IEnumerable<T> source, string? seperator = ", ")
	{
		using var _ = __.pool.Rent<StringBuilder>(out var sb);
		__.GetLogger()._EzError(sb.Length == 0, "StringBuilder should be empty");

		var count = 0;
		foreach (var s in source)
		{
			count++;
			sb.Append(s);
			sb.Append(seperator);
		}

		sb.Append(']');

		var toReturn = $"count={count}[" + sb;
		sb.Clear();
		return toReturn;
	}


	public static string _Join(this IEnumerable<string> source, string? seperator = null)
	{
		return string.Join(seperator, source);
	}


	/// <summary>
	///    wrapper over normal `.Select()`, but will return an empty collection if the target source is null.
	/// </summary>
	public static IEnumerable<TResult> _Select<TSource, TResult>(
		this IEnumerable<TSource> source, Func<TSource, TResult> selector)
	{
		__.GetLogger()._EzError(source is not null);
		if (source is null)
		{
			return Array.Empty<TResult>();
		}

		return source.Select(selector);
	}

	/// <summary>
	///    obtain a random number of elements from the target collection.   will not return dupes.
	/// </summary>
	public static List<T> _TakeRandom<T>(this IEnumerable<T> collection, int count)
	{
		var rand = new Random();
		var temp = collection.ToList();

		if (count > temp.Count())
		{
			throw new ArgumentOutOfRangeException(
				$"collection count is {temp.Count()} but you are trying to take {count}.");
		}

		List<T> toReturn = new();
		for (var i = 0; i < count; i++)
		{
			var index = rand.Next(temp.Count);
			toReturn.Add(temp[index]);
			temp.RemoveAt(index);
		}

		return toReturn;
	}

	public static T _Sum<T>(this IEnumerable<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
	{
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			toReturn += val;
		}

		return toReturn;
	}

	public static T _Avg<T>(this IEnumerable<T> values)
		where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, float, T>
	{
		var count = 0;
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			count++;
			toReturn += val;
		}

		return toReturn / count;
	}

	public static T _Min<T>(this IEnumerable<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T, bool>
	{
		var toReturn = T.MaxValue;

		foreach (var val in values)
		{
			if (toReturn > val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}

	public static T _Max<T>(this IEnumerable<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T, bool>
	{
		var toReturn = T.MinValue;

		foreach (var val in values)
		{
			if (toReturn < val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}


	public static TResult _Max<TSource, TResult>(this IEnumerable<TSource> values, Func<TSource, TResult> selector, TResult defaultValue = default)
		where TResult : IMinMaxValue<TResult>, IComparisonOperators<TResult, TResult, bool>
	{

		int results = 0;
		var toReturn = TResult.MinValue;

		foreach (var val in values)
		{
			results++;
			var result = selector(val);
			if (toReturn < result)
			{
				toReturn = result;
			}
		}

		if (results == 0)
		{
			return defaultValue;
		}

		return toReturn;
	}
	public static TResult _Min<TSource, TResult>(this IEnumerable<TSource> values, Func<TSource, TResult> selector, TResult defaultValue = default)
		where TResult : IMinMaxValue<TResult>, IComparisonOperators<TResult, TResult, bool>
	{

		int results = 0;
		var toReturn = TResult.MaxValue;

		foreach (var val in values)
		{
			results++;
			var result = selector(val);
			if (toReturn > result)
			{
				toReturn = result;
			}
		}

		if (results == 0)
		{
			return defaultValue;
		}

		return toReturn;
	}
}

