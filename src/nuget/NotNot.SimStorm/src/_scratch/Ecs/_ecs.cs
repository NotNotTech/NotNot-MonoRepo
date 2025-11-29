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
using NotNot.SimStorm._scratch.Ecs.Allocation;
using System.Collections.Concurrent;
using System.Threading.Channels;
using NotNot.Advanced;
using NotNot.Collections.SpanLike;

namespace NotNot.SimStorm._scratch.Ecs;

//public delegate void CreateEntitiesCallback(ReadOnlySpan<AccessToken> accessTokens, ReadOnlySpan<EntityHandle> entities, Archetype archetype);
//public delegate void DeleteEntitiesCallback(ReadOnlySpan<AccessToken> accessTokens, Archetype archetype);
using CreateEntitiesCallback =
	Action<(EphemeralReadMem<AccessToken> accessTokens, EphemeralReadMem<EntityHandle> entityHandles, Archetype archetype)>;
using DeleteEntitiesCallback = Action<(EphemeralReadMem<AccessToken> accessTokens, Archetype archetype)>;

/// <summary>
///    A "Simulation World".  all archetypes, their entities, their components, and systems are under a single world.
/// </summary>
public class World : SystemBase
{
	public EntityManager entityManager = new();

	public ContainerNode Phase0_EntityMaint = new();
	public ContainerNode Phase1_Physics = new();
	public ContainerNode Phase2_Simulation = new();
	public ContainerNode Phase3_End = new();

	protected override ValueTask OnInitialize()
	{
		Phase1_Physics._updateAfter.Add(Phase0_EntityMaint.Name);
		Phase2_Simulation._updateAfter.Add(Phase1_Physics.Name);
		Phase3_End._updateAfter.Add(Phase2_Simulation.Name);


		Phase0_EntityMaint.AddChild(entityManager);
		AddChild(Phase0_EntityMaint);
		AddChild(Phase1_Physics);
		AddChild(Phase2_Simulation);
		AddChild(Phase3_End);


		//entityManager.Initialize();
		//AddChild(entityManager);
		return base.OnInitialize();
	}

	//protected override Task OnUpdate(Frame frame)
	//{
	//	//throw new NotImplementedException();
	//	return Task.CompletedTask;
	//}
}

//public delegate void EntityCreateCallback(Archetype archetype, ReadOnlySpan<EntityHandle> entities);

/// <summary>
///    simplified data channel, where the TFramePacket needs to manage concurrency
///    This should only be added to the Phase0 StateSync System, as it takes the past frame's work and readies it for
///    asynchronous systems to use.
/// </summary>
public class FrameDataChannelSlim<TFramePacket> : ISystemField where TFramePacket : FramePacketBase, new()
{
	private Channel<TFramePacket> _channel;
	public TFramePacket CurrentFrameData;
	public ConcurrentQueue<TFramePacket> recycled = new();

	public FrameDataChannelSlim(int maxFrames)
	{
		MaxFrames = maxFrames;
		_channel = Channel.CreateBounded<TFramePacket>(
			new BoundedChannelOptions(maxFrames) { FullMode = BoundedChannelFullMode.DropOldest });
		//	Reader = _channel.Reader;
	}

	/// <summary>
	///    Max number of frames to buffer for async systems to use.
	/// </summary>
	public int MaxFrames { get; private set; }

	ValueTask ISystemField.OnInitialize(SystemBase parent)
	{
		if (CurrentFrameData == null)
		{
			CurrentFrameData = new TFramePacket();
			CurrentFrameData.Initialize();
		}

		return ValueTask.CompletedTask;
	}


	///// <summary>
	///// 
	///// </summary>
	///// <returns></returns>
	//public ValueTask<TFramePacket> GetFinishedFramePacket()

	async Task ISystemField.OnUpdate(Frame frame)
	{
		var _curVersion = CurrentFrameData._version;
		CurrentFrameData.Seal();

		var reader = _channel.Reader;
		var writer = _channel.Writer;


		//__.AssertOnce(reader.Count < MaxFrames,
		//	"why are we at max frames enqueued?  async worker isn't processing fast enough?");


		//while (reader.Count > MaxFrames && reader.TryRead(out var staleFramePacket))
		//{
		//	//only here if exceeding 2 extra frames   usually an extra frame is culled by reader.
		//	__.DEBUG.WriteLine($"dropped frame packet due to count exceeding MaxFrames ({MaxFrames})");
		//	staleFramePacket.Recycle();
		//	recycled.Enqueue(staleFramePacket);
		//}

		await writer.WriteAsync(CurrentFrameData);

		__.GetLogger()._EzErrorThrow<SimStormException>(CurrentFrameData._version == _curVersion,
			"race condition failed.   still being written as we are prepping for read by async systems");

		//ready next (current frame starting)
		if (!recycled.TryDequeue(out CurrentFrameData))
		{
			CurrentFrameData = new TFramePacket();
		}

		CurrentFrameData.Initialize();
	}

	void ISystemField.Dispose()
	{
		CurrentFrameData.Recycle();
		_channel.Writer.Complete();
		while (_channel.Reader.TryRead(out var packet))
		{
			packet.Recycle();
		}

		recycled.Clear();
	}

	/// <summary>
	///    read one packet, blocking if none is available.
	/// </summary>
	/// <param name="toRecycle"></param>
	/// <returns></returns>
	public async ValueTask<TFramePacket> Read(TFramePacket toRecycle = null)
	{
		//recycle done frame
		if (toRecycle != null)
		{
			if (toRecycle.IsInitialized)
			{
				toRecycle.Recycle();
			}

			recycled.Enqueue(toRecycle);
		}

		//get frame
		var toReturn = await _channel.Reader.ReadAsync();
		return toReturn;
		//TFramePacket toReturn=null;
		//do
		//{
		//	if (toReturn != null)
		//	{
		//		toReturn.Recycle();
		//		recycled.Enqueue(toReturn);
		//	}
		//	toReturn = await _channel.Reader.ReadAsync();
		//} while (_channel.Reader.Count >= MaxFrames); //recycle any older than our max

		//return toReturn;
	}
}

/// <summary>
///    A system that is a child of a world
///    <para>sets up required execution order stuff and gives references to important things</para>
///    <para>
///       If you don't like/need this constraint, inherit from SystemBase instead, but don't interact with ECS if you
///       do.
///    </para>
/// </summary>
public abstract class System : SystemBase
{
	public EntityManager entityManager;
	public World world;

