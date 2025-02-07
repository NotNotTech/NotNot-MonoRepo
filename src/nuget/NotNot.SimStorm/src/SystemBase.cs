namespace NotNot.SimStorm;

/// <summary>
///    base class for all nodes used by an engine
/// </summary>
public abstract class SystemBase : FixedTimestepNode
{
#if DEBUG
	private bool _DEBUG_calledUpdate;
#endif

	/// <summary>
	///    a lightweight node that is meant to be used as a "field" of the simNode.  never detached and lives for the lifetime
	///    of the parent simNode.
	/// </summary>
	private List<ISystemField> _fieldNodeChildren = new();

	protected NodeFrameState _lastUpdateState;

	/// <summary>
	///    add a "Field" to this System.  SystemField get notified on Initialize/Update/Dispose and can be considered
	///    "lightweight" nodes.
	/// </summary>
	/// <param name="systemField"></param>
	public void AddField(ISystemField systemField)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(IsDisposed == false);
		_fieldNodeChildren.Add(systemField);
		if (IsInitialized)
		{
			systemField.OnInitialize(this)._SyncWait();
		}
	}

	protected sealed override async Task OnUpdate(Frame frame, NodeFrameState nodeState)
	{
		_lastUpdateState = nodeState;
#if DEBUG
		_DEBUG_calledUpdate = false;
#endif
		await OnUpdate(frame);
#if DEBUG
		__.GetLogger()._EzErrorThrow<SimStormException>(_DEBUG_calledUpdate,
			$"Your System of type '{GetType().FullName}' override of OnUpdate() didn't call base.OnUpdate() like you are supposed to");
#endif
	}

	/// <summary>
	///    when overriding, be sure to call the base.OnUpdate() method
	/// </summary>
	protected virtual async Task OnUpdate(Frame frame)
	{
#if DEBUG
		_DEBUG_calledUpdate = true;
#endif
		foreach (var fieldNode in _fieldNodeChildren)
		{
			await fieldNode.OnUpdate(frame);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnInitialize()
	{
		foreach (var fieldNode in _fieldNodeChildren)
		{
			await fieldNode.OnInitialize(this);
		}

		await base.OnInitialize();
	}

	/// <param name="disposing"></param>
	/// <inheritdoc />
	protected override void OnDispose(bool managedDisposing)
	{
		foreach (var fieldNode in _fieldNodeChildren)
		{
			fieldNode.Dispose();
		}

		base.OnDispose(managedDisposing);
	}
}