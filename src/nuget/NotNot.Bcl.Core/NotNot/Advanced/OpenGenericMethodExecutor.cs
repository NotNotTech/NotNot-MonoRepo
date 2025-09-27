using System;
using System.Linq.Expressions;
using System.Reflection;
using NotNot;

namespace NotNot.Advanced
{
	/// <summary>
	/// Builds strongly typed delegates that can invoke open generic instance methods once supplied with a concrete type argument.
	/// </summary>
	/// <remarks>
	/// The helpers in this class close a single generic parameter, validate delegate signatures against the resolved method, and refuse static bindings.
	/// </remarks>
	public static class OpenGenericMethodExecutor
	{
		/// <summary>
		/// Creates a delegate of type <typeparamref name="TDelegate"/> that closes and invokes an open generic method on the specified <paramref name="declaringType"/>.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate signature to produce. The first parameter must be assignable from <paramref name="declaringType"/>.</typeparam>
		/// <param name="declaringType">The type that declares the generic method definition.</param>
		/// <param name="methodName">The name of the generic method definition to bind.</param>
		/// <param name="genericTypeArgument">The type used to close the method's single generic argument.</param>
		/// <param name="bindingFlags">Binding flags that control how the method lookup is performed.</param>
		/// <returns>A delegate instance that invokes the resolved method.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="declaringType"/> or <paramref name="genericTypeArgument"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentException">Thrown when <paramref name="methodName"/> is missing or whitespace.</exception>
		/// <exception cref="NotSupportedException">Thrown when the delegate does not represent a supported instance signature or when static binding is requested.</exception>
		/// <exception cref="MissingMethodException">Thrown when no matching generic method definition can be found.</exception>
		public static TDelegate CreateInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			where TDelegate : Delegate
		{
			return (TDelegate)CreateDelegate(typeof(TDelegate), declaringType, methodName, genericTypeArgument, bindingFlags);
		}

		/// <summary>
		/// Creates an <see cref="Action{T}"/> delegate that invokes an open generic instance method on <typeparamref name="TTarget"/>.
		/// </summary>
		/// <typeparam name="TTarget">The concrete type that declares the method to bind.</typeparam>
		/// <param name="methodName">The name of the method to bind.</param>
		/// <param name="genericTypeArgument">The type that replaces the method's single generic parameter.</param>
		/// <param name="flags">Binding flags used to locate the target method.</param>
		/// <returns>An action that invokes the resolved method for an instance of <typeparamref name="TTarget"/>.</returns>
		public static Action<TTarget> CreateAction<TTarget>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Action<TTarget>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates a <see cref="Func{T,TResult}"/> delegate that invokes a generic method on <typeparamref name="TTarget"/> and returns <typeparamref name="TResult"/>.
		/// </summary>
		/// <typeparam name="TTarget">The type that provides the method implementation.</typeparam>
		/// <typeparam name="TResult">The return type produced by the method.</typeparam>
		/// <param name="methodName">The target method name.</param>
		/// <param name="genericTypeArgument">The concrete type that closes the method's generic parameter.</param>
		/// <param name="flags">Binding flags used during method discovery.</param>
		/// <returns>A function delegate that executes the closed generic method on an instance of <typeparamref name="TTarget"/>.</returns>
		public static Func<TTarget, TResult> CreateFunc<TTarget, TResult>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Func<TTarget, TResult>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates an action delegate capable of invoking a generic method that accepts a <see cref="Span{T}"/> argument.
		/// </summary>
		/// <typeparam name="TTarget">The instance type that exposes the method.</typeparam>
		/// <typeparam name="TElement">The element type of the <see cref="Span{T}"/> parameter.</typeparam>
		/// <param name="methodName">The name of the target method.</param>
		/// <param name="genericTypeArgument">The single generic type argument used to close the method.</param>
		/// <param name="flags">Binding flags used to locate the method definition.</param>
		/// <returns>An action that executes the closed method with a span parameter.</returns>
		public static Action<TTarget, Span<TElement>> CreateSpanInvoker<TTarget, TElement>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Action<TTarget, Span<TElement>>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates a function delegate for a generic method that accepts a <see cref="Span{T}"/> and returns <typeparamref name="TResult"/>.
		/// </summary>
		/// <typeparam name="TTarget">The instance type that will be invoked.</typeparam>
		/// <typeparam name="TElement">The element type of the span parameter.</typeparam>
		/// <typeparam name="TResult">The return type of the method.</typeparam>
		/// <param name="methodName">The method name to bind.</param>
		/// <param name="genericTypeArgument">The concrete type argument that closes the method definition.</param>
		/// <param name="flags">Binding flags used during method discovery.</param>
		/// <returns>A function that executes the resolved method and returns a value.</returns>
		public static Func<TTarget, Span<TElement>, TResult> CreateSpanFunc<TTarget, TElement, TResult>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Func<TTarget, Span<TElement>, TResult>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Creates a delegate with a dynamically constructed signature that invokes a generic instance method returning <see langword="void"/>.
		/// </summary>
		/// <typeparam name="TTarget">The declaring type that hosts the method.</typeparam>
		/// <param name="methodName">The name of the generic method to bind.</param>
		/// <param name="genericTypeArgument">The type that closes the method's generic parameter.</param>
		/// <param name="parameterTypes">An ordered span describing the delegate parameter types following the target instance.</param>
		/// <param name="flags">Binding flags that control visibility and inheritance during method lookup.</param>
		/// <returns>A delegate that matches the specified signature and invokes the resolved method.</returns>
		/// <exception cref="NotSupportedException">Thrown when a static binding is requested.</exception>
		public static Delegate CreateDynamicAction<TTarget>(
			string methodName,
			Type genericTypeArgument,
			Span<Type> parameterTypes,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateDynamicDelegate(
				typeof(TTarget),
				typeof(TTarget),
				methodName,
				genericTypeArgument,
				typeof(void),
				parameterTypes,
				flags);
		}

