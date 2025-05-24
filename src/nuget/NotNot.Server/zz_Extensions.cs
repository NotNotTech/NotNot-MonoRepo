using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;


/// <summary>
/// extension methods for various google cloud datastore v1 objects
/// </summary>
public static class zz_Extensions_Entity_GoogleCloud_Datastore_V1
{
   /// <summary>
   /// returns entity.Key.Path.First().Kind;
   /// </summary>
   /// <param name="entity"></param>
   /// <returns></returns>
   public static string _Kind(this Google.Cloud.Datastore.V1.Entity entity)
   {
      return entity.Key.Path.First().Kind;
   }
   public static string _Name(this Google.Cloud.Datastore.V1.Entity entity)
   {
      return entity.Key.Path.First().Name;
   }
   public static bool _IsPropertyNull(this Google.Cloud.Datastore.V1.Entity entity, string propertyName)
   {
      if (entity.Properties.ContainsKey(propertyName))
      {
         return entity.Properties[propertyName].IsNull;
      }
      return true;
   }
   public static TValue? _GetOrDefault<TValue>(this Google.Cloud.Datastore.V1.Entity entity, string propertyName)
   {
      if (entity.Properties.ContainsKey(propertyName))
      {
         var value = entity.Properties[propertyName];
         if (value.IsNull)
         {
            return default;
         }


         var t = typeof(TValue);
         switch (t)
         {
            case var _ when t == typeof(string):
               return (TValue)(object)value.StringValue;
            case var _ when t == typeof(int):
               return (TValue)(object)(int)value.IntegerValue;
            case var _ when t == typeof(long):
               return (TValue)(object)value.IntegerValue;
            case var _ when t == typeof(double):
               return (TValue)(object)value.DoubleValue;
            case var _ when t == typeof(bool):
               return (TValue)(object)value.BooleanValue;
            case var _ when t == typeof(DateTime):
               return (TValue)(object)value.TimestampValue.ToDateTime();
            case var _ when t == typeof(DateTimeOffset):
               return (TValue)(object)value.TimestampValue.ToDateTimeOffset();
            //case var _ when t == typeof(IPAddress):
            //   return (TValue)(object)IPAddress.Parse(value.StringValue);
            default:
               throw new NotImplementedException($"type {t} not implemented");


         }


      }
      return default;
   }


   /// <summary>
   /// add or set the entity.properties[propertyName] to value
   /// </summary>
   public static void _AddOrSet<TValue>(this Google.Cloud.Datastore.V1.Entity entity, string propertyName, TValue value)
   {

      var properties = entity.Properties;

      ////create the appropriate Googlestore Value
      Google.Cloud.Datastore.V1.Value dsValue;

      if (value is null)
      {
         dsValue = new Google.Cloud.Datastore.V1.Value { NullValue = Google.Protobuf.WellKnownTypes.NullValue.NullValue };
      }
      else
      {
         dsValue = value switch
         {
            string s => new Google.Cloud.Datastore.V1.Value { StringValue = s },
            int i => new Google.Cloud.Datastore.V1.Value { IntegerValue = i },
            long l => new Google.Cloud.Datastore.V1.Value { IntegerValue = l },
            double d => new Google.Cloud.Datastore.V1.Value { DoubleValue = d },
            bool b => new Google.Cloud.Datastore.V1.Value { BooleanValue = b },
            DateTime dt => new Google.Cloud.Datastore.V1.Value { TimestampValue = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(dt.ToUniversalTime()) },
            DateTimeOffset dto => new Google.Cloud.Datastore.V1.Value { TimestampValue = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(dto) },
            //IPAddress ip => new Google.Cloud.Datastore.V1.Value { StringValue = ip?.ToString() },       
            _ => throw new NotImplementedException($"type {value.GetType()} not implemented"),
         };
      }



      //set the property value based on the respective value type
      if (properties.ContainsKey(propertyName))
      {
         properties[propertyName] = dsValue;
      }
      else
      {
         properties.Add(propertyName, dsValue);
      }
   }


}

