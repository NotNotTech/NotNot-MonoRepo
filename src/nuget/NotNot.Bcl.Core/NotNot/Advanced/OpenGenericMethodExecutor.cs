using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

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
			Type DelegateType);

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
		public static TDelegate GetOrAllocInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
			where TDelegate : Delegate
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));

			var delegateType = typeof(TDelegate);
			var key = new CacheKey(declaringType, methodName, genericTypeArgument, delegateType);

			var del = _cache.GetOrAdd(key, k =>
			{
				var invokeMethod = k.DelegateType.GetMethod("Invoke")
					?? throw new InvalidOperationException($"{k.DelegateType} is not a valid delegate type");

				var paramTypes = invokeMethod.GetParameters().Select(p => p.ParameterType).ToArray();
				var returnType = invokeMethod.ReturnType;

				// For instance methods, first param is the target instance
				bool isInstanceDelegate = paramTypes.Length > 0 && paramTypes[0].IsAssignableFrom(k.DeclaringType);
				var methodParamTypes = isInstanceDelegate ? paramTypes.Skip(1).ToArray() : paramTypes;

				// Determine binding flags based on delegate signature
				var searchFlags = bindingFlags;
				if (isInstanceDelegate)
					searchFlags = (searchFlags | BindingFlags.Instance) & ~BindingFlags.Static;
				else if (paramTypes.Length == methodParamTypes.Length) // No instance param consumed
					searchFlags = (searchFlags | BindingFlags.Static) & ~BindingFlags.Instance;

				// Find the generic method definition
				var method = FindGenericMethod(
					k.DeclaringType,
					k.MethodName,
					searchFlags,
					methodParamTypes,
					returnType,
					requiredGenericArity: 1);

				if (method is null)
					throw new MissingMethodException(k.DeclaringType.FullName, k.MethodName);

				// Close the generic method with runtime type
				var closedMethod = method.MakeGenericMethod(k.GenericArg);

				// Create the delegate
				return closedMethod.CreateDelegate(k.DelegateType);
			});

			return (TDelegate)del;
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
		/// </summary>
		/// <typeparam name="TTarget">The type containing the instance method</typeparam>
		/// <param name="methodName">Name of the generic method to invoke</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="parameterTypes">Array of parameter types for the method (empty for parameterless)</param>
		/// <param name="flags">Binding flags for method lookup (defaults to instance methods)</param>
		/// <returns>Delegate that can be invoked with DynamicInvoke. First arg is the instance.</returns>
		/// <remarks>
		/// Uses Expression.GetDelegateType to dynamically construct the appropriate Action delegate.
		/// Result is cached for performance. Use DynamicInvoke for invocation.
		/// </remarks>
		/// <example>
		/// <code>
		/// // Method: void Process&lt;T&gt;(string name, int count)
		/// var processor = GetOrAllocDynamicAction&lt;MyClass&gt;(
		///     "Process",
		///     typeof(DateTime),
		///     typeof(string), typeof(int));
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
			// Build the complete delegate signature: [TTarget, param1, param2, ..., void]
			// Expression.GetDelegateType expects all parameter types followed by return type
			var allTypes = new[] { typeof(TTarget) }
				.Concat(parameterTypes ?? Type.EmptyTypes)
				.Append(typeof(void))  // Action returns void
				.ToArray();

			// Let Expression.GetDelegateType create the appropriate Action<...> or custom delegate
			// Handles any parameter count, including >16 which exceeds built-in Action types
			var delegateType = Expression.GetDelegateType(allTypes);

			// Reuse the existing GetOrAllocInvoker infrastructure via reflection
			// This maintains all caching benefits while supporting dynamic signatures
			var getMethod = typeof(OpenGenericMethodExecutor)
				.GetMethod(nameof(GetOrAllocInvoker), BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException("Could not find GetOrAllocInvoker method");

			var genericMethod = getMethod.MakeGenericMethod(delegateType);

			// Invoke the generic method to get the cached delegate
			// Cast is safe because GetOrAllocInvoker<T> returns T where T : Delegate
			return (Delegate)genericMethod.Invoke(null, new object[] {
				typeof(TTarget), methodName, genericTypeArgument, flags
			})!;
		}

		/// <summary>
		/// Creates a cached delegate for instance methods with dynamic parameter and return types (Func signature).
		/// Supports any number and type of parameters with a return value.
		/// </summary>
		/// <typeparam name="TTarget">The type containing the instance method</typeparam>
		/// <param name="methodName">Name of the generic method to invoke</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="returnType">Return type of the method</param>
		/// <param name="parameterTypes">Array of parameter types for the method (empty for parameterless)</param>
		/// <param name="flags">Binding flags for method lookup (defaults to instance methods)</param>
		/// <returns>Delegate that can be invoked with DynamicInvoke. First arg is the instance.</returns>
		/// <remarks>
		/// Uses Expression.GetDelegateType to dynamically construct the appropriate Func delegate.
		/// Result is cached for performance. Use DynamicInvoke for invocation and cast the result.
		/// </remarks>
		/// <example>
		/// <code>
		/// // Method: string Calculate&lt;T&gt;(double x, double y)
		/// var calculator = GetOrAllocDynamicFunc&lt;MyClass&gt;(
		///     "Calculate",
		///     typeof(int),
		///     typeof(string),  // return type
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

			// Build the complete delegate signature: [TTarget, param1, param2, ..., TReturn]
			// Expression.GetDelegateType expects all parameter types followed by return type
			var allTypes = new[] { typeof(TTarget) }
				.Concat(parameterTypes ?? Type.EmptyTypes)
				.Append(returnType)
				.ToArray();

			// Let Expression.GetDelegateType create the appropriate Func<...> or custom delegate
			// Handles any parameter count, including >16 which exceeds built-in Func types
			var delegateType = Expression.GetDelegateType(allTypes);

			// Reuse the existing GetOrAllocInvoker infrastructure
			var getMethod = typeof(OpenGenericMethodExecutor)
				.GetMethod(nameof(GetOrAllocInvoker), BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException("Could not find GetOrAllocInvoker method");

			var genericMethod = getMethod.MakeGenericMethod(delegateType);

			// Invoke the generic method to get the cached delegate
			return (Delegate)genericMethod.Invoke(null, new object[] {
				typeof(TTarget), methodName, genericTypeArgument, flags
			})!;
		}

		/// <summary>
		/// Creates a cached delegate for static methods with dynamic parameter and return types.
		/// Supports any number and type of parameters for static methods.
		/// </summary>
		/// <param name="declaringType">Type containing the static method</param>
		/// <param name="methodName">Name of the generic method to invoke</param>
		/// <param name="genericTypeArgument">Runtime type to close the generic method with</param>
		/// <param name="returnType">Return type of the method (use typeof(void) for Action-like behavior)</param>
		/// <param name="parameterTypes">Array of parameter types for the method (empty for parameterless)</param>
		/// <param name="flags">Binding flags for method lookup (defaults to static methods)</param>
		/// <returns>Delegate that can be invoked with DynamicInvoke</returns>
		/// <remarks>
		/// Uses Expression.GetDelegateType to dynamically construct the appropriate delegate.
		/// For void methods, pass typeof(void) as returnType.
		/// Result is cached for performance.
		/// </remarks>
		/// <example>
		/// <code>
		/// // Static method: void Register&lt;T&gt;(string name, int priority)
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

			// For static methods, no instance parameter needed
			// Build signature: [param1, param2, ..., TReturn]
			var allTypes = (parameterTypes ?? Type.EmptyTypes)
				.Append(returnType)
				.ToArray();

			// Let Expression.GetDelegateType handle delegate type creation
			var delegateType = Expression.GetDelegateType(allTypes);

			// Ensure static flags are set correctly
			var staticFlags = (flags | BindingFlags.Static) & ~BindingFlags.Instance;

			// Reuse the existing GetOrAllocInvoker infrastructure
			var getMethod = typeof(OpenGenericMethodExecutor)
				.GetMethod(nameof(GetOrAllocInvoker), BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException("Could not find GetOrAllocInvoker method");

			var genericMethod = getMethod.MakeGenericMethod(delegateType);

			// Invoke the generic method to get the cached delegate
			return (Delegate)genericMethod.Invoke(null, new object[] {
				declaringType, methodName, genericTypeArgument, staticFlags
			})!;
		}

		public static void Dispose() => _cache.Clear();

		private static MethodInfo? FindGenericMethod(
			Type declaringType,
			string methodName,
			BindingFlags flags,
			Type[] parameterTypes,
			Type returnType,
			int requiredGenericArity)
		{
			bool needWalk = (flags & BindingFlags.Instance) != 0 && (flags & BindingFlags.NonPublic) != 0;

			for (Type? t = declaringType; t is not null; t = needWalk ? t.BaseType : null)
			{
				var candidates = t.GetMethods(flags)
					.Where(m => m.Name == methodName && m.IsGenericMethodDefinition)
					.Where(m => m.GetGenericArguments().Length == requiredGenericArity);
				// Return type validation removed - defer to CreateDelegate for covariance support

				foreach (var method in candidates)
				{
					var parameters = method.GetParameters();
					if (parameters.Length != parameterTypes.Length) continue;

					// For generic methods, we cannot reliably validate parameter types
					// because they may contain or reference unresolved generic parameters.
					// Examples that would fail with strict validation:
					// - void Process<T>(List<T> items)
					// - void Map<T>(T[] items)
					// - void Handle<T>(ref T value)
					// - void Copy<T>(Nullable<T> value)
					//
					// We must defer ALL validation to CreateDelegate after MakeGenericMethod
					// closes the generic types. CreateDelegate will throw if incompatible.
					//
					// This is the first method with matching name, generic arity, and param count.
					// If CreateDelegate fails later, the user's delegate signature was incompatible.
					return method;
				}

				if (!needWalk) break;
			}

			return null;
		}
	}
}