namespace NotNot.Bcl.Diagnostics;

/// <summary>
/// Excludes a controller or method from Maybe return pattern enforcement.
/// Use sparingly for legacy compatibility or special cases only.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class MaybeReturnNotRequiredAttribute : Attribute
{
}
