using System.Diagnostics;
using NotNot.Diagnostics;
using NotNot.Diagnostics.Advanced;
using NotNot.SimStorm._scratch.Ecs;

namespace NotNot.SimStorm;

public partial class Frame ////node graph setup and execution
{
	private List<SimNode> _allNodesInFrame = new();
	private List<SimNode> _allNodesToProcess = new();
	private Dictionary<SimNode, NodeFrameState> _frameStates = new();

	/// <summary>
	///    informs all registered nodes that a frame is going to start.
	///    This initializes the frame's state.
	/// </summary>
	/// <param name="root"></param>
	/// <returns></returns>
	public async Task InitializeNodeGraph(RootNode root)
	{
		//notify all nodes of our intent to execute a frame, and obtain a flat listing of all nodes
		__.GetLogger()._EzError(_allNodesInFrame.Count == 0 && _allNodesToProcess.Count == 0 && _frameStates.Count == 0,
			"should be cleared at end of last frame / from pool recycle");
		root.OnFrameStarting(this, _allNodesInFrame);


		//TODO: allow multiple frames to run overlapped once we have read/write component locks implemented.  allow next frame to start/run if conditions met:
		//TODO: example conditions:  NF system can run IF (NF Read + CF No Writes) OR (NF Write + CF No Reads or Writes)


		//TODO: this section  should be moved into Frame class probably, as it is per-frame data.   will need to do this when we allow starting next frame early.

		//sort so at bottom are those that should be executed at highest priority
		_allNodesInFrame.Sort(); //TODO: sort by hierarchy execution time. so longest running nodes get executed first.
#if CHECKED
		//when #CHECKED, randomize order to uncover order-of-execution bugs
		_allNodesInFrame._Randomize();
#endif
		_allNodesToProcess.AddRange(_allNodesInFrame);

		//generate per-frame tracking state for each node
		foreach (var node in _allNodesInFrame)
		{
			var nodeState = new NodeFrameState { _node = node };
			_frameStates.Add(node, nodeState);
		}

		//reloop building tree of children active in scene (for this frame)
		////This could probably be combined with prior foreach, but leaving sepearate for clarity and not assuming certain implementation
		foreach (var node in _allNodesInFrame)
		{
			if (node.parent == null)
			{
				__.GetLogger()._EzError(node.GetType() == typeof(RootNode), "only root node should not have parent");
				continue;
			}

			var thisNodeState = _frameStates[node];
			var parentNodeState = _frameStates[node.parent];
			parentNodeState._activeChildren.Add(thisNodeState);
			thisNodeState._parent = parentNodeState;
		}

		//now that our tree of NodeFrameStates is setup properly, reloop calculating execution order dependencies
		foreach (var node in _allNodesInFrame)
		{
			var nodeState = _frameStates[node];


			//TODO: calculate and store all runBefore/ runAfter dependencies


			//updateAfter
			foreach (var afterName in node._updateAfter)
			{
				if (!node.FindNode(afterName, out var afterNode))
				{
					__.AssertOnceIfNot(false,
						$"'{afterName}' node is listed as an updateAfter dependency in '{node.GetHierarchyName()}' node.  target node not registered");
					continue;
				}

				if (!_frameStates.TryGetValue(afterNode, out var afterNodeState))
				{
					__.GetLogger()._EzError(false, "missing?  maybe ok.  node not participating in this frame");
					continue;
				}

				__.GetLogger()._EzErrorThrow<SimStormException>(node.GetHierarchy().GetSpan()._Contains(afterNodeState._node) == false,
					$"updateBefore('{afterName}') is invalid.  Node '{node.Name}' is a (grand)child of '{afterName}'.  You can not mark a parent as updateBefore/After." +
					$"{node.Name} will always update during it's it's parent's update (parent updates first, but is not marked as complete until all it's children finish).");
				nodeState._updateAfter.Add(afterNodeState);
			}

			//updateBefore
			foreach (var beforeName in node._updateBefore)
			{
				if (!node.FindNode(beforeName, out var beforeNode))
				{
					__.AssertOnceIfNot(false,
						$"'{beforeName}' node is listed as an updateBefore dependency in '{node.Name}' node.  target node not registered");
					continue;
				}

				if (!_frameStates.TryGetValue(beforeNode, out var beforeNodeState))
				{
					__.GetLogger()._EzError(false, "missing?  maybe ok.  node not participating in this frame");
					continue;
				}

				__.GetLogger()._EzErrorThrow<SimStormException>(node.GetHierarchy().GetSpan()._Contains(beforeNodeState._node) == false,
					$"updateBefore('{beforeName}') is invalid.  Node '{node.Name}' is a (grand)child of '{beforeName}'.  You can not mark a parent as updateBefore/After." +
					$"{node.Name} will always update during it's it's parent's update (parent updates first, but is not marked as complete until all it's children finish).");
				beforeNodeState._updateAfter.Add(nodeState);
			}

			//var lockTest = new DotNext.Threading.AsyncReaderWriterLock();


			//calc and store resource R/W locking

			//reads
			foreach (var obj in node._registeredReadLocks)
			{
				var readRequests = _readRequestsRemaining._GetOrAdd(obj, () => new HashSet<SimNode>());
				readRequests.Add(node);
			}

			//writes
			foreach (var obj in node._registeredWriteLocks)
			{
				var writeRequests = _writeRequestsRemaining._GetOrAdd(obj, () => new HashSet<SimNode>());
				writeRequests.Add(node);
			}
		}
		//var rwLock = new ReaderWriterLockSlim();
		//rwLock.h

		//System.Threading.res
		//DotNext.Threading.
	}


