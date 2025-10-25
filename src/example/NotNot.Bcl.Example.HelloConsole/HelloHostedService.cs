using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NotNot.Example.HelloConsole;

public class HelloHostedService(ILogger<HelloHostedService> _logger)

	: BackgroundService //background service implements IHostedService, which is automatically registered and launched on `host.RunAsync();`
		, IDiAutoInitialize //allows this service to have an AutoInitialize() method called when it is created by DI, allowing for custom initialization logic.
{

	public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	{
		_logger._EzTrace("HelloHostedService initialized!");
	}

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		_logger._EzTrace("Hello from the hosted service!");
		return base.StartAsync(cancellationToken);
	}

	public override Task StopAsync(CancellationToken cancellationToken)
	{
		_logger._EzTrace("Goodbye from the hosted service!");
		return base.StopAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		stoppingToken.ThrowIfCancellationRequested();
		while (!stoppingToken.IsCancellationRequested)
		{
			_logger._EzError($"Background task running at: {DateTimeOffset.Now}");
			// Perform background work here
			await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
		}
	}

}
