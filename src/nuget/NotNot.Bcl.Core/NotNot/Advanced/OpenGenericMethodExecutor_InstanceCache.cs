using System;
using System.Reflection;

namespace NotNot.Advanced
{
	/// <summary>
	/// Instance-based cache for delegates created from generic methods. This partial provides caching functionality
	/// to eliminate repeated reflection overhead when creating delegates for the same signature multiple times.
	/// Thread-safe for concurrent access in ECS allocation hot paths.
	/// </summary>
	/// <remarks>
	/// Use instances when the same generic method signatures are invoked repeatedly.
	/// For one-off invocations, use the static methods directly.
	/// <para>
	/// Cache key includes: declaring type, method name, binding flags, generic type arguments, and delegate type.
	/// This ensures exact matches are cached while maintaining type safety.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// // Create executor instance for ComponentPartition
	/// var executor = new OpenGenericMethodExecutor();
	///
	/// // First call performs reflection and caches delegate
	/// var invoker1 = executor.GetCachedInstanceInvoker&lt;Action&lt;ComponentPartition, Mem&lt;EntityHandle&gt;&gt;&gt;(
	///     typeof(ComponentPartition),
	///     "_OnAllocHelper",
	///     typeof(PositionComponent));
	///
	/// // Subsequent calls with same signature return cached delegate instantly
	/// var invoker2 = executor.GetCachedInstanceInvoker&lt;Action&lt;ComponentPartition, Mem&lt;EntityHandle&gt;&gt;&gt;(
	///     typeof(ComponentPartition),
	///     "_OnAllocHelper",
	///     typeof(PositionComponent));
	///
	/// // invoker1 == invoker2 (same cached instance)
	/// </code>
	/// </example>
	public partial class OpenGenericMethodExecutor
	{
		/// <summary>
		/// Thread-safe delegate cache. Key uniquely identifies delegate signature.
		/// </summary>
		private readonly Dictionary<DelegateCacheKey, Delegate> _delegateCache = new();

		/// <summary>
		/// Lock for thread-safe cache access in concurrent scenarios.
		/// </summary>
		private readonly object _cacheLock = new object();

		/// <summary>
		/// Gets or creates a cached delegate for an instance method with the specified signature.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="declaringType">The type that declares the method.</param>
		/// <param name="methodName">The name of the method to invoke.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached delegate that invokes the specified method.</returns>
		/// <remarks>
		/// First call performs full reflection and caches the result.
		/// Subsequent calls with identical parameters return the cached delegate.
		/// Uses default binding flags: Public | NonPublic | Instance.
		/// </remarks>
		public TDelegate GetCachedInstanceInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			return GetCachedInstanceInvoker<TDelegate>(
				declaringType,
				methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
				genericTypeArguments);
		}

		/// <summary>
		/// Gets or creates a cached delegate for an instance method with the specified signature and binding flags.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="declaringType">The type that declares the method.</param>
		/// <param name="methodName">The name of the method to invoke.</param>
		/// <param name="bindingFlags">Binding flags controlling method lookup.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached delegate that invokes the specified method.</returns>
		/// <remarks>
		/// First call performs full reflection and caches the result.
		/// Subsequent calls with identical parameters return the cached delegate.
		/// </remarks>
		public TDelegate GetCachedInstanceInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			BindingFlags bindingFlags,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			var key = new DelegateCacheKey(
				declaringType,
				methodName,
				bindingFlags,
				genericTypeArguments,
				typeof(TDelegate));

