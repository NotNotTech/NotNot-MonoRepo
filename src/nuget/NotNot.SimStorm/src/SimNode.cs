using NotNot.Diagnostics;
using NotNot.SimStorm._scratch.Ecs;

namespace NotNot.SimStorm;

public abstract partial class SimNode //update logic
{
	/// <summary>
	///    helper to detect when enable/disable occurs
	/// </summary>
	private bool _isDisableCached;

	/// <summary>
	///    stats about the time at which the update() last completed successfully
	/// </summary>
	public NodeUpdateStats _lastUpdate = new();

	/// <summary>
	///    If Disabled <see cref="OnUpdate" /> will not run, nor OnFrameStarting/OnUpdate for it's children.
	///    Also when this occurs, <see cref="OnDisabled_Phase0" /> or <see cref="OnEnabled_Phase0" /> will be triggered.
	/// </summary>
	public bool IsDisabled { get; set; }

	/// <summary>
	///    triggered for root node and propigating down.  allows node to decide if it, or it+children should participate
	///    in this frame.
	/// </summary>
	internal virtual void OnFrameStarting(Frame frame, ICollection<SimNode> allNodesToUpdateInFrame)
	{
		IsSkipped = false;
		//TODO:  store last updateTime metric, use this+children to estimate node priority (costly things run first!)  add out TimeSpan hiearchyLastElapsed
		//TODO:  when doing that, maybe take the average of executions.   store in nodeState

		if (IsDisabled != _isDisableCached)
		{
			_isDisableCached = IsDisabled;
			if (IsDisabled)
			{
				OnDisabled_Phase0(frame);
			}
			else
			{
				OnEnabled_Phase0(frame);
			}
		}


		if (IsDisabled)
		{
			OnFrameSkipped(frame,$"Node ({this.Name}): IsDisabled.  Frame {frame._stats._frameId}");
		}
		else
		{

			//add this node to be executed this frame
			allNodesToUpdateInFrame.Add(this); //TODO: node filtering logic here based on FPS limiting, etc
			foreach (var child in children)
			{
				//child.IsDisabled = IsDisabled; //current hiearchy state is propagated
				child.OnFrameStarting(frame, allNodesToUpdateInFrame);
			}
		}
	}

	/// <summary>
	/// provides a hint that this frame is skipped or not.   if skipped, see .IsSkippedDebugReason for details
	/// <para>this value may chance from frame to frame</para>
	/// </summary>
	public bool IsSkipped { get; private set; }
	/// <summary>
	/// a hint to the dev, why the node was skipped.
	/// </summary>
	public string IsSkippedDebugReason { get; private set; }

	/// <summary>
	/// when a node is skipped for any reason (currently .IsDisabled, or FixedTimestepNode not at target FPS), it and it's children will be notified via this method.
	/// triggers once per frame per node, as long as skipped.
	/// </summary>
	/// <param name="reason"></param>
	/// <param name="frame"></param>
	protected virtual void OnFrameSkipped(Frame frame, string reason)
	{
		IsSkipped = true;
		IsSkippedDebugReason = reason;
		
		foreach (var child in children)
		{
			child.OnFrameSkipped(frame, reason);
		}
		//	__.GetLogger()._EzDebug(_DEBUG_PRINT_TRACE, $"Skipping {GetHierarchyName()}  reason={reason}");
	}

	/// <summary>
	///    //IMPORTANT: a node.Update() will fire and complete BEFORE it's children are started.
	/// </summary>
	/// <param name="frame"></param>
	/// <returns></returns>
	internal Task DoUpdate(Frame frame, NodeFrameState nodeState)
	{
		//try
		{
			__.GetLogger()._EzDebug(_DEBUG_PRINT_TRACE != true, $"{frame._stats._frameId}  :  {GetHierarchyName()}");
			//if (IsInitialized == false)
			//{
			//	Initialize();
			//}
			return OnUpdate(frame, nodeState);
		}
		//catch (Exception e)
		//{
		//	throw e;
		//}
		//finally
		//{

		//}
	}

	protected abstract Task OnUpdate(Frame frame, NodeFrameState nodeState);


	/// <summary> frame is totally done.  clean up anything created/stored for this frame </summary>
	internal virtual void OnFrameFinished(Frame frame)
	{
		//_frameStates.Remove(frame);
	}

