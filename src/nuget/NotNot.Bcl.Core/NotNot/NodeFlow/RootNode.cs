
namespace NotNot.NodeFlow;

public class RootNode : SlimNode
{


	public override bool IsRoot { get; init; } = true;

	public TickState CurrentTick { get; protected set; }

	/// <summary>
	/// for external code to pump the update, usually in time to the physics (60fps)
	/// </summary>
	/// <param name="wallTimeDelta"></param>
	/// <returns></returns>
	public ValueTask RootUpdate(TimeSpan wallTimeDelta)
	{
		CurrentTick = TickState.ComputeNext(CurrentTick, wallTimeDelta);
		return Update(CurrentTick);
	}

	public ValueTask RootInitialize(CancellationToken ct)
	{
		return this.Initialize(ct);
	}
}
