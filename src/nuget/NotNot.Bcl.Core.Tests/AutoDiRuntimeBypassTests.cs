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

	[Fact]
	public void AddNotNotDiServices_TopLevelInternalHostedService_IsNotScanned()
	{
		// F2 GROUND-TRUTH PROBE (a). The Scrutor scan in _ScrutorRegisterServiceInterfaces registers
		// IHostedService via `.AddClasses(classes => classes.AssignableTo<IHostedService>().Where(...))`.
		// Scrutor 7.0 defaults that scan to PUBLIC-ONLY at the top-level: a top-level `internal` class is
		// NOT registered. This pins the runtime truth the NN_DI_007 analyzer aligns to (top-level
		// non-public → not scan-eligible → analyzer stays silent, IsScrutorPublicScanVisible correct for this axis).
		var services = new ServiceCollection();

		services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly);

		Assert.DoesNotContain(services, descriptor => descriptor.ImplementationType == typeof(InternalHostedFixture));
	}

	[Fact]
	public void AddNotNotDiServices_PublicNestedInInternal_IsScanned()
	{
		// F2 GROUND-TRUTH PROBE (b). A `public` IHostedService NESTED inside an `internal` container has
		// non-public EFFECTIVE visibility, yet Scrutor 7.0's public-type scan STILL registers it — the
		// scan keys on the type's OWN DeclaredAccessibility, not the effective (enclosing-aware)
		// visibility. This WAS the false-negative the pre-fix effective-public predicate produced: the
		// runtime auto-registers this shape, so an explicit AddHostedService of it IS a duplicate and the
		// analyzer MUST fire. Aligned predicate (NN_DI_007 IsScrutorPublicScanVisible): own-accessibility
		// public, not effective-public.
		var services = new ServiceCollection();

		services.AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly);

		Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(InternalContainer.PublicNestedHostedFixture));
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

// ── F2 visibility-matrix fixtures ──────────────────────────────────────────────

/// <summary>
/// A top-level <c>internal</c> (non-public) <see cref="IHostedService"/>. Proves the Scrutor
/// <c>AddClasses</c> public-type scan does NOT register a top-level internal hosted service — the
/// runtime ground-truth the NN_DI_007 <c>IsScrutorPublicScanVisible</c> predicate aligns to.
/// </summary>
internal sealed class InternalHostedFixture : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// An <c>internal</c> container holding a <c>public</c> nested <see cref="IHostedService"/> whose
/// EFFECTIVE visibility is not public (a non-public enclosing type). Proves the Scrutor scan STILL
/// registers it (keying on the type's OWN accessibility, not effective enclosing-aware visibility) —
/// the case NN_DI_007 <c>IsScrutorPublicScanVisible</c> now correctly fires on (the pre-fix
/// effective-public predicate produced a false negative here).
/// </summary>
internal static class InternalContainer
{
	public sealed class PublicNestedHostedFixture : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