	protected override void OnRegister()
	{
		try
		{
			world = GetHierarchy().DangerousGetArray().OfType<World>().First();
			entityManager = world.entityManager;
		}
		catch (Exception ex)
		{
			throw new ApplicationException(
				"could not find world in hiearchy. a System should be registered as a child of a World.  Otherwise inherit from SystemBase directly",
				ex);
		}

		//ensure that nodes execute after entityManager finishes.  
		_updateAfter.Add(entityManager.Name);


		base.OnRegister();
	}

	protected override void OnUnregister()
	{
		world = null;
		entityManager = null;

		base.OnUnregister();
	}
}

/// <summary>
///    coordinates the creation/deletion/query of entities and their components for the given <see cref="World" />
/// </summary>
public partial class EntityManager : SystemBase //init / setup
{
	public AccessGuard _accessGuard;
	public EntityRegistry _entityRegistry = new();

	protected override ValueTask OnInitialize()
	{
		_accessGuard = new AccessGuard(manager);
		return base.OnInitialize();
	}

	protected override void OnDispose(bool managedDisposing)
	{
		_lookup.Dispose();
		_lookup = null;
		//_archetypes.Clear();
		//_archetypes = null;
		base.OnDispose(managedDisposing);
	}
}

public partial class EntityManager //archetype management
{
	//TODO: make adding archetypes threadsafe
	//TODO: make racecheck versions of all collections


	//public Dictionary<long, List<Archetype>> _archetypeLookup = new();


	/// <summary>
	///    helper for finding archetypes by components
	/// </summary>
	public ArchetypeFinder _lookup = new();

	/// <summary>
	///    increments when archetypes are added/removed, signaling queries are invalidated.
	/// </summary>
	public int _version => _lookup._version;


	public Archetype GetOrCreateArchetype(HashSet<Type> componentTypes)
	{
		return GetOrCreateArchetype(InstanceNameHelper.CreateName<Archetype>(), componentTypes);
	}

	public Archetype GetOrCreateArchetype(string name, HashSet<Type> componentTypes)
	{
		if (!_lookup.TryGetArchetype(componentTypes, out var archetype))
		{
			archetype = new Archetype(name, componentTypes);
			archetype.Initialize(this, _entityRegistry);
			_lookup.Add(archetype);
		}

		return archetype;
	}

	/// <summary>
	///    allow adding a custom archetype.  be sure to call this before another archetype matching the same componentTypes is
	///    added/created.
	/// </summary>
	public bool TryAddArchetype(Archetype archetype)
	{
		if (_lookup.TryGetArchetype(archetype._componentTypes, out var found))
		{
			return false;
		}

		if (archetype.IsInitialized == false)
		{
			archetype.Initialize(this, _entityRegistry);
		}

		_lookup.Add(archetype);
		return true;
	}

	/// <summary>
	///    helper for finding archetypes by components.   use via <see cref="_lookup" />
	/// </summary>
	public class ArchetypeFinder
	{
		//public List<Archetype> _archetypes = new();
		public HashSet<Archetype> _archetypes = new();

		public Dictionary<long, List<Archetype>> _storage = new();

		public int _version;

		public void Dispose()
		{
			_archetypes.Clear();
			_archetypes = null;
			foreach (var (hash, list) in _storage)
			{
				list.Clear();
			}

			_storage.Clear();
			_storage = null;
		}

		public void Add(Archetype archetype)
		{
			//__.GetLogger()._EzCheckedThrow<SimStormException>(_archetypes.Contains(archetype) == false, "why already added");
			var checkHash = GetCheckHash(archetype);
			if (!_storage.TryGetValue(checkHash, out var list))
			{
				list = new List<Archetype>();
				_storage.Add(checkHash, list);
			}

			__.GetLogger()._EzErrorThrow<SimStormException>(list.Contains(archetype) == false, "already contains");
			__.GetLogger()._EzError(list.Count == 0,
				"we have an archetype checkHash collision.  this is expected to be very rare so investigate.  the design supports this though");

			foreach (var other in list)
			{
				__.GetLogger()._EzErrorThrow<SimStormException>(other._componentTypes.SetEquals(archetype._componentTypes) == false,
					"can not add archetype that manages identical components");
			}

			__.GetLogger()._EzErrorThrow<SimStormException>(archetype.IsInitialized,
				"archetype should be initialized before adding to lookup.");

			list.Add(archetype);
			_archetypes.Add(archetype);
			_version++;
		}

		public void Remove(Archetype archetype)
		{
			var hash = GetCheckHash(archetype);
			var list = _storage[hash];
			var result = list.Remove(archetype);
			__.GetLogger()._EzErrorThrow<SimStormException>(result);
			if (list.Count == 0)
			{
				_storage.Remove(hash);
			}

			_version++;
		}

		public long GetCheckHash(Archetype archetype)
		{
			return GetCheckHash(archetype._componentTypes);
		}

		public long GetCheckHash(IEnumerable<Type> types)
		{
			long checkHash = 0;
			foreach (var type in types)
			{
				checkHash += type.GetHashCode();
			}

			return checkHash;
		}

		public bool TryGetArchetype(HashSet<Type> componentTypes, out Archetype archetype)
		{
			//__.ERROR.AssertOnce(false, "todo: change List of componentTypes to something better performant");
			var checkHash = GetCheckHash(componentTypes);
			if (!_storage.TryGetValue(checkHash, out var list) || list.Count == 0)
			{
				archetype = null;
				return false;
			}

			if (list.Count == 1)
			{
				var toReturn = list[0];
				__.GetLogger()._EzErrorThrow<SimStormException>(toReturn._componentTypes.Count == componentTypes.Count);
			}

			//if here, there is a checkHash collision, so multiple items in list need to be picked from
			foreach (var potential in list)
			{
				if (potential._componentTypes.SetEquals(componentTypes))
				{
					archetype = potential;
					return true;
				}
			}

			archetype = null;
			return false;
		}

		//[ThreadStatic]
		//private List<Archetype> _queryTemp = new();
		//public MemoryOwner<Archetype> Query<TC1>()
		//{
		//	__.GetLogger()._EzErrorThrow<SimStormException>(_queryTemp.Count == 0);
		//	foreach (var archetype in _archetypes)
		//	{
		//		if (archetype._componentTypes.Contains(typeof(TC1)))
		//		{
		//			_queryTemp.Add(archetype);
		//		}
		//	}
		//	var toReturn = MemoryOwner<Archetype>.Allocate(_queryTemp.Count);
		//	_queryTemp.CopyTo(toReturn.DangerousGetArray().Array);
		//	_queryTemp.Clear();
		//	return toReturn;

