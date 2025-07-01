// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using CommunityToolkit.HighPerformance.Buffers;
using NotNot.Collections.Advanced;
using System.Diagnostics;

namespace NotNot;

/// <summary>
///    Use an array of {T} from the ArrayPool, without allocating any objects upon use. (no gc pressure)
///    use this instead of <see cref="SpanOwner{T}" />.  This will alert you if you do not dispose properly.
/// </summary>
/// <typeparam name="T"></typeparam>
public ref struct SpanGuard<T>
{
	/// <summary>
	///    should dispose prior to exit function.   easiest way is to ex:  `using var spanGuard = SpanGuard[int](42)
	/// </summary>
	/// <param name="size"></param>
	/// <returns></returns>
	public static SpanGuard<T> Allocate(int size)
	{
		return new SpanGuard<T>(SpanOwner<T>.Allocate(size));
	}

	public SpanGuard(SpanOwner<T> owner)
	{
		_poolOwner = owner;
#if CHECKED
		_disposeGuard = new();
#endif
	}

	public SpanOwner<T> _poolOwner;

	public Span<T> Span => _poolOwner.Span;

	public ArraySegment<T> DangerousGetArray()
	{
		return _poolOwner.DangerousGetArray();
	}


#if CHECKED
	private DisposeGuard _disposeGuard;
#endif
	public void Dispose()
	{
		_poolOwner.Dispose();

#if CHECKED
		_disposeGuard.Dispose();
#endif
	}
}

/// <summary>
///    helpers to allocate a WriteMem instance
/// </summary>
public static class Mem
{
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(ArraySegment<T> backingStore)
	{
		return Mem<T>.CreateUsing(backingStore);
	}

