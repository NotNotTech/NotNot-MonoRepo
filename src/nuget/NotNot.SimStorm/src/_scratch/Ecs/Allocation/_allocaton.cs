// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot.Collections;
using NotNot.Collections._unused;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NotNot.SimStorm._scratch.Ecs.Allocation;

[StructLayout(LayoutKind.Explicit)]
public readonly struct EntityHandle : IComparable<EntityHandle>
{
	/// <summary>
	///    this entityHandle stored as a long
	/// </summary>
	[FieldOffset(0)] public readonly ulong _packValue;

	/// <summary>
	///    can be used to access slot into the entityRegistry
	/// </summary>
	[FieldOffset(0)] public readonly int id;

	/// <summary>
	/// </summary>
	[FieldOffset(4)] public readonly int version;

	public EntityHandle(ulong packValue) : this()
	{
		_packValue = packValue;
	}

	public EntityHandle(int id, int version) : this()
	{
		this.id = id;
		this.version = version;
	}

	public int CompareTo(EntityHandle other)
	{
		return id.CompareTo(other.id);
	}

	public override string ToString()
	{
		return $"{id}.{version}({_packValue:x16})";
		//try
		//{
		//	return $"{id}.{version}";
		//}
		//catch
		//{
		//	return "ERROR";
		//}
	}
}

/// <summary>
///    tracks all entities for the World.This is a central registry for all archetype pages of the world.
/// </summary>
/// <remarks>
///    <para>allocation/free is coordinated deeply with <see cref="Page" /> workflows.  </para>
///    .
/// </remarks>
public class EntityRegistry
{
	private volatile int _allocationVersion;


	public StructSlotArray<EntityData>
		_storage = new(2000000); //TODO: 1 million items = 232mb at current size of access token (needs size optimizations)

	public int Count => _storage.Count;


	public ref EntityData this[EntityHandle handle]
	{
		get
		{
			ref var toReturn = ref _storage[handle.id];
			__.GetLogger()._EzError(toReturn.handle._packValue == handle._packValue,
				"handle is invalid.  do you have a stale handle?  (use after dispose?)");
			return ref toReturn;
		}
	}

	public ref EntityData this[ulong packedHandle]
	{
		get
		{
			EntityHandle handle = new(packedHandle);
			return ref this[handle];
		}
	}


	public Mem<EntityHandle> Alloc(int count)
	{
		var toReturn = Mem<EntityHandle>.Allocate(count);
		Alloc(toReturn.Span);
		return toReturn;
	}

	public void Alloc(Span<EntityHandle> output)
	{
		using var allocSpanOwner = SpanGuard<int>.Allocate(output.Length);
		var allocIndicies = allocSpanOwner.Span;
		_storage.Alloc(allocIndicies);
		var storageArray = _storage._storage;

		var version = _allocationVersion++;
		for (var i = 0; i < output.Length; i++)
		{
			var index = allocIndicies[i];
			var handle = new EntityHandle(index, version);
			output[i] = handle;
			storageArray[index].handle = handle;
		}

		//__.GetLogger()._EzError(output._IsSorted(), "why not sorted?");
		__.GetLogger()._EzErrorThrow<SimStormException>(_storage._storage == storageArray,
			"MULTITHREADING CORRUPTION DETECTED: storageArray is different.  writes can not be done at the same time as resizes");
	}


	public void Free(Span<EntityHandle> handles)
	{
		__.DebugAssertOnceIfNot(handles._IsSorted(), "sort first");
		using var freeSpanOwner = SpanGuard<int>.Allocate(handles.Length);
		var freeSpan = freeSpanOwner.Span;
		for (var i = 0; i < handles.Length; i++)
		{
			var handle = handles[i];
			freeSpan[i] = handle.id;
		}

		_storage.Free(freeSpan);
	}

	public void Free(Span<AccessToken> tokens)
	{
		//__.CHECKED.AssertOnce(tokens._IsSorted(), "sort first"); //accessTokens are sorted differently
		using var freeSpanOwner = SpanGuard<int>.Allocate(tokens.Length);
		var freeSpan = freeSpanOwner.Span;
		for (var i = 0; i < tokens.Length; i++)
		{
			var handle = tokens[i].entityHandle;
			freeSpan[i] = handle.id;
		}

		//freeSpan._Sort();
		_storage.Free(freeSpan);
	}

	public void Get(Span<EntityHandle> handles, Span<AccessToken> output)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(handles.Length == output.Length);

		var storageArray = _storage._storage;

		for (var i = 0; i < output.Length; i++)
		{
			var handle = handles[i];
			output[i] = storageArray[handle.id].pageAccessToken;
		}
	}

	/// <summary>
	///    links an entityHandle to it's current accessToken in the EntityRegistry
	/// </summary>
	public record struct EntityData
	{
		public EntityHandle handle;
		public AccessToken pageAccessToken;
		public bool IsAlive => pageAccessToken.isAlive;
	}
}

/// <summary>
///    a "shortcut" providing direct (fast) access to an entities components.
/// </summary>
/// <remarks>
///    only good for the current frame, unless chunk packing is disabled.  in that case it's good for lifetime of
///    entity in the page.
/// </remarks>
public readonly record struct AccessToken : IComparable<AccessToken>
{
	/// <summary>
	///    check this to determine if the token is explicitly valid
	/// </summary>
	public readonly bool isAlive { get; init; }

	/// <summary>
	///    entityId
	/// </summary>
	public readonly EntityHandle entityHandle { get; init; }


	/// <summary>
	///    the id of the page that created/tracks this slotRef.  //TODO: make the page also use this ID as it's own (on create
	///    page, use it's ID)
	///    If needed, the page can be accessed via `Page._GLOBAL_LOOKUP(pageId)`
	/// </summary>
	public readonly int pageId { get; init; }

	/// <summary>
	///    used to verify page was not replaced with another
	/// </summary>
	public readonly int pageVersion { get; init; }

	public readonly SlotRef slotRef { get; init; }

	/// <summary>
	///    needs to match Page._packVersion, otherwise a pack took place and the token needs to be refreshed.
	/// </summary>
	public readonly uint packVersion { get; init; }
	//[Conditional("CHECKED")]
	//private void _CHECKED_VerifyInstance<T>()
	//{
	//	if (typeof(T) == typeof(EntityMetadata))
	//	{
	//		//otherwise can cause infinite recursion
	//		return;
	//	}
	//	ref readonly var entityMetadata = ref GetComponentReadRef<EntityMetadata>();
	//	__.GetLogger()._EzCheckedThrow<SimStormException>(entityMetadata.pageToken == this, "mismatch");
	//	//get chunk via the page, where the default way is direct through the Chunk<T>._GLOBAL_LOOKUP
	//	var atomId = Page.Atom.GetId<T>();

	//	//var chunk = GetPage()._componentColumns[typeof(EntityMetadata)][slotRef.columnChunkIndex] as Chunk<T>;
	//	var chunk = GetPage()._GetColumnsSpan()[atomId][slotRef.chunkIndex] as Chunk<T>;
	//	__.GetLogger()._EzCheckedThrow<SimStormException>(GetContainingChunk<T>() == chunk, "chunk lookup between both techniques does not match");
	//	var allocMetaChunk = GetContainingChunk<EntityMetadata>();
	//	__.GetLogger()._EzCheckedThrow<SimStormException>(this == allocMetaChunk.Span[slotRef.chunkSlotIndex].pageToken);
	//}

	/// <summary>
	///    allows sorting
	/// </summary>
	/// <param name="other"></param>
	/// <returns></returns>
	public int CompareTo(AccessToken other)
	{
		var toReturn = pageId.CompareTo(other.pageId);
		if (toReturn == 0)
		{
			return slotRef.CompareTo(other.slotRef);
		}

		return toReturn;
	}
	//	/// <summary>
	//	/// can be used to directly find a chunk from `Chunk[TComponent]._GLOBAL_LOOKUP(chunkId)`
	//	/// </summary>
	//	public long GetChunkLookupId()
	//	{
	//		//create a long from two ints
	//		//long correct = (long)left << 32 | (long)(uint)right;  //from: https://stackoverflow.com/a/33325313/1115220
	//#if CHECKED

	//		var chunkLookup = new ChunkLookupId { pageId = pageId, columnChunkIndex = slotRef.columnChunkIndex };
	//		var chunkLookupId = chunkLookup._packedValue;
	//		var toReturn = (long)pageId << 32 | (uint)slotRef.columnChunkIndex;
	//		//var toReturn = (long)slotRef.columnChunkIndex<< 32 | (uint)pageId;
	//		__.GetLogger()._EzCheckedThrow<SimStormException>(chunkLookupId == toReturn, "ints to long is wrong");
	//#endif


	//		//return (long)slotRef.columnChunkIndex << 32 | (uint)pageId;
	//		return (long)pageId << 32 | (uint)slotRef.columnChunkIndex;
	//	}
	/// <summary>
	///    obtain an ad-hoc reference to a component with write access.
	///    <para>BAD PERFORMANCE: you should prefer to use the Query() method instead</para>
	/// </summary>
	public ref TComponent GetComponentWriteRef<TComponent>()
	{
		GetOwner().WriteNotify<TComponent>();

		var (result, reason) = GetPage().CheckIsValid(this);
		__.GetLogger()._EzErrorThrow<SimStormException>(result, reason);

		//_CHECKED_VerifyInstance<T>();
		var chunk = GetContainingChunk<TComponent>();
		return ref chunk.GetWriteRef(this);
	}

	/// <summary>
	///    obtain an ad-hoc reference to a component with read access.
	///    <para>BAD PERFORMANCE: you should prefer to use the Query() method instead</para>
	/// </summary>
	public ref readonly TComponent GetComponentReadRef<TComponent>()
	{
		GetOwner().ReadNotify<TComponent>();

		var (result, reason) = GetPage().CheckIsValid(this);
		__.GetLogger()._EzErrorThrow<SimStormException>(result, reason);

		//_CHECKED_VerifyInstance<T>();
		var chunk = GetContainingChunk<TComponent>();
		return ref chunk.GetReadRef(this);
	}

	internal IPageOwner GetOwner()
	{
		return GetPage()._owner;
	}

	/// <summary>
	///    internal helper.   get the chunk this pageToken is mapped to for the given type
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	internal Chunk<T> GetContainingChunk<T>()
	{
		//_CHECKED_VerifyInstance<T>();
		//var chunkLookupId = GetChunkLookupId();

		//lock (Chunk<T>._GLOBAL_LOOKUP)
		//{
		//	if (!Chunk<T>._GLOBAL_LOOKUP.TryGetValue(chunkLookupId, out var chunk))
		//	{
		//		//if (!Chunk<T>._GLOBAL_LOOKUP.TryGetValue(chunkLookupId, out chunk))
		//		{
		//			__.GetLogger()._EzErrorThrow<SimStormException>(GetPage().HasComponentType<T>(), $"the page this element is attached to does not have a component of type {typeof(T).FullName}. Be aware that base classes do not match.");
		//			//need to refresh token
		//			__.GetLogger()._EzErrorThrow<SimStormException>(false, "the chunk this pageToken points to does not exist.  either entity was deleted or it was packed.  Do not use PageAccessTokens beyond the frame aquired unless page.page.AutoPack==false");
		//		}
		//	}
		//	return chunk;
		//}
		__.GetLogger()._EzCheckedThrow<SimStormException>(
			Chunk<T>._GLOBAL_LOOKUP.Length > pageId && Chunk<T>._GLOBAL_LOOKUP[pageId] != null,
			$"column does not exist.  are you sure the TComponent type `{typeof(T).Name}` is registered with the entities archetype?");
		var column = Chunk<T>._GLOBAL_LOOKUP[pageId];
		__.GetLogger()._EzErrorThrow<SimStormException>(column.Count > slotRef.chunkIndex, "chunk doesn't exist");
		var chunk = column._AsSpan_Unsafe()[slotRef.chunkIndex];
		__.GetLogger()._EzErrorThrow<SimStormException>(chunk != null);
		return chunk;
	}

	/// <summary>
	///    Get Write (exclusive) access to the chunk this entity component is in
	/// </summary>
	public Mem<TComponent> GetWriteMem<TComponent>()
	{
		GetOwner().WriteNotify<TComponent>();
		var chunk = GetContainingChunk<TComponent>();
		return Mem.Wrap(chunk.StorageSlice);
	}

	/// <summary>
	///    Get Read only (shared) access to the chunk this entity component is in
	/// </summary>
	public ReadMem<TComponent> GetReadMem<TComponent>()
	{
		GetOwner().ReadNotify<TComponent>();
		var chunk = GetContainingChunk<TComponent>();
		return ReadMem.Wrap(chunk.StorageSlice);
	}


	/// <summary>
	///    Get page this token is associated with.  If a null is returned or an exception is thchunkn, it is likely our
	///    PageAccessToken is out of date.
	/// </summary>
	public Page GetPage()
	{
		return Page._GLOBAL_LOOKUP.Span[pageId];

		//var toReturn = Page._GLOBAL_LOOKUP.Span[pageId];
		//__.GetLogger()._EzErrorThrow<SimStormException>(toReturn != null && toReturn._version == pageVersion, "alloc token seems to be expired");
		//__.GetLogger()._EzCheckedThrow<SimStormException>(toReturn._pageId == pageId);

		//return toReturn;
	}

	//public bool Equals(AccessToken other)
	//{
	//	throw new NotImplementedException();
	//}

	//public static bool operator ==(AccessToken left, AccessToken right)
	//{
	//	throw new NotImplementedException();
	//}

	//public static bool operator !=(AccessToken left, AccessToken right)
	//{
	//	throw new NotImplementedException();
	//}


	//public override string ToString()
	//{
	//	if (isInit)
	//	{
	//		return base.ToString();
	//	}
	//	else
	//	{
	//		return "PageAccessToken [NOT INITIALIZED]";
	//	}
	//}

	//public GetChunkTag<TTag>()
}

