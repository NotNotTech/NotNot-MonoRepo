namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marks the SINGLE type in an application that is allowed to read the inherited <c>PATH</c> environment
/// variable, exempting it from <c>NN_R011</c>. Apply it to the type that OWNS composing the live system
/// search path — never to a convenience caller that merely wants the diagnostic to go away.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the rule exists</b>: a process receives its environment from its parent at creation and never
/// re-reads it. A long-lived ancestor — Explorer, a service host, a shell started before an install —
/// therefore hands every descendant a stale <c>PATH</c> for that descendant's entire life, and on Windows
/// only a sign-out repairs it. Code that resolves an executable against that inherited value silently
/// stops finding tools that were installed later, or that live in a directory added to the USER path after
/// the ancestor started. The failure is invisible in code review because the code is correct in isolation.
/// </para>
/// <para>
/// <b>What the owner is for</b>: the marked type composes the CURRENT search path — on Windows by reading
/// the persistent machine and user stores via <c>EnvironmentVariableTarget.Machine</c> /
/// <c>EnvironmentVariableTarget.User</c>, which are current even when this process's copy is stale. The
/// inherited value remains legitimate there as a SOFT FALLBACK when the persistent store is unreadable
/// (a locked-down or sandboxed host), because a stale path still resolves machine-installed tools whereas
/// an empty one resolves nothing. That fallback read is exactly what this marker exempts.
/// </para>
/// <para>
/// <b>Detection mechanism</b>: <c>NN_R011</c> identifies this attribute by its simple name
/// (<c>SystemPathOwnerAttribute</c>) AND its containing namespace (<c>NotNot.Bcl.Diagnostics</c>), compared
/// as two separate ordinal string equalities rather than one rendered fully-qualified string — a single
/// rendered form varies by symbol kind and Roslyn version, and a mismatch there would fail OPEN, silently
/// ending the exemption. The namespace half is what keeps the match strict: a locally-declared shadow in an
/// unrelated namespace is NOT honored, so the rule cannot be disabled by a naming collision. This mirrors
/// the <c>[AutoDiScanAssembly]</c> / <c>[AutoDiBypass]</c> strictness contract. The comparison reads the
/// attribute symbol already attached to the marked type, so it needs no
/// <c>Compilation.GetTypeByMetadataName</c> lookup and works when this assembly is referenced but its
/// source is not part of the consumer compilation.
/// </para>
/// <para>
/// <b>Cardinality is the point</b>: this marker is meaningful only while exactly one type carries it. Two
/// marked types means two search-path authorities that can disagree, which is the condition the rule was
/// written to prevent. If a second type seems to need it, route that caller through the existing owner
/// instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SystemPathOwnerAttribute : Attribute
{
}
