using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace NotNot.Example.HelloConsole;

internal class Program
{
	private static async Task Main(string[] args)
	{
		Console.Clear();
		
		var logger = NotNot.LoLoRoot.__.GetLogger();
		logger._EzTrace("starting app scafolding (Microsoft.Extensions.Hosting)");

		logger._EzTrace("running DI setup. (builder)....");
		var builder = Host.CreateApplicationBuilder(args);
		
		//required settings so things like logging behaves as expected
		builder.Configuration.AddJsonFile("appsettings.json", optional: false);

		//configure app, services that need specific initialization that can not be done through IAutoInitialize
		{
			logger._EzTrace("applying any app specific settings....");
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
		NotNot.LoLoRoot.__.Services = host.Services;

		logger._EzTrace("run app (runs our hosted service)...  (next line blocks until app exits)");
		await host.RunAsync();
		

		Console.WriteLine("Done!  (host + services are now disposed)");

	}
}