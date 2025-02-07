using NotNot.SimStorm._scratch.Ecs;
using System.Diagnostics;

namespace NotNot.SimStorm;

/// <summary>
///    stores state for this node's frame of execution.     This is temporary, only should be used/modified during the
///    current execution frame.
/// </summary>
public class NodeFrameState
{
	/// <summary>
	///    not all nodes are active in all frames.   this is a listing of the node's active children.
	/// </summary>
	public List<NodeFrameState> _activeChildren = new();

	public NodeFrameState _parent;

	/// <summary>
	///    nodes that this node must run after
	/// </summary>
	public List<NodeFrameState> _updateAfter = new();

	internal TimeSpan _updateHierarchyTime;

	internal Stopwatch _updateStopwatch;
	//{
	//	get => _updateTcs.Task;
	//}

	/// <summary>
	///    how long the update took.  used for prioritizing future frame execution order
	/// </summary>
	public TimeSpan _updateTime = TimeSpan.Zero;

	/// <summary> the target node </summary>
	public SimNode _node { get; init; }

	//public TaskCompletionSource _updateTcs { get; init; } = new();
	public FrameStatus _status { get; set; } = FrameStatus.SCHEDULED;

	/// <summary>
	///    the current state of the node's update.
	/// </summary>
	public Task UpdateTask { get; set; }

	public override string ToString()
	{
		return $"{_node.Name} ({_status})";
	}

	public bool CanUpdateNow()
	{
		//TODO:  check for blocking nodes (update before/after)
		//TODO:  check for r/w locks
		//TODO:  check prior frame:  this node complete + r/w locks

		__.GetLogger()._EzError(_status == FrameStatus.SCHEDULED,
			"only should run this method if scheduled and trying to put to pending");

		////ensure all children are finished
		//foreach (var child in _activeChildren)
		//{
		//	if (child._status < FrameStatus.HIERARCHY_FINISHED)
		//	{
		//		return false;
		//	}
		//}

		//ensure parent has run to SELF_FINISHED before starting		
		if (_parent == null)
		{
			__.GetLogger()._EzError(_node is RootNode);
		}
		else
		{
			var testStatus = _parent._status;
			var result = testStatus is FrameStatus.FINISHED_WAITING_FOR_CHILDREN or FrameStatus.SCHEDULED
				or FrameStatus.PENDING or FrameStatus.RUNNING;
			__.GetLogger()._EzError(result);
			if (_parent._status != FrameStatus.FINISHED_WAITING_FOR_CHILDREN)
			{
				return false;
			}
		}

		//ensure nodes we run after are completed
		foreach (var otherNode in _updateAfter)
		{
			if (otherNode._status != FrameStatus.HIERARCHY_FINISHED)
			{
				return false;
			}
		}


		return true;
	}
}