//shared component needs to partition chunks based on component value/hashcode.   easiest way is to require shared components to be object
//store as a per-page value, so partition by pages, not chunks
//api:  

/// <summary>
///    <para>Components are stored via a page --> column -> chunk -> slot</para>
///    <para>This tracks the chunkIndex and slotIndex</para>
///    on it's own, this is not enough to access an entities components, you need the alocator.   See
///    <see cref="AccessToken" />.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public record struct SlotRef : IComparable<SlotRef>
{
	[FieldOffset(0)] private long _packedValue;

	/// <summary>
	///    the location of the chunk in the column
	/// </summary>
	[FieldOffset(4)] public int chunkIndex;

	/// <summary>
	/// can be used to directly find a chunk from `Chunk[TComponent]._GLOBAL_LOOKUP(chunkId)`
	/// </summary>
	//[FieldOffset(0)]
	//public int chunkId;

	//[FieldOffset(0)]
	//public short pageId;
	/// <summary>
	///    the index to the slot from inside the chunk.
	/// </summary>
	[FieldOffset(0)] public int slotIndex;

	public SlotRef( //short pageId, 
		int chunkIndex, int slotIndex)
	{
		this = default;
		//this.pageId = pageId;
		this.chunkIndex = chunkIndex;
		this.slotIndex = slotIndex;
	}

	public int CompareTo(SlotRef other)
	{
		return _packedValue.CompareTo(other._packedValue);
	}

	public long GetChunkLookupId(int pageId)
	{
		//return (long)pageId << 32 | (uint)columnChunkIndex;
		return ((long)pageId << 32) | (uint)chunkIndex;
	}

	public static bool operator <(SlotRef left, SlotRef right)
	{
		return left.CompareTo(right) < 0;
	}

	public static bool operator <=(SlotRef left, SlotRef right)
	{
		return left.CompareTo(right) <= 0;
	}

	public static bool operator >(SlotRef left, SlotRef right)
	{
		return left.CompareTo(right) > 0;
	}

	public static bool operator >=(SlotRef left, SlotRef right)
	{
		return left.CompareTo(right) >= 0;
	}
}

public partial class Page //unit test
{
	public static async Task __TEST_Unit_ParallelPages(EntityRegistry entityRegistry, bool autoPack, int chunkSize,
		int entityCount, int pageCount, float batchSizeMultipler)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(entityRegistry.Count == 0);
		//Console.WriteLine("start parallel");
		var PARALLEL_LOOPS = pageCount;
		var execCount = 0;
		await ParallelFor.RangeAsync(0, PARALLEL_LOOPS, batchSizeMultipler, (start, endExclusive) =>
		{
			var tempCount = 0;
			for (var i = start; i < endExclusive; i++)
			{
				var page = _TEST_HELPER_CreateAndEditPage(entityRegistry, autoPack, chunkSize, entityCount);
				page.Dispose();
				tempCount++;
			}

			Interlocked.Add(ref execCount, tempCount);

			return ValueTask.CompletedTask;
		});
		__.GetLogger()._EzErrorThrow<SimStormException>(execCount == PARALLEL_LOOPS);

