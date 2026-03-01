using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace NotNot.Serialization;

/// <summary>
/// Three-layer filtering system for <see cref="SerializationHelper._logJsonOptions"/>:
/// <list type="bullet">
/// <item>Layer 1: <see cref="Apply"/> — TypeInfoResolver modifier (static, cached per type by STJ)</item>
/// <item>Layer 2: <see cref="ApplyRecursiveFilters"/> — JsonNode DOM post-processing (runtime, per call)</item>
/// <item>Layer 3: <see cref="NeedsRecursiveFiltering"/> — attribute scanning + caching</item>
/// </list>
/// </summary>
public static class JsonLogFilterModifier
{
	// ═══════════════════════════════════════════════════════════════════
	// Layer 3: Caching infrastructure (declared first — used by Layers 1 & 2)
	// ═══════════════════════════════════════════════════════════════════
	// SINGLE-OPTIONS CONSTRAINT: _typeMetadataCache and _propTypeMapCache resolve JSON property
	// names via JsonSerializerOptions.GetTypeInfo(), making their values options-dependent.
	// All call sites MUST pass the same JsonSerializerOptions instance (_logJsonOptions).
	// Using these caches with a different options instance will return stale/incorrect results.
	// _needsRecursiveFilteringCache uses reflection only and is options-independent.

	private static readonly ConcurrentDictionary<Type, bool> _needsRecursiveFilteringCache = new();
	private static readonly ConcurrentDictionary<Type, JsonLogFilterTypeMetadata> _typeMetadataCache = new();
	private static readonly ConcurrentDictionary<Type, Dictionary<string, Type>?> _propTypeMapCache = new();

	private sealed class JsonLogFilterTypeMetadata
	{
		public JsonLogFilterAttribute? TypeAttribute { get; init; }
		public Dictionary<string, JsonLogFilterAttribute> PropertyAttributes { get; init; } = new();
	}

	/// <summary>
	/// Returns true if <paramref name="type"/> or any reachable property type in its object graph
	/// has a <see cref="JsonLogFilterAttribute.ChildFilterExclude"/> that requires Layer 2 DOM post-processing.
	/// Thread-safe, cached per type.
	/// </summary>
	public static bool NeedsRecursiveFiltering(Type type)
	{
		if (_needsRecursiveFilteringCache.TryGetValue(type, out var cached))
			return cached;

		var visited = new HashSet<Type>();
		var result = ScanForRecursiveFiltering(type, visited, maxDepth: 32);
		_needsRecursiveFilteringCache.TryAdd(type, result);
		return result;
	}

