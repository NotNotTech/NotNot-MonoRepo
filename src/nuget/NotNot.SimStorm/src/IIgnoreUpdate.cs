namespace NotNot.SimStorm;

/// <summary>
///    apply this interface to SimNodes that you do not need to invoke the Update() method for.
///    This optimizes our execution engine so it does not need to asynchronously wait for an empty update method to execute
///    in the thread pool before invoking dependent nodes.
/// </summary>
public interface IIgnoreUpdate
{
}