using Microsoft.OpenApi.Any;
using Newtonsoft.Json.Linq;

namespace NotNot.SwaggerGen.Advanced;

/// <summary>
/// helper needed to convert json into openApi objects
/// </summary>
public static class Converter
{
   public static OpenApiObject JsonToOpenApiObject(string jsonString)
   {
      var jObject = JObject.Parse(jsonString);
      return ConvertJObjectToOpenApiObject(jObject);
   }

   private static OpenApiObject ConvertJObjectToOpenApiObject(JObject jObject)
   {
      var openApiObject = new OpenApiObject();
      foreach (var property in jObject)
      {
         openApiObject.Add(property.Key, ConvertJTokenToOpenApiAny(property.Value));
      }
      return openApiObject;
   }

   private static IOpenApiAny ConvertJTokenToOpenApiAny(JToken token)
   {
      switch (token.Type)
      {
         case JTokenType.Object:
            return ConvertJObjectToOpenApiObject(token as JObject);
         case JTokenType.Array:
            return ConvertJArrayToOpenApiArray(token as JArray);
         case JTokenType.String:
            return new OpenApiString(token.ToString());
         case JTokenType.Integer:
            return new OpenApiInteger((int)token);
         case JTokenType.Float:
            return new OpenApiFloat((float)token);
         case JTokenType.Boolean:
            return new OpenApiBoolean((bool)token);
         case JTokenType.Null:
            return new OpenApiNull();
         default:
            return new OpenApiString(token.ToString());
      }
   }

   private static OpenApiArray ConvertJArrayToOpenApiArray(JArray jArray)
   {
      var openApiArray = new OpenApiArray();
      foreach (var item in jArray)
      {
         openApiArray.Add(ConvertJTokenToOpenApiAny(item));
      }
      return openApiArray;
   }
}