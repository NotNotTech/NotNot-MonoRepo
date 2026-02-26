using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
// using Newtonsoft.Json.Linq; // Removed - replaced with System.Text.Json

namespace NotNot.Serialization;

//public static class zz_Extensions_Object
//{
//	public record struct ToPoCoOptions(int MaxDepth = 10, bool IncludeFields = false,
//		bool IncludeRecursive = false,
//		bool IncludeNonPublic = false
//	);

//	public static object ToPoCo(this object obj, ToPoCoOptions options = default)
//	{
//		HashSet<object> visited = new();
//		return ToPoCo_Worker(obj, options, visited);
//	}
//	public static object ToPoCo_Worker(object obj, ToPoCoOptions options , HashSet<object> visited)
//	{
//		var type = obj.GetType();

//	}

//}

public class SerializationHelper
{


	private bool _isDisposed = false;
	/// <summary>
	/// dispose static resources _logJsonOptions and _roundtripJsonOptions
	/// <para>usually not needed, but some runtimes like Godot need this explicitly cleared out during lifecycle disposal for assembly unloading to work properly</para>
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed is true)
		{
			throw new ObjectDisposedException("SerializationHelper", "Dispose() already called");
			return;
		}
		_isDisposed = true;

		//if (SerializationHelper._logJsonOptions is not null)
		{
			foreach (var converter in _logJsonOptions.Converters)
			{
				if (converter is IDisposable disposable)
				{
					try
					{
						disposable.Dispose();
					}
#pragma warning disable NN_R005 // Disposal cleanup must continue despite individual converter failures
					catch (Exception ex)
					{
						Debug.WriteLine($"_JsonObjectConverters.Unloading() Error disposing converter {converter.GetType().Name}: {ex.Message}");
					}
#pragma warning restore NN_R005
				}
			}
			if (_logJsonOptions.IsReadOnly is false)
			{
				_logJsonOptions.Converters.Clear();
			}
			_logJsonOptions = null;
		}

		//if (SerializationHelper._roundtripJsonOptions is not null)
		{
			foreach (var converter in _roundtripJsonOptions.Converters)
			{
				if (converter is IDisposable disposable)
				{
					try
					{
						disposable.Dispose();
					}
#pragma warning disable NN_R005 // Disposal cleanup must continue despite individual converter failures
					catch (Exception ex)
					{
						Debug.WriteLine($"_JsonObjectConverters.Unloading() Error disposing converter {converter.GetType().Name}: {ex.Message}");
					}
#pragma warning restore NN_R005
				}
			}
			if (_roundtripJsonOptions.IsReadOnly is false)
			{
				_roundtripJsonOptions.Converters.Clear();
			}
			_roundtripJsonOptions = null;
		}
	}

	/// <summary>
	/// configure sane defaults for http json options (de)serializing post body
	/// </summary>
	/// <param name="options"></param>
	[Obsolete("use yourOptions._CopyFrom(__.SerializationHelper._roundtripJsonOptions), or _logJsonOptions directly",true)]
	public void ConfigureJsonOptions(JsonSerializerOptions options)
	{
		throw __.placeholder.NotImplemented();

		////be forgiving in parsing user json
		//options.ReadCommentHandling = JsonCommentHandling.Skip;
		//options.AllowTrailingCommas = true;
		//options.PropertyNameCaseInsensitive = true;
		//options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		//options.MaxDepth = 10;
		//options.NumberHandling = JsonNumberHandling.Strict;
		//options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
		//options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement;
		//options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
		//options.WriteIndented = true;
		//var newConverters = new List<JsonConverter>
		//{
		//	new ObjConverter<MethodBase>(value => value.Name),
		//	new ObjConverter<Type>(value => value.FullName),
		//	new ObjConverter<StackTrace>(value => value.GetFrames()),
		//	new ObjConverter<StackFrame>(value =>
		//		$"at {value.GetMethod().Name} in {value.GetFileName()}:{value.GetFileLineNumber()}"),
		//	//new ObjConverter<StackFrame>((value) => $"{value.ToString()}\n"),
		//};
		//foreach (var converter in newConverters)
		//{
		//	options.Converters.Add(converter);
		//}

	}

	// Removed ConfigureJsonOptions(JsonLoadSettings) - Newtonsoft-specific method


	//public SerializationHelper() { }

	//private static ILogger _logger = __.GetLogger<SerializationHelper>();

	/// <summary>
	/// how the serialization helper should convert objects to json (for use with logging, etc).
	/// <para>if you have a custom type that needs to be handled, add it to _jsonOptions.Converters at application start up.</para>
	/// <para>Deep object graphs are truncated at depth 10 with "[depth limit exceeded]" placeholder.</para>
	/// </summary>
	public JsonSerializerOptions _logJsonOptions = new()
	{
		MaxDepth = 64, // High limit - actual truncation handled by DepthTruncatingConverterFactory
		IncludeFields = true,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
		Converters =
		{
			//new ObjConverter<Exception>(value => $"EX={value.GetType().Name}_MSG={value.Message}_INNER={value.InnerException?.Message}"),
			new ObjConverter<MethodBase>(value => value.Name),
			new ObjConverter<Type>(value => value.FullName),
			new ObjConverter<StackTrace>(value => value.GetFrames()),
			new ObjConverter<IntPtr>(value => value.ToInt64().ToString("x8")),
			new ObjConverter<StackFrame>(value =>
				$"at {value.GetMethod().Name} in {value.GetFileName()}:{value.GetFileLineNumber()}"),
			//new ObjConverter<StackFrame>((value) => $"{value.ToString()}\n"),
			new ObjConverter<Delegate>(value => $"[delegate: {value.Method?.DeclaringType?.Name}.{value.Method?.Name}]"),
			// Must be LAST - gracefully truncates deep graphs instead of throwing
			new DepthTruncatingConverterFactory(maxDepth: 10),
		},
		AllowTrailingCommas = true,
		WriteIndented = true,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
	};

	/// <summary>
	/// our general, standard way of serializing to/from JSON. 
	/// <para>if you have a custom type that needs to be handled, add it to _roundtripJsonOptions.Converters at application start up.</para>
	/// <para>use the jsonSerializerOptions _CopyFrom() extension method to copy this to your existing options, eg: `yourOptions._CopyFrom(__.SerializationHelper._roundtripJsonOptions)`</para>
	/// </summary>
	public JsonSerializerOptions _roundtripJsonOptions = new()
	{
		

		Converters = {
			new CaseInsensitiveEnumConverter(),
			new NumberHandlingConverter(),
			new JsonStringEnumConverter(),
			//VIBE_CRITICAL: Add Maybe converters for proper deserialization
			new MaybeNonGenericJsonConverter(),
			new MaybeJsonConverterFactory(),
			//VIBE_CRITICAL: these objects below do not get serialized properly, leading to .net crashes, so need to summarize them
			new ObjConverter<MethodBase>(value => value.Name),
			new ObjConverter<Type>(value => value.FullName),
			new ObjConverter<StackTrace>(value => value.GetFrames()),
			new ObjConverter<StackFrame>(value =>
				$"at {value.GetMethod().Name} in {value.GetFileName()}:{value.GetFileLineNumber()}"),

		},

		AllowTrailingCommas = true,
		WriteIndented = true,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,
		ReadCommentHandling = JsonCommentHandling.Skip,
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		MaxDepth = 10,
		IncludeFields = true,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
		

	};




	/// <summary>
	///    converts input object into a "plain old collection object", a nested Dictionary/List structure.
	/// <para>this is useful for logging only, not round-trip</para>
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public object ToLogPoCo(object obj)
	{
		try
		{
			//serialize/deserialize to unknown collection
			//this process is used because System.Text.Json can better serialize various types that would cause Newtonsoft to throw PlatformNotSupported exceptions
			//however deserialize to unknown collection is not supported in System.Text.Json so we have to use Newtonsoft to deserialize to Dictionary/Array, using our custom JsonHelper.DeserializeUnknownType() function
			{
				var serialized = JsonSerializer.Serialize(obj, _logJsonOptions);
				var deserialized = JsonToPoCo(serialized);
				return deserialized;
			}
		}

#pragma warning disable NN_R005 // Logging serialization final safety net — must never crash (greenfield-patterns.md)
		catch (Exception ex)
		{
			try
			{
				__.DebugAssertOnce(ex);
				__.GetLogger()._EzError(ex, "could not convert to PoCo due to json roundtrip error");
				return new Dictionary<string, object?>
				{
					["__serializationError"] = ex.Message,
					["__type"] = obj?.GetType().FullName,
					["__toString"] = obj?.ToString()
				};
			}
			catch
			{
				// Double-fault: return hardcoded fallback — zero allocations that could throw
				return new Dictionary<string, object?>
				{
					["__serializationError"] = "double-fault in ToLogPoCo"
				};
			}
		}
#pragma warning restore NN_R005
	}
	/// <summary>
	/// converts input object into a "plain old collection object", then to JSON string.
	/// <para>this is useful for logging only, not round-trip</para>
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public string ToJsonLog(object obj)
	{
		try
		{
			return JsonSerializer.Serialize(obj, _logJsonOptions);
		}

#pragma warning disable NN_R005 // Logging serialization final safety net — must never crash (greenfield-patterns.md)
		catch (Exception ex)
		{
			try
			{
				__.DebugAssertOnce(ex);
				__.GetLogger()._EzError(ex, "ToJsonLog serialization failed for {Type}", obj?.GetType().FullName);
				return JsonSerializer.Serialize(new
				{
					__serializationError = ex.Message,
					__type = obj?.GetType().FullName,
					__toString = obj?.ToString()
				});
			}
			catch
			{
				// Double-fault: hardcoded string fallback — zero allocations that could throw
				return "{\"__serializationError\":\"double-fault in ToJsonLog\"}";
			}
		}
#pragma warning restore NN_R005
	}
	public JsonDocument ToJsonLogDocument(object obj)
	{
		try
		{
			return JsonSerializer.SerializeToDocument(obj, _logJsonOptions);
		}

#pragma warning disable NN_R005 // Logging serialization final safety net — must never crash (greenfield-patterns.md)
		catch (Exception ex)
		{
			try
			{
				__.DebugAssertOnce(ex);
				__.GetLogger()._EzError(ex, "ToJsonLogDocument serialization failed for {Type}", obj?.GetType().FullName);
				return JsonSerializer.SerializeToDocument(new
				{
					__serializationError = ex.Message,
					__type = obj?.GetType().FullName,
					__toString = obj?.ToString()
				});
			}
			catch
			{
				// Double-fault: minimal pre-serialized fallback
				return JsonSerializer.SerializeToDocument(new { __serializationError = "double-fault in ToJsonLogDocument" });
			}
		}
#pragma warning restore NN_R005
	}

	/// <summary>
	/// round-trippable json serialization of an object.
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public string ToJson(object obj)
	{
		return JsonSerializer.Serialize(obj, _roundtripJsonOptions);
	}

	public JsonDocument ToJsonDocument(object obj)
	{
		return JsonSerializer.SerializeToDocument(obj, _roundtripJsonOptions);
	}

	/// <summary>
	///    deserialize json to a Dictionary/List hiearchy.  This is useful for logging json of "unknown" or hard-to-deserialize
	///    types.
	///    best used with objects serialized via System.Text.Json, using the ReferenceHandler = ReferenceHandler.IgnoreCycles
	///    option.
	///    adapted from this answer https://stackoverflow.com/a/19140420/1115220
	///    via
	///    https://stackoverflow.com/questions/5546142/how-do-i-use-json-net-to-deserialize-into-nested-recursive-dictionary-and-list
	/// </summary>
	public object JsonToPoCo(string json)
	{
		using var document = JsonDocument.Parse(json, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
			MaxDepth = 10
		});

		return JsonElementToPoCo(document.RootElement);
	}

	/// <summary>
	/// </summary>
	/// <param name="token"></param>
	/// <param name="discardMetaNodes">
	///    TRUE useful to remove metadata nodes (starting with $) if ReferenceHandler.Preserve
	///    option is used. not useful otherwise.
	/// </param>
	/// <returns></returns>
	private object JsonElementToPoCo(JsonElement element, bool discardMetaNodes = false)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				{
					if (discardMetaNodes == false)
					{
						// When discardMetaNodes is false, convert all properties to dictionary
						// without special handling - this matches the original behavior
						var dict = new Dictionary<string, object>();

						foreach (var prop in element.EnumerateObject())
						{
							dict[prop.Name] = JsonElementToPoCo(prop.Value, discardMetaNodes);
						}

						return dict;
					}
					else
					{
						// When discardMetaNodes is true, we have special handling for $values
						// and filter out $ prefixed properties
						var dict = new Dictionary<string, object>();

						foreach (var prop in element.EnumerateObject())
						{
							if (prop.Name == "$values")
							{
								// Special case: just return the value metadata node
								// This happens when ReferenceHandler.Preserve is used
								return JsonElementToPoCo(prop.Value, discardMetaNodes);
							}

							if (prop.Name.StartsWith("$"))
							{
								// Skip other metadata nodes
								continue;
							}

							dict[prop.Name] = JsonElementToPoCo(prop.Value, discardMetaNodes);
						}

						return dict;
					}
				}

			case JsonValueKind.Array:
				{
					var list = new List<object>();
					foreach (var item in element.EnumerateArray())
					{
						list.Add(JsonElementToPoCo(item, discardMetaNodes));
					}
					return list;
				}

			case JsonValueKind.String:
				return element.GetString();

			case JsonValueKind.Number:
				// Try to get the most appropriate numeric type
				if (element.TryGetInt32(out var intValue))
					return intValue;
				if (element.TryGetInt64(out var longValue))
					return longValue;
				if (element.TryGetDouble(out var doubleValue))
					return doubleValue;
				// Fallback to decimal for maximum precision
				return element.GetDecimal();

			case JsonValueKind.True:
				return true;

			case JsonValueKind.False:
				return false;

			case JsonValueKind.Null:
			case JsonValueKind.Undefined:
				return null;

			default:
				throw new NotSupportedException($"Unsupported JsonValueKind: {element.ValueKind}");
		}
	}

	/// <summary>
	/// Preprocess a JSON5 string to convert it to valid JSON for Microsoft's System.Text.Json deserializer.
	/// <para>use in conjunction with `DeserializeJson5` to fully conform to json5 spec.   see https://json5.org/</para>
	/// </summary>
	/// <param name="json5String">The JSON5 string to preprocess.</param>
	/// <returns>A valid JSON string.</returns>
	private string PreprocessJson5ToJson(string json5String)
	{
		// Handle multi-line strings with backslash-newline
		json5String = @"\\\r?\n\s*"._ToRegex().Replace(json5String, "");

		// Convert unquoted keys to quoted keys (supports ECMAScript 5.1 IdentifierName)
		json5String = @"(^|[{,\s])([$_\p{L}][$_\p{L}\p{Nd}]*)\s*:"._ToRegex().Replace(json5String, "$1\"$2\":");

		// Remove leading plus sign from numbers (simplified version)
		json5String = @"([^\w\.\-])\+(\d+(\.\d*)?([eE][+\-]?\d+)?)"._ToRegex().Replace(json5String, "$1$2");

		// Add leading zero to numbers starting with decimal point (simplified version)
		json5String = @"([^\w\.])\.(\d+([eE][+\-]?\d+)?)"._ToRegex().Replace(json5String, "$10.$2");

		// Add trailing zero to numbers ending with decimal point (simplified version without negative lookahead)
		json5String = @"(\d+)\.(\s|,|\}|\])"._ToRegex().Replace(json5String, "$1.0$2");

		// Convert hexadecimal numbers to decimal
		json5String = @"0x[0-9a-fA-F]+"._ToRegex().Replace(json5String, m => Convert.ToInt64(m.Value, 16).ToString());

		// Convert single-quoted strings to double-quoted strings, handling escapes (simplified version)
		json5String = @"'([^'\\]*(\\.[^'\\]*)*)'"._ToRegex().Replace(json5String, m => "\"" + m.Groups[1].Value.Replace("\"", "\\\"") + "\"");

		// Convert NaN, Infinity, -Infinity to strings without positive lookahead.  will be converted back to number by our `NumberHandlingConverter` converter in a later step
		json5String = @"([:,\s\[\{])\s*(NaN|Infinity|-Infinity)(\s|,|\]|\})"._ToRegex().Replace(json5String, m => m.Groups[1].Value + "\"" + m.Groups[2].Value + "\"" + m.Groups[3].Value);

		return json5String;

	}
	///// <summary>
	///// Preprocess a JSON5 string to convert it to valid JSON for Microsoft's System.Text.Json deserializer.
	///// </summary>
	///// <param name="json5String">The JSON5 string to preprocess.</param>
	///// <returns>A valid JSON string.</returns>
	//public static string PreprocessJson5ToJson(string json5String)
	//{
	//	// Remove single-line comments (//...)  (not needed because json options can handle it)
	//	//json5String = Regex.Replace(json5String, @"//.*(?=\n|$)", string.Empty);

	//	// Remove multi-line comments (/*...*/)  (not needed because json options can handle it)
	//	//json5String = Regex.Replace(json5String, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

	//	// Convert unquoted keys to quoted keys
	//	//json5String = Regex.Replace(json5String, @"(?<=^|\{|,|\s)([$a-zA-Z_][a-zA-Z0-9_]*)(?=\s*:)", "\"$1\"");
	//	//json5String = @"(?<=^|\{|,|\s)([$a-zA-Z_][a-zA-Z0-9_]*)(?=\s*:)"._ToRegex().Replace(json5String, "\"$1\""); //optimized
	//	json5String = @"(^|[{,\s])([$a-zA-Z_][a-zA-Z0-9_]*)\s*:"._ToRegex().Replace(json5String, "$1\"$2\":");

	//	// Remove trailing commas (not needed because json options can handle it)
	//	//json5String = Regex.Replace(json5String, @",(?=\s*[}\]])", string.Empty);

	//	// Convert single-quoted values to double-quoted values
	//	//json5String = Regex.Replace(json5String, @"'([^']*)'", "\"$1\""); 
	//	json5String = @"'([^']*)'"._ToRegex().Replace(json5String, "\"$1\"");


	//	return json5String;
	//}

	/// <summary>
	/// deserialize a json file using json5, which is less strict about json formatting
	/// </summary>
	/// <typeparam name="TJsonSerialized"></typeparam>
	/// <param name="json5ResFilePath"></param>
	/// <returns></returns>
	public TJsonSerialized DeserializeJson5<TJsonSerialized>(string json5String)
	{

		//dotnet, doesn't support unquoted keys
		var jsonString = PreprocessJson5ToJson(json5String);




		//var jsonString = FileAccess.Open(match, FileAccess.ModeFlags.Read).GetAsText();
		var jsonFile = JsonSerializer.Deserialize<TJsonSerialized>(jsonString, _roundtripJsonOptions);
		return jsonFile;

	}

}