		//}
	}
}

public partial class EntityManager //entity creation
{
	private ConcurrentQueue<_EnqueueCreateArgs_Internal> _createQueue = new();
	private ConcurrentQueue<(EntityHandle[] toDelete, DeleteEntitiesCallback doneCallback)> _deleteQueue = new();


	//public void EnqueueCreateEntity(int count, Archetype archetype, Action<Mem<AccessToken>, Mem<EntityHandle>, Archetype> doneCallback)


	public void EnqueueCreateEntity(int count, Archetype archetype, CreateEntitiesCallback doneCallback)
	{
		EnqueueCreateEntity(count, archetype, Mem<object>.Empty, doneCallback);
	}


	public void EnqueueCreateEntity(int count, Archetype archetype, Mem<object> partitionComponents,
		CreateEntitiesCallback doneCallback)
	{
		_createQueue.Enqueue(new _EnqueueCreateArgs_Internal(count, archetype,
			SharedComponentGroup.GetOrCreate(partitionComponents), doneCallback));
	}

	public void EnqueueCreateEntity(int count, Archetype archetype, SharedComponentGroup partitionGroup,
		CreateEntitiesCallback doneCallback)
	{
		_createQueue.Enqueue(new _EnqueueCreateArgs_Internal(count, archetype, partitionGroup, doneCallback));
	}

	public void EnqueueDeleteEntity(ReadOnlySpan<EntityHandle> toDelete, DeleteEntitiesCallback doneCallback)
	{
		_deleteQueue.Enqueue((toDelete.ToArray(), doneCallback));
	}

	/// <summary>
	///    process all enqueued entity changes.  If a callback enqueues additional changes, those are also done immediately.
	/// </summary>
	protected void ProcessEnqueued_Phase0()
	{
		var didWork = true;
		while (didWork) //in case a callback enqueues more entity changes, keep doing all of them now
		{
			didWork = false;
			while (_deleteQueue.TryDequeue(out var tuple))
			{
				didWork = true;
				var (toDelete, doneCallback) = tuple;
				_DoDeleteEntities_Phase0(toDelete, doneCallback);
			}

			while (_createQueue.TryDequeue(out var args))
			{
				didWork = true;
				DoCreateEntities_Phase0(ref args);
			}
		}
	}

	/// <summary>
	///    do deletes that have been enqueued while everything is stopped
	/// </summary>
	/// <param name="toDelete"></param>
	/// <param name="doneCallback"></param>
	private void _DoDeleteEntities_Phase0(Span<EntityHandle> toDelete, DeleteEntitiesCallback doneCallback)
	{
		if (toDelete.Length == 0)
		{
			return;
		}

		///obtain the actual accessTokens for the entities to be deleted
		using var accessTokensSO = RentedMem<AccessToken>.Allocate(toDelete.Length);
		var accessTokens = accessTokensSO.Span;
		_entityRegistry.Get(toDelete, accessTokens);

		//sort so that in order by page
		accessTokens.Sort();
		__.GetLogger()._EzCheckedThrow<SimStormException>(accessTokens[0].pageId <= accessTokens[accessTokens.Length - 1].pageId,
			"not sorted properly?");

		//group be pageId and send off for deletion
		var pageStartIndex = 0;
		var pageLength = 0;
		var pageId = accessTokens[0].pageId;


		static void __DoDeleteHelper(DeleteEntitiesCallback doneCallback, Span<AccessToken> accessTokens,
			int pageStartIndex, int pageLength)
		{
			//that page finished.
			//find archetype and delte for it then call callback
			var pageSpan = accessTokens.Slice(pageStartIndex, pageLength);

			//all owners are archetypes, so leap of faith cast
			var archetype = pageSpan[0].GetOwner() as Archetype;
			//let archetype invoke the required callback function
			archetype.DoDeleteEntities_Phase0(pageSpan, doneCallback, pageSpan[0].GetPage());
		}

		for (var i = 0; i < accessTokens.Length; i++)
		{
			if (accessTokens[i].pageId == pageId)
			{
				pageLength++;
				continue;
			}

			__DoDeleteHelper(doneCallback, accessTokens, pageStartIndex, pageLength);

			//start marking our next page
			pageStartIndex = i;
			pageLength = 1;
		}

		//handle our last page
		__DoDeleteHelper(doneCallback, accessTokens, pageStartIndex, pageLength);
	}

	private void DoCreateEntities_Phase0(ref _EnqueueCreateArgs_Internal args)
	{
		//int count, Archetype archetype, Mem<object> partitionComponents, CreateEntitiesCallback doneCallback
		//var(count, archetype,partitionComponents, doneCallback) = tuple;
		args.archetype.DoCreateEntities_Phase0(ref args);
	}

	//private void TryRepackEntities_Sync()
	//{
	//	//TODO: add expected cost of update metrics for current frame and past frames (to SimNode/Frame)
	//	//check expected cost of update, if equal to lowest point in last 10 frames, or if at least 10 frames has gone by and we are expected lower than past frame, do a repack.

	//	//repace should 

	//	throw new NotImplementedException();
	//}

	protected override async Task OnUpdate(Frame frame)
	{
		_accessGuard._enabled = false;
		ProcessEnqueued_Phase0();
		_accessGuard._enabled = true;
		//TryRepackEntities_Sync();
		await base.OnUpdate(frame);
	}

	public record struct EnqueueCreateArgs(int count, Archetype archetype, Mem<object> partitionComponents,
		CreateEntitiesCallback doneCallback);

	internal record struct _EnqueueCreateArgs_Internal(int count, Archetype archetype,
		SharedComponentGroup partitionGroup, CreateEntitiesCallback doneCallback);
}

public partial class EntityManager //entity query
{
	/// <summary>
	///    create a query to efficiently loop through entities and their components
	/// </summary>
	/// <param name="options"></param>
	/// <returns></returns>
	public EntityQuery Query(QueryOptions options)
	{
		var toReturn = new EntityQuery(this, options);
		toReturn.RefreshQuery();
		return toReturn;
	}
}

