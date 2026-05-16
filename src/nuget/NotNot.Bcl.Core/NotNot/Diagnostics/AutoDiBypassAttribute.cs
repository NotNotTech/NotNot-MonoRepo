namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marker that exempts a scope from the <c>NN_DI_*</c> dependency-injection marker-enforcement
/// analyzer rule family (<c>NN_DI_001</c> lifetime mismatch, <c>NN_DI_002</c> redundant
/// registration, <c>NN_DI_003</c> passthrough factory, <c>NN_DI_004</c> marker +
/// <c>IHostedService</c>). Mirrors the <c>LdddBypassAttribute</c> precedent — single bypass
/// marker that suppresses the entire analyzer rule family within the marked scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this bypasses</b>: the <c>NotNot.Analyzers</c> Roslyn analyzer family that
/// enforces the project's <c>IDi{Singleton,Scoped,Transient}Service</c> marker-interface
/// auto-registration convention (handled by
/// <c>zz_Extensions_IServiceCollection_DI.AutoRegisterDiServices</c> in this assembly).
/// The analyzer flags explicit <c>AddSingleton</c>/<c>AddScoped</c>/<c>AddTransient</c>
/// invocations that conflict with, duplicate, or are eligible for auto-registration.
/// </para>
/// <para>
/// <b>Scope of suppression</b>:
/// <list type="bullet">
///   <item>
///     <description>
///     <c>[assembly: AutoDiBypass]</c> — entire compilation exempt from all
///     <c>NN_DI_*</c> rules. Intended for bootstrap/composition-root assemblies whose
///     registration policy intentionally diverges from the marker-interface convention
///     (e.g. hosting projects with complex conditional registration patterns).
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[AutoDiBypass]</c> on a class/struct — type-scope exempt. Intended for
///     individual registration helpers or fixture types that intentionally use explicit
///     <c>Add{L}</c> calls despite implementing a marker interface (e.g. types registered
///     under multiple service contracts, or instances requiring custom factory construction).
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[AutoDiBypass]</c> on a method — method-scope exempt. Intended for narrowly
///     documented call-site exceptions (e.g. a single bootstrap method that performs
///     ordering-sensitive manual registration for diagnostic or testability reasons).
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>When to prefer this attribute over alternative carve-outs</b>: the bypass is
/// architectural intent recorded in source rather than a temporary workaround. For carve-outs
/// driven by stable false-positive patterns (interface-bridge factories, third-party types,
/// open generics, conditional registration), the analyzer already provides built-in skip
/// logic — no attribute needed. Reach for <c>[AutoDiBypass]</c> only when the carve-out is
/// genuinely architectural and the analyzer's built-in carve-outs do not match.
/// </para>
/// <para>
/// <b>Detection mechanism</b>: The analyzer matches by fully-qualified name only —
/// <c>NotNot.Bcl.Diagnostics.AutoDiBypassAttribute</c>. Consumer assemblies MUST reference
/// this attribute directly from <c>NotNot.Bcl.Core</c>; locally-declared
/// <c>internal sealed class AutoDiBypassAttribute</c> shadows in unrelated namespaces are
/// NOT honored. This diverges intentionally from the <c>[LdddBypassAttribute]</c> precedent
/// (which DOES allow simple-name match) because <c>AutoDiBypassAttribute</c> is brand-new
/// and has no consumer-side namespace precedent — accepting any simple-name match would
/// risk silent rule disabling by a collision with an unrelated local attribute (HIGH-impact-
/// when-it-happens). See Wave 1 H2 review finding for the full reasoning.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct
			  | AttributeTargets.Method,
				AllowMultiple = false, Inherited = false)]
public sealed class AutoDiBypassAttribute : Attribute
{
}
