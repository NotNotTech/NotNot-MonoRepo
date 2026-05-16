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

public static class zz_Extensions_TimeSpan
{
	public static int _ToFps(this TimeSpan timeSpan)
	{
		var ms = timeSpan.TotalMilliseconds;
		return (int)(1000 / ms);
	}
	//public static TimeSpan Multiply(this TimeSpan timeSpan, double number)
	//{
	//	return TimeSpan.FromTicks((long)(timeSpan.Ticks * number));
	//}

	//public static TimeSpan Divide(this TimeSpan timeSpan, double number)
	//{
	//	return TimeSpan.FromTicks((long)(timeSpan.Ticks / number));
	//}
	///// <summary>
	///// 
	///// </summary>
	///// <param name="timeSpan"></param>
	///// <param name="other"></param>
	///// <returns>ratio</returns>
	//public static double Divide(this TimeSpan timeSpan, TimeSpan other)
	//{
	//	return timeSpan.Ticks / (double)other.Ticks;
	//}

	/// <summary>
	/// Returns the minimum value between the current TimeSpan and another TimeSpan.
	/// </summary>
	/// <param name="_this">The current TimeSpan instance</param>
	/// <param name="other">The TimeSpan to compare with</param>
	/// <returns>The minimum TimeSpan value</returns>
	public static TimeSpan _Min(this TimeSpan _this, TimeSpan other)
	{
		// Return the smaller TimeSpan using the conditional operator
		return _this < other ? _this : other;
	}


	public static TimeSpan _Max(this TimeSpan _this, TimeSpan other)
	{
		// Return the larger TimeSpan using the conditional operator
		return _this > other ? _this : other;
	}

	/// <summary>
	/// calculate mod, ie:  _this % other
	/// </summary>
	public static TimeSpan _Mod(this TimeSpan _this, TimeSpan other)
	{
		var thisTicks = _this.Ticks;
		var otherTicks = other.Ticks;

		var result = thisTicks % otherTicks;

		return TimeSpan.FromTicks(result);
	}


	/// <summary>
	///    given an interval, find the previous occurance of that interval's multiple. (prior to this timespan).
	///    <para>If This timespan is precisely a multiple of interval, itself will be returned.</para>
	/// </summary>
	public static TimeSpan _IntervalPrior(this TimeSpan target, TimeSpan interval)
	{
		var remainder = target.Ticks % interval.Ticks;
		return TimeSpan.FromTicks(target.Ticks - remainder);
	}

	/// <summary>
	///    given an interval, find the next occurance of that interval's multiple.
	/// </summary>
	public static TimeSpan _IntervalNext(this TimeSpan target, TimeSpan interval)
	{
		return target._IntervalPrior(interval) + interval;
	}

	/// <summary>
	/// use your timespan as an accumulator (added to outside of this method).  will reset the accumulator and return true when the accumulator exceeds the interval.
	/// pass keepRemainderOnInterval=true to keep any extra time beyond the interval in the accumulator.
	/// </summary>
	/// <returns></returns>
	public static bool _RefIntervalLoopTimer(this ref TimeSpan accumulator, TimeSpan interval, bool keepRemainderOnInterval = false)
	{
		if (accumulator >= interval)
		{
			if (keepRemainderOnInterval)
			{
				accumulator -= interval;
			}
			else
			{
				accumulator = TimeSpan.Zero;
			}
			return true;
		}
		return false;
	}


	private static Random _random = new();

	/// <summary>
	///    implementation of exponential backoff waiting
	/// </summary>
	/// <param name="initialValue">value of 0 is ok, next value will be at least 1</param>
	/// <param name="limit">the maximum time, excluding any random buffering via the <see cref="omitRandomPadding" /> variable</param>
	/// <param name="multiplier">default is 2.  exponent used as y variable in power function</param>
	/// <param name="omitRandomPadding">default false (random padding applied — up to 1 second added to aid server load balancing). set true to disable padding for deterministic timing.</param>
	/// <returns></returns>
	public static TimeSpan _ExponentialBackoff(this TimeSpan initialValue, TimeSpan limit, double multiplier = 2,
		bool omitRandomPadding = false)
	{
		__.GetLogger()._EzError(initialValue >= TimeSpan.Zero && limit >= TimeSpan.Zero, "input must not be Timespan.Zero");


		var backoff = initialValue.Multiply(multiplier);
		backoff = backoff > limit ? limit : backoff;

		if (!omitRandomPadding)
		{
			backoff += TimeSpan.FromSeconds(_random.NextDouble());
		}

		return backoff;

		//shitty non-working way
		//var limitTicks = limit.Ticks;
		//var ticks = Math.Pow(initialValue.Ticks, exponent);
		//ticks = ticks > limitTicks ? limitTicks : ticks;

		//var toReturn = TimeSpan.FromTicks((long)ticks);
		//if (toReturn == TimeSpan.Zero)
		//{
		//	toReturn = TimeSpan.FromSeconds(1);
		//}
		//else if (toReturn <= TimeSpan.FromSeconds(1))
		//{
		//	toReturn = TimeSpan.FromSeconds(2);
		//}

		//if (randomPadding)
		//{
		//	double randomPercent;
		//	lock (_random)
		//	{
		//		randomPercent = _random.NextDouble();
		//	}
		//	return toReturn + TimeSpan.FromSeconds(randomPercent);
		//}
		//else
		//{
		//	return toReturn;
		//}
	}

	/// <summary>
	/// Formats a TimeSpan as a human-readable string using the single most significant unit,
	/// rounded down (truncated). Output units: y, d, h, m, s.
	/// <para>Examples: "2y", "14d", "3h", "45m", "8s", "0s" (for sub-second or zero).</para>
	/// <para>Negative timespans return the absolute value prefixed with "-" (e.g., "-3m").</para>
	/// </summary>
	public static string _ToStringSignificant(this TimeSpan timeSpan)
	{
		if (timeSpan < TimeSpan.Zero)
			return "-" + (-timeSpan)._ToStringSignificant();

		if (timeSpan.TotalDays >= 365)
			return $"{(int)(timeSpan.TotalDays / 365)}y";
		if (timeSpan.TotalDays >= 1)
			return $"{(int)timeSpan.TotalDays}d";
		if (timeSpan.TotalHours >= 1)
			return $"{(int)timeSpan.TotalHours}h";
		if (timeSpan.TotalMinutes >= 1)
			return $"{(int)timeSpan.TotalMinutes}m";
		return $"{(int)timeSpan.TotalSeconds}s";
	}
}

