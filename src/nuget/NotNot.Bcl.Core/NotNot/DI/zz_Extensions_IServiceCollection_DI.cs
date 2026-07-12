using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;
using NotNot.Bcl.Diagnostics;
using NotNot.DI.Advanced;
using Scrutor;

/// <summary>
/// Provides assembly filtering for DI auto-registration scanning.
/// </summary>
public static class AssemblyReflectionHelper
{
	/// <summary>
	/// Selects the assemblies eligible for Scrutor DI scanning. A candidate assembly is scanned
	/// iff-and-only-if it carries <c>[assembly: AutoDiScanAssembly]</c> AND does NOT carry
	/// <c>[assembly: AutoDiBypass]</c> (bypass is higher precedence). This is the SINGLE marker
	/// authority — there is no assembly-name-prefix inference and no scan-all-minus-ignore-glob
	/// default.
	/// </summary>
	/// <param name="scanAssemblies">
	/// Assemblies to consider. <c>null</c> selects the DEFAULT-APPDOMAIN path
	/// (<c>AppDomain.CurrentDomain.GetAssemblies()</c>) — unmarked assemblies are SILENTLY excluded.
	/// A non-null list selects the EXPLICIT-SCAN path — every named assembly lacking the marker is a
	/// caller error and the method THROWS with a list of the offenders (fail-fast).
	/// </param>
	/// <param name="scanIgnore">
	/// Optional secondary assembly-name glob filter applied AFTER the marker gate. Marked assemblies
	/// matching a pattern here are still dropped (unless matched by <paramref name="keepRegardless"/>).
	/// Rarely needed now that the marker is the primary gate; retained for callers that want to
	/// narrow an already-marked set.
	/// </param>
	/// <param name="keepRegardless">Assembly name glob patterns to keep even if matched by <paramref name="scanIgnore"/>.</param>
	public static List<Assembly> _FilterAssemblies(IEnumerable<Assembly>? scanAssemblies, IEnumerable<string>? scanIgnore = null, IEnumerable<string>? keepRegardless = null)
	{
		// null => default-AppDomain path (silent-exclude unmarked); non-null => explicit path (fail-fast unmarked).
		var isExplicitScan = scanAssemblies is not null;
		var candidates = new List<Assembly>(scanAssemblies ?? AppDomain.CurrentDomain.GetAssemblies());

		// NotNot.Bcl.Core participates via the DEFAULT-AppDomain path (it carries [assembly: AutoDiScanAssembly]
		// and is always present in AppDomain.CurrentDomain.GetAssemblies()). It is NOT force-added here: on
		// the default path the force-add was dead code, and on the explicit path it silently injected an
		// assembly the caller never named — violating the explicit-scan fail-fast contract and risking a
		// double-scan (duplicate descriptors). An explicit caller who wants Bcl.Core scanned must name it.

		// PRIMARY GATE — marker presence. AutoDiBypass is higher precedence than the scan marker.
		if (isExplicitScan)
		{
			// Fail-fast: a caller who explicitly names an assembly expects it scanned; an unmarked one
			// (that is not deliberately bypassed) is a caller error, not a silent drop.
			var unmarked = candidates
				.Where(assembly => !_IsBypassed(assembly) && !_IsScanMarked(assembly))
				.Distinct()
				.ToList();
			if (unmarked.Count > 0)
			{
				var offenders = string.Join("\n", unmarked.Select(a => $"  - {a.FullName}"));
				__.Throw(
					"AddNotNotDiServices was passed explicit assemblies that lack " +
					"[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]. Add the marker to each " +
					"assembly that participates in auto-registration, or remove it from the scan list " +
					$"(apply [assembly: AutoDiBypass] to deliberately opt out):\n{offenders}");
			}
		}

		var targetAssemblies = candidates
			.Where(assembly => _IsScanMarked(assembly) && !_IsBypassed(assembly))
			.Distinct()
			.ToList();

		// SECONDARY GATE — optional name-glob narrowing over the already-marked set.
		if (scanIgnore is null || !scanIgnore.Any())
		{
			return targetAssemblies;
		}

		var removeMatcher = new Matcher();
		removeMatcher.AddIncludePatterns(scanIgnore);

		var keepMatcher = new Matcher();
		if (keepRegardless is not null)
		{
			keepMatcher.AddIncludePatterns(keepRegardless);
		}

		for (var i = targetAssemblies.Count - 1; i >= 0; i--)
		{
			var current = targetAssemblies[i];
			var name = current.FullName;
			var removeResults = removeMatcher.Match(name);
			if (removeResults.HasMatches)
			{
				var keepResults = keepMatcher.Match(name);
				if (keepResults.HasMatches is false)
				{
					targetAssemblies.RemoveAt(i);
				}
			}
		}

		return targetAssemblies;
	}