		__.GetLogger()._EzErrorThrow<SimStormException>(entityRegistry.Count == 0);
	}

	[Conditional("TEST")]
	public static void __TEST_Unit_SeriallPages(EntityRegistry entityRegistry, bool autoPack, int chunkSize,
		int entityCount, int pageCount)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(entityRegistry.Count == 0);
		var count = pageCount;
		using var allocOwner = SpanGuard<Page>.Allocate(count);
		var allocs = allocOwner.Span;
		for (var i = 0; i < count; i++)
		{
			allocs[i] = _TEST_HELPER_CreateAndEditPage(entityRegistry, autoPack, chunkSize, entityCount);
		}

		for (var i = 0; i < count; i++)
		{
			allocs[i].Dispose();
		}

		allocs.Clear();
		//var result = Parallel.For(0, 10000, (index) => __TEST_Unit_SinglePage());
		//__.GetLogger()._EzErrorThrow<SimStormException>(result.IsCompleted);

		__.GetLogger()._EzErrorThrow<SimStormException>(entityRegistry.Count == 0);
	}

	[Conditional("TEST")]
	public static void __TEST_Unit_SinglePage()
	{
		EntityRegistry entityRegistry = new();
		var page = _TEST_HELPER_CreatePage(entityRegistry);
		page.Dispose();
	}


	[Conditional("TEST")]
	public static void __TEST_Unit_SinglePage_AndEdit(EntityRegistry entityRegistry, bool autoPack, int chunkSize,
		int entityCount)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(entityRegistry.Count == 0);
		//Console.WriteLine("start single");
		var page = _TEST_HELPER_CreateAndEditPage(entityRegistry, autoPack, chunkSize, entityCount);

		page.Dispose();

		__.GetLogger()._EzErrorThrow<SimStormException>(entityRegistry.Count == 0);
	}

	private static Page _TEST_HELPER_CreatePage(EntityRegistry entityRegistry)
	{
		var page = new Page(__.Random._NextBoolean(), __.Random.Next(1, 100),
			new HashSet<Type> { typeof(int), typeof(string) }, null);
		//{
		//	AutoPack = __.Random._NextBoolean(),
		//	ChunkSize = __.Random.Next(1, 100),
		//	ComponentTypes = new() { typeof(int), typeof(string) },


		//};
		page.Initialize(null, entityRegistry);


		var entityCount = __.Random.Next(0, 1000);
		//using var entityHandlesOwner = SpanGuard<EntityHandle>.Allocate(__.Random.Next(0, 1000));
		//var set = new HashSet<long>();
		//var entityHandles = entityHandlesOwner.Span;
		//while (set.Count < entityHandles.Length)
		//{
		//	set.Add(__.Random.NextInt64());
		//}
		//var count = 0;
		//foreach (var id in set)
		//{
		//	entityHandles[count] = new EntityHandle(id);
		//	count++;
		//}
		//Span<long> entityHandles = stackalloc long[] { 2, 4, 8, 7, -2 };
		using var tokensOwner = SpanGuard<AccessToken>.Allocate(entityCount);
		var tokens = tokensOwner.Span;
		using var entitiesOwner = SpanGuard<EntityHandle>.Allocate(entityCount);
		var entities = entitiesOwner.Span;
		page.AllocEntityNew(tokens, entities);
		return page;
	}

	private static Page _TEST_HELPER_CreateAndEditPage(EntityRegistry entityRegistry, bool autoPack, int chunkSize,
		int entityCount)
	{
		//MemoryOwner<EntityHandle> entityHandlesOwner;
		//HashSet<EntityHandle> evenSet;
		//HashSet<EntityHandle> oddSet;


		var page = new Page(autoPack, chunkSize, new HashSet<Type> { typeof(int), typeof(Vector3), typeof(bool) }, null);
		//{
		//	AutoPack = autoPack,
		//	ChunkSize = chunkSize,
		//	ComponentTypes = new() { typeof(int), typeof(string) },


		//};
		page.Initialize(new _TEST_FakeOwner(), entityRegistry);

		//using var entityHandlesOwner = SpanPool<long>.Allocate(__.Random.Next(0, 1000));
		//var set = new HashSet<long>();
		//var entityHandles = entityHandlesOwner.Span;
		//while (set.Count < entityHandles.Length)
		//{
		//	set.Add(__.Random.NextInt64());
		//}

		//var count = 0;
		//foreach (var id in set)
		//{
		//	entityHandles[count] = id;
		//	count++;
		//}

		//var entityHandles = entityHandlesOwner.Span;

		//Span<long> entityHandles = stackalloc long[] { 2, 4, 8, 7, -2 };
		using var tokensOwner = SpanGuard<AccessToken>.Allocate(entityCount);
		var tokens = tokensOwner.Span;
		using var entitiesOwner = SpanGuard<EntityHandle>.Allocate(entityCount);
		var entities = entitiesOwner.Span;
		page.AllocEntityNew(tokens, entities);


		//test edits
		for (var i = 0; i < tokens.Length; i++)
		{
			var token = tokens[i];


			ref var num = ref token.GetComponentWriteRef<int>();
			__.GetLogger()._EzErrorThrow<SimStormException>(num == 0);
			num = i;
			var numRead = token.GetComponentReadRef<int>();
			__.GetLogger()._EzErrorThrow<SimStormException>(numRead == i);

			num = i + 1;
			numRead = token.GetComponentReadRef<int>();
			__.GetLogger()._EzErrorThrow<SimStormException>(numRead == i + 1);


			//ref var myStr = ref token.GetComponentWriteRef<string>();
			//__.GetLogger()._EzErrorThrow<SimStormException>(myStr == null);
			//myStr = $"hello {i}";
			//__.GetLogger()._EzErrorThrow<SimStormException>(token.GetComponentReadRef<string>() == myStr);


			ref var numExId = ref token.GetComponentWriteRef<int>();
			__.GetLogger()._EzErrorThrow<SimStormException>(num == numExId);
			numExId = token.entityHandle.id;
			__.GetLogger()._EzErrorThrow<SimStormException>(num == numExId);
		}

		var first = entities.Slice(0, entities.Length / 2);
		var second = entities.Slice(entities.Length / 2);
		//mark first group
		for (var i = 0; i < first.Length; i++)
		{
			ref var isFirst = ref tokens[i].GetComponentWriteRef<bool>();
			isFirst = true;
		}

		//delete second
		page.Free(second);


		//verify that first still here
		//{

		//	var i = 0;
		//	foreach (var (entityHandle, pageToken) in page._entityLookup)
		//	{
		//		if (pageToken.entityHandle.id % 2 != 0)
		//		{
		//			__.GetLogger()._EzErrorThrow<SimStormException>(false, "only even should exist");
		//		}

		//		var num = pageToken.GetComponentReadRef<int>();
		//		__.GetLogger()._EzErrorThrow<SimStormException>(num == (int)pageToken.entityHandle.id);
		//		__.GetLogger()._EzErrorThrow<SimStormException>(pageToken.GetComponentReadRef<string>().StartsWith("hello"));
		//		i++;

		//	}
		//}
		////same verify only evens,
		//foreach (var (entityHandle, pageToken) in page._entityLookup)
		//{
		//	ref var numExId = ref pageToken.GetComponentWriteRef<int>();
		//	__.GetLogger()._EzErrorThrow<SimStormException>(numExId % 2 == 0);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(pageToken.entityHandle.id % 2 == 0);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(pageToken.GetComponentReadRef<string>().StartsWith("hello"));
		//}
		__.GetLogger()._EzErrorThrow<SimStormException>(page.Count == first.Length);
		{
			var i = 0;
			foreach (var entityHandle in first)
			{
				var refreshedToken = page._entityLookup[entityHandle._packValue];
				var (result, reason) = page.CheckIsValid(refreshedToken);
				__.GetLogger()._EzErrorThrow<SimStormException>(result, reason);


				var num = refreshedToken.GetComponentReadRef<int>();
				__.GetLogger()._EzErrorThrow<SimStormException>(num == refreshedToken.entityHandle.id);
				//__.GetLogger()._EzErrorThrow<SimStormException>(refreshedToken.GetComponentReadRef<string>().StartsWith("hello"));


				var isFirst = refreshedToken.GetComponentReadRef<bool>();
				__.GetLogger()._EzErrorThrow<SimStormException>(isFirst);
				i++;
			}
		}


		//add second again
		using var secondAgainSO = SpanGuard<AccessToken>.Allocate(second.Length);
		//var oddSpan = oddTokens.Span;
		//page.Alloc(oddSet.ToArray(), oddSpan);
		page.AllocEntityNew(secondAgainSO.Span, second);
		__.GetLogger()._EzErrorThrow<SimStormException>(page.Count == second.Length + first.Length);


		//delete first

		page.Free(first);

		//verify only second
		foreach (var (entityHandle, pageToken) in page._entityLookup)
		{
			ref var numExId = ref pageToken.GetComponentWriteRef<int>();
			__.GetLogger()._EzErrorThrow<SimStormException>(numExId == 0, "we just wrote a new entity, old data should be blown away");
			//__.GetLogger()._EzErrorThrow<SimStormException>(pageToken.entityHandle.id % 2 == 1);
			//__.GetLogger()._EzErrorThrow<SimStormException>(pageToken.GetComponentReadRef<string>() == null);

			var isFirst = pageToken.GetComponentReadRef<bool>();
			__.GetLogger()._EzErrorThrow<SimStormException>(isFirst == false);
		}


		//delete second again
		page.Free(second);


		//verify empty
		__.GetLogger()._EzErrorThrow<SimStormException>(page._entityLookup.Count == 0);


		foreach (var column in page._columnStorage)
		{
			if (column == null)
			{
				//invalid atomid

				continue;
			}

			if (autoPack)
			{
				__.GetLogger()._EzErrorThrow<SimStormException>(column.Count == 1 && column[0]._count == 0,
					"columns should be empty except a single, empty chunk");
			}
			else
			{
				foreach (var chunk in column)
				{
					__.GetLogger()._EzErrorThrow<SimStormException>(chunk._count == 0, "autoPack==false chunks should all be empty");
				}
			}

			__.GetLogger()._EzErrorThrow<SimStormException>(column[0].pageId == page._pageId && column[0].pageVersion == page._version,
				"column doesn't match our page");
		}

		return page;
	}


	private class _TEST_FakeOwner : IPageOwner
	{
		public void ReadNotify<TComponent>()
		{
			//throw new NotImplementedException();
		}

		public void WriteNotify<TComponent>()
		{
			//throw new NotImplementedException();
		}
	}
}

public partial class Page //ATOM logic
{
	/// <summary>
	///    helper to convert a Type into an id.  maybe not useful, if internal to Page we want to switch back to a Type keyed
	///    dictionary again (for columns).
	///    right now we are using atomId keyed columns.
	/// </summary>
	protected internal static class Atom
	{
		private static Dictionary<Type, int> _typeLookup = new();

		private static Dictionary<int, Type> _atomIdLookup = new();

		public static int GetId<T>()
		{
			return AtomHelper<T>._atomId;
		}

		public static Type GetType(int atomId)
		{
			lock (_typeLookup)
			{
				return _atomIdLookup[atomId];
			}
		}

		public static int GetId(Type type)
		{
			lock (_typeLookup)
			{
				if (_typeLookup.TryGetValue(type, out var atomId))
				{
					return atomId;
				}
			}

			{
				//make atomId
				var helperType = typeof(AtomHelper<>).MakeGenericType(type);
				var helper = Activator.CreateInstance(helperType) as AtomHelper;
				var newAtomId = helper.GetId();
				lock (_typeLookup)
				{
					if (_typeLookup.TryGetValue(type, out var atomId))
					{
						__.GetLogger()._EzErrorThrow<SimStormException>(newAtomId == atomId);
						return atomId;
					}

					_typeLookup.Add(type, newAtomId);
					_atomIdLookup.Add(atomId, type);
					return newAtomId;
				}
			}
		}

		/**
		 * *
		 * * Tanner Gooding — Today at 3:44 PM
		 * I'd recommend looking at an ATOM based system
		 * 
		 * that is, your issue is you have a bunch of keys (the type) and using the hashcode in a dictionary is expensive for your scenario
		 * however, the number of types you need to support isn't likely most of them, its probably a small subset (like 1000-10k)
		 * 
		 * so you can have a system that offsets most of the dictionary cost to be "1 time" by mapping it to an incremental key (sometimes called an atom)
		 * that key can then be used for constant time indexing into an array
		 * 
		 * this is how a lot of Windows/Unix work internally for the xprocess windowing support
		 * almost every string is registered as a ushort ATOM, which is really just used as the index into a linear array
		 * and then everything else carries around the ATOM, not the string (or in your case the TYPE)
		 * its similar in concept to primary keys in a database, or similar
		 * 
		 * or maybe its not primary keys, I'm bad with databases; but there is a "key" like concept in databases that corresponds to integers rather than actual values
		 * 
		 * Zombie — Today at 3:48 PM
		 * In other words, you're basically turning a Type into an array index as early as you can, and you just pass that around
		 * So then your code doesn't need to query the dictionary nearly as much
		 * Tanner Gooding — Today at 3:49 PM
		 * right, which is similar to what GetHashCode does
		 * the difference being that GetHashCode is "random"
		 * while ATOM is explicitly incremental and "registered"
		 * 
		 * Zombie — Today at 3:49 PM
		 * You'd have an API like Atom RegisterComponent
		 * <T>
		 *    ()
		 *    And you'd pass around that Atom instead of T
		 *    or in addition to T
		 *    Tanner Gooding — Today at 3:50 PM
		 *    so rather than needing to do buckets and stuff based on arbitrary integers (what dictionaries do)
		 *    you can just do array[atom]
		 *    yeah, no need for Atom to be an allocation
		 *    its just a simple integer that is itself a dictionary lookup over the Type
		 *    and a small registration lock if an entry doesn't exist to handle multi-threading
		 *    so its a 1 time cost up front
		 *    and a basically zero cost for anything that already has the atom, which should be most things
		 *    *
		 */
		private abstract class AtomHelper
		{
			/// <summary>
			///    don't start at zero so that signifies an error
			/// </summary>
			protected static int _counter = 1;

			public abstract int GetId();
		}

		private class AtomHelper<T> : AtomHelper
		{
			public static int _atomId;

			static AtomHelper()
			{
				_atomId = Interlocked.Increment(ref _counter);
				lock (_typeLookup)
				{
					_typeLookup.Add(typeof(T), _atomId);
					_atomIdLookup.Add(_atomId, typeof(T));
				}
			}

			public override int GetId()
			{
				return _atomId;
			}
		}
	}
}

/// <summary>
///    allows integrating a Page (and thus, the allocation system) with external code
/// </summary>
public interface IPageOwner
{
	///// <summary>
	///// /A helper 
	///// </summary>
	///// <param name="pageSpan"></param>
	///// <param name="doneCallback"></param>
	//void DoDeleteEntities_Phase0(Span<AccessToken> pageSpan, Action_RoSpan<AccessToken, Archetype> doneCallback);

