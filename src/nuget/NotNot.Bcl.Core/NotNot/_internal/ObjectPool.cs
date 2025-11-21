using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using NotNot.Advanced;

namespace NotNot._internal;

/// <summary>
///    simple Object Pool implementation.  each instance of this class owns it's own, separate pool of objects.
/// </summary>
[ThreadSafe]
public class ObjectPool : IDisposeGuard
{
	/// <summary>
	/// Cache for compiled Clear() method delegates
	/// </summary>
	private ConcurrentDictionary<Type, Action<object>?> _clearDelegateCache = new();

#if CHECKED
	/// <summary>
	/// Tracks live (rented) objects with their version numbers for use-after-return detection. CHECKED builds only.
	/// </summary>
	private readonly ConcurrentDictionary<object, int> _CHECKED_liveObjects = new(ReferenceEqualityComparer.Instance);
	
	/// <summary>
	/// Current version counter for rent operations. Incremented atomically. CHECKED builds only.
	/// </summary>
	private int _currentVersion = 1; // Start at 1, 0 reserved for uninitialized
#endif

	/// <summary>
	/// Disposable wrapper for rented objects. Use with `using` pattern to auto-return to pool.
	/// </summary>
	public struct Rented<T> : IDisposable where T : class
	{
		public readonly bool IsAllocated => Value is not null;
		public T Value { get; private set; }
		private readonly ObjectPool _pool;
		private Action<T>? _clearAction;
		private readonly bool _skipAutoClear;
#if CHECKED
		private readonly int _version;
		private DisposeGuard _disposeGuard;
#endif

		public Rented(ObjectPool pool, T item, Action<T>? clearAction = null, bool skipAutoClear = false, int version = 0)
		{

			_pool = pool;
			Value = item;
			_clearAction = clearAction;
			_skipAutoClear = skipAutoClear;
#if CHECKED
			_version = version;
			_disposeGuard = new();
#endif
		}

		public void Dispose()
		{
			try
			{
				if (_clearAction != null)
				{
					_clearAction(Value);
				}
			}
			finally
			{
#if CHECKED
				_pool.ValidateAndRemoveVersion(Value, _version);
#endif
				_pool.Return(Value, _skipAutoClear);

#if CHECKED
				_disposeGuard.Dispose();
#endif
				Value = null;
			}
		}
	}

	/// <summary>
	/// Disposable wrapper for rented arrays. Use with `using` pattern to auto-return to pool.
	/// </summary>
	public struct RentedArray<T> : IDisposable
	{
		private readonly ObjectPool _pool;
		public readonly bool IsAllocated => Value is not null;
		public T[] Value { get; private set; }
		private readonly bool _preserveContents;
#if CHECKED
		private readonly int _version;
		private DisposeGuard _disposeGuard;
#endif

		public RentedArray(ObjectPool pool, T[] array, bool preserveContents = false, int version = 0)
		{
			_pool = pool;
			Value = array;
			_preserveContents = preserveContents;
#if CHECKED
			_version = version;
			_disposeGuard = new();
#endif
		}

		public void Dispose()
		{
#if CHECKED
			_pool.ValidateAndRemoveVersion(Value, _version);
#endif
			_pool.ReturnArray(Value, _preserveContents);

#if CHECKED
			_disposeGuard.Dispose();
#endif
			Value = null!;
		}
	}


	/// <summary>
	///    stores recycled objects.   uses HashSet for double-dispose protection.
	/// </summary>
	private ConcurrentDictionary<Type, HashSet<object>> _itemStorage = new();
	/// <summary>
	///    stores recycled arrays of precise length.   uses HashSet for double-dispose protection.
	/// </summary>
	private ConcurrentDictionary<Type, ConcurrentDictionary<int, HashSet<object>>> _arrayStore = new();


