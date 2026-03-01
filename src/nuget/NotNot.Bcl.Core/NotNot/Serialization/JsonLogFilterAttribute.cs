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
	/// <para>Truncation requires BOTH sides to be set. To show only the first N items, also set
	/// <see cref="MaxCountEnd"/> to 0. Example: <c>[JsonLogFilter(MaxCountStart = 5, MaxCountEnd = 0)]</c>
	/// shows the first 5 items only. Setting only one side while the other remains <see cref="int.MaxValue"/>
	/// effectively means "keep all" and no truncation occurs.</para>
	/// <para>When <c>MaxCountStart + MaxCountEnd >= total</c>, all items are shown (no duplicates, no indicator).</para>
	/// </summary>
	public int MaxCountStart { get; set; } = int.MaxValue;

	/// <summary>
	/// Number of items to keep from the end of a collection. Default: <see cref="int.MaxValue"/> (no truncation).
	/// <para>Truncation requires BOTH sides to be set. To show only the last N items, also set
	/// <see cref="MaxCountStart"/> to 0. Example: <c>[JsonLogFilter(MaxCountStart = 0, MaxCountEnd = 10)]</c>
	/// shows the last 10 items only.</para>
	/// <para>When truncation occurs, omitted items are replaced with a <c>"[...N items omitted...]"</c>
	/// indicator (for string-typed collections only).</para>
	/// </summary>
	public int MaxCountEnd { get; set; } = int.MaxValue;

	/// <summary>
	/// Maximum string length before truncation with "..." suffix. Default: <see cref="int.MaxValue"/> (no truncation).
	/// </summary>
	public int MaxStringLength { get; set; } = int.MaxValue;
}