	/// <summary>
	///    inform that a read occured to a component type.   Used for AccessSentinal guards
	/// </summary>
	public void ReadNotify<TComponent>();

	/// <summary>
	///    inform that a write occured to a component type.   Used for AccessSentinal guards
	/// </summary>
	public void WriteNotify<TComponent>();
}

/// <summary>
///    page for archetypes
///    see ReadMe.md
/// </summary>
public partial class Page : IDisposable //init logic
{
	/// <summary>
	///    the archetype.  abstracted as <see cref="IPageOwner" /> for Seperation of Concerns (SoC)
	/// </summary>
	public IPageOwner _owner;

	public EntityRegistry _entityRegistry;

	//public static int _pageId_GlobalCounter;
	//public static Dictionary<int, Page> _GLOBAL_LOOKUP = new();
	//public int _pageId = _pageId_GlobalCounter._InterlockedIncrement();
	public static SlotList<Page> _GLOBAL_LOOKUP = new();

	/// <summary>
	///    this page instance can be looked up via the static Page._GLOBAL_LOOKUP[_pageId]
	/// </summary>
	public short _pageId = -1;

	private static int _fingerprintCounter;

	/// <summary>
	///    tracker so that every page has a different "fingerprint".  needed because pageId is reused when an page is disposed.
	/// </summary>
	public int _version = _fingerprintCounter++;

	/// <summary>
	///    if you want to add additional custom components to each entity, list them here.  These are not used to compute the
	///    <see cref="_componentsHashId" />
	///    <para>be sure not to remove the items already in the list.</para>
	/// </summary>
	public List<Type> CustomMetaComponentTypes = new() { typeof(EntityMetadata) };

	/// <summary>
	///    the Type of components this page manages.
	/// </summary>
	public HashSet<Type> ComponentTypes { get; init; }


	/// <summary>
	///    used to quickly identify what collection of ComponentTypes this page is in charge of
	/// </summary>
	public int _componentsHashId;


	//public Dictionary<Type, List<Chunk>> _componentColumns = new();
	/// <summary>
	///    ATOM_ID --> chunkId --> chunkId --> The_Component
	/// </summary>
	public List<List<Chunk>> _columnStorage = new();

	/// <summary>
	///    internal helper.   Get a Span containing all columns.
	/// </summary>
	/// <returns></returns>
	public Span<List<Chunk>> _GetColumnsSpan() { return _columnStorage._AsSpan_Unsafe(); }

	/// <summary>
	///    all the atomId's used in columns.  can use the atomId to get the offset to the proper column
	/// </summary>
	public List<int> _atomIdsUsed = new();

	/// <summary>
	///    How many Slots a Chunk should have. (how big the array is).
	/// </summary>
	public int ChunkSize { get; init; } = 1000;

	/// <summary>
	///    how many entities are stored in this Page
	/// </summary>
	public int Count => _entityLookup.Count;

	public SharedComponentGroup SharedComponents { get; init; }

	public Page(bool autoPack, int chunkSize, HashSet<Type> componentTypes, SharedComponentGroup partitionGroup)
	{
		AutoPack = autoPack;
		//_entityRegistry = entityRegistry;
		ChunkSize = chunkSize;
		ComponentTypes = componentTypes;
		SharedComponents = partitionGroup;
	}

	public void Initialize(IPageOwner owner, EntityRegistry entityRegistry)
	{
		_owner = owner;

		_entityRegistry = entityRegistry;
		//add self to global lookup
		__.GetLogger()._EzErrorThrow<SimStormException>(_pageId == -1, "why already set?");
		var tempId = _GLOBAL_LOOKUP.AllocSlot();
		__.GetLogger()._EzErrorThrow<SimStormException>(tempId < short.MaxValue,
			"pageId is set to be of size short.   this is too long");
		_pageId = (short)tempId;

		//__.AssertOnce(_pageId < 10, "//TODO: change pageId to use a pool, not increment, otherwise risk of collisions with long-running programs");
		__.GetLogger()._EzErrorThrow<SimStormException>(ComponentTypes != null, "need to set properties before init");
		//generate hash for fast matching of archetypes
		foreach (var type in ComponentTypes)
		{
			_componentsHashId += type.GetHashCode();
		}


		void _AllocColumnsHelper(Type type)
		{
			var atomId = Atom.GetId(type);
			__.GetLogger()._EzErrorThrow<SimStormException>(_atomIdsUsed.Contains(atomId) == false);
			_atomIdsUsed.Add(atomId);
			while (_columnStorage.Count() <= atomId)
			{
				_columnStorage.Add(null);
			}

			__.GetLogger()._EzErrorThrow<SimStormException>(_columnStorage[atomId] == null);
			_columnStorage[atomId] = new List<Chunk>();
		}


		//create columns
		foreach (var type in ComponentTypes)
		{
			//_componentColumns.Add(type, new());
			_AllocColumnsHelper(type);
		}

		//add our special metadata component column
		__.GetLogger()._EzErrorThrow<SimStormException>(CustomMetaComponentTypes.Contains(typeof(EntityMetadata)),
			"we must have entityMetadata to store info on each entity added");
		foreach (var type in CustomMetaComponentTypes)
		{
			//_componentColumns.Add(type, new());	
			_AllocColumnsHelper(type);
		}


		//create our next slot alloc tracker
		_nextSlotTracker = new AllocPositionTracker
		{
			chunkSize = ChunkSize,
			nextAvailable = new SlotRef(0, 0),
			page = this,
		};

		//create the first (blank) chunk for each column
		_AllocNextChunk();


		//lock (_GLOBAL_LOOKUP)
		//{
		//	_GLOBAL_LOOKUP.Add(_pageId, this);
		//}
		_GLOBAL_LOOKUP.Span[_pageId] = this;
	}

	public bool IsDisposed { get; private set; }
#if CHECKED
	private DisposeGuard _disposeCheck = new();
#endif
	public void Dispose()
	{
		if (IsDisposed)
		{
			__.GetLogger()._EzError(false, "why dispose twice?");
			return;
		}

		IsDisposed = true;
#if CHECKED
		_disposeCheck.Dispose();
#endif

		//lock (_GLOBAL_LOOKUP)
		//{
		//	_GLOBAL_LOOKUP.Remove(_pageId);
		//}

		var columns = _GetColumnsSpan();
		//foreach (var (type, columnList) in _componentColumns)
		//foreach (var columnList in _columns)
		foreach (var atomId in _atomIdsUsed)
		{
			var columnList = columns[atomId];
			foreach (var chunk in columnList)
			{
				chunk.Dispose();
			}

			columnList.Clear();
			columns[atomId] = null;
		}
#if CHECKED
		foreach (var columnList in columns)
		{
			__.GetLogger()._EzCheckedThrow<SimStormException>(columnList == null);
		}
#endif

		columns.Clear();
		_columnStorage = null;
		_free.Clear();
		_free = null;
		_entityLookup.Clear();
		_entityLookup = null;
		_entityRegistry = null;
		_GLOBAL_LOOKUP.FreeSlot(_pageId);
		__.GetLogger()._EzErrorThrow<SimStormException>(_GLOBAL_LOOKUP.Span.Length <= _pageId || _GLOBAL_LOOKUP.Span[_pageId] == null);
		_pageId = -1;
	}
#if DEBUG
	~Page()
	{
		if (IsDisposed == false)
		{
			__.AssertOnceIfNot(false, "need to have parent page dispose page for proper cleanup");
			Dispose();
		}
	}
#endif
}

public partial class Page //column / component type management
{
	public List<Chunk> GetColumn<T>()
	{
		var atomId = Atom.GetId<T>();
		return _GetColumnsSpan()[atomId];
	}

	public Chunk<T> GetChunk<T>(ref AccessToken pageToken)
	{
		var (result, reason) = CheckIsValid(ref pageToken);
		__.GetLogger()._EzErrorThrow<SimStormException>(result, reason);

		__CHECKED_INTERNAL_VerifyPageAccessToken(ref pageToken);
		var column = GetColumn<T>();
		return column[pageToken.slotRef.chunkIndex] as Chunk<T>;
	}

	public ref T GetComponentRef<T>(ref AccessToken pageToken)
	{
		var chunk = GetChunk<T>(ref pageToken);
		return ref chunk.UnsafeArray[pageToken.slotRef.slotIndex];
	}

	public static ref T GetComponent<T>(ref AccessToken pageToken)
	{
		return ref _GLOBAL_LOOKUP.Span[pageToken.pageId].GetComponentRef<T>(ref pageToken);
		//var atomId = Atom.GetId<T>();
		//var chunk = _GLOBAL_LOOKUP.Span[pageToken.pageId]._GetColumnsSpan()[atomId]._AsSpan_Unsafe()[pageToken.slotRef.columnChunkIndex] as Chunk<T>;
		//return ref chunk.Span[pageToken.slotRef.chunkChunkIndex];
	}


	/// <summary>
	///    will only return false if the slot is not present.  so check .IsAlive
	/// </summary>
	private bool __TryQueryMetadata(SlotRef slot, out EntityMetadata metadata)
	{
		//var columns = _GetColumnsSpan();
		//var atomId = Atom.GetId<EntityMetadata>();
		//var columnList = columns[atomId];//  _componentColumns[typeof(EntityMetadata)];
		var column = GetColumn<EntityMetadata>();
		if (column == null || column.Count < slot.chunkIndex)
		{
			metadata = default;
			return false;
		}

		var chunk = column[slot.chunkIndex] as Chunk<EntityMetadata>;
		metadata = chunk.UnsafeArray[slot.slotIndex];
		return true;
	}


	protected internal static ref T _UNCHECKED_GetComponent<T>(ref AccessToken pageToken)
	{
		var atomId = Atom.GetId<T>();
		var chunk =
			_GLOBAL_LOOKUP.Span[pageToken.pageId]._GetColumnsSpan()[atomId]._AsSpan_Unsafe()[pageToken.slotRef.chunkIndex]
				as Chunk<T>;
		return ref chunk.UnsafeArray[pageToken.slotRef.slotIndex];
	}

	/// <summary>
	///    INTERNAL USE ONLY.  doesn't do checks to ensure token is valid.
	///    returns false if the slot is not allocated.  if it's allocated but free, it still returns whatever data is
	///    contained.
	/// </summary>
	protected internal ref T _UNCHECKED_GetComponent<T>(ref SlotRef slot, out bool exists)
	{
		//var atomId = Atom.GetId<T>();
		var column = GetColumn<T>();
		if (column == null)
		{
			exists = false;
			return ref Unsafe.NullRef<T>();
		}

		var columnSpan = column._AsSpan_Unsafe();
		if (columnSpan.Length <= slot.chunkIndex || columnSpan[slot.chunkIndex] == null)
		{
			exists = false;
			return ref Unsafe.NullRef<T>();
		}

		var chunk = columnSpan[slot.chunkIndex] as Chunk<T>;
		if (chunk == null)
		{
			exists = false;
			return ref Unsafe.NullRef<T>();
		}

		exists = true;
		return ref chunk.UnsafeArray[slot.slotIndex];
	}

