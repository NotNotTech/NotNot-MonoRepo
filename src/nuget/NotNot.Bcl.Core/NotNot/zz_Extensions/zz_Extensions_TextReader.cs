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

/// <summary>
///    Extension methods for the TextReader class and its sub classes (StreamReader, StringReader)
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_TextReader
{
	/// <summary>
	///    The method provides an iterator through all lines of the text reader.
	/// </summary>
	/// <param name="reader">The text reader.</param>
	/// <returns>The iterator</returns>
	/// <example>
	///    <code>
	/// 		using(var reader = fileInfo.OpenText()) {
	/// 		foreach(var line in reader.IterateLines()) {
	/// 		// ...
	/// 		}
	/// 		}
	/// 	</code>
	/// </example>
	/// <remarks>
	///    Contributed by OlivierJ
	/// </remarks>
	public static IEnumerable<string> _IterateLines(this TextReader reader)
	{
		string? line = null;
		while ((line = reader.ReadLine()) != null)
			yield return line;
	}

	/// <summary>
	///    The method executes the passed delegate /lambda expression) for all lines of the text reader.
	/// </summary>
	/// <param name="reader">The text reader.</param>
	/// <param name="action">The action.</param>
	/// <example>
	///    <code>
	/// 		using(var reader = fileInfo.OpenText()) {
	/// 		reader.IterateLines(l => Console.WriteLine(l));
	/// 		}
	/// 	</code>
	/// </example>
	/// <remarks>
	///    Contributed by OlivierJ
	/// </remarks>
	public static void _IterateLines(this TextReader reader, Action<string> action)
	{
		foreach (var line in reader._IterateLines())
		{
			action(line);
		}
	}
}

