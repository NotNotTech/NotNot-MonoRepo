using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;


public static class zz_Extensions_HostApplicationBuilder
{
	// DI scanning (_ScrutorRegisterServiceInterfaces, _DecorateAutoInitializeServices, TypeDiscovery,
	// _ScrutorHookAutoInitialize_old) have been moved to NotNot.Bcl.Core.
	// See: NotNot.Bcl.Core/NotNot/DI/zz_Extensions_IServiceCollection_DI.cs
	// This class now delegates to AddNotNotDiServices() and retains only Serilog/logging config.

	/// <summary>
	/// Configures Serilog logging and auto-registers DI services via Scrutor marker interfaces
	/// (IDiSingletonService, IDiScopedService, IDiTransientService, IHostedService).
	/// DI scanning logic lives in NotNot.Bcl.Core via AddNotNotDiServices().
	/// </summary>
	/// <param name="builder"></param>
	/// <param name="ct"></param>
	/// <param name="scanAssemblies">assemblies you want to scan for scrutor types.  default is everything: AppDomain.CurrentDomain.GetAssemblies()</param>
	/// <param name="scanIgnore">assemblies to not scan for DI types.   if null is passed, the default will be ["Microsoft.*", "netstandard*", "Serilog*", "System*", "Azure*"] because ASP NetCore IHostedService internal registrations conflict, and others are internal packages.
	/// <para>example of the assembly name that will be matched against:  "Cleartrix.Cloud.WebApi, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"</para></param>
	/// <returns></returns>
	public static async Task _NotNotEzSetup(this IHostApplicationBuilder builder, CancellationToken ct, IEnumerable<Assembly>? scanAssemblies = null
		, IEnumerable<string>? scanIgnore = null, Action<IConfiguration, LoggerConfiguration> extraLoggerConfig = null)
	{
		await _NotNotUtils_ConfigureLogging(builder, ct, extraLoggerConfig);

		try
		{
			builder.Services.AddNotNotDiServices(scanAssemblies, scanIgnore);
		}
		catch (Exception ex)
		{
			throw new Exception("If this is an assembly load exception, try adding it's referencing assembly to 'scanIgnore'", ex);
		}
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
					// Greenfield: if AI is configured, it should work - let exceptions propagate
					loggerConfiguration = loggerConfiguration.WriteTo.ApplicationInsights(
						connectionString: aiConnectionString,
						telemetryConverter: new Serilog.Sinks.ApplicationInsights.TelemetryConverters.TraceTelemetryConverter(),
						restrictedToMinimumLevel: LogEventLevel.Information
					);
				}

				if (extraLoggerConfig != null)
				{
					extraLoggerConfig(builder.Configuration, loggerConfiguration);
				}

			},
			writeToProviders: true
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
}