	/// <summary>True when the assembly carries <c>[assembly: AutoDiScanAssembly]</c>.</summary>
	private static bool _IsScanMarked(Assembly assembly)
		=> assembly.IsDefined(typeof(AutoDiScanAssemblyAttribute), inherit: false);

	/// <summary>True when the assembly carries <c>[assembly: AutoDiBypass]</c> (higher precedence than the scan marker).
	/// The canonical assembly-level bypass check — the single owner of this predicate.</summary>
	internal static bool _IsBypassed(Assembly assembly)
		=> assembly.IsDefined(typeof(AutoDiBypassAttribute), inherit: false);
}

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to auto-register DI services
/// using marker interfaces (<see cref="IDiScopedService"/>, <see cref="IDiSingletonService"/>, <see cref="IDiTransientService"/>).
/// <para>Works with any DI container that exposes <see cref="IServiceCollection"/> — including
/// <c>WebApplicationBuilder</c>, <c>WebAssemblyHostBuilder</c>, and <c>HostApplicationBuilder</c>.</para>
/// </summary>
public static class zz_Extensions_IServiceCollection_DI
{
	/// <summary>
	/// Scans the specified assemblies for types implementing <see cref="IDiScopedService"/>,
	/// <see cref="IDiSingletonService"/>, <see cref="IDiTransientService"/>, and <see cref="IHostedService"/>,
	/// and auto-registers them. Also decorates <see cref="IDiAutoInitialize"/> services.
	/// </summary>
	/// <param name="services">The service collection to register into.</param>
	/// <param name="scanAssemblies">
	/// Assemblies to scan. <c>null</c> => default-AppDomain path (scans marked assemblies in
	/// <c>AppDomain.CurrentDomain.GetAssemblies()</c>; unmarked silently excluded). A non-null list =>
	/// explicit path (fail-fast on any named assembly lacking <c>[assembly: AutoDiScanAssembly]</c>).
	/// </param>
	/// <param name="scanIgnore">Optional secondary assembly-name glob filter applied AFTER the
	/// <c>[assembly: AutoDiScanAssembly]</c> marker gate. Defaults to <c>null</c> (marker gate is the
	/// sole filter).</param>
	public static void AddNotNotDiServices(this IServiceCollection services,
		IEnumerable<Assembly>? scanAssemblies = null,
		IEnumerable<string>? scanIgnore = null)
	{
		var targetAssemblies = AssemblyReflectionHelper._FilterAssemblies(
			scanAssemblies: scanAssemblies,
			scanIgnore: scanIgnore);

		_ScrutorRegisterServiceInterfaces(services, targetAssemblies);
		_DecorateAutoInitializeServices(services);
	}

	/// <summary>
	/// Convenience overload: scans a specific explicit assembly list (WASM clients scanning known
	/// library assemblies). Routes through the same marker gate — each named assembly MUST carry
	/// <c>[assembly: AutoDiScanAssembly]</c> or the scan fails fast.
	/// </summary>
	public static void AddNotNotDiServices(this IServiceCollection services, params Assembly[] assemblies)
	{
		var targetAssemblies = AssemblyReflectionHelper._FilterAssemblies(scanAssemblies: assemblies);

		_ScrutorRegisterServiceInterfaces(services, targetAssemblies);
		_DecorateAutoInitializeServices(services);
	}

