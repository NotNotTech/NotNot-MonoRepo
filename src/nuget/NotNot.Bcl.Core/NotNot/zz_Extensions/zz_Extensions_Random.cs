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

public static class zz_Extensions_Random
{
	public static TimeSpan _NextTimeSpan(this Random rand, double maxSeconds)
	{
		return rand._NextTimeSpan(0, maxSeconds);
	}

	public static TimeSpan _NextTimeSpan(this Random rand, double minSeconds, double maxSeconds)
	{
		var seconds = rand.NextDouble() * (maxSeconds - minSeconds) + minSeconds;
		var toReturn = TimeSpan.FromSeconds(seconds);
		return toReturn;
	}

	/// <summary>
	///    Return a vector with each component ranging from 0f to 1f
	/// </summary>
	/// <param name="random"></param>
	/// <returns></returns>
	public static Vector3 _NextVector3(this Random random)
	{
		//return (float)random.NextDouble();
		return new Vector3 { X = random.NextSingle(), Y = random.NextSingle(), Z = random.NextSingle(), };
	}


	/// <summary>
	///    return boolean true or false
	/// </summary>
	/// <returns></returns>
	public static bool _NextBoolean(this Random random)
	{
		return random.Next(2) == 1;
	}
	public static float _Next(this Random random, float minInclusive, float maxExclusive)
	{
		if (minInclusive >= maxExclusive)
			throw new ArgumentException("minInclusive must be less than maxExclusive");

		double range = (double)maxExclusive - (double)minInclusive;
		double sample = random.NextDouble();
		double scaled = (sample * range) + minInclusive;
		return (float)scaled;
	}

	public static TEnum _NextEnum<TEnum>(this Random random) where TEnum : Enum
	{
		var values = Enum.GetValues(typeof(TEnum));
		var index = random.Next(0, values.Length);
		return (TEnum)values.GetValue(index)!;
	}

	/// <summary>
	///    return a printable unicode character (letters, numbers, symbols, whiteSpace)
	///    <para>note: this includes whiteSpace</para>
	/// </summary>
	/// <param name="random"></param>
	/// <returns></returns>
	public static char _NextChar(this Random random, bool symbolsOrWhitespace = false, bool unicodeOkay = false)
	{
		if (unicodeOkay)
		{
			while (true)
			{
				var c = (char)random.Next(0, ushort.MaxValue);
				if (symbolsOrWhitespace)
				{
					if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
					{
						return c;
					}
				}
				else
				{
					if (char.IsLetterOrDigit(c))
					{
						return c;
					}
				}
			}
		}

		//ascii only
		while (true)
		{
			var c = (char)random.Next(0, 127);
			if (symbolsOrWhitespace)
			{
				if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
				{
					return c;
				}
			}
			else
			{
				if (char.IsLetterOrDigit(c))
				{
					return c;
				}
			}
		}
	}

	/// <summary>
	///    return a printable unicode string (letters, numbers, symbols, whiteSpace)
	///    <para>note: this includes whiteSpace</para>
	/// </summary>
	/// <param name="random"></param>
	/// <returns></returns>
	public static string _NextString(this Random random, int length, bool symbolsOrWhitespace = false,
		bool unicodeOkay = false)
	{
		StringBuilder sb = new(length);
		for (int i = 0; i < length; i++)
			sb.Append(random._NextChar(unicodeOkay, symbolsOrWhitespace));

		return sb.ToString();
	}


	//[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
	//static zz_Extensions_Random()
	//{
	//	List<char> valid = new List<char>();
	//	for (int i = 0; i < ushort.MaxValue; i++)
	//	{
	//		var c = Convert.ToChar(i);
	//		if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
	//		{
	//			valid.Add(c);
	//			if (c < 127)
	//			{
	//				//__.GetLogger()._EzError(c >= 0, "expect char to be unsigned");
	//				lowerAsciiLimitIndexExclusive = valid.Count;
	//			}
	//		}
	//	}
	//	var span = new Span<char>(valid.ToArray());
	//	printableUnicode = span.ToString();

	//	//printableUnicode = valid.ToArray();
	//}
	///// <summary>
	///// the exclusive bound of the lower ascii set in our <see cref="printableUnicode"/> characters array
	///// </summary>
	//static int lowerAsciiLimitIndexExclusive;
	///// <summary>
	///// a sorted array of all unicode characters that meet the following criteria:
	///// <para>
	///// (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
	///// </para>
	///// </summary>
	//static string printableUnicode;

	/// <summary>Roll</summary>
	/// <param name="diceNotation">string to be evaluated</param>
	/// <returns>result of evaluated string</returns>
	/// <remarks>
	///    <para>
	///       source taken from http://stackoverflow.com/questions/1031466/evaluate-dice-rolling-notation-strings and
	///       reformatted for greater readability
	///    </para>
	/// </remarks>
	public static int _NextDice(this Random rand, string diceNotation)
	{
		//ToDo.Anyone("improve performance of this dice parser. also add zero bias and open ended notation.  and consider other factors like conditional expressions");

		__.GetLogger()._EzError(diceNotation._ContainsOnly("d1234567890-+/* )("),
			"unexpected characters detected.  are you sure you are inputing dice notation?");

		__.GetLogger()._EzError(
			!(diceNotation.Contains("-") || diceNotation.Contains("/") || diceNotation.Contains("%") ||
			  diceNotation.Contains("(")),
			"this is a limited functionality dice parser.  please add this functionality (it's easy).  Also, remove the lock ");

		//lock (rand)
		{
			int total = 0;

			// Addition is lowest order of precedence
			var addGroups = diceNotation.Split('+');

			// Add results of each group
			if (addGroups.Length > 1)
			{
				foreach (var expression in addGroups)
				{
					total += rand._NextDice(expression);
				}
			}
			else
			{
				// Multiplication is next order of precedence
				var multiplyGroups = addGroups[0].Split('*');

				// Multiply results of each group
				if (multiplyGroups.Length > 1)
				{
					total = 1; // So that we don't zero-out our results...

					foreach (var expression in multiplyGroups)
					{
						total *= rand._NextDice(expression);
					}
				}
				else
				{
					// Die definition is our highest order of precedence
					var diceGroups = multiplyGroups[0].Split('d');

					// This operand will be our die count, static digits, or else something we don't understand
					if (!int.TryParse(diceGroups[0].Trim(), out total))
					{
						total = 0;
					}

					int faces;

					// Multiple definitions ("2d6d8") iterate through left-to-right: (2d6)d8
					for (int i = 1; i < diceGroups.Length; i++)
					{
						// If we don't have a right side (face count), assume 6
						if (!int.TryParse(diceGroups[i].Trim(), out faces))
						{
							faces = 6;
						}

						int groupOutcome = 0;

						// If we don't have a die count, use 1
						for (int j = 0; j < (total == 0 ? 1 : total); j++)
							groupOutcome += rand.Next(1, faces);

						total += groupOutcome;
					}
				}
			}

			return total;
		}
	}
}

