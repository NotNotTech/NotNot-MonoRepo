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

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_Array
{
	//public static bool Contains<TValue>(this TValue[] array, TValue value) where TValue : IEquatable<TValue>
	//{
	//   foreach (var item in array)
	//   {
	//      if (value.Equals(item))
	//      {
	//         return true;
	//      }
	//   }
	//   return false;
	//}

	//public static Span<T> _Reverse<T>(this T[] array)
	//{
	//	array.Reverse();
	//	return array;
	//}


	/// <summary>
	///    Find the first occurence of an byte[] in another byte[]
	/// </summary>
	/// <param name="toSearchInside">the byte[] to search in</param>
	/// <param name="toFind">the byte[] to find</param>
	/// <returns>the first position of the found byte[] or -1 if not found</returns>
	/// <remarks>
	///    Contributed by blaumeister, http://www.codeplex.com/site/users/view/blaumeiser
	/// </remarks>
	public static int _FindArrayInArray<T>(this T[] toSearchInside, T[] toFind)
	{
		int i, j;
		for (j = 0; j < toSearchInside.Length - toFind.Length; j++)
		{
			for (i = 0; i < toFind.Length; i++)
				if (!Equals(toSearchInside[j + i], toFind[i]))
				{
					break;
				}

			if (i == toFind.Length)
			{
				return j;
			}
		}

		return -1;
	}


	public static void _Fill<T>(this T[] array, T value)
	{
		array._Fill(value, 0, array.Length);
	}

	public static void _Fill<T>(this T[] array, T value, int index, int count)
	{
		for (int i = index; i < index + count; i++)
			array[i] = value;
	}

	/// <summary>
	///    creates a new array with the values from this and <see cref="other" />  (joins the two arrays together)
	///    <para>note: this is a simple copy, it does not skip empty elements, etc</para>
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="array"></param>
	/// <param name="other"></param>
	/// <returns></returns>
	public static T[] _Join<T>(this T[] array, params T[] other)
	{
		T[] toReturn = new T[array.Length + other.Length];
		Array.Copy(array, toReturn, array.Length);
		Array.Copy(other, 0, toReturn, array.Length, other.Length);
		return toReturn;
	}


	public static void _CopyTo<T>(this T[] array, T[] other)
	{
		__.GetLogger()._EzError(array.Length == other.Length);
		Array.Copy(array, other, array.Length);
	}

	public static T[] _Copy<T>(this T[] array, int index = 0, int? count = null)
	{
		if (!count.HasValue)
		{
			count = array.Length - index;
		}

		var toReturn = new T[count.Value];
		Array.Copy(array, index, toReturn, 0, count.Value);
		return toReturn;
	}

	/// <summary>
	///    quickly clears an array
	/// </summary>
	/// <param name="?"></param>
	public static void _Clear(this Array array)
	{
		array._Clear(0, array.Length);
	}

	public static Span<T> _AsSpan<T>(this T[] array)
	{
		return new Span<T>(array);
	}
	public static Span2D<T> _AsSpan<T>(this T[,] array)
	{
		return new Span2D<T>(array);
	}

	public static void _Clear(this Array array, int offset, int count)
	{
		Array.Clear(array, offset, count);
	}


	/// <summary>
	///    invokes .Clone on all elements, only works when items are cloneable and class
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="array"></param>
	/// <returns></returns>
	public static T[] _Clone<T>(this T[] array)
		where T : ICloneable
	{
		var toReturn = new T[array.Length];
		for (int i = 0; i < array.Length; i++)
			//__.GetLogger()._EzError(value is ICloneable || RuntimeHelpers.IsReferenceOrContainsReferences<TValue>() is false);
			//if (array[i] != null)
			toReturn[i] = (T)array[i].Clone();
		//__.GetLogger()._EzError(toReturn[i] != null);
		return toReturn;
	}

	///// <summary>
	///// 
	///// </summary>
	///// <typeparam name="T"></typeparam>
	///// <param name="array"></param>
	///// <param name="task">args = <see cref="array"/>, startInclusive, endExclusive</param>
	///// <param name="offset"></param>
	///// <param name="count"></param>
	//public static void _ParallelFor<T>(this T[] array, Action<T[], int, int> task, int offset = 0, int count = -1)
	//{
	//	count = count == -1 ? array.Length - offset : count;
	//	StormPool.instance.ParallelFor(array, offset, count, task);
	//}
}

