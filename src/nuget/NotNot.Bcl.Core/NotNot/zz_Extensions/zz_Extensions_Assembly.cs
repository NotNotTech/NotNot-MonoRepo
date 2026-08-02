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

public static class zz_Extensions_Assembly
{
	/// <summary>
	/// if you are using GitVersion, gets the sha shorthash (first 7 digits) from AssemblyInformationalVersionAttribute
	/// <para>otherwise returns "?"</para>
	/// </summary>
	public static string _GetGitShortHash(this Assembly assembly)
	{
		return assembly._GetGitHash()._Left(7);
	}
	/// <summary>
	/// if you are using GitVersion, gets the sha hash from AssemblyInformationalVersionAttribute
	/// <para>otherwise returns "?"</para>
	/// </summary>
	public static string _GetGitHash(this Assembly assembly)
	{
		// GetCustomAttribute returns null when the attribute is absent (no GitVersion): return "?" per contract.
		var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (string.IsNullOrWhiteSpace(info))
		{
			return "?";
		}

		var hash = @".*\.Sha\.([a-z\d]*).*"._ToRegex()._FirstMatch(info);
		if (string.IsNullOrWhiteSpace(hash))
		{
			return "?";
		}
		return hash;
	}
}