public delegate void SelectRangeCallback_R<TC1>(Mem<EntityMetadata> meta, Mem<TC1> c1);

public delegate void SelectRangeCallback_W<TC1>(Mem<EntityMetadata> meta, Mem<TC1> c1);

public delegate void SelectRangeCallback_RR<TC1, TC2>(Mem<EntityMetadata> meta, Mem<TC1> c1, Mem<TC2> c2);

public delegate void SelectRangeCallback_WR<TC1, TC2>(Mem<EntityMetadata> meta, Mem<TC1> c1, Mem<TC2> c2);

public delegate void SelectRangeCallback_WW<TC1, TC2>(Mem<EntityMetadata> meta, Mem<TC1> c1, Mem<TC2> c2);

public delegate void SelectRangeCallback_RRR<TC1, TC2, TC3>(Mem<EntityMetadata> meta, Mem<TC1> c1,
	Mem<TC2> c2, Mem<TC3> c3);

public delegate void SelectRangeCallback_WRR<TC1, TC2, TC3>(Mem<EntityMetadata> meta, Mem<TC1> c1, Mem<TC2> c2,
	Mem<TC3> c3);

public delegate void SelectRangeCallback_WWR<TC1, TC2, TC3>(Mem<EntityMetadata> meta, Mem<TC1> c1, Mem<TC2> c2,
	Mem<TC3> c3);

public delegate void SelectRangeCallback_WWW<TC1, TC2, TC3>(Mem<EntityMetadata> meta, Mem<TC1> c1, Mem<TC2> c2,
	Mem<TC3> c3);

public delegate void SelectRangeCallback_RRRR<TC1, TC2, TC3, TC4>(Mem<EntityMetadata> meta, Mem<TC1> c1,
	Mem<TC2> c2, Mem<TC3> c3, Mem<TC4> c4);

public delegate void SelectRangeCallback_WRRR<TC1, TC2, TC3, TC4>(Mem<EntityMetadata> meta, Mem<TC1> c1,
	Mem<TC2> c2, Mem<TC3> c3, Mem<TC4> c4);

public delegate void SelectRangeCallback_WWRR<TC1, TC2, TC3, TC4>(Mem<EntityMetadata> meta, Mem<TC1> c1,
	Mem<TC2> c2, Mem<TC3> c3, Mem<TC4> c4);

public delegate void SelectRangeCallback_WWWR<TC1, TC2, TC3, TC4>(Mem<EntityMetadata> meta, Mem<TC1> c1,
	Mem<TC2> c2, Mem<TC3> c3, Mem<TC4> c4);

public delegate void SelectRangeCallback_WWWW<TC1, TC2, TC3, TC4>(Mem<EntityMetadata> meta, Mem<TC1> c1,
	Mem<TC2> c2, Mem<TC3> c3, Mem<TC4> c4);

/// <summary>
///    filtering criteria to narrow down an EntityQuery
/// </summary>
public class QueryOptions
{
	/// <summary>
	///    all of these components must exist on an archetype, otherwise the archetype (all it's entities) are rejected from
	///    the query.
	/// </summary>
	public HashSet<Type> all = new();

	/// <summary>
	///    as long as one or more of these components exist on an archetype, it (all it's entities) are included in the query.
	///    otherwise the archetype is rejected.
	/// </summary>
	public HashSet<Type> any = new();

	/// <summary>
	///    an optional delegate to let you add/remove archetypes from the query.
	/// </summary>
	/// <remarks>
	///    be sure that added archetypes have all TComponents you may request in <see cref="EntityQuery.SelectRange" />
	///    otherwise an exception will be thrown
	/// </remarks>
	public Action<List<Archetype>> custom;

	/// <summary>
	///    Disables the automatic requery when archetypes are added to the world.
	/// </summary>
	/// <remarks>
	///    default is false, added archetypes causes will cause the query to re-aquire on it's next call.
	///    Setting to true means that when archetypes are added that match your query, they won't be included until you
	///    Manually call <see cref="EntityQuery.RefreshQuery" />.
	///    <para>
	///       You might want to set to True for better performance, if you have a specialized query and you know no other
	///       archetypes added will impact it.
	///    </para>
	/// </remarks>
	public bool disableAutoRefresh;

	/// <summary>
	///    If any of the specified Component Types are included on an archetype, that archetype (all it's entities) are
	///    rejected from this query.
	/// </summary>
	public HashSet<Type> none = new();

	/// <summary>
	///    filters returned chunks to only include those with the all specified sharedComponents (found in the chunk's
	///    <see cref="SharedComponentGroup" />)
	/// </summary>
	public List<object> sharedComponent = new();

	/// <summary>
	///    filters returned chunks to only include those with the all specified TYPES of shared components  (found in the
	///    chunk's <see cref="SharedComponentGroup" />)
	/// </summary>
	public List<Type> sharedComponentTypes = new();

	public QueryOptions() { }

	public QueryOptions(QueryOptions cloneFrom)
	{
		any.UnionWith(cloneFrom.any);
		all.UnionWith(cloneFrom.all);
		none.UnionWith(cloneFrom.none);
		custom = cloneFrom.custom;
		disableAutoRefresh = cloneFrom.disableAutoRefresh;
	}
}

public class EntityQuery
{
	internal EntityManager _entityManager;
	internal int _entityManagerVersion = -1;


	public QueryOptions _options;

	/// <summary>
	///    archetypes matching our query
	/// </summary>
	public List<Archetype> archetypes;

	public EntityQuery(EntityManager entityManager, QueryOptions options)
	{
		_entityManager = entityManager;
		archetypes = new List<Archetype>(entityManager._lookup._archetypes);
		_options = options;
	}

	public EntityQuery(EntityQuery cloneFrom)
	{
		_entityManager = cloneFrom._entityManager;
		archetypes = new List<Archetype>(cloneFrom.archetypes);
		_options = new QueryOptions(cloneFrom._options);
	}

	/// <summary>
	///    archetypes have been added/removed from the EntityManager.  This query needs to be refreshed to pick up changes.
	/// </summary>
	public bool IsOutOfDate => _entityManagerVersion != _entityManager._version;


