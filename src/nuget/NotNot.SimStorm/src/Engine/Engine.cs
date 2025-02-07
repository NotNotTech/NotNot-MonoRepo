// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NotNot.Diagnostics.Advanced;
using NotNot.SimStorm._scratch.Ecs;
using NotNot.SimStorm._scratch.Rendering;

namespace NotNot.SimStorm.Engine;

public class Engine : DisposeGuard
{
	private SimManager _simManager;

	public ILogger _log = __.GetLogger<Engine>();
	public RootNode RootNode => _simManager.root;

	/// <summary>
	///    pumps the update loop. you to pass a new <see cref="HeadlessUpdater" />, or perhaps `ExternalLoopUpdater`.
	/// </summary>
	public required IUpdatePump Updater { get; init; }

	public Task MainThread => Updater.MainThread;

	public bool IsInitialized { get; private set; }

	public bool ShouldStop
	{
		get => Updater.ShouldStop;
		set => Updater.ShouldStop = value;
	}

	public virtual ValueTask Initialize(CancellationToken ct = default)
	{
		__.GetLogger()._EzError(IsInitialized is false, "already initialized");
		IsInitialized = true;

		__.GetLogger()._EzErrorThrow<SimStormException>(Updater != null,
			"you must set the Updater property before calling Initialize()");

		_simManager = new SimManager(this);

		Updater.OnUpdate += OnUpdate;
		Updater.OnRunningLate += OnRunningLate;

		return ValueTask.CompletedTask;
	}

	protected virtual async ValueTask OnRunningLate()
	{
		//_log._EzWarn("why running late?!");
	}
	protected virtual async ValueTask OnUpdate(TimeSpan elapsed)
	{
		await _simManager.Update(elapsed);

		//return ValueTask.CompletedTask;
	}

	public void Start()
	{
		Updater.Start();
	}

	public Task Stop()
	{
		return Updater.Stop();
	}


	protected override void OnDispose(bool managedDisposing)
	{
		//Updater?.Stop()._SyncWaitNoCancelException();
		Updater?.Dispose();

		_simManager?.Dispose();
		_simManager = null;


		base.OnDispose(managedDisposing);
	}
}

public interface IUpdatePump : IDisposable
{
	public Task MainThread { get; }
	public bool ShouldStop { get; set; }
	public event Func<TimeSpan, ValueTask> OnUpdate;
	public event Func<ValueTask> OnRunningLate;

	//void StopUiThread()
	public Task Stop();
	public void Start();
}

public class HeadlessUpdater : DisposeGuard, IUpdatePump
{
	/// <summary>
	///    the (weighted) average elapsed time of the last 10 frames.
	///    this will be used as the elapsed time when the debugger is paused/stepped through.
	/// </summary>
	public TimeSpan AvgElapsed { get; private set; } = TimeSpan.FromMilliseconds(1);
	//System.Threading.SemaphoreSlim runningLock = new(1);

	public event Func<TimeSpan, ValueTask> OnUpdate;

	/// <summary>
	/// never called in headless updater
	/// </summary>
	public event Func<ValueTask> OnRunningLate;


	public Task MainThread { get; private set; }

	public void Start()
	{
		MainThread = __.Async.LongRun(_MainLoop);


		async Task _MainLoop()
		{
			var sw = Stopwatch.StartNew();
			var loop = 0;
			//await runningLock.WaitAsync();
			while (ShouldStop == false)
			{
				await Task.Yield(); //yield to other threads once per loop
				loop++;
				var lastElapsed = sw.Elapsed; // -  Stopwatch.GetTimestamp() - start;
				if (DebuggerInfo.IsPaused)
				{
					lastElapsed = AvgElapsed;
				}
				else
				{
					//weighted avg
					AvgElapsed = ((AvgElapsed * 9) + lastElapsed) / 10;
				}

				sw.Restart();
				//Console.WriteLine($" ======================== {loop} ({Math.Round(TimeSpan.FromTicks(lastElapsed).TotalMilliseconds,1)}ms) ============================================== ");
				await OnUpdate(lastElapsed);
				//Console.WriteLine($"last Elapsed = {lastElapsed}");


				//runningLock.Release();
				//await runningLock.WaitAsync();
			}
			//runningLock.Release();
		}
	}

	public bool ShouldStop { get; set; }


	public Task Stop()
	{
		ShouldStop = true;
		return MainThread ?? Task.CompletedTask;
		//await runningLock.WaitAsync();
		//IsRunning= false;
		//runningLock.Release();
	}

	protected override void OnDispose(bool managedDisposing)
	{
		ShouldStop = true;
		Stop()._SyncWait();
		base.OnDispose(managedDisposing);
	}
}

/// <summary>
///    This system runs at the very start of every frame, in exclusive mode (no other systems running yet).
/// </summary>
public class Phase0_StateSync : SystemBase
{
	//public FrameDataChannel<IRenderPacketNew> renderPackets = new(1);


	public FrameDataChannelSlim<RenderFrame> renderChannel = new(1);

	protected override ValueTask OnInitialize()
	{
		AddField(renderChannel);

		return base.OnInitialize();
	}

	protected override async Task OnUpdate(Frame frame)
	{
		var i = 0;
		i++;

		//renderPackets.EndFrameAndEnqueue();

		//__.GetLogger()._EzError(renderPackets.CurrentFramePacketDataCount == 0,
		//	"should not race with main thread to write packets");
		await base.OnUpdate(frame);
		//return Task.CompletedTask;
	}
}