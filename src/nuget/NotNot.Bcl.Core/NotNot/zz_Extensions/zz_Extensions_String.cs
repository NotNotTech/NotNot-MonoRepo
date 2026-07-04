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

//[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]
//public static class zz__CharArray_Extensions
//{

//	/// <summary>
//	/// 	Converts the char[] to a byte-array using the supplied encoding
//	/// </summary>
//	/// <param name = "value">The input string.</param>
//	/// <param name = "encoding">The encoding to be used.  default UTF8</param>
//	/// <returns>The created byte array</returns>
//	/// <example>
//	/// 	<code>
//	/// 		var value = "Hello World";
//	/// 		var ansiBytes = value.ToBytes(Encoding.GetEncoding(1252)); // 1252 = ANSI
//	/// 		var utf8Bytes = value.ToBytes(Encoding.UTF8);
//	/// 	</code>
//	/// </example>
//	public static byte[] _ToBytes(this char[] array, Encoding encoding = null, bool withPreamble = false, int start = 0, int? count = null)
//	{

//		if (!count.HasValue)
//		{
//			count = array.Length - start;
//		}
//		encoding = (encoding ?? Encoding.UTF8);
//		if (withPreamble)
//		{
//			var preamble = encoding.GetPreamble();

//			var stringBytes = encoding.GetBytes(array, start, count.Value);
//			var bytes = preamble._Join(stringBytes);
//			__.GetLogger()._EzError(bytes.Compare(preamble) == 0);
//			return bytes;
//		}
//		else
//		{
//			return encoding.GetBytes(array, start, count.Value);
//		}
//	}
//	public static int GetHashUniversal(this char[] array, int start = 0, int? count = null)
//	{
//		var bytes = array.ToBytes(start: start, count: count);
//		return (int)HashAlgorithm.Hash(bytes);
//	}
//}
/// <summary>
///    Extension methods for the string data type
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_String
{
	/// <summary>
	/// Compresses the provided string using Brotli compression and encodes the result in Z85e.
	/// <para>If you need to use a dictionary, use ZstdSharp (already a referenced nuget package)</para>
	/// </summary>
	/// <param name="input">The string to compress</param>
	/// <returns>The Z85e encoded compressed string</returns>
	/// <exception cref="ArgumentNullException">Thrown when input is null</exception>
	public static string _Compress(this string input)
	{
		// Validate input: throw an exception if input is null
		if (input == null)
		{
			throw new ArgumentNullException(nameof(input), "Input string cannot be null.");
		}

		// Return an empty string for an empty input to avoid unnecessary processing
		if (input == "")
		{
			return "";
		}

		// Convert the input string to a byte array using UTF8 encoding
		byte[] inputBytes = Encoding.UTF8.GetBytes(input);


		//{
		//   //using zStd
		//   var c = new ZstdSharp.Compressor();
		//   var compressedBytes = c.Wrap(inputBytes);
		//   // Use the Z85e encoding method to encode the compressed bytes
		//   return SimpleBase.Base85.Z85.Encode(compressedBytes);
		//}

		// Using a memory stream to temporarily store the compressed data
		using (var outputStream = new MemoryStream())
		{
			// Compress the data using Brotli compression stream
			using (var compressionStream = new BrotliStream(outputStream, CompressionMode.Compress))
			{
				compressionStream.Write(inputBytes, 0, inputBytes.Length);
			}

			// Retrieve the compressed data
			byte[] compressedBytes = outputStream.ToArray();



			// Use the Z85e encoding method to encode the compressed bytes
			return SimpleBase.Base85.Z85.Encode(compressedBytes);


		}
	}

	/// <summary>
	/// Decompresses a Z85e encoded and Brotli compressed string back to its original form.
	/// <para>If you need to use a dictionary, use ZstdSharp (already a referenced nuget package)</para>
	/// </summary>
	/// <param name="input">The Z85e encoded and compressed string</param>
	/// <returns>The original uncompressed string</returns>
	/// <exception cref="ArgumentNullException">Thrown when input is null</exception>
	/// <exception cref="FormatException">Thrown when input is not a valid Z85e encoded string</exception>
	public static string _Decompress(this string input)
	{
		// Validate input: throw an exception if input is null
		if (input == null)
		{
			throw new ArgumentNullException(nameof(input), "Input string cannot be null.");
		}

		// Return an empty string for an empty input to avoid unnecessary processing
		if (input == "")
		{
			return "";
		}

		try
		{

			// Decode the input string from Z85e encoding
			byte[] decodedBytes = SimpleBase.Base85.Z85.Decode(input);


			//{
			//   //using zStd
			//   var d = new ZstdSharp.Decompressor();
			//   var decompressedBytes = d.Unwrap(decodedBytes);
			//   // Convert the decompressed byte array back to a string using UTF8 encoding
			//   return Encoding.UTF8.GetString(decompressedBytes);
			//}


			// Decompress the data using Brotli compression stream
			using (var inputStream = new MemoryStream(decodedBytes))
			using (var decompressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
			using (var resultStream = new MemoryStream())
			{
				decompressionStream.CopyTo(resultStream);

				// Convert the decompressed byte array back to a string using UTF8 encoding
				return Encoding.UTF8.GetString(resultStream.ToArray());
			}
		}
		catch (Exception ex) when (ex is FormatException || ex is InvalidDataException)
		{
			throw new FormatException("Input string is not a valid Z85e encoded or Brotli compressed string.", ex);
		}
	}


	public static string _FormatAppendArgs(this string message,
		object? objToLog0 = null, object? objToLog1 = null, object? objToLog2 = null,
		[CallerArgumentExpression("objToLog0")] string? objToLog0Name = null,
		[CallerArgumentExpression("objToLog1")] string? objToLog1Name = null,
		[CallerArgumentExpression("objToLog2")] string? objToLog2Name = null,
		string joinString = "\n\t"
	)
	{
		//store all (objToLog,objToLogName) pairs in a list, discarding any pairs with an objToLogName of "null"
		//create a finalLogMessage combining the message with the names from each pair, showing the values from each pair      
		//pass the finalLogMessage and all the values to the Microsoft.Extensions.Logging.ILogger.Log method
		//that ILogger.Log has the following signature: public static void Log(this ILogger logger, LogLevel logLevel, Exception? exception, string? message, params object?[] args)

		using (__.pool.Rent<List<(string name, object? value)>>(out var argPairs))
		{
			if (argPairs.Count > 0)
			{
				throw new Exception("argPairs.Count > 0");
			}

			if (objToLog0 is not null || objToLog0Name is not null)
			{
				argPairs.Add((objToLog0Name, objToLog0));
			}

			if (objToLog1 is not null || objToLog1Name is not null)
			{
				argPairs.Add((objToLog1Name, objToLog1));
			}

			if (objToLog2 is not null || objToLog2Name is not null)
			{
				argPairs.Add((objToLog2Name, objToLog2));
			}

			////roundtrip argValues to json to avoid logger (serilog) max depth errors
			//{
			//   for (var i = 0; i < argPairs.Count; i++)
			//   {
			//      try
			//      {
			//         var obj = argPairs[i].value;

			//         if (obj is null)
			//         {
			//            continue;
			//         }

			//         argPairs[i] = (argPairs[i].name, SerializationHelper.ToPoCo(obj));
			//      }
			//      catch (Exception err)
			//      {
			//         __.GetLogger().LogError($"could not roundtrip {argPairs[i].name} due to error {argPairs[i].value}.", err);
			//         throw;
			//      }
			//   }
			//}


			//adjust our message to include all arg Name+Values
			for (var i = 0; i < argPairs.Count; i++)
			{
				var (argName, argValue) = argPairs[i];


				//sanitize argName            
				argName = argName._ConvertToAlphanumeric();

				//sanitize argValue
				if (argValue is null)
				{
					argValue = "null";
				}
				//else if (argValue is string str)
				//{
				//   argValue = str._ConvertToAlphanumeric();
				//}
				//else if (argValue is IEnumerable enumerable)
				//{
				//   argValue = enumerable.Cast<object>().Select(x => x.ToString()).Join()
				//}
				else
				{
					argValue = argValue.ToString()._Replace("[", '(')._Replace("]", ')');
				}

				message += $"{joinString}{argName} : {argValue}";
			}


			return message;
		}
	}


	public static string _AppendArgs(this string message, object? objToLog0 = null, object? objToLog1 = null, object? objToLog2 = null,
		[CallerArgumentExpression("objToLog0")] string? objToLog0Name = null,
		[CallerArgumentExpression("objToLog1")] string? objToLog1Name = null,
		[CallerArgumentExpression("objToLog2")] string? objToLog2Name = null
	)
	{
		if (string.IsNullOrWhiteSpace(objToLog0Name) is false)
		{
			message = $"{message}; {objToLog0Name}={objToLog0}";
		}
		if (string.IsNullOrWhiteSpace(objToLog1Name) is false)
		{
			message = $"{message}; {objToLog1Name}={objToLog1}";
		}
		if (string.IsNullOrWhiteSpace(objToLog2Name) is false)
		{
			message = $"{message}; {objToLog2Name}={objToLog2}";
		}

		return message;
	}

	/// <summary>
	///    extracts the suffix of this string once it no longer matches the other string
	/// </summary>
	public static string _GetUniqueSuffix(this string value, string other)
	{
		//find the point in both strings where they no longer match
		//return the remainder of the "value" string from that point onwards


		if (string.IsNullOrEmpty(value))
		{
			return value;
		}

		int minLength = Math.Min(value.Length, other.Length);
		int mismatchIndex = 0;

		while (mismatchIndex < minLength && value[mismatchIndex] == other[mismatchIndex])
			mismatchIndex++;

		return mismatchIndex >= value.Length ? "" : value.Substring(mismatchIndex);
	}

	/// <summary>
	///    Extracts the prefix of this string once it no longer matches the other string's ending.
	/// </summary>
	/// <param name="value">The string to extract the unique prefix from.</param>
	/// <param name="other">The string to compare against.</param>
	/// <returns>The beginning of the "value" string, before the non-matching point.</returns>
	public static string _GetUniquePrefix(this string value, string other)
	{
		if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(other))
		{
			return value;
		}

		int valueIndex = value.Length - 1;
		int otherIndex = other.Length - 1;

		// Searching from the end, find where they no longer match
		while (valueIndex >= 0 && otherIndex >= 0 && value[valueIndex] == other[otherIndex])
		{
			valueIndex--;
			otherIndex--;
		}

		// Return the beginning of the "value" string, before the non-matching point
		return value.Substring(0, valueIndex + 1);
	}

	/// <summary>
	///    ignores case, trims, etc and culture
	/// </summary>
	/// <param name="value"></param>
	/// <param name="other"></param>
	/// <returns></returns>
	public static bool _AproxEqual(this string value, string? other)
	{
		if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(other))
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(other))
		{
			return false;
		}

		return value._ConvertToAlphanumeric().Trim().Equals(other._ConvertToAlphanumeric().Trim(), StringComparison.InvariantCultureIgnoreCase);
	}

	/// <summary>
	///    like <see cref="_AproxEqual" /> but MORE lenient: strips ALL non-alphanumeric characters
	///    (including all whitespace) from both strings first, so separator/punctuation PLACEMENT never
	///    causes a mismatch, then compares via <see cref="_AproxEqual" /> semantics (case + culture
	///    insensitive).
	///    <para>example: "✳ Skill-Housekeeping!" ._Similar "skill housekeeping" ==> true</para>
	///    <para>(contrast <see cref="_AproxEqual" />, which collapses separator RUNS to a single '_' —
	///    "SkillHousekeeping" vs "Skill Housekeeping" differ there but match here)</para>
	/// </summary>
	public static bool _Similar(this string? value, string? other)
	{
		if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(other))
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(other))
		{
			return false;
		}

		return value._ConvertToAlphanumeric(whiteSpace: null)._AproxEqual(other._ConvertToAlphanumeric(whiteSpace: null));
	}

	///// <summary>
	///// Equality using OrdinalIgnoreCase
	///// </summary>
	//public static bool _Eq(this string first, string second)
	//{
	//	return first.Equals(second, StringComparison.OrdinalIgnoreCase);
	//}



	private static ConcurrentDictionary<string, Regex> _ToRegex_compiledCache = new();
	/// <summary>
	///    convert string to regex, with default options for performance.
	///    Future calls return the prior, compiled version
	/// </summary>
	public static Regex _ToRegex(this string regexp)
	{
		return _ToRegex_compiledCache.GetOrAdd(regexp, static (regexp) =>
		{
			RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.Compiled | RegexOptions.CultureInvariant;
			var toReturn = new Regex(regexp, options);
			return toReturn;
		});
	}

	public static bool _ToBool(this string? value, bool defaultIfNullOrInvalid = default)
	{
		if (bool.TryParse(value, out var result))
		{
			return result;
		}
		return defaultIfNullOrInvalid;
	}

	//public static double _ToDouble(this string? value, double defaultIfNullOrInvalid = default)
	//{
	//   if (double.TryParse(value, out var result))
	//   {
	//      return result;
	//   }
	//   return defaultIfNullOrInvalid;
	//}

	public static T _ToNumber<T>(this string? value, T defaultIfNullOrInvalid = default, IFormatProvider? formatProvider = default) where T : INumber<T>
	{
		formatProvider ??= CultureInfo.InvariantCulture;
		if (T.TryParse(value, formatProvider, out var result))
		{
			return result;
		}
		return defaultIfNullOrInvalid;
	}



	#region Bytes & Base64

	/// <summary>
	///    Converts the string directly into a byte-array using the supplied encoding (utf8 by default)
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="encoding">The encoding to be used.  default UTF8</param>
	/// <param name="withPreamble">default false.   if true, prepends a marker specifying encoding</param>
	/// <returns>The created byte array</returns>
	/// <example>
	///    <code>
	/// 		var value = "Hello World";
	/// 		var ansiBytes = value.ToBytes(Encoding.GetEncoding(1252)); // 1252 = ANSI
	/// 		var utf8Bytes = value.ToBytes(Encoding.UTF8);
	/// 	</code>
	/// </example>
	public static byte[] _ToBytes(this string value, Encoding encoding = null, bool withPreamble = false)
	{
		encoding = encoding ?? Encoding.UTF8;
		if (withPreamble)
		{
			var preamble = encoding.GetPreamble();
			var stringBytes = encoding.GetBytes(value);
			var bytes = preamble._Join(stringBytes);
			__.GetLogger()._EzError(bytes._Compare(preamble) == 0);
			return bytes;
		}

		return encoding.GetBytes(value);
	}



	/// <summary>
	/// convert base64 encoded binary back into a byte[]
	/// </summary>
	/// <param name="input"></param>
	/// <returns></returns>
	public static byte[] _FromBase64(this string input)
	{
		var requiredPadding = 4 - input.Length % 4;
		if (requiredPadding < 4)
		{
			input += new string('=', requiredPadding);
		}
		return Convert.FromBase64String(input);
	}

	#endregion

	#region globalization

	public static string _FormatInvariant(this string format, params object[] args)
	{
		return string.Format(CultureInfo.InvariantCulture, format, args);
	}

	public static int _CompareTo(this string strA, string strB, StringComparison comparison)
	{
		return string.Compare(strA, strB, comparison);
	}

	#endregion globalization

	#region Common string extensions

	/// <summary>
	///    returns true if string only contains <see cref="characters" /> from input paramaters.
	/// </summary>
	/// <param name="toEvaluate"></param>
	/// <param name="characters"></param>
	public static bool _ContainsOnly(this string toEvaluate, string characters)
	{
		foreach (var c in toEvaluate)
		{
			if (characters.IndexOf(c) >= 0)
			{
				continue;
			}

			return false;
		}

		return true;
	}

	/// <summary>
	///    returns true if string only contains <see cref="characters" /> from input paramaters.
	/// </summary>
	/// <param name="toEvaluate"></param>
	/// <param name="characters"></param>
	public static bool _ContainsOnly(this string toEvaluate, params char[] characters)
	{
		foreach (var c in toEvaluate)
		{
			if (characters.Contains(c))
			{
				continue;
			}

			return false;
		}

		return true;
	}

	/// <summary>
	///    returns true if string only contains <see cref="characters" /> from input paramaters.
	/// </summary>
	/// <param name="toEvaluate"></param>
	/// <param name="characters"></param>
	public static bool _ContainsOnly(this string toEvaluate, char only)
	{
		foreach (var c in toEvaluate)
		{
			if (c == only)
			{
				continue;
			}

			return false;
		}

		return true;
	}


	public static bool _EndsWith(this string value, char c)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		return value[value.Length - 1].Equals(c);
	}

	public static bool _StartsWith(this string value, char c)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		return value[0].Equals(c);
	}

	/// <summary>
	///    Determines whether the specified string is null or empty.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	public static bool _IsNullOrEmpty(this string value)
	{
		return string.IsNullOrEmpty(value);
	}

	/// <summary>
	/// Normalizes a markdown-ish text body into a stable canonical form.
	/// <list type="number">
	///   <item>Normalizes line endings (<c>\r\n</c>, <c>\r</c>) to <c>\n</c>.</item>
	///   <item>Right-trims each line (lines containing only whitespace become empty). Leading whitespace is preserved (markdown indentation matters).</item>
	///   <item>Collapses runs of 3+ consecutive <c>\n</c> down to exactly <c>\n\n</c> (max one blank line between content).</item>
	///   <item>Strips leading and trailing empty lines. All-whitespace input collapses to <c>""</c>.</item>
	/// </list>
	/// Null-in -> null-out. Empty-in -> empty-out.
	/// </summary>
	public static string? _TrimMarkdown(this string? value)
	{
		if (string.IsNullOrEmpty(value))
			return value;

		// Step 1: normalize line endings to \n
		var normalized = value.Replace("\r\n", "\n").Replace("\r", "\n");

		// Step 2: right-trim each line (preserves leading whitespace for markdown indent)
		var lines = normalized.Split('\n');
		for (var i = 0; i < lines.Length; i++)
			lines[i] = lines[i].TrimEnd();
		var joined = string.Join('\n', lines);

		// Step 3: collapse 3+ consecutive newlines to exactly 2
		while (joined.Contains("\n\n\n"))
			joined = joined.Replace("\n\n\n", "\n\n");

		// Step 4: trim leading and trailing empty lines
		return joined.Trim('\n');
	}


	/// <summary>
	///    Trims the text to a provided maximum length.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="maxLength">Maximum length.</param>
	/// <returns></returns>
	/// <remarks>
	///    Proposed by Rene Schulte
	/// </remarks>
	public static string _SetMaxLength(this string value, int maxLength, bool avoidEllipsis=false)
	{
		var useEllipsis = !avoidEllipsis && maxLength > 3;
		if (value == null || value.Length <= maxLength)
		{
			return value;
		}

		if (useEllipsis is false)
		{
			return  value.Substring(0, maxLength);
		}
		else
		{
			return value.Substring(0, maxLength - 3) + "...";
		}
	}





	/// <summary>
	/// truncate string if too long, ending with `...` if truncation occurs and maxLength is > 3
	/// </summary>
	public static string _SetMaxLengthPostEllipsis(this string value, int maxLength)
	{
		var useEllipsis = maxLength > 3;
		if (value == null || value.Length <= maxLength)
		{
			return value;
		}

		if (useEllipsis is false)
		{
			return value.Substring(0, maxLength);
		}
		else
		{
			return value.Substring(0, maxLength - 3) + "...";
		}
	}
	/// <summary>
	/// truncate string if too long, preceeding by `...` if truncation occurs and maxLength is > 3
	/// </summary>
	public static string _SetMaxLengthPreEllipsis(this string value, int maxLength)
	{
		var useEllipsis = maxLength > 3;
		if (value == null || value.Length <= maxLength)
		{
			return value;
		}

		if (useEllipsis is false)
		{
			return value.Substring(0, maxLength);
		}
		else
		{
			return "..." + value.Substring(value.Length-maxLength+3, maxLength - 3);
		}
	}


	/// <summary>
	///    Determines whether the comparison value strig is contained within the input value string
	/// </summary>
	/// <param name="inputValue">The input value.</param>
	/// <param name="comparisonValue">The comparison value.</param>
	/// <param name="comparisonType">Type of the comparison to allow case sensitive or insensitive comparison.</param>
	/// <returns>
	///    <c>true</c> if input value contains the specified value, otherwise, <c>false</c>.
	/// </returns>
	public static bool _Contains(this string inputValue, string comparisonValue, StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase)
	{
		return inputValue.IndexOf(comparisonValue, comparisonType) != -1;
	}

	public static bool _Contains(this string value, char toFind)
	{
		return value.IndexOf(toFind) != -1;
	}

	public static bool _Contains(this string value, params char[] toFind)
	{
		return value.IndexOfAny(toFind) != -1;
	}

	/// <summary>
	/// true if target contains any of the substrings.
	/// <para>note: a null/empty substring/target will never match</para>
	/// </summary>
	public static bool _ContainsAny(this string? value, ReadOnlySpan<string> stringsToFind, StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase)
	{
		if (value._IsNullOrEmpty())
		{
			return false;
		}
		foreach (var span in stringsToFind)
		{
			if (span._IsNullOrEmpty())
			{
				continue; //skip empty strings
			}

			if (value.IndexOf(span, comparisonType) != -1)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// true if any matches the target
	/// </summary>
	public static bool _EqualsAny(this string? value, ReadOnlySpan<string> stringsToFind, StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase)
	{
		foreach (var span in stringsToFind)
		{
			if (String.Equals(value, span, comparisonType))
			{
				return true;
			}
		}
		return false;
	}


	/// <summary>
	///    Centers a charters in this string, padding in both, left and right, by specified Unicode character,
	///    for a specified total lenght.
	/// </summary>
	/// <param name="value">Instance value.</param>
	/// <param name="width">
	///    The number of characters in the resulting string,
	///    equal to the number of original characters plus any additional padding characters.
	/// </param>
	/// <param name="padChar">A Unicode padding character.</param>
	/// <param name="truncate">
	///    Should get only the substring of specified width if string width is
	///    more than the specified width.
	/// </param>
	/// <returns>
	///    A new string that is equivalent to this instance,
	///    but center-aligned with as many paddingChar characters as needed to create a
	///    length of width paramether.
	/// </returns>
	public static string _PadBoth(this string value, int width, char padChar, bool truncate = false)
	{
		int diff = width - value.Length;
		if (diff == 0 || diff < 0 && !truncate)
		{
			return value;
		}

		if (diff < 0)
		{
			return value.Substring(0, width);
		}

		return value.PadLeft(width - diff / 2, padChar).PadRight(width, padChar);
	}


	/// <summary>
	///    Reverses / mirrors a string.
	/// </summary>
	/// <param name="value">The string to be reversed.</param>
	/// <returns>The reversed string</returns>
	public static string _Reverse(this string value)
	{
		if (value._IsNullOrEmpty() || value.Length == 1)
		{
			return value;
		}

		var chars = value.ToCharArray();
		Array.Reverse(chars);
		return new string(chars);
	}

	/// <summary>
	///    Ensures that a string starts with a given prefix.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="prefix">The prefix value to check for.</param>
	/// <returns>The string value including the prefix</returns>
	/// <example>
	///    <code>
	/// 		var extension = "txt";
	/// 		var fileName = string.Concat(file.Name, extension.EnsureStartsWith("."));
	/// 	</code>
	/// </example>
	public static string _EnsureStartsWith(this string value, string prefix,
		StringComparison compare = StringComparison.OrdinalIgnoreCase)
	{
		return value.StartsWith(prefix, compare) ? value : string.Concat(prefix, value);
	}

	/// <summary>
	///    Ensures that a string ends with a given suffix.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="suffix">The suffix value to check for.</param>
	/// <returns>The string value including the suffix</returns>
	/// <example>
	///    <code>
	/// 		var url = "http://www.pgk.de";
	/// 		url = url.EnsureEndsWith("/"));
	/// 	</code>
	/// </example>
	public static string _EnsureEndsWith(this string value, string suffix,
		StringComparison compare = StringComparison.OrdinalIgnoreCase)
	{
		return value.EndsWith(suffix, compare) ? value : string.Concat(value, suffix);
	}

	/// <summary>
	///    Ensures that a string ends with a given suffix.
	/// </summary>
	/// <param name="value">The string value to check.</param>
	/// <param name="suffix">The suffix value to check for.</param>
	/// <returns>The string value including the suffix</returns>
	/// <example>
	///    <code>
	/// 		var url = "http://www.pgk.de";
	/// 		url = url.EnsureEndsWith("/"));
	/// 	</code>
	/// </example>
	public static string _EnsureEndsWith(this string value, char suffix)
	{
		return value.EndsWith(suffix) ? value : string.Concat(value, suffix);
	}

	/// <summary>
	///    Repeats the specified string value as provided by the repeat count.
	/// </summary>
	/// <param name="value">The original string.</param>
	/// <param name="repeatCount">The repeat count.</param>
	/// <returns>The repeated string</returns>
	public static string _Repeat(this string value, int repeatCount)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < repeatCount; i++)
			sb.Append(value);

		return sb.ToString();
	}

	/// <summary>
	///    Tests whether the contents of a string is a numeric value
	/// </summary>
	/// <param name="value">String to check</param>
	/// <returns>
	///    Boolean indicating whether or not the string contents are numeric
	/// </returns>
	/// <remarks>
	///    Contributed by Kenneth Scott
	/// </remarks>
	public static bool _IsNumeric(this string value)
	{
		float output;
		return float.TryParse(value, out output);
	}

	/// <summary>
	///    Extracts all digits from a string.
	/// </summary>
	/// <param name="value">String containing digits to extract</param>
	/// <returns>
	///    All digits contained within the input string
	/// </returns>
	/// <remarks>
	///    Contributed by Kenneth Scott
	/// </remarks>
	public static string _ExtractDigits(this string value)
	{
		return string.Join(null, Regex.Split(value, "[^\\d]"));
	}

	/// <summary>
	/// returns the index where the two strings become different
	/// </summary>
	public static int _IndexOfDifference(this string str1, string str2)
	{
		// If either string is null, return -1 to indicate an invalid comparison.
		if (str1 == null || str2 == null)
			return -1;

		// Find the length of the shorter string to avoid out-of-bounds errors.
		int minLength = Math.Min(str1.Length, str2.Length);

		// Iterate through each character in both strings up to the length of the shorter string.
		for (int i = 0; i < minLength; i++)
		{
			if (str1[i] != str2[i])
				return i; // Return the index where the characters differ.
		}

		// If no difference was found in the overlapping portion, check for length difference.
		if (str1.Length != str2.Length)
			return minLength; // Difference is at the end of the shorter string.

		// If strings are identical in both content and length, return -1.
		return -1;
	}

	/// <summary>
	///    gets the string after the first instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing"></param>
	/// <returns></returns>
	public static string _GetAfterFirst(this string value, string left, bool? fullIfLeftMissing = null)
	{
		var xPos = value.IndexOf(left, StringComparison.Ordinal);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				__.Throw($" '{left}' not found in '{value}'");
			}
			return fullIfLeftMissing.GetValueOrDefault() ? value : string.Empty;
		}

		var startIndex = xPos + left.Length;
		return startIndex >= value.Length ? string.Empty : value[startIndex..];
	}

	/// <summary>
	///    gets the string after the first instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing">if not set, will throw if missing</param>
	/// <returns></returns>
	public static string _GetAfterFirst(this string value, char left, bool? fullIfLeftMissing = null)
	{
		var xPos = value.IndexOf(left);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				__.Throw($" '{left}' not found in '{value}'");
			}
			return fullIfLeftMissing.GetValueOrDefault() ? value : string.Empty;
		}

		var startIndex = xPos + 1;
		return startIndex >= value.Length ? string.Empty : value.Substring(startIndex);
	}

	/// <summary>
	///    Gets the string before the first instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="right">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetBefore(this string value, string right, bool? fullIfRightMissing = null)
	{
		var xPos = value.IndexOf(right, StringComparison.Ordinal);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    Gets the string before the first instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="right">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetBefore(this string value, char right, bool? fullIfRightMissing = null)
	{
		var xPos = value.IndexOf(right);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    gets the string before the last instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing"></param>
	/// <returns></returns>
	public static string _GetBeforeLast(this string value, string right, bool? fullIfRightMissing = null)
	{
		var xPos = value.LastIndexOf(right, StringComparison.Ordinal);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    gets the string before the last instance of the given parameter
	/// </summary>
	/// <param name="value"></param>
	/// <param name="right"></param>
	/// <param name="fullIfRightMissing"></param>
	/// <returns></returns>
	public static string _GetBeforeLast(this string value, char right, bool? fullIfRightMissing = null)
	{
		var xPos = value.LastIndexOf(right);
		__.ThrowIfNot(xPos != -1 || fullIfRightMissing.HasValue, "search string not found");
		return xPos == -1 ? fullIfRightMissing.Value ? value : string.Empty : value.Substring(0, xPos);
	}

	/// <summary>
	///    Gets the string between the first and last instance of the given parameters.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The left string parameter.</param>
	/// <param name="right">The right string parameter</param>
	/// <returns></returns>
	public static string _GetBetween(this string value, string left, string right, bool fullIfLeftMissing,
		bool fullIfRightMissing)
	{
		var xPos = value.IndexOf(left, StringComparison.Ordinal);
		var yPos = value.LastIndexOf(right, StringComparison.Ordinal);

		if (xPos == -1 && yPos == -1)
		{
			return fullIfLeftMissing && fullIfRightMissing ? value : string.Empty;
		}

		if (xPos == -1)
		{
			return fullIfLeftMissing ? value.Substring(0, yPos) : string.Empty;
		}

		if (yPos == -1)
		{
			var firstIndex = xPos + left.Length;
			return fullIfRightMissing ? value.Substring(firstIndex, value.Length - firstIndex) : string.Empty;
		}

		var startIndex = xPos + left.Length;
		return startIndex >= yPos ? string.Empty : value.Substring(startIndex, yPos - startIndex);
	}

	/// <summary>
	///    Gets the string between the first and last instance of the given parameters.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The left string parameter.</param>
	/// <param name="right">The right string parameter</param>
	/// <returns></returns>
	public static string _GetBetween(this string value, char left, char right, bool fullIfLeftMissing,
		bool fullIfRightMissing)
	{
		var xPos = value.IndexOf(left);
		var yPos = value.LastIndexOf(right);

		if (xPos == -1 && yPos == -1)
		{
			return fullIfLeftMissing && fullIfRightMissing ? value : string.Empty;
		}

		if (xPos == -1)
		{
			return fullIfLeftMissing ? value.Substring(0, yPos) : string.Empty;
		}

		if (yPos == -1)
		{
			var firstIndex = xPos + 1;
			return fullIfRightMissing ? value.Substring(firstIndex, value.Length - firstIndex) : string.Empty;
		}

		var startIndex = xPos + 1;
		return startIndex >= yPos ? string.Empty : value.Substring(startIndex, yPos - startIndex);
	}

	/// <summary>
	///    Gets the string after the last instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetAfter(this string value, string left, bool? fullIfLeftMissing = null,
		StringComparison comparison = StringComparison.OrdinalIgnoreCase)
	{
		var xPos = value.LastIndexOf(left, comparison);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				throw __.Throw("substring not found");
			}
			return fullIfLeftMissing.Value ? value : string.Empty;
		}

		var startIndex = xPos + left.Length;
		return startIndex >= value.Length ? string.Empty : value.Substring(startIndex);
		;
	}

	/// <summary>
	///    Gets the string after the last instance of the given parameter.
	/// </summary>
	/// <param name="value">The default value.</param>
	/// <param name="left">The given string parameter.</param>
	/// <returns></returns>
	public static string _GetAfter(this string value, char left, bool? fullIfLeftMissing = null)
	{
		var xPos = value.LastIndexOf(left);

		if (xPos == -1)
		{
			if (fullIfLeftMissing.HasValue is false)
			{
				throw __.Throw("substring not found");
			}
			return fullIfLeftMissing.Value ? value : string.Empty;
		}

		var startIndex = xPos + 1;
		return startIndex >= value.Length ? string.Empty : value.Substring(startIndex);
	}


	/// <summary>
	///    Remove any instance of the given character from the current string.
	/// </summary>
	/// <param name="value">
	///    The input.
	/// </param>
	/// <param name="charactersToRemove">
	///    The remove char.
	/// </param>
	public static string _Remove(this string value, params char[] charactersToRemove)
	{
		__.GetLogger()._EzError(value is not null);
		var result = value;
		if (!string.IsNullOrEmpty(result) && charactersToRemove != null)
		{
			Array.ForEach(charactersToRemove, c => result = result._Remove(c.ToString()));
		}

		return result;
	}

	/// <summary>
	///    Remove any instance of the given string pattern from the current string.
	/// </summary>
	/// <param name="value">The input.</param>
	/// <param name="strings">The strings.</param>
	/// <returns></returns>
	public static string _Remove(this string value, params string[] strings)
	{
		__.GetLogger()._EzError(value is not null);
		return strings.Aggregate(value, (current, c) => current.Replace(c, string.Empty));
		//var result = value;
		//if (!string.IsNullOrEmpty(result) && removeStrings != null)
		//  Array.ForEach(removeStrings, s => result = result.Replace(s, string.Empty));

		//return result;
	}

	/// <summary>Finds out if the specified string contains null, empty or consists only of white-space characters</summary>
	/// <param name="value">The input string</param>
	public static bool _IsNullOrWhiteSpace(this string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			foreach (var c in value)
			{
				if (!char.IsWhiteSpace(c))
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	///    returns the acronym from the given sentence, with inclusion of camelCases
	///    <example>"The first SimpleExample   ... startsHere!" ==> "TfSEsH"</example>
	/// </summary>
	/// <param name="camelCaseSentence"></param>
	/// <returns></returns>
	public static string _ToAcronym(this string camelCaseSentence)
	{
		__.GetLogger()._EzError(camelCaseSentence is not null);
		if (camelCaseSentence == null)
		{
			return null;
		}

		camelCaseSentence = camelCaseSentence.Trim();

		string toReturn = string.Empty;

		foreach (var camelCaseWord in camelCaseSentence.Split(' '))
		{
			toReturn += camelCaseWord._GetAcronymHelper();
		}

		return toReturn;
	}

	private static string _GetAcronymHelper(this string camelCaseWord)
	{
		__.GetLogger()._EzError(camelCaseWord is not null);
		if (camelCaseWord == null)
		{
			return string.Empty;
		}

		camelCaseWord = camelCaseWord.Trim();

		if (camelCaseWord.Length == 0)
		{
			return string.Empty;
		}

		string toReturn = string.Empty;
		int firstFoundChar = 0;
		for (; firstFoundChar < camelCaseWord.Length; firstFoundChar++)
			if (char.IsLetter(camelCaseWord, firstFoundChar))
			{
				toReturn += camelCaseWord[firstFoundChar];
				break;
			}

		for (int i = firstFoundChar + 1; i < camelCaseWord.Length; i++)
			if (char.IsUpper(camelCaseWord, i))
			{
				toReturn += camelCaseWord[i];
			}

		return toReturn;
	}

	/// <summary>Uppercase First Letter</summary>
	/// <param name="value">The string value to process</param>
	public static string _ToUpperFirstLetter(this string value)
	{
		if (value._IsNullOrWhiteSpace())
		{
			return string.Empty;
		}

		char[] valueChars = value.ToCharArray();
		valueChars[0] = char.ToUpper(valueChars[0], CultureInfo.InvariantCulture);

		return new string(valueChars);
	}
	public static string _ToLowerFirstLetter(this string value)
	{
		if (value._IsNullOrWhiteSpace())
		{
			return string.Empty;
		}

		char[] valueChars = value.ToCharArray();
		valueChars[0] = char.ToLower(valueChars[0], CultureInfo.InvariantCulture);

		return new string(valueChars);
	}

	/// <summary>
	///    Returns the left part of the string.
	/// </summary>
	/// <param name="value">The original string.</param>
	/// <param name="characterCount">The character count to be returned.</param>
	/// <returns>The left part</returns>
	public static string _Left(this string value, int characterCount, bool throwIfTooShort = false)
	{
		if (throwIfTooShort)
		{
			return value.Substring(0, characterCount);
		}
		else
		{
			return value.Substring(0, Math.Min(characterCount, value.Length));
		}
	}

	/// <summary>
	///    Returns the Right part of the string.
	/// </summary>
	/// <param name="value">The original string.</param>
	/// <param name="characterCount">The character count to be returned.</param>
	/// <returns>The right part</returns>
	public static string _Right(this string value, int characterCount, bool throwIfTooShort = false)
	{
		if (throwIfTooShort)
		{
			return value.Substring(value.Length - characterCount);
		}
		else
		{
			return value.Substring(value.Length - Math.Min(characterCount, value.Length));
		}
	}

	/// <summary>Returns the right part of the string from index.</summary>
	/// <param name="value">The original value.</param>
	/// <param name="index">The start index for substringing.</param>
	/// <returns>The right part.</returns>
	public static string _SubstringFrom(this string value, int index)
	{
		return index < 0 ? value : value.Substring(index, value.Length - index);
	}


	public static string _ToPlural(this string singular)
	{
		// Multiple words in the form A of B : Apply the plural to the first word only (A)
		int index = singular.LastIndexOf(" of ", StringComparison.OrdinalIgnoreCase);
		if (index > 0)
		{
			return singular.Substring(0, index) + singular.Remove(0, index)._ToPlural();
		}

		// single Word rules
		//sibilant ending rule
		if (singular.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		if (singular.EndsWith("c", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		if (singular.EndsWith("us", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		if (singular.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
		{
			return singular + "es";
		}

		//-ies rule
		if (singular.EndsWith("y", StringComparison.OrdinalIgnoreCase))
		{
			return singular.Remove(singular.Length - 1, 1) + "ies";
		}

		// -oes rule
		if (singular.EndsWith("o", StringComparison.OrdinalIgnoreCase))
		{
			return singular.Remove(singular.Length - 1, 1) + "oes";
		}

		// -s suffix rule
		return singular + "s";
	}

	/// <summary>
	///    Makes the current instance HTML safe.
	/// </summary>
	/// <param name="s">The current instance.</param>
	/// <returns>An HTML safe string.</returns>
	public static string _ToHtmlSafe(this string s)
	{
		return s._ToHtmlSafe(false, false);
	}

	/// <summary>
	///    Makes the current instance HTML safe.
	/// </summary>
	/// <param name="s">The current instance.</param>
	/// <param name="all">Whether to make all characters entities or just those needed.</param>
	/// <returns>An HTML safe string.</returns>
	public static string _ToHtmlSafe(this string s, bool all)
	{
		return s._ToHtmlSafe(all, false);
	}

	/// <summary>
	///    Makes the current instance HTML safe.
	/// </summary>
	/// <param name="s">The current instance.</param>
	/// <param name="all">Whether to make all characters entities or just those needed.</param>
	/// <param name="replace">Whether or not to encode spaces and line breaks.</param>
	/// <returns>An HTML safe string.</returns>
	public static string _ToHtmlSafe(this string s, bool all, bool replace)
	{
		if (s._IsNullOrWhiteSpace())
		{
			return string.Empty;
		}

		var entities = new[]
		{
			0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 29,
			30, 31, 34, 39, 38, 60, 62, 123, 124, 125, 126, 127, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170,
			171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191,
			215, 247, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210,
			211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231,
			232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252,
			253, 254, 255, 256, 8704, 8706, 8707, 8709, 8711, 8712, 8713, 8715, 8719, 8721, 8722, 8727, 8730, 8733,
			8734, 8736, 8743, 8744, 8745, 8746, 8747, 8756, 8764, 8773, 8776, 8800, 8801, 8804, 8805, 8834, 8835, 8836,
			8838, 8839, 8853, 8855, 8869, 8901, 913, 914, 915, 916, 917, 918, 919, 920, 921, 922, 923, 924, 925, 926,
			927, 928, 929, 931, 932, 933, 934, 935, 936, 937, 945, 946, 947, 948, 949, 950, 951, 952, 953, 954, 955,
			956, 957, 958, 959, 960, 961, 962, 963, 964, 965, 966, 967, 968, 969, 977, 978, 982, 338, 339, 352, 353,
			376, 402, 710, 732, 8194, 8195, 8201, 8204, 8205, 8206, 8207, 8211, 8212, 8216, 8217, 8218, 8220, 8221,
			8222, 8224, 8225, 8226, 8230, 8240, 8242, 8243, 8249, 8250, 8254, 8364, 8482, 8592, 8593, 8594, 8595, 8596,
			8629, 8968, 8969, 8970, 8971, 9674, 9824, 9827, 9829, 9830
		};
		var sb = new StringBuilder();
		foreach (var c in s)
		{
			if (all || entities.Contains(c))
			{
				sb.Append("&#" + (int)c + ";");
			}
			else
			{
				sb.Append(c);
			}
		}

		return replace
			? sb.Replace("", "<br />").Replace("\n", "<br />").Replace(" ", "&nbsp;").ToString()
			: sb.ToString();
	}

	#endregion

	#region Regex based extension methods

	/// <summary>
	///    Uses regular expressions to determine if the string matches to a given regex pattern.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>
	///    <c>true</c> if the value is matching to the specified pattern; otherwise, <c>false</c>.
	/// </returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var isMatching = s.IsMatchingTo(@"^\d+$");
	/// 	</code>
	/// </example>
	public static bool _Equals(this string value, string regexPattern, RegexOptions options)
	{
		return Regex.IsMatch(value, regexPattern, options);
	}

	/// <summary>
	///    replace all instances of the given characters with the given value
	/// </summary>
	/// <param name="value"></param>
	/// <param name="toReplace"></param>
	/// <param name="newValue"></param>
	/// <returns></returns>
	public static string _Replace(this string value, string charsToReplace, char? replacementChar,
		StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
	{
		var sb = new StringBuilder(value.Length);
		foreach (var c in value)
		{
			if (charsToReplace.Contains(c, stringComparison))
			{
				if (replacementChar.HasValue)
				{
					//append replacement
					sb.Append(replacementChar.Value);
				}
				//do nothing
			}
			else
			{
				//char is okay
				sb.Append(c);
			}
		}

		return sb.ToString();
	}

	/// <summary>
	///    Uses regular expressions to replace parts of a string.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="replaceValue">The replacement value.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>The newly created string</returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var replaced = s.ReplaceWith(@"\d", m => string.Concat(" -", m.Value, "- "));
	/// 	</code>
	/// </example>
	public static string _Replace(this string value, string regexPattern, string replaceValue, RegexOptions options)
	{
		return Regex.Replace(value, regexPattern, replaceValue, options);
	}

	/// <summary>
	///    Uses regular expressions to replace parts of a string.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="evaluator">The replacement method / lambda expression.</param>
	/// <returns>The newly created string</returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var replaced = s.ReplaceWith(@"\d", m => string.Concat(" -", m.Value, "- "));
	/// 	</code>
	/// </example>
	public static string _Replace(this string value, string regexPattern, MatchEvaluator evaluator)
	{
		return value._Replace(regexPattern, RegexOptions.None, evaluator);
	}

	/// <summary>
	///    Uses regular expressions to replace parts of a string.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <param name="evaluator">The replacement method / lambda expression.</param>
	/// <returns>The newly created string</returns>
	/// <example>
	///    <code>
	/// 		var s = "12345";
	/// 		var replaced = s.ReplaceWith(@"\d", m => string.Concat(" -", m.Value, "- "));
	/// 	</code>
	/// </example>
	public static string _Replace(this string value, string regexPattern, RegexOptions options, MatchEvaluator evaluator)
	{
		return Regex.Replace(value, regexPattern, evaluator, options);
	}


	public static string _ReplaceFirst(this string text, string search, string replace)
	{
		int length = text.IndexOf(search);
		return length < 0 ? text : text.Substring(0, length) + replace + text.Substring(length + search.Length);
	}
	public static string _ReplaceLast(this string text, string search, string replace)
	{
		int length = text.LastIndexOf(search);
		return length < 0 ? text : text.Substring(0, length) + replace + text.Substring(length + search.Length);
	}


	/// <summary>
	///    Uses regular expressions to determine all matches of a given regex pattern.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <returns>A collection of all matches</returns>
	public static MatchCollection _GetMatches(this string value, string regexPattern)
	{
		return value._GetMatches(regexPattern, RegexOptions.None);
	}

	/// <summary>
	///    Uses regular expressions to determine all matches of a given regex pattern.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>A collection of all matches</returns>
	public static MatchCollection _GetMatches(this string value, string regexPattern, RegexOptions options)
	{
		return Regex.Matches(value, regexPattern, options);
	}
	/// <summary>
	/// a string extension method that splits a long string into substrings of a fixed length, and returns all as a List<string>.   any remainder is also returned.
	/// </summary>
	public static List<string> _Split(this string str, int chunkSize)
	{
		if (str is null)
		{
			throw new ArgumentNullException(nameof(str));
		}
		if (string.IsNullOrEmpty(str))
			return new List<string>();

		if (chunkSize <= 0)
			throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

		var chunks = new List<string>();

		for (int i = 0; i < str.Length; i += chunkSize)
		{
			if (i + chunkSize <= str.Length)
			{
				chunks.Add(str.Substring(i, chunkSize));
			}
			else
			{
				chunks.Add(str.Substring(i));
			}
		}

		return chunks;
	}

	/// <summary>
	///    Uses regular expressions to split a string into parts.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <returns>The splitted string array</returns>
	public static string[] _Split(this string value, string regexPattern)
	{
		return value._Split(regexPattern, RegexOptions.None);
	}

	/// <summary>
	///    Uses regular expressions to split a string into parts.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <param name="regexPattern">The regular expression pattern.</param>
	/// <param name="options">The regular expression options.</param>
	/// <returns>The splitted string array</returns>
	public static string[] _Split(this string value, string regexPattern, RegexOptions options)
	{
		return Regex.Split(value, regexPattern, options);
	}

	/// <summary>
	///    Splits the given string into words and returns a string array.
	/// </summary>
	/// <param name="value">The input string.</param>
	/// <returns>The splitted string array</returns>
	public static string[] _GetWords(this string value)
	{
		return value.Split(@"\W");
	}

	/// <summary>
	///    Gets the nth "word" of a given string, where "words" are substrings separated by a given separator
	/// </summary>
	/// <param name="value">The string from which the word should be retrieved.</param>
	/// <param name="index">Index of the word (0-based).</param>
	/// <returns>
	///    The word at position n of the string.
	///    Trying to retrieve a word at a position lower than 0 or at a position where no word exists results in an exception.
	/// </returns>
	/// <remarks>
	///    Originally contributed by MMathews
	/// </remarks>
	public static string _GetWordByIndex(this string value, int index)
	{
		var words = value._GetWords();

		if (index < 0 || index > words.Length - 1)
		{
			throw new ArgumentOutOfRangeException("index", "The word number is out of range.");
		}

		return words[index];
	}


	/// <summary>
	///    converts a string to a stripped down version, only allowing alphaNumeric plus a single whiteSpace character
	///    (customizable with default being '_' )
	///    <para>
	///       note: leading and trailing whiteSpace is trimmed, and internal whiteSpace is truncated down to single
	///       characters
	///    </para>
	///    <para>This can be used to "safe encode" strings before use with xml</para>
	///    <para>example:  "Hello, World!" ==> "Hello_World"</para>
	/// </summary>
	/// <param name="toConvert">special case: if null, "null" is returned</param>
	/// <param name="whiteSpace">
	///    char to use as whiteSpace.  set to null to not write any whiteSpace (alphaNumeric chars only)
	///    default is underscore '_'
	/// </param>
	/// <returns></returns>
	public static string _ConvertToAlphanumeric(this string toConvert, char? whiteSpace = '_')
	{
		if (toConvert is null)
		{
			return "null";
		}
		var sb = new StringBuilder(toConvert.Length);

		bool includeWhitespace = whiteSpace.HasValue;
		bool isWhitespace = false;
		foreach (var c in toConvert)
		{
			if (c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
			{
				sb.Append(c);
				isWhitespace = false;
			}
			else
			{
				if (!isWhitespace && includeWhitespace)
				{
					sb.Append(whiteSpace.Value);
				}

				isWhitespace = true;
			}
		}

		string toReturn = sb.ToString();

		if (includeWhitespace)
		{
			return toReturn.Trim(whiteSpace.Value);
		}

		return toReturn;
	}

	/// <summary>
	///    converts a string to a stripped down version, only allowing alphaNumeric plus a single whiteSpace character
	///    (customizable with default being '_' )
	///    <para>
	///       note: leading and trailing whiteSpace is trimmed, and internal whiteSpace is truncated down to single
	///       characters
	///    </para>
	///    <para>This can be used to "safe encode" strings before use with xml</para>
	///    <para>example:  "Hello, World!" ==> "Hello_World"</para>
	/// </summary>
	/// <param name="toConvert"></param>
	/// <param name="whiteSpace">
	///    char to use as whiteSpace.  set to null to not write any whiteSpace (alphaNumeric chars only)
	///    default is underscore '_'
	/// </param>
	/// <returns></returns>
	public static string _ConvertToAlphanumericCapitalize(this string toConvert, char? whiteSpace = '_')
	{
		var toReturn = _ConvertToAlphanumeric(toConvert, whiteSpace);
		if (toReturn.Length > 0)
		{
			toReturn = char.ToUpper(toReturn[0]) + toReturn.Substring(1);
		}
		return toReturn;
		//var sb = new StringBuilder(toConvert.Length);

		//bool includeWhitespace = whiteSpace.HasValue;
		//bool isWhitespace = false;
		//foreach (var c in toConvert)
		//{
		//	if (c >= '0' && c <= '9' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
		//	{
		//		sb.Append(c);
		//		isWhitespace = false;
		//	}
		//	else
		//	{
		//		if (!isWhitespace && includeWhitespace)
		//		{
		//			sb.Append(whiteSpace.Value);
		//		}

		//		isWhitespace = true;
		//	}
		//}



		//string toReturn = sb.ToString();

		//if (includeWhitespace)
		//{
		//	toReturn = toReturn.Trim(whiteSpace.Value);
		//}
		//if (toReturn.Length > 0)
		//{
		//	toReturn = char.ToUpper(toReturn[0]) + toReturn.Substring(1);
		//}
		//return toReturn;
	}

	#endregion

	#region Template String Resolution

	/// <summary>
	/// Compiled regex for matching %key% placeholders in template strings.
	/// Keys must start with letter or underscore, followed by letters, digits, underscores, or colons.
	/// Colons enable prefixed keys like <c>%env:VARNAME%</c>.
	/// </summary>
	private static readonly Regex _templatePlaceholderPattern = new(
		@"%([a-zA-Z_][a-zA-Z0-9_:]*)%",
		RegexOptions.Compiled);

	/// <summary>
	/// Replaces %key% placeholders with values from resolver callback.
	/// Keys are case-sensitive. Unresolved keys (callback returns null) are left unchanged.
	/// </summary>
	/// <param name="template">The template string containing %key% placeholders</param>
	/// <param name="resolver">Callback that receives key (without %) and returns replacement value, or null to leave unchanged</param>
	/// <returns>Template with resolved placeholders</returns>
	/// <exception cref="ArgumentNullException">Thrown when template or resolver is null</exception>
	/// <example>
	/// <code>
	/// var result = "Hello %name%!"._ResolveTemplate(key => key == "name" ? "World" : null);
	/// // result: "Hello World!"
	/// </code>
	/// </example>
	public static string _ResolveTemplate(this string template, Func<string, string?> resolver)
	{
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(resolver);

		return _templatePlaceholderPattern.Replace(template, match =>
		{
			var key = match.Groups[1].Value;
			return resolver(key) ?? match.Value;
		});
	}

	/// <summary>
	/// Replaces %key% placeholders with values from dictionary.
	/// Keys are case-sensitive. Missing keys are left unchanged.
	/// </summary>
	/// <param name="template">The template string containing %key% placeholders</param>
	/// <param name="values">Dictionary mapping keys to replacement values</param>
	/// <returns>Template with resolved placeholders</returns>
	/// <exception cref="ArgumentNullException">Thrown when template or values is null</exception>
	/// <example>
	/// <code>
	/// var values = new Dictionary&lt;string, string&gt; { ["name"] = "World" };
	/// var result = "Hello %name%!"._ResolveTemplate(values);
	/// // result: "Hello World!"
	/// </code>
	/// </example>
	public static string _ResolveTemplate(this string template, IReadOnlyDictionary<string, string> values)
	{
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(values);

		return template._ResolveTemplate(key => values.TryGetValue(key, out var value) ? value : null);
	}

	/// <summary>
	/// Replaces %key% placeholders with values from dictionary, with fallback resolver.
	/// Resolution order: dictionary first, then fallback callback.
	/// Keys are case-sensitive. Unresolved keys are left unchanged.
	/// </summary>
	/// <param name="template">The template string containing %key% placeholders</param>
	/// <param name="values">Dictionary mapping keys to replacement values (checked first)</param>
	/// <param name="fallback">Callback for keys not found in dictionary (checked second)</param>
	/// <returns>Template with resolved placeholders</returns>
	/// <exception cref="ArgumentNullException">Thrown when template, values, or fallback is null</exception>
	/// <remarks>
	/// Note: This differs from <see cref="TemplateString"/> class which checks callback FIRST.
	/// Use this when dictionary is authoritative with callback as fallback.
	/// Use <see cref="TemplateString"/> when callback should intercept/override dictionary values.
	/// </remarks>
	public static string _ResolveTemplate(
		this string template,
		IReadOnlyDictionary<string, string> values,
		Func<string, string?> fallback)
	{
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(values);
		ArgumentNullException.ThrowIfNull(fallback);

		return template._ResolveTemplate(key =>
			values.TryGetValue(key, out var value) ? value : fallback(key));
	}

	#endregion
}