	private static bool ScanForRecursiveFiltering(Type type, HashSet<Type> visited, int maxDepth)
	{
		if (maxDepth <= 0)
			return false; // depth exhausted — safe default (no recursive filtering assumed)

		if (!visited.Add(type))
			return false; // cycle — already scanning this type

		// Skip simple types that can't carry attributes
		if (IsSimpleType(type))
			return false;

		// object/dynamic/interface — conservative: assume yes
		if (type == typeof(object) || type.IsInterface)
			return true;

		// Check class-level attribute
		var typeAttr = type.GetCustomAttribute<JsonLogFilterAttribute>();
		if (typeAttr?.ChildFilterExclude != null)
			return true;

		// Check property-level attributes and recurse into property types
		var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
		foreach (var prop in properties)
		{
			var propAttr = prop.GetCustomAttribute<JsonLogFilterAttribute>();
			if (propAttr?.ChildFilterExclude != null)
				return true;

			var propType = prop.PropertyType;
			// Unwrap collection element types for recursion
			var elementType = GetCollectionElementType(propType);
			if (elementType != null)
				propType = elementType;

			if (!IsSimpleType(propType) && ScanForRecursiveFiltering(propType, visited, maxDepth - 1))
				return true;
		}

		// Check field-level attributes (IncludeFields = true in _logJsonOptions)
		var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
		foreach (var field in fields)
		{
			var fieldAttr = field.GetCustomAttribute<JsonLogFilterAttribute>();
			if (fieldAttr?.ChildFilterExclude != null)
				return true;

			var fieldType = field.FieldType;
			var elementType = GetCollectionElementType(fieldType);
			if (elementType != null)
				fieldType = elementType;

			if (!IsSimpleType(fieldType) && ScanForRecursiveFiltering(fieldType, visited, maxDepth - 1))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Gets type metadata keyed by JSON serialized property names (not C# reflection names).
	/// This ensures property lookups match the JSON DOM property names used in Layer 2.
	/// </summary>
	private static JsonLogFilterTypeMetadata GetTypeMetadata(Type type, JsonSerializerOptions options)
	{
		return _typeMetadataCache.GetOrAdd(type, t =>
		{
			var typeAttr = t.GetCustomAttribute<JsonLogFilterAttribute>();
			var propAttrs = new Dictionary<string, JsonLogFilterAttribute>();

			// Build a reflection-name → JSON-name mapping from STJ metadata when available
			Dictionary<string, string>? reflectionToJsonName = null;
			if (options != null)
			{
				try
				{
					var typeInfo = options.GetTypeInfo(t);
					if (typeInfo.Kind == JsonTypeInfoKind.Object)
					{
						reflectionToJsonName = new Dictionary<string, string>();
						foreach (var pi in typeInfo.Properties)
						{
							// pi.Name = JSON serialized name; AttributeProvider gives us the MemberInfo
							if (pi.AttributeProvider is MemberInfo mi)
							{
								reflectionToJsonName[mi.Name] = pi.Name;
							}
						}
					}
				}
#pragma warning disable NN_R005 // GetTypeInfo can throw for types with custom converters
				catch { }
#pragma warning restore NN_R005
			}

			foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				var attr = prop.GetCustomAttribute<JsonLogFilterAttribute>();
				if (attr != null)
				{
					// Use JSON serialized name as key (falls back to reflection name if mapping unavailable)
					var jsonName = reflectionToJsonName != null && reflectionToJsonName.TryGetValue(prop.Name, out var jn) ? jn : prop.Name;
					propAttrs[jsonName] = attr;
				}
			}

			foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
			{
				var attr = field.GetCustomAttribute<JsonLogFilterAttribute>();
				if (attr != null)
				{
					var jsonName = reflectionToJsonName != null && reflectionToJsonName.TryGetValue(field.Name, out var jn) ? jn : field.Name;
					propAttrs[jsonName] = attr;
				}
			}

			return new JsonLogFilterTypeMetadata
			{
				TypeAttribute = typeAttr,
				PropertyAttributes = propAttrs,
			};
		});
	}

	private static bool IsSimpleType(Type type)
	{
		return type.IsPrimitive
			|| type == typeof(string)
			|| type == typeof(decimal)
			|| type == typeof(DateTime)
			|| type == typeof(DateTimeOffset)
			|| type == typeof(Guid)
			|| type == typeof(TimeSpan)
			|| type.IsEnum;
	}

	private static Type? GetCollectionElementType(Type type)
	{
		if (type.IsArray)
			return type.GetElementType();

		if (type.IsGenericType)
		{
			var genDef = type.GetGenericTypeDefinition();
			// List<T>, IList<T>, ICollection<T>, IEnumerable<T>
			if (genDef == typeof(List<>)
				|| genDef == typeof(IList<>)
				|| genDef == typeof(ICollection<>)
				|| genDef == typeof(IEnumerable<>)
				|| genDef == typeof(IReadOnlyList<>)
				|| genDef == typeof(IReadOnlyCollection<>))
			{
				return type.GetGenericArguments()[0];
			}
		}

		// Handle non-generic classes inheriting from generic collections
		// e.g., "class MyCollection : List<int>" — inspect interfaces for IEnumerable<T>
		foreach (var iface in type.GetInterfaces())
		{
			if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			{
				return iface.GetGenericArguments()[0];
			}
		}

		return null;
	}

	private static Type? GetDictionaryValueType(Type type)
	{
		// STJ serializes all dictionary keys as JSON string properties regardless of TKey type,
		// so we detect dictionaries regardless of key type to avoid treating values as untyped.
		if (type.IsGenericType)
		{
			var genDef = type.GetGenericTypeDefinition();
			if (genDef == typeof(Dictionary<,>)
				|| genDef == typeof(IDictionary<,>)
				|| genDef == typeof(IReadOnlyDictionary<,>))
			{
				return type.GetGenericArguments()[1];
			}
		}

		// Check interfaces for types that implement IDictionary<TKey, TValue>
		foreach (var iface in type.GetInterfaces())
		{
			if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
			{
				return iface.GetGenericArguments()[1];
			}
		}

		return null;
	}

	private static Dictionary<string, Type>? GetPropertyTypeMap(Type declaredType, JsonSerializerOptions options)
	{
		if (IsSimpleType(declaredType) || declaredType == typeof(object))
			return null;

		return _propTypeMapCache.GetOrAdd(declaredType, t =>
		{
			try
			{
				var typeInfo = options.GetTypeInfo(t);
				if (typeInfo.Kind == JsonTypeInfoKind.Object)
				{
					var map = new Dictionary<string, Type>(typeInfo.Properties.Count);
					foreach (var pi in typeInfo.Properties)
					{
						map[pi.Name] = pi.PropertyType;
					}
					return map;
				}
			}
#pragma warning disable NN_R005 // GetTypeInfo can throw for types with custom converters — fall back gracefully to untyped DOM walk
			catch
			{
				// GetTypeInfo can throw for types with custom converters — fall back to no type info
			}
#pragma warning restore NN_R005
			return null;
		});
	}

	// ═══════════════════════════════════════════════════════════════════
	// Layer 1: TypeInfoResolver Modifier
	// ═══════════════════════════════════════════════════════════════════

	/// <summary>
	/// STJ TypeInfoResolver modifier. Handles direct (non-recursive) filtering:
	/// property exclusion, same-level ChildFilterExclude, string truncation, collection truncation.
	/// <para>Add to <c>DefaultJsonTypeInfoResolver.Modifiers</c>.</para>
	/// </summary>
	public static void Apply(JsonTypeInfo typeInfo)
	{
		if (typeInfo.Kind != JsonTypeInfoKind.Object)
			return;

		var typeAttr = typeInfo.Type.GetCustomAttribute<JsonLogFilterAttribute>();

		// No attribute on type and no property-level attributes → nothing to do
		// (We still need to check properties for property-level attributes even without a class-level one)

		// Collect properties to remove (iterate forward, remove after)
		var toRemove = new List<JsonPropertyInfo>();

		foreach (var prop in typeInfo.Properties)
		{
			// Get property-level attribute
			JsonLogFilterAttribute? propAttr = null;
			if (prop.AttributeProvider != null)
			{
				propAttr = prop.AttributeProvider.GetCustomAttributes(typeof(JsonLogFilterAttribute), true)
					.FirstOrDefault() as JsonLogFilterAttribute;
			}

			// Merge: property-level wins if present
			var effective = propAttr ?? typeAttr;

			if (effective == null)
				continue;

			// (a) Direct Exclude
			if (propAttr?.Exclude == true)
			{
				toRemove.Add(prop);
				continue;
			}

			// (b) Class-level ChildFilterExclude on direct children
			// Only applies when: class has ChildFilterExclude, property does NOT have its own [JsonLogFilter]
			if (typeAttr?.ChildFilterExclude != null && propAttr == null)
			{
				bool excluded = typeAttr.ChildFilterExclude._ToRegex().IsMatch(prop.Name);
				bool included = typeAttr.ChildFilterInclude != null
					&& typeAttr.ChildFilterInclude._ToRegex().IsMatch(prop.Name);

				if (excluded && !included)
				{
					toRemove.Add(prop);
					continue;
				}
			}

			// (c) String truncation
			if (effective.MaxStringLength < int.MaxValue && prop.PropertyType == typeof(string))
			{
				var maxLen = Math.Max(0, effective.MaxStringLength);
				var originalGet = prop.Get;
				if (originalGet != null)
				{
					prop.Get = (obj) =>
					{
						var value = originalGet(obj) as string;
						if (value != null && value.Length > maxLen)
							return value.Substring(0, maxLen) + "...";
						return value;
					};
				}
			}

			// (d) Collection truncation
			if (effective.MaxCountStart < int.MaxValue || effective.MaxCountEnd < int.MaxValue)
			{
				ApplyCollectionTruncation(prop, Math.Max(0, effective.MaxCountStart), Math.Max(0, effective.MaxCountEnd));
			}
		}

		foreach (var prop in toRemove)
		{
			typeInfo.Properties.Remove(prop);
		}
	}

	private static void ApplyCollectionTruncation(JsonPropertyInfo prop, int maxStart, int maxEnd)
	{
		var originalGet = prop.Get;
		if (originalGet == null)
			return;

		var propType = prop.PropertyType;

		// Determine if this is a truncatable collection type
		if (propType.IsArray)
		{
			var elementType = propType.GetElementType()!;
			prop.Get = (obj) =>
			{
				var value = originalGet(obj);
				if (value is not Array arr || arr.Length == 0)
					return value;
				return TruncateArray(arr, elementType, maxStart, maxEnd);
			};
		}
		else if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>))
		{
			var elementType = propType.GetGenericArguments()[0];
			prop.Get = (obj) =>
			{
				var value = originalGet(obj);
				if (value is not IList list || list.Count == 0)
					return value;
				return TruncateList(list, propType, elementType, maxStart, maxEnd);
			};
		}
		// For IList, ICollection etc. — we can't return the same concrete type safely,
		// so we skip truncation for non-concrete collection interfaces
	}

