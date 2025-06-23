using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NotNot.Serialization;

/// <summary>
/// auto-add serialization helper for known (problematic) types
/// </summary>
internal static class _Initialize_SerializationHelper_ObjectConverters
{
	/// <summary>
	/// auto-add serialization helper for known (problematic) types
	/// </summary>
	[ModuleInitializer]
	public static void _Initialize()
	{

		//need to treat as string because potential loops
		NotNot.Serialization.SerializationHelper._logJsonOptions.Converters.Add(new NotNot.Serialization.ObjConverter<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>(value => value.ToString()));

	}
}
