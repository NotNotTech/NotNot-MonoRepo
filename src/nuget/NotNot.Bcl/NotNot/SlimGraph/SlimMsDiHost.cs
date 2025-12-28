using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace NotNot.SlimGraph;

/// <summary>
/// Hosts a SlimGraph RootNode lifecycle within MS Generic Host (BackgroundService).
/// Implements IHostedLifecycleService for additional extensibility hooks.
/// </summary>
/// <typeparam name="TRoot">RootNode subclass to host</typeparam>
/// <remarks>
/// <para>Lifecycle mapping:</para>
/// <list type="bullet">
/// <item><description>StartingAsync → (extensibility hook)</description></item>
/// <item><description>StartAsync/ExecuteAsync → RootNode.RootInitialize(ct), then tick loop</description></item>
/// <item><description>StartedAsync → (extensibility hook)</description></item>
/// <item><description>StoppingAsync → (extensibility hook)</description></item>
/// <item><description>StopAsync → await loop completion, RootNode.Dispose()</description></item>
/// <item><description>StoppedAsync → (extensibility hook)</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var container = new MsDIContainer();
/// await container.Initialize(builder);
///
/// builder.Services.AddHostedService(_ => new SlimMsDiHost&lt;MyGameRoot&gt;(
///     () => new MyGameRoot { MsDIContainer = container },
///     tickInterval: TimeSpan.FromMilliseconds(50)
/// ));
///
/// await Host.CreateDefaultBuilder(args).Build().RunAsync();
/// </code>
/// </example>
public class SlimMsDiHost<TRoot> : BackgroundService, IHostedLifecycleService
	where TRoot : RootNode
{
	private readonly Func<TRoot> _rootFactory;
	private readonly TimeSpan _tickInterval;
	private TRoot? _rootNode;

	/// <summary>
	/// Default tick interval: 50ms (20fps). Suitable for background processing.
	/// </summary>
	public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(50);

	/// <summary>
	/// The hosted RootNode instance. Available after ExecuteAsync starts.
	/// </summary>
	public TRoot? RootNode => _rootNode;

	/// <summary>
	/// Creates a new SlimGraph host for MS Generic Host.
	/// </summary>
	/// <param name="rootFactory">Factory that creates fully-configured RootNode (with MsDIContainer set)</param>
	/// <param name="tickInterval">Update interval. Default: 50ms (20fps)</param>
	public SlimMsDiHost(Func<TRoot> rootFactory, TimeSpan? tickInterval = null)
	{
		_rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
		_tickInterval = tickInterval ?? DefaultTickInterval;
	}

	#region IHostedLifecycleService - Virtual hooks for extensibility

	/// <summary>
	/// Called before StartAsync. Override for early initialization.
	/// </summary>
	public virtual Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// Called after StartAsync completes. Override for post-start notifications.
	/// </summary>
	public virtual Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// Called before StopAsync. Override for pre-shutdown preparation.
	/// </summary>
	public virtual Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// Called after StopAsync completes. Override for post-shutdown cleanup.
	/// </summary>
	public virtual Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	#endregion

	/// <summary>
	/// Main execution loop. Creates RootNode, initializes, then pumps updates.
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Create and initialize RootNode
		_rootNode = _rootFactory();
		try
		{
			await _rootNode.RootInitialize(stoppingToken);
		}
		catch
		{
			// Cleanup if initialization fails (host may terminate without calling StopAsync)
			_rootNode?.Dispose();
			_rootNode = null;
			throw;
		}

		// Tick loop with actual elapsed time measurement
		using var timer = new PeriodicTimer(_tickInterval);
		var sw = Stopwatch.StartNew();

		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			var elapsed = sw.Elapsed;
			sw.Restart();

			try
			{
				await _rootNode.RootUpdate(elapsed);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// Log error and continue (matches Godot pattern)
				__.GetLogger<SlimMsDiHost<TRoot>>()._EzError(ex, "Error in RootUpdate");
			}
		}
	}

	/// <summary>
	/// Stops the host and disposes the RootNode.
	/// </summary>
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken); // Awaits ExecuteAsync completion
		_rootNode?.Dispose();
		_rootNode = null;
	}

	
}
