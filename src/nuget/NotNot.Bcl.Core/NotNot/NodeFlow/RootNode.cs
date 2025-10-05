
namespace NotNot.NodeFlow;

public class RootNode : SlimNode
{


	//protected override bool IsRoot { get; init; } = true;

	//public override RootNode RootNode { get; private set; }

	
	public RootNode()
	{
		IsRoot = true;
		RootNode = this;
	}

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

	protected override ValueTask OnUpdate(TickState currentTick)
	{
		__.AssertIfNot(currentTick == CurrentTick,"we expect RootNode's OnUpdate to be called via the .RootUpdate() method.  did you call .Update() instead?");
		return base.OnUpdate(currentTick);
	}

	public ValueTask RootInitialize(CancellationToken ct)
	{
		return this.Initialize(ct);
	}


	internal Dictionary<Type, SlimNode>? _singletonCache=new();

	/// <summary>
	/// for internal use only, by SlimNode during .AddChild() if the child is marked as a singleton (ISingletonService)
	/// </summary>
	/// <typeparam name="TSingletonNode"></typeparam>
	/// <param name="instance"></param>
	internal void RegisterSingleton(ISingletonNode instance) 
	{
		var type = instance.GetType();
		if(_singletonCache.TryAdd(type,(SlimNode)instance) == false)
		{
			throw __.Throw($"a singleton of type {type.Name} is already registered");
		}
	}
	internal void UnRegisterSingleton(ISingletonNode instance)
	{
		var type = instance.GetType();
		int removeCount = 0;
		foreach(var registeredType in _singletonCache.Keys)
		{
			if (registeredType.IsAssignableFrom(type)){
				__.AssertIfNot(_singletonCache[registeredType] == instance, "assignable, from type of instance being removed, so it should match");
				_singletonCache.Remove(registeredType);
				removeCount++;
			}
		}
		__.AssertIfNot(removeCount >= 1, "should have removed at least one");
	}
}
