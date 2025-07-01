// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Generic;
using System.Text;

namespace NotNot.NodeFlow;

/// <summary>
/// a lightweight re-implementation of "SimNode", with only minimal features
/// <para>Needs more work for advanced features: does not currently support reattach of initialized nodes, nor multithreaded execution</para>
/// </summary>
public abstract class SlimNode : AsyncDisposeGuard
{
	public virtual bool IsRoot { get; init; }
	public bool IsInitialized { get; private set; }

	public SlimNode Parent { get; private set; }

	private List<SlimNode>? _children;
	public bool HasChildren => _children is not null && _children.Count > 0;

	protected CancellationToken _lifecycleCt;

	public ReadOnlySpan<SlimNode> Children
	{
		get
		{
			if (_children is null)
			{
				return ReadOnlySpan<SlimNode>.Empty;
			}
			return _children._AsSpan_Unsafe();
		}
	}

	protected async ValueTask Initialize(CancellationToken lifecycleCt)
	{
		_lifecycleCt = lifecycleCt;

		__.ThrowIfNot((IsRoot && Parent is null) || (!IsRoot && Parent is not null), "either should be root, or no parent");

		if (IsInitialized)
		{
			__.GetLogger()._EzError(false, "why is Initialize called twice?", GetType().Name);
			__.AssertIfNot(false);

			return;
		}

		await OnInitialize();
		lifecycleCt.ThrowIfCancellationRequested();
		__.AssertIfNot(IsInitialized);
	}

	/// <summary>
	/// override to perform initialization logic
	/// <para>generally you want to call `base.OnInitialize() first, then do your logic, as then each item will be initialized line-by-line.</para>
	/// <para>added children init inside the base call, and children added after this will initialize immediately.</para>
	/// <para></para>
	/// </summary>
	/// <returns></returns>
	protected virtual async ValueTask OnInitialize()
	{
		IsInitialized = true;
		using var copy = _children._MemoryOwnerCopy();
		foreach (var child in copy)
		{
			await child.Initialize(_lifecycleCt);
			_lifecycleCt.ThrowIfCancellationRequested();
		}
	}


	protected async ValueTask Update(TickState currentTick)
	{
		var tempCounter = _callCounter;
		await OnUpdate(currentTick);
		__.AssertIfNot(_callCounter > tempCounter, "didn't call base method?");
		_lifecycleCt.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// <para>children are updated inside the base call, so you can execute before/after.</para>
	/// </summary>
	protected virtual async ValueTask OnUpdate(TickState currentTick)
	{

		__.AssertIfNot(IsInitialized);
		__.AssertIfNot(IsDisposed is false);
		_callCounter++;

		using var copy = _children._MemoryOwnerCopy();
		foreach (var child in copy)
		{
			await child.OnUpdate(currentTick);
			_lifecycleCt.ThrowIfCancellationRequested();
		}
	}

	public virtual async ValueTask AddChild(SlimNode child)
	{
		if (_children is null)
		{
			_children = new();
		}
		_children.Add(child);

		child.Parent = this;

		if (IsInitialized)
		{
			await child.Initialize(_lifecycleCt);
			_lifecycleCt.ThrowIfCancellationRequested();
		}

		var childCounter = child._callCounter;
		child.OnAdded();
		__.AssertIfNot(child._callCounter > childCounter, "didn't call base method?");
	}
	/// <summary>
	/// a private guard to ensure that the base method is called.
	/// </summary>
	private int _callCounter;

	/// <summary>
	/// called immediately after being added to a parent.
	/// </summary>
	protected virtual void OnAdded()
	{
		_callCounter++;
	}


	public virtual void RemoveChild(SlimNode child)
	{
		__.AssertIfNot(_children is not null && _children.Contains(child));
		var childCounter = child._callCounter;
		child.OnRemove();
		__.AssertIfNot(child._callCounter > childCounter, "didn't call base method?");

		_children.Remove(child);
	}

	/// <summary>
	/// called immediately before being removed
	/// </summary>
	protected virtual void OnRemove()
	{
		_callCounter++;
	}

	/// <summary>
	/// don't check _lifecycleCt when disposing, as we are already in a dispose state.
	/// </summary>
	/// <param name="managedDisposing"></param>
	/// <returns></returns>
	protected override async ValueTask OnDispose(bool managedDisposing)
	{

		await base.OnDispose(managedDisposing);
		if (managedDisposing)
		{
			using var copy = _children._MemoryOwnerCopy();
			foreach (var child in copy)
			{
				await child.DisposeAsync();
			}
		}

	}
}