	/// <summary>
	///    Execute the nodes Update methods.
	/// </summary>
	/// <returns></returns>
	public async Task<List<NodeFrameState>>
		ExecuteNodeGraph() //TODO: this should be moved to an execution manager class, shared by all Frames of the same SimManager, otherwise each Frame has it's own threadpool.
	{
		//TODO: when Frames executed in parallel, do at least 1 pass through prior frame nodes before starting next frame.
		//process nodes
		var maxThreads = Environment.ProcessorCount + 2;
		var greedyFillThreads = Environment.ProcessorCount / 2;
		var currentTasks = new List<Task>();
		var activeNodes = new List<NodeFrameState>();
		var finishedWaitingOnChildrenNodes = new List<NodeFrameState>();
		var childrenAreFinished = new List<NodeFrameState>();
		var finishedNodes = new List<NodeFrameState>();

		//helper to update our nodeState when the update method is done running
		static void doneUpdateTask_Helper(Task updateTask, NodeFrameState nodeState)
		{
			__.GetLogger()._EzError(nodeState._status == FrameStatus.RUNNING);
			nodeState._status = FrameStatus.FINISHED_WAITING_FOR_CHILDREN;
			nodeState._updateTime = nodeState._updateStopwatch.Elapsed;
		}
		//var doneUpdateTask = (Task updateTask) =>
		//{
		//	__.GetLogger()._EzError(nodeState._status == FrameStatus.RUNNING);
		//	nodeState._status = FrameStatus.FINISHED_WAITING_FOR_CHILDREN;
		//	nodeState._updateTime = nodeState._updateStopwatch.Elapsed;
		//	//nodeState._updateTcs.SetFromTask(updateTask);
		//	//return updateTask;
		//};

		int outerWhileCount = 0, innerForCount = 0, updateTaskCreates = 0, updateTaskAsyncStarts = 0, updateTaskAsyncFinishes = 0, currentTaskRemoveDones = 0, activeNodesFinishes = 0, okayExecutions = 0, badExecutions = 0, waitForAllCurrentTasks = 0, notifyAllNodesInFrameFinishes = 0;

		__.GetLogger()._EzInfo(SimNode._DEBUG_PRINT_TRACE != true,
			$"[[[[[=================------- {_stats._frameId} -------=================]]]]]");
		while (_allNodesToProcess?.Count > 0 || currentTasks.Count > 0)
		{
			outerWhileCount++;
			var DEBUG_startedThisPass = 0;
			var DEBUG_finishedNodeUpdate = 0;
			var DEBUG_finishedHierarchy = 0;


			//try to execute highest priority first
			for (var i = _allNodesToProcess.Count - 1; i >= 0; i--)
			{
				innerForCount++;
				var node = _allNodesToProcess[i];
				var nodeState = _frameStates[node];
				var canUpdateNow = nodeState.CanUpdateNow();
				var areResourcesAvailable = AreResourcesAvailable(node);
				if (canUpdateNow && areResourcesAvailable)
				{
					//execute the node's .Update() method
					__.GetLogger()._EzError(nodeState._status == FrameStatus.SCHEDULED);
					nodeState._status = FrameStatus.PENDING;
					LockResources(node);

					_allNodesToProcess.RemoveAt(i);

					nodeState._updateStopwatch = Stopwatch.StartNew();

					__.GetLogger()._EzError(nodeState._status == FrameStatus.PENDING);
					nodeState._status = FrameStatus.RUNNING;
					activeNodes.Add(nodeState);


					static async Task taskRunner(SimNode node, NodeFrameState nodeState, Frame _this)
					{
						var _task = node.DoUpdate(_this, nodeState);
						await _task;
						doneUpdateTask_Helper(_task, nodeState);
						//return _task;
					}

					if (node is IIgnoreUpdate)
					{
						//node has no update loop, it's done immediately.
						doneUpdateTask_Helper(Task.CompletedTask, nodeState);
						nodeState.UpdateTask = Task.CompletedTask;
						DEBUG_finishedNodeUpdate++;
					}
					else
					{
						updateTaskCreates++;
						////node update() may be async, so need to monitor it to track when it completes.
						var updateTask = __.Async.Factory.Run(async () =>
						{
							Interlocked.Increment(ref updateTaskAsyncStarts);
							//var _task = node.DoUpdate(this, nodeState);
							//await _task;
							await node.DoUpdate(this, nodeState);
							doneUpdateTask_Helper(Task.CompletedTask, nodeState);
							Interlocked.Increment(ref updateTaskAsyncFinishes);
							//return _task;
						}); //.ContinueWith(doneUpdateTask);

						//Task.Run(taskRunner(node,nodeState,this))
						//var updateTask =  taskRunner(node, nodeState, this);


						//updateTask.ConfigureAwait(false)

						currentTasks.Add(updateTask);
						nodeState.UpdateTask = updateTask;
						DEBUG_startedThisPass++;
					}

					if (currentTasks.Count > greedyFillThreads)
					{
						//OPTIMIZATION?: once our threads are half filled, we become more picky about node prioritization.
						//Since our _allNodesToProccess is sorted in priority order,
						//With the following line (starting the for loop over) we will always pick the higest priority node that's available.
						//we don't do this by default in case the simulation has hundreds of nodes with lots of blocking.
						//(constantly starting the for-loop over every Task start seems wasteful)
						i = _allNodesToProcess.Count - 1;
					}
				}


				if (currentTasks.Count >= maxThreads)
				{
					//too many enqueued, stop trying to enqueue more
					break;
				}
			}

			//unsafe
			//{
			//	byte[] byteArray = new byte[100];
			//	fixed(byte)

			//}

			if (currentTasks.Count != 0)
			{
				//wait on at least one task	
#if DEBUG
				try
				{
					await Task.WhenAny(currentTasks).WaitAsync(__.Async.CancelAfter(TimeSpan.FromSeconds(2)));
				}
				catch (TimeoutException ex)
				{
					__.AssertIfNot(false);
					__.GetLogger()._EzErrorThrow<SimStormException>(DebuggerInfo.IsPaused,
						"SimPipeline appears deadlocked, as no executing task has completed in less than 2 seconds.");
				}
#else
				await Task.WhenAny(currentTasks);
#endif
			}

			//remove done
			for (var i = currentTasks.Count - 1; i >= 0; i--)
			{
				var currentTask = currentTasks[i];
				if (currentTask.IsFaulted)
				{
					throw currentTask.Exception!;
				}

				if (currentTask.IsCompleted)
				{
					//NOTE: task may complete LONG AFTER the actual update() is finished.
					//so we have a counter to help debuggers understand this.
					DEBUG_finishedNodeUpdate++;
					currentTasks.RemoveAt(i);
					currentTaskRemoveDones++;
				}
			}

			//loop through all active nodes, and if FINISHED unlock resources and move to waiting on children collection
			for (var i = activeNodes.Count - 1; i >= 0; i--)
			{
				var nodeState = activeNodes[i];
				__.GetLogger()._EzError(nodeState._status is FrameStatus.RUNNING or FrameStatus.FINISHED_WAITING_FOR_CHILDREN);

				if (nodeState._status != FrameStatus.FINISHED_WAITING_FOR_CHILDREN)
				{
					continue;
				}

				//node is finished, free resource locks
				UnlockResources(nodeState._node);

				//move to waiting on Children group
				finishedWaitingOnChildrenNodes.Add(nodeState);
				finishedNodes.Add(nodeState);
				activeNodes.RemoveAt(i);
				activeNodesFinishes++;
			}

			//loop through all waitingOnChildrenNodes, and if all children are HIERARCHY_FINISHED then mark this as finished and remove from active

			for (var i = finishedWaitingOnChildrenNodes.Count - 1; i >= 0; i--)
			{
				var nodeState = finishedWaitingOnChildrenNodes[i];
				__.GetLogger()._EzError(nodeState._status is FrameStatus.FINISHED_WAITING_FOR_CHILDREN);

				//node is finished, check children if can remove
				var childrenFinished = true;
				foreach (var child in nodeState._activeChildren)
				{
					if (child._status != FrameStatus.HIERARCHY_FINISHED)
					{
						childrenFinished = false;
						break;
					}
				}

				if (childrenFinished)
				{
					//this node and it's children are all done for this frame!
					DEBUG_finishedHierarchy++;
					nodeState._status = FrameStatus.HIERARCHY_FINISHED;
					nodeState._updateHierarchyTime = nodeState._updateStopwatch.Elapsed;
					finishedWaitingOnChildrenNodes.RemoveAt(i);
					childrenAreFinished.Add(nodeState);
					//record stats about this frame for easy access in the node
					nodeState._node._lastUpdate.Update(this, nodeState);
				}
			}


			if (DEBUG_startedThisPass > 0 || DEBUG_finishedHierarchy > 0 || currentTasks.Count > 0 ||
				 DEBUG_finishedNodeUpdate > 0)
			{
				//ok
				okayExecutions++;
			}
			else
			{
				badExecutions++;
				var errorStr = $"Node execution deadlocked for frame {_stats._frameId}.  " +
									$"There are {_allNodesToProcess.Count} nodes that can not execute due to circular dependencies in UpdateBefore/After settings.  " +
									$"These are their settings (set in code) and their runtimeUpdateAfter computed values for this frame.  Check any nodes mentioned:\n";
				foreach (var node in _allNodesToProcess)
				{
					var nodeState = _frameStates[node];
					errorStr += $"   {node.GetHierarchyName()} " +
									$"updateBefore=[{string.Join(',', node._updateBefore)}] updateAfter=[{string.Join(',', node._updateAfter)}]  calculatedUpdateAfter=[{string.Join(',', nodeState._updateAfter)}]\n";
				}

				__.GetLogger()._EzErrorThrow<SimStormException>(false, errorStr);
			}
		}

		__.GetLogger()._EzError(currentTasks.Count == 0);
		//nothing else to process, wait on all remaining tasks
		waitForAllCurrentTasks++;
#if DEBUG
		try
		{
			await Task.WhenAll(currentTasks).WaitAsync(TimeSpan.FromSeconds(2));
		}
		catch (TimeoutException ex)
		{
			__.GetLogger()._EzErrorThrow<SimStormException>(DebuggerInfo.IsPaused,
				"SimPipeline appears deadlocked, as no executing task has completed in less than 2 seconds.");
		}
#else
				await Task.WhenAll(currentTasks);
#endif

		foreach (var (resource, resourceRequests) in _readRequestsRemaining)
		{
			__.GetLogger()._EzError(resourceRequests.Count == 0,
				"in single frame execution mode, expect all resourcesRequests to be fulfilled by end of frame");
		}

		foreach (var (resource, resourceRequests) in _writeRequestsRemaining)
		{
			__.GetLogger()._EzError(resourceRequests.Count == 0,
				"in single frame execution mode, expect all resourcesRequests to be fulfilled by end of frame");
		}

		//notify all nodes that our frame is done
		foreach (var node in _allNodesInFrame)
		{
			notifyAllNodesInFrameFinishes++;
			node.OnFrameFinished(this);
		}

		return finishedNodes;
	}
}

