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

public static class zz_Extensions_Regex
{
	/// <summary>
	/// like normal regex.Match but returns null on match failure.  (normal regex returns a match with .Success=false)
	/// </summary>
	/// <param name="regex"></param>
	/// <param name="input"></param>
	/// <returns></returns>
	public static Match? _Match(this Regex regex, string input)
	{
		if (input is null)
		{
			return null;
		}

		var match = regex.Match(input);
		if (match.Success is false)
		{
			return null;
		}

		return match;
	}

	/// <summary>
	/// returns the first match result, or a default string if no match  noMatchDefault = ""
	/// </summary>
	/// <param name="regex"></param>
	/// <param name="input"></param>
	/// <param name="noMatchDefault"></param>
	/// <returns></returns>
	public static string _FirstMatch(this Regex regex, string input, string noMatchDefault = "")
	{
		var match = regex.Match(input);
		if (match.Success is false)
		{
			return noMatchDefault;
		}
		return match.Groups[1].Value;
	}




}