	public void RefreshQuery()
	{
		archetypes.Clear();
		archetypes.AddRange(_entityManager._lookup._archetypes);
		_entityManagerVersion = _entityManager._version;


		for (var i = archetypes.Count - 1; i >= 0; i--)
		{
			var archetype = archetypes[i];
			var toRemove = false;
			//apply option filters
			if (_options.all != null && _options.all.Count > 0 &&
				 archetype._componentTypes.IsSupersetOf(_options.all) == false)
			{
				toRemove = true;
			}
			else if (_options.none != null && _options.none.Count > 0 && archetype._componentTypes.Overlaps(_options.none))
			{
				toRemove = true;
			}
			else if (_options.any != null && _options.any.Count > 0 &&
						archetype._componentTypes.Overlaps(_options.any) != true)
			{
				toRemove = true;
			}

			if (toRemove)
			{
				archetypes.RemoveAt(i);
			}
		}

		//apply option custom callback
		if (_options.custom != null)
		{
			_options.custom(archetypes);
		}
	}

	private void _TryAutoRefresh()
	{
		if (IsOutOfDate && _options.disableAutoRefresh != true)
		{
			RefreshQuery();
		}
	}

	public int Count()
	{
		_TryAutoRefresh();

		var toReturn = 0;


		//for all our archetypes, get the columns requested
		foreach (var archetype in archetypes)
		{
			toReturn += archetype.Count;
		}

		return toReturn;
	}

	private void _ReadNotify<TC>()
	{
		_entityManager._accessGuard.ReadNotify<TC>();
	}

	private void _WriteNotify<TC>()
	{
		_entityManager._accessGuard.WriteNotify<TC>();
	}

	#region //////////////////    SelectQuery   ///////////////////////////////////////////////////////////

	/// <summary>
	///    helper to find chunks for the SelectRange methods
	/// </summary>
	private void _SelectRangeHelper<TC1>(Action<Chunk<EntityMetadata>, Chunk<TC1>> helperCallback)
	{
		_TryAutoRefresh();

		//for all our archetypes, get the columns requested
		foreach (var archetype in archetypes)
		{
			if (archetype.IsDisposed || archetype.Count == 0)
			{
				continue;
			}


			//loop through all pages (archetypes are partitioned by sharedComponentGroups into pages) 
			foreach (var (sharedComponents, page) in archetype._pages)
			{
				//filter by sharedComponents (skip pages that do not match)
				{
					var usePage = true;
					//apply the specific object filter
					foreach (var componentToFind in _options.sharedComponent)
					{
						if (sharedComponents.Contains(componentToFind) == false)
						{
							usePage = false;
							break;
						}
					}

					//apply the type filter
					foreach (var type in _options.sharedComponentTypes)
					{
						if (sharedComponents.storage.ContainsKey(type) == false)
						{
							usePage |= false;
							break;
						}
					}

					if (usePage == false)
					{
						continue;
					}
				}

				//return the page's entities to the caller
				var entityMetaCol = page.GetColumn<EntityMetadata>();

				for (var i = 0; i < entityMetaCol.Count; i++)
				{
					helperCallback(
						entityMetaCol[i] as Chunk<EntityMetadata>
						, page.GetColumn<TC1>()[i] as Chunk<TC1>
					);
				}
			}
		}
	}

	/// <summary>
	///    helper to find chunks for the SelectRange methods
	/// </summary>
	private void _SelectRangeHelper<TC1, TC2>(Action<Chunk<EntityMetadata>, Chunk<TC1>, Chunk<TC2>> helperCallback)
	{
		_TryAutoRefresh();

		//for all our archetypes, get the columns requested
		foreach (var archetype in archetypes)
		{
			if (archetype.IsDisposed || archetype.Count == 0)
			{
				continue;
			}

			foreach (var (partitionComponents, page) in archetype._pages)
			{
				//filter by partitionComponents (skip pages that do not match)
				{
					var usePage = true;
					foreach (var componentToFind in _options.sharedComponent)
					{
						if (partitionComponents.Contains(componentToFind) == false)
						{
							usePage = false;
							break;
						}
					}

					if (usePage == false)
					{
						continue;
					}
				}

				//return the page's entities to the caller
				var entityMetaCol = page.GetColumn<EntityMetadata>();

				for (var i = 0; i < entityMetaCol.Count; i++)
				{
					helperCallback(
						entityMetaCol[i] as Chunk<EntityMetadata>
						, page.GetColumn<TC1>()[i] as Chunk<TC1>
						, page.GetColumn<TC2>()[i] as Chunk<TC2>
					);
				}
			}
		}
	}

	/// <summary>
	///    helper to find chunks for the SelectRange methods
	/// </summary>
	private void _SelectRangeHelper<TC1, TC2, TC3>(
		Action<Chunk<EntityMetadata>, Chunk<TC1>, Chunk<TC2>, Chunk<TC3>> helperCallback)
	{
		_TryAutoRefresh();

		//for all our archetypes, get the columns requested
		foreach (var archetype in archetypes)
		{
			if (archetype.IsDisposed || archetype.Count == 0)
			{
				continue;
			}

			foreach (var (partitionComponents, page) in archetype._pages)
			{
				//filter by partitionComponents (skip pages that do not match)
				{
					var usePage = true;
					foreach (var componentToFind in _options.sharedComponent)
					{
						if (partitionComponents.Contains(componentToFind) == false)
						{
							usePage = false;
							break;
						}
					}

					if (usePage == false)
					{
						continue;
					}
				}

				//return the page's entities to the caller
				var entityMetaCol = page.GetColumn<EntityMetadata>();

				for (var i = 0; i < entityMetaCol.Count; i++)
				{
					helperCallback(
						entityMetaCol[i] as Chunk<EntityMetadata>
						, page.GetColumn<TC1>()[i] as Chunk<TC1>
						, page.GetColumn<TC2>()[i] as Chunk<TC2>
						, page.GetColumn<TC3>()[i] as Chunk<TC3>
					);
				}
			}
		}
	}