/// <summary>
///    A frame of execution.  We need to store state regarding SimNode execution status and that is done here, with the
///    accompanying logic.
/// </summary>
public partial class Frame : DisposeGuard //general setup
{
	public SimManager _manager;

	public List<FixedTimestepNode> _slowRunningNodes = new();
	public TimeStats _stats;

	public bool IsRunningSlowly => _slowRunningNodes.Count > 0;

	internal static Frame FromPool(TimeStats stats, Frame priorFrame, SimManager manager)
	{
		//TODO: make chain, recycle old stuff every update
		if (priorFrame != null)
		{
			priorFrame.Dispose();
		}

		return new Frame { _stats = stats, _manager = manager };
	}

	public override string ToString()
	{
		return _stats.ToString();
	}


	protected override void OnDispose(bool managedDisposing)
	{
		_manager = null;
		_stats = default;
		_allNodesInFrame.Clear();
		_allNodesInFrame = null;
		_allNodesToProcess.Clear();
		_allNodesToProcess = null;
		_frameStates.Clear();
		_frameStates = null;
		_priorFrame = null;
		_readRequestsRemaining.Clear();
		_readRequestsRemaining = null;
		_writeRequestsRemaining.Clear();
		_writeRequestsRemaining = null;


		base.OnDispose(managedDisposing);
	}
}