	/// <summary>
	///    the node and all children are becomming disabled.  OnUpdate will not be called anymore
	/// </summary>
	/// <param name="frame"></param>
	protected virtual void OnDisabled_Phase0(Frame frame)
	{
	}

	/// <summary>
	///    the node and all children are becomming enabled.   OnUpdate will resume being called every frame
	/// </summary>
	/// <param name="frame"></param>
	protected virtual void OnEnabled_Phase0(Frame frame)
	{
	}
}

/// <summary>
///    A node holds logic in it's <see cref="Update(Frame)" /> method that is executed in parallel with other Simnodes.
///    See <see cref="SimManager" /> for detail.
/// </summary>
/// <remarks>
///    <para>
///       Lifecycle:  Create --> Add --> Register --> Update --> Unregister --> Remove --> Dispose.
///       Initialize() can occur any time between Create and Register.
///       Dispose() can occur at any time, ahd the node doesn't nessicarly have to be Removed or Unregistered first,
///       however it will crash if used when disposed.
///       This is acceptable in situations when the World is shutdown.
///       For the Parent/Child hiearchy, parents get initialized/added/registered before children.  children get
///       unregistered/removed/disposed before parents.
///    </para>
///    <para>
///       Long running tasks:   All SimNodes execute according to a dependency tree, with those able to execute in-parallel
///       (multithreaded) able to do so.
///       But before the next execution frame starts (Frame N), all prior frame SimNodes must complete (Frame N-1).
///       Because a frame of execution should be as short as possible (perhaps 5ms) you should put potentially long-running
///       work in a seperate thread
///       that coordinates/synchronizes with your SimNode.  Take a look at the RenderReferenceImplementationSystem for an
///       example of how to do this.
///    </para>
/// </remarks>
public abstract partial class SimNode //tree logic
{
	public const bool _DEBUG_PRINT_TRACE = false;

	private string _nameCached;


	private string _parentNameCached;

	public List<SimNode> children = new();
	public SimManager manager;

	public SimNode parent;

	public string Name
	{
		get
		{
			if (_nameCached == null)
			{
				_nameCached = InstanceNameHelper.CreateName(GetType());
			}

			return _nameCached;
		}
		init => _nameCached = value;
	}

	/// <summary>
	///    The name of the parent node.
	///    <para>
	///       you can set this to a string upon node creation, and then pass to simManager.Register(), which will find the
	///       parent and attach this node as it's child.
	///    </para>
	///    <para>but it's usually better to just have a reference to the parent, and call parent.AddChild() instead</para>
	/// </summary>
	public string ParentName
	{
		get
		{
			if (parent != null)
			{
				return parent.Name;
			}

			return _parentNameCached;
		}
		set
		{
			__.GetLogger()._EzErrorThrow<SimStormException>(parent == null,
				"you should only set node.ParentName if there isn't a node.parent already set");
			_parentNameCached = value;
		}
	}

	/// <summary>
	///    how far down the node hiearchy this is.  RootNode has depth 0.
	///    <para>when not registered with the SimManager (not attached to a running simulation) the depth is -1</para>
	/// </summary>
	protected internal int HierarchyDepth { get; internal set; } = -1;


	public bool IsRegistered { get; private set; }


	public bool IsAdded { get; private set; }


	public ReadMem<SimNode> GetHierarchy()
	{
		var toReturn = ReadMem<SimNode>.Allocate(HierarchyDepth + 1);

		var writeable = toReturn.AsWriteMem();


		if (toReturn.Length == 0)
		{
			return toReturn;
		}

		//var array = toReturn.Segment().Array;
		var span = writeable.Span; // toReturn. array.AsSpan(0, toReturn.length);

		span[0] = this;
		var curNode = this;
		var index = 1;
		while (curNode.parent != null)
		{
			span[index] = curNode.parent;
			curNode = curNode.parent;
			index++;
		}

		span.Reverse(); //so root is at item 0
		return toReturn;


		////var toReturn = new List<SimNode>();
		////toReturn.Add(this);
		//var curNode = this;
		//while (curNode._parent != null)
		//{
		//	toReturn.Add(curNode._parent);
		//	curNode = curNode._parent;
		//}
		//toReturn.Reverse();
		//return toReturn;
	}

