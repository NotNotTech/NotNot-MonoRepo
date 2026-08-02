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

public static class zz_Extensions_HttpContent
{
	/// <summary>
	/// Validates that HttpContent can be deserialized as Maybe&lt;T&gt; and throws descriptive errors if not
	/// </summary>
	/// <typeparam name="T">The expected inner type of Maybe&lt;T&gt;</typeparam>
	/// <param name="content">The HTTP response content</param>
	/// <returns>The deserialized Maybe&lt;T&gt; if successful</returns>
	/// <exception cref="InvalidOperationException">Thrown with descriptive message if content cannot be deserialized as Maybe&lt;T&gt;</exception>
	public static async Task<Maybe<T>> _DeserializeMaybe<T>(this HttpContent content)
	{
		__.NotNull(content, "HttpContent cannot be null");

		try
		{
			// Check if content is empty
			var contentLength = content.Headers.ContentLength;
			if (contentLength == 0)
			{
				__.Throw("Content Empty - Expected Maybe<T> response but received empty content");
			}

			// Read content as string for debugging
			var contentString = await content.ReadAsStringAsync();
			if (string.IsNullOrWhiteSpace(contentString))
			{
				__.Throw("Content Empty - Expected Maybe<T> response but content string is null or whitespace");
			}

			// Attempt to deserialize as Maybe<T>
			try
			{
				// Reset the content stream position for JSON deserialization
				content.Headers.ContentType ??= new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

				//VIBE_CRITICAL: Use shared SerializationHelper options for consistent JSON handling
				var options = __.SerializationHelper._roundtripJsonOptions;

				var result = await content.ReadFromJsonAsync<Maybe<T>>(options);
				if (result == null)
				{
					__.Throw($"Content not Maybe<{typeof(T).Name}> - Deserialization returned null. Content is: {contentString}");
				}

				return result;
			}
			catch (System.Text.Json.JsonException jsonEx)
			{
				__.Throw($"Content not Maybe<{typeof(T).Name}> - JSON deserialization failed: {jsonEx.Message}. Content is: {contentString}");
			}
		}
		catch (Exception ex) when (ex.Message.StartsWith("Content not Maybe<") || ex.Message.StartsWith("Content Empty"))
		{
			// Re-throw our custom exceptions
			throw;
		}
#pragma warning disable NN_R005, NN_R006 // Re-throws via __.Throw() after building error message
		catch (Exception ex)
		{
			// Catch any other unexpected exceptions
			var contentText = "Unable to read content";
			try
			{
				contentText = await content.ReadAsStringAsync();
			}
			catch (Exception readEx)
			{
				// Best-effort second read while building a diagnostic message — the content may be
				// already-consumed/disposed. Observe-not-silence: surface the FULL read-failure
				// exception (type + message + stack) in the error text instead of dropping it
				// silently (fail-fast doctrine).
				contentText = $"Unable to read content: {readEx}";
			}

			__.Throw($"Content not Maybe<{typeof(T).Name}> - Unexpected error: {ex}. Content is: {contentText}");
		}
#pragma warning restore NN_R005, NN_R006

		// This should never be reached due to the throws above, but satisfies compiler
		throw new InvalidOperationException("Unreachable code");
	}

	public static async Task<Maybe> _DeserializeMaybe(this HttpContent content)
	{
		__.NotNull(content, "HttpContent cannot be null");

		try
		{
			// Check if content is empty
			var contentLength = content.Headers.ContentLength;
			if (contentLength == 0)
			{
				__.Throw("Content Empty - Expected Maybe<T> response but received empty content");
			}

			// Read content as string for debugging
			var contentString = await content.ReadAsStringAsync();
			if (string.IsNullOrWhiteSpace(contentString))
			{
				__.Throw("Content Empty - Expected Maybe<T> response but content string is null or whitespace");
			}

			// Attempt to deserialize as Maybe<T>
			try
			{
				// Reset the content stream position for JSON deserialization
				content.Headers.ContentType ??= new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

				//VIBE_CRITICAL: Use shared SerializationHelper options for consistent JSON handling
				var options = __.SerializationHelper._roundtripJsonOptions;

				var result = await content.ReadFromJsonAsync<Maybe>(options);
				if (result == null)
				{
					__.Throw($"Content not `Maybe` - Deserialization returned null. Content is: {contentString}");
				}

				return result;
			}
			catch (System.Text.Json.JsonException jsonEx)
			{
				__.Throw($"Content not `Maybe` - JSON deserialization failed: {jsonEx.Message}. Content is: {contentString}");
			}
		}
		catch (Exception ex) when (ex.Message.StartsWith("Content not `Maybe") || ex.Message.StartsWith("Content Empty"))
		{
			// Re-throw our custom exceptions
			throw;
		}
#pragma warning disable NN_R005, NN_R006 // Re-throws via __.Throw() after building error message
		catch (Exception ex)
		{
			// Catch any other unexpected exceptions
			var contentText = "Unable to read content";
			try
			{
				contentText = await content.ReadAsStringAsync();
			}
			catch (Exception readEx)
			{
				// Best-effort second read while building a diagnostic message — the content may be
				// already-consumed/disposed. Observe-not-silence: surface the FULL read-failure
				// exception (type + message + stack) in the error text instead of dropping it
				// silently (fail-fast doctrine).
				contentText = $"Unable to read content: {readEx}";
			}

			__.Throw($"Content not `Maybe` - Unexpected error: {ex}. Content is: {contentText}");
		}
#pragma warning restore NN_R005, NN_R006

		// This should never be reached due to the throws above, but satisfies compiler
		throw new InvalidOperationException("Unreachable code");
	}
}

