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

public static class zz_Extensions_Boolean
{
	/// <summary>
	///    Converts the value of this instance to its equivalent string representation (either "Yes" or "No").
	/// </summary>
	/// <param name="boolean"></param>
	/// <returns>string</returns>
	public static string _ToString(this bool boolean, bool asYesNo)
	{
		if (asYesNo)
		{
			return boolean ? "Yes" : "No";
		}

		return boolean.ToString();
	}

	/// <summary>
	///    Converts the value in number format {1 , 0}.
	/// </summary>
	/// <param name="boolean"></param>
	/// <returns>int</returns>
	/// <example>
	///    <code>
	/// 		int result= default(bool).ToBinaryTypeNumber()
	/// 	</code>
	/// </example>
	/// <remarks>
	///    Contributed by Mohammad Rahman, http://mohammad-rahman.blogspot.com/
	/// </remarks>
	public static int _ToInt(this bool boolean)
	{
		return boolean ? 1 : 0;
	}
}

