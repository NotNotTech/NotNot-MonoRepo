namespace NotNot.Diagnostics;

public static class ThreadDiag
{
	/// <summary>
	///    returns true if the object is locked by any thread (including the current thread)
	/// </summary>
	/// <remarks>
	///    due to limitations with `Monitor`, this code requires a lock to be taken when detecting.
	///    As such this should really only be used for #DEBUG conditional checks
	/// </remarks>
	public static bool IsLocked(object obj)
	{
		//this kind of wacky logic is required because 
		//threads can re-enter a lock they already own
		if (Monitor.IsEntered(obj))
		{
			return true;
		}

		if (Monitor.TryEnter(obj))
		{
			Monitor.Exit(obj);
			return false;
		}

		return true;
	}
}