	/// <summary>
	///    returns hierarchy in the string format "NodeName|root->ParentName->NodeName"
	/// </summary>
	/// <returns></returns>
	public string GetHierarchyName()
	{
		var chain = GetHierarchy();

		var query = from node in chain.DangerousGetArray() select node.Name;
		return $"{Name}|{string.Join("->", query)}";
	}

	public override string ToString()
	{
		return $"{GetHierarchyName()}";

		//return $"{Name}  parent={ParentName}";
	}

	/// <summary>
	///    count of all nodes under this node (children+their children)
	/// </summary>
	/// <returns></returns>
	public int HiearchyCount()
	{
		var toReturn = children.Count;
		foreach (var child in children)
		{
			toReturn += child.HiearchyCount();
		}

		return toReturn;
	}

	public bool FindNode(string name, out SimNode node)
	{
		return manager._nodeRegistry.TryGetValue(name, out node);
	}

	internal void Register(SimManager manager)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(IsRegistered == false);
		IsRegistered = true;
		this.manager = manager;
		var result = this.manager._nodeRegistry.TryAdd(Name, this);
		__.GetLogger()._EzErrorThrow<SimStormException>(result);

		if (HierarchyDepth != -1)
		{
			__.AssertOnce(this is RootNode, "expect only root node to have it's hiearchy depth preset.");
			__.AssertOnce(parent == null,
				"expect only root node to have it's hiearchy depth preset, and it to not have a parent");
			//no op
		}
		else
		{
			__.GetLogger()._EzErrorThrow<SimStormException>(parent != null && HierarchyDepth == -1);
			HierarchyDepth = parent.HierarchyDepth + 1;
		}

		OnRegister();
		foreach (var child in children)
		{
			child.Register(manager);
		}

		if (IsInitialized == false)
		{
			Initialize()._SyncWait();
		}
	}

	/// <summary>
	///    invoked when registered with the engine, meaning its parents are all hooked up with the engine and could possibly
	///    start getting <see cref="OnUpdate" /> calls
	///    parents will register before children.   children unregister before parents.
	/// </summary>
	protected virtual void OnRegister()
	{
	}

	internal void Unregister()
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(IsRegistered);
		IsRegistered = false;

		foreach (var child in children)
		{
			child.Unregister();
		}

		OnUnregister();
		var result = manager._nodeRegistry.TryRemove(Name, out var self);
		__.GetLogger()._EzErrorThrow<SimStormException>(result && self == this);
		manager = null;
	}

	/// <summary>
	///    invoked upon call to UnRegister, which detaches from the Engine so that it no longer recieves Update() calls.
	///    first unregisters children, then unregisters this
	/// </summary>
	protected virtual void OnUnregister()
	{
	}

	public void AddChild(SimNode child)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(child.IsRegistered == false && child.IsAdded == false &&
		                                  children.Contains(child) == false);
		children.Add(child);
		child.Added(this);
		OnChildAdded(child);

		if (IsRegistered)
		{
			child.Register(manager);
		}
	}


	private void Added(SimNode parent)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(this.parent == null //&& HierarchyDepth == -1
		);
		this.parent = parent;
		//HierarchyDepth = parent.HierarchyDepth + 1;
		OnAdded();
	}

	/// <summary>
	///    invoked when added to a parent, regardless of if registered with the engine or not.
	/// </summary>
	protected virtual void OnAdded()
	{
	}

	protected virtual void OnChildAdded(SimNode node)
	{
	}


	public void RemoveChild(SimNode child)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(child.IsAdded);
		__.GetLogger()._EzErrorThrow<SimStormException>(children.Contains(child));

		children.Remove(child);
		if (IsRegistered)
		{
			child.Unregister();
		}

		child.Removed();

		OnChildRemoved(child);
	}

	private void Removed()
	{
		parent = null;
		HierarchyDepth = -1;
		OnRemoved();
	}

	protected virtual void OnRemoved()
	{
	}


	internal virtual void OnChildRemoved(SimNode node)
	{
	}


	//protected virtual void OnAdded(SimNode parent, SimManager manager)
	//{
	//	_parentNew = parent;
	//	_managerNew = manager;
	//}

	///// <summary>
	///// triggered when removed from the simNode parent
	///// </summary>
	//protected virtual void OnRemoved()
	//{
	//	_parentNew = null;
	//	_managerNew = null;
	//}

	//public void RegisterChild(SimNode child)
	//{
	//	if (child.ParentName != null && child.ParentName != this.Name)
	//	{
	//		__.GetLogger()._EzErrorThrow<SimStormException>(false, $"you are adding the node {child.Name} as a child of ${this.Name} but it's Parent parameter is already set to {child.ParentName}.  either set the ParentName to null or to {this.Name}  ");
	//	}

	//	child._parentName = this.Name;

	//	_manager.Register(child);
	//}
	//public void UnregisterSelf()
	//{
	//	_manager.Unregister(this);
	//}
}