	private static object TruncateArray(Array arr, Type elementType, int maxStart, int maxEnd)
	{
		var total = arr.Length;
		// Clamp each side against total so int.MaxValue defaults don't prevent truncation
		maxStart = Math.Min(maxStart, total);
		maxEnd = Math.Min(maxEnd, total);
		if (maxStart + maxEnd >= total)
			return arr; // no truncation needed — show all

		var omitted = total - maxStart - maxEnd;

		// For string arrays, insert omission indicator
		if (elementType == typeof(string))
		{
			var result = new string[maxStart + 1 + maxEnd];
			Array.Copy(arr, 0, result, 0, maxStart);
			result[maxStart] = $"[...{omitted} items omitted...]";
			Array.Copy(arr, total - maxEnd, result, maxStart + 1, maxEnd);
			return result;
		}

		// For non-string arrays, truncate silently (can't insert indicator of wrong type)
		var resultLength = maxStart + maxEnd;
		var resultArr = Array.CreateInstance(elementType, resultLength);
		Array.Copy(arr, 0, resultArr, 0, maxStart);
		Array.Copy(arr, total - maxEnd, resultArr, maxStart, maxEnd);
		return resultArr;
	}

	private static object TruncateList(IList list, Type listType, Type elementType, int maxStart, int maxEnd)
	{
		var total = list.Count;
		// Clamp each side against total so int.MaxValue defaults don't prevent truncation
		maxStart = Math.Min(maxStart, total);
		maxEnd = Math.Min(maxEnd, total);
		if (maxStart + maxEnd >= total)
			return list; // no truncation needed — show all

