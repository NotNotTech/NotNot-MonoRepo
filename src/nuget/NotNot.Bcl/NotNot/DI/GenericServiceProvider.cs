using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace NotNot.DI;


/// <summary>
/// Provides a base implementation for managing a dependency injection container, 
/// supporting both inheritance-based and delegation-based service configuration.
/// </summary>
public class GenericDIServices : AsyncDisposeGuard
{
	/// <summary>
	/// The underlying service provider instance.
	/// </summary>
	private ServiceProvider? _serviceProvider;

	/// <summary>
	/// Gets the configured service provider.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if the provider has not been initialized via <see cref="Initialize"/>.</exception>
	public IServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("Initialize has not been called.");

	/// <summary>
	/// Initializes the service provider.
	/// This method supports both inheritance-based and delegation-based service configuration. 
	/// It will first call the virtual `OnInitialize` method, allowing subclasses to register their default services. 
	/// Then, it will execute the optional `configureServices` delegate, which allows the calling code to dynamically add or override service registrations.
	/// </summary>
	/// <param name="configureServices">An optional delegate to further configure the service collection.</param>
	public async ValueTask Initialize(Func<IServiceCollection, ValueTask>? configureServices = null)
	{
		var serviceCollection = new ServiceCollection();
		await OnInitialize(serviceCollection);
		if (configureServices != null)
		{
			await configureServices(serviceCollection);
		}
		_serviceProvider = serviceCollection.BuildServiceProvider();
	}

	/// <summary>
	/// A virtual method that allows subclasses to register their default services.
	/// This method is called by <see cref="Initialize"/> before the optional `configureServices` delegate is executed.
	/// </summary>
	/// <param name="serviceCollection">The service collection to add services to.</param>
	public virtual ValueTask OnInitialize(IServiceCollection serviceCollection)
	{
		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Disposes the underlying service provider if it has been created.
	/// </summary>
	protected override async ValueTask OnDispose(bool managedDisposing)
	{
		if (managedDisposing)
		{
			if (_serviceProvider is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
			else
			{
				_serviceProvider.Dispose();
			}
		}
		_serviceProvider = null;

		await base.OnDispose(managedDisposing);
	}


}