public static class zz_Extensions_Key_GoogleCloud_Datastore_V1
{
   /// <summary>
   /// returns entity.Key.Path.First().Kind;
   /// </summary>
   /// <param name="entity"></param>
   /// <returns></returns>
   public static string _Kind(this Google.Cloud.Datastore.V1.Key key)
   {
      return key.Path.First().Kind;
   }
   public static string _Name(this Google.Cloud.Datastore.V1.Key key)
   {
      return key.Path.First().Name;
   }
}


public static class zz_Extensions_HostApplicationBuilder
{
   /// <summary>
   /// extends the normal _NotNotEzSetup to include server specific init, such as secrets loading
   /// </summary>
   public static async Task _NotNotEzSetup_Server(this IHostApplicationBuilder builder, CancellationToken ct, IEnumerable<Assembly>? scanAssemblies = null, IEnumerable<string>? scanIgnore = null)
   {

      NotNot.Secrets.SecretsLoader.LoadSecrets(builder.Configuration);

      await _NotNotUtils_ConfigureCache(builder, ct);

      await builder._NotNotEzSetup(ct, scanAssemblies, scanIgnore);


     // await _NotNotUtils_ConfigureSwaggerGen(builder, ct);


   }


   /// <summary>
   /// nice caching subsystem.  docs for fusionCache here: https://github.com/ZiggyCreatures/FusionCache/tree/main
   /// </summary>
   internal static async Task _NotNotUtils_ConfigureCache(this IHostApplicationBuilder builder, CancellationToken ct)
   {

      //builder.Services.AddMemoryCache();
      //builder.Services.AddDistributedMemoryCache();


      //verify that NotNot.Cache node exists in AppSettings.Json
      var cacheNode = builder.Configuration.GetSection("NotNot.Cache");
      if (!cacheNode.Exists())
      {
         __.GetLogger()._EzError("NotNot.Cache node not found in AppSettings.json.  FusionCache will use defaults");
      }


      //config cache
      builder.Services.AddFusionCache()
         //.WithOptions(opt => { 

         //})
         .WithDefaultEntryOptions(opt =>
         {
            opt.Duration = TimeSpan.FromSeconds(builder.Configuration.GetValue<double?>("NotNot.Cache.DurationDefaultSec") ?? 33);
            opt.FailSafeMaxDuration = TimeSpan.FromSeconds(builder.Configuration.GetValue<double?>("NotNot.Cache.DurationMaxFailSafeSec") ?? 77);
            opt.IsFailSafeEnabled = builder.Configuration.GetValue<bool?>("NotNot.Cache.IsFailSafeEnabled") ?? true;
            //opt.FactorySoftTimeout = TimeSpan.FromMilliseconds(100);
         })
         ;

   }

   ///// <summary>
   ///// does general swaggergen configs:  adding xml docs, adding [SwaggerIgnore] or [SwaggerExample] attributes
   ///// </summary>
   ///// <param name="builder"></param>
   ///// <param name="ct"></param>
   ///// <returns></returns>
   //internal static async Task _NotNotUtils_ConfigureSwaggerGen(this IHostApplicationBuilder builder, CancellationToken ct)
   //{
   //   builder.Services.ConfigureSwaggerGen((options) =>
   //   {

   //      //use globbing to load xml docs from all assemblies, to be used for swagger request/response examples docgen
   //      var xmlFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.xml", SearchOption.TopDirectoryOnly).ToList();
   //      xmlFiles.ForEach(xmlFile => options.IncludeXmlComments(xmlFile));


   //      //Add custom SwaggerGen filters so you can decorate properties with [SwaggerIgnore] or [SwaggerExample] attributes
   //      options.SchemaFilter<SwaggerSchemaFilter_ApplyAttributes>();
   //      options.OperationFilter<SwaggerOperationFilter_DiscoverUsedSchemas>();

   //      options.DocumentFilter<SwaggerDocumentFilter_RebuildSchema>();
   //   });
   //}

   public static Problem _ToProblem(this ValidationProblemDetails problemDetails)
   {
	   throw __.placeholder.NotImplemented("need to populate a problem with this, MAYBE, if we end up using the related validation code.");
   }
}
