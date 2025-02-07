using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using NotNot.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NotNot.SwaggerGen.Advanced;

/// <summary>
/// Add custom SwaggerGen filters so you can decorate properties with [SwaggerIgnore] or [SwaggerExample] attributes
/// </summary>
public class SwaggerSchemaFilter_ApplyAttributes : ISchemaFilter
{
   //private static List<(string from,string toHide)> IgnoredTypes = new();
   public void Apply(OpenApiSchema schema, SchemaFilterContext context)
   {
      _ApplyExampleAttrib(schema, context);
      _ApplyIgnore(schema, context);
   }

   private void _ApplyIgnore(OpenApiSchema schema, SchemaFilterContext context)
   {
      //enumerate properties to try to apply ignore to each
      foreach (var propInfo in context.Type.GetProperties())
      {
         if (propInfo._TryGetAttribute<SwaggerIgnoreAttribute>(out var attrib))
         {
            var propertyName = propInfo.Name._ToLowerFirstLetter();
            var result = schema.Properties.Remove(propertyName);
            __.Assert(result);
         }
      }
   }


   private void _ApplyExampleAttrib(OpenApiSchema schema, SchemaFilterContext context)
   {
      {
         if (context.Type._TryGetAttribute<SwaggerExampleAttribute>(out var attrib))
         {
            _ApplyExampleAttrib_Helper(schema, attrib);
            //applied, overwriting entire schema with example so no need to enumerate properties (below)
            return;
         }
      }

      //enumerate properties to try to apply example to each
      foreach (var propInfo in context.Type.GetProperties())
      {
         if (propInfo._TryGetAttribute<SwaggerExampleAttribute>(out var attrib))
         {
            var propertyName = propInfo.Name._ToLowerFirstLetter();
            if (schema.Properties.TryGetValue(propertyName, out var propertySchema))
            {
               var oldRef = propertySchema.Reference ?? propertySchema.Items?.Reference;

               if (oldRef is not null)
               {
                  //this is a reference that needs to be removed so that our example can be shown

                  var oldRefId = oldRef.Id;
                  var isArray = propertySchema.Items is not null;

                  propertySchema.Reference = null;

                  var oldRefType = propInfo.PropertyType.Name + (isArray ? "[]" : "");
                  __.Assert(propInfo.PropertyType.Name == oldRefId, "why not equal?  investigate");
                  //add back some details so user can identify the schema
                  propertySchema.Title = $"See Schema for type '{oldRefType}'";
                  propertySchema.Type = oldRefType;
                  propertySchema.Description = $"See the Schema section below for the definition for '{oldRefType}'.";

                  //add to list of used schemas so that SwaggerDocumentFilter_RebuildSchema doesn't remove it
                  SwaggerOperationFilter_DiscoverUsedSchemas.usedSchemas.Add(oldRefId);
               }

               _ApplyExampleAttrib_Helper(propertySchema, attrib);
            }
            else
            {
               __.Assert(false, "property not found in schema, why?");
            }
         }
      }
   }


   private static void _ApplyExampleAttrib_Helper(OpenApiSchema schema, SwaggerExampleAttribute attrib)
   {
      if (attrib.Type == SwaggerExampleType.Auto)
      {
         if (attrib.Value is not null)
         {
            attrib.Type = attrib.Value switch
            {
               string _ => SwaggerExampleType.String,
               double _ => SwaggerExampleType.Double,
               bool _ => SwaggerExampleType.Bool,
               int _ => SwaggerExampleType.Int,
               _ => SwaggerExampleType.Json,
            };
         }
         else
         {
            attrib.Type = SwaggerExampleType.Null;
         }
      }



      switch (attrib.Type)
      {
         case SwaggerExampleType.String:
            schema.Example = new OpenApiString((string)attrib.Value!);
            break;
         case SwaggerExampleType.Double:
            schema.Example = new OpenApiDouble((double)attrib.Value!);
            break;
         case SwaggerExampleType.Bool:
            schema.Example = new OpenApiBoolean((bool)attrib.Value!);
            break;
         case SwaggerExampleType.Json:
            if (attrib.Value is string)
            {
               schema.Example = Converter.JsonToOpenApiObject((string)attrib.Value!);
            }
            else
            {
               var jsonSerialized = NotNot.Serialization.SerializationHelper.ToJson_Log(attrib.Value!);
               schema.Example = Converter.JsonToOpenApiObject(jsonSerialized);
            }

            break;
         case SwaggerExampleType.Null:
            schema.Example = new OpenApiNull();
            break;
         case SwaggerExampleType.Int:
            schema.Example = new OpenApiInteger((int)attrib.Value!);
            break;
         default:
            __.Assert(false, "unknown example type");
            break;
      }



      if (attrib.ExternalDocDescription is not null || attrib.ExternalDocUrl is not null)
      {
         schema.ExternalDocs = new OpenApiExternalDocs()
         {
            Description = attrib.ExternalDocDescription,
            Url = new Uri(attrib.ExternalDocUrl ?? "about:blank")
         };
      }
      if (attrib.SchemaDocTitle is not null)
      {
         schema.Title = attrib.SchemaDocTitle;
      }
      if (attrib.SchemaDocDescription is not null)
      {
         schema.Description = attrib.SchemaDocDescription;
      }
   }


}