	/// <summary>
	///    helper to find chunks for the SelectRange methods
	/// </summary>
	private void _SelectRangeHelper<TC1, TC2, TC3, TC4>(
		Action<Chunk<EntityMetadata>, Chunk<TC1>, Chunk<TC2>, Chunk<TC3>, Chunk<TC4>> helperCallback)
	{
		_TryAutoRefresh();

		//for all our archetypes, get the columns requested
		foreach (var archetype in archetypes)
		{
			if (archetype.IsDisposed || archetype.Count == 0)
			{
				continue;
			}

			foreach (var (partitionComponents, page) in archetype._pages)
			{
				//filter by partitionComponents (skip pages that do not match)
				{
					var usePage = true;
					foreach (var componentToFind in _options.sharedComponent)
					{
						if (partitionComponents.Contains(componentToFind) == false)
						{
							usePage = false;
							break;
						}
					}

					if (usePage == false)
					{
						continue;
					}
				}

				//return the page's entities to the caller
				var entityMetaCol = page.GetColumn<EntityMetadata>();

				for (var i = 0; i < entityMetaCol.Count; i++)
				{
					helperCallback(
						entityMetaCol[i] as Chunk<EntityMetadata>
						, page.GetColumn<TC1>()[i] as Chunk<TC1>
						, page.GetColumn<TC2>()[i] as Chunk<TC2>
						, page.GetColumn<TC3>()[i] as Chunk<TC3>
						, page.GetColumn<TC4>()[i] as Chunk<TC4>
					);
				}
			}
		}
	}

	public void Run<TC1>(SelectRangeCallback_R<TC1> callback)
	{
		_ReadNotify<TC1>();

		_SelectRangeHelper<TC1>((meta, c1) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
		));
	}

	public void Run<TC1>(SelectRangeCallback_W<TC1> callback)
	{
		_WriteNotify<TC1>();

		_SelectRangeHelper<TC1>((meta, c1) =>
		{
			callback(
				Mem.Wrap(meta.StorageSlice)
				, Mem.Wrap(c1.StorageSlice)
			);
		});
	}


	public void Run<TC1, TC2>(SelectRangeCallback_RR<TC1, TC2> callback)
	{
		_ReadNotify<TC1>();
		_ReadNotify<TC2>();

		_SelectRangeHelper<TC1, TC2>((meta, c1, c2) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
		));
	}

	public void Run<TC1, TC2>(SelectRangeCallback_WR<TC1, TC2> callback)
	{
		_WriteNotify<TC1>();
		_ReadNotify<TC2>();

		_SelectRangeHelper<TC1, TC2>((meta, c1, c2) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
		));
	}

	public void Run<TC1, TC2>(SelectRangeCallback_WW<TC1, TC2> callback)
	{
		_WriteNotify<TC1>();
		_WriteNotify<TC2>();

		_SelectRangeHelper<TC1, TC2>((meta, c1, c2) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
		));
	}

	public void Run<TC1, TC2, TC3>(SelectRangeCallback_RRR<TC1, TC2, TC3> callback)
	{
		_ReadNotify<TC1>();
		_ReadNotify<TC2>();
		_ReadNotify<TC3>();

		_SelectRangeHelper<TC1, TC2, TC3>((meta, c1, c2, c3) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
			, Mem.Wrap(c3.StorageSlice)
		));
	}

	public void Run<TC1, TC2, TC3>(SelectRangeCallback_WRR<TC1, TC2, TC3> callback)
	{
		_WriteNotify<TC1>();
		_ReadNotify<TC2>();
		_ReadNotify<TC3>();

		_SelectRangeHelper<TC1, TC2, TC3>((meta, c1, c2, c3) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
			, Mem.Wrap(c3.StorageSlice)
		));
	}

	public void Run<TC1, TC2, TC3>(SelectRangeCallback_WWR<TC1, TC2, TC3> callback)
	{
		_WriteNotify<TC1>();
		_WriteNotify<TC2>();
		_ReadNotify<TC3>();

		_SelectRangeHelper<TC1, TC2, TC3>((meta, c1, c2, c3) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
			, Mem.Wrap(c3.StorageSlice)
		));
	}

	public void Run<TC1, TC2, TC3>(SelectRangeCallback_WWW<TC1, TC2, TC3> callback)
	{
		_WriteNotify<TC1>();
		_WriteNotify<TC2>();
		_WriteNotify<TC3>();

		_SelectRangeHelper<TC1, TC2, TC3>((meta, c1, c2, c3) => callback(
			Mem.Wrap(meta.StorageSlice)
			, Mem.Wrap(c1.StorageSlice)
			, Mem.Wrap(c2.StorageSlice)
			, Mem.Wrap(c3.StorageSlice)
		));
	}

	#endregion SelectQuery
}

public partial class Archetype : DisposeGuard //initialization
{
	//internal int _hashId;


	public HashSet<Type> _componentTypes;
	public EntityManager _entityManager;

	private EntityRegistry _entityRegistry;

	public Archetype(string name, HashSet<Type> componentTypes)
	{
		Name = name;
		_componentTypes = componentTypes;
	}

	public string Name { get; set; }

	//public bool AutoPack { get; init; } = true;
	public int ChunkSize { get; init; } = 1000;

	//public bool IsInitialized { get=>_initTask!= null && _initTask.IsCompleted; }
	//internal Task _initTask;

	public bool IsInitialized { get; private set; }

	protected internal virtual void Initialize(EntityManager entityManager, EntityRegistry entityRegistry)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(!IsInitialized, "already initialized");
		IsInitialized = true;

		_entityManager = entityManager;

		//if (_page == null)
		//{
		//	_page = new Page(true, ChunkSize, _componentTypes);
		//}
		_entityRegistry = entityRegistry;
		//_page.Initialize(this, entityRegistry);
	}


	protected override void OnDispose(bool managedDisposing)
	{
		//_page.Dispose();

		foreach (var pair in _pages)
		{
			pair.Value.Dispose();
		}

		_pages.Clear();
		_pages = null;

		base.OnDispose(managedDisposing);
	}
}

public partial class Archetype : IPageOwner
{
	void IPageOwner.ReadNotify<TComponent>()
	{
		_entityManager._accessGuard.ReadNotify<TComponent>();
	}

	void IPageOwner.WriteNotify<TComponent>()
	{
		_entityManager._accessGuard.WriteNotify<TComponent>();
	}
}

public partial class Archetype //passthrough of page stuff
{
	private int _count;

	public Dictionary<SharedComponentGroup, Page> _pages = new();

	//public Page _page;
	//public short ArchtypeId { get => _page._pageId; }
	//public int Version { get => _page._version; }
	public int Count
	{
		get
		{
#if CHECKED
			var tempCount = 0;
			foreach (var (partitionGroup, page) in _pages)
			{
				tempCount += page.Count;
			}

			__.GetLogger()._EzCheckedThrow<SimStormException>(tempCount == _count);
#endif

			return _count;
		}
	}

