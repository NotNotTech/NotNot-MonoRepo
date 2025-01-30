using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NotNot.DI.Advanced;


/// <summary>
/// Factory to create service instances based on the original ServiceDescriptor.
/// <para>this is all internal helpers to perform IAutoInitialize.AutoInitialize() calls</para>
/// </summary>
public class DecoratorFactory_AutoInit<TService>
{
   private readonly IServiceProvider _serviceProvider;
   private readonly ServiceDescriptor _serviceDescriptor;

   public DecoratorFactory_AutoInit(IServiceProvider serviceProvider, ServiceDescriptor serviceDescriptor)
   {
      _serviceProvider = serviceProvider;
      _serviceDescriptor = serviceDescriptor;
   }

   /// <summary>
   /// Creates an instance of the service based on the original ServiceDescriptor.
   /// </summary>
   public TService Create()
   {
      var (isNew, service) = _CreateHelper();

      if (isNew && service is IAutoInitialize autoInitialize)
      {
         autoInitialize.AutoInitialize(_serviceProvider, default)._SyncWait();
      }

      return service;
   }

   private (bool isNew, TService service) _CreateHelper()
   {
      var isNew = false;
      TService service;

      if (_serviceDescriptor.ImplementationFactory != null)
      {// Check if the service is created using a factory method
         service = (TService)_serviceDescriptor.ImplementationFactory.Invoke(_serviceProvider);
         isNew = true;
      }
      else if (_serviceDescriptor.ImplementationInstance != null)
      {// Check if the service is a specific instance

         service = (TService)_serviceDescriptor.ImplementationInstance;
         isNew = false;
      }
      else if (_serviceDescriptor.ImplementationType != null)
      {// Otherwise, the service is created using its type
         service = (TService)ActivatorUtilities.CreateInstance(_serviceProvider, _serviceDescriptor.ImplementationType);
         isNew = true;
      }
      else
      {
         throw new InvalidOperationException("Invalid ServiceDescriptor configuration.");
      }
      return (isNew, service);
   }
}

/// <summary>
/// Provides methods to update service registrations in an IServiceCollection
/// to include decorators.
/// <para>this is all internal helpers to perform IAutoInitialize.AutoInitialize() calls</para>
/// </summary>
public static class Decorate_AutoInit_ServiceRegistrationUpdater
{
   /// <summary>
   /// Decorates a single service registration with a specified decorator.
   /// </summary>
   /// <param name="services">The IServiceCollection containing the service registrations.</param>
   /// <param name="decoratorType">The type of the decorator to be applied.</param>
   /// <param name="serviceDescriptor">The service descriptor of the service to be decorated.</param>
   public static void DecorateService(IServiceCollection services, ServiceDescriptor serviceDescriptor)
   {
      // Create the generic factory type for the service and decorator
      var serviceType = serviceDescriptor.ServiceType;

      // Check if the service type is an open generic type
      if (serviceType.IsGenericTypeDefinition)
      {
         throw new LoLoDiagnosticsException($"{serviceType._GetReadableTypeName()} can not implement a DI decorator interface like IAutoInitialize. Open generic types are not supported for decoration because generic factories are not supported in C# DI. You must do a workaround (for example, init via the ctor instead of IAutoInitialize)");
      }
      else
      {
         // Handle non-generic or closed generic types
         RegisterNonGenericService(services, serviceDescriptor);
      }
   }

   private static void RegisterNonGenericService(IServiceCollection services, ServiceDescriptor serviceDescriptor)
   {
      var serviceType = serviceDescriptor.ServiceType;

      var factoryType = typeof(DecoratorFactory_AutoInit<>).MakeGenericType(serviceType);

      services.Remove(serviceDescriptor);
      services.Add(new ServiceDescriptor(serviceType, serviceProvider =>
      {
         var factory = (dynamic)Activator.CreateInstance(factoryType, serviceProvider, serviceDescriptor);
         return factory.Create();
      }, serviceDescriptor.Lifetime));
   }


   /// <summary>
   /// Iterates over all services in an IServiceCollection and decorates them
   /// with a specified decorator type.
   /// </summary>
   /// <param name="services">The IServiceCollection to be updated.</param>
   /// <param name="decoratorType">The type of the decorator to apply to all services.</param>
   public static void DecorateAllServices(IServiceCollection services)
   {
      // Iterate over a copy of the service collection to avoid modification issues
      foreach (var serviceDescriptor in services.ToList())
      {
         // Decorate each service using the DecorateService method
         DecorateService(services, serviceDescriptor);
      }
   }
}
