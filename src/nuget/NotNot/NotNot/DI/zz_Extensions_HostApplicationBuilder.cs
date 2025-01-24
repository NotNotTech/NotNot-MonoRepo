using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NotNot.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotNot.DI.Advanced;
using Scrutor;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;



public static class zz_Extensions_HostApplicationBuilder
{

   private static ObjectTrackingCollection<object> _initializedServiceTracker = new();

   /// <summary>
   /// registeres services extending interfaces like ISingleton and also decorates services implementing IAutoInitialize with a call to .AutoInitialize()
   /// </summary>
   /// <param name="services"></param>
   /// <param name="ct"></param>
   /// <param name="scanAssemblies">assemblies you want to scan for automapper and scrutor types.  default is everything: AppDomain.CurrentDomain.GetAssemblies()</param>
   /// <param name="scanIgnore">assemblies to not scan for DI types.   by default this is 'Microsoft.*' because ASP NetCore IHostedService internal registrations conflict.</param>
   /// <returns></returns>
   public static async Task _NotNotEzSetup(this IHostApplicationBuilder builder, CancellationToken ct, IEnumerable<Assembly>? scanAssemblies = null
	   , IEnumerable<string>? scanIgnore = null, Action<IConfiguration,LoggerConfiguration> extraLoggerConfig = null)
   {

      await _NotNotUtils_ConfigureLogging(builder, ct, extraLoggerConfig);

      var targetAssemblies = AssemblyReflectionHelper._FilterAssemblies(
         scanAssemblies: scanAssemblies
         , scanIgnore: scanIgnore
         //, keepRegardless:new[] { "Microsoft.CodeAnalysis" }
         );



      try
      {
         //add automapper type mappings found in all assemblies
         builder.Services.AddAutoMapper(targetAssemblies);

         await _ScrutorRegisterServiceInterfaces(builder, ct, targetAssemblies);

      }catch(Exception ex)
      {
         throw new Exception("If this is an assembly load exception, try adding it's referencing assembly to 'scanIgnore'", ex);
      }


      await _DecorateAutoInitializeServices(builder, ct);





   }



   internal static async Task _NotNotUtils_ConfigureLogging(this IHostApplicationBuilder builder, CancellationToken ct, Action<IConfiguration, LoggerConfiguration> extraLoggerConfig = null)
   {

      //config logging
      builder.Logging.ClearProviders();
      builder.Services.AddSerilog((hostingContext, loggerConfiguration) =>
	      {
		      loggerConfiguration = loggerConfiguration.ReadFrom.Configuration(builder.Configuration)
#if DEBUG
                  
            .WriteTo.Sink(new NotNot.Logging.AssertOnMsgSink(builder.Configuration), LogEventLevel.Error)
				//	.AssertOnMsgSinkWithoutBatching(builder.Configuration, LogEventLevel.Warning)
#endif
					;


			if (extraLoggerConfig != null)
         {
            extraLoggerConfig(builder.Configuration, loggerConfiguration);
         }

      }
      );
   }
   public static LoggerConfiguration AssertOnMsgSinkWithoutBatching(
	   this LoggerConfiguration loggerConfiguration,
	   IConfiguration configuration,
	   LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
	   LoggingLevelSwitch? levelSwitch = null)
   {
	   return loggerConfiguration.WriteTo.Sink(
		   new NotNot.Logging.AssertOnMsgSink(configuration),
		   restrictedToMinimumLevel,
		   levelSwitch);
   }

