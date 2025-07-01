namespace NotNot.SimStorm;

/// <summary>
///    a lightweight node that can be attached to a SimNode to hook into the Initialize/Dispose/Update workflow
/// </summary>
public interface ISystemField
{
	ValueTask OnInitialize(SystemBase parent);

	Task OnUpdate(Frame frame);

	void Dispose();
}
