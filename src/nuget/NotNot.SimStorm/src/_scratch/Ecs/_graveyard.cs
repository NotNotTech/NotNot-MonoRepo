// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

//////namespace NotNot.Ecs._graveyard;


//////public class ArchetypeSlotAllocator
//////{

//////	public struct InternalData
//////	{
//////		public EntityHandle _handle;
//////		public ArchetypeSlotInfo _slotInfo;
//////	}

//////	private static int _ALLOCATOR_VERSION;
//////	public int _allocatorId = _ALLOCATOR_VERSION++;

//////	private int _nextNewId;
//////	private int _version;

//////	public Dictionary<int, InternalData> _storage = new();
//////	public List<int> _free = new();


//////	public EntityHandle Alloc(ArchetypeSlotInfo slotInfo)
//////	{
//////		var version = _version++;
//////		if (!_free._TryTakeLast(out var id))
//////		{
//////			id = _nextNewId++;
//////		}
//////		var toReturn = new EntityHandle() { _allocatorId = _allocatorId, _id = id, _version = version };
//////		_storage.Add(id, new() { _handle = toReturn, _slotInfo = slotInfo });
//////		return toReturn;
//////	}

//////	public bool TryFree(EntityHandle handle)
//////	{
//////		if (!_storage.TryGetValue(handle._id, out var internalData))
//////		{
//////			//__.GetLogger()._EzErrorThrow(false, "handle not found to Free.  maybe already freed?");
//////			return false;
//////		}
//////		if (internalData._handle != handle)
//////		{
//////			//__.GetLogger()._EzErrorThrow(internalData._handle==handle,"handles do not match.  old handle?")
//////			return false;
//////		}
//////		_storage.Remove(handle._id);
//////		_free.Add(handle._id);
//////		return true;
//////	}
//////	public bool TryGet(EntityHandle handle, out ArchetypeSlotInfo slotInfo)
//////	{
//////		if (_storage.TryGetValue(handle._id, out var internalData))
//////		{
//////			if (internalData._handle == handle)
//////			{
//////				slotInfo = internalData._slotInfo;
//////				return true;
//////			}

//////		}
//////		slotInfo = default;
//////		return false;
//////	}
//////	public bool TryChange(EntityHandle handle, in ArchetypeSlotInfo slotInfo)
//////	{
//////		if (_storage.TryGetValue(handle._id, out var internalData))
//////		{
//////			if (internalData._handle == handle)
//////			{
//////				internalData._slotInfo = slotInfo;
//////				_storage[handle._id] = internalData;
//////				return true;
//////			}

//////		}
//////		return false;
//////	}
//////}


//////public class DataChunk
//////{
//////	public const int CHUNK_SIZE = 1000;
//////}

///////// <summary>
///////// the actual data for components is split into fixed-sized "chunks"
///////// </summary>
///////// <typeparam name="TComponent"></typeparam>
//////public class DataChunk<TComponent> : DataChunk
//////{

//////	public static List<DataChunk<TComponent>> _GLOBAL_DATACHUNK_LOOKUP = new();
//////	public int _globalLookupId;

//////	public TComponent[] _storage = new TComponent[CHUNK_SIZE];

//////	public DataColumn _parent;

//////	public DataChunk()
//////	{
//////		_globalLookupId = _GLOBAL_DATACHUNK_LOOKUP.Count;
//////		_GLOBAL_DATACHUNK_LOOKUP.Add(this);
//////	}
//////}


///////// <summary>
///////// A column is all the chunks of a specific ComponentType, for a single archetype.
///////// </summary>
//////public abstract class DataColumn
//////{
//////	/// <summary>
//////	/// the Type of the component is used for query purposes
//////	/// </summary>
//////	public Type _componentType;
//////	public Archetype _parent;

//////	public DataColumn(Type componentType)
//////	{
//////		_componentType = componentType;
//////	}

//////	internal abstract void Alloc(int chunkId, int slotId, ref EntityHandle entityHandle, Archetype archetype);

//////	internal void Alloc(MemoryOwner<EntityToken> tokens)
//////	{
//////		throw new NotImplementedException();
//////	}

//////	internal abstract void Free(int chunkId, int slotId, ref EntityHandle entityHandle, Archetype archetype);

//////	internal void Free(Span<EntityToken> tokensSpan)
//////	{
//////		throw new NotImplementedException();
//////	}
//////}


//////public class DataColumn<TComponent> : DataColumn
//////{
//////	public List<DataChunk<TComponent>> _pages = new();

