namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Explicit opt-in marker declaring that an assembly participates in dependency-injection
/// auto-registration — the <c>AddNotNotDiServices</c> runtime scan registers a type only when the
/// type's assembly carries this attribute, and the <c>NN_DI_007</c> analyzer rule fires only on a
/// hosted-service whose assembly carries it. Companion to <see cref="AutoDiBypassAttribute"/>: this
/// attribute OPTS an assembly IN to scanning; <c>[AutoDiBypass]</c> OPTS a scope OUT.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an explicit marker</b>: eligibility is declared in source, not inferred from an
/// assembly-name prefix. An assembly is a scan candidate iff-and-only-if it opts in here — there is
/// no name-family heuristic, no scan-all-minus-ignore-glob default, and no dual authority between
/// the runtime scanner and the analyzer. The two producer sites agree on ONE predicate: marker
/// presence.
/// </para>
/// <para>
/// <b>Precedence</b>: <see cref="AutoDiBypassAttribute"/> is HIGHER precedence. An assembly carrying
/// BOTH <c>[assembly: AutoDiScanAssembly]</c> and <c>[assembly: AutoDiBypass]</c> is EXCLUDED —
/// marked-but-bypassed still bypasses.
/// </para>
/// <para>
/// <b>Runtime scan behavior</b> (<c>zz_Extensions_IServiceCollection_DI.AddNotNotDiServices</c>):
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Default-AppDomain path</b> (no explicit <c>scanAssemblies</c> — scans
///     <c>AppDomain.CurrentDomain.GetAssemblies()</c>): unmarked assemblies are SILENTLY excluded.
///     The AppDomain is full of framework and third-party assemblies that legitimately have no
///     marker; throwing would be intractable.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Explicit-<c>scanAssemblies</c> path</b> (the caller passes a specific assembly list):
///     FAIL-FAST. Every explicitly-named assembly that lacks the marker is a caller error — the
///     scan throws with a message listing each unmarked assembly. A caller who names an assembly
///     expects it to be scanned; silently dropping it would hide a registration gap.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Analyzer scope</b>: <c>NN_DI_007</c> (explicit <c>AddHostedService&lt;T&gt;</c> duplicates the
/// auto-scan) fires only when <c>T</c>'s containing assembly carries this marker AND <c>T</c> is
/// public + concrete + implements <c>IHostedService</c> AND neither the type nor its assembly carries
/// <c>[AutoDiBypass]</c>. A hosted service in an unmarked assembly is never auto-scanned, so an
/// explicit registration there is legitimate and stays silent.
/// </para>
/// <para>
/// <b>Detection mechanism</b>: matched by fully-qualified name only —
/// <c>NotNot.Bcl.Diagnostics.AutoDiScanAssemblyAttribute</c>. Consumer assemblies MUST reference this
/// attribute directly from <c>NotNot.Bcl.Core</c>; a locally-declared
/// <c>AutoDiScanAssemblyAttribute</c> shadow in an unrelated namespace is NOT honored. This mirrors
/// <see cref="AutoDiBypassAttribute"/>'s FQN-only match — accepting any simple-name match would risk
/// silent scan-enabling (or, for the bypass twin, silent rule disabling) by a collision with an
/// unrelated local attribute.
/// </para>
/// <para>
/// <b>Placement</b>: annotate the assembly in its <c>Properties/AssemblyInfo.cs</c> (or a small
/// dedicated marker source file) with <c>[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]</c>.
/// Every assembly that CONTAINS a type expected to be auto-registered
/// (<c>IHostedService</c> / <c>IDi{Singleton,Scoped,Transient}Service</c> / <c>IDiAutoInitialize</c>)
/// must carry it — a missed marker on such an assembly is a silently-unregistered service. Marking is
/// about PARTICIPATING in the scan (having scanned types), not about CALLING it: a caller that invokes
/// the scan but declares no auto-registered types of its own needs no marker.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class AutoDiScanAssemblyAttribute : Attribute
{
}
