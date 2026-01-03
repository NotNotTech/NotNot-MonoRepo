using System.Collections.Concurrent;
using System.Reflection;
using Namotion.Reflection;

namespace NotNot;

/// <summary>
/// Provides runtime access to XML documentation comments.
/// Uses Namotion.Reflection to read from compiler-generated XML doc files.
/// </summary>
/// <remarks>
/// <para>
/// Requires projects to have <c>&lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt;</c>.
/// Best for dev tooling where XML doc files are always present.
/// </para>
/// <para>
/// All methods use thread-safe caching for performance.
/// </para>
/// </remarks>
public static class XmlDocHelper
{
	// Thread-safe caches for property, field, and type descriptions
	private static readonly ConcurrentDictionary<PropertyInfo, string?> _propertyCache = new();
	private static readonly ConcurrentDictionary<FieldInfo, string?> _fieldCache = new();
	private static readonly ConcurrentDictionary<Type, string?> _typeCache = new();

	/// <summary>
	/// Gets the XML documentation summary for a property by name.
	/// </summary>
	/// <param name="type">The type containing the property.</param>
	/// <param name="propertyName">The property name.</param>
	/// <returns>The summary text, or null if not found or no documentation.</returns>
	public static string? _GetPropertySummary(this Type type, string propertyName)
	{
		var prop = type.GetProperty(propertyName);
		if (prop is null) return null;

		return _propertyCache.GetOrAdd(prop, static p =>
		{
			var summary = p.GetXmlDocsSummary();
			return string.IsNullOrWhiteSpace(summary) ? null : summary;
		});
	}

	/// <summary>
	/// Gets the XML documentation summary for a property by name.
	/// </summary>
	/// <typeparam name="T">The type containing the property.</typeparam>
	/// <param name="propertyName">The property name.</param>
	/// <returns>The summary text, or null if not found or no documentation.</returns>
	public static string? _GetPropertySummary<T>(string propertyName) =>
		typeof(T)._GetPropertySummary(propertyName);

	/// <summary>
	/// Gets the XML documentation summary for an enum value.
	/// </summary>
	/// <typeparam name="TEnum">The enum type.</typeparam>
	/// <param name="value">The enum value.</param>
	/// <returns>The summary text, or null if not found or no documentation.</returns>
	public static string? _GetEnumSummary<TEnum>(this TEnum value) where TEnum : struct, Enum
	{
		var field = typeof(TEnum).GetField(value.ToString()!);
		if (field is null) return null;

		return _fieldCache.GetOrAdd(field, static f =>
		{
			var summary = f.GetXmlDocsSummary();
			return string.IsNullOrWhiteSpace(summary) ? null : summary;
		});
	}

	/// <summary>
	/// Gets the XML documentation summary for a type (class, struct, record, interface).
	/// </summary>
	/// <param name="type">The type to get documentation for.</param>
	/// <returns>The summary text, or null if no documentation.</returns>
	public static string? _GetTypeSummary(this Type type)
	{
		return _typeCache.GetOrAdd(type, static t =>
		{
			var summary = t.GetXmlDocsSummary();
			return string.IsNullOrWhiteSpace(summary) ? null : summary;
		});
	}

	/// <summary>
	/// Gets the XML documentation summary for a type (class, struct, record, interface).
	/// </summary>
	/// <typeparam name="T">The type to get documentation for.</typeparam>
	/// <returns>The summary text, or null if no documentation.</returns>
	public static string? _GetTypeSummary<T>() =>
		typeof(T)._GetTypeSummary();

	/// <summary>
	/// Clears all cached XML documentation summaries.
	/// </summary>
	/// <remarks>
	/// Useful for testing or when assemblies are reloaded.
	/// </remarks>
	public static void ClearCache()
	{
		_propertyCache.Clear();
		_fieldCache.Clear();
		_typeCache.Clear();
	}
}
