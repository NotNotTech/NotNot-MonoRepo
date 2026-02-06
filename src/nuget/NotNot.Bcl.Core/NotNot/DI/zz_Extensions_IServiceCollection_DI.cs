using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;
using NotNot.DI.Advanced;
using Scrutor;

/// <summary>
/// Provides assembly filtering for DI auto-registration scanning.
/// </summary>
public static class AssemblyReflectionHelper
{
	/// <summary>
	/// Filters assemblies for Scrutor DI scanning.
	/// </summary>
	/// <param name="scanAssemblies">Assemblies to scan. Default: <c>AppDomain.CurrentDomain.GetAssemblies()</c>.</param>
	/// <param name="scanIgnore">Assembly name glob patterns to exclude. Default: <c>["Microsoft.*", "netstandard*", "Serilog*", "System*", "Azure*"]</c>.</param>
	/// <param name="keepRegardless">Assembly name glob patterns to keep even if matched by <paramref name="scanIgnore"/>.</param>
	public static List<Assembly> _FilterAssemblies(IEnumerable<Assembly>? scanAssemblies, IEnumerable<string>? scanIgnore = null, IEnumerable<string>? keepRegardless = null)
	{
		scanAssemblies ??= AppDomain.CurrentDomain.GetAssemblies();
		var targetAssemblies = new List<Assembly>(scanAssemblies);

		var thisAssembly = Assembly.GetExecutingAssembly();
		if (!targetAssemblies.Contains(thisAssembly))
		{
			targetAssemblies.Add(thisAssembly);
		}

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
	/// <param name="scanAssemblies">Assemblies to scan. Default: <c>AppDomain.CurrentDomain.GetAssemblies()</c>.</param>
	/// <param name="scanIgnore">Assembly name glob patterns to exclude from scanning.
	/// Default: <c>["Microsoft.*", "netstandard*", "Serilog*", "System*", "Azure*"]</c>.</param>
	public static void AddNotNotDiServices(this IServiceCollection services,
		IEnumerable<Assembly>? scanAssemblies = null,
		IEnumerable<string>? scanIgnore = null)
	{
		scanIgnore ??= ["Microsoft.*", "netstandard*", "Serilog*", "System*", "Azure*"];

		var targetAssemblies = AssemblyReflectionHelper._FilterAssemblies(
			scanAssemblies: scanAssemblies,
			scanIgnore: scanIgnore);

		_ScrutorRegisterServiceInterfaces(services, targetAssemblies);
		_DecorateAutoInitializeServices(services);
	}

	/// <summary>
	/// Convenience overload: scans specific assemblies with no ignore patterns.
	/// Useful for WASM clients scanning known library assemblies.
	/// </summary>
	public static void AddNotNotDiServices(this IServiceCollection services, params Assembly[] assemblies)
	{
		_ScrutorRegisterServiceInterfaces(services, assemblies);
		_DecorateAutoInitializeServices(services);
	}

	/// <summary>
	/// Registers all services implementing marker interfaces via Scrutor assembly scanning.
	/// </summary>
	[SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
	internal static void _ScrutorRegisterServiceInterfaces(IServiceCollection services, IEnumerable<Assembly> targetAssemblies)
	{
		// Validate: IHostedService implementations must not also implement DI marker interfaces
		var invalidServices = targetAssemblies
			.SelectMany(assembly => assembly.GetTypes())
			.Where(type => typeof(IHostedService).IsAssignableFrom(type) &&
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
			scan.FromAssemblies(targetAssemblies)
				.AddClasses(classes => classes.AssignableTo<IHostedService>())
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithSingletonLifetime();

			// IDiSingletonService → Singleton
			scan.FromAssemblies(targetAssemblies)
				.AddClasses(classes => classes.AssignableTo<IDiSingletonService>())
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithSingletonLifetime();

			// IDiTransientService → Transient
			scan.FromAssemblies(targetAssemblies)
				.AddClasses(classes => classes.AssignableTo<IDiTransientService>())
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithTransientLifetime();

			// IDiScopedService → Scoped
			scan.FromAssemblies(targetAssemblies)
				.AddClasses(classes => classes.AssignableTo<IDiScopedService>())
				.UsingRegistrationStrategy(RegistrationStrategy.Append)
				.AsSelfWithInterfaces()
				.WithScopedLifetime();
		});

		// Validate no duplicate IHostedService registrations
		var manualHostedServiceRegistrations = services
			.Where(sd => sd.ServiceType == typeof(IHostedService) &&
						 sd.ImplementationType != null &&
						 !sd.ImplementationType.FullName!.StartsWith("Microsoft."))
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
