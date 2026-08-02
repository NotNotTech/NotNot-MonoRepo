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

public static class zz_Extensions_Span
{
	[ThreadStatic] private static Random _rand = new();

	public static bool _Overlaps<T>(this Span<T> span, HashSet<T> other)
	{
		foreach (var value in span)
		{
			if (other.Contains(value))
			{
				return true;
			}
		}
		return false;
	}

	public static Span<T> _Reverse<T>(this Span<T> span)
	{
		span.Reverse();
		return span;
	}
	public static List<T> _ToList<T>(this Span<T> span)
	{
		return span.ToArray().ToList();
	}


	public static TOut _Aggregate<TIn, TOut>(this Span<TIn> source, TOut seedValue,
		Func<TIn, TOut, TOut> handler)
	{
		return source._AsReadOnly()._Aggregate(seedValue, handler);
	}

	public static TOut _Aggregate<TIn, TOut>(this ReadOnlySpan<TIn> source, TOut seedValue,
		Func<TIn, TOut, TOut> accumFunc)
	{
		var accumulation = seedValue;
		foreach (var value in source)
		{
			accumulation = accumFunc(value, accumulation);
		}

		return accumulation;
	}


	/// <summary>
	/// like ._Randomize() but the swaps are deterministic psudorandom.
	/// <para>useful for rearanging values while still having a deterministic order</para>
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="target"></param>
	public static void _Swizzle<T>(this Span<T> target)//, long? primeToUse=null)
	{
		var length = target.Length;
		if (length <= 1) return;
		int prime = 3; //primeToUse ?? LoLo.PrimeFinder.FindLargestPrimeLessThan(length-1); //(long)Math.Pow(2 , 31) - 1;


		for (var index = 0; index < length; index++)
		{
			int swapIndex = (index + 1) * prime % length;// Deterministic swapping pattern
			(target[index], target[swapIndex]) = (target[swapIndex], target[index]);
		}

		//var index = 0;
		//for (var i = 0; i < length; i++)
		//{
		//   int swapIndex = (int)(((index +1) * prime) % length);// Deterministic swapping pattern
		//   (target[index], target[swapIndex]) = (target[swapIndex], target[index]);
		//   index = (swapIndex+i)%length;
		//}
	}

	//public static int[] _SwizzleTest(this int[] values)
	//{
	//   var prime = 2 ^ 31 - 1;
	//   var length = values.Length;
	//   //size in bits
	//   values = values._Clone().ToArray();
	//   var targetIndex = 0;
	//   for (int i = 0; i < length; i++)
	//   {
	//      var swapIndex = (((i + 1) * prime)) % length; // Deterministic swapping pattern

	//      (values[i], values[swapIndex]) = (values[swapIndex], values[i]);

	//      //// Swapping bits at i and swapIndex if they are different
	//      //if (((value >> i) & 1) != ((value >> swapIndex) & 1))
	//      //{
	//      //   // Toggle bits at i and swapIndex
	//      //   value ^= (1 << i) | (1 << swapIndex);
	//      //}

	//      targetIndex++;
	//   }
	//   return values;
	//}


	public static void _Randomize<T>(this Span<T> target)
	{
		if (target.Length <= 1) return;
		//lock (_rand)
		{
			for (var index = 0; index < target.Length; index++)
			{
				var swapIndex = _rand.Next(0, target.Length);
				(target[index], target[swapIndex]) = (target[swapIndex], target[index]);
			}
		}
	}

	public static bool _IsSorted<T>(this Span<T> target) where T : IComparable<T>
	{
		if (target.Length < 2)
		{
			return true;
		}

		ref var r_previous = ref target[0]!;
		for (var i = 1; i < target.Length; i++)
		{
			if (r_previous.CompareTo(target[i]) > 0) //ex: 1.CompareTo(2) == -1
			{
				return false;
			}

			r_previous = ref target[i]!;
		}

		return true;


		//using var temp= SpanGuard<T>.Allocate(target.Length);
		//var tempSpan = temp.Span;
		//tempSpan.Sort(target, (first, second) => {
		//	var result = first.CompareTo(second);
		//	if(result < 0)
		//	{
		//		isSorted = false;
		//	}
		//	return result;
		//});
		//return isSorted;		
	}