	/// <summary>
	///    INTERNAL USE ONLY.  doesn't do checks to ensure token is valid.
	/// </summary>
	protected internal ref T _UNCHECKED_GetComponent<T>(ref SlotRef slot)
	{
		//var atomId = Atom.GetId<T>();
		return ref (GetColumn<T>()._AsSpan_Unsafe()[slot.chunkIndex] as Chunk<T>).UnsafeArray[slot.slotIndex];
	}

	/// <summary>
	///    check if this Page is assigned the specified Type
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	public bool HasComponentType<T>()
	{
		//return _componentColumns.ContainsKey(typeof(T));
		var atomId = Atom.GetId<T>();
		var columns = _GetColumnsSpan();
		return columns.Length > atomId && columns[atomId] != null;
	}
}

public partial class Page //chunk management logic
{
	private void
		_AllocNextChunk() //TODO: preallocate extra chunks ahead of their need (always keep 1x extra chunk around)
	{
		void _AllocChunkHelper(Type type)
		{
			var columns = _GetColumnsSpan();
			var atomId = Atom.GetId(type);
			var chunkType = typeof(Chunk<>).MakeGenericType(type);
			var chunk = Activator.CreateInstance(chunkType) as Chunk;
			//chunk.Initialize(_nextSlotTracker.chunkSize, _nextSlotTracker.nextAvailable.GetChunkLookupId(_pageId));
			chunk.Initialize(_nextSlotTracker.chunkSize, this, _nextSlotTracker.nextAvailable.chunkIndex);

			__.GetLogger()._EzError(columns[atomId].Count == _nextSlotTracker.nextAvailable.chunkIndex,
				"somehow our column allocations is out of step with our next free tracking.");
			columns[atomId].Add(chunk);
		}

		foreach (var type in ComponentTypes)
		{
			_AllocChunkHelper(type);
		}


		foreach (var type in CustomMetaComponentTypes)
		{
			_AllocChunkHelper(type);
		}
	}

	private void _FreeLastChunk()
	{
		void _FreeChunkHelper(Type type)
		{
			var columns = _GetColumnsSpan();
			var atomId = Atom.GetId(type);
			var result = columns[atomId]._TryTakeLast(out var chunk);
			__.GetLogger()._EzErrorThrow<SimStormException>(result && chunk._count == 0);
			chunk.Dispose();
			__.GetLogger()._EzError(columns[atomId].Count - 1 == _nextSlotTracker.nextAvailable.chunkIndex,
				"somehow our column allocations is out of step with our next free tracking.");
		}

		foreach (var type in ComponentTypes)
		{
			_FreeChunkHelper(type);
		}

		foreach (var type in CustomMetaComponentTypes)
		{
			_FreeChunkHelper(type);
		}
	}
}

public partial class Page //alloc/free/pack logic
{
	/// <summary>
	///    entities registered with this page.
	///    <para>
	///       OBSOLETE: kind of expensive any maybe not so helpful?   can access all entities via entityRegistry, or all
	///       this page entitites by enumerating it's Column{EntityMeta}
	///    </para>
	/// </summary>
	/// <remarks>
	///    <para>
	///       given an entityHandle, find current token.  this allows decoupling our internal storage location from external
	///       callers, allowing packing.
	///    </para>
	/// </remarks>
	[Obsolete(
		"kind of expensive any maybe not so helpful?   can access all entities via entityRegistry, or all this page entitites by enumerating it's Column<EntityMeta>")]
	public Dictionary<ulong, AccessToken> _entityLookup = new();

	/// <summary>
	///    temp listing of free entities, we will pack and/or deallocate at a specific time each frame
	/// </summary>
	public List<SlotRef> _free = new();

	public bool _isFreeSorted = true;

	/// <summary>
	///    the next slot we will allocate from, and logic informing us when to add/remove chunks
	/// </summary>
	public AllocPositionTracker _nextSlotTracker;

	/// <summary>
	///    when we pack, we move entities around.  This is used to determine if a PageAccessToken is out of date.
	/// </summary>
	public uint _packVersion;


	/// <summary>
	///    default true, automatically pack when Free() is called.
	/// </summary>
	public bool AutoPack { get; init; } = true;


	/// <summary>
	///    using this Page, allocate the number of entities given the output length
	/// </summary>
	/// <param name="output"></param>
	public void AllocEntityNew(Span<AccessToken> outputAccessTokens)
	{
		using var entitiesOwner = SpanGuard<EntityHandle>.Allocate(outputAccessTokens.Length);
		var outputEntityHandles = entitiesOwner.Span;
		AllocEntityNew(outputAccessTokens, outputEntityHandles);
	}

	/// <summary>
	///    using this Page, allocate the number of entities given the output length
	/// </summary>
	/// <param name="output"></param>
	public void AllocEntityNew(Span<AccessToken> outputAccessTokens, Span<EntityHandle> outputEntityHandles)
	{
		__.GetLogger()._EzErrorThrow<SimStormException>(outputAccessTokens.Length == outputEntityHandles.Length);
		_entityRegistry.Alloc(outputEntityHandles);
		_AllocEntityNew_Helper(outputEntityHandles, outputAccessTokens);
	}

	/// <summary>
	///    given already allocated (but not live) entityHandles, alloc on this page.
	/// </summary>
	public void _AllocEntityNew_Helper(Span<EntityHandle> inputEntityHandles, Span<AccessToken> outputAccessTokens)
	{
		/// get a slot (recycling free if available)
		/// make pageToken
		/// add slot to columnList
		/// set builtin entityMetadata component
		/// verify
		if (_isFreeSorted != true)
		{
			_free.Sort();
			_isFreeSorted = true;
		}

		__.GetLogger()._EzError(outputAccessTokens.Length == inputEntityHandles.Length);


		var columns = _GetColumnsSpan();

		for (var i = 0; i < inputEntityHandles.Length; i++)
		{
			var entityHandle = inputEntityHandles[i];
			//get next free.
			if (!_free._TryTakeLast(out var slot))
			{
				//  if we need to allocate a chunk do so here also.  //TODO: multithread
				slot = _nextSlotTracker.AllocNext(out var newChunk);
				if (newChunk)
				{
					_AllocNextChunk();
				}
			}

			ref var pageToken = ref outputAccessTokens[i];
			pageToken = _GenerateLivePageAccessToken(entityHandle, slot);
			//new()  
			//{
			//	isInit = true,
			//	pageId = _pageId,
			//	slotRef = slot,
			//	entityHandle = entityHandle,
			//	packVersion = _packVersion,
			//};


			//loop all components zeroing out data and informing chunk of added item
			foreach (var atomId in _atomIdsUsed)
			{
				columns[atomId][slot.chunkIndex].OnAllocSlot(ref pageToken);
			}


			//set the entityMetadata builtin componenent
			ref var entityMetadata =
				ref pageToken.GetContainingChunk<EntityMetadata>().UnsafeArray[pageToken.slotRef.slotIndex];
			__.GetLogger()._EzCheckedThrow<SimStormException>(entityMetadata == default, "expect this to be cleared out, why not?");
			entityMetadata = new EntityMetadata { accessToken = pageToken, componentCount = _atomIdsUsed.Count, };


			//add to lookup
			_entityLookup.Add(entityHandle._packValue, pageToken);
			__.GetLogger()._EzCheckedThrow<SimStormException>(_entityRegistry[entityHandle].IsAlive == false,
				"already alive, why overwriting?");
			_entityRegistry[entityHandle].pageAccessToken = pageToken;


			__.GetLogger()._EzCheckedThrow<SimStormException>(entityMetadata == pageToken.GetComponentReadRef<EntityMetadata>(),
				"component reference verification failed.  why?");

#if CHECKED
			var (result, reason) = CheckIsValid(ref pageToken);
			__.GetLogger()._EzErrorThrow<SimStormException>(result, reason);
#endif
			__CHECKED_INTERNAL_VerifyPageAccessToken(ref pageToken);
		}
	}


	private AccessToken _GenerateLivePageAccessToken(EntityHandle entityHandle, SlotRef slot)
	{
		return new AccessToken
		{
			isAlive = true,
			pageId = _pageId,
			slotRef = slot,
			entityHandle = entityHandle,
			packVersion = _packVersion,
			pageVersion = _version,
		};
	}

	/// <summary>
	///    helper to determine if a token is valid, and if not, why.
	/// </summary>
	public (bool result, string reason) CheckIsValid(AccessToken pageToken)
	{
		unsafe
		{
			return CheckIsValid(ref *&pageToken);
		}
	}

	/// <summary>
	///    helper to determine if a token is valid, and if not, why.
	/// </summary>
	public (bool result, string reason) CheckIsValid(ref AccessToken pageToken)
	{
		var result = true;
		var reason = "OK";

		if (pageToken.isAlive == false)
		{
			reason = "token is not alive";
			result = false;
		}
		else if (pageToken.pageId != _pageId)
		{
			reason = "token not for this page";
			result = false;
		}
		else if (!_entityLookup.TryGetValue(pageToken.entityHandle._packValue, out var lookupToken))
		{
			reason = "token does not have a matching entityId.  was it removed?";
			result = false;
		}
		else if (lookupToken.packVersion != pageToken.packVersion)
		{
			reason =
				"wrong packVersion.  aquire a new PageAccessToken every update from Archetype.GetPageAccessToken() with AutoPack=true. But for best performance over many entities, use Archetype.Query()";
			result = false;
		}

		if (result)
		{
			__CHECKED_INTERNAL_VerifyPageAccessToken(ref pageToken);
		}

		return (result, reason);
	}