//////	public DataColumn()
//////		: base(typeof(TComponent))
//////	{

//////	}

//////	internal override void Alloc(int chunkId, int slotId, ref EntityHandle entityHandle, Archetype archetype)
//////	{
//////		throw new NotImplementedException();
//////	}

//////	internal override void Free(int chunkId, int slotId, ref EntityHandle entityHandle, Archetype archetype)
//////	{
//////		throw new NotImplementedException();
//////	}
//////}


///////// <summary>
///////// token only gurenteed to be valid for a single frame.  after that the entity's archetype details might be invalid so may need to be reaquired.
///////// </summary>
//////public record struct EntityToken
//////{
//////	public EntityHandle _entityHandle;
//////	public ArchetypeEntityHandle _archHandle;
//////}

///////// <summary>
///////// a persistant handle to an entity, valid for it's lifetime
///////// </summary>
//////public record struct EntityHandle
//////{

//////	public int _worldId;
//////	public int _id;
//////	public int _version;

//////	///// <summary>
//////	///// store archetype so can default to looking up directly here, not the global entityStore
//////	///// </summary>
//////	//public ArchetypeSlotInfo _archetypeSlot;


//////	/// <summary>
//////	/// will dispose this entity in the Housekeeping phase (at start of update loop)
//////	/// </summary>
//////	public void EnqueueDispose()
//////	{
//////		throw new NotImplementedException();
//////	}

//////	/// <summary>
//////	/// get a component for this entity.  Need to be sure the calling system has the proper data permissions (read/write)
//////	/// </summary>
//////	/// <typeparam name="TComponent"></typeparam>
//////	/// <param name="callback"></param>
//////	/// <returns>if the entity doesn't have a component of this type, returns false</returns>
//////	public bool TryGetComponent<TComponent>(Action_Ref<TComponent> callback)
//////	{
//////		throw new NotImplementedException();
//////	}
//////	public bool TryGetComponent<TComponent1, TComponent2>(Action_Ref<TComponent1, TComponent2> callback)
//////	{
//////		throw new NotImplementedException();
//////	}

//////	public ref TComponent Component<TComponent>()
//////	{
//////		throw new NotImplementedException();
//////	}

//////}


//////public class EntityHandleAllocator
//////{

//////	public struct InternalData
//////	{
//////		public EntityHandle _handle;
//////		public ArchetypeSlotInfo _slotInfo;
//////	}

//////	private static int _ALLOCATOR_VERSION;
//////	public int _allocatorId = _ALLOCATOR_VERSION++;

//////	private int _nextNewId;
//////	private int _version;

//////	public Dictionary<int, InternalData> _storage = new();
//////	public List<int> _free = new();


//////	public EntityHandle Alloc(ArchetypeSlotInfo slotInfo)
//////	{
//////		var version = _version++;
//////		if (!_free._TryTakeLast(out var id))
//////		{
//////			id = _nextNewId++;
//////		}
//////		var toReturn = new EntityHandle() { _allocatorId = _allocatorId, _id = id, _version = version };
//////		_storage.Add(id, new() { _handle = toReturn, _slotInfo = slotInfo });
//////		return toReturn;
//////	}

//////	public bool TryFree(EntityHandle handle)
//////	{
//////		if (!_storage.TryGetValue(handle._id, out var internalData))
//////		{
//////			//__.GetLogger()._EzErrorThrow(false, "handle not found to Free.  maybe already freed?");
//////			return false;
//////		}
//////		if (internalData._handle != handle)
//////		{
//////			//__.GetLogger()._EzErrorThrow(internalData._handle==handle,"handles do not match.  old handle?")
//////			return false;
//////		}
//////		_storage.Remove(handle._id);
//////		_free.Add(handle._id);
//////		return true;
//////	}
//////	public bool TryGet(EntityHandle handle, out ArchetypeSlotInfo slotInfo)
//////	{
//////		if (_storage.TryGetValue(handle._id, out var internalData))
//////		{
//////			if (internalData._handle == handle)
//////			{
//////				slotInfo = internalData._slotInfo;
//////				return true;
//////			}

//////		}
//////		slotInfo = default;
//////		return false;
//////	}
//////	public bool TryChange(EntityHandle handle, in ArchetypeSlotInfo slotInfo)
//////	{
//////		if (_storage.TryGetValue(handle._id, out var internalData))
//////		{
//////			if (internalData._handle == handle)
//////			{
//////				internalData._slotInfo = slotInfo;
//////				_storage[handle._id] = internalData;
//////				return true;
//////			}