	public static bool _IsSorted<T>(this Span<T> target, Func_RefArg<T, T, int> compare)
	{
		if (target.Length < 2)
		{
			return true;
		}

		ref var r_previous = ref target[0]!;
		for (var i = 1; i < target.Length; i++)
		{
			if (compare(ref r_previous, ref target[i]) > 0) //ex: 1.CompareTo(2) == -1
			{
				return false;
			}

			r_previous = ref target[i]!;
		}

		return true;


		//using var temp= SpanGuard<T>.Allocate(target.Length);
		//var tempSpan = temp.Span;
		//tempSpan.Sort(target, (first, second) => {
		//	var result = first.CompareTo(second);
		//	if(result < 0)
		//	{
		//		isSorted = false;
		//	}
		//	return result;
		//});
		//return isSorted;		
	}

	/// <summary>
	///    returns true if both spans starting address in memory is the same.  Different length and/or type is ignored.
	/// </summary>
	public static unsafe bool _ReferenceEquals<T1, T2>(ref this Span<T1> target, ref Span<T2> other)
		where T1 : unmanaged where T2 : unmanaged
	{
		fixed (T1* pSpan1 = target)
		{
			fixed (T2* pSpan2 = other)
			{
				return pSpan1 == pSpan2;
			}
		}
	}

	public static Span<int> _AsInts(ref this Span<byte> target)
	{
		return MemoryMarshal.Cast<byte, int>(target);
	}
	public static Span<float> _AsFloats(ref this Span<byte> target)
	{
		return MemoryMarshal.Cast<byte, float>(target);
	}
	public static Span<long> _AsLongs(ref this Span<byte> target)
	{
		return MemoryMarshal.Cast<byte, long>(target);
	}
	public static Span<ulong> _AsULongs(this Span<byte> target)
	{
		return MemoryMarshal.Cast<byte, ulong>(target);
	}
	public static Span<double> _AsDoubles(ref this Span<byte> target)
	{
		return MemoryMarshal.Cast<byte, double>(target);
	}

	/// <summary>
	///    cast this span as another.  Any extra bytes remaining are ignored (the number of bytes in the castTo may be smaller
	///    than the original)
	/// </summary>
	public static Span<TTo> _CastAs<TFrom, TTo>(ref this Span<TFrom> target)
		where TFrom : unmanaged where TTo : unmanaged
	{
		return MemoryMarshal.Cast<TFrom, TTo>(target);
	}