	/// <summary>
	///    stores objects of the given type. Returns HashSet for double-dispose protection.
	/// </summary>
	private HashSet<object> _GetItemTypePool<T>()
	{
		var type = typeof(T);
		var pool = _itemStorage.GetOrAdd(type, _type => new HashSet<object>(ReferenceEqualityComparer.Instance));

		return pool;
	}

	public void Dispose()
	{
		if (IsDisposed) { return; }
		IsDisposed = true;
		_itemStorage.Clear();
		_itemStorage = null!;
		_arrayStore.Clear();
		_arrayStore = null!;

		_clearDelegateCache.Clear();
		_clearDelegateCache = null;
	}
	public bool IsDisposed { get; private set; } = false;


#if CHECKED
	/// <summary>
	/// Generate next version number for rent tracking. Thread-safe with overflow handling.
	/// </summary>
	private int GetNextVersion()
	{
		var version = Interlocked.Increment(ref _currentVersion);
		if (version == int.MinValue) // Wrapped from MaxValue
		{
			// Skip 0 and negative values, reset to 1
			Interlocked.CompareExchange(ref _currentVersion, 1, int.MinValue);
			return 1;
		}
		return version;
	}

	/// <summary>
	/// Validates object version matches expected and removes from live tracking. Detects use-after-return bugs.
	/// </summary>
	internal void ValidateAndRemoveVersion<T>(T item, int expectedVersion)
	{
		if (item is null) return;

		if (_CHECKED_liveObjects.TryRemove(item, out var actualVersion))
		{
			if (actualVersion != expectedVersion)
			{
				// Use-after-return detected: object was returned and re-rented
				__.AssertIfNot(false,
					$"Use-after-return detected for {typeof(T).Name}. " +
					$"Expected version {expectedVersion}, but object has version {actualVersion}. " +
					$"Object was returned to pool and rented again while still being used.");
			}
		}
		else
		{
			// Object not in tracking - likely double-return scenario
			// This is handled by existing HashSet.Add check in Return()
			// Don't assert here as double-dispose is caught elsewhere
		}
	}
#endif



	/// <summary>
	///    stores arrays of the given length. Returns HashSet for double-dispose protection.
	/// </summary>
	private HashSet<object> _GetTypeArrayPool<T>(int length)
	{
		var type = typeof(T);

		var typeArrayStore =
			_arrayStore.GetOrAdd(type, _type => new ConcurrentDictionary<int, HashSet<object>>());

		var pool = typeArrayStore.GetOrAdd(length, _len => new HashSet<object>(ReferenceEqualityComparer.Instance));

		return pool;
	}

	[Obsolete("Use Rent<T>() method instead for better usability with using pattern.")]
	public T Get_Unsafe<T>() where T : class, new()
	{
		if (IsDisposed) { return new T(); }

		var pool = _GetItemTypePool<T>();

		lock (pool)
		{
			if (pool.Count > 0)
			{
				var item = pool.First();
				pool.Remove(item);
				return (T)item;
			}
		}

		return new T();
	}

	/// <summary>
	///    Rent an object from the pool. Use with `using` pattern to auto-return to pool.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="clearAction">optional, for clearing the item before returning to the pool</param>
	/// <param name="skipAutoClear">If false (default), will auto call `.Clear()` on the object when returned, if the method exists.</param>
	/// <returns></returns>
	public Rented<T> Rent<T>(out T item, Action<T>? clearAction = null, bool skipAutoClear = false) where T : class, new()
	{
		item = Get_Unsafe<T>();

#if CHECKED
		var version = GetNextVersion();
		var added = _CHECKED_liveObjects.TryAdd(item, version);
		if (!added)
		{
			// Object still marked as rented - this is a pool corruption bug
			__.Throw($"Object {typeof(T).Name} rented while still in live objects tracking. Pool corruption detected.");
		}
		return new Rented<T>(this, item, clearAction, skipAutoClear, version);
#else
		return new Rented<T>(this, item, clearAction, skipAutoClear);
#endif
	}

