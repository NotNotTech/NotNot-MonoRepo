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
		private DisposeGuard _disposeGuard;
#endif

		public Rented(ObjectPool pool, T item, Action<T>? clearAction = null, bool skipAutoClear = false)
		{

			_pool = pool;
			Value = item;
			_clearAction = clearAction;
			_skipAutoClear = skipAutoClear;
#if CHECKED
			_disposeGuard = new();
#endif
		}

		public void Dispose()
		{
			if (_clearAction != null)
			{
				_clearAction(Value);
			}

			_pool.Return(Value, _skipAutoClear);

#if CHECKED
			_disposeGuard.Dispose();
#endif
			Value = null;
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
		private DisposeGuard _disposeGuard;
#endif

		public RentedArray(ObjectPool pool, T[] array, bool preserveContents = false)
		{
			_pool = pool;
			Value = array;
			_preserveContents = preserveContents;
#if CHECKED
			_disposeGuard = new();
#endif
		}

		public void Dispose()
		{
			_pool.ReturnArray(Value, _preserveContents);

#if CHECKED
			_disposeGuard.Dispose();
#endif
			Value = null!;
		}
	}


	private ConcurrentDictionary<Type, ConcurrentQueue<object>> _itemStorage = new();
	/// <summary>
	///    stores recycled arrays of precise length
	/// </summary>
	private static ConcurrentDictionary<Type, ConcurrentDictionary<int, ConcurrentQueue<object>>> _arrayStore = new();


	private ConcurrentQueue<object> _GetItemTypePool<T>()
	{
		var type = typeof(T);
		var queue = _itemStorage.GetOrAdd(type, _type => new ConcurrentQueue<object>())!;

		//if (!_itemStorage.TryGetValue(type, out var queue))
		//{
		//	lock (_itemStorage) //be the only one to add at the time
		//	{
		//		if (!_itemStorage.TryGetValue(type, out queue))
		//		{
		//			queue = new ConcurrentQueue<object>();
		//			var result = _itemStorage.TryAdd(type, queue);
		//			__.GetLogger()._EzError(result);
		//		}
		//	}
		//}
		return queue;
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


	/// <summary>
	///    stores arrays of the given length
	/// </summary>
	private ConcurrentQueue<object> _GetTypeArrayPool<T>(int length)
	{
		var type = typeof(T);

		var typeArrayStore =
			_arrayStore.GetOrAdd(type, _type => new ConcurrentDictionary<int, ConcurrentQueue<object>>());

		var queue = typeArrayStore.GetOrAdd(length, _len => new ConcurrentQueue<object>());

		return queue;
	}

	[Obsolete("Use Rent<T>() method instead for better usability with using pattern.")]
	public T Get_Unsafe<T>() where T : class, new()
	{
		var queue = _GetItemTypePool<T>();

		if (queue.TryDequeue(out var item))
		{
			return (T)item;
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
		return new Rented<T>(this, item, clearAction, skipAutoClear);
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
		return new RentedArray<T>(this, array, preserveContents);
	}



	/// <summary>
	/// Return an object to the pool with optional auto-clear support.
	/// If skipAutoClear is false (default), attempts to call Clear() method on the object before returning to pool.
	/// </summary>
	/// <typeparam name="T">Type of object to return</typeparam>
	/// <param name="item">The object to return to the pool</param>
	/// <param name="skipAutoClear">If false (default), will auto call `.Clear()` on the object when returned, if the method exists.</param>
	[Obsolete("Use Rent<T>() method instead for better usability with using pattern.")]
	public void Return<T>(T item, bool skipAutoClear = false)
	{
		if (item is null) { return; }

		// Auto-clear logic (before enqueueing)
		if (!skipAutoClear)
		{
			_TryAutoClear(item);
		}

		var queue = _GetItemTypePool<T>();
		queue.Enqueue(item);
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
		var queue = _GetTypeArrayPool<T>(length);

		if (queue.TryDequeue(out var item))
		{
			return (T[])item;
		}

		return new T[length];
	}

	/// <summary>
	///    recycle the array.   will automatically clear the array unless told otherwise
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="item"></param>
	[Obsolete("Use RentArray<T>() method instead for better usability with using pattern.")]
	public void ReturnArray<T>(T[] item, bool preserveContents = false)
	{
		if (item is null) { return; }

		var length = item.Length;
		if (!preserveContents)
		{
			item._Clear();
		}

		var queue = _GetTypeArrayPool<T>(length);
		queue.Enqueue(item);
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
