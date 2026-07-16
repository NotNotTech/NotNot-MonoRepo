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

public static class zz_Extensions_Dictionary
{
	/// <summary>
	///    create a clone of this dictionary and attempts to clone all key/values
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="source"></param>
	/// <returns></returns>
	public static Dictionary<TKey, TValue> _Clone<TKey, TValue>(this Dictionary<TKey, TValue> source)
	{
		//try to clone all values
		var toReturn = new Dictionary<TKey, TValue>(source.Count());
		foreach (var kvp in source)
		{
			var value = kvp.Value;

			__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);

			var key = kvp.Key is ICloneable ck ? (TKey)ck.Clone() : kvp.Key;
			var val = value is ICloneable cv ? (TValue)cv.Clone() : value;
			toReturn.Add(key, val);
		}

		return toReturn;
	}

	public static RentedMem<(TKey key, TValue value)> _CopyToMem<TKey, TValue>(this Dictionary<TKey, TValue> source)
	{
		var toReturn = RentedMem<(TKey key, TValue value)>.Allocate(source.Count);
		var span = toReturn.GetSpan();
		var i = 0;
		foreach (var kvp in source)
		{
			span[i] = (kvp.Key, kvp.Value);
			i++;
		}
		return toReturn;
	}

	public static TDerived _Get<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key)
		where TDerived : TBase
		where TKey : notnull
	{
		return (TDerived)dict[key]!;
	}

	public static bool _TryGetValue<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key,
		out TDerived? value) where TDerived : TBase
		where TKey : notnull
	{
		if (dict.TryGetValue(key, out var baseValue))
		{
			value = (TDerived)baseValue;
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	/// get the value (if it exists) and remove it from the dictionary
	/// </summary>
	public static bool _Remove<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key,
		out TDerived? value) where TDerived : TBase
		where TKey : notnull
	{
		if (dict.Remove(key, out var baseValue))
		{
			value = (TDerived)baseValue;
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	///    returns string with count followed by contents in format:  "count=N [(key1,val1) (key2,val2) ]"
	/// </summary>
	/// <typeparam name="TKey"></typeparam>
	/// <typeparam name="TValue"></typeparam>
	/// <param name="dict"></param>
	/// <returns></returns>
	public static string _ToStringAll<TKey, TValue>(this IDictionary<TKey, TValue> dict)
	{
		//var sb = new StringBuilder();//  __.pool.Get<StringBuilder>();
		using var _ = __.pool.Rent<StringBuilder>(out var sb);
		sb.Append($"count={dict.Count} [");
		foreach (var pair in dict)
		{
			sb.Append($"({pair.Key},{pair.Value}) ");
		}

		sb.Append("]");

		var toReturn = sb.ToString();
		sb.Clear();
		return toReturn;
	}

	//  public static TValue _GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> onAddNew)
	//     where TKey : notnull
	//  {
	//     if (!dict.TryGetValue(key, out var value))
	//     {
	//        value = onAddNew();
	//        dict.Add(key, value);
	//     }
	//	return value;
	//}

	public static TDerived _GetOrAdd<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key, Func<TDerived> onAddNew) where TDerived : TBase
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = onAddNew();
			dict.Add(key, value);
		}

		return (TDerived)value!;
	}
	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TValue _GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new()
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TValue();
			dict.Add(key, value);
		}

		return ((TValue)value!);
	}

	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TDerived _GetOrNew<TKey, TBase, TDerived>(this IDictionary<TKey, TBase> dict, TKey key) where TDerived : TBase, new()
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TDerived();
			dict.Add(key, value);
		}

		return ((TDerived)value!);
	}
	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TDerived _GetOrNew<TDerived>(this IDictionary<string, object> dict, string key) where TDerived : new()
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TDerived();
			dict.Add(key, value);
		}

		return ((TDerived)value!);
	}

	/// <summary>
	/// like _GetOrAdd(), but will create a new value via the default ctor() if the key doesn't exist. 
	/// </summary>
	public static TDerived _GetOrNew<TDerived>(this IDictionary<object, object> dict, object key) where TDerived : new()
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = new TDerived();
			dict.Add(key, value);
		}

		return ((TDerived)value!);
	}

	/// <summary>
	/// if key doesn't exist, returns default of TDerived without adding to the dict.
	/// <para>default value is determined by the TDerived generic you pass in</para>
	/// </summary>
	public static TDerived _GetOrDefault<TDerived>(this IDictionary<string, object> dict, string key)
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = default(TDerived);
		}

		return ((TDerived)value!);
	}

	/// <summary>
	/// if key doesn't exist, returns default of TDerived without adding to the dict.
	/// <para>default value is determined by the TDerived generic you pass in</para>
	/// </summary>
	public static TDerived _GetOrDefault<TDerived>(this IDictionary<object, object> dict, object key)
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = default(TDerived);
		}
		return ((TDerived)value!);
	}
	/// <summary>
	/// if key doesn't exist, returns default of TDerived without adding to the dict.
	/// <para>default value is defined by the func you pass in</para>
	/// </summary>
	public static TDerived _GetOrDefault<TKey, TValue, TDerived>(this IDictionary<TKey, TValue> dict, TKey key, Func<TDerived> _default) where TDerived : TValue
		where TKey : notnull
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = _default();
		}

		return ((TDerived)value!);
	}

	public static bool _TryRemove<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, out TValue value)
		where TKey : notnull
	{
		var toReturn = dict.TryGetValue(key, out value);
		if (toReturn)
		{
			dict.Remove(key);
		}

		return toReturn;
	}

	/// <summary>
	///    get by reference!   ref returns allow efficient storage of structs in dictionaries
	///    These are UNSAFE in that further modifying (adding/removing) the dictionary while using the ref return will break
	///    things!
	/// </summary>
	public static ref TValue _GetValueRef_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key,
		out bool exists)
		where TKey : notnull
		where TValue : struct
	{
		ref var r_toReturn = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
		exists = Unsafe.IsNullRef(ref r_toReturn) == false;
		return ref r_toReturn;
	}

	/// <summary>
	///    get by reference!   ref returns allow efficient storage of structs in dictionaries
	///    These are UNSAFE in that further modifying (adding/removing) the dictionary while using the ref return will break
	///    things!
	/// </summary>
	public static ref TValue _GetValueRef_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
		where TKey : notnull
		where TValue : struct
	{
		return ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
	}
	//public static ref TValue _GetValueRefOrAddDefault_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, out bool exists)
	//	where TKey : notnull
	//	where TValue : struct
	//{

	//	ref var toReturn = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out var existCopy);
	//	exists = existCopy;
	//	return ref toReturn;
	//}

	public static ref TValue _GetValueRefOrAddDefault_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dictionary,
		TKey key, out bool exists)
		where TKey : notnull
		where TValue : struct
	{
		return ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, key, out exists);
	}

	public static unsafe ref TValue _GetValueRefOrAddDefault_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict,
		TKey key)
		where TKey : notnull
		where TValue : struct
	{
		bool exists;
		return ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out *&exists);
	}
	//	//below is bad pattern:  instead just set the ref returned value to the new.  (avoid struct copy)
	//	public static unsafe ref TValue _GetValueRefOrAdd_Unsafe<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func_Ref<TValue> onAddNew) 
	//		where TKey : notnull
	//		where TValue : struct
	//	{		
	//		bool exists;
	//		ref var toReturn = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out *&exists);
	//		if (exists != true)
	//		{
	//			ref var toAdd = ref onAddNew();
	//			dict.Add(key, toAdd);
	//#if DEBUG
	//			toReturn = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out *&exists);
	//			__.GetLogger()._EzError(exists);
	//#else
	//			toReturn = ref dict._GetValueRef_Unsafe(key);
	//#endif		
	//		}
	//		return ref toReturn;
	//	}
}

//    The included numeric extension methods utilize experimental CLR behavior to allow generic numerical operations.
//    Might work great, might have hidden perf costs?

