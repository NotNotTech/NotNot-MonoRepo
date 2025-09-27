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