public partial class Frame //resource locking
{
	private Frame _priorFrame;

	/// <summary>
	///    track what reads are remaining for this frame.   used so next frame writes will not start until these are empty.
	/// </summary>
	public Dictionary<object, HashSet<SimNode>> _readRequestsRemaining = new();

	/// <summary>
	///    track what writes remain for this frame.  used so next frame R/W will not start until these are empty.
	/// </summary>
	public Dictionary<object, HashSet<SimNode>> _writeRequestsRemaining = new();

	/// <summary>
	///    Returns true if the read/write resources used by this frame are available.
	/// </summary>
	/// <param name="node"></param>
	/// <returns></returns>
	private bool AreResourcesAvailable(SimNode node)
	{
		//make sure that there are no pending resource usage in prior frames
		if (_priorFrame != null)
		{
			//for a READ resource, make sure no WRITES remaining from last frame, otherwise can not proceed yet.
			foreach (var obj in node._registeredReadLocks)
			{
				if (_priorFrame._writeRequestsRemaining.TryGetValue(obj, out var nodesRemaining) &&
					 nodesRemaining.Count > 0)
				{
					return false;
				}
			}

			//for a WRITE resource, make sure no READS or WRITES remaining from last frame, otherwise can not proceed yet.
			foreach (var obj in node._registeredWriteLocks)
			{
				{
					if (_priorFrame._writeRequestsRemaining.TryGetValue(obj, out var nodesRemaining) &&
						 nodesRemaining.Count > 0)
					{
						return false;
					}
				}
				{
					if (_priorFrame._readRequestsRemaining.TryGetValue(obj, out var nodesRemaining) &&
						 nodesRemaining.Count > 0)
					{
						return false;
					}
				}
			}
		}

		//reads
		foreach (var obj in node._registeredReadLocks)
		{
			var rwCounter = _manager._resourceLocks._GetOrAdd(obj, () => new RaceCheck(true));
			if (rwCounter.IsWriteHeld)
			{
				return false;
			}
		}

		//writes
		foreach (var obj in node._registeredWriteLocks)
		{
			var rwCounter = _manager._resourceLocks._GetOrAdd(obj, () => new RaceCheck(true));
			if (rwCounter.IsAnyHeld)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	///    lock the resource and decrement our remaining locks needed for this frame.
	/// </summary>
	private bool LockResources(SimNode node)
	{
		//reads
		foreach (var obj in node._registeredReadLocks)
		{
			var rwCounter = _manager._resourceLocks._GetOrAdd(obj, () => new RaceCheck(true));
			rwCounter.EnterRead();
			var result = _readRequestsRemaining[obj].Remove(node);
			__.GetLogger()._EzError(result);
		}

		//writes
		foreach (var obj in node._registeredWriteLocks)
		{
			var rwCounter = _manager._resourceLocks._GetOrAdd(obj, () => new RaceCheck(true));
			rwCounter.EnterWrite();
			var result = _writeRequestsRemaining[obj].Remove(node);
			__.GetLogger()._EzError(result);
		}

		return true;
	}

	private bool UnlockResources(SimNode node)
	{
		//reads
		foreach (var obj in node._registeredReadLocks)
		{
			var rwLock = _manager._resourceLocks._GetOrAdd(obj, () => new RaceCheck(true));
			rwLock.ExitRead();
		}

		//writes
		foreach (var obj in node._registeredWriteLocks)
		{
			var rwLock = _manager._resourceLocks._GetOrAdd(obj, () => new RaceCheck(true));
			rwLock.ExitWrite();
		}

		return true;
	}
}