//////		}
//////		return false;
//////	}
//////}


///////// <summary>
///////// a handle to the entity's archetype data slot, valid until the archetype moves things around.
///////// </summary>
//////public record struct ArchetypeEntityHandle
//////{
//////	public int _archetypeId;
//////	public ChunkSlot _chunkSlot;
//////	/// <summary>
//////	/// maybe not needed?  use entityHandle version instead?
//////	/// </summary>
//////	public int _version;
//////}

//////public record struct ChunkSlot : IComparable<int>, IComparable<ChunkSlot>, IEquatable<int>, IEquatable<ChunkSlot>
//////{
//////	public int _id;


//////	public ChunkSlot(int id)
//////	{
//////		_id = id;
//////	}
//////	public ChunkSlot(int chunkId, int slotId)
//////	{
//////		_id = (chunkId * DataChunk.CHUNK_SIZE) + slotId;
//////	}
//////	public int Chunk { get => _id / DataChunk.CHUNK_SIZE; }
//////	public int Slot { get => _id % DataChunk.CHUNK_SIZE; }

//////	//public static bool operator <(ChunkSlot left, ChunkSlot right)
//////	//{
//////	//	return left.CompareTo(right) < 0;
//////	//}

//////	//public static bool operator <=(ChunkSlot left, ChunkSlot right)
//////	//{
//////	//	return left.CompareTo(right) <= 0;
//////	//}

//////	//public static bool operator >(ChunkSlot left, ChunkSlot right)
//////	//{
//////	//	return left.CompareTo(right) > 0;
//////	//}

//////	//public static bool operator >=(ChunkSlot left, ChunkSlot right)
//////	//{
//////	//	return left.CompareTo(right) >= 0;
//////	//}

//////	public bool Equals(int other)
//////	{
//////		return _id == other;
//////	}

//////	public int CompareTo(ChunkSlot other)
//////	{
//////		return _id.CompareTo(other._id);
//////	}
//////	public int CompareTo(int other)
//////	{
//////		return _id.CompareTo(other);
//////	}
//////}


//////public partial class Archetype  //entity allocations
//////{

//////	public Dictionary<EntityHandle, ArchetypeEntityHandle> _lookup = new();

//////	/// <summary>
//////	/// free slots, where int = chunk * ChunkSize + slotId
//////	/// </summary>
//////	public List<ChunkSlot> _free = new();
//////	private bool _isFreeSorted = false;

//////	private int _version;


//////	/**
//////	 * each entity needs to be stored in a registry, so that it can be found based on it's entityHandle
//////	 * 
//////	 * 
//////	 * 
//////	 * entity registry needs:
//////	 * - free slot tracking.  when free at end, decrement end.  thus FREE SLOTS MUST BE AN ORDERED COLLECTION
//////	 * - when add, add to first free slot (try to be contiguous)
//////	 * - track what chunks are in use
//////	 * 
//////	 * 
//////	 * 
//////	 * archetype needs to be in charge of it's own allocations/packing.
//////	 * allow outsiders to investigate via an archeHandle
//////	 * 
//////	 * 
//////	 */


//////	/// <summary>
//////	/// registry of chunkSlots in use AND free slots.
//////	/// </summary>
//////	public SlotStore<ArchetypeEntityHandle> _storage = new();

//////	private object writeLock = new();


//////	private void Allocate(ReadOnlySpan<EntityHandle> input, Span<EntityToken> output)
//////	{

//////		//make stuff based on input, fill the output span with tokens based on what was created

//////	}

//////	private void Test<T, V>(T entityHandles) where T : IEnumerable<V>
//////	{
//////		Span<int> test = stackalloc int[200];

//////		TEst2(test);

//////		Test<Span<int>, int>(test);

//////	}

//////	public MemoryOwner<EntityToken> Allocate<T>(T entityHandles) where T : IEnumerable<EntityHandle>
//////	{
//////		lock (writeLock)
//////		{
//////			var tokens = MemoryOwner<EntityToken>.Allocate(entityHandles.Length);
//////			var tokensSpan = tokens.Span;
//////			if (_isFreeSorted == false)
//////			{
//////				//sort in reverse order  (lowest id at end)
//////				_free.Sort((first, second) => second.CompareTo(first));
//////				_isFreeSorted = true;
//////			}


//////			for (var i = 0; i < entityHandles.Length; i++)
//////			{
//////				ref var token = ref tokensSpan[i];
//////				var entityHandle = entityHandles[i];

