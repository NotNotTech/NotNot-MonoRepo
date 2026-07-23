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
	// ACCEPTED_BY_DESIGN CA2255: intentionally auto-registers the ObjConverter set at module
	// load; a redesign to explicit initialization is out of scope.
#pragma warning disable CA2255
	[ModuleInitializer]
#pragma warning restore CA2255
	public static void _Initialize()
	{

		//need to treat as string because potential loops
		__.SerializationHelper._logJsonOptions.Converters.Add(new NotNot.Serialization.ObjConverter<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>(value => value.ToString()));

	}
}