			lock (_cacheLock)
			{
				if (_delegateCache.TryGetValue(key, out var cached))
				{
					return (TDelegate)cached;
				}

				var newDelegate = CreateInstanceInvoker<TDelegate>(
					declaringType,
					methodName,
					bindingFlags,
					genericTypeArguments);

				_delegateCache[key] = newDelegate;
				return newDelegate;
			}
		}

		/// <summary>
		/// Gets or creates a cached delegate from a known MethodInfo.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="method">The method to create a delegate for.</param>
		/// <param name="genericTypeArguments">Type arguments to close the generic method.</param>
		/// <returns>A cached delegate that invokes the specified method.</returns>
		/// <remarks>
		/// First call performs delegate creation and caches the result.
		/// Subsequent calls with identical parameters return the cached delegate.
		/// Uses the method's declaring type and name for cache key generation.
		/// </remarks>
		public TDelegate GetCachedExactInstanceInvoker<TDelegate>(
			MethodInfo method,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			if (method == null) throw new ArgumentNullException(nameof(method));
			if (method.DeclaringType == null)
				throw new ArgumentException("Method must have a declaring type", nameof(method));

			var key = new DelegateCacheKey(
				method.DeclaringType,
				method.Name,
				BindingFlags.Default,
				genericTypeArguments,
				typeof(TDelegate));

			lock (_cacheLock)
			{
				if (_delegateCache.TryGetValue(key, out var cached))
				{
					return (TDelegate)cached;
				}

				var newDelegate = CreateExactInstanceInvoker<TDelegate>(
					method,
					genericTypeArguments);

				_delegateCache[key] = newDelegate;
				return newDelegate;
			}
		}

		/// <summary>
		/// Gets or creates a cached Action delegate for an instance method.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TTarget">The type that declares the method.</typeparam>
		/// <param name="methodName">The name of the method to invoke.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached Action delegate.</returns>
		public Action<TTarget> GetCachedInstanceAction<TTarget>(
			string methodName,
			params Type[] genericTypeArguments)
			where TTarget : class
		{
			return GetCachedInstanceInvoker<Action<TTarget>>(
				typeof(TTarget),
				methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
				genericTypeArguments);
		}

		/// <summary>
		/// Gets or creates a cached Func delegate for an instance method.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TTarget">The type that declares the method.</typeparam>
		/// <typeparam name="TResult">The return type of the method.</typeparam>
		/// <param name="methodName">The name of the method to invoke.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached Func delegate.</returns>
		public Func<TTarget, TResult> GetCachedInstanceFunc<TTarget, TResult>(
			string methodName,
			params Type[] genericTypeArguments)
			where TTarget : class
		{
			return GetCachedInstanceInvoker<Func<TTarget, TResult>>(
				typeof(TTarget),
				methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
				genericTypeArguments);
		}

		/// <summary>
		/// Clears all cached delegates. Use when memory pressure is high or after major type reloading.
		/// </summary>
		public void ClearCache()
		{
			lock (_cacheLock)
			{
				_delegateCache.Clear();
			}
		}

		/// <summary>
		/// Gets the current number of cached delegates.
		/// </summary>
		public int CachedDelegateCount
		{
			get
			{
				lock (_cacheLock)
				{
					return _delegateCache.Count;
				}
			}
		}

		/// <summary>
		/// Cache key that uniquely identifies a delegate signature.
		/// Includes declaring type, method name, binding flags, generic type arguments, and delegate type.
		/// </summary>
		private readonly struct DelegateCacheKey : IEquatable<DelegateCacheKey>
		{
			private readonly Type _declaringType;
			private readonly string _methodName;
			private readonly BindingFlags _bindingFlags;
			private readonly Type[] _genericTypeArguments;
			private readonly Type _delegateType;
			private readonly int _hashCode;

			public DelegateCacheKey(
				Type declaringType,
				string methodName,
				BindingFlags bindingFlags,
				Type[] genericTypeArguments,
				Type delegateType)
			{
				_declaringType = declaringType;
				_methodName = methodName;
				_bindingFlags = bindingFlags;
				_genericTypeArguments = genericTypeArguments;
				_delegateType = delegateType;

				var hash = new HashCode();
				hash.Add(_declaringType);
				hash.Add(_methodName);
				hash.Add(_bindingFlags);
				hash.Add(_delegateType);
				foreach (var typeArg in _genericTypeArguments)
				{
					hash.Add(typeArg);
				}
				_hashCode = hash.ToHashCode();
			}

			public bool Equals(DelegateCacheKey other)
			{
				if (_declaringType != other._declaringType) return false;
				if (_methodName != other._methodName) return false;
				if (_bindingFlags != other._bindingFlags) return false;
				if (_delegateType != other._delegateType) return false;
				if (_genericTypeArguments.Length != other._genericTypeArguments.Length) return false;

				for (int i = 0; i < _genericTypeArguments.Length; i++)
				{
					if (_genericTypeArguments[i] != other._genericTypeArguments[i])
						return false;
				}

				return true;
			}

			public override bool Equals(object? obj)
			{
				return obj is DelegateCacheKey other && Equals(other);
			}

			public override int GetHashCode() => _hashCode;
		}

		#region Static Method Caching

		/// <summary>
		/// Gets or creates a cached delegate for a static method with the specified signature.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="declaringType">The type that declares the static method.</param>
		/// <param name="methodName">The name of the static method to invoke.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached delegate that invokes the specified static method.</returns>
		/// <remarks>
		/// First call performs full reflection and caches the result.
		/// Subsequent calls with identical parameters return the cached delegate.
		/// Uses default binding flags: Public | NonPublic | Static.
		/// </remarks>
		public TDelegate GetCachedStaticInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			return GetCachedStaticInvoker<TDelegate>(
				declaringType,
				methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				genericTypeArguments);
		}

		/// <summary>
		/// Gets or creates a cached delegate for a static method with the specified signature and binding flags.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="declaringType">The type that declares the static method.</param>
		/// <param name="methodName">The name of the static method to invoke.</param>
		/// <param name="bindingFlags">Binding flags to control method lookup.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached delegate that invokes the specified static method.</returns>
		public TDelegate GetCachedStaticInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			BindingFlags bindingFlags,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			var key = new DelegateCacheKey(
				declaringType,
				methodName,
				bindingFlags,
				genericTypeArguments,
				typeof(TDelegate));

			lock (_cacheLock)
			{
				if (_delegateCache.TryGetValue(key, out var cached))
				{
					return (TDelegate)cached;
				}

				var newDelegate = CreateStaticInvoker<TDelegate>(
					declaringType,
					methodName,
					bindingFlags,
					genericTypeArguments);

				_delegateCache[key] = newDelegate;
				return newDelegate;
			}
		}

		/// <summary>
		/// Gets or creates a cached delegate for a known static MethodInfo.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="method">The static method to create a delegate for.</param>
		/// <param name="genericTypeArguments">Type arguments to close the generic method.</param>
		/// <returns>A cached delegate that invokes the specified static method.</returns>
		/// <remarks>
		/// Cache key includes method's declaring type, name, and type arguments.
		/// Faster than search-based caching for known MethodInfo.
		/// </remarks>
		public TDelegate GetCachedExactStaticInvoker<TDelegate>(
			MethodInfo method,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			if (method == null) throw new ArgumentNullException(nameof(method));
			if (!method.IsStatic)
				throw new ArgumentException($"Method '{method.Name}' is not static. Use GetCachedExactInstanceInvoker for instance methods.", nameof(method));

			// Use method's declaring type and name for cache key
			// This allows caching even when MethodInfo instance differs
			var bindingFlags = method.IsPublic ? BindingFlags.Public : BindingFlags.NonPublic;
			bindingFlags |= BindingFlags.Static;

			var key = new DelegateCacheKey(
				method.DeclaringType!,
				method.Name,
				bindingFlags,
				genericTypeArguments,
				typeof(TDelegate));

			lock (_cacheLock)
			{
				if (_delegateCache.TryGetValue(key, out var cached))
				{
					return (TDelegate)cached;
				}

				var newDelegate = CreateExactStaticInvoker<TDelegate>(
					method,
					genericTypeArguments);

				_delegateCache[key] = newDelegate;
				return newDelegate;
			}
		}

		/// <summary>
		/// Gets or creates a cached Action delegate for a static method.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <param name="declaringType">The type that declares the static method.</param>
		/// <param name="methodName">The name of the static method to invoke.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached Action delegate.</returns>
		public Action GetCachedStaticAction(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
		{
			return GetCachedStaticInvoker<Action>(
				declaringType,
				methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				genericTypeArguments);
		}

		/// <summary>
		/// Gets or creates a cached Func delegate for a static method.
		/// Thread-safe for concurrent access.
		/// </summary>
		/// <typeparam name="TResult">The return type of the static method.</typeparam>
		/// <param name="declaringType">The type that declares the static method.</param>
		/// <param name="methodName">The name of the static method to invoke.</param>
		/// <param name="genericTypeArguments">The type arguments to close the generic method.</param>
		/// <returns>A cached Func delegate.</returns>
		public Func<TResult> GetCachedStaticFunc<TResult>(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
		{
			return GetCachedStaticInvoker<Func<TResult>>(
				declaringType,
				methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				genericTypeArguments);
		}

		#endregion
	}
}