//////				token._entityHandle = entityHandle;


//////				if (_free._TryTakeLast(out var chunkSlot) == false)
//////				{
//////					var id = _storage.Alloc();
//////					chunkSlot = new ChunkSlot(id);
//////				}
//////				token._archHandle = new()
//////				{
//////					_archetypeId = _archetypeId,
//////					_chunkSlot = chunkSlot,
//////					_version = _version++,
//////				};


//////				//__.GetLogger()._EzErrorThrow(entityHandle._archetypeSlot == default(ArchetypeSlotInfo), "entityHandle already has arechetype allocated");
//////				__.GetLogger()._EzErrorThrow(_lookup.ContainsKey(entityHandle) == false, "entity already has a slot in this archetype allocated");
//////				_lookup.Add(entityHandle, token._archHandle);
//////			}

//////			//inform the DataChunks to allocate
//////			foreach (var dataColumn in this._componentColumns)
//////			{
//////				dataColumn.Alloc(tokens);
//////			}

//////			return tokens;
//////		}
//////	}

//////	public void Free(ReadOnlySpan<EntityHandle> entityHandles)
//////	{
//////		lock (writeLock)
//////		{
//////			_isFreeSorted = false;
//////			Span<EntityToken> tokensSpan = stackalloc EntityToken[entityHandles.Length];

//////			for (var i = 0; i < entityHandles.Length; i++)
//////			{
//////				ref var token = ref tokensSpan[i];
//////				var entityHandle = entityHandles[i];

//////				token._entityHandle = entityHandle;


//////				var result = _lookup.TryGetValue(entityHandle, out var archHandle);
//////				__.GetLogger()._EzErrorThrow(result, "trying to free entity not registered on this archetype");
//////				_lookup.Remove(entityHandle);
//////				_free.Add(archHandle._chunkSlot);
//////				token._archHandle = archHandle;
//////			}
//////			foreach (var dataColumn in this._componentColumns)
//////			{
//////				dataColumn.Free(tokensSpan);
//////			}

//////		}
//////	}

//////	public void Compact()
//////	{
//////		lock (writeLock)
//////		{
//////			if (_isFreeSorted == false)
//////			{
//////				//sort in reverse order  (lowest id at end)
//////				_free.Sort((first, second) => second.CompareTo(first));
//////				_isFreeSorted = true;
//////			}

//////			_storage.Free;
//////			//while(_free._TryTakeLast
//////		}

//////	}
//////}


///////// <summary>
///////// archetype tracks what components are associated with a specific kind of entity.
///////// </summary>
//////public partial class Archetype //ctor
//////{
//////	private static int _archetypeGlobalCounter;
//////	public int _archetypeId = _archetypeGlobalCounter++;
//////	/// <summary>
//////	/// the components are stored here
//////	/// </summary>
//////	public List<DataColumn> _componentColumns = new();
//////	public HashSet<Type> _componentTypes = new();

//////	/// <summary>
//////	/// friendly name.  archetypes are created automatically when an entity using them is created, but can be named for debugging purposes
//////	/// </summary>
//////	public string _name;

//////	//IMPLEMENTATION NOTE: don't need to have a generic form of archetype class.  it should build it's internal structure procedurally based on input Component types.

//////	/// <summary>
//////	/// hash of component types belonging to this archetype.  used to quickly narrow down possible archetype matches when searching for one based on componentTypes
//////	/// </summary>
//////	public int _hashId;
//////	public static int ComputeArchetypeHash(ReadOnlySpan<Type> componentTypes)
//////	{
//////		int hashCode = 0;
//////		foreach (var type in componentTypes)
//////		{
//////			hashCode += type.GetHashCode();
//////		}
//////		return hashCode;
//////	}


//////	public Archetype(string name, ReadOnlySpan<Type> componentTypes)
//////	{
//////		_name = name;
//////		_hashId = ComputeArchetypeHash(componentTypes);
//////		foreach (var type in componentTypes)
//////		{
//////			var result = _componentTypes.Add(type);
//////			__.GetLogger()._EzErrorThrow(result, "each component type must only be listed once");
//////			var dataColumnType = (typeof(DataColumn<>)).MakeGenericType(type);
//////			var dataColumn = Activator.CreateInstance(dataColumnType) as DataColumn;
//////			dataColumn._parent = this;
//////			_componentColumns.Add(dataColumn);
//////		}
//////	}
//////}