		/// <summary>
		/// Creates a delegate with a dynamically constructed signature that invokes a generic instance method returning <paramref name="returnType"/>.
		/// </summary>
		/// <typeparam name="TTarget">The declaring type that hosts the method.</typeparam>
		/// <param name="methodName">The name of the generic method to bind.</param>
		/// <param name="genericTypeArgument">The type that closes the method's generic parameter.</param>
		/// <param name="returnType">The return type expected from the closed method.</param>
		/// <param name="parameterTypes">An ordered span describing the delegate parameter types following the target instance.</param>
		/// <param name="flags">Binding flags that control visibility and inheritance during method lookup.</param>
		/// <returns>A delegate whose signature matches the supplied parameter and return types.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="returnType"/> is <see langword="null"/>.</exception>
		/// <exception cref="NotSupportedException">Thrown when a static binding is requested.</exception>
		public static Delegate CreateDynamicFunc<TTarget>(
			string methodName,
			Type genericTypeArgument,
			Type returnType,
			Span<Type> parameterTypes,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			if (returnType == null) throw new ArgumentNullException(nameof(returnType));

			return CreateDynamicDelegate(
				typeof(TTarget),
				typeof(TTarget),
				methodName,
				genericTypeArgument,
				returnType,
				parameterTypes,
				flags);
		}

