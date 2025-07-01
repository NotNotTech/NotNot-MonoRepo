namespace NotNot.SimStorm;

/// <summary>
///    a dummy node, special because it is the root of the simulation hierarchy.  All other <see cref="SimNode" /> get
///    added under it.
/// </summary>
public class RootNode : SimNode, IIgnoreUpdate
{
	protected override Task OnUpdate(Frame frame, NodeFrameState nodeState)
	{
		throw new Exception("This exception will never be thrown because this implements IIgnoreUpdate");
	}

	protected override void OnAdded()
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(false,
			"root node is special, should not be added/removed, only manually placed at root of SimManager");
		base.OnAdded();
	}

	protected override void OnRemoved()
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(false,
			"root node is special, should not be added/removed, only manually placed at root of SimManager");
		base.OnRemoved();
	}
}