	//public static WriteMem<T> Allocate<T>(MemoryOwnerCustom<T> MemoryOwnerNew) => WriteMem<T>.Allocate(MemoryOwnerNew);
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(T[] array)
	{
		return Mem<T>.CreateUsing(array);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> AllocateAndAssign<T>(T singleItem)
	{
		return Mem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(int count)
	{
		return Mem<T>.Allocate(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static Mem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return Mem<T>.Allocate(span);
	}

	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(Mem<T> writeMem)
	{
		return writeMem;
	}

	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static Mem<T> CreateUsing<T>(ReadMem<T> readMem)
	{
		return Mem<T>.CreateUsing(readMem);
	}
}

/// <summary>
///    helpers to allocate a ReadMem instance
/// </summary>
public static class ReadMem
{
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static ReadMem<T> CreateUsing<T>(ArraySegment<T> backingStore)
	{
		return ReadMem<T>.CreateUsing(backingStore);
	}

	//public static ReadMem<T> Allocate<T>(MemoryOwnerCustom<T> MemoryOwnerNew) => ReadMem<T>.Allocate(MemoryOwnerNew);
	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static ReadMem<T> CreateUsing<T>(T[] array)
	{
		return ReadMem<T>.CreateUsing(array);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> AllocateAndAssign<T>(T singleItem)
	{
		return ReadMem<T>.AllocateAndAssign(singleItem);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> Allocate<T>(int count)
	{
		return ReadMem<T>.Allocate(count);
	}

	/// <summary>
	///    allocate from the pool (recycles the backing array for reuse when done)
	/// </summary>
	public static ReadMem<T> Allocate<T>(ReadOnlySpan<T> span)
	{
		return ReadMem<T>.Allocate(span);
	}

	/// <summary>
	///    Create a temporary (no-pooled) mem using your own backing data object
	/// </summary>
	public static ReadMem<T> CreateUsing<T>(Mem<T> writeMem)
	{
		return ReadMem<T>.CreateUsing(writeMem);
	}
}

/// <summary>
///    a write capable view into an array/span
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct Mem<T>
{
	/// <summary>
	///    if pooled, this will be set.  a reference to the pooled location so it can be recycled
	/// </summary>
	private readonly MemoryOwner_Custom<T>? _poolOwner;

	/// <summary>
	///    details the backing storage
	/// </summary>
	private readonly ArraySegment<T> _segment;

	//private readonly T[] _array;
	//private readonly int _offset;
	//public readonly int length;

	public static readonly Mem<T> Empty = new(null, ArraySegment<T>.Empty);
	internal Mem(MemoryOwner_Custom<T> owner) : this(owner, owner.DangerousGetArray()) { }
	internal Mem(ArraySegment<T> segment) : this(null, segment) { }

	internal Mem(MemoryOwner_Custom<T> owner, ArraySegment<T> segment, int subOffset, int length) : this(owner,
		new ArraySegment<T>(segment.Array, segment.Offset + subOffset, length))
	{
		__.GetLogger()._EzError(subOffset + segment.Offset + length <= segment.Count);
		//__.GetLogger()._EzError(length <= segment.Count);
	}

	internal Mem(T[] array, int offset, int length) : this(null, new ArraySegment<T>(array, offset, length)) { }

	internal Mem(T[] array) : this(null, new ArraySegment<T>(array)) { }

	internal Mem(MemoryOwner_Custom<T> owner, ArraySegment<T> segment)
	{
		_poolOwner = owner;
		_segment = segment;
	}


	/// <summary>
	///    allocate memory from the shared pool.
	///    If your Type is a reference type or contains references, be sure to use clearOnDispose otherwise you will have
	///    memory leaks.
	///    also note that the memory is not cleared by default.
	/// </summary>
	public static Mem<T> Allocate(int size)
	{
		//__.AssertOnce(RuntimeHelpers.IsReferenceOrContainsReferences<T>() == false || clearOnDispose, "alloc of classes via memPool can/will cause leaks");
		var mo = MemoryOwner_Custom<T>.Allocate(size, AllocationMode.Clear);
		//mo.ClearOnDispose = clearOnDispose;
		return new Mem<T>(mo);
	}

	/// <summary>
	///    allocate memory from the shared pool and copy the contents of the specified span into it
	/// </summary>
	public static Mem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.Span);
		return toReturn;
	}

	public static Mem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Allocate(1);
		mem[0] = singleItem;
		return mem;
	}

	public static Mem<T> CreateUsing(T[] array)
	{
		return new Mem<T>(new ArraySegment<T>(array));
	}

	public static Mem<T> CreateUsing(T[] array, int offset, int count)
	{
		return new Mem<T>(new ArraySegment<T>(array, offset, count));
	}

	public static Mem<T> CreateUsing(ArraySegment<T> backingStore)
	{
		return new Mem<T>(backingStore);
	}

	internal static Mem<T> CreateUsing(MemoryOwner_Custom<T> MemoryOwnerNew)
	{
		return new Mem<T>(MemoryOwnerNew);
	}

	public static Mem<T> CreateUsing(ReadMem<T> readMem)
	{
		return readMem.AsWriteMem();
	}


	public Mem<T> Slice(int offset, int count)
	{
		//var toReturn = new Mem<T>(_poolOwner, new(_array, _offset + offset, count), _array, _offset + offset, count);
		var toReturn = new Mem<T>(_poolOwner, _segment, offset, count);
		return toReturn;
	}


	/// <summary>
	///    beware: the size of the array allocated may be larger than the size requested by this Mem.
	///    As such, beware if using the backing Array directly.  respect the offset+length described in this segment.
	/// </summary>
	public ArraySegment<T> DangerousGetArray()
	{
		return _segment;
	}

	public Span<T> Span =>
		//return new Span<T>(_array, _offset, length);
		_segment.AsSpan();

	public Memory<T> Memory =>
		//return new Memory<T>(_array, _offset, length);
		_segment.AsMemory();

	public int Length => _segment.Count;

	/// <summary>
	///    if owned by a pook, recycles.   DANGER: any other references to the same backing pool slot are also disposed at this
	///    time!
	/// </summary>
	public void Dispose()
	{
		//only do work if backed by an owner, and if so, recycle
		if (_poolOwner != null)
		{
			AssertNotDisposed();
			__.GetLogger()._EzError(_poolOwner.IsDisposed, "backing _poolOwner is already disposed!");

			var array = _segment.Array;
			Array.Clear(array, 0, array.Length);
			_poolOwner.Dispose();
		}

		//#if DEBUG
		//		Array.Clear(_array, _offset, Length);
		//#endif
	}

	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		if (_poolOwner != null)
		{
			__.GetLogger()._EzError(_poolOwner?.IsDisposed != true, "disposed while in use");
		}
	}

	public ref T this[int index]
	{
		get
		{
			AssertNotDisposed();
			return ref Span[index];
			//__.GetLogger()._EzError(index >= 0 && index < length);
			//return ref _array[_offset + index];
		}
	}

	public Span<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	public IEnumerable<T> Enumerable => _segment;

	public ReadMem<T> AsReadMem()
	{
		return new ReadMem<T>(_poolOwner, _segment);
	}

	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{_segment.Count}]";
	}
}