/// <summary>
/// Custom converter to handle special number values like Infinity, -Infinity, and NaN.
/// </summary>
internal class NumberHandlingConverter : JsonConverter<double>
{
	public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			string value = reader.GetString();
			return value.ToLower() switch
			{
				"infinity" => double.PositiveInfinity,
				"-infinity" => double.NegativeInfinity,
				"nan" => double.NaN,
				_ => throw new JsonException($"Unable to convert \"{value}\" to a valid double.")
			};
		}
		else if (reader.TokenType == JsonTokenType.Number)
		{
			return reader.GetDouble();
		}
		throw new JsonException();
	}

	public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
	{
		if (double.IsPositiveInfinity(value))
		{
			writer.WriteStringValue("Infinity");
		}
		else if (double.IsNegativeInfinity(value))
		{
			writer.WriteStringValue("-Infinity");
		}
		else if (double.IsNaN(value))
		{
			writer.WriteStringValue("NaN");
		}
		else
		{
			writer.WriteNumberValue(value);
		}
	}
}

/// <summary>
/// Custom converter to handle case-insensitive deserialization of enums.
/// </summary>
internal class CaseInsensitiveEnumConverter : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
	{
		return typeToConvert.IsEnum;
	}

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		var converterType = typeof(CaseInsensitiveEnumConverter<>).MakeGenericType(typeToConvert);
		return (JsonConverter)Activator.CreateInstance(converterType);
	}
}