	/// <summary>
	///    verify engine state internally.  Use `CheckIsValid()` to verify user input
	/// </summary>
	/// <param name="pageToken"></param>
	[Conditional("CHECKED")]
	public void __CHECKED_INTERNAL_VerifyPageAccessToken(ref AccessToken pageToken)
	{
		//__.GetLogger()._EzErrorThrow<SimStormException>(_packVersion == pageToken.packVersion, "pageToken out of date.  a pack occured.  you need to reaquire the token every frame if AutoPack==true");
		var storedToken = _entityLookup[pageToken.entityHandle._packValue];
		__.GetLogger()._EzErrorThrow<SimStormException>(storedToken == pageToken);

		__.GetLogger()._EzErrorThrow<SimStormException>(_entityRegistry[pageToken.entityHandle].pageAccessToken == pageToken);


		var columns = _GetColumnsSpan();

		//make sure proper chunk is referenced, and field
		//foreach (var (type, columnList) in _componentColumns)
		foreach (var atomId in _atomIdsUsed)
		{
			var columnChunk = columns[atomId][pageToken.slotRef.chunkIndex];

			//__.GetLogger()._EzCheckedThrow<SimStormException>(columnChunk._chunkLookupId == pageToken.GetChunkLookupId(), "lookup id mismatch");
			__.GetLogger()._EzCheckedThrow<SimStormException>(
				columnChunk.pageId == pageToken.pageId && columnChunk.pageVersion == pageToken.pageVersion,
				"lookup id mismatch");
		}


		//verify chunk accessor workflows are correct
		//var manualGetChunk = _componentColumns[typeof(EntityMetadata)][pageToken.slotRef.columnChunkIndex] as Chunk<EntityMetadata>;
		//var manualGetChunk = GetChunk<EntityMetadata>(ref pageToken);
		var manualGetChunk =
			GetColumn<EntityMetadata>()._AsSpan_Unsafe()[pageToken.slotRef.chunkIndex] as Chunk<EntityMetadata>;

		var autoGetChunk = pageToken.GetContainingChunk<EntityMetadata>();
		__.GetLogger()._EzCheckedThrow<SimStormException>(manualGetChunk == autoGetChunk, "should match");

		//verify entityMetadatas match
		__.GetLogger()._EzErrorThrow<SimStormException>(
			manualGetChunk.UnsafeArray[pageToken.slotRef.slotIndex].accessToken == pageToken);


		//verify access thru Chunk<T> works also
		var chunkLookupChunk =
			Chunk<EntityMetadata>._GLOBAL_LOOKUP[pageToken.pageId]._AsSpan_Unsafe()[pageToken.slotRef.chunkIndex];
		__.GetLogger()._EzErrorThrow<SimStormException>(chunkLookupChunk == manualGetChunk);
	}


	/// <summary>
	///    free the entity handle, both here and in the entityRegistry
	/// </summary>
	public void Free(Span<EntityHandle> entityHandles)
	{
		//__.CHECKED.AssertOnce(entityHandles._IsSorted(), "should sort entityHandles before Free, for optimal performance");


		using var so_PageAccessTokens = SpanGuard<AccessToken>.Allocate(entityHandles.Length);
		var pageTokens = so_PageAccessTokens.Span;

		//get tokens for freeing
		for (var i = 0; i < entityHandles.Length; i++)
		{
			var entityHandle = entityHandles[i];
			pageTokens[i] = _entityRegistry[entityHandle].pageAccessToken;
		}

		//sort so that when we itterate through, they will have a higher chance of being in the same chunk
		pageTokens.Sort();

		Free(pageTokens);
	}

	public unsafe void Free(Span<AccessToken> tokens)
	{
		/// get the pageTokens to delete
		/// verify
		/// delete from allocations lookup
		/// free slot from columnList
		/// add to free list
		/// if AutoPack, do it now.
		/// 
		//__.CHECKED.AssertOnce(tokens._IsSorted(), "should sort entityHandles before Free, for optimal performance");
		if (tokens.Length == 0)
		{
			return;
		}

		//get tokens for freeing
		for (var i = 0; i < tokens.Length; i++)
		{
			var entityHandle = tokens[i].entityHandle;

#if DEBUG
			var (result, reason) = CheckIsValid(ref tokens[i]);
			__.GetLogger()._EzErrorThrow<SimStormException>(result, reason);
#endif

			//remove them now??  maybe will cause further issues with verification
			_entityLookup.Remove(entityHandle._packValue);
		}

		//remove from external registry
		_entityRegistry.Free(tokens);


		//parallel through all columns, deleting


		fixed (AccessToken* p = tokens)
		{
			var pTokens = p;
			var count = tokens.Length; // pageTokensArraySegment.Count;

			//var allocArray = pageTokensArraySegment.Array;
			////var workCount = 0;
			ParallelFor.Range(0, _atomIdsUsed.Count, (start, end) =>
			{
				//Span<AccessToken> span = allocArray;
				//var span = new Span<AccessToken>(pTokens, count);
				for (var aIndex = start; aIndex < end; aIndex++)
				{
					//Interlocked.Increment(ref workCount);
					var atomId = _atomIdsUsed[aIndex];

					var columns = _GetColumnsSpan();
					var columnList = columns[atomId];
					//var (type, columnList) = pair;
					for (var i = 0; i < count; i++)
					{
						ref var pageToken = ref pTokens[i];
						columnList[pageToken.slotRef.chunkIndex].OnFreeSlot(ref pageToken);
					}
				}
			});
		}


		//var pageTokensArraySegment = so_PageAccessTokens.DangerousGetArray();


		////__.GetLogger()._EzErrorThrow<SimStormException>(workCount==_atomIdsUsed.Count,"parallel is missing work"); 

		//Parallel.ForEach(_atomIdsUsed, (atomId, loopState) =>
		////foreach(var atomId in _atomIdsUsed)
		//{

		//	var columns = _GetColumnsSpan();
		//	var columnList = columns[atomId];
		//	//var (type, columnList) = pair;
		//	for (var i = 0; i < pageTokensArraySegment.Count; i++)
		//	{
		//		ref var pageToken = ref allocArray[i];
		//		columnList[pageToken.slotRef.chunkIndex].OnFreeSlot(ref pageToken);
		//	}
		//}
		//);


		//add to free list
		for (var i = 0; i < tokens.Length; i++)
		{
			__.GetLogger()._EzCheckedThrow<SimStormException>(_free.Contains(tokens[i].slotRef) == false);
			_free.Add(tokens[i].slotRef);
		}

		_isFreeSorted = false;


		if (AutoPack)
		{
			var priorPackVersion = _packVersion;
			__.GetLogger()._EzError(tokens.Length == _free.Count);
			Pack(tokens.Length);
			__.GetLogger()._EzError(priorPackVersion != _packVersion && _free.Count == 0, "autopack not working?");
		}
	}


	private void _PackHelper_MoveSlotToFree(AccessToken highestAlive, SlotRef lowestFree)
	{
		//verify freeSlot is free, and pageToken is valid
#if CHECKED
		__CHECKED_INTERNAL_VerifyPageAccessToken(ref highestAlive);
		__.GetLogger()._EzError(highestAlive.slotRef > lowestFree);
		if (!__TryQueryMetadata(lowestFree, out var freeSlotMeta))
		{
			__.GetLogger()._EzCheckedThrow<SimStormException>(false, "this should not happen.  returning false means no chunk exists.");
		}
		__.GetLogger()._EzError(freeSlotMeta.IsAlive == false && freeSlotMeta.accessToken.isAlive == false, "should be default value");
#endif
		//generate our newPos pageToken
		var newSlotPageAccessToken = _GenerateLivePageAccessToken(highestAlive.entityHandle, lowestFree);

		//do a single alloc for that freeSlot componentColumns
		//foreach (var (type, columnList) in _componentColumns)

		var columns = _GetColumnsSpan();
		foreach (var columnList in columns)
		{
			if (columnList == null)
			{
				//not our atom
				continue;
			}

			//copy data from old while allocting the slot
			columnList[lowestFree.chunkIndex].OnPackSlot(ref newSlotPageAccessToken, ref highestAlive);
			//deallocate old slot componentColumns
			columnList[highestAlive.slotRef.chunkIndex].OnFreeSlot(ref highestAlive);
		}

		//update the metadata component with the new token (the other fields of metadata were coppied along with all other components in the above loop)
		//var metadataChunk = _componentColumns[typeof(EntityMetadata)][newSlotPageAccessToken.slotRef.columnChunkIndex] as Chunk<EntityMetadata>;
		//var metadataChunk = GetChunk<EntityMetadata>(ref newSlotPageAccessToken);
		//ref var metadataComponent = ref metadataChunk.Span[newSlotPageAccessToken.slotRef.chunkChunkIndex];
		ref var metadataComponent = ref _UNCHECKED_GetComponent<EntityMetadata>(ref newSlotPageAccessToken);
		metadataComponent.accessToken = newSlotPageAccessToken;


		//update our _lookup and registry
		_entityLookup[newSlotPageAccessToken.entityHandle._packValue] = newSlotPageAccessToken;
		_entityRegistry[newSlotPageAccessToken.entityHandle].pageAccessToken = newSlotPageAccessToken;

		//make sure our newly moved is all setup properly
		__CHECKED_INTERNAL_VerifyPageAccessToken(ref newSlotPageAccessToken);
	}