/// <summary>
/// removes unused schemas, IE those removed from [SwaggerIgnore] attributes
/// </summary>
public class SwaggerDocumentFilter_RebuildSchema : IDocumentFilter
{
   public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
   {
      // Identify root schemas (for example, schemas used in endpoints)
      var rootSchemas = SwaggerOperationFilter_DiscoverUsedSchemas.usedSchemas;
      var rebuiltSchemas = new Dictionary<string, OpenApiSchema>();
      foreach (var id in rootSchemas)
      {
         _RebuildReferencedSchema(rebuiltSchemas, context.SchemaRepository.Schemas, id);
      }

      // Replace the context's schemas with the referenced schemas
      context.SchemaRepository.Schemas.Clear();
      foreach (var schema in rebuiltSchemas)
      {
         context.SchemaRepository.Schemas.Add(schema.Key, schema.Value);
      }
   }

   public void _RebuildReferencedSchema(Dictionary<string, OpenApiSchema> rebuilt, Dictionary<string, OpenApiSchema> originals, string schemaIdToAdd)
   {
      var toAdd = originals[schemaIdToAdd];
      if (!rebuilt.TryAdd(key: schemaIdToAdd, value: toAdd))
      {
         //already been added, so don't recurse to avoid circular refs
         return;
      }

      //walk all properties and recurse
      if (toAdd.Properties is not null)
      {
         foreach (var prop in toAdd.Properties)
         {
            //handle normal objects
            if (prop.Value.Reference is not null)
            {
               _RebuildReferencedSchema(rebuilt, originals, prop.Value.Reference.Id);
            }
            //handle arrays
            if (prop.Value.Items?.Reference is not null)
            {
               _RebuildReferencedSchema(rebuilt, originals, prop.Value.Items.Reference.Id);
            }
            //handle dictionaries
            if (prop.Value.AdditionalProperties?.Reference is not null)
            {
               _RebuildReferencedSchema(rebuilt, originals, prop.Value.AdditionalProperties.Reference.Id);
            }
         }
      }
   }
}


/// <summary>
/// parses endpoint request/response objects to find all used schemas
/// later these "usedSchemas" will be used by SwaggerDocumentFilter_RebuildSchema to remove unused schemas IE those removed from [SwaggerIgnore] attributes
/// </summary>
public class SwaggerOperationFilter_DiscoverUsedSchemas : IOperationFilter
{
   public static HashSet<string> usedSchemas = new HashSet<string>();

   public void Apply(OpenApiOperation operation, OperationFilterContext context)
   {
      // Inspect request parameters for schema usage
      foreach (var parameter in operation.Parameters)
      {
         if (parameter.Schema is not null)
         {
            AddUsedSchema(parameter.Schema);
         }
      }

      // inspect the request body for schema usage
      var requestBodySchemaRef = operation.RequestBody?.Content.Values
         .SelectMany(content => content.Schema?.Reference != null ? new[] { content.Schema.Reference.Id } : Enumerable.Empty<string>())
         .FirstOrDefault();

      if (!string.IsNullOrEmpty(requestBodySchemaRef))
      {
         usedSchemas.Add(requestBodySchemaRef);
      }

      // Inspect operation responses for schema usage
      foreach (var response in operation.Responses.Values)
      {
         foreach (var content in response.Content)
         {
            var schemaRef = content.Value.Schema?.Reference?.Id;
            var schemaRef2 = content.Value.Schema?.Items?.Reference.Id;

            AddUsedSchema(content.Value.Schema);
         }
      }
   }

   private static void AddUsedSchema(OpenApiSchema? schema)
   {
      if (schema is null)
      {
         return;
      }
      var schemaRef = schema?.Reference?.Id;
      if (!string.IsNullOrEmpty(schemaRef))
      {
         usedSchemas.Add(schemaRef);
      }
      //used for arrays
      schemaRef = schema?.Items?.Reference.Id;
      if (!string.IsNullOrEmpty(schemaRef))
      {
         usedSchemas.Add(schemaRef);
      }
   }

   public HashSet<string> GetUsedSchemas()
   {
      return usedSchemas;
   }
}