	[MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
	public static ReadOnlySpan<T> _AsReadOnly<T>(this Span<T> span)
	{
		return span;
	}


	/// <summary>
	///    important implementation notes, be sure to read
	///    https://docs.microsoft.com/en-us/windows/communitytoolkit/high-performance/parallelhelper
	/// </summary>
	/// <typeparam name="TData"></typeparam>
	private readonly unsafe struct _ParallelForEach_ActionHelper<TData> : IAction where TData : unmanaged
	{
		public readonly TData* pSpan;
		public readonly Action_Ref<TData, int> parallelAction;

		public _ParallelForEach_ActionHelper(TData* pSpan, Action_Ref<TData, int> parallelAction)
		{
			this.pSpan = pSpan;
			this.parallelAction = parallelAction;
		}

		public void Invoke(int index)
		{
			//Using delegate pointer invoke, Because action is a readonly field,
			//but Invoke is an interface method where the compiler can't see it's actually readonly in all implementing types,
			//so it emits a defensive copies. This skips that 
			Unsafe.AsRef(in parallelAction).Invoke(ref pSpan[index], ref index);
		}
	}

	private readonly unsafe struct _ParallelForEach_ActionHelper_OutputSpan<TData, TOutput> : IAction
		where TData : unmanaged where TOutput : unmanaged
	{
		public readonly TData* pSpan;
		public readonly TOutput* pOutput;
		public readonly Action_Ref<TData, TOutput, int> parallelAction;

		public _ParallelForEach_ActionHelper_OutputSpan(TData* pSpan, TOutput* pOutput,
			Action_Ref<TData, TOutput, int> parallelAction)
		{
			this.pSpan = pSpan;
			this.pOutput = pOutput;
			this.parallelAction = parallelAction;
		}

		public void Invoke(int index)
		{
			//Using delegate pointer invoke, Because action is a readonly field,
			//but Invoke is an interface method where the compiler can't see it's actually readonly in all implementing types,
			//so it emits a defensive copies. This skips that 
			Unsafe.AsRef(in parallelAction).Invoke(ref pSpan[index], ref pOutput[index], ref index);
		}
	}

	private readonly unsafe struct _ParallelForEach_ActionHelper_FunctionPtr<TData> : IAction where TData : unmanaged
	{
		public readonly TData* pSpan;
		public readonly delegate*<ref TData, ref int, void> parallelAction;

		public _ParallelForEach_ActionHelper_FunctionPtr(TData* pSpan, delegate*<ref TData, ref int, void> parallelAction)
		{
			this.pSpan = pSpan;
			this.parallelAction = parallelAction;
		}

		public void Invoke(int index)
		{
			//Using delegate pointer invoke, Because action is a readonly field,
			//but Invoke is an interface method where the compiler can't see it's actually readonly in all implementing types,
			//so it emits a defensive copies. This skips that 
			//Unsafe.AsRef(parallelAction).Invoke(ref pSpan[index], ref index);
			parallelAction(ref pSpan[index], ref index);
		}
	}

	public static unsafe void _ParallelForEach<TData>(this Span<TData> inputSpan, Action_Ref<TData, int> parallelAction)
		where TData : unmanaged
	{
		fixed (TData* pSpan = inputSpan)
		{
			var actionStruct = new _ParallelForEach_ActionHelper<TData>(pSpan, parallelAction);
			ParallelHelper.For(0, inputSpan.Length, in actionStruct);
		}
	}

	public static unsafe void _ParallelForEach<TData>(this Span<TData> inputSpan,
		delegate*<ref TData, ref int, void> parallelAction) where TData : unmanaged
	{
		fixed (TData* pSpan = inputSpan)
		{
			var actionStruct = new _ParallelForEach_ActionHelper_FunctionPtr<TData>(pSpan, parallelAction);
			ParallelHelper.For(0, inputSpan.Length, in actionStruct);
		}
	}

	public static unsafe void _ParallelForEach<TData, TOutput>(this Span<TData> inputSpan, Span<TOutput> outputSpan,
		Action_Ref<TData, TOutput, int> parallelAction) where TData : unmanaged where TOutput : unmanaged
	{
		fixed (TData* pSpan = inputSpan)
		{
			fixed (TOutput* pOutput = outputSpan)
			{
				var actionStruct =
					new _ParallelForEach_ActionHelper_OutputSpan<TData, TOutput>(pSpan, pOutput, parallelAction);
				ParallelHelper.For(0, inputSpan.Length, in actionStruct);
			}
		}
	}

	///////// <summary>
	///////// do work in parallel over the span.  each parallelAction will operate over a segment of the span
	///////// </summary>
	//////public static unsafe void _ParallelFor<TData>(this Span<TData> inputSpan, int parallelCount, Action_Span<TData> parallelAction) where TData : unmanaged
	//////{
	//////	var length = inputSpan.Length;
	//////	fixed (TData* p = inputSpan)
	//////	{
	//////		var pSpan = p; //need to stop compiler complaint

	//////		Parallel.For(0, parallelCount + 1, (index) => { //plus one to capture remainder

	//////			var count = length / parallelCount;
	//////			var startIndex = index * count;
	//////			var endIndex = startIndex + count;
	//////			if (endIndex > length)
	//////			{
	//////				endIndex = length;
	//////				count = endIndex - startIndex; //on last loop, only do remainder
	//////			}

	//////			var spanPart = new Span<TData>(&pSpan[startIndex], count);

	//////			parallelAction(spanPart);

	//////		});
	//////	}
	//////}
	///////// <summary>
	///////// do work in parallel over the span.  each parallelAction will operate over a segment of the span
	///////// </summary>
	//////public static unsafe void _ParallelForRange<TData>(this ReadOnlySpan<TData> inputSpan, int parallelCount, Action_RoSpan<TData> parallelAction) where TData : unmanaged
	//////{

	//////	var partition = System.Collections.Concurrent.Partitioner.Create(0, inputSpan.Length);

	//////	inputSpan.s


	//////	__.GetLogger()._EzError(false, "needs verification of algo.  probably doesn't partition properly");
	//////	var length = inputSpan.Length;
	//////	fixed (TData* p = inputSpan)
	//////	{
	//////		var pSpan = p;

	//////		Parallel.For(0, parallelCount + 1, (index) => { //plus one to capture remainder

	//////			var count = length / parallelCount;
	//////			var startIndex = index * count;
	//////			var endIndex = startIndex + count;
	//////			if (endIndex > length)
	//////			{
	//////				endIndex = length;
	//////				count = endIndex - startIndex; //on last loop, only do remainder
	//////			}

	//////			var spanPart = new ReadOnlySpan<TData>(&pSpan[startIndex], count);

	//////			parallelAction(spanPart);

	//////		});
	//////	}
	//////}

	///// <summary>
	///// get ref to item at index 0
	///// </summary>
	//public static ref T _GetRef<T>(this Span<T> span)
	//{		
	//	return System.Runtime.InteropServices.MemoryMarshal.GetReference(span);
	//}

#if GENERIC_MATH
	//// GENERIC MATH  requires System.Runtime.Experimental nuget package matching the current DotNet runtime.
	public static T _Sum<T>(this Span<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
	{
		return values._AsReadOnly()._Sum();
	}
	public static T _Sum<T>(this ReadOnlySpan<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>
	{
		var toReturn = T.AdditiveIdentity;
		foreach (var val in values)
		{
			toReturn += val;
		}
		return toReturn;
	}

	public static T _Avg<T>(this Span<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, float, T>
	{
		return values._AsReadOnly()._Avg();
	}
	public static T _Avg<T>(this ReadOnlySpan<T> values) where T : IAdditionOperators<T, T, T>, IAdditiveIdentity<T, T>, IDivisionOperators<T, float, T>
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
	public static T _Min<T>(this Span<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
	{
		return values._AsReadOnly()._Min();
	}
	public static T _Min<T>(this ReadOnlySpan<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
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
	public static T _Max<T>(this Span<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
	{
		return values._AsReadOnly()._Max();
	}
	public static T _Max<T>(this ReadOnlySpan<T> values) where T : IMinMaxValue<T>, IComparisonOperators<T, T>
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
	//MISSING GENERIC MATH  requires System.Runtime.Experimental nuget package matching the current DotNet runtime.
#endif
	public static float _Sum(this Span<float> values)
	{
		return values._AsReadOnly()._Sum();
	}

	public static float _Sum(this ReadOnlySpan<float> values)
	{
		float toReturn = 0;
		foreach (var val in values)
		{
			toReturn += val;
		}

		return toReturn;
	}

	public static TimeSpan _Sum(this Span<TimeSpan> values)
	{
		return values._AsReadOnly()._Sum();
	}

	public static TimeSpan _Sum(this ReadOnlySpan<TimeSpan> values)
	{
		TimeSpan toReturn = TimeSpan.Zero;
		foreach (var val in values)
		{
			toReturn += val;
		}

		return toReturn;
	}

	public static float _Avg(this Span<float> values)
	{
		return values._AsReadOnly()._Avg();
	}

	public static float _Avg(this ReadOnlySpan<float> values)
	{
		var count = 0;
		var toReturn = 0f;
		foreach (var val in values)
		{
			count++;
			toReturn += val;
		}

		return toReturn / count;
	}

	public static float _Min(this Span<float> values)
	{
		return values._AsReadOnly()._Min();
	}

	public static float _Min(this ReadOnlySpan<float> values)
	{
		var toReturn = float.MaxValue;

		foreach (var val in values)
		{
			if (toReturn > val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}

	public static float _Max(this Span<float> values)
	{
		return values._AsReadOnly()._Max();
	}

	public static float _Max(this ReadOnlySpan<float> values)
	{
		var toReturn = float.MinValue;

		foreach (var val in values)
		{
			if (toReturn < val)
			{
				toReturn = val;
			}
		}

		return toReturn;
	}

	public static bool _Contains<T>(this Span<T> values, T toFind) where T : class
	{
		foreach (var val in values)
		{
			if (val == toFind)
			{
				return true;
			}
		}

		return false;
	}

	public static bool _Contains<T>(this ReadOnlySpan<T> values, T toFind) where T : class
	{
		foreach (var val in values)
		{
			if (val == toFind)
			{
				return true;
			}
		}

		return false;
	}
}