	internal void DoCreateEntities_Phase0(ref EntityManager._EnqueueCreateArgs_Internal args)
	{
		var (count, archetype, partitionGroup, doneCallback) = args;
		__.GetLogger()._EzError(this == archetype);


		//need to get a unique page per partitionComponent grouping

		//create entityHandles
		using var entityHandlesMem = Mem.Rent<EntityHandle>(count);
		var entityHandles = entityHandlesMem.Span;
		using var accessTokensMem = Mem.Rent<AccessToken>(count);
		var accessTokens = accessTokensMem.Span;
		_entityRegistry.Alloc(entityHandles);

		var page = _pages._GetOrAdd(partitionGroup, () =>
		{
			var page = new Page(true, ChunkSize, _componentTypes, partitionGroup);
			page.Initialize(this, _entityRegistry);
			return page;
		});

		page.AllocEntityNew(accessTokens, entityHandles);

		_count += entityHandles.Length;
		doneCallback((accessTokensMem.CastEphermialRO(), entityHandlesMem.CastEphermialRO(), this));
	}

	/// <summary>
	///    deletes entities from a specific page
	/// </summary>
	/// <param name="toDelete"></param>
	/// <param name="doneCallback"></param>
	/// <param name="containingPage"></param>
	internal void DoDeleteEntities_Phase0(Span<AccessToken> toDelete, DeleteEntitiesCallback doneCallback,
		Page containingPage)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(_pages.ContainsValue(containingPage),
			"the specified page should be part of this archetype, otherwise the following logic is invalid");
		containingPage.Free(toDelete);
		//call the callback to notify
		using var pageMem = Mem.Rent<AccessToken>(toDelete);
		_count -= toDelete.Length;
		doneCallback((pageMem.CastEphermial(), this));
	}
}

/// <summary>
///    A group of object-based components shared by multiple entities.  All entities in a chunk reference the same
///    SharedComponentGroup.
///    <para>use the static <see cref="GetOrCreate(object[])" /> function to obtain one</para>
/// </summary>
/// <remarks>
///    While this object/pattern is named "Shared Component", internally it is more akin to a "Partition Group".
///    Meaning, this provides a way of splitting an archetype's entities into chunks based on the unique grouping of
///    partition components.
///    <para>
///       for example, having a RenderMesh as an item in this will split entiies so that only those with the same
///       renderMesh will be in the same chunk.
///       this is useful for building the rendering system off of, to aid in batching instances
///    </para>
/// </remarks>
[ThreadSafety(ThreadSituation.Always)]
public class SharedComponentGroup
{
	/// <summary>
	///    when an instance is collected by the GC, it's hash is put here so that next call to .GetOrCreate() we remove the
	///    slot from _GLOBAL_STORAGE
	/// </summary>
	private static ConcurrentQueue<long> _gcCollected = new();

	/// <summary>
	///    pool of all instances
	///    key is a hashcode.  but because hashcodes can collide, we use a list as value
	/// </summary>
	private static ConcurrentDictionary<long, List<WeakReference<SharedComponentGroup>>> _GLOBAL_STORAGE = new();

	public long hashSum;

	/// <summary>
	///    stores the components shared by all entities in the partition
	/// </summary>
	public Dictionary<Type, object> storage = new();


	private SharedComponentGroup(long hashSum, ref Mem<object> components)
	{
		this.hashSum = hashSum;
		foreach (var component in components)
		{
			storage.Add(component.GetType(), component);
		}
	}

	protected static long _ComputeHashSum(ref Mem<object> components)
	{
		var objHashSum = 0;
		var typeHashSum = 0;
		foreach (var component in components)
		{
			objHashSum += component.GetHashCode();
			typeHashSum += component.GetType().GetHashCode();
		}

		var toReturn = ((long)typeHashSum << 32) | (uint)objHashSum;
		return toReturn;
	}

	~SharedComponentGroup()
	{
		_gcCollected.Enqueue(hashSum);
	}

	public static SharedComponentGroup GetOrCreate(params object[] components)
	{
		return GetOrCreate(Mem.Wrap(components));
	}


	/// <summary>
	///    factory method, either returns an existing, or creates a new object from the given parameters
	/// </summary>
	public static SharedComponentGroup GetOrCreate(Mem<object> components)
	{
		//cleanup disposed ParitionComponents, if any
		if (_gcCollected.Count > 0)
		{
			lock (_gcCollected)
			{
				while (_gcCollected.TryDequeue(out var keyToDelete))
				{
					if (_GLOBAL_STORAGE.TryRemove(keyToDelete, out var deletedList))
					{
						//removed successfully, but need to make sure all weakRefs are deallocated
						for (var i = deletedList.Count - 1; i >= 0; i--)
						{
							var deletedWR = deletedList[i];
							if (deletedWR == null || deletedWR.TryGetTarget(out var deletedPC) == false)
							{
								//truely dead
								deletedList.RemoveAt(i);
							}
						}

						if (deletedList.Count != 0)
						{
							//it's not actually dead, so put it back
							var result = _GLOBAL_STORAGE.TryAdd(keyToDelete, deletedList);
							__.GetLogger()._EzErrorThrow<SimStormException>(result);
						}
					}
				}
			}
		}


		var hash = _ComputeHashSum(ref components);
		WeakReference<SharedComponentGroup> weakRef;

		//first get list, optimal path requires no locking
		if (_GLOBAL_STORAGE.TryGetValue(hash, out var listWR))
		{
			//get from list
			for (var i = listWR.Count - 1; i >= 0; i--)
			{
				var maybeWeakRef = listWR[i];
			}

			foreach (var maybeWeakRef in listWR)
			{
				if (maybeWeakRef.TryGetTarget(out var maybePC))
				{
					if (maybePC.Matches(components))
					{
						return maybePC;
					}
				}
			}
		}

		//if here, lock and try again, creating as we go, if needed
		lock (_gcCollected)
		{
			//get or create list
			if (!_GLOBAL_STORAGE.TryGetValue(hash, out listWR))
			{
				listWR = new List<WeakReference<SharedComponentGroup>>();
				var result = _GLOBAL_STORAGE.TryAdd(hash, listWR);
				__.GetLogger()._EzErrorThrow<SimStormException>(result);
			}

			//have list
			foreach (var maybeWeakRef in listWR)
			{
				if (maybeWeakRef.TryGetTarget(out var maybePC))
				{
					if (maybePC.Matches(components))
					{
						return maybePC;
					}
				}
			}

			//create and add to list
			var newPC = new SharedComponentGroup(hash, ref components);
			weakRef = new WeakReference<SharedComponentGroup>(newPC);
			listWR.Add(weakRef);
			return newPC;
		}
	}

