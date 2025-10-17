// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Generic;
using System.Text;
using NotNot.Advanced;

namespace NotNot.NodeFlow;





/// <summary>
/// a lightweight re-implementation of "SimNode", with only minimal features
/// <para>Needs more work for advanced features: does not currently support reattach of initialized nodes, nor multithreaded execution</para>
/// </summary>
public abstract class SlimNode : DisposeGuard
{
	protected bool IsRoot { get; init; }
	public bool IsInitialized { get; private set; }

	public virtual SlimNode Parent { get; private set; }


	private RootNode _rootNode;
	/// <summary>
	/// quick access to the rootNode, eg for root specific features like time, or global services
	/// <para>will be null until attached to node graph</para>
	/// </summary>
	public RootNode RootNode
	{
		get
		{
			if (_rootNode is null)
			{
				_rootNode = Parent?.RootNode;
				__.AssertIfNot(_rootNode is not null, "should be setable unless adding when not attached to node graph.  disable this assert if that's the case");
			}
			return _rootNode;
		}
		protected set
		{
			_rootNode = value;
		}
	}



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

	/// <summary>
	/// internal helper, do not use/call directly.  Update is pumped via the RootNode.RootUpdate() method.
	/// </summary>
	/// <param name="currentTick"></param>
	/// <returns></returns>
	protected async ValueTask Update(TickState currentTick)
	{
		_updateCount = currentTick.UpdateCount;
		var tempCounter = _callCounter;
		await OnUpdate(currentTick);
		__.AssertIfNot(_callCounter > tempCounter, "didn't call base method?");
		_lifecycleCt.ThrowIfCancellationRequested();
	}
	/// <summary>
	/// the rootNode's frame count (when this node's update was last invoked)
	/// </summary>
	protected ulong _updateCount;

	/// <summary>
	/// <para>children are updated inside the base call, so you can execute before/after.</para>
	/// do not use/call directly.  Update is pumped via the RootNode.RootUpdate() method.
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
		child.RootNode = this.RootNode;
		if (child is ISingletonNode singletonNode)
		{
			RootNode._DoRegisterSingleton(singletonNode);
		}
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


		if (child is ISingletonNode singletonNode)
		{
			RootNode._DoUnRegisterSingleton(singletonNode);
		}

		child.Parent = null;
		child.RootNode = null;
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
	protected override void OnDispose(bool managedDisposing)
	{


		if (managedDisposing)
		{
			//using var copy = _children._MemoryOwnerCopy();
			foreach (var child in _children)
			{
				child.Dispose();
			}
		}
		base.OnDispose(managedDisposing);
		_children = null;
	}

	/// <summary>
	/// when ISingletonNode's are added to the node graph, they are registered in the root node for easy access.
	/// <para>this easily lets you refernce it.  target Must already have been added, and max 1 per type.</para>
	/// </summary>
	public TSingletonNode GetSingletonNode<TSingletonNode>() where TSingletonNode : SlimNode, ISingletonNode
	{
		__.AssertIfNot(RootNode is not null, "not attached to node graph");

		if (RootNode._singletonCache.TryGetValue(typeof(TSingletonNode), out var node))
		{
			return (TSingletonNode)node;
		}
		else
		{
			//search for derived types in the dictionary.  if just one, add it to the dictionary cache for next time then return it.
			//if multiple, throw error.
			TSingletonNode? found = null;
			foreach (var kvp in RootNode._singletonCache)
			{
				if (typeof(TSingletonNode).IsAssignableFrom(kvp.Key))
				{
					if (found is not null)
					{
						__.GetLogger()._EzError(false, "multiple singleton nodes found for type " + typeof(TSingletonNode).FullName + ".  cannot determine which to return.  use exact type instead of base type.");
						__.AssertIfNot(false);
					}
					found = (TSingletonNode)kvp.Value;
				}
			}
			if (found is not null)
			{
				RootNode._singletonCache.Add(typeof(TSingletonNode), found);
				return found;
			}
			throw __.Throw("no singleton node found for type " + typeof(TSingletonNode).FullName);

		}
	}
}

/// <summary>
/// allows psudo-singleton / DI Service behavior.  There should only be one of this type added to the node-graph at once.
/// <para>you can retrieve using mySlimNode.GetSingletonNode{TSingletonNode}()</para>
/// </summary>
public interface ISingletonNode
{
}
