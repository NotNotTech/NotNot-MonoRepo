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

public static class zz_Extensions_HashSet
{
	/// <summary>
	/// return a new hashset excluding the elements in the other collection
	/// </summary>
	public static HashSet<T> _Except<T>(this HashSet<T> set, IEnumerable<T> other)
	{
		var result = new HashSet<T>(set);
		result.ExceptWith(other);
		return result;
	}

	/// <summary>
	/// returns a new hashset of all elements in both collections
	/// </summary>
	public static HashSet<T> _Union<T>(this HashSet<T> set, IEnumerable<T> other)
	{
		var result = new HashSet<T>(set);
		result.UnionWith(other);
		return result;
	}

	/// <summary>
	/// returns a new hashset of common elements between the two collections
	/// </summary>
	public static HashSet<T> _Intersection<T>(this HashSet<T> set, IEnumerable<T> other)
	{
		var result = new HashSet<T>(set);
		result.IntersectWith(other);
		return result;
	}

	public static HashSet<T> _Copy<T>(this HashSet<T> set)
	{
		return new HashSet<T>(set);
	}
}


/// <summary>
/// Extension methods for HttpContent to validate and deserialize Maybe<T> responses
/// </summary>

