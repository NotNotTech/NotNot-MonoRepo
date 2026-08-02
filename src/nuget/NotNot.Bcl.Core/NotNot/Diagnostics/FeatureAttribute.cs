namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Enumerates the architectural roles a type can play within the ABMCS feature boundary system.
/// Used as the constructor argument to <see cref="FeatureAttribute"/> for unified feature-role
/// marking. The ordinal values (0, 1, 2) are matched by the Roslyn analyzers in
/// <c>NotNot.BlazorAnalyzers</c> via <c>ConstructorArguments[0].Value</c> — do NOT reorder
/// existing members.
/// </summary>
public enum FeatureRole
{
	/// <summary>
	/// Refit-attributed transport contract interface — the canonical Server-to-Client communication
	/// surface. Equivalent to the legacy <see cref="LdddDataServiceAttribute"/> marker. Detected by
	/// <c>NN_ABMCS_003</c> / <c>NN_LDDD_003</c> to allow <c>ComponentBase</c>-derived classes to
	/// <c>[Inject]</c> only types bearing this role (plus the framework allow-list).
	/// </summary>
	Contract = 0,

	/// <summary>
	/// Server-side business-logic type that owns domain compute, validation, or data processing.
	/// Equivalent to the legacy <see cref="LdddDomainServiceAttribute"/> marker. Detected by
	/// <c>NN_ABMCS_002</c> / <c>NN_LDDD_005</c> which fires when a <c>ComponentBase</c>-derived
	/// class directly invokes an instance method on a receiver whose type bears this role.
	/// </summary>
	ServerLogic = 1,

	/// <summary>
	/// Bypass marker that exempts a scope from the ABMCS / LiteDDD analyzer rule families.
	/// Equivalent to the legacy <see cref="LdddBypassAttribute"/> marker. Can be applied at
	/// assembly, class, struct, interface, or method scope to suppress <c>NN_ABMCS_*</c> and
	/// <c>NN_LDDD_*</c> diagnostics within the marked scope.
	/// </summary>
	Bypass = 2,
}

/// <summary>
/// Unified ABMCS feature-role marker attribute. Consolidates the former separate attributes
/// (<c>FeatureContractAttribute</c>, <c>FeatureServerLogicAttribute</c>,
/// <c>FeatureBypassAttribute</c>) into a single attribute with a <see cref="FeatureRole"/>
/// discriminator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection mechanism</b>: Marker attribute with no runtime behavior. The analyzers in
/// <c>NotNot.BlazorAnalyzers</c> detect this attribute by simple name
/// (<c>AttributeClass.Name == "FeatureAttribute"</c>) or fully-qualified name
/// (<c>NotNot.Bcl.Diagnostics.FeatureAttribute</c>), then inspect
/// <c>ConstructorArguments[0].Value</c> as an <c>int</c> to determine the
/// <see cref="FeatureRole"/> ordinal (0 = Contract, 1 = ServerLogic, 2 = Bypass). Consumers
/// may reference this attribute directly from <c>NotNot.Bcl.Core</c> or declare a local copy
/// with the same simple name and enum shape — the analyzer treats both equivalently.
/// </para>
/// <para>
/// <b>Legacy compatibility</b>: The legacy <see cref="LdddDataServiceAttribute"/>,
/// <see cref="LdddDomainServiceAttribute"/>, and <see cref="LdddBypassAttribute"/> remain
/// functional. The analyzers recognize all legacy simple names alongside this unified attribute.
/// Migration from legacy attributes is incremental per ABMCS architecture doc section 1.4.
/// </para>
/// <para>
/// <b>Usage examples</b>:
/// <code>
/// // Transport contract (replaces [FeatureContract] / [LdddDataService])
/// [Feature(FeatureRole.Contract)]
/// public interface IOrderDataService { ... }
///
/// // Server logic (replaces [FeatureServerLogic] / [LdddDomainService])
/// [Feature(FeatureRole.ServerLogic)]
/// public class OrderService { ... }
///
/// // Assembly-level bypass (replaces [FeatureBypass] / [LdddBypass])
/// [assembly: Feature(FeatureRole.Bypass)]
///
/// // Type-level bypass
/// [Feature(FeatureRole.Bypass)]
/// public class SampleComponent : ComponentBase { ... }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(
	AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Struct
	| AttributeTargets.Method | AttributeTargets.Assembly,
	AllowMultiple = false)]
public sealed class FeatureAttribute : Attribute
{
	/// <summary>The architectural role this attribute marks.</summary>
	public FeatureRole Role { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="FeatureAttribute"/> class with the specified
	/// architectural role.
	/// </summary>
	/// <param name="role">The ABMCS feature role (Contract, ServerLogic, or Bypass).</param>
	public FeatureAttribute(FeatureRole role) => Role = role;
}
