namespace NotNot.Templating;

/// <summary>
/// The outcome of one <see cref="TemplateString.Resolve"/> call: the resolved text plus the keys the
/// resolver declined.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a <c>sealed class</c> and NOT a <c>record</c>: <see cref="UnresolvedKeys"/> is an
/// <see cref="IReadOnlyList{T}"/>, so compiler-generated structural equality would compare it by reference
/// while advertising value semantics.
/// </para>
/// <para>
/// The reported set makes the three consumer roles fall out of one method: RESOLVE reads
/// <see cref="Text"/>; VALIDATE gates on <c>UnresolvedKeys.Count &gt; 0</c>; ENUMERATE resolves with no
/// resolver at all, where every key is declined and the declined set IS the template's key set.
/// </para>
/// </remarks>
public sealed class TemplateResolveResult
{
	/// <summary>
	/// The resolved template text. Declined keys are left literal, so this is never lossy.
	/// </summary>
	public required string Text { get; init; }

	/// <summary>
	/// The keys the resolver DECLINED (callback returned null AND the dictionary missed), distinct and in
	/// source order; empty when every key resolved.
	/// </summary>
	/// <remarks>
	/// A key that resolved to a BLANK value is resolved, not declined — even inside a conditional group,
	/// where the blank merely drops the group. A <c>%key%</c>-shaped substring appearing inside a
	/// SUBSTITUTED value never lands here: substitution is single-pass, so values are not re-scanned.
	/// </remarks>
	public required IReadOnlyList<string> UnresolvedKeys { get; init; }
}
