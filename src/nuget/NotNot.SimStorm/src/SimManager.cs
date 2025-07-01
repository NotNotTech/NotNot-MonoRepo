using NotNot.Diagnostics;
using NotNot.SimStorm._scratch.Ecs;
using System.Collections.Concurrent;

namespace NotNot.SimStorm;

/// <summary>
///    Manages execution of <see cref="SimNode" /> in parallel based on order-of-execution requirements (see
///    <see cref="SimNode._updateBefore" />) and resource requirements (see <see cref="SimNode._registeredReadLocks" /> and
///    <see cref="SimNode._registeredWriteLocks" />)
/// </summary>
public partial class SimManager : DisposeGuard //tree management
{
	public ConcurrentDictionary<string, SimNode> _nodeRegistry = new();
	public Engine.Engine engine;
	public RootNode root;

	public SimManager(Engine.Engine engine)
	{
		this.engine = engine;
		root = new RootNode { Name = "root", HierarchyDepth = 0 };
		root.Register(this);
		__.GetLogger()._EzErrorThrow<SimStormException>(_nodeRegistry.ContainsKey(root.Name));
		//var result = _nodeRegistry.TryAdd(_root.Name, _root);
		//__.GetLogger()._EzErrorThrow<SimStormException>(result);
	}

	/// <summary>
	///    if you know the ParentName and that parent is already registered, you can attach to it indirectly here
	/// </summary>
	/// <param name="node"></param>
	public void Register(SimNode node)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(node.ParentName != null,
			"you must know the parent name and store it as string in node.ParentName ");
		Register(node, node.ParentName);


		//__.GetLogger()._EzErrorThrow<SimStormException>(node.Name != null, $"your node of type {node.GetType().Name} has a blank name.  It must have a unique .Name property");

		////find parent
		////SimNode parent;
		//if(!_nodeRegistry.TryGetValue(node.ParentNameNew, out var parent))
		//{
		//	//no parent found
		//	__.GetLogger()._EzErrorThrow<SimStormException>(false, $"Registration of node '{node.Name}' failed because it's parent, named '{node.ParentNameNew}' was not already registered");
		//	return;
		//}
		//__.GetLogger()._EzCheckedThrow<SimStormException>(parent._managerNew == this);
		//parent.AddChild(node);


		//lock (_nodeRegistry)
		//{
		//	__.GetLogger()._EzErrorThrow<SimStormException>(_nodeRegistry.TryAdd(node.Name, node), $"A node with the same name of '{node.Name}' is already registered");
		//	var result = _nodeRegistry.TryGetValue(node.ParentName, out parent);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(result, $"Node registration failed.  Node '{node.Name}' parent of '{node.ParentName}' is not registered.");
		//}


		//__.GetLogger()._EzErrorThrow<SimStormException>(node._children.Count == 0, "register/unregister hiearchies is not currently supported due to SimManager._nodeRegistry not getting updated for children");
		//SimNode parent;
		//lock (_nodeRegistry)
		//{
		//	__.GetLogger()._EzErrorThrow<SimStormException>(_nodeRegistry.TryAdd(node.Name, node), $"A node with the same name of '{node.Name}' is already registered");
		//	var result = _nodeRegistry.TryGetValue(node.ParentName, out parent);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(result, $"Node registration failed.  Node '{node.Name}' parent of '{node.ParentName}' is not registered.");
		//}

