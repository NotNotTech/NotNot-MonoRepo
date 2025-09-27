using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using NotNot;

namespace NotNot.Advanced
{
	/// <summary>
	/// High-performance cached invocation of open-generic methods with runtime type arguments.
	/// Supports any delegate signature (Action, Func, custom) with zero allocation after caching.
	/// </summary>
	public static class OpenGenericMethodExecutor
	{
		private readonly record struct CacheKey(
			Type DeclaringType,
			string MethodName,
			Type GenericArg,
			Type DelegateType,
			BindingFlags BindingFlags);

		private static readonly ConcurrentDictionary<CacheKey, Delegate> _cache = new();

		/// <summary>
		/// Gets or creates a cached delegate for any generic method signature.
		/// The delegate type determines the expected method signature.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type (e.g., Action{T}, Func{T,TResult}, custom delegate)</typeparam>
		/// <param name="declaringType">Type containing the method (use typeof(T) for instances)</param>
		/// <param name="methodName">Name of the generic method</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="bindingFlags">Method lookup flags</param>
		/// <returns>Cached delegate matching TDelegate signature</returns>
		/// <example>
		/// <code>
		/// // Instance method returning value: TResult Process{T}(TInput input)
		/// var processor = GetOrAllocInvoker{Func{MyClass, string, int}}(
		///     typeof(MyClass), "Process", typeof(DateTime));
		/// int result = processor(instance, "data");
		///
		/// // Static void method: void Register{T}()
		/// var register = GetOrAllocInvoker{Action}(
		///     typeof(Factory), "Register", typeof(Widget),
		///     BindingFlags.Static | BindingFlags.Public);
		/// register();
		///
		/// // Instance void with parameters: void Add{T}(T item)
		/// var adder = GetOrAllocInvoker{Action{Container, string}}(
		///     typeof(Container), "Add", typeof(string));
		/// adder(container, "item");
		/// </code>
		/// </example>
		private static Delegate GetOrAllocInvokerCore(
			Type delegateType,
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, delegateType, bindingFlags);
			return _cache.GetOrAdd(key, CreateDelegate);
		}

		/// <summary>
		/// Gets or creates a cached delegate for any generic method signature.
		/// The delegate type determines the expected method signature.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type (e.g., Action{T}, Func{T,TResult}, custom delegate)</typeparam>
		/// <param name="declaringType">Type containing the method (use typeof(T) for instances)</param>
		/// <param name="methodName">Name of the generic method</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="bindingFlags">Method lookup flags</param>
		/// <returns>Cached delegate matching TDelegate signature</returns>
		/// <example>
		/// <code>
		/// // Instance method returning value: TResult Process{T}(TInput input)
		/// var processor = GetOrAllocInvoker{Func{MyClass, string, int}}(
		///     typeof(MyClass), "Process", typeof(DateTime));
		/// int result = processor(instance, "data");
		///
		/// // Static void method: void Register{T}()
		/// var register = GetOrAllocInvoker{Action}(
		///     typeof(Factory), "Register", typeof(Widget),
		///     BindingFlags.Static | BindingFlags.Public);
		/// register();
		///
		/// // Instance void with parameters: void Add{T}(T item)
		/// var adder = GetOrAllocInvoker{Action{Container, string}}(
		///     typeof(Container), "Add", typeof(string));
		/// adder(container, "item");
		/// </code>
		/// </example>
		public static TDelegate GetOrAllocInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
			where TDelegate : Delegate
		{
			return (TDelegate)GetOrAllocInvokerCore(
				typeof(TDelegate), declaringType, methodName, genericTypeArgument, bindingFlags);
		}

