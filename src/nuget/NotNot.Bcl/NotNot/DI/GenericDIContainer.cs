using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NotNot.DI;


/// <summary>
/// Provides a base implementation for managing a dependency injection container using the Generic Host, 
/// supporting both inheritance-based and delegation-based service configuration.
/// </summary>
public class GenericDIContainer : DisposeGuard
{
	/// <summary>
	/// The underlying host instance.
	/// </summary>
	public IHost? _host;

	/// <summary>
	/// Gets the configured service provider from the host.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if the host has not been initialized via <see cref="Initialize"/>.</exception>
	public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Initialize has not been called.");

	/// <summary>
	/// Initializes the host and service provider.
	/// This method supports both inheritance-based and delegation-based service configuration. 
	/// It will first call the virtual `OnInitialize` method, allowing subclasses to configure the host builder. 
	/// Then, it will execute the optional `configureDelegate` for further customization.
	/// </summary>
	/// <param name="builder">An optional `IHostApplicationBuilder` to use. If null, a default one will be created.</param>
	/// <param name="configureDelegate">An optional delegate to further configure the host builder.</param>
	public async ValueTask Initialize(HostApplicationBuilder? builder = null, Func<HostApplicationBuilder, ValueTask>? configureDelegate = null)
	{
		builder ??= Host.CreateApplicationBuilder();

		await OnInitialize(builder);
		if (configureDelegate != null)
		{
			await configureDelegate(builder);
		}
		_host = builder.Build();
	}

	/// <summary>
	/// A virtual method that allows subclasses to register their default services and configurations on the host builder.
	/// This method is called by <see cref="Initialize"/> before the optional `configureDelegate` is executed.
	/// </summary>
	/// <param name="builder">The host application builder to add services to.</param>
	protected virtual ValueTask OnInitialize(HostApplicationBuilder builder)
	{
		return ValueTask.CompletedTask;
	}

	protected override void OnDispose(bool managedDisposing)
	{
		base.OnDispose(managedDisposing);
		if (managedDisposing)
		{
			_host?.Dispose();
		}
		_host = null;


	}


}
