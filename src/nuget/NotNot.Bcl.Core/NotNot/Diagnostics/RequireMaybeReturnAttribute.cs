namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Marks a class or method as requiring the Maybe return pattern for API endpoints.
/// When applied, the MaybeReturnContractAnalyzer will enforce that controller actions
/// return Maybe&lt;T&gt; types instead of throwing exceptions or returning nulls.
/// </summary>
/// <remarks>
/// This attribute supports the architectural pattern where API endpoints communicate
/// errors through the type system rather than exceptions. The analyzer will report
/// violations based on the configured severity level.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class RequireMaybeReturnAttribute : Attribute
{
	/// <summary>
	/// Gets or sets an optional diagnostic severity override for this specific usage.
	/// When null, the analyzer's default or .editorconfig-specified severity is used.
	/// </summary>
	public DiagnosticSeverity? Severity { get; set; }
}

/// <summary>
/// Diagnostic severity levels for analyzer rules.
/// </summary>
public enum DiagnosticSeverity
{
	/// <summary>
	/// Hidden diagnostics are not displayed to the user.
	/// </summary>
	Hidden = 0,

	/// <summary>
	/// Informational diagnostics provide suggestions or best practices.
	/// </summary>
	Info = 1,

	/// <summary>
	/// Warning diagnostics indicate potential issues that should be reviewed.
	/// </summary>
	Warning = 2,

	/// <summary>
	/// Error diagnostics indicate violations that must be fixed.
	/// </summary>
	Error = 3
}
