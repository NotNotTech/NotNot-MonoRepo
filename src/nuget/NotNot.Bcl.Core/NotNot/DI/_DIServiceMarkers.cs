using Microsoft.Extensions.Hosting;

/// <summary>
/// Base marker interface for DI auto-registration. Do not implement directly.
/// </summary>
public interface IMsDiService { }

/// <summary>
/// Mark a DI service to be auto-registered with Singleton lifetime.
/// </summary>
public interface IDiSingletonService : IMsDiService
{
}

/// <summary>
/// Mark a DI service to be auto-registered with Transient lifetime.
/// </summary>
public interface IDiTransientService : IMsDiService
{
}

/// <summary>
/// Mark a DI service to be auto-registered with Scoped lifetime.
/// </summary>
public interface IDiScopedService : IMsDiService
{
}

/// <summary>
/// Signals that when a service is created, it should be initialized via a call to <see cref="AutoInitialize"/>.
/// <para>Note: Already-created services returned by <c>serviceDescriptor.ImplementationInstance</c> will not have AutoInitialize() called.</para>
/// <para>Also: "open generics" services cannot implement a DI decorator interface like IAutoInitialize.
/// Open generic types are not supported for decoration because generic factories are not supported in C# DI.
/// You must do a workaround (for example, init via the ctor instead of IAutoInitialize).</para>
/// </summary>
public interface IDiAutoInitialize
{
	/// <summary>
	/// Override to provide (optional) custom initialization logic for your service.
	/// </summary>
	ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}

/// <summary>
/// Helper DI service for letting LoLo automatically get a reference to services (<c>__.Services</c>).
/// </summary>
public class LoLoRunner(IServiceProvider _services) : IHostedLifecycleService, IDiAutoInitialize
{
	public async Task StartAsync(CancellationToken cancellationToken) { }
	public async Task StopAsync(CancellationToken cancellationToken) { }
	public async Task StartingAsync(CancellationToken cancellationToken) { }
	public async Task StartedAsync(CancellationToken cancellationToken) { }
	public async Task StoppingAsync(CancellationToken cancellationToken) { }
	public async Task StoppedAsync(CancellationToken cancellationToken) { }

	public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	{
		__.Initialize(services);
	}
}