	/// <summary>
	///    Rent an array from the pool. Use with `using` pattern to auto-return to pool.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="length">Length of array to rent</param>
	/// <param name="preserveContents">If true, contents will not be cleared when returned to pool</param>
	/// <returns></returns>
	public RentedArray<T> RentArray<T>(int length, out T[] array, bool preserveContents = false)
	{
		array = GetArray_Unsafe<T>(length);

#if CHECKED
		var version = GetNextVersion();
		var added = _CHECKED_liveObjects.TryAdd(array, version);
		if (!added)
		{
			// Array still marked as rented - this is a pool corruption bug
			__.Throw($"Array {typeof(T).Name}[{length}] rented while still in live objects tracking. Pool corruption detected.");
		}
		return new RentedArray<T>(this, array, preserveContents, version);
#else
		return new RentedArray<T>(this, array, preserveContents);
#endif
	}



	/// <summary>
	/// Return an object to the pool with optional auto-clear support. Detects double-dispose.
	/// If skipAutoClear is false (default), attempts to call Clear() method on the object before returning to pool.
	/// </summary>
	/// <typeparam name="T">Type of object to return</typeparam>
	/// <param name="item">The object to return to the pool</param>
	/// <param name="skipAutoClear">If false (default), will auto call `.Clear()` on the object when returned, if the method exists.</param>
	[Obsolete("Use Rent<T>() method instead for better usability with using pattern.")]
	public void Return<T>(T item, bool skipAutoClear = false)
	{
		if (item is null) { return; }
		if (IsDisposed) { return; }

		// Auto-clear logic (before lock)
		if (!skipAutoClear)
		{
			_TryAutoClear(item);
		}

		var pool = _GetItemTypePool<T>();

		lock (pool)
		{
			var wasAdded = pool.Add(item);
			__.AssertIfNot(wasAdded, $"Double-return detected for object of type {typeof(T).Name}. Object was already returned to pool.");
		}
	}

	/// <summary>
	/// Attempts to call Clear() method on the object using cached compiled delegates for performance.
	/// Swallows any exceptions to prevent pool corruption.
	/// </summary>
	private void _TryAutoClear<T>(T item)
	{
		if (item is null) { return; }

		var clearAction = _clearDelegateCache.GetOrAdd(typeof(T), type =>
		{
			// Handle arrays specially - use Array.Clear()
			if (type.IsArray)
			{
				// Create compiled delegate: (object obj) => Array.Clear((Array)obj, 0, ((Array)obj).Length)
				var param = Expression.Parameter(typeof(object), "obj");
				var arrayParam = Expression.Convert(param, typeof(Array));
				var lengthExpr = Expression.Property(arrayParam, "Length");
				var clearCall = Expression.Call(
					typeof(Array).GetMethod("Clear", new[] { typeof(Array), typeof(int), typeof(int) })!,
					arrayParam,
					Expression.Constant(0),
					lengthExpr);

				return Expression.Lambda<Action<object>>(clearCall, param).Compile();
			}

			// Look for public instance Clear() method with no parameters
			var clearMethod = type.GetMethod("Clear",
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy,
				null, Type.EmptyTypes, null);

			if (clearMethod == null) { return null; }

			// Create compiled delegate for performance (~10-20ns vs ~100-1000ns for MethodInfo.Invoke)
			var param2 = Expression.Parameter(typeof(object), "obj");
			var castParam = Expression.Convert(param2, type);
			var callExpr = Expression.Call(castParam, clearMethod);

			return Expression.Lambda<Action<object>>(callExpr, param2).Compile();
		});

		// Invoke compiled delegate if Clear method exists
		try
		{
			clearAction?.Invoke(item);
		}
		catch
		{
			// Swallow exceptions from Clear() to prevent pool corruption
			// User's Clear() implementation issues should not crash the pool
		}
	}


