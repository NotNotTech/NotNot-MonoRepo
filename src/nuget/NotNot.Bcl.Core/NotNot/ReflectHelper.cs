using System.Collections.Concurrent;
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
	public static (string MemberName, string FilePath, int LineNumber) GetCallerInfo([CallerMemberName] string callerMemberName = "",
		[CallerFilePath] string callerFilePath = "",
		[CallerLineNumber] int callerLineNumber = 0)
	{
		return (callerMemberName, callerFilePath, callerLineNumber);
	}

	/// <summary>
	/// return callerInfo formatted "{callerMemberName}|{callerFilePath}:{callerLineNumber}"
	/// </summary>
	/// <param name="callerMemberName"></param>
	/// <param name="callerFilePath"></param>
	/// <param name="callerLineNumber"></param>
	/// <returns></returns>
	public static string GetCallerInfoString([CallerMemberName] string callerMemberName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
	{
		return $"{callerMemberName}|{callerFilePath}:{callerLineNumber}";
	}

	/// <summary>
	/// helper for injecting a short unique id for a callsite, plus the callsite info.  output is `"XRAYID|METHOD|CALLSITE"`.   `  eg:  "MF.42|MyMethod|C:\Path\To\MyFile.cs:42"
	/// </summary>
	public static string XrayId([CallerMemberName] string callerMemberName = "", [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
	{
		var callsite = GetCallerInfoString(callerMemberName, callerFilePath, callerLineNumber);
		if (!_xrayIdCache.TryGetValue(callerFilePath, out var xrayId))
		{
			//this filepath doesn't have an xrayId yet, generate one
			lock (_xrayIdCache)
			{
				// generate a short unique id for this callsite
				var fileAcronym = Path.GetFileNameWithoutExtension(callerFilePath)._ToAcronym();

				//get/create the xrayId for this file
				{

					//make sure not already used, loop until unique
					int? attempts = null;
					while (true)
					{
						var xrayIdToTry = attempts == null ? fileAcronym : $"{fileAcronym}{attempts}";
						//in addition to callerFilePath --> xrayId mapping, we also store xrayId --> callerFilePath mapping to detect collisions
						if (_xrayIdCache.TryGetValue(xrayIdToTry, out var existingFilePath))
						{
							if (existingFilePath != callerFilePath)
							{
								//collision, add suffix
								if (attempts == null)
								{
									attempts = 1;
								}
								else
								{
									attempts++;
								}
								continue;
							}
							else
							{
								//same file, use it, as some other callsite raced us to add it
								xrayId = xrayIdToTry;
								break;
							}
						}
						else
						{
							//found a "slot" not used, use it for this mapping
							xrayId = xrayIdToTry;
							//store both mappings
							_xrayIdCache[xrayIdToTry] = callerFilePath; // xrayId --> callerFilePath
							_xrayIdCache[callerFilePath] = xrayIdToTry; // callerFilePath --> xrayId
							break;
						}
					}

				}


			}
		}
		//now, xrayId is mapped to callerFilePath
		return $"{xrayId}.{callerLineNumber}|{callsite}";

	}

	private static ConcurrentDictionary<string, string> _xrayIdCache = new();


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
