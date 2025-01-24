using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NotNot.Example.HelloConsole;

internal class Program
{
	private static async Task Main(string[] args)
	{
		Console.Clear();


		//var hostBuilder = new HostBuilder()
		//	.ConfigureServices(services =>
		//		services.AddHostedService<HelloHostedService2>());
		//var host = hostBuilder.Build();
		//__.Services = host.Services;
		//await host.RunAsync();

		var logger = __.GetLogger();
		logger._EzTrace("starting app scafolding (Microsoft.Extensions.Hosting)");

		logger._EzTrace("running DI setup. (builder)....");
		var builder = Host.CreateApplicationBuilder(args);
		//sql / etc required settings in this file
		builder.Configuration.AddJsonFile("appsettings.json", optional: true);

		//configure app, services that need specific initialization that can not be done through IAutoInitialize
		{
			logger._EzTrace("applying our app specific settings....");
			////setup our dbContext
			//builder.Services.AddDbContext<PoliceDbContext>(
			//	contextLifetime: ServiceLifetime.Singleton
			//	, optionsAction: options => { options.UseNpgsql(builder.Configuration.GetConnectionString("SqlDbConnection")); }
			//);
		}

		//builder.Services.AddHostedService<ErrorReporting>();

		logger._EzTrace("utilizing Scrutor to Decorate AutoInitialize those services inheriting IAutoInitialize....");
		await builder._NotNotEzSetup(CancellationToken.None);

		logger._EzTrace("performing final DI Build step....");
		//build all di services
		var host = builder.Build();
		__.Services = host.Services;

		logger._EzTrace("run app (runs our hosted service)...  (next line blocks until app exits)");
		await host.RunAsync();

		Console.WriteLine("Done!  (host + services are now disposed)");

	}
}

public class TestService : IAutoInitialize, ISingletonService
{
	private readonly ILogger<TestService> _logger;
	public TestService(ILogger<TestService> logger)
	{
		_logger = logger;
	}
	public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	{
		_logger._EzInfo("TestService initialized!");
	}
}
public class HelloHostedService(ILogger<HelloHostedService> _logger)
	: BackgroundService, IAutoInitialize, ISingletonService
{
	//public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	//{
	//	__.CheckedExec(() => { __.Validator.Service.MustBeSingleton(this); });
	//}

	//public Task StartAsync(CancellationToken cancellationToken)
	//{
	//	_logger.LogInformation("Hello from the hosted service!");
	//	return Task.CompletedTask;
	//}
	//public Task StopAsync(CancellationToken cancellationToken)
	//{
	//	_logger.LogInformation("Goodbye from the hosted service!");
	//	return Task.CompletedTask;


	//}

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		_logger._EzInfo("Hello from the hosted service!");
		return base.StartAsync(cancellationToken);
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		_logger._EzInfo("Goodbye from the hosted service!");
		return base.StopAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			_logger._EzInfo("Background task running at: {time}", DateTimeOffset.Now);
			// Perform background work here
			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
		}
	}

}

//public class HelloHostedService2()
//	: BackgroundService
//{
	
//	public override Task StartAsync(CancellationToken cancellationToken)
//	{
//		Console.WriteLine("Hello from the hosted service!");
//		return base.StartAsync(cancellationToken);
//	}

//	public override Task StopAsync(CancellationToken cancellationToken)
//	{
//		Console.WriteLine("Goodbye from the hosted service!");
//		return base.StopAsync(cancellationToken);
//	}

//	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//	{
//		while (!stoppingToken.IsCancellationRequested)
//		{
//			Console.WriteLine($"Background task running at: {DateTimeOffset.Now}");
//			// Perform background work here
//			await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
//		}
//	}

//}
