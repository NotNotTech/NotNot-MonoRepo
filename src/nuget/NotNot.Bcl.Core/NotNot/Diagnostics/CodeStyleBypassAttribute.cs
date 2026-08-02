namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Catch-all marker that exempts a scope from the NotNot code-style analyzer rule family
/// (<c>NN_C00X</c>). Currently covers <c>NN_C001</c> (ref-var must use <c>r_</c> prefix),
/// <c>NN_C002</c> (<c>r_</c> prefix must be ref), and <c>NN_C003</c> (boolean default must be
/// <c>false</c>). Any future analyzer added under <c>NotNot.Analyzers.Conventions</c> is
/// expected to honor this attribute as well — the bypass is per-family, not per-rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this bypasses</b>: the <c>NotNot.Analyzers</c> Roslyn analyzers that enforce
/// general code-style conventions (naming, default values, formatting-adjacent rules). Pairs
/// with <c>[LdddBypass]</c> (LiteDDD architecture family, <c>NN_LDDD_*</c>) and
/// <c>[AutoDiBypass]</c> (DI marker-enforcement family, <c>NN_DI_*</c>) — the project's
/// convention is one bypass attribute per analyzer family, named after the family's
/// semantic category rather than after individual rules.
/// </para>
/// <para>
/// <b>Scope of suppression</b>:
/// <list type="bullet">
///   <item>
///     <description>
///     <c>[assembly: CodeStyleBypass]</c> — entire compilation exempt from all
///     <c>NN_C00X</c> rules. Intended for legacy assemblies adopting NotNot.Analyzers
///     incrementally where wholesale adherence isn't yet practical, or for code-generation
///     output that intentionally diverges from hand-written conventions.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[CodeStyleBypass]</c> on a class/struct/interface — type-scope exempt. Intended
///     for individual types that intentionally diverge from a convention (e.g. interop
///     records whose member shape mirrors an external schema you can't rename, or generated
///     types that intentionally use default-<c>true</c> bool fields to match a wire
///     contract).
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[CodeStyleBypass]</c> on a method/property/field/parameter — member-scope exempt.
///     Intended for narrowly documented call-site exceptions (e.g. a serialization model
///     with a default-<c>true</c> <c>IsActive</c> boolean that mirrors an external schema
///     and cannot be renamed without breaking consumers).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Detection mechanism</b>: Analyzers in the <c>NN_C00X</c> family match this attribute
/// by fully-qualified name only — <c>NotNot.Bcl.Diagnostics.CodeStyleBypassAttribute</c>.
/// Consumer assemblies MUST reference this attribute directly from <c>NotNot.Bcl.Core</c>;
/// locally-declared <c>internal sealed class CodeStyleBypassAttribute</c> shadows in
/// unrelated namespaces are NOT honored. This mirrors the <c>[AutoDiBypass]</c> strictness
/// contract to prevent silent rule disabling via naming collision with an unrelated local
/// attribute, and intentionally diverges from the <c>[LdddBypass]</c> simple-name match
/// precedent (which exists for analyzer-only consumers that can't take a runtime dependency
/// on NotNot.Bcl.Core).
/// </para>
/// <para>
/// <b>When to prefer this attribute over editorconfig-level suppression</b>: <c>[CodeStyleBypass]</c>
/// makes the carve-out visible at the violation site, where future readers can see WHY a
/// convention was bypassed. <c>dotnet_diagnostic.NN_C003.severity = none</c> in
/// <c>.editorconfig</c> is coarser-grained (per-folder or per-project) and is appropriate
/// when a directory tree intentionally diverges from the convention (e.g. generated-code
/// output folders). Prefer the attribute for narrowly documented member-level exceptions;
/// prefer editorconfig for project-wide directional choices.
/// </para>
/// </remarks>
[AttributeUsage(
	AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct
	| AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property
	| AttributeTargets.Field | AttributeTargets.Parameter,
	AllowMultiple = false, Inherited = false)]
public sealed class CodeStyleBypassAttribute : Attribute
{
}