		var omitted = total - maxStart - maxEnd;

		// Create new List<T> of the same generic type
		var newList = (IList)Activator.CreateInstance(listType)!;
		for (var i = 0; i < maxStart; i++)
			newList.Add(list[i]);

		// For string lists, insert omission indicator
		if (elementType == typeof(string))
			newList.Add($"[...{omitted} items omitted...]");

		for (var i = total - maxEnd; i < total; i++)
			newList.Add(list[i]);
		return newList;
	}

	// ═══════════════════════════════════════════════════════════════════
	// Layer 2: JsonNode DOM Post-Processing
	// ═══════════════════════════════════════════════════════════════════

	/// <summary>
	/// Walks a <see cref="JsonNode"/> tree and applies recursive <see cref="JsonLogFilterAttribute.ChildFilterExclude"/>
	/// patterns inherited from parent types. Call after <c>JsonSerializer.SerializeToNode</c> with <c>_logJsonOptions</c>.
	/// </summary>
	/// <param name="node">The root JsonNode (typically a JsonObject).</param>
	/// <param name="rootType">The CLR type of the serialized object.</param>
	/// <param name="logJsonOptions">The JsonSerializerOptions used for serialization (needed for type metadata lookup).</param>
	public static void ApplyRecursiveFilters(JsonNode? node, Type rootType, JsonSerializerOptions logJsonOptions)
	{
		if (node == null)
			return;

		var metadata = GetTypeMetadata(rootType, logJsonOptions);
		Regex? rootExclude = metadata.TypeAttribute?.ChildFilterExclude?._ToRegex();
		Regex? rootInclude = metadata.TypeAttribute?.ChildFilterInclude?._ToRegex();

		WalkNode(node, rootType, rootExclude, rootInclude, logJsonOptions);
	}

