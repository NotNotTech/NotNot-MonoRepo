namespace NotNot.Advanced;

/// <summary>
///    meta attribute to mark a class as thread safe.
/// </summary>
[AttributeUsage(
   AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method |
   AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Delegate,
   Inherited = false)]
public class ThreadSafeAttribute : Attribute
{
}