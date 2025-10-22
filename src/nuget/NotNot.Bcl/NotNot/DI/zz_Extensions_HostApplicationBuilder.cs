using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotNot.Collections;
using NotNot.DI.Advanced;
using Scrutor;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;



public static class zz_Extensions_HostApplicationBuilder
{

	private static ObjectTrackingCollection<object> _initializedServiceTracker = new();

	/// <summary>
	/// registeres services extending interfaces like ISingleton and also decorates services implementing IAutoInitialize with a call to .AutoInitialize()
	/// </summary>
	/// <param name="services"></param>
	/// <param name="ct"></param>
	/// <param name="scanAssemblies">assemblies you want to scan for scrutor types.  default is everything: AppDomain.CurrentDomain.GetAssemblies()</param>
	/// <param name="scanIgnore">assemblies to not scan for DI types.   if null is passed, the default will be ["Microsoft.*", "netstandard*", "Serilog*", "System*"] because ASP NetCore IHostedService internal registrations conflict, and others are internal packages.</param>
	/// <returns></returns>
	public static async Task _NotNotEzSetup(this IHostApplicationBuilder builder, CancellationToken ct, IEnumerable<Assembly>? scanAssemblies = null
		, IEnumerable<string>? scanIgnore = null, Action<IConfiguration, LoggerConfiguration> extraLoggerConfig = null)
	{
		scanIgnore ??= ["Microsoft.*", "netstandard*", "Serilog*", "System*", "Azure*"];
		await _NotNotUtils_ConfigureLogging(builder, ct, extraLoggerConfig);

		var targetAssemblies = AssemblyReflectionHelper._FilterAssemblies(
			scanAssemblies: scanAssemblies
			, scanIgnore: scanIgnore
			//, keepRegardless:new[] { "Microsoft.CodeAnalysis" }
			);



		try
		{
			//add scutor found in all assemblies
			await _ScrutorRegisterServiceInterfaces(builder, ct, targetAssemblies);

		}
		catch (Exception ex)
		{
			throw new Exception("If this is an assembly load exception, try adding it's referencing assembly to 'scanIgnore'", ex);
		}


		await _DecorateAutoInitializeServices(builder, ct);





	}



	internal static async Task _NotNotUtils_ConfigureLogging(this IHostApplicationBuilder builder, CancellationToken ct, Action<IConfiguration, LoggerConfiguration> extraLoggerConfig = null)
	{

		//config logging
		//before aspire, we cleared all providers then rebuilt our logging providers.  we can't do that now, because it will unhook aspire, which was configured earlier.
		//builder.Logging.ClearProviders();
		//serilog will log to console, so we will remove the default console logger
		builder.Services.RemoveAll<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>();
		//also remove default debug provider, as this is also what serilog does.
		builder.Services.RemoveAll<Microsoft.Extensions.Logging.Debug.DebugLoggerProvider>();


		builder.Services.AddSerilog((hostingContext, loggerConfiguration) =>
			{
				loggerConfiguration = loggerConfiguration.ReadFrom.Configuration(builder.Configuration)
#if DEBUG

				.WriteTo.Sink(new NotNot.Logging.AssertOnMsgSink(builder.Configuration), LogEventLevel.Error)
					//	.AssertOnMsgSinkWithoutBatching(builder.Configuration, LogEventLevel.Warning)
#endif
					;

				//// Add Azure App Service file sink if running on Azure
				//var isAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
				//if (isAzure)
				//{
				//	loggerConfiguration = loggerConfiguration.WriteTo.File(
				//		path: @"D:\home\LogFiles\Application\cleartrix-app-.log",
				//		rollingInterval: Serilog.RollingInterval.Day,
				//		fileSizeLimitBytes: 10_000_000,
				//		retainedFileCountLimit: 7,
				//		shared: true,
				//		flushToDiskInterval: TimeSpan.FromSeconds(5),
				//		outputTemplate: "<{Timestamp:HH:mm:ss.fff}> [{Level:u}] {Message:w} <s:{SourceContext}>{NewLine}{Exception}"
				//	);
				//}

				// Add Application Insights sink if connection string available
				var aiConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights")
					?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

				if (!string.IsNullOrEmpty(aiConnectionString))
				{
					try
					{
						loggerConfiguration = loggerConfiguration.WriteTo.ApplicationInsights(
							connectionString: aiConnectionString,
							telemetryConverter: new Serilog.Sinks.ApplicationInsights.TelemetryConverters.TraceTelemetryConverter(),
							restrictedToMinimumLevel: LogEventLevel.Information
						);
					}
					catch (Exception ex)
					{
						// Log error but don't fail application startup if AI is misconfigured
						Console.WriteLine($"Warning: Failed to configure Application Insights logging: {ex.Message}");
					}
				}

				if (extraLoggerConfig != null)
				{
					extraLoggerConfig(builder.Configuration, loggerConfiguration);
				}

			}
		);
	}
	public static LoggerConfiguration AssertOnMsgSinkWithoutBatching(
		this LoggerConfiguration loggerConfiguration,
		IConfiguration configuration,
		LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
		LoggingLevelSwitch? levelSwitch = null)
	{
		return loggerConfiguration.WriteTo.Sink(
			new NotNot.Logging.AssertOnMsgSink(configuration),
			restrictedToMinimumLevel,
			levelSwitch);
	}

