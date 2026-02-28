namespace NotNot.Serialization;

/// <summary>
/// Controls how properties and types are filtered during log serialization via <see cref="SerializationHelper._logJsonOptions"/>.
/// <para>Place on classes/structs to set defaults for all child properties, or on individual properties/fields to override.</para>
/// <para>Has no effect on <see cref="SerializationHelper._roundtripJsonOptions"/> — only log serialization is affected.</para>
/// </summary>
[AttributeUsage(
	AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field,
	AllowMultiple = false, Inherited = true)]
public class JsonLogFilterAttribute : Attribute
{
	/// <summary>
	/// When true, this property is excluded from log serialization entirely.
	/// </summary>
	public bool Exclude { get; set; }

	/// <summary>
	/// Regex pattern matched against property NAMES at every depth in the subtree.
	/// Matched properties and their entire subtrees are removed. Cascades recursively through child types.
	/// </summary>
	public string? ChildFilterExclude { get; set; }

	/// <summary>
	/// Regex pattern for scoped add-back: overrides only the immediate parent's <see cref="ChildFilterExclude"/>.
	/// Has no effect if the parent node was itself excluded.
	/// </summary>
	public string? ChildFilterInclude { get; set; }

	/// <summary>
	/// Number of items to keep from the start of a collection. Default: <see cref="int.MaxValue"/> (no truncation).
	/// Explicit opt-in required — set to a finite value to activate collection truncation.
	/// </summary>
	public int MaxCountStart { get; set; } = int.MaxValue;

	/// <summary>
	/// Number of items to keep from the end of a collection. Default: <see cref="int.MaxValue"/> (no truncation).
	/// Explicit opt-in required — set to a finite value to activate collection truncation.
	/// </summary>
	public int MaxCountEnd { get; set; } = int.MaxValue;

	/// <summary>
	/// Maximum string length before truncation with "..." suffix. Default: <see cref="int.MaxValue"/> (no truncation).
	/// </summary>
	public int MaxStringLength { get; set; } = int.MaxValue;
}
