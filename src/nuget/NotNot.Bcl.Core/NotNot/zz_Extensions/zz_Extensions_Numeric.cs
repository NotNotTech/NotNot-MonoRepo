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

public static class zz_Extensions_Numeric
{
	//public static T _Max<T>(this T value, T other) where T : IComparable<T>
	//{
	//	return value.CompareTo(other) >= 0 ? value : other;

	//}
	//public static T _Min<T>(this T value, T other) where T : IComparable<T>
	//{
	//	return value.CompareTo(other) <= 0 ? value : other;

	//}


	public static bool _Between<T>(this T value, T lowerInclusive, T upperExclusive) where T : INumber<T>
	{
		return value >= lowerInclusive && value < upperExclusive;
	}
	public static bool _AproxEqual<T>(this T value, T other, T tolerance) where T : IFloatingPoint<T>
	{
		// Handle NaN (Not a Number)
		if (T.IsNaN(value) || T.IsNaN(other))
		{
			return false; // NaN is not equal to anything, including itself
		}

		// Handle infinity (both positive and negative)
		if (T.IsInfinity(value) || T.IsInfinity(other))
		{
			return value == other; // Only return true if both are exactly the same infinity
		}
		return T.Abs(value - other) <= tolerance;
	}
	public static bool _AproxEqual(this double value, double other)
	{
		var tolerance = Math.Max(Math.Max(Math.Abs(value), Math.Abs(other)), 1d) * double.Epsilon;
		return value._AproxEqual(other, tolerance);
	}
	public static bool _AproxEqual(this float value, float other)
	{
		var tolerance = Math.Max(Math.Max(Math.Abs(value), Math.Abs(other)), 1f) * float.Epsilon;
		return value._AproxEqual(other, tolerance);
	}
	public static T _Round<T>(this T value, int digits, MidpointRounding mode = MidpointRounding.AwayFromZero)
		where T : IFloatingPoint<T>
	{
		return T.Round(value, digits, mode);
	}

	/// <summary>
	/// Extension method to round the float to the nearest specified increment
	/// </summary>
	public static T _RoundToNearest<T>(this T number, T increment)
		where T : IFloatingPoint<T>
	{
		if (increment == T.Zero)
		{
			return T.Round(number);
		}

		T multiplier = T.One / increment;
		return T.Round(number * multiplier) / multiplier;
	}

	public static T _Abs<T>(this T value)
		where T : IFloatingPoint<T>
	{
		return T.Abs(value);
	}
	public static int _Sign<T>(this T value)
		where T : IFloatingPoint<T>
	{

		return T.Sign(value);
	}
	public static T _CopySign<T>(this T value, T other)
		where T : IFloatingPoint<T>
	{

		return T.CopySign(value, other);
	}

	public static T _Ceiling<T>(this T value)
		where T : IFloatingPoint<T>
	{

		return T.Ceiling(value);
	}
	public static T _Floor<T>(this T value)
		where T : IFloatingPoint<T>
	{


		return T.Floor(value);
	}
	public static T _Clamp<T>(this T value, T min, T max)
		where T : IFloatingPoint<T>
	{


		return T.Clamp(value, min, max);
	}
	public static T _Max<T>(this T value, T other) where T : IFloatingPoint<T>
	{
		return T.Max(value, other);

	}
	public static T _Min<T>(this T value, T other) where T : IFloatingPoint<T>
	{
		return T.Min(value, other);
	}

	public static bool _TryParse<T>(this string toParse, out T value) where T : IFloatingPoint<T>
	{
		return T.TryParse(toParse, null, out value);

	}
	public static T _Truncate<T>(this T value) where T : IFloatingPoint<T>
	{
		return T.Truncate(value);
	}

	public static float _AsFloat<T>(this T value) where T : INumberBase<T>
	{
		return float.CreateChecked(value);
		//// Check if T is a floating-point type and convert accordingly
		//if (value is float f)
		//{
		//   return f;
		//}
		//else if (value is double d)
		//{
		//   return (float)d;
		//}
		//else if (value is decimal m)
		//{
		//   return (float)m;
		//}
		//else
		//{
		//   throw new InvalidOperationException("Unsupported floating-point type.");
		//}
	}

	/// <summary>
	/// Converts the specified numeric value to a 32-bit signed integer using checked conversion.
	/// </summary>
	/// <remarks>If the value is outside the range of Int32, an exception will be thrown. This method uses checked
	/// conversion to ensure that overflows are detected.</remarks>
	/// <typeparam name="T">The numeric type of the value to convert. Must implement the INumber<T> interface.</typeparam>
	/// <param name="value">The numeric value to convert to an integer.</param>
	/// <returns>A 32-bit signed integer representation of the specified value.</returns>
	public static int _AsInt<T>(this T value) where T : INumber<T>
	{
		return int.CreateChecked(value);
	}

	public static T _SubtractTowardsZero<T>(this T value, T amount) where T : INumber<T>
	{
		amount = T.Abs(amount);
		//if (Y < 0)
		//{
		//	throw new ArgumentException("Y must be positive", nameof(Y));
		//}

		if (value > T.Zero)
		{
			return T.Max(T.Zero, value - amount);
		}
		else if (value < T.Zero)
		{
			return T.Min(T.Zero, value + amount);
		}
		else
		{
			return T.Zero;
		}
	}
}

