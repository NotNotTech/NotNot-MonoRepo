using NotNot.SimStorm._scratch.Ecs;
using NotNot.SimStorm.Engine;

namespace NotNot.SimStorm._scratch;

public class EcsEngine : Engine.Engine
{
	//private SimManager _simManager;

	//public RootNode RootNode { get => _simManager.root; }

	public Phase0_StateSync StateSync { get; set; } = new() { Name = "!!_StateSync" };

	public World DefaultWorld { get; set; } = new() { Name = "!!_DefaultWorld" };

	public ContainerNode Rendering { get; } = new() { Name = "!!_Rendering", _updateAfter = { "!!_StateSync" } };
	public ContainerNode Worlds { get; } = new() { Name = "!!_Worlds", _updateAfter = { "!!_Rendering" } };

	//public IUpdatePump Updater = new HeadlessUpdater();


	// public Task MainThread { get => Updater.MainThread; }

	public override async ValueTask Initialize(CancellationToken ct = default)
	{
		await base.Initialize(ct);

		RootNode.AddChild(StateSync);
		RootNode.AddChild(Rendering);
		RootNode.AddChild(Worlds);

		if (DefaultWorld is not null)
		{
			await DefaultWorld.Initialize();
			Worlds.AddChild(DefaultWorld);
		}
	}


	protected override void OnDispose(bool managedDisposing)
	{
		base.OnDispose(managedDisposing);

		__.GetLogger()._EzErrorThrow<SimStormException>(DefaultWorld.IsDisposed,
			"disposing simManager should have disposed all nodes inside");
		DefaultWorld = null;
	}
}

//public class MyClass
//{

//   public async Task SomeAsyncMethod()
//   {
//      await Task.Delay(100);
//   }

//   public Task SomeOtherMethod()
//   {
//      this.SomeAsyncMethod();

//      return Task.CompletedTask;
//	}

//}