	private static void WalkNode(
		JsonNode? node,
		Type declaredType,
		Regex? inheritedExclude,
		Regex? inheritedInclude,
		JsonSerializerOptions options)
	{
		if (node == null)
			return;

		if (node is JsonObject jsonObj)
		{
			WalkJsonObject(jsonObj, declaredType, inheritedExclude, inheritedInclude, options);
		}
		else if (node is JsonArray jsonArr)
		{
			// Recurse into array elements with the collection's element type
			var elementType = GetCollectionElementType(declaredType) ?? typeof(object);
			foreach (var element in jsonArr)
			{
				WalkNode(element, elementType, inheritedExclude, inheritedInclude, options);
			}
		}
		// JsonValue — leaf node, nothing to do
	}

	private static void WalkJsonObject(
		JsonObject jsonObj,
		Type declaredType,
		Regex? inheritedExclude,
		Regex? inheritedInclude,
		JsonSerializerOptions options)
	{
		// Dictionary<string, T> — keys are data, NOT property names
		var dictValueType = GetDictionaryValueType(declaredType);
		if (dictValueType != null)
		{
			// Recurse into values only, do NOT filter by key names
			foreach (var kvp in jsonObj.ToList())
			{
				WalkNode(kvp.Value, dictValueType, inheritedExclude, null, options);
			}
			return;
		}

		// Build property name → Type lookup from STJ metadata (cached per type)
		var propTypeMap = GetPropertyTypeMap(declaredType, options);

		// Get declaring type's metadata for property-level override checks
		var declaredMetadata = GetTypeMetadata(declaredType, options);

		// Collect properties to remove
		var toRemove = new List<string>();

		foreach (var kvp in jsonObj.ToList())
		{
			var propName = kvp.Key;

			// Check inherited exclude — but respect property-level [JsonLogFilter] overrides
			if (inheritedExclude != null && inheritedExclude.IsMatch(propName))
			{
				// Property-level attribute = override: skip inherited exclude unless it explicitly sets Exclude=true
				if (declaredMetadata.PropertyAttributes.TryGetValue(propName, out var propOverride))
				{
					if (propOverride.Exclude)
					{
						toRemove.Add(propName);
						continue;
					}
					// Property has its own [JsonLogFilter] without Exclude=true — it overrides the inherited exclude
				}
				// Check scoped include (immediate parent only)
				else if (inheritedInclude == null || !inheritedInclude.IsMatch(propName))
				{
					toRemove.Add(propName);
					continue;
				}
			}

			// Resolve child type for recursion
			Type childType = typeof(object);
			if (propTypeMap != null && propTypeMap.TryGetValue(propName, out var resolved))
			{
				childType = resolved;
			}

			// Get child type's own metadata for its ChildFilterExclude
			var childMetadata = GetTypeMetadata(childType, options);
			Regex? childExclude = childMetadata.TypeAttribute?.ChildFilterExclude?._ToRegex();
			Regex? childInclude = childMetadata.TypeAttribute?.ChildFilterInclude?._ToRegex();

			// Also check property-level [JsonLogFilter] on the DECLARING type for this property
			// Property-level ChildFilterExclude/ChildFilterInclude applies to the subtree under that property
			if (declaredMetadata.PropertyAttributes.TryGetValue(propName, out var propLevelAttr))
			{
				if (propLevelAttr.ChildFilterExclude != null)
					childExclude = MergeRegex(childExclude, propLevelAttr.ChildFilterExclude._ToRegex());
				if (propLevelAttr.ChildFilterInclude != null)
					childInclude = MergeRegex(childInclude, propLevelAttr.ChildFilterInclude._ToRegex());
			}

			// Merge inherited exclude with child's own exclude + property-level exclude
			// Inherited cascades; child's own and property-level add to it
			Regex? mergedExclude = MergeRegex(inheritedExclude, childExclude);
			// Include is scoped to immediate parent only — use child's own + property-level include, not inherited
			Regex? mergedInclude = childInclude;

			// Recurse
			WalkNode(kvp.Value, childType, mergedExclude, mergedInclude, options);
		}

		foreach (var name in toRemove)
		{
			jsonObj.Remove(name);
		}
	}

	private static Regex? MergeRegex(Regex? a, Regex? b)
	{
		if (a == null) return b;
		if (b == null) return a;
		// Combine patterns with alternation
		var combined = $"(?:{a})|(?:{b})";
		return combined._ToRegex();
	}
}
