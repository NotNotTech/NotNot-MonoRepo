namespace NotNot.SimStorm;

public abstract class FixedTimestepNode : SimNode
{
	private TimeSpan _intervalOffset = TimeSpan.Zero;


	private TimeSpan _nextRunTime;

	/// <summary>
	///    if our update is more than this many frames out of date, we ignore others.
	///    <para> only matters if FixedStep==true</para>
	/// </summary>
	public int CatchUpMaxFrames = 1;

	/// <summary>
	///    If set to true, attempts to ensure we update at precisely the rate specified.  if we execute slightly later than the
	///    TargetFrameRate,
	///    the extra delay is not ignored and is taken into consideration on future updates.
	/// </summary>
	public bool FixedStep = false;

	/// <summary>
	///    by default, all FixedTimestepNodes of the same interval update on the same frame.
	///    If you don't want this node to update at the same time as others, set this to however many frames you want it's
	///    update to be offset by;
	/// </summary>
	public int FrameOffset;

	/// <summary>
	///    the target framerate you want to execute at.
	///    Note that nested groups will already be constrained by the parent group TargetFrameRate,
	///    but this property can still be set for the child group. In that case the child group will update at the slowest of
	///    the two TargetFrameRates.
	///    <para>default is int.MaxValue (update as fast as possible (every tick))</para>
	/// </summary>
	public int TargetFps;

	//{
	//	get => (int)(TimeSpan.TicksPerSecond / _targetUpdateInterval.Ticks);
	//	set => _targetUpdateInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / value);
	//}
	private TimeSpan _targetUpdateInterval => TimeSpan.FromTicks(TimeSpan.TicksPerSecond / TargetFps);

	//	get => (int)(_nextRunOffset * TargetFps / _targetUpdateInterval);
	//	set => _nextRunOffset = _targetUpdateInterval / TargetFps * value; 
	//}
	private TimeSpan _nextRunOffset => _targetUpdateInterval / TargetFps * FrameOffset;

	internal override void OnFrameStarting(Frame frame, ICollection<SimNode> allNodesToUpdateInFrame)
	{
		if (TargetFps == 0)
		{
			//register this node and it's children for participation in this frame.
			base.OnFrameStarting(frame, allNodesToUpdateInFrame);
			//skip this logic
			return;
		}

		//run our node at a fixed interval, based on the target frame rate.  
		if (frame._stats._wallTime >= _nextRunTime)
		{
			var offset = _nextRunOffset;
			_nextRunTime = offset + _nextRunTime._IntervalNext(_targetUpdateInterval);
			if (frame._stats._wallTime >= _nextRunTime)
			{
				//we are running behind our target framerate.

				frame._slowRunningNodes.Add(this);
				var nextCatchupRunTime = offset + frame._stats._wallTime._IntervalNext(_targetUpdateInterval) -
				                         (_targetUpdateInterval * CatchUpMaxFrames);
				if (nextCatchupRunTime > _nextRunTime)
				{
					//further behind than our CatchUpMaxFrames so ignore missing updates beyond that.
					_nextRunTime = nextCatchupRunTime;
				}
			}

			//register this node and it's children for participation in this frame.
			base.OnFrameStarting(frame, allNodesToUpdateInFrame);
		}
		else
		{
			//not time to run this node or it's children!
			OnFrameSkipped(frame, $"FixedTimestepNode ({this.Name}): not time to execute.    Frame {frame._stats._frameId}");
		}
	}
}