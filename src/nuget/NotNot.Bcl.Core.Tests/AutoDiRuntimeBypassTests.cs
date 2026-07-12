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
#pragma warning disable NN_DI_007 // Intentional violation: this test proves the runtime guard rejects it.
		services.AddHostedService<ScannedHostedFixture>();
#pragma warning restore NN_DI_007

		var exception = Assert.ThrowsAny<Exception>(
			() => services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly));

		Assert.Contains("Manual IHostedService registrations detected", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddNotNotDiServices_ExplicitUnmarkedAssembly_FailsFast_NamesOffender()
	{
		// Explicit-scan fail-fast contract (zz_Extensions_IServiceCollection_DI._FilterAssemblies): a caller
		// who explicitly names an assembly lacking [assembly: AutoDiScanAssembly] made a mistake — the scan
		// throws rather than silently dropping the assembly. System.Private.CoreLib (typeof(object).Assembly)
		// is a framework assembly guaranteed NOT to carry the marker.
		var services = new ServiceCollection();
		var unmarkedAssembly = typeof(object).Assembly;

		var exception = Assert.ThrowsAny<Exception>(
			() => services.AddNotNotDiServices(unmarkedAssembly));

		// The message must NAME the offending assembly so the caller can act on it.
		Assert.Contains("AutoDiScanAssembly", exception.Message, StringComparison.Ordinal);
		Assert.Contains(unmarkedAssembly.FullName!, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddNotNotDiServices_ExplicitMarkedAssembly_Scans_HappyPath()
	{
		// Happy path for the explicit-scan contract: an explicitly-named MARKED assembly (this test assembly
		// carries [assembly: AutoDiScanAssembly] via Properties/AutoDiScanMarker.cs) scans successfully and
		// registers its scan-eligible types — no throw.
		var services = new ServiceCollection();

		services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly);

		Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ScannedSingletonFixture));
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