	/// <summary>
	///    obtain an array of T of the exact length requested
	/// </summary>
	[Obsolete("Use RentArray<T>() method instead for better usability with using pattern.")]
	public T[] GetArray_Unsafe<T>(int length)
	{
		if (IsDisposed) { return new T[length]; }

		var pool = _GetTypeArrayPool<T>(length);

		lock (pool)
		{
			if (pool.Count > 0)
			{
				var item = pool.First();
				pool.Remove(item);
				return (T[])item;
			}
		}

		return new T[length];
	}

	/// <summary>
	///    recycle the array.   will automatically clear the array unless told otherwise. Detects double-dispose.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="item"></param>
	[Obsolete("Use RentArray<T>() method instead for better usability with using pattern.")]
	public void ReturnArray<T>(T[] item, bool preserveContents = false)
	{
		if (item is null) { return; }
		if (IsDisposed) { return; }

		var length = item.Length;
		if (!preserveContents)
		{
			item._Clear();
		}

		var pool = _GetTypeArrayPool<T>(length);
		
		lock (pool)
		{
			var wasAdded = pool.Add(item);

			__.AssertIfNot(wasAdded, $"Double-return detected for array of type {typeof(T).Name}[{length}]. Array was already returned to pool.");
		}
	}
}

/// <summary>
///    a global ObjectPool.    all methods are static and share the same common object pool.
///    If you want a separate pool per-instance, use <see cref="ObjectPool" />
/// </summary>
[ThreadSafe]
public static class StaticPool
{
	/// <summary>
	/// Shared ObjectPool instance that all StaticPool methods delegate to.
	/// </summary>
	private static readonly ObjectPool _shared = new();

	/// <summary>
	///    Rent an object from the static pool. Use with `using` pattern to auto-return to pool.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="clearAction">optional, for clearing the item before returning to the pool</param>
	/// <param name="skipAutoClear">If false (default), will auto-clear the object when returned. If true, skip auto-clear.</param>
	/// <returns></returns>
	public static ObjectPool.Rented<T> Rent<T>(Action<T>? clearAction = null, bool skipAutoClear = false) where T : class, new()
	{
		return _shared.Rent(out _, clearAction, skipAutoClear);
	}

	/// <summary>
	///    Rent an array from the static pool. Use with `using` pattern to auto-return to pool.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="length">Length of array to rent</param>
	/// <param name="preserveContents">If true, contents will not be cleared when returned to pool</param>
	/// <returns></returns>
	public static ObjectPool.RentedArray<T> RentArray<T>(int length, bool preserveContents = false)
	{
		return _shared.RentArray<T>(length, out _, preserveContents);
	}

	public static T Get<T>() where T : class, new()
	{
		return _shared.Get_Unsafe<T>();
	}


	public static void Return<T>(T item)
	{
		// Legacy Return method - does NOT auto-clear
		_shared.Return(item, skipAutoClear: true);
	}


	/// <summary>
	/// Return an object to the pool with optional auto-clear support.
	/// If skipAutoClear is false (default), attempts to call Clear() method on the object before returning to pool.
	/// </summary>
	/// <typeparam name="T">Type of object to return</typeparam>
	/// <param name="item">The object to return to the pool</param>
	/// <param name="skipAutoClear">If false, will attempt to auto-clear the object. If true, object returned as-is.</param>
	public static void Return_New<T>(T item, bool skipAutoClear = false)
	{
		// Return_New method - respects skipAutoClear parameter
		_shared.Return(item, skipAutoClear);
	}


	/// <summary>
	///    obtain an array of T of the exact length requested
	/// </summary>
	public static T[] GetArray<T>(int length)
	{
		return _shared.GetArray_Unsafe<T>(length);
	}

	/// <summary>
	///    recycle the array.   will automatically clear the array unless told otherwise
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="item"></param>
	public static void ReturnArray<T>(T[] item, bool preserveContents = false)
	{
		_shared.ReturnArray(item, preserveContents);
	}

}