		//parent.OnChildRegister(node);
	}

	public void Register(SimNode node, string parentName)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(node.Name != null,
			$"your node of type {node.GetType().Name} has a blank name.  It must have a unique .Name property");
		__.GetLogger()._EzCheckedThrow<SimStormException>(node.IsRegistered == false && node.IsAdded == false);

		//find parent
		//SimNode parent;
		if (!_nodeRegistry.TryGetValue(parentName, out var parent))
		{
			//no parent found
			__.GetLogger()._EzErrorThrow<SimStormException>(false,
				$"Registration of node '{node.Name}' failed because it's parent, named '{node.ParentName}' was not already registered");
			return;
		}

		__.GetLogger()._EzCheckedThrow<SimStormException>(parent.manager == this);
		parent.AddChild(node);


		//node.ParentNameNew = parentName;
		//Register(node);

		//__.GetLogger()._EzErrorThrow<SimStormException>(node._children.Count == 0, "register/unregister hiearchies is not currently supported due to SimManager._nodeRegistry not getting updated for children");
		//__.GetLogger()._EzErrorThrow<SimStormException>(node.Name != null, $"your node of type {node.GetType().Name} has a blank name.  It must have a unique .Name property");
		//SimNode parent;
		//lock (_nodeRegistry)
		//{
		//	__.GetLogger()._EzErrorThrow<SimStormException>(_nodeRegistry.TryAdd(node.Name, node), $"A node with the same name of '{node.Name}' is already registered");
		//	var result = _nodeRegistry.TryGetValue(node.ParentName, out parent);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(result, $"Node registration failed.  Node '{node.Name}' parent of '{node.ParentName}' is not registered.");
		//}

		//parent.OnChildRegister(node);
	}


	//public void Unregister(SimNode node)
	//{
	//	node._parentNew.
	//	__.GetLogger()._EzErrorThrow<SimStormException>(node._children.Count == 0, "register/unregister hiearchies is not currently supported due to SimManager._nodeRegistry not getting updated for children");

	//	node._parent.OnChildUnregister(node);

	//	lock (_nodeRegistry)
	//	{
	//		var result = _nodeRegistry.TryGetValue(node.Name, out var foundNode);
	//		_nodeRegistry.Remove(node.Name);
	//		__.GetLogger()._EzErrorThrow<SimStormException>(result);
	//		__.GetLogger()._EzErrorThrow<SimStormException>(node == foundNode, "should ref equal");
	//	}
	//}

	protected override void OnDispose(bool managedDisposing)
	{
		//dispose entire hirearchy
		root.Dispose();
		_nodeRegistry.Clear();
		_nodeRegistry = null;
		root = null;
		_resourceLocks.Clear();
		_resourceLocks = null;
		_frame?.Dispose();
		_frame = null;
		_priorFrameTask?.Dispose();
		_priorFrameTask = null;
		base.OnDispose(managedDisposing);
	}
}

public partial class SimManager //thread execution
{
	private Frame _frame;
	private Task _priorFrameTask;

	/// <summary>
	///    stores current locks of all resources used by SimNodes.   This central location is needed for coordination of frame
	///    executions.
	/// </summary>
	public Dictionary<object, RaceCheck> _resourceLocks = new();

	private TimeStats _stats;

	//private TimeSpan _realElapsedBuffer;
	//private const int _targetFramerate = 240;
	//private TimeSpan _targetFrameElapsed = TimeSpan.FromTicks(TimeSpan.TicksPerSecond/ _targetFramerate);

	/// <summary>
	/// constructs a graph of all registered nodes then executes them in order of dependency/priority
	/// </summary>
	/// <param name="_targetFrameElapsed"></param>
	/// <returns>finished nodes, in order of execution</returns>
	public async Task<List<NodeFrameState>> Update(TimeSpan _targetFrameElapsed)
	{
		//_realElapsedBuffer += realElapsed;
		//if (_realElapsedBuffer >= _targetFrameElapsed)
		//{
		//	var isRunningSlowly = false;
		//	_realElapsedBuffer -= _targetFrameElapsed;
		//	if (_realElapsedBuffer > _targetFrameElapsed)
		//	{
		//		//sim is running slowly (it takes more than _targetFrameElapsed time to do an update)
		//		isRunningSlowly = true;
		//		if (_realElapsedBuffer > _targetFrameElapsed*2)
		//		{
		//			//more than 2 frames behind.  start dropping
		//			_realElapsedBuffer *= 0.9f;
		//		}

		//	}
		_stats.Update(_targetFrameElapsed);
		_frame = Frame.FromPool(_stats, _frame, this);


		await _frame.InitializeNodeGraph(root);

		var finishedNodes = await _frame.ExecuteNodeGraph();
		return finishedNodes;
		// }
	}
}
