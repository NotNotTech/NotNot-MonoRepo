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

public static class zz_Extensions_JsonSerializerOptions
{
	/// <summary>
	/// Copies all settings from source JsonSerializerOptions to target
	/// </summary>
	/// <param name="target">The JsonSerializerOptions to copy settings to</param>
	/// <param name="source">The JsonSerializerOptions to copy settings from</param>
	public static void _CopyFrom(this JsonSerializerOptions target, JsonSerializerOptions source)
	{
		//VIBE_CRITICAL: Copy all JsonSerializerOptions settings for consistent serialization
		target.MaxDepth = source.MaxDepth;
		target.IncludeFields = source.IncludeFields;
		target.ReferenceHandler = source.ReferenceHandler;
		target.AllowTrailingCommas = source.AllowTrailingCommas;
		target.WriteIndented = source.WriteIndented;
		target.NumberHandling = source.NumberHandling;
		target.ReadCommentHandling = source.ReadCommentHandling;
		target.PropertyNameCaseInsensitive = source.PropertyNameCaseInsensitive;
		target.PropertyNamingPolicy = source.PropertyNamingPolicy;
		target.DefaultIgnoreCondition = source.DefaultIgnoreCondition;
		target.DefaultBufferSize = source.DefaultBufferSize;
		target.IgnoreNullValues = source.IgnoreNullValues;
		target.IgnoreReadOnlyProperties = source.IgnoreReadOnlyProperties;
		target.IgnoreReadOnlyFields = source.IgnoreReadOnlyFields;
		target.UnknownTypeHandling = source.UnknownTypeHandling;
		target.UnmappedMemberHandling = source.UnmappedMemberHandling;

		// Clear existing converters and copy from source
		target.Converters.Clear();
		foreach (var converter in source.Converters)
		{
			target.Converters.Add(converter);
		}

		// Copy other settings if available
		target.Encoder = source.Encoder;
		target.DictionaryKeyPolicy = source.DictionaryKeyPolicy;
		target.PreferredObjectCreationHandling = source.PreferredObjectCreationHandling;

		// CRITICAL: Must set TypeInfoResolver for ASP.NET Core serialization
		// Use default resolver if source doesn't have one
		target.TypeInfoResolver = source.TypeInfoResolver ?? JsonSerializerOptions.Default.TypeInfoResolver;
		// TypeInfoResolverChain is read-only, skip it
	}
}