		/// <summary>
		/// Convenience method for common Action{TTarget} pattern (void instance methods with no parameters)
		/// </summary>
		public static Action<TTarget> GetOrAllocAction<TTarget>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return GetOrAllocInvoker<Action<TTarget>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Convenience method for common Func{TTarget,TResult} pattern
		/// </summary>
		public static Func<TTarget, TResult> GetOrAllocFunc<TTarget, TResult>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return GetOrAllocInvoker<Func<TTarget, TResult>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Custom delegate for Span methods (CLR doesn't support Action{T, Span{T}})
		/// </summary>
		public delegate void SpanInvoker<TTarget, TElement>(TTarget target, Span<TElement> span) where TTarget : class;
		public delegate TResult SpanFunc<TTarget, TElement, TResult>(TTarget target, Span<TElement> span) where TTarget : class;

		/// <summary>
		/// Special handler for Span parameter methods due to CLR restrictions
		/// </summary>
		public static SpanInvoker<TTarget, TElement> GetOrAllocSpanInvoker<TTarget, TElement>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return GetOrAllocInvoker<SpanInvoker<TTarget, TElement>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates a cached delegate for instance methods with dynamic parameter types (Action signature).
		/// Supports any number and type of parameters determined at runtime.
		/// Zero allocations on hot path after initial cache.
		/// </summary>
		/// <typeparam name="TTarget">The type containing the instance method</typeparam>
		/// <param name="methodName">Name of the generic method to invoke</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="parameterTypes">Array of parameter types for the method (empty for parameterless)</param>
		/// <param name="flags">Binding flags for method lookup (defaults to instance methods)</param>
		/// <returns>Delegate that can be invoked with DynamicInvoke. First arg is the instance.</returns>
		/// <remarks>
		/// Uses cached delegate types and unified invocation path for optimal performance.
		/// Zero GC pressure on hot path using SpanGuard for large parameter counts.
		/// </remarks>
		/// <example>
		/// <code>
		/// // Method: void Process<T>(string name, int count)
		/// var processor = GetOrAllocDynamicAction<MyClass>(
		///     "Process",
		///     typeof(DateTime),
		///     new[] { typeof(string), typeof(int) });
		///
		/// processor.DynamicInvoke(instance, "test", 42);
		/// </code>
		/// </example>
		public static Delegate GetOrAllocDynamicAction<TTarget>(
			string methodName,
			Type genericTypeArgument,
			Type[] parameterTypes,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			var paramCount = parameterTypes?.Length ?? 0;
			var totalCount = paramCount + 2; // +1 for TTarget, +1 for void return

			using var guard = SpanGuard<Type>.Allocate(totalCount);
			var allTypes = guard.Span;

			allTypes[0] = typeof(TTarget);
			if (parameterTypes != null)
			{
				for (int i = 0; i < parameterTypes.Length; i++)
				{
					allTypes[i + 1] = parameterTypes[i];
				}
			}
			allTypes[^1] = typeof(void);

			var delegateType = DelegateTypeCache.GetOrAdd(allTypes);
			return GetOrAllocInvokerCore(
				delegateType, typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates a cached delegate for instance methods with dynamic parameter and return types (Func signature).
		/// Supports any number and type of parameters with a return value.
		/// Zero allocations on hot path after initial cache.
		/// </summary>
		/// <typeparam name="TTarget">The type containing the instance method</typeparam>
		/// <param name="methodName">Name of the generic method to invoke</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="returnType">Return type of the method</param>
		/// <param name="parameterTypes">Array of parameter types for the method (empty for parameterless)</param>
		/// <param name="flags">Binding flags for method lookup (defaults to instance methods)</param>
		/// <returns>Delegate that can be invoked with DynamicInvoke. First arg is the instance.</returns>
		/// <remarks>
		/// Uses cached delegate types and unified invocation path for optimal performance.
		/// Zero GC pressure on hot path using SpanGuard for large parameter counts.
		/// </remarks>
		/// <example>
		/// <code>
		/// // Method: string Calculate<T>(double x, double y)
		/// var calculator = GetOrAllocDynamicFunc<MyClass>(
		///     "Calculate",
		///     typeof(int),
		///     typeof(string),
		///     new[] { typeof(double), typeof(double) });
		///
		/// var result = (string)calculator.DynamicInvoke(instance, 3.14, 2.71);
		/// </code>
		/// </example>
		public static Delegate GetOrAllocDynamicFunc<TTarget>(
			string methodName,
			Type genericTypeArgument,
			Type returnType,
			Type[] parameterTypes,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			if (returnType == null) throw new ArgumentNullException(nameof(returnType));

			var paramCount = parameterTypes?.Length ?? 0;
			var totalCount = paramCount + 2; // +1 for TTarget, +1 for return type

			using var guard = SpanGuard<Type>.Allocate(totalCount);
			var allTypes = guard.Span;

			allTypes[0] = typeof(TTarget);
			if (parameterTypes != null)
			{
				for (int i = 0; i < parameterTypes.Length; i++)
				{
					allTypes[i + 1] = parameterTypes[i];
				}
			}
			allTypes[^1] = returnType;

			var delegateType = DelegateTypeCache.GetOrAdd(allTypes);
			return GetOrAllocInvokerCore(
				delegateType, typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates a cached delegate for static methods with dynamic parameter and return types.
		/// Supports any number and type of parameters for static methods.
		/// Zero allocations on hot path after initial cache.
		/// </summary>
		/// <param name="declaringType">Type containing the static method</param>
		/// <param name="methodName">Name of the generic method to invoke</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="returnType">Return type of the method (use typeof(void) for Action-like behavior)</param>
		/// <param name="parameterTypes">Array of parameter types for the method (empty for parameterless)</param>
		/// <param name="flags">Binding flags for method lookup (defaults to static methods)</param>
		/// <returns>Delegate that can be invoked with DynamicInvoke</returns>
		/// <remarks>
		/// Uses cached delegate types and unified invocation path for optimal performance.
		/// Zero GC pressure on hot path using SpanGuard for large parameter counts.
		/// </remarks>
		/// <example>
		/// <code>
		/// // Static method: void Register<T>(string name, int priority)
		/// var register = GetOrAllocDynamicStatic(
		///     typeof(Factory),
		///     "Register",
		///     typeof(Widget),
		///     typeof(void),
		///     new[] { typeof(string), typeof(int) });
		///
		/// register.DynamicInvoke("WidgetA", 10);
		/// </code>
		/// </example>
		public static Delegate GetOrAllocDynamicStatic(
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			Type returnType,
			Type[] parameterTypes,
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
		{
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
			if (returnType == null) throw new ArgumentNullException(nameof(returnType));

			var paramCount = parameterTypes?.Length ?? 0;
			var totalCount = paramCount + 1; // +1 for return type (no instance for static)

			using var guard = SpanGuard<Type>.Allocate(totalCount);
			var allTypes = guard.Span;

			if (parameterTypes != null)
			{
				for (int i = 0; i < parameterTypes.Length; i++)
				{
					allTypes[i] = parameterTypes[i];
				}
			}
			allTypes[^1] = returnType;

			var delegateType = DelegateTypeCache.GetOrAdd(allTypes);
			var staticFlags = (flags | BindingFlags.Static) & ~BindingFlags.Instance;
			return GetOrAllocInvokerCore(
				delegateType, declaringType, methodName, genericTypeArgument, staticFlags);
		}

		public static void Dispose()
		{
			_cache.Clear();
			DelegateTypeCache.Clear();
		}

		private static Delegate CreateDelegate(CacheKey key)
		{
			var invokeMethod = key.DelegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{key.DelegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			var parameterTypes = new Type[invokeParameters.Length];
			for (int i = 0; i < invokeParameters.Length; i++)
			{
				parameterTypes[i] = invokeParameters[i].ParameterType;
			}

			var parameterSpan = new ReadOnlySpan<Type>(parameterTypes);
			var isInstanceDelegate = parameterSpan.Length > 0 && parameterSpan[0].IsAssignableFrom(key.DeclaringType);
			var methodParamSpan = isInstanceDelegate ? parameterSpan[1..] : parameterSpan;

			var searchFlags = key.BindingFlags;
			if (isInstanceDelegate)
			{
				searchFlags = (searchFlags | BindingFlags.Instance) & ~BindingFlags.Static;
			}
			else if (parameterSpan.Length == methodParamSpan.Length)
			{
				searchFlags = (searchFlags | BindingFlags.Static) & ~BindingFlags.Instance;
			}

			var method = FindGenericMethod(
				key.DeclaringType,
				key.MethodName,
				searchFlags,
				methodParamSpan,
				requiredGenericArity: 1)
				?? throw new MissingMethodException(key.DeclaringType.FullName, key.MethodName);

			var closedMethod = method.MakeGenericMethod(key.GenericArg);
			return closedMethod.CreateDelegate(key.DelegateType);
		}

		private static MethodInfo? FindGenericMethod(
			Type declaringType,
			string methodName,
			BindingFlags flags,
			ReadOnlySpan<Type> parameterTypes,
			int requiredGenericArity)
		{
			bool needWalk = (flags & BindingFlags.Instance) != 0 && (flags & BindingFlags.NonPublic) != 0;

			for (Type? t = declaringType; t is not null; t = needWalk ? t.BaseType : null)
			{
				var methods = t.GetMethods(flags);
				for (int i = 0; i < methods.Length; i++)
				{
					var method = methods[i];
					if (!method.IsGenericMethodDefinition) continue;
					if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) continue;
					if (method.GetGenericArguments().Length != requiredGenericArity) continue;

					var parameters = method.GetParameters();
					if (parameters.Length != parameterTypes.Length) continue;

					return method;
				}

				if (!needWalk) break;
			}

			return null;
		}

		private static class DelegateTypeCache
		{
			private sealed class Bucket
			{
				private readonly object _sync = new();
				private Entry[] _entries = Array.Empty<Entry>();

				public Type GetOrAdd(ReadOnlySpan<Type> signature)
				{
					var snapshot = _entries;
					for (int i = 0; i < snapshot.Length; i++)
					{
						if (snapshot[i].Matches(signature))
						{
							return snapshot[i].DelegateType;
						}
					}

					lock (_sync)
					{
						snapshot = _entries;
						for (int i = 0; i < snapshot.Length; i++)
						{
							if (snapshot[i].Matches(signature))
							{
								return snapshot[i].DelegateType;
							}
						}

						var ownedTypes = signature.ToArray();
						var delegateType = Expression.GetDelegateType(ownedTypes);

						var newEntries = new Entry[snapshot.Length + 1];
						snapshot.CopyTo(newEntries, 0);
						newEntries[^1] = new Entry(ownedTypes, delegateType);
						_entries = newEntries;
						return delegateType;
					}
				}
			}

			private readonly struct Entry
			{
				private readonly Type[] _types;
				public readonly Type DelegateType;

				public Entry(Type[] types, Type delegateType)
				{
					_types = types;
					DelegateType = delegateType;
				}

				public bool Matches(ReadOnlySpan<Type> candidate)
				{
					return _types.AsSpan().SequenceEqual(candidate);
				}
			}

			private static readonly ConcurrentDictionary<int, Bucket> _buckets = new();

			public static Type GetOrAdd(ReadOnlySpan<Type> signature)
			{
				var hash = ComputeHash(signature);
				var bucket = _buckets.GetOrAdd(hash, _ => new Bucket());
				return bucket.GetOrAdd(signature);
			}

			public static void Clear() => _buckets.Clear();

			private static int ComputeHash(ReadOnlySpan<Type> types)
			{
				var hash = new HashCode();
				for (int i = 0; i < types.Length; i++)
				{
					hash.Add(types[i]);
				}

				return hash.ToHashCode();
			}
		}
	}
}
