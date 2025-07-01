namespace NotNot.Data;

public enum OperationResult
{
	/// <summary>
	/// represents value not set, or perhaps error.
	/// <para>if used with a `Maybe` object, this result should never be used, as an error should instead be reported as a `Problem`</para>
	/// </summary>
	Error,
	/// <summary>
	/// The operation was successful
	/// </summary>
	Success,
}
