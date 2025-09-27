using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace NotNot.Advanced
{
	/// <summary>
	/// High-performance cached invocation of open-generic methods with runtime type arguments.
	/// Supports any delegate signature (Action, Func, custom) with zero allocation after caching.
	/// </summary>
	public static class OpenGenericMethodExecutor2
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
		/// Creates a bound delegate with instance pre-captured.
		/// </summary>
		public static TDelegate GetOrAllocBoundInvoker<TDelegate>(
			object target,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			where TDelegate : Delegate
		{
			if (target is null) throw new ArgumentNullException(nameof(target));

			var targetType = target.GetType();
			var delegateType = typeof(TDelegate);

			// Get the unbound delegate with target as first parameter
			var invokeMethod = delegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var boundParams = invokeMethod.GetParameters().Select(p => p.ParameterType).ToArray();
			var returnType = invokeMethod.ReturnType;

			// Build the unbound delegate type (with target as first param)
			Type unboundDelegateType;
			if (returnType == typeof(void))
			{
				var actionTypes = new[] { targetType }.Concat(boundParams).ToArray();
				unboundDelegateType = actionTypes.Length switch
				{
					1 => typeof(Action<>).MakeGenericType(actionTypes),
					2 => typeof(Action<,>).MakeGenericType(actionTypes),
					3 => typeof(Action<,,>).MakeGenericType(actionTypes),
					4 => typeof(Action<,,,>).MakeGenericType(actionTypes),
					_ => throw new NotSupportedException($"Too many parameters for Action delegate")
				};
			}
			else
			{
				var funcTypes = new[] { targetType }.Concat(boundParams).Append(returnType).ToArray();
				unboundDelegateType = funcTypes.Length switch
				{
					2 => typeof(Func<,>).MakeGenericType(funcTypes),
					3 => typeof(Func<,,>).MakeGenericType(funcTypes),
					4 => typeof(Func<,,,>).MakeGenericType(funcTypes),
					5 => typeof(Func<,,,,>).MakeGenericType(funcTypes),
					_ => throw new NotSupportedException($"Too many parameters for Func delegate")
				};
			}

			// Get the open delegate via reflection to call generic method
			var getMethod = typeof(OpenGenericMethodExecutor2)
				.GetMethod(nameof(GetOrAllocInvoker), BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException("Could not find GetOrAllocInvoker method");

			var genericGetMethod = getMethod.MakeGenericMethod(unboundDelegateType);
			var unboundDelegate = genericGetMethod.Invoke(null, new object[]
			{
				targetType,
				methodName,
				genericTypeArgument,
				bindingFlags | BindingFlags.Instance
			}) as Delegate
				?? throw new InvalidOperationException("Failed to create unbound delegate");

			// Bind the target instance
			return (TDelegate)Delegate.CreateDelegate(delegateType, target, unboundDelegate.Method);
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
					.Where(m => m.GetGenericArguments().Length == requiredGenericArity)
					.Where(m => m.ReturnType == returnType ||
						(m.ReturnType.IsGenericParameter && returnType != typeof(void)));

				foreach (var method in candidates)
				{
					var parameters = method.GetParameters();
					if (parameters.Length != parameterTypes.Length) continue;

					bool matches = true;
					var genericArgs = method.GetGenericArguments();

					for (int i = 0; i < parameters.Length; i++)
					{
						var paramType = parameters[i].ParameterType;
						var expectedType = parameterTypes[i];

						// Handle generic parameter (T)
						if (genericArgs.Contains(paramType))
						{
							// Skip validation - will be checked at MakeGenericMethod
							continue;
						}
						// Handle Span<T>
						else if (paramType.IsConstructedGenericType &&
							paramType.GetGenericTypeDefinition() == typeof(Span<>))
						{
							if (!expectedType.IsConstructedGenericType ||
								expectedType.GetGenericTypeDefinition() != typeof(Span<>) ||
								paramType.GenericTypeArguments[0] != expectedType.GenericTypeArguments[0])
							{
								matches = false;
								break;
							}
						}
						// Handle regular types
						else if (paramType != expectedType)
						{
							matches = false;
							break;
						}
					}

					if (matches) return method;
				}

				if (!needWalk) break;
			}

			return null;
		}
	}
}