	/// <summary>
	///    pack the page, so that there are no gaps between live entities.  The earliest free slots are filled by moving the
	///    ending entities up to fill it.
	///    Chunks that become empty will be recycled.
	/// </summary>
	/// <param name="maxCount"></param>
	/// <returns></returns>
	public bool Pack(int maxCount)
	{
		//sor frees
		//loop through all to free, lowest to highest
		//if free is higher than highest active slot, done.
		//take highest active slotRef and swap it with the free


		///<summary>helper function to walk from our highest slot downward (deallocating any free) until it hits a live slot.</summary>
		void _TryFreeAndGetNextAlive(out SlotRef highestAllocatedSlot, out EntityMetadata highestAliveToken)
		{
			highestAliveToken = default;
			while (true)
			{
				var result = _nextSlotTracker.TryGetHighestAllocatedSlot(out highestAllocatedSlot);
				if (result == false)
				{
					//  we are done
					__.GetLogger()._EzError(_entityLookup.Count == 0,
						"no more slots available.  we expect this to happen if the Page is totally empty.");
					break;
				}

				result = __TryQueryMetadata(highestAllocatedSlot, out highestAliveToken);
				if (result == false)
				{
					__.GetLogger()._EzError(false,
						"why are we not getting a slot?   our nextSlotTracker thinks there should be this allocated.  investigate");
				}

				if (highestAliveToken.IsAlive)
				{
					//we have a live slot!
					break;
				}
#if CHECKED
					//verify The highest slot is free.  
					var foundMeta = _UNCHECKED_GetComponent<EntityMetadata>(ref highestAllocatedSlot);
					__.GetLogger()._EzErrorThrow<SimStormException>(foundMeta.IsAlive == false);

#endif

				//decrement our allocations because the top slot is not alive
				_nextSlotTracker.FreeLast(out var shouldFreeChunk);
#if CHECKED
					//verify next free is actually free
					result = __TryQueryMetadata(_nextSlotTracker.nextAvailable, out var shouldBeFreeMetadata);
					__.GetLogger()._EzErrorThrow<SimStormException>(result && shouldBeFreeMetadata.IsAlive == false && shouldBeFreeMetadata.accessToken.isAlive == false);
#endif
				if (shouldFreeChunk)
				{
					//okay to instantly free our chunk as soon as it's empty because we are using a memory pool tp aid recycling
					_FreeLastChunk();
				}
			}
		}


		if (_free.Count == 0)
		{
			return false;
		}

		_packVersion++;


		var count = Math.Min(maxCount, _free.Count);

		//sort our free list so that the ones closest to chunkIndex zero will be swapped out with higher live ones
		if (_isFreeSorted != true)
		{
			_free.Sort();
			_isFreeSorted = true;
		}

		//loop through our free items
		for (var i = 0; i < count; i++)
		{
			var firstFreeSlotToFill = _free[i];

			//find the highestAliveToken, reducing our allocated until that occurs
			SlotRef highestAllocatedSlot;
			EntityMetadata highestAliveToken;
			_TryFreeAndGetNextAlive(out highestAllocatedSlot, out highestAliveToken);
			if (firstFreeSlotToFill >= highestAllocatedSlot)
			{
				//our first free slot is higher up than our highest allocated.   Because the _free list is sorted, everything else to free is already in unallocated territory.
				//this happens because during the pack, when our `_TryFreeAndGetNextAlive` finds a dead slot on top it will deallocate it.
				//the above while loop already deallocated to before our current free slot.  so we are done				
				break;
			}

			//if we get here, we have a live `highestAliveToken` and a `firstFreeSlotToFill` that is below it.   lets swap
			{
				//swap out free and highest
				__.GetLogger()._EzCheckedThrow<SimStormException>(highestAliveToken.IsAlive);
				__CHECKED_INTERNAL_VerifyPageAccessToken(ref highestAliveToken.accessToken);
				_PackHelper_MoveSlotToFree(highestAliveToken.accessToken, firstFreeSlotToFill);
			}


			//////			if (!_nextSlotTracker.TryGetHighestAllocatedSlot(out var highestAllocatedSlot))
			//////			{
			//////				__.GetLogger()._EzError(false, "investigate?  no slots are filled?  probably okay, just clear our slots?");
			//////				break;
			//////			}
			//////			if (firstFreeSlotToFill > highestAllocatedSlot)
			//////			{
			//////				__.GetLogger()._EzError(false, "investigate?  our free are higher than our filled?  probably okay, just clear our slots?");
			//////				break;
			//////			}


			//////			var highSlotResult = __TryQueryMetadata(highestAllocatedSlot, out var highestAliveToken);
			//////			if (highSlotResult == false || highestAliveToken.IsAlive == false)
			//////			{
			//////				//__.GetLogger()._EzErrorThrow<SimStormException>(false, "if our highestAllocatedSlot is not alive, it should have been sorted ");
			//////				var foundMeta = _UNCHECKED_GetComponent<EntityMetadata>(ref highestAllocatedSlot);
			//////				__.GetLogger()._EzErrorThrow<SimStormException>(foundMeta.IsAlive == false);
			//////				//The highest slot is free.  
			//////			}
			//////			else
			//////			{
			//////				//swap out free and highest
			//////				__.GetLogger()._EzCheckedThrow<SimStormException>(highestAliveToken.IsAlive == true);
			//////				__CHECKED_VerifyPageAccessToken(ref highestAliveToken.pageToken);
			//////				_PackHelper_MoveSlotToFree(highestAliveToken.pageToken, firstFreeSlotToFill);
			//////			}


			//////			//decrement our slotTracker position now that we have moved our top item (or if top was already free)
			//////			_nextSlotTracker.FreeLast(out var shouldFreeChunk);
			//////#if CHECKED
			//////			//verify next free is actually free
			//////			var result = __TryQueryMetadata(_nextSlotTracker.nextAvailable, out var shouldBeFreeMetadata);
			//////			__.GetLogger()._EzErrorThrow<SimStormException>(result && shouldBeFreeMetadata.IsAlive ==false && shouldBeFreeMetadata.pageToken.isAlive == false);
			//////#endif
			//////			if (shouldFreeChunk)
			//////			{
			//////				_FreeLastChunk();
			//////			}
		}

		{
			//finally, remove any more dead slots on top.  This can occur if the last `_free` items iterated (above) were swaps.
			_TryFreeAndGetNextAlive(out var highestAllocatedSlot, out var highestAliveToken);
		}
#if CHECKED
		//verify that our highest allocated is either free or init
		if (_entityLookup.Count > 0)
		{
			var result = _nextSlotTracker.TryGetHighestAllocatedSlot(out var highestAllocated);
			__.GetLogger()._EzErrorThrow<SimStormException>(result);
			result = __TryQueryMetadata(highestAllocated, out var highestMetadata);
			__.GetLogger()._EzCheckedThrow<SimStormException>(highestMetadata.IsAlive || _free.Contains(highestAllocated));
		}
#endif
		//remove these free slots now that we are done filling them
		_free.RemoveRange(0, count);

		return true;
	}
}

/// <summary>
///    data for a default chunk always applied to all pages.
/// </summary>
public record struct EntityMetadata
{
	public AccessToken accessToken;

	/// <summary>
	///    how many components are associated with this entity
	/// </summary>
	public int componentCount;

	/// <summary>
	///    hint informing that a writeRef was aquired for one of the components.
	///    <para>
	///       Important Note: writing to this fieldWrites is done internally, and does not increment the
	///       Chunk[EntityMetadata]._writeVersion.  This is so _writeVersion can be used to detect entity alloc/free
	///    </para>
	/// </summary>
	public int fieldWrites;


	public EntityHandle Entity => accessToken.entityHandle;

	/// <summary>
	///    If this slot is in use by an entity
	/// </summary>
	public bool IsAlive => accessToken.isAlive;

	/// <summary>
	///    obtain the SharedComponents, common for this page
	/// </summary>
	public SharedComponentGroup SharedComponents => accessToken.GetPage().SharedComponents;


	/// <summary>
	///    Obtain write access to the specified TComponent chunk that this entity is part of.
	/// </summary>
	public Mem<TComponent> GetWriteMem<TComponent>()
	{
		return accessToken.GetWriteMem<TComponent>();
	}

	/// <summary>
	///    Obtain read-only access to the specified TComponent chunk that this entity is part of.
	/// </summary>
	public ReadMem<TComponent> GetReadMem<TComponent>()
	{
		return accessToken.GetReadMem<TComponent>();
	}

	public override string ToString()
	{
		var live = IsAlive ? "ALIVE" : "DEAD";
		return $"{Entity}.{live}";
	}
}

/// <summary>
///    a helper struct that tracks and computes the next slot available for ALLOCATION.  Note that freed slots are still
///    considered allocated.
///    when getting a new slot just check the value of `newChunk` to determine if a new chunk needs to be allocated.
/// </summary>
public struct AllocPositionTracker
{
	public int chunkSize;
	public SlotRef nextAvailable;
	public Page page;

	/// <summary>
	///    allocate a new slot for the Page object.   this slot is identical for all components in this page.  (together, it
	///    makes up a "chunk")
	/// </summary>
	/// <param name="newChunk"></param>
	/// <returns></returns>
	public SlotRef AllocNext(out bool newChunk)
	{
		var toReturn = nextAvailable;
		nextAvailable.slotIndex++;
		if (nextAvailable.slotIndex >= chunkSize)
		{
			nextAvailable.slotIndex = 0;
			nextAvailable.chunkIndex++;
			newChunk = true;
		}
		else
		{
			newChunk = false;
		}

		__.GetLogger()._EzCheckedThrow<SimStormException>(nextAvailable != toReturn, "make sure these are still structs?");
		return toReturn;
	}

	/// <summary>
	///    Free the last slot  (highest chunk+chunk index)
	/// </summary>
	/// <param name="freeChunk"></param>
	public void FreeLast(out bool freeChunk)
	{
		//if(TryGetPriorSlot(nextAvailable,out var prior))
		//{
		//	if (nextAvailable.columnChunkIndex != prior.columnChunkIndex)
		//	{
		//		freeChunk = true;
		//	}
		//	else
		//	{
		//		freeChunk = false;
		//	}
		//	nextAvailable = prior;
		//}

#if CHECKED
		//ensure that slot we are going to free is actually free
		var result = TryGetHighestAllocatedSlot(out var slotToCheck);
		__.GetLogger()._EzErrorThrow<SimStormException>(result, "shouldnt free if nothing left allocated");
		ref var checkMeta = ref page._UNCHECKED_GetComponent<EntityMetadata>(ref slotToCheck);
		__.GetLogger()._EzErrorThrow<SimStormException>(checkMeta.IsAlive == false, "should be dead if we are about to free it");
#endif


		nextAvailable.slotIndex--;
		if (nextAvailable.slotIndex < 0)
		{
			nextAvailable.slotIndex = chunkSize - 1;
			nextAvailable.chunkIndex--;
			freeChunk = true;
			__.GetLogger()._EzErrorThrow<SimStormException>(nextAvailable.chunkIndex >= 0, "less than zero allocations");
		}
		else
		{
			freeChunk = false;
		}
	}

	private bool _TryGetPriorSlot(SlotRef current, out SlotRef prior)
	{
		if (nextAvailable.chunkIndex == 0 && nextAvailable.slotIndex == 0)
		{
			prior = default;
			return false;
		}

		prior = current;
		prior.slotIndex--;
		if (prior.slotIndex < 0)
		{
			prior.slotIndex = chunkSize - 1;
			prior.chunkIndex--;
		}

		return true;
	}

	/// <summary>
	///    For our Page, get the highest slot currently allocated.
	///    Note: while the slot may be allocated, it may also be freed.
	///    <para>returns false if no slots are allocated</para>
	/// </summary>
	/// <param name="slot"></param>
	/// <returns></returns>
	internal bool TryGetHighestAllocatedSlot(out SlotRef slot)
	{
		return _TryGetPriorSlot(nextAvailable, out slot);
	}
}

/// <summary>
///    use the Generic version of this class.
/// </summary>
public abstract class Chunk : IDisposable
{
	/// <summary>
	///    how many entities are currently in this chunk
	/// </summary>
	public int _count;

	/// <summary>
	///    if true, obtaining components via StorageSlice will provide a contiguous sequence of all entity components in this
	///    chunk.
	///    If false, the user needs to manually detect
	/// </summary>
	public bool _isAutoPack;

	/// <summary>
	///    how many entity row slots our chunk has.
	/// </summary>
	public int _length = -1;

	/// <summary>
	///    incremented every time a system writes to any of its slots.
	/// </summary>
	public int _writeVersion;