internal class CaseInsensitiveEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException();
		}

		string enumValue = reader.GetString();
		if (Enum.TryParse(enumValue, ignoreCase: true, out T result))
		{
			return result;
		}

		throw new JsonException($"Unable to convert \"{enumValue}\" to Enum \"{typeof(T)}\".");
	}

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString());
	}
}

/// <summary>
/// A JsonConverterFactory that wraps serialization to gracefully truncate deep object graphs
/// instead of throwing MaxDepth exceptions. Uses Utf8JsonWriter.CurrentDepth for tracking.
/// <para>Add as the LAST converter in the chain (lowest priority) so type-specific converters run first.</para>
/// </summary>
internal class DepthTruncatingConverterFactory : JsonConverterFactory
{
	private readonly int _maxDepth;
	private readonly string _truncationMessage;

	public DepthTruncatingConverterFactory(int maxDepth = 10, string truncationMessage = "[depth limit exceeded]")
	{
		_maxDepth = maxDepth;
		_truncationMessage = truncationMessage;
	}

	public override bool CanConvert(Type typeToConvert)
	{
		// Handle complex types that can cause deep nesting
		// Exclude primitives, strings, enums which don't need depth protection
		return !typeToConvert.IsPrimitive
			&& typeToConvert != typeof(string)
			&& typeToConvert != typeof(decimal)
			&& typeToConvert != typeof(DateTime)
			&& typeToConvert != typeof(DateTimeOffset)
			&& typeToConvert != typeof(Guid)
			&& typeToConvert != typeof(TimeSpan)
			&& !typeToConvert.IsEnum;
	}

	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		var converterType = typeof(DepthTruncatingConverter<>).MakeGenericType(typeToConvert);
		return (JsonConverter?)Activator.CreateInstance(converterType, _maxDepth, _truncationMessage);
	}

	private class DepthTruncatingConverter<T> : JsonConverter<T>
	{
		private readonly int _maxDepth;
		private readonly string _truncationMessage;

		public DepthTruncatingConverter(int maxDepth, string truncationMessage)
		{
			_maxDepth = maxDepth;
			_truncationMessage = truncationMessage;
		}

		public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			// For reading, delegate to default behavior
			// Create temporary options without this converter (depth irrelevant for reads)
			var tempOptions = CreateOptionsWithoutThisConverter(options, remainingDepth: 64);
			return JsonSerializer.Deserialize<T>(ref reader, tempOptions);
		}

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		{
			if (value is null)
			{
				writer.WriteNullValue();
				return;
			}

			// Check depth using the writer's CurrentDepth property
			if (writer.CurrentDepth >= _maxDepth)
			{
				// At max depth - write truncation message instead of recursing
				writer.WriteStringValue(_truncationMessage);
				return;
			}

			// Buffer-then-copy: serialize to a temp buffer first, then copy to the real writer
			// on success. This prevents partial writes from corrupting the real writer if a
			// custom converter throws NotSupportedException after beginning to write.
			// F6 fix: Carry forward remaining depth budget instead of fixed MaxDepth=64.
			// The temp writer starts at depth 0, so limit it to the budget remaining from the
			// original writer's perspective.
			var remainingDepth = Math.Max(1, _maxDepth - writer.CurrentDepth);
			var tempOptions = CreateOptionsWithoutThisConverter(options, remainingDepth);
			try
			{
				using var buffer = new System.IO.MemoryStream();
				using var tempWriter = new Utf8JsonWriter(buffer);
				JsonSerializer.Serialize(tempWriter, value, tempOptions);
				tempWriter.Flush();

				// Success — replay buffered JSON to real writer
				using var doc = JsonDocument.Parse(buffer.ToArray());
				doc.RootElement.WriteTo(writer);
			}
			catch (NotSupportedException)
			{
				// Type is not serializable (e.g., delegates, function pointers, unregistered complex types).
				// Temp buffer absorbed any partial writes — real writer is still clean.
				writer.WriteStringValue($"[non-serializable: {value.GetType().Name}]");
			}
			catch (JsonException)
			{
				// Depth overflow in buffer path — remaining budget was tight and STJ threw
				// "maximum depth exceeded" instead of NotSupportedException.
				// Temp buffer absorbed partial writes — real writer is still clean.
				writer.WriteStringValue(_truncationMessage);
			}
		}

		private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _baseOptionsCache = new();

		private static JsonSerializerOptions CreateOptionsWithoutThisConverter(JsonSerializerOptions options, int remainingDepth)
		{
			// Cache base options (converter list stripped) per parent options instance.
			// MaxDepth is set dynamically per call since remaining depth varies.
			var baseOptions = _baseOptionsCache.GetValue(options, static opts =>
			{
				var newOptions = new JsonSerializerOptions(opts);

				// Remove DepthTruncatingConverterFactory to avoid infinite recursion
				for (int i = newOptions.Converters.Count - 1; i >= 0; i--)
				{
					if (newOptions.Converters[i] is DepthTruncatingConverterFactory)
					{
						newOptions.Converters.RemoveAt(i);
					}
				}

				return newOptions;
			});

			// Clone from cached base and set dynamic MaxDepth for this call.
			// JsonSerializerOptions becomes read-only after first use, so we must
			// create a fresh copy to set per-call MaxDepth.
			var callOptions = new JsonSerializerOptions(baseOptions)
			{
				MaxDepth = remainingDepth
			};
			return callOptions;
		}
	}
}
