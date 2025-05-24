namespace NotNot.SwaggerGen;

/// <summary>
/// <para>apply to class to set the swagger example for that class.</para>
/// <para>apply to properties to set the swagger example for that property.  Important note: applying to object property will overwrite the entire schema for that object with the example.  Better to apply to the class instead to not overwrite the schema.</para>/// 
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class SwaggerExampleAttribute : Attribute
{


   /// <summary>
   /// pick the appropriate type for your example so that it renders properly in swaggerdocs
   /// <para>If not explicitly set, will be set to "Auto" which will attempt to choose the appropriate type based on the .Value parameter.   For Json strings you must set this to .Json</para>
   /// </summary>
   public SwaggerExampleType Type { get; set; } = SwaggerExampleType.Auto;

   /// <summary>
   /// if set, adds additional docs section in swagger
   /// </summary>
   public string? ExternalDocDescription { get; init; }
   /// <summary>
   /// if set, adds additional docs section in swagger
   /// </summary>
   public string? ExternalDocUrl { get; init; }

   /// <summary>
   /// set doc text for the schema
   /// </summary>
   public string? SchemaDocTitle { get; init; }

   /// <summary>
   /// set doc text for the schema
   /// </summary>
   public string? SchemaDocDescription { get; init; }
   /// <summary>
   /// set to an example value of type specified by the Type property
   /// </summary>
   public object? Value { get; init; }


}

public enum SwaggerExampleType
{
   Auto,
   String,
   Double,
   Bool,
   Json,
   Null,
   Int,
}