/// <summary>
///    a read-only capable view into an array/span
/// </summary>
/// <typeparam name="T"></typeparam>
//[DebuggerTypeProxy(typeof(NotNot.Bcl.Collections.Advanced.CollectionDebugView<>))]
//[DebuggerDisplay("{ToString(),raw}")]
//[DebuggerDisplay("{ToString(),nq}")]
public readonly struct ReadMem<T>
{
	/// <summary>
	///    if pooled, this will be set.  a reference to the pooled location so it can be recycled
	/// </summary>
	private readonly MemoryOwner_Custom<T>? _poolOwner;

	/// <summary>
	///    details the backing storage
	/// </summary>
	private readonly ArraySegment<T> _segment;

	//private readonly T[] _array;
	//private readonly int _offset;
	//public readonly int length;
	//public int Length => _segment.Count;

	public static readonly ReadMem<T> Empty = new(null, ArraySegment<T>.Empty);
	internal ReadMem(MemoryOwner_Custom<T> owner) : this(owner, owner.DangerousGetArray()) { }
	internal ReadMem(ArraySegment<T> segment) : this(null, segment) { }

	internal ReadMem(MemoryOwner_Custom<T> owner, ArraySegment<T> segment, int subOffset, int length) : this(owner,
		new ArraySegment<T>(segment.Array, segment.Offset + subOffset, length))
	{
		__.GetLogger()._EzError(subOffset + segment.Offset + length < segment.Count);
		__.GetLogger()._EzError(length <= segment.Count);
	}

	internal ReadMem(T[] array, int offset, int length) : this(null, new ArraySegment<T>(array, offset, length)) { }

	internal ReadMem(T[] array) : this(null, new ArraySegment<T>(array)) { }

	internal ReadMem(MemoryOwner_Custom<T> owner, ArraySegment<T> segment)
	{
		_poolOwner = owner;
		_segment = segment;
	}


	/// <summary>
	///    allocate memory from the shared pool.
	///    If your Type is a reference type or contains references, be sure to use clearOnDispose otherwise you will have
	///    memory leaks.
	///    also note that the memory is not cleared by default.
	/// </summary>
	public static ReadMem<T> Allocate(int size)
	{
		//__.AssertOnce(RuntimeHelpers.IsReferenceOrContainsReferences<T>() == false || , "alloc of classes via memPool can/will cause leaks");
		var mo = MemoryOwner_Custom<T>.Allocate(size, AllocationMode.Clear);
		//mo.ClearOnDispose = clearOnDispose;
		return new ReadMem<T>(mo);
	}

	/// <summary>
	///    allocate memory from the shared pool and copy the contents of the specified span into it
	/// </summary>
	public static ReadMem<T> Allocate(ReadOnlySpan<T> span)
	{
		var toReturn = Allocate(span.Length);
		span.CopyTo(toReturn.AsWriteSpan());
		return toReturn;
	}

	public static ReadMem<T> AllocateAndAssign(T singleItem)
	{
		var mem = Mem<T>.Allocate(1);
		mem[0] = singleItem;
		return CreateUsing(mem);
	}

	public static ReadMem<T> CreateUsing(T[] array)
	{
		return new ReadMem<T>(new ArraySegment<T>(array));
	}

	public static ReadMem<T> CreateUsing(T[] array, int offset, int count)
	{
		return new ReadMem<T>(new ArraySegment<T>(array, offset, count));
	}

	public static ReadMem<T> CreateUsing(ArraySegment<T> backingStore)
	{
		return new ReadMem<T>(backingStore);
	}

	internal static ReadMem<T> CreateUsing(MemoryOwner_Custom<T> MemoryOwnerNew)
	{
		return new ReadMem<T>(MemoryOwnerNew);
	}

	public static ReadMem<T> CreateUsing(Mem<T> mem)
	{
		return mem.AsReadMem();
	}


	public Mem<T> Slice(int offset, int count)
	{
		//var toReturn = new Mem<T>(_poolOwner, new(_array, _offset + offset, count), _array, _offset + offset, count);
		var toReturn = new Mem<T>(_poolOwner, _segment, offset, count);
		return toReturn;
	}


	/// <summary>
	///    beware: the size of the array allocated may be larger than the size requested by this Mem.
	///    As such, beware if using the backing Array directly.  respect the offset+length described in this segment.
	/// </summary>
	public ArraySegment<T> DangerousGetArray()
	{
		return _segment;
	}

	public ReadOnlySpan<T> Span =>
		//return new Span<T>(_array, _offset, length);
		_segment.AsSpan();

	public Span<T> AsWriteSpan()
	{
		return _segment.AsSpan();
	}

	public Memory<T> Memory =>
		//return new Memory<T>(_array, _offset, length);
		_segment.AsMemory();

	public int Length => _segment.Count;

	/// <summary>
	///    if owned by a pook, recycles.   DANGER: any other references to the same backing pool slot are also disposed at this
	///    time!
	/// </summary>
	public void Dispose()
	{
		//only do work if backed by an owner, and if so, recycle
		if (_poolOwner != null)
		{
			AssertNotDisposed();
			__.GetLogger()._EzError(_poolOwner.IsDisposed, "backing _poolOwner is already disposed!");

			var array = _segment.Array;
			Array.Clear(array, 0, array.Length);
			_poolOwner.Dispose();
		}

		//#if DEBUG
		//		Array.Clear(_array, _offset, Length);
		//#endif
	}

	[Conditional("CHECKED")]
	private void AssertNotDisposed()
	{
		__.GetLogger()._EzError(_poolOwner?.IsDisposed != true, "disposed while in use");
	}

	public T this[int index]
	{
		get
		{
			AssertNotDisposed();
			return _segment[index];
			//return ref Span[index];
			//__.GetLogger()._EzError(index >= 0 && index < length);
			//return ref _array[_offset + index];
		}
	}

	public ReadOnlySpan<T>.Enumerator GetEnumerator()
	{
		return Span.GetEnumerator();
	}

	public IEnumerable<T> Enumerable => _segment;

	public Mem<T> AsWriteMem()
	{
		return new Mem<T>(_poolOwner, _segment);
	}

	public override string ToString()
	{
		return $"{GetType().Name}<{typeof(T).Name}>[{_segment.Count}]";
	}
}