	/// <summary>
	/// hooks up all services that implement ISingleton, ITransient, IScoped to be auto-registered
	/// </summary>
	/// <param name="services"></param>
	/// <param name="ct"></param>
	/// <returns></returns>
	[SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
	internal static async Task _ScrutorRegisterServiceInterfaces(this IHostApplicationBuilder builder, CancellationToken ct, IEnumerable<Assembly> targetAssemblies)
	{


		//in targetAssemblies, ensure that any IHostedService do not also implement ISingletonService, ITransientService, IScopedService
		//this is because IHostedService is already registered as a singleton by default

		var invalidServices = targetAssemblies
			 .SelectMany(assembly => assembly.GetTypes())
			 .Where(type => typeof(IHostedService).IsAssignableFrom(type) &&
								(typeof(ISingletonService).IsAssignableFrom(type) ||
								 typeof(ITransientService).IsAssignableFrom(type) ||
								 typeof(IScopedService).IsAssignableFrom(type)))
			 .Select(type => new
			 {
				 Type = type,
				 Violations = new[]
				  {
						  typeof(ISingletonService).IsAssignableFrom(type) ? "ISingletonService" : null,
						  typeof(ITransientService).IsAssignableFrom(type) ? "ITransientService" : null,
						  typeof(IScopedService).IsAssignableFrom(type) ? "IScopedService" : null
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


		//logger.LogTrace("utilizing Scrutor auto-registration of DI services....");

		//use Scrutor nuget library to auto-register DI Services
		builder.Services.Scan(scan =>
		{

			{
				//register all services that implement IHostedService
				scan.FromAssemblies(targetAssemblies)
					.AddClasses(classes => classes.AssignableTo<IHostedService>())
					.UsingRegistrationStrategy(RegistrationStrategy.Append)
					.AsSelfWithInterfaces()
					.WithSingletonLifetime();
			}

			{
				//register all services that implement ISingletonService
				//as interface, append
				scan.FromAssemblies(targetAssemblies)
					.AddClasses((classes) =>
					{
						var result = classes.AssignableTo<ISingletonService>();

					})
					//.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
					//                                          t.GetInterfaces().Any(i => i.IsGenericType &&
					//                                                                     i.GetGenericTypeDefinition() == typeof(TInterface).GetGenericTypeDefinition())))
					//.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
					//                                          t.BaseType != null && t.BaseType.GetInterfaces().Any(i => i.IsGenericType &&
					//                                                                                                    i.GetGenericTypeDefinition() == typeof(TInterface).GetGenericTypeDefinition())))
					//.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
					//                                          t.BaseType != null && t.BaseType._IsAssignableTo<TInterface>()))
					//.AddClasses(classes => classes.Where(t => !t.IsGenericTypeDefinition &&
					//                                          t._IsAssignableTo<TInterface>()))

					.UsingRegistrationStrategy(RegistrationStrategy.Append)
					.AsSelfWithInterfaces()
					.WithSingletonLifetime();
			}
			{
				//			//register all services that implement ITransientService
				//as interface, append
				scan.FromAssemblies(targetAssemblies)
					.AddClasses(classes => classes.AssignableTo<ITransientService>())
					.UsingRegistrationStrategy(RegistrationStrategy.Append)
					.AsSelfWithInterfaces()
					.WithTransientLifetime();
			}

			{
				//register all services that implement IScopedService
				//as interface, append
				scan.FromAssemblies(targetAssemblies)
					.AddClasses(classes => classes.AssignableTo<IScopedService>())
					.UsingRegistrationStrategy(RegistrationStrategy.Append)
					.AsSelfWithInterfaces()
					.WithScopedLifetime()
					;
			}

		});

	}


	//// Skip open generic types
	/// if (serviceType.Name.StartsWith("Cache"))
	/// {
	/// var xxx = 0;
	/// }
	//if (serviceType.IsGenericTypeDefinition)
	//{
	//   continue;
	//}

	public static class TypeDiscovery
	{
		public static List<Type> FindClosedGenericsOfOpenGeneric(Type openGenericType)
		{



			if (!openGenericType.IsGenericTypeDefinition)
			{
				throw new ArgumentException("The provided type must be an open generic type", nameof(openGenericType));
			}
			// Get all loaded assemblies
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

			var closedTypes = new List<Type>();

			foreach (var assembly in assemblies)
			{
				foreach (var type in assembly.GetTypes())
				{
					// Check if the type is a generic type and if it is constructed from the openGenericType
					if (type.IsGenericType && type.GetGenericTypeDefinition() == openGenericType)
					{
						closedTypes.Add(type);
					}
				}
			}

			return closedTypes;
		}
	}

	internal static async Task _DecorateAutoInitializeServices(this IHostApplicationBuilder builder, CancellationToken ct)
	{
		var services = builder.Services;

		foreach (var serviceDescriptor in services.ToList())
		{
			var serviceType = serviceDescriptor.ServiceType;


			if (typeof(IAutoInitialize).IsAssignableFrom(serviceType))
			{
				//ok
			}
			else if (serviceDescriptor.ImplementationType != null && typeof(IAutoInitialize).IsAssignableFrom(serviceDescriptor.ImplementationType))
			{
				//ok
			}
			else if (serviceDescriptor.ImplementationInstance != null && typeof(IAutoInitialize).IsAssignableFrom(serviceDescriptor.ImplementationInstance.GetType()))
			{
				//ok
			}
			else
			{
				//not assignable
				continue;
			}

			Decorate_AutoInit_ServiceRegistrationUpdater.DecorateService(services, serviceDescriptor);

		}
	}


	/// <summary>
	/// hooks up all services that implement IAutoInitialize to be decorated with a call to .AutoInitialize()
	/// </summary>
	internal static async Task _ScrutorHookAutoInitialize_old(this IHostApplicationBuilder builder, CancellationToken ct)
	{
		var hasImplementation = builder.Services.Where(sd =>
		{
			var serviceType = sd.ServiceType;

			if (typeof(IAutoInitialize).IsAssignableFrom(serviceType))
			{
				return true;
			}

			//can't check factory return type, so always have to decorate and try their returned result
			if (sd.ImplementationFactory != null)
			{
				return true;
			}

			//check if ImplementationInstance inherits from IInitializeableService
			if (sd.ImplementationInstance != null)
			{
				if (typeof(IAutoInitialize).IsAssignableFrom(sd.ImplementationInstance.GetType()))
				{
					return true;
				}

				return false;
			}

			//check if ImplementationType inherits from IInitializeableService
			if (sd.ImplementationType != null)
			{
				if (typeof(IAutoInitialize).IsAssignableFrom(sd.ImplementationType))
				{
					return true;
				}

				return false;
			}

			return false;
		});

		var groupedByServiceType = hasImplementation
			.GroupBy(serviceDescriptor => serviceDescriptor.ServiceType);

		List<Type> serviceTypes = groupedByServiceType.Select(grouping => grouping.Key).ToList();

		foreach (Type serviceType in serviceTypes)
		{
			try
			{
				var _myServiceType1 = serviceType;
				builder.Services.Decorate(serviceType, (innerService, serviceProvider) =>
				{
					var _myServiceType2 = serviceType;
					if (innerService is IAutoInitialize initService)
					{
						//ensure that we only call .AutoInitialize() once per object, first time it's requested
						if (_initializedServiceTracker.TryAdd(innerService))
						{
							initService.AutoInitialize(serviceProvider, ct)._SyncWait();
						}
					}

					return innerService;
				});
			}
			catch (DecorationException ex)
			{
				__.GetLogger()._EzError("error calling .AutoInitialzie() on  decorated service. ", serviceType, ex);
			}
		}
	}

}
