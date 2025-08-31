using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;

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


	private static bool _isDisposed = false;
	/// <summary>
	/// dispose static resources _logJsonOptions and _roundtripJsonOptions
	/// <para>usually not needed, but some runtimes like Godot need this explicitly cleared out during lifecycle disposal for assembly unloading to work properly</para>
	/// </summary>
	public static void Dispose()
	{
		if (_isDisposed is true)
		{
			throw new ObjectDisposedException("SerializationHelper", "Dispose() already called");
			return;
		}
		_isDisposed = true;

		//if (SerializationHelper._logJsonOptions is not null)
		{
			foreach (var converter in SerializationHelper._logJsonOptions.Converters)
			{
				if (converter is IDisposable disposable)
				{
					try
					{
						disposable.Dispose();
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"_JsonObjectConverters.Unloading() Error disposing converter {converter.GetType().Name}: {ex.Message}");
					}
				}
			}
			SerializationHelper._logJsonOptions.Converters.Clear();
			SerializationHelper._logJsonOptions = null;
		}

		//if (SerializationHelper._roundtripJsonOptions is not null)
		{
			foreach (var converter in SerializationHelper._roundtripJsonOptions.Converters)
			{
				if (converter is IDisposable disposable)
				{
					try
					{
						disposable.Dispose();
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"_JsonObjectConverters.Unloading() Error disposing converter {converter.GetType().Name}: {ex.Message}");
					}
				}
			}
			SerializationHelper._roundtripJsonOptions.Converters.Clear();
			_roundtripJsonOptions = null;
		}
	}

	/// <summary>
	/// configure sane defaults for http json options (de)serializing post body
	/// </summary>
	/// <param name="options"></param>
	public static void ConfigureJsonOptions(JsonSerializerOptions options)
	{
		//be forgiving in parsing user json
		options.ReadCommentHandling = JsonCommentHandling.Skip;
		options.AllowTrailingCommas = true;
		options.PropertyNameCaseInsensitive = true;
		options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
		options.MaxDepth = 10;
		options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
		options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
		options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement;
		options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
		options.WriteIndented = true;
		var newConverters = new List<JsonConverter>
		{
			new ObjConverter<MethodBase>(value => value.Name),
			new ObjConverter<Type>(value => value.FullName),
			new ObjConverter<StackTrace>(value => value.GetFrames()),
			new ObjConverter<StackFrame>(value =>
				$"at {value.GetMethod().Name} in {value.GetFileName()}:{value.GetFileLineNumber()}"),
			//new ObjConverter<StackFrame>((value) => $"{value.ToString()}\n"),
		};
		foreach (var converter in newConverters)
		{
			options.Converters.Add(converter);
		}

	}

	public static void ConfigureJsonOptions(JsonLoadSettings options)
	{
		options.DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace;
		options.CommentHandling = CommentHandling.Ignore;
		options.LineInfoHandling = LineInfoHandling.Ignore;
	}


	private SerializationHelper() { }

	private static ILogger _logger = __.GetLogger<SerializationHelper>();

	/// <summary>
	/// how the serialization helper should convert objects to json (for use with logging, etc).
	/// <para>if you have a custom type that needs to be handled, add it to _jsonOptions.Converters at application start up.</para>
	/// </summary>
	public static JsonSerializerOptions _logJsonOptions = new()
	{
		MaxDepth = 10,
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
		},
		AllowTrailingCommas = true,
		WriteIndented = true,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
	};

	/// <summary>
	/// how the serialization helper should convert objects to json (for use with logging, etc).
	/// <para>if you have a custom type that needs to be handled, add it to _jsonOptions.Converters at application start up.</para>
	/// </summary>
	public static JsonSerializerOptions _roundtripJsonOptions = new()
	{
		MaxDepth = 10,
		IncludeFields = true,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,

		Converters = {
			new CaseInsensitiveEnumConverter(),
			new NumberHandlingConverter(),
			//VIBE_CRITICAL: Add Maybe converters for proper deserialization
			new MaybeNonGenericJsonConverter(),
			new MaybeJsonConverterFactory()
		},

		AllowTrailingCommas = true,
		WriteIndented = true,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.AllowReadingFromString,


		ReadCommentHandling = JsonCommentHandling.Skip,
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

	};




	/// <summary>
	///    converts input object into a "plain old collection object", a nested Dictionary/List structure.
	/// <para>this is useful for logging only, not round-trip</para>
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public static object ToLogPoCo(object obj)
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
		catch (Exception ex)
		{
			__.GetLogger()._EzError(ex, "could not convert to PoCo due to json roundtrip error");
			//throw new ApplicationException("could not convert to PoCo due to json roundtrip error", ex);
			throw;
		}
	}
	/// <summary>
	/// converts input object into a "plain old collection object", then to JSON string.
	/// <para>this is useful for logging only, not round-trip</para>
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public static string ToJsonLog(object obj)
	{
		//var roundTrip = ToLogPoCo(obj);
		return JsonSerializer.Serialize(obj, _logJsonOptions);
	}
	public static JsonDocument ToJsonLogDocument(object obj)
	{
		return JsonSerializer.SerializeToDocument(obj, _logJsonOptions);
	}

	/// <summary>
	/// round-trippable json serialization of an object.
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public static string ToJson(object obj)
	{
		return JsonSerializer.Serialize(obj, _roundtripJsonOptions);
	}

	public static JsonDocument ToJsonDocument(object obj)
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
	public static object JsonToPoCo(string json)
	{
		//Console.WriteLine($"DeserializeUnknownType {json}");
		return ToObject(JToken.Parse(json));
	}

	/// <summary>
	/// </summary>
	/// <param name="token"></param>
	/// <param name="discardMetaNodes">
	///    TRUE useful to remove metadata nodes (starting with $) if ReferenceHandler.Preserve
	///    option is used. not useful otherwise.
	/// </param>
	/// <returns></returns>
	private static object ToObject(JToken token, bool discardMetaNodes = false)
	{
		switch (token.Type)
		{
			// key/value node
			case JTokenType.Object:
				{
					if (discardMetaNodes == false)
					{
						return token.Children<JProperty>()
							.ToDictionary(prop => prop.Name,
								prop => ToObject(prop.Value, discardMetaNodes));
					}

					var dict = new Dictionary<string, object>();


					foreach (var prop in token.Children<JProperty>())
					{
						if (prop.Name == "$values")
						{
							//if (dict.Count > 0)
							//{
							//	throw new ApplicationException("assume no other value nodes if $values is present");
							//}

							//just return the value metdata node
							return ToObject(prop.Value, discardMetaNodes);
						}

						if (prop.Name.StartsWith("$"))
						{
							continue;
						}

						dict.Add(prop.Name, ToObject(prop.Value, discardMetaNodes));
					}

					return dict;
				}
			// array node
			case JTokenType.Array:
				{
					return token.Select(tok => ToObject(tok, discardMetaNodes)).ToList();
				}
			// simple node
			default:
				return ((JValue)token).Value;
		}
	}

	/// <summary>
	/// Preprocess a JSON5 string to convert it to valid JSON for Microsoft's System.Text.Json deserializer.
	/// <para>use in conjunction with `DeserializeJson5` to fully conform to json5 spec.   see https://json5.org/</para>
	/// </summary>
	/// <param name="json5String">The JSON5 string to preprocess.</param>
	/// <returns>A valid JSON string.</returns>
	private static string PreprocessJson5ToJson(string json5String)
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
	public static TJsonSerialized DeserializeJson5<TJsonSerialized>(string json5String)
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
