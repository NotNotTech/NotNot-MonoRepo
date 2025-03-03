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

	protected async ValueTask Initialize()
	{

		if (IsInitialized)
		{
			__.GetLogger()._EzError(false, "why is Initialize called twice?", GetType().Name);
			__.Assert(false);

			return;
		}

		await OnInitialize();
		__.Assert(IsInitialized);
	}

	/// <summary>
	/// override to perform initialization logic
	/// <para>generally you want to call `base.OnInitialize() first, then do your logic, as then each item will be initialized line-by-line.</para>
	/// <para></para>
	/// </summary>
	/// <returns></returns>
	protected virtual async ValueTask OnInitialize()
	{
		IsInitialized = true;
		using var copy = _children._MemoryOwnerCopy();
		foreach (var child in copy)
		{
			await child.Initialize();
		}
	}


	protected async ValueTask Update(TickState currentTick)
	{
		var tempCounter = _callCounter;
		await OnUpdate(currentTick);
		__.Assert(_callCounter > tempCounter, "didn't call base method?");
	}

	protected virtual async ValueTask OnUpdate(TickState currentTick)
	{

		__.Assert(IsInitialized);
		__.Assert(IsDisposed is false);
		_callCounter++;

		using var copy = _children._MemoryOwnerCopy();
		foreach (var child in copy)
		{
			await child.OnUpdate(currentTick);
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
			await child.Initialize();
		}

		var childCounter = child._callCounter;
		child.OnAdded();
		__.Assert(child._callCounter > childCounter, "didn't call base method?");
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
		__.Assert(_children is not null && _children.Contains(child));
		var childCounter = child._callCounter;
		child.OnRemove();
		__.Assert(child._callCounter > childCounter, "didn't call base method?");

		_children.Remove(child);
	}

	/// <summary>
	/// called immediately before being removed
	/// </summary>
	protected virtual void OnRemove()
	{
		_callCounter++;
	}

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