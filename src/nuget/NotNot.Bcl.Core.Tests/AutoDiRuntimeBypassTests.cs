using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

public sealed class AutoDiRuntimeBypassTests
{
	[Fact]
	public void AddNotNotDiServices_ExcludesTypeLevelBypassAcrossHostedAndMarkerPasses()
	{
		var services = new ServiceCollection();

		services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly);

		Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ScannedHostedFixture));
		Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ScannedSingletonFixture));
		Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BypassedHostedFixture));
		Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(BypassedSingletonFixture));
	}

	[Fact]
	public void AddNotNotDiServices_AllowsBypassedHostedServiceRegisteredBeforeScan()
	{
		var services = new ServiceCollection();
		services.AddHostedService<BypassedHostedFixture>();

		services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly);

		Assert.Contains(
			services,
			descriptor => descriptor.ServiceType == typeof(IHostedService)
				&& descriptor.ImplementationType == typeof(BypassedHostedFixture));
	}

	[Fact]
	public void AddNotNotDiServices_RejectsScanEligibleHostedServiceRegisteredBeforeScan()
	{
		var services = new ServiceCollection();
		services.AddHostedService<ScannedHostedFixture>();

		var exception = Assert.ThrowsAny<Exception>(
			() => services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly));

		Assert.Contains("Manual IHostedService registrations detected", exception.Message, StringComparison.Ordinal);
	}
}

public sealed class ScannedHostedFixture : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[AutoDiBypass]
public sealed class BypassedHostedFixture : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ScannedSingletonFixture : IDiSingletonService;

[AutoDiBypass]
public sealed class BypassedSingletonFixture : IDiSingletonService;