	/// <summary>
	/// Registers all services implementing marker interfaces via Scrutor assembly scanning.
	/// </summary>
	[SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
	internal static void _ScrutorRegisterServiceInterfaces(IServiceCollection services, IEnumerable<Assembly> targetAssemblies)
	{
		var runtimeScanAssemblies = targetAssemblies
			.Where(assembly => !AssemblyReflectionHelper._IsBypassed(assembly))
			.Distinct()
			.ToArray();

		// Validate: IHostedService implementations must not also implement DI marker interfaces
		var invalidServices = runtimeScanAssemblies
			.SelectMany(assembly => assembly.GetTypes())
			.Where(type => !_IsRuntimeAutoDiBypassed(type) &&
						   typeof(IHostedService).IsAssignableFrom(type) &&
						   (typeof(IDiSingletonService).IsAssignableFrom(type) ||
							typeof(IDiTransientService).IsAssignableFrom(type) ||
							typeof(IDiScopedService).IsAssignableFrom(type)))
			.Select(type => new
			{
				Type = type,
				Violations = new[]
				{
					typeof(IDiSingletonService).IsAssignableFrom(type) ? "ISingletonService" : null,
					typeof(IDiTransientService).IsAssignableFrom(type) ? "ITransientService" : null,
					typeof(IDiScopedService).IsAssignableFrom(type) ? "IScopedService" : null
				}.Where(x => x != null)
			})
			.ToList();

		if (invalidServices.Any())
		{
			var errorMessage = "DI services cannot implement both IHostedService and other auto registering interfaces:\n\n" +
				string.Join("\n", invalidServices.Select(s =>
					$"- {s.Type.FullName}\n  Conflicting interfaces: {string.Join(", ", s.Violations)}"));
			__.Throw(errorMessage);
		}

		services.Scan(scan =>
		{
			// IHostedService → Singleton
			scan.FromAssemblies(runtimeScanAssemblies)
				.AddClasses(classes => classes.AssignableTo<IHostedService>()
					.Where(type => !_IsRuntimeAutoDiBypassed(type)))
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithSingletonLifetime();

			// IDiSingletonService → Singleton
			scan.FromAssemblies(runtimeScanAssemblies)
				.AddClasses(classes => classes.AssignableTo<IDiSingletonService>()
					.Where(type => !_IsRuntimeAutoDiBypassed(type)))
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithSingletonLifetime();

			// IDiTransientService → Transient
			scan.FromAssemblies(runtimeScanAssemblies)
				.AddClasses(classes => classes.AssignableTo<IDiTransientService>()
					.Where(type => !_IsRuntimeAutoDiBypassed(type)))
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithTransientLifetime();

			// IDiScopedService → Scoped
			scan.FromAssemblies(runtimeScanAssemblies)
				.AddClasses(classes => classes.AssignableTo<IDiScopedService>()
					.Where(type => !_IsRuntimeAutoDiBypassed(type)))
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithScopedLifetime();
		});

		// Validate no duplicate IHostedService registrations
		var manualHostedServiceRegistrations = services
			.Where(sd => sd.ServiceType == typeof(IHostedService) &&
						 sd.ImplementationType != null &&
						 !sd.ImplementationType.FullName!.StartsWith("Microsoft.") &&
						 runtimeScanAssemblies.Contains(sd.ImplementationType.Assembly) &&
						 !_IsRuntimeAutoDiBypassed(sd.ImplementationType))
			.Select(sd => sd.ImplementationType!)
			.ToList();

		if (manualHostedServiceRegistrations.Any())
		{
			var msg = string.Join("\n", manualHostedServiceRegistrations.Select(t =>
				$"  - {t.FullName}"));
			__.Throw($"Manual IHostedService registrations detected. " +
				$"IHostedService implementations are auto-registered by Scrutor - do NOT call AddHostedService<T>().\n{msg}");
		}
	}

	private static bool _IsRuntimeAutoDiBypassed(Type type)
		=> type.IsDefined(typeof(AutoDiBypassAttribute), inherit: false)
			|| AssemblyReflectionHelper._IsBypassed(type.Assembly);

	/// <summary>
	/// Decorates all <see cref="IDiAutoInitialize"/> services with auto-initialization calls.
	/// </summary>
	internal static void _DecorateAutoInitializeServices(IServiceCollection services)
	{
		foreach (var serviceDescriptor in services.ToList())
		{
			var serviceType = serviceDescriptor.ServiceType;

			if (typeof(IDiAutoInitialize).IsAssignableFrom(serviceType))
			{
				// ok
			}
			else if (serviceDescriptor.ImplementationType != null && typeof(IDiAutoInitialize).IsAssignableFrom(serviceDescriptor.ImplementationType))
			{
				// ok
			}
			else if (serviceDescriptor.ImplementationInstance != null && typeof(IDiAutoInitialize).IsAssignableFrom(serviceDescriptor.ImplementationInstance.GetType()))
			{
				// ok
			}
			else
			{
				continue;
			}

			Decorate_AutoInit_ServiceRegistrationUpdater.DecorateService(services, serviceDescriptor);
		}
	}
}
