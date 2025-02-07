// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot.Diagnostics;
using System.Diagnostics;

namespace NotNot.SimStorm._scratch.Ecs;

/// <summary>
///    Time stats about the execution of a frame.
/// </summary>
public unsafe struct TimeStats
{
	public TimeStats()
	{
	}

	public TimeSpan _frameElapsed = default;
	public TimeSpan _wallTime = default;
	public int _frameId = 0;

	private const int SAMPLE_COUNT = 100;

	//private fixed float _lastFpsSamples[SAMPLE_COUNT];
	private fixed float _elapsedSamples[SAMPLE_COUNT];

	public float _avgMs = 0;
	public float _minMs = 0;
	public float _maxMs = 0;


	internal void Update(TimeSpan frameElapsed)
	{
		//simple fps metrics calculated

		_frameElapsed = frameElapsed;
		_wallTime = _wallTime.Add(frameElapsed);
		_frameId++;
		//var instantFps = (float)(1f / frameElapsed.TotalSeconds);
		var elapsedMs = (float)frameElapsed.TotalMilliseconds._Round(2);

		//TODO: instead of doing calculations on last 100 frames, store avg/min/max for last 10 seconds.

		fixed (float* ptr = _elapsedSamples)
		{
			var samples = new Span<float>(ptr, SAMPLE_COUNT);

			samples[_frameId % samples.Length] = elapsedMs;
			_avgMs = samples._Avg();
			_maxMs = samples._Max();
			_minMs = samples._Min();
		}

		var lst = new List<float>();
	}

	public override string ToString()
	{
		//var gcTime = GC.GetGCMemoryInfo().PauseDurations._Aggregate(TimeSpan.Zero, (time, sum) => time + sum);//..._Sum();
		var frameInfo = $"frame= {_frameId} @ {_wallTime.TotalSeconds._Round(0)}sec ";
		var historyInfo =
			$" history = {_frameElapsed.TotalMilliseconds._Round(2)}cur {_maxMs._Round(1)}max {_avgMs._Round(1)}avg {_minMs._Round(1)}min  ";
		var gcInfo =
			$" GC={__GcHelper.GetGcTimings()}"; // $" GC={GC.CollectionCount(0)} ({gcTime.TotalMilliseconds._Round(1)} ms)";

		return frameInfo + historyInfo + gcInfo;
	}
	//TODO: add stopwatch showing current frame execution time
}

/// <summary>
///    details about the update of the node
/// </summary>
public struct NodeUpdateStats
{
	private bool _isCtored = true;
	public TimeStats _timeStats = default;

	public PercentileSampler800<TimeSpan> _updateDurations = new();
	public PercentileSampler800<TimeSpan> _updateChildrenDurations = new();

	public TimeSpan _lastUpdateTime = default;
	public TimeSpan _lastUpdateHierarchyTime = default;
	public TimeSpan _avgUpdateTime = default;
	public TimeSpan _avgUpdateHierarchyTime = default;

	//public Tim
	//TODO: record time since last run of update()
	public TimeSpan _timeSinceLastUpdate = default;
	public Stopwatch _timeSinceLastUpdateStopwatch = Stopwatch.StartNew();

	public NodeUpdateStats()
	{
	}

	public void Update(Frame frame, NodeFrameState nodeState)
	{
		__.GetLogger()._EzCheckedThrow<SimStormException>(_isCtored, "you need to use a .ctor() otherwise fields are not init");
		__.GetLogger()._EzError(nodeState._status == FrameStatus.HIERARCHY_FINISHED);
		_timeSinceLastUpdate = frame._stats._wallTime - _timeStats._wallTime;
		_timeStats = frame._stats;
		_avgUpdateTime = (_avgUpdateTime + _lastUpdateTime + nodeState._updateTime) / 3;
		_avgUpdateHierarchyTime =
			(_avgUpdateHierarchyTime + _lastUpdateHierarchyTime + nodeState._updateHierarchyTime) / 3;
		_lastUpdateTime = nodeState._updateTime;
		_lastUpdateHierarchyTime = nodeState._updateHierarchyTime;
		_updateDurations.RecordSample(nodeState._updateTime);
		_updateChildrenDurations.RecordSample(nodeState._updateHierarchyTime - nodeState._updateTime);
	}

	public override string ToString()
	{
		__.GetLogger()._EzCheckedThrow<SimStormException>(_isCtored, "you need to use a .ctor() otherwise fields are not init");
		return
			$"self={_lastUpdateTime.TotalMilliseconds._Round(1)}ms, children={(_lastUpdateHierarchyTime - _lastUpdateTime).TotalMilliseconds._Round(1)}ms, " +
			$"selfAvg={+_avgUpdateTime.TotalMilliseconds._Round(1)}ms, childrenAvg={(_avgUpdateHierarchyTime - _avgUpdateTime).TotalMilliseconds._Round(1)}ms  " +
			$"updateDur={_updateDurations.ToString(ts => ts.TotalMilliseconds._Round(2))}  " +
			$"updateChildrenDur={_updateChildrenDurations.ToString(ts => ts.TotalMilliseconds._Round(2))}  " +
			$"timeSinceLastUpdate={_timeSinceLastUpdate.TotalMilliseconds._Round(1)}ms";
	}
}

/// <summary>
///    The status of a <see cref="SimNode" /> execution during a Frame.
/// </summary>
public enum FrameStatus
{
	/// <summary>
	///    empty.  uninitialized.  error state.
	/// </summary>
	NONE,

	/// <summary>
	///    node is scheduled for execution this frame
	/// </summary>
	SCHEDULED,

	/// <summary>
	///    Node is about to execute  .
	///    <para>OBSOLETE: only briefly (a few nanoseconds) in this state before moving to RUNNING</para>
	/// </summary>
	PENDING,

	/// <summary>
	///    The node <see cref="SimNode.Update(Frame)" /> method is executing.
	/// </summary>
	RUNNING,

	/// <summary>
	///    this node update() method completed, but it's children nodes ative (this frame) are not yet known to be finished.
	/// </summary>
	FINISHED_WAITING_FOR_CHILDREN,

	/// <summary>
	///    this node and all children (active this frame) are finished
	/// </summary>
	HIERARCHY_FINISHED,
}