	/// <summary>
	///    the offset down the column this chunk is in (when referencing <see cref="Chunk{TComponent}._GLOBAL_LOOKUP" />)
	/// </summary>
	public int chunkIndex = -1;
	//public long _chunkLookupId = -1;

	/// <summary>
	///    The ID of the Page this Chunk is associated with
	/// </summary>
	public int pageId = -1;

	/// <summary>
	///    Page version counter, if this does not match the current page at slot pageId, we have a mismatched reference. (stale
	///    ref/disposed page)
	/// </summary>
	public int pageVersion = -1;


	/// <summary>
	///    delete from global chunk store
	/// </summary>
	public abstract void Dispose();

	/// <summary>
	///    using the given _chunkId, allocate self a slot on the global chunk store for Chunk[T]
	/// </summary>
	public abstract void Initialize(int length, Page page, int chunkIndex);

	internal abstract void OnAllocSlot(ref AccessToken pageToken);

	/// <summary>
	///    overload used internally for packing
	/// </summary>
	internal abstract void OnPackSlot(ref AccessToken pageToken, ref AccessToken moveComponentDataFrom);

	internal abstract void OnFreeSlot(ref AccessToken pageToken);
}

[StructLayout(LayoutKind.Explicit)]
public record struct ChunkLookupId
{
	[FieldOffset(0)] public long _packedValue;

	[FieldOffset(0)] public int columnChunkIndex;

	[FieldOffset(4)] public int pageId;
}

/// <summary>
///    contains the slots (storage array) used to contain the components.
/// </summary>
public class Chunk<TComponent> : Chunk
{
	//public static Dictionary<long, Chunk<TComponent>> _GLOBAL_LOOKUP = new(0);


	/// <summary>
	///    find by pageId --> chunkIndex --> slotIndex
	/// </summary>
	public static ResizableArray<List<Chunk<TComponent>>> _GLOBAL_LOOKUP = new();


	public Mem<TComponent> _storageRaw;

	/// <summary>
	///    If the Page is set to AutoPack (default true for NotNot Engine) this will provide a contiguous slice of allocated
	///    entity components.
	///    This is useful so you don't have to check if the slot is alive, and don't have gaps in live data.
	///    If AutoPack is false, this will return the underlying storage array.
	/// </summary>
	public Mem<TComponent> StorageSlice
	{
		get
		{
			if (_isAutoPack)
			{
				return _storageRaw.Slice(0, _count);
			}

			return _storageRaw;
		}
	}

	//public ArraySegment<TComponent> Span
	//{
	//	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	//	get => _storage.DangerousGetArray();
	//}

	/// <summary>
	///    this is an array obtained by a object pool (cache).  It is longer than actually needed.  Do not use the extra slots.
	///    always get length from _storage or Span
	/// </summary>
	public TComponent[] UnsafeArray;

#if CHECKED
	private DisposeGuard _disposeCheck = new();
#endif
	public bool IsDisposed { get; private set; }

	public override void Dispose()
	{
		if (IsDisposed)
		{
			__.GetLogger()._EzErrorThrow<SimStormException>(false, "why already disposed");
			return;
		}

		IsDisposed = true;

#if CHECKED
		_disposeCheck.Dispose();
#endif
		//lock (_GLOBAL_LOOKUP)
		//{
		//	var result = _GLOBAL_LOOKUP._TryRemove(_chunkLookupId, out _);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(result);
		//}
		__.GetLogger()._EzCheckedThrow<SimStormException>(_GLOBAL_LOOKUP[pageId][chunkIndex] == this, "ref mismatch");
		_GLOBAL_LOOKUP[pageId][chunkIndex] = null;

		pageId = -1;
		pageVersion = -1;
		chunkIndex = -1;


		//our page.FreeLastChunk code checks count.   we don't want to check count here because of cases like game shutdown
		//__.GetLogger()._EzErrorThrow<SimStormException>(_count == 0); 
		UnsafeArray = null;
		_storageRaw.Dispose();
		_storageRaw = default;
	}

	public override void Initialize(int length, Page page, int chunkIndex)
	{
		//	_chunkLookupId = chunkLookupId;
		__.GetLogger()._EzErrorThrow<SimStormException>(pageId == -1, "already init");
		pageId = page._pageId;
		pageVersion = page._version;
		this.chunkIndex = chunkIndex;
		_isAutoPack = page.AutoPack;


		_length = length;

		////__.GetLogger()._EzErrorThrow<SimStormException>(_chunkLookupId != -1 && _length != -1, "need to set before init");
		//lock (_GLOBAL_LOOKUP)
		//{
		//	var result = _GLOBAL_LOOKUP.TryAdd(_chunkLookupId, this);
		//	__.GetLogger()._EzErrorThrow<SimStormException>(result);
		//}
		var column = _GLOBAL_LOOKUP.GetOrSet(pageId, () => new List<Chunk<TComponent>>());
		__.GetLogger()._EzCheckedThrow<SimStormException>(column.Count <= chunkIndex || column[chunkIndex] == null);
		column._ExpandAndSet(chunkIndex, this);

		//_storage = MemoryOwner<TComponent>.Allocate(_length, AllocationMode.Clear); //TODO: maybe no need to clear?
		_storageRaw = Mem<TComponent>.Allocate(_length);

		UnsafeArray = _storageRaw.DangerousGetArray().Array!;
	}

	internal override void OnAllocSlot(ref AccessToken pageToken)
	{
		_count++;
#if DEBUG
		//clear the slot
		UnsafeArray[pageToken.slotRef.slotIndex] = default;
#endif
	}

	/// <summary>
	///    overload used internally for packing
	/// </summary>
	internal override void OnPackSlot(ref AccessToken pageToken, ref AccessToken moveComponentDataFrom)
	{
		_count++;
		UnsafeArray[pageToken.slotRef.slotIndex] = moveComponentDataFrom.GetComponentReadRef<TComponent>();
#if DEBUG
		//lock (_GLOBAL_LOOKUP)
		{
			//clear the old slot.  this isn't needed, but in case someone is still using the old ref, lets make them aware of it in DEBUG
			//var chunkLookupId = moveComponentDataFrom.GetChunkLookupId();
			//var result = _GLOBAL_LOOKUP.TryGetValue(chunkLookupId, out var chunk);
			//__.GetLogger()._EzErrorThrow<SimStormException>(result);

			var chunk =
				_GLOBAL_LOOKUP[moveComponentDataFrom.pageId]._AsSpan_Unsafe()[moveComponentDataFrom.slotRef.chunkIndex];

			chunk.UnsafeArray[moveComponentDataFrom.slotRef.slotIndex] = default;
		}
#endif
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
	internal override void OnFreeSlot(ref AccessToken pageToken)
	{
		_count--;

		//if (pageToken.GetPage().AutoPack)
		//{
		//	//no need to clear, as we will pack over this!
		//	return;
		//}

		//clear the slot
		UnsafeArray[pageToken.slotRef.slotIndex] = default;
	}

	public unsafe ref TComponent GetWriteRef(AccessToken pageToken)
	{
		return ref GetWriteRef(ref *&pageToken); //cast to ptr using *& to circumvent return ref safety check
	}

	public ref TComponent GetWriteRef(ref AccessToken pageToken)
	{
		//lock (Chunk<EntityMetadata>._GLOBAL_LOOKUP)
		{
			_CHECKED_VerifyIntegrity(ref pageToken);
			var chunkIndex = pageToken.slotRef.slotIndex;
			//inform metadata that a write is occuring.  //TODO: is this needed?  If not, remove it to reduce random memory access
			//var result = Chunk<EntityMetadata>._GLOBAL_LOOKUP.TryGetValue(_chunkLookupId, out var chunk);
			//__.GetLogger()._EzErrorThrow<SimStormException>(result);
			var entityMetadataChunk =
				Chunk<EntityMetadata>._GLOBAL_LOOKUP[pageToken.pageId]._AsSpan_Unsafe()[pageToken.slotRef.chunkIndex];
			ref var entityMetadata = ref entityMetadataChunk.UnsafeArray[chunkIndex];
			entityMetadata.fieldWrites++;

			_writeVersion++;
			return ref UnsafeArray[chunkIndex];
		}
	}

	public unsafe ref readonly TComponent GetReadRef(AccessToken pageToken)
	{
		return ref GetReadRef(ref *&pageToken); //cast to ptr using *& to circumvent return ref safety check
	}

	public ref readonly TComponent GetReadRef(ref AccessToken pageToken)
	{
		_CHECKED_VerifyIntegrity(ref pageToken);
		var chunkIndex = pageToken.slotRef.slotIndex;
		return ref UnsafeArray[chunkIndex];
	}

	[Conditional("CHECKED")]
	private void _CHECKED_VerifyIntegrity(ref AccessToken pageToken)
	{
		//lock (_GLOBAL_LOOKUP)
		{
			pageToken.GetPage().__CHECKED_INTERNAL_VerifyPageAccessToken(ref pageToken);
			//__.GetLogger()._EzErrorThrow<SimStormException>(pageToken.GetChunkLookupId() == _chunkLookupId, "pageToken does not belong to this chunk");
			__.GetLogger()._EzErrorThrow<SimStormException>(
				pageToken.pageId == pageId && pageToken.pageVersion == pageVersion &&
				pageToken.slotRef.chunkIndex == this.chunkIndex, "pageToken does not belong to this chunk");

			//var result = Chunk<TComponent>._GLOBAL_LOOKUP.TryGetValue(_chunkLookupId, out var chunk);
			//__.GetLogger()._EzErrorThrow<SimStormException>(result);
			var chunk = _GLOBAL_LOOKUP[pageToken.pageId]._AsSpan_Unsafe()[pageToken.slotRef.chunkIndex];
			__.GetLogger()._EzCheckedThrow<SimStormException>(chunk == this, "alloc system internal integrity failure");
			__.GetLogger()._EzCheckedThrow<SimStormException>(!IsDisposed, "use after dispose");

			var chunkIndex = pageToken.slotRef.slotIndex;
			//result = Chunk<EntityMetadata>._GLOBAL_LOOKUP.TryGetValue(_chunkLookupId, out var entityMetadataChunk);
			//__.GetLogger()._EzErrorThrow<SimStormException>(result);
			var entityMetadataChunk =
				Chunk<EntityMetadata>._GLOBAL_LOOKUP[pageToken.pageId]._AsSpan_Unsafe()[pageToken.slotRef.chunkIndex];
			ref var entityMetadata = ref entityMetadataChunk.UnsafeArray[chunkIndex];
			__.GetLogger()._EzErrorThrow<SimStormException>(entityMetadata.accessToken == pageToken, "invalid alloc token.   why?");
		}
	}
}