		/// <summary>
		/// Not supported. Static method binding is intentionally disallowed because delegate creation assumes instance targets.
		/// </summary>
		/// <exception cref="NotSupportedException">Always thrown to signal that static methods cannot be bound.</exception>
		public static Delegate CreateDynamicStatic(
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			Type returnType,
			Span<Type> parameterTypes,
			BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
		{
			throw new NotSupportedException("Static method binding is not supported.");
		}



		/// <summary>
		/// Creates a delegate instance using the supplied signature metadata while ensuring only instance methods are bound.
		/// </summary>
		/// <param name="instanceType">The type containing the instance parameter expected by the delegate.</param>
		/// <param name="declaringType">The type that declares the method definition.</param>
		/// <param name="methodName">The name of the method to bind.</param>
		/// <param name="genericTypeArgument">The type used to close the method.</param>
		/// <param name="returnType">The return type expected from the delegate signature.</param>
		/// <param name="parameterTypes">The parameter types (excluding the instance parameter) that compose the delegate signature.</param>
		/// <param name="bindingFlags">Binding flags used during method discovery.</param>
		/// <returns>A delegate that matches the supplied signature.</returns>
		/// <exception cref="NotSupportedException">Thrown when static binding is attempted.</exception>
		private static Delegate CreateDynamicDelegate(
			Type? instanceType,
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			Type returnType,
			Span<Type> parameterTypes,
			BindingFlags bindingFlags)
		{
			if (instanceType is null)
			{
				throw new NotSupportedException("Static method binding is not supported.");
			}

			if ((bindingFlags & BindingFlags.Static) != 0)
			{
				throw new NotSupportedException("Static method binding is not supported.");
			}

			var signatureLength = parameterTypes.Length + 2;
			using var signatureGuard = SpanGuard<Type>.Allocate(signatureLength);
			var signature = signatureGuard.Span;
			var index = 0;

			signature[index++] = instanceType;

			if (!parameterTypes.IsEmpty)
			{
				parameterTypes.CopyTo(signature.Slice(index));
				index += parameterTypes.Length;
			}

			signature[index] = returnType;

			var segment = signatureGuard.DangerousGetArray();
			Type[]? backingArray = segment.Array;
			if (backingArray is null)
			{
				backingArray = GC.AllocateUninitializedArray<Type>(signatureLength);
				signature.CopyTo(backingArray);
			}
			else if (segment.Offset != 0 || backingArray.Length != signatureLength)
			{
				var exact = GC.AllocateUninitializedArray<Type>(signatureLength);
				signature.CopyTo(exact);
				backingArray = exact;
			}

			var delegateType = Expression.GetDelegateType(backingArray);
			var flags = (bindingFlags | BindingFlags.Instance) & ~BindingFlags.Static;

			return CreateDelegate(delegateType, declaringType, methodName, genericTypeArgument, flags);
		}

		/// <summary>
		/// Resolves the generic method definition corresponding to <paramref name="methodName"/> and creates a delegate of <paramref name="delegateType"/>.
		/// </summary>
		/// <param name="delegateType">The delegate type whose signature must match the method.</param>
		/// <param name="declaringType">The type that declares the method definition.</param>
		/// <param name="methodName">The method name to bind.</param>
		/// <param name="genericTypeArgument">The type that closes the generic method parameter.</param>
		/// <param name="bindingFlags">Binding flags used during discovery.</param>
		/// <returns>A delegate that can invoke the closed method.</returns>
		/// <exception cref="ArgumentNullException">Thrown when required arguments are <see langword="null"/>.</exception>
		/// <exception cref="ArgumentException">Thrown when <paramref name="methodName"/> is blank.</exception>
		/// <exception cref="NotSupportedException">Thrown when the delegate does not represent an instance method.</exception>
		/// <exception cref="MissingMethodException">Thrown when the underlying method cannot be located.</exception>
		private static Delegate CreateDelegate(
			Type delegateType,
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((bindingFlags & BindingFlags.Static) != 0) throw new NotSupportedException("Static method binding is not supported.");

			var invokeMethod = delegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			using var parameterGuard = SpanGuard<Type>.Allocate(invokeParameters.Length);
			var parameterSpan = parameterGuard.Span;
			for (int i = 0; i < parameterSpan.Length; i++)
			{
				parameterSpan[i] = invokeParameters[i].ParameterType;
			}

			var isInstanceDelegate = parameterSpan.Length > 0 && parameterSpan[0].IsAssignableFrom(declaringType);
			if (!isInstanceDelegate) throw new NotSupportedException("Delegates must represent instance methods.");
			ReadOnlySpan<Type> methodParameterTypes = parameterSpan.Slice(1);

			var searchFlags = (bindingFlags | BindingFlags.Instance) & ~BindingFlags.Static;

			var method = FindGenericMethod(
				declaringType,
				methodName,
				searchFlags,
				methodParameterTypes,
				requiredGenericArity: 1,
				genericTypeArgument,
				invokeMethod.ReturnType)
				?? throw new MissingMethodException(declaringType.FullName, methodName);

			var closedMethod = method.MakeGenericMethod(genericTypeArgument);
			return closedMethod.CreateDelegate(delegateType);
		}

		/// <summary>
		/// Searches <paramref name="declaringType"/> and optionally its base types for a generic method that matches the provided signature.
		/// </summary>
		/// <param name="declaringType">The type that declares the candidate methods.</param>
		/// <param name="methodName">The name of the method to locate.</param>
		/// <param name="flags">Binding flags controlling visibility and inheritance.</param>
		/// <param name="parameterTypes">The parameter types expected by the closed method.</param>
		/// <param name="requiredGenericArity">The number of generic parameters the method definition must expose.</param>
		/// <param name="genericTypeArgument">The type argument used to close the method during evaluation.</param>
		/// <param name="expectedReturnType">The return type required for a successful match.</param>
		/// <returns>The open generic <see cref="MethodInfo"/> that matches the supplied criteria, or <see langword="null"/> when none is found.</returns>
		private static MethodInfo? FindGenericMethod(
			Type declaringType,
			string methodName,
			BindingFlags flags,
			ReadOnlySpan<Type> parameterTypes,
			int requiredGenericArity,
			Type genericTypeArgument,
			Type expectedReturnType)
		{
			bool needWalk = (flags & BindingFlags.Instance) != 0 && (flags & BindingFlags.NonPublic) != 0;

			for (Type? type = declaringType; type != null; type = needWalk ? type.BaseType : null)
			{
				var methods = type.GetMethods(flags);
				for (int i = 0; i < methods.Length; i++)
				{
					var method = methods[i];
					if (!method.IsGenericMethodDefinition) continue;
					if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) continue;
					if (method.GetGenericArguments().Length != requiredGenericArity) continue;

					MethodInfo closedMethod;
					try
					{
						closedMethod = method.MakeGenericMethod(genericTypeArgument);
					}
					catch (ArgumentException)
					{
						continue;
					}

					var parameters = closedMethod.GetParameters();
					if (parameters.Length != parameterTypes.Length) continue;

					bool parametersMatch = true;
					for (int p = 0; p < parameters.Length; p++)
					{
						if (parameters[p].ParameterType != parameterTypes[p])
						{
							parametersMatch = false;
							break;
						}
					}

					if (!parametersMatch) continue;
					if (closedMethod.ReturnType != expectedReturnType) continue;

					return method;
				}

				if (!needWalk) break;
			}

			return null;
		}
	}
}