	private bool Matches(Mem<object> components)
	{
		if (storage.Count != components.Length)
		{
			return false;
		}
		//var computedHash = _ComputeHashSum(ref components);
		//if(hashSum != computedHash)
		//{
		//	return false;
		//}

		foreach (var component in components)
		{
			var type = component.GetType();
			if (!storage[type].Equals(component))
			{
				return false;
			}
		}

		return true;
	}

	public bool Contains(object componentToFind)
	{
		var type = componentToFind.GetType();
		if (storage.TryGetValue(type, out var foundComponent))
		{
			return componentToFind.Equals(foundComponent);
		}

		return false;
	}

	public T Get<T>()
	{
		return (T)storage[typeof(T)];
	}
}

/// <summary>
///    not actually used, just a friendly hint that a given class/struct is meant to be used as a component.
/// </summary>
public interface IEcsComponent
{
}

///// <summary>
///// functionality to pool instances for discovery later
///// </summary>
//public abstract class PooledBase : DisposeGuard
//{

//}

///// <summary>
///// callback used when querying components
///// </summary>
//public delegate void ComponentQueryCallback_w<TComponent>(in AccessToken accessTokens, ref TComponent c1);
//public delegate void ComponentQueryCallback_r<TComponent>(in AccessToken accessTokens, in TComponent c1);
//public delegate void ComponentQueryCallback_writeAll<TC1, TC2>(in AccessToken accessTokens, ref TC1 c1, ref TC2 c2);
//public delegate void ComponentQueryCallback<TC1, TC2, TC3>(in AccessToken accessTokens, ref TC1 c1, ref TC2 c2, ref TC3 c3);

//public partial class Archetype  //query entities owned by this archetype
//{

//	/// <summary>
//	/// efficient query of entities owned by this archetype.
//	/// </summary>
//	public void Query<TComponent>(Mem<AccessToken> accessTokens, ComponentQueryCallback_w<TComponent> callback)
//	{
//		var span = accessTokens.Span;
//		foreach (ref var token in span)
//		{
//			ref var c1 = ref token.GetComponentWriteRef<TComponent>();
//			callback(in token, ref c1);
//		}
//	}
//	public unsafe async Task QueryAsnyc<TComponent>(Mem<AccessToken> accessTokens, ComponentQueryCallback_w<TComponent> callback)
//	{
//		var array = accessTokens.DangerousGetArray();

//		fixed (AccessToken* _pArray = &array.Array![0])
//		{
//			var pArray = _pArray;
//			ParallelFor.Range(0, array.Count, (start, end) =>
//			{

//				for (var i = start; i < end; i++)
//				{
//					ref var token = ref pArray[i];
//					ref var c1 = ref token.GetComponentWriteRef<TComponent>();
//					callback(in token, ref c1);
//				}
//			});
//		}

//	}

//	/// <summary>
//	/// given a column and token, return a ref to component
//	/// </summary>
//	private static ref TC __QueryHelper<TC>(Span<Chunk<TC>> column, ref AccessToken token)
//	{
//		return ref column[token.slotRef.chunkIndex].UnsafeArray[token.slotRef.slotIndex];
//	}

//	public unsafe void QueryAsync<TC1, TC2>(Mem<AccessToken> accessTokens, ComponentQueryCallback_writeAll<TC1, TC2> callback)
//	{
//		//ComponentQueryCallback_w<int> test1 = (in AccessToken accessToken, ref int c1) => { };
//		//Query<int>(null, (in AccessToken accessToken, ref int c1) => {

//		//});

//		//OPTIMIZE LATER: assumign input is sorted, can get the current chunk and itterate through, then move to the next chunk.
//		//currently each chunk is re-aquired from the column for each element.   maybe that is okay perfwise tho.

//		//var array = accessTokens.ArraySegment();
//		fixed (AccessToken* _pArray = accessTokens.Span)
//		{
//			var pArray = _pArray;
//			ParallelFor.Range(0, accessTokens.length, (start, end) =>
//			{
//				var col1 = Chunk<TC1>._GLOBAL_LOOKUP[ArchtypeId]._AsSpan_Unsafe();
//				var col2 = Chunk<TC2>._GLOBAL_LOOKUP[ArchtypeId]._AsSpan_Unsafe();

//				for (var i = start; i < end; i++)
//				{
//					ref var pageToken = ref pArray[i];
//					var chunkIndex = pageToken.slotRef.chunkIndex;
//					var slotIndex = pageToken.slotRef.slotIndex;
//					//ref var c1 = ref token.GetComponentWriteRef<TC1>();
//					callback(in pageToken, ref __QueryHelper(col1, ref pageToken), ref __QueryHelper(col2, ref pageToken));
//				}
//			});
//		}
//	}
//	public unsafe void QueryAsync<TC1, TC2>(ComponentQueryCallback_writeAll<TC1, TC2> callback)
//	{
//		var colMeta = Chunk<EntityMetadata>._GLOBAL_LOOKUP[ArchtypeId];

//		ParallelFor.Range(0, colMeta.Count, (start, end) =>
//		{
//			var col0 = colMeta._AsSpan_Unsafe();

//			var col1 = Chunk<TC1>._GLOBAL_LOOKUP[ArchtypeId]._AsSpan_Unsafe(); ;
//			var col2 = Chunk<TC2>._GLOBAL_LOOKUP[ArchtypeId]._AsSpan_Unsafe(); ;
//			for (var chunkIndex = start; chunkIndex < end; chunkIndex++)
//			{
//				var metaChunk = col0[chunkIndex];
//				var metaArray = metaChunk.UnsafeArray;

//				if (metaChunk._count == 0)
//				{
//					continue;
//				}

//				for (var slotIndex = 0; slotIndex < metaChunk._length; slotIndex++)
//				{
//					var entityToken = metaArray[slotIndex];
//					var pageToken = entityToken.accessToken;
//					if (entityToken.IsAlive == false)
//					{
//						continue;
//					}
//					callback(in pageToken, ref __QueryHelper(col1, ref pageToken), ref __QueryHelper(col2, ref pageToken));
//				}
//			}
//		});

//	}

//}

//public interface IGroupByComponent<TSelf> where TSelf : class
//{

//}
