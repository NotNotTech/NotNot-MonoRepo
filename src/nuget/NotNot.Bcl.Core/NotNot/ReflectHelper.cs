using System.Reflection;
using System.Runtime.CompilerServices;

namespace NotNot;

/// <summary>
///    high performance reflection helpers
/// </summary>
public class ReflectHelper
{
	//public static ReflectHelper Instance = new();

	/// <summary>
	///    return details about the callsite of the caller
	///    this is generated at build time, so no performance impact.
	/// </summary>
	public static (string MemberName, string FilePath, int LineNumber) GetCallerInfo([CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		return (memberName, sourceFilePath, sourceLineNumber);
	}

	// Extension method to check if a virtual method is overridden
	public static bool IsMethodOverridden(Object obj, string methodName)
	{
		// Get the type of the object
		Type objectType = obj.GetType();

		// Get the method info for the specified method name
		MethodInfo methodInfo = objectType.GetMethod(methodName);

		// If methodInfo is null, the method does not exist
		if (methodInfo == null)
		{
			throw new ArgumentException($"Method '{methodName}' not found on type '{objectType.FullName}'");
		}

		// Get the base method definition
		MethodInfo baseMethod = methodInfo.GetBaseDefinition();

		// Check if the method is overridden
		return methodInfo.DeclaringType != baseMethod.DeclaringType;
	}
}
