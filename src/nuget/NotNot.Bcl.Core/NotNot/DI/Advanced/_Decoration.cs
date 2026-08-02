using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NotNot.DI.Advanced;


/// <summary>
/// Factory to create service instances based on the original ServiceDescriptor.
/// <para>Internal helpers to perform <see cref="IDiAutoInitialize.AutoInitialize"/> calls.</para>
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

		if (isNew && service is IDiAutoInitialize autoInitialize)
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
		{
			service = (TService)_serviceDescriptor.ImplementationFactory.Invoke(_serviceProvider);
			isNew = true;
		}
		else if (_serviceDescriptor.ImplementationInstance != null)
		{
			service = (TService)_serviceDescriptor.ImplementationInstance;
			isNew = false;
		}
		else if (_serviceDescriptor.ImplementationType != null)
		{
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
/// <para>Internal helpers to perform <see cref="IDiAutoInitialize.AutoInitialize"/> calls.</para>
/// </summary>
public static class Decorate_AutoInit_ServiceRegistrationUpdater
{
	/// <summary>
	/// Decorates a single service registration with auto-initialization.
	/// </summary>
	public static void DecorateService(IServiceCollection services, ServiceDescriptor serviceDescriptor)
	{
		var serviceType = serviceDescriptor.ServiceType;

		if (serviceType.IsGenericTypeDefinition)
		{
			throw new LoLoDiagnosticsException($"{serviceType._GetReadableTypeName()} can not implement a DI decorator interface like IAutoInitialize. Open generic types are not supported for decoration because generic factories are not supported in C# DI. You must do a workaround (for example, init via the ctor instead of IAutoInitialize)");
		}
		else
		{
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
			// Activator.CreateInstance on a concrete closed-generic type never returns null.
			var factory = (dynamic)Activator.CreateInstance(factoryType, serviceProvider, serviceDescriptor)!;
			return factory.Create();
		}, serviceDescriptor.Lifetime));
	}

	/// <summary>
	/// Iterates over all services in an IServiceCollection and decorates them all.
	/// </summary>
	public static void DecorateAllServices(IServiceCollection services)
	{
		foreach (var serviceDescriptor in services.ToList())
		{
			DecorateService(services, serviceDescriptor);
		}
	}
}