public abstract partial class SimNode : DisposeGuard, IComparable<SimNode> //frame blocking / update order
{
	private int _executionPriority = 0;


	/// <summary>
	///    object "token" used as a shared read-access key when trying to execute this node.
	///    <para>Nodes with read access to the same key may execute in parallel</para>
	/// </summary>
	public HashSet<object> _registeredReadLocks = new();

	/// <summary>
	///    object "token" used as an exclusive write-access key when trying to execute this node.
	///    <para>Nodes with write access to a key will run seperate from any other node using the same resource (Read or Write)</para>
	/// </summary>
	public HashSet<object> _registeredWriteLocks = new();

	/// <summary>
	///    name of nodes this needs to update after
	/// </summary>
	public List<string> _updateAfter = new();

	/// <summary>
	///    name of nodes this needs to update before
	/// </summary>
	public List<string> _updateBefore = new();

	public bool IsInitialized { get; private set; }

	int IComparable<SimNode>.CompareTo(SimNode? other)
	{
		if (other == null)
		{
			return 1;
		}

		return _executionPriority - other._executionPriority;
	}

	/// <summary>
	///    informs the SimPipeline that this node needs shared-read access to the specified resource during the Update() loop
	/// </summary>
	/// <typeparam name="TComponent"></typeparam>
	protected void RegisterReadLock(object resource)
	{
		_registeredReadLocks.Add(resource);
	}

	/// <summary>
	///    informs the SimPipeline that this node needs exclusive-write access to the specified resource during the Update()
	///    loop
	/// </summary>
	protected void RegisterWriteLock(object resource)
	{
		_registeredWriteLocks.Add(resource);
	}

	/// <summary>
	///    informs the SimPipeline that this node needs shared-read access to the specified resource during the Update() loop
	/// </summary>
	/// <typeparam name="TComponent"></typeparam>
	protected void RegisterReadLock<TComponent>()
	{
		_registeredReadLocks.Add(typeof(TComponent));
	}

	/// <summary>
	///    informs the SimPipeline that this node needs exclusive-write access to the specified resource during the Update()
	///    loop
	/// </summary>
	protected void RegisterWriteLock<TComponent>()
	{
		_registeredWriteLocks.Add(typeof(TComponent));
	}


	/// <summary>
	///    dispose self and all children.  be sure to call base.OnDispose() when overriding.
	/// </summary>
	/// <param name="disposing"></param>
	protected override void OnDispose(bool managedDisposing)
	{
		foreach (var child in children)
		{
			if (child.IsDisposed == false)
			{
				child.Dispose();
			}
		}

		base.OnDispose(managedDisposing);
	}


	internal async ValueTask Initialize()
	{
		if (IsInitialized)
		{
			return;
		}

		await OnInitialize();


#if DEBUG
		__.GetLogger()._EzErrorThrow<SimStormException>(IsInitialized,
			"Your override didn't call base.OnInitialize() like you are supposed to");
#endif
	}

	/// <summary>
	///    if your node has initialization steps, override this method, but be sure to call it's base.OnInitialize();
	///    <para>
	///       If Initialize is not called by the time this node is registered with the Engine, initialize will be called
	///       automatically.
	///    </para>
	///    <para>will only be called ONCE for the node's lifetime</para>
	/// </summary>
	protected virtual ValueTask OnInitialize()
	{
		IsInitialized = true;
		return ValueTask.CompletedTask;
	}
}

public class SimStormException : Exception
{
	public SimStormException()
	{
	}

	public SimStormException(string message) : base(message) { }
	public SimStormException(string message, Exception? innerException = null) : base(message, innerException) { }
}