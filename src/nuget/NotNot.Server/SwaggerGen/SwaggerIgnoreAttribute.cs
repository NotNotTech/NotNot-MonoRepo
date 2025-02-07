namespace NotNot.SwaggerGen;

/// <summary>
/// causes swagger to ignore this property when generating docs.
/// if that means the property's type is not used by any endpoints, then the schema for the type will be removed from the swagger doc.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class SwaggerIgnoreAttribute : Attribute
{
   // This class can remain empty as it's used as a marker
}