	/// <summary>
	/// hooks up all services that implement ISingleton, ITransient, IScoped to be auto-registered
	/// </summary>
	/// <param name="services"></param>
	/// <param name="ct"></param>
	/// <returns></returns>
	[SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
   internal static async Task _ScrutorRegisterServiceInterfaces(this IHostApplicationBuilder builder, CancellationToken ct, IEnumerable<Assembly> targetAssemblies)
   {


      //logger.LogTrace("utilizing Scrutor auto-registration of DI services....");

      //use Scrutor nuget library to auto-register DI Services
      builder.Services.Scan(scan =>
      {
         //register interfaces

         void RegisterHosted<TInterface>()
         {
            scan.FromAssemblies(targetAssemblies)
               .AddClasses(classes => classes.AssignableTo<TInterface>())
               .UsingRegistrationStrategy(RegistrationStrategy.Append)
               .AsSelfWithInterfaces()
               .WithSingletonLifetime();

         }
         void RegisterSingleton<TInterface>()
         {
            //as interface, append
            scan.FromAssemblies(targetAssemblies)
               .AddClasses((classes) =>
               {
                  var result = classes.AssignableTo<TInterface>();

               })
               //.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
               //                                          t.GetInterfaces().Any(i => i.IsGenericType &&
               //                                                                     i.GetGenericTypeDefinition() == typeof(TInterface).GetGenericTypeDefinition())))
               //.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
               //                                          t.BaseType != null && t.BaseType.GetInterfaces().Any(i => i.IsGenericType &&
               //                                                                                                    i.GetGenericTypeDefinition() == typeof(TInterface).GetGenericTypeDefinition())))
               //.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
               //                                          t.BaseType != null && t.BaseType._IsAssignableTo<TInterface>()))
               //.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
               //                                          t._IsAssignableTo<TInterface>()))

               .UsingRegistrationStrategy(RegistrationStrategy.Append)
               .AsSelfWithInterfaces()
               .WithSingletonLifetime();
         }

         void RegisterTransient<TInterface>()
         {
            //as interface, append
            scan.FromAssemblies(targetAssemblies)
               .AddClasses(classes => classes.AssignableTo<TInterface>())
               .UsingRegistrationStrategy(RegistrationStrategy.Append)
               .AsSelfWithInterfaces()
               .WithTransientLifetime();
         }

         void RegisterScoped<TInterface>()
         {
            //as interface, append
            scan.FromAssemblies(targetAssemblies)
               .AddClasses(classes => classes.AssignableTo<TInterface>())
               .UsingRegistrationStrategy(RegistrationStrategy.Append)
               .AsSelfWithInterfaces()
               .WithScopedLifetime()
               ;
         }

         RegisterHosted<IHostedService>();
         RegisterSingleton<ISingletonService>();
         RegisterTransient<ITransientService>();
         RegisterScoped<IScopedService>();
      });

   }


   //// Skip open generic types
   /// if (serviceType.Name.StartsWith("Cache"))
   /// {
   /// var xxx = 0;
   /// }
   //if (serviceType.IsGenericTypeDefinition)
   //{
   //   continue;
   //}

   public static class TypeDiscovery
   {
      public static List<Type> FindClosedGenericsOfOpenGeneric(Type openGenericType)
      {



         if (!openGenericType.IsGenericTypeDefinition)
         {
            throw new ArgumentException("The provided type must be an open generic type", nameof(openGenericType));
         }
         // Get all loaded assemblies
         Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

         var closedTypes = new List<Type>();

         foreach (var assembly in assemblies)
         {
            foreach (var type in assembly.GetTypes())
            {
               // Check if the type is a generic type and if it is constructed from the openGenericType
               if (type.IsGenericType && type.GetGenericTypeDefinition() == openGenericType)
               {
                  closedTypes.Add(type);
               }
            }
         }

         return closedTypes;
      }
   }

   internal static async Task _DecorateAutoInitializeServices(this IHostApplicationBuilder builder, CancellationToken ct)
   {
      var services = builder.Services;

      foreach (var serviceDescriptor in services.ToList())
      {
         var serviceType = serviceDescriptor.ServiceType;


         if (typeof(IAutoInitialize).IsAssignableFrom(serviceType))
         {
            //ok
         }
         else if (serviceDescriptor.ImplementationType != null && typeof(IAutoInitialize).IsAssignableFrom(serviceDescriptor.ImplementationType))
         {
            //ok
         }
         else if (serviceDescriptor.ImplementationInstance != null && typeof(IAutoInitialize).IsAssignableFrom(serviceDescriptor.ImplementationInstance.GetType()))
         {
            //ok
         }
         else
         {
            //not assignable
            continue;
         }

         Decorate_AutoInit_ServiceRegistrationUpdater.DecorateService(services, serviceDescriptor);

      }
   }


   /// <summary>
   /// hooks up all services that implement IAutoInitialize to be decorated with a call to .AutoInitialize()
   /// </summary>
   internal static async Task _ScrutorHookAutoInitialize_old(this IHostApplicationBuilder builder, CancellationToken ct)
   {
      var hasImplementation = builder.Services.Where(sd =>
      {
         var serviceType = sd.ServiceType;

         if (typeof(IAutoInitialize).IsAssignableFrom(serviceType))
         {
            return true;
         }

         //can't check factory return type, so always have to decorate and try their returned result
         if (sd.ImplementationFactory != null)
         {
            return true;
         }

         //check if ImplementationInstance inherits from IInitializeableService
         if (sd.ImplementationInstance != null)
         {
            if (typeof(IAutoInitialize).IsAssignableFrom(sd.ImplementationInstance.GetType()))
            {
               return true;
            }

            return false;
         }

         //check if ImplementationType inherits from IInitializeableService
         if (sd.ImplementationType != null)
         {
            if (typeof(IAutoInitialize).IsAssignableFrom(sd.ImplementationType))
            {
               return true;
            }

            return false;
         }

         return false;
      });

      var groupedByServiceType = hasImplementation
         .GroupBy(serviceDescriptor => serviceDescriptor.ServiceType);

      List<Type> serviceTypes = groupedByServiceType.Select(grouping => grouping.Key).ToList();

      foreach (Type serviceType in serviceTypes)
      {
         try
         {
            var _myServiceType1 = serviceType;
            builder.Services.Decorate(serviceType, (innerService, serviceProvider) =>
            {
               var _myServiceType2 = serviceType;
               if (innerService is IAutoInitialize initService)
               {
                  //ensure that we only call .AutoInitialize() once per object, first time it's requested
                  if (_initializedServiceTracker.TryAdd(innerService))
                  {
                     initService.AutoInitialize(serviceProvider, ct)._SyncWait();
                  }
               }

               return innerService;
            });
         }
         catch (DecorationException ex)
         {
            __.GetLogger()._EzError("error calling .AutoInitialzie() on  decorated service. ", serviceType, ex);
         }
      }
   }

}
