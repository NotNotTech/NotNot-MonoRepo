using System;
using System.Linq.Expressions;
using System.Reflection;
using NotNot;
using NotNot.Collections.SpanLike;

namespace NotNot.Advanced
{


	/// <summary>
	/// Builds strongly typed delegates that can invoke open generic instance methods once supplied with a concrete type argument.
	/// The purpose of OpenGenericMethodExecutor is to allow the user to exactly match existing generic methods using known types.
	/// This means the user should exactly specify their target method.
	/// </summary>
	/// <remarks>
	/// The helpers in this class close a single generic parameter, validate delegate signatures against the resolved method, and refuse static bindings.
	/// IMPORTANT: This implementation requires exact type matching - no variance is supported. The delegate's parameter and return types
	/// must exactly match the closed method's signature.
	/// <para>This partial class contains static methods, which DO NOT cache, just focus on the logic of creating the delegates</para>
	/// </remarks>
	public partial class OpenGenericMethodExecutor
	{
		/// <summary>
		/// Creates a delegate of type <typeparamref name="TDelegate"/> that closes and invokes an open generic method on the specified <paramref name="declaringType"/>.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate signature to produce. The first parameter must be assignable from <paramref name="declaringType"/>.</typeparam>
		/// <param name="declaringType">The type that declares the generic method definition.</param>
		/// <param name="methodName">The name of the generic method definition to bind.</param>
		/// <param name="genericTypeArguments">The type used to close the method's single generic argument.</param>
		/// <param name="bindingFlags">Binding flags that control how the method lookup is performed.</param>
		/// <returns>A delegate instance that invokes the resolved method.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="declaringType"/> or <paramref name="genericTypeArguments"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentException">Thrown when <paramref name="methodName"/> is missing or whitespace.</exception>
		/// <exception cref="NotSupportedException">Thrown when the delegate does not represent a supported instance signature or when static binding is requested.</exception>
		/// <exception cref="MissingMethodException">Thrown when no matching generic method definition can be found.</exception>
		/// <example>
		/// <code>
		/// var invoker = OpenGenericMethodExecutor.CreateInvoker&lt;Action&lt;ComponentDataPartition, Span&lt;SlotHandle&gt;&gt;&gt;(
		///     typeof(ComponentDataPartition),
		///     "_OnAlloc",
		///     typeof(PositionComponent));
		///
		/// invoker(partitionInstance, slotSpan);
		/// </code>
		/// </example>
		public static TDelegate CreateInstanceInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			return CreateInstanceInvoker<TDelegate>(declaringType, methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, genericTypeArguments);
		}

		/// <example>
		/// <code>
		/// var invoker = OpenGenericMethodExecutor.CreateInvoker&lt;Action&lt;ComponentDataPartition, Span&lt;SlotHandle&gt;&gt;&gt;(
		///     typeof(ComponentDataPartition),
		///     "_OnAlloc",
		///     BindingFlags.Instance | BindingFlags.NonPublic,
		///     typeof(PositionComponent));
		///
		/// invoker(partitionInstance, slotSpan);
		/// </code>
		/// </example>
		public static TDelegate CreateInstanceInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			BindingFlags bindingFlags,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
			if (declaringType.ContainsGenericParameters)
				throw new ArgumentException(
					$"Declaring type '{declaringType}' contains open generic parameters. " +
					"Provide a fully closed generic type instead.", nameof(declaringType));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Method name is required", nameof(methodName));
			ValidateTypeArguments(genericTypeArguments);

			return (TDelegate)CreateDelegate(typeof(TDelegate), declaringType, methodName, genericTypeArguments, bindingFlags);
		}

		/// <summary>
		/// Creates a delegate from a known MethodInfo, bypassing all search logic.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="method">The method to create a delegate for (can be generic method definition).</param>
		/// <param name="genericTypeArguments">Type arguments to close the generic method (empty for non-generic).</param>
		/// <returns>A delegate that invokes the specified method.</returns>
		/// <exception cref="ArgumentNullException">When method or type arguments are null.</exception>
		/// <exception cref="ArgumentException">When type argument count doesn't match method requirements.</exception>
		/// <exception cref="InvalidOperationException">When method has open generic parameters or declaring type is open.</exception>
		/// <exception cref="NotSupportedException">When attempting to bind a static method.</exception>
		/// <example>
		/// <code>
		/// var methodDefinition = typeof(ComponentDataPartition)
		///     .GetMethod("_OnAlloc", BindingFlags.Instance | BindingFlags.NonPublic);
		/// var invoker = OpenGenericMethodExecutor.CreateExactInvoker&lt;Action&lt;ComponentDataPartition, Span&lt;SlotHandle&gt;&gt;&gt;(
		///     methodDefinition!,
		///     typeof(PositionComponent));
		///
		/// invoker(partitionInstance, slotSpan);
		/// </code>
		/// </example>
		public static TDelegate CreateExactInstanceInvoker<TDelegate>(
			MethodInfo method,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			if (method == null) throw new ArgumentNullException(nameof(method));
			ValidateTypeArguments(genericTypeArguments);

			// Handle generic, already-closed, and non-generic methods
			MethodInfo targetMethod;
			if (method.ContainsGenericParameters)
			{
				// Open generic method OR already-constructed generic with open type parameters
				if (!method.IsGenericMethodDefinition)
				{
					throw new InvalidOperationException(
						"Method is a constructed generic that still contains open type parameters. " +
						"Pass the generic method definition instead.");
				}
				if (method.GetGenericArguments().Length != genericTypeArguments.Length)
					throw new ArgumentException($"Method requires {method.GetGenericArguments().Length} type arguments");

				// Validate constraints before attempting to close the method
				ValidateGenericConstraints(method, genericTypeArguments);
				targetMethod = method.MakeGenericMethod(genericTypeArguments);
			}
			else if (genericTypeArguments.Length == 0)
			{
				targetMethod = method; // Already closed or non-generic, use as-is
			}
			else
			{
				throw new ArgumentException("Method is already closed and cannot accept additional type arguments");
			}

			// Validate instance-only invariant (reject static methods)
			if (targetMethod.IsStatic)
			{
				throw new NotSupportedException("Static method binding is not supported.");
			}

			// Validate declaring type is fully closed
			if (targetMethod.DeclaringType?.ContainsGenericParameters == true)
			{
				throw new InvalidOperationException(
					$"Cannot create delegate for method '{targetMethod.Name}' on open generic type '{targetMethod.DeclaringType}'. " +
					"The declaring type must be fully closed.");
			}
			if (targetMethod.ContainsGenericParameters)
			{
				throw new InvalidOperationException(
					$"Method '{targetMethod.Name}' still contains open generic parameters after applying type arguments.");
			}

			// Validate delegate signature matches target method signature
			ValidateDelegateSignature(targetMethod, typeof(TDelegate));

			// Create the delegate
			return targetMethod.CreateDelegate<TDelegate>();
		}

		/// <summary>
		/// Creates an <see cref="Action{T}"/> delegate that invokes an open generic instance method on <typeparamref name="TTarget"/>.
		/// </summary>
		/// <typeparam name="TTarget">The concrete type that declares the method to bind.</typeparam>
		/// <param name="methodName">The name of the method to bind.</param>
		/// <param name="genericTypeArguments">The type that replaces the method's single generic parameter.</param>
		/// <param name="flags">Binding flags used to locate the target method.</param>
		/// <returns>An action that invokes the resolved method for an instance of <typeparamref name="TTarget"/>.</returns>
		/// <example>
		/// <code>
		/// var action = OpenGenericMethodExecutor.CreateAction&lt;ComponentDataPartition&gt;(
		///     "ResetComponentState",
		///     typeof(PositionComponent));
		///
		/// action(partitionInstance);
		/// </code>
		/// </example>
		public static Action<TTarget> CreateInstanceAction<TTarget>(
			string methodName,
			params Type[] genericTypeArguments)
			where TTarget : class
		{
			return CreateInstanceInvoker<Action<TTarget>>(
				typeof(TTarget), methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, genericTypeArguments);
		}

		/// <summary>
		/// Creates a <see cref="Func{T,TResult}"/> delegate that invokes a generic method on <typeparamref name="TTarget"/> and returns <typeparamref name="TResult"/>.
		/// </summary>
		/// <typeparam name="TTarget">The type that provides the method implementation.</typeparam>
		/// <typeparam name="TResult">The return type produced by the method.</typeparam>
		/// <param name="methodName">The target method name.</param>
		/// <param name="genericTypeArguments">The concrete type that closes the method's generic parameter.</param>
		/// <param name="flags">Binding flags used during method discovery.</param>
		/// <returns>A function delegate that executes the closed generic method on an instance of <typeparamref name="TTarget"/>.</returns>
		/// <example>
		/// <code>
		/// var func = OpenGenericMethodExecutor.CreateFunc&lt;ComponentDataPartition, int&gt;(
		///     "CountComponents",
		///     typeof(PositionComponent));
		///
		/// var count = func(partitionInstance);
		/// </code>
		/// </example>
		public static Func<TTarget, TResult> CreateInstanceFunc<TTarget, TResult>(
			string methodName,
			params Type[] genericTypeArguments)
			where TTarget : class
		{
			return CreateInstanceInvoker<Func<TTarget, TResult>>(
				typeof(TTarget), methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, genericTypeArguments);
		}

		// APIs removed - CreateSpanInvoker, CreateSpanFunc, CreateDynamicAction, CreateDynamicFunc, CreateDynamicStatic, CreateDynamicDelegate
		// These APIs contradicted exact-match semantics by inferring signatures or making assumptions

		/// <summary>
		/// Validates that all type arguments in the array are non-null.
		/// </summary>
		/// <param name="genericTypeArguments">The type arguments to validate.</param>
		/// <exception cref="ArgumentNullException">When the array is null or contains null elements.</exception>
		private static void ValidateTypeArguments(Type[] genericTypeArguments)
		{
			if (genericTypeArguments == null)
				throw new ArgumentNullException(nameof(genericTypeArguments));

			for (int i = 0; i < genericTypeArguments.Length; i++)
			{
				if (genericTypeArguments[i] == null)
					throw new ArgumentNullException(
						$"{nameof(genericTypeArguments)}[{i}]",
						"Type arguments cannot be null");
			}
		}

		/// <summary>
		/// Validates that type arguments satisfy the generic constraints of a method.
		/// </summary>
		/// <param name="method">The generic method definition.</param>
		/// <param name="typeArguments">The type arguments to validate.</param>
		/// <exception cref="ArgumentException">When type arguments violate method constraints.</exception>
		private static void ValidateGenericConstraints(MethodInfo method, Type[] typeArguments)
		{
			var genericParams = method.GetGenericArguments();
			if (genericParams.Length != typeArguments.Length)
				return; // Arity mismatch handled elsewhere

			for (int i = 0; i < genericParams.Length; i++)
			{
				var param = genericParams[i];
				var arg = typeArguments[i];
				var constraints = param.GetGenericParameterConstraints();

				// Check class/struct/new() constraints
				var attrs = param.GenericParameterAttributes;
				if ((attrs & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
				{
					if (!arg.IsValueType || (Nullable.GetUnderlyingType(arg) != null))
						throw new ArgumentException(
							$"Type argument '{arg.Name}' for generic parameter '{param.Name}' must be a non-nullable value type (struct constraint).");
				}
				if ((attrs & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
				{
					if (arg.IsValueType)
						throw new ArgumentException(
							$"Type argument '{arg.Name}' for generic parameter '{param.Name}' must be a reference type (class constraint).");
				}
				if ((attrs & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
				{
					if (!arg.IsValueType && arg.GetConstructor(Type.EmptyTypes) == null)
						throw new ArgumentException(
							$"Type argument '{arg.Name}' for generic parameter '{param.Name}' must have a parameterless constructor (new() constraint).");
				}

				// Check interface/base class constraints
				foreach (var constraint in constraints)
				{
					if (!constraint.IsAssignableFrom(arg))
						throw new ArgumentException(
							$"Type argument '{arg.Name}' does not satisfy constraint '{constraint.Name}' for generic parameter '{param.Name}'.");
				}
			}
		}

		/// <summary>
		/// Resolves the generic method definition corresponding to <paramref name="methodName"/> and creates a delegate of <paramref name="delegateType"/>.
		/// </summary>
		/// <param name="delegateType">The delegate type whose signature must match the method.</param>
		/// <param name="declaringType">The type that declares the method definition.</param>
		/// <param name="methodName">The method name to bind.</param>
		/// <param name="genericTypeArguments">The type that closes the generic method parameter.</param>
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
			Type[] genericTypeArguments,
			BindingFlags bindingFlags)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (declaringType.ContainsGenericParameters)
				throw new ArgumentException(
					$"Declaring type '{declaringType}' contains open generic parameters. " +
					"Provide a fully closed generic type instead.", nameof(declaringType));
			ValidateTypeArguments(genericTypeArguments);
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Method name is required", nameof(methodName));
			if ((bindingFlags & BindingFlags.Static) != 0) throw new NotSupportedException("Static method binding is not supported.");

			var invokeMethod = delegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			using var parameterGuard = RentedMem<Type>.Allocate(invokeParameters.Length);
			var parameterSpan = parameterGuard.GetSpan();
			for (int i = 0; i < parameterSpan.Length; i++)
			{
				parameterSpan[i] = invokeParameters[i].ParameterType;
			}

			var isInstanceDelegate = parameterSpan.Length > 0 && declaringType.IsAssignableFrom(parameterSpan[0]);
			if (!isInstanceDelegate) throw new NotSupportedException("Delegates must represent instance methods.");
			ReadOnlySpan<Type> methodParameterTypes = parameterSpan.Slice(1);

			var searchFlags = (bindingFlags | BindingFlags.Instance) & ~BindingFlags.Static;

			var method = FindGenericMethod(
				declaringType,
				methodName,
				searchFlags,
				methodParameterTypes,
				genericTypeArguments.Length,
				genericTypeArguments,
				invokeMethod.ReturnType)
				?? throw new MissingMethodException(declaringType.FullName, methodName);

			var closedMethod = method.MakeGenericMethod(genericTypeArguments);
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
		/// <param name="genericTypeArguments">The type argument used to close the method during evaluation.</param>
		/// <param name="expectedReturnType">The return type required for a successful match.</param>
		/// <returns>The open generic <see cref="MethodInfo"/> that matches the supplied criteria, or <see langword="null"/> when none is found.</returns>
		private static MethodInfo? FindGenericMethod(
			Type declaringType,
			string methodName,
			BindingFlags flags,
			ReadOnlySpan<Type> parameterTypes,
			int requiredGenericArity,
			Type[] genericTypeArguments,
			Type expectedReturnType)
		{
			bool walkBaseTypes = (flags & BindingFlags.DeclaredOnly) == 0;
			BindingFlags perTypeFlags = flags | BindingFlags.DeclaredOnly;
			ArgumentException? firstConstraintFailure = null;

			for (Type? type = declaringType; type != null; type = walkBaseTypes ? type.BaseType : null)
			{
				var methods = type.GetMethods(perTypeFlags);
				for (int i = 0; i < methods.Length; i++)
				{
					var method = methods[i];
					if (!method.IsGenericMethodDefinition) continue;
					if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) continue;
					if (method.GetGenericArguments().Length != requiredGenericArity) continue;

					MethodInfo closedMethod;
					try
					{
						// Pre-validate constraints for clearer error messages
						ValidateGenericConstraints(method, genericTypeArguments);
						closedMethod = method.MakeGenericMethod(genericTypeArguments);
					}
					catch (ArgumentException ex)
					{
						// Capture first constraint failure for reporting
						firstConstraintFailure ??= ex;
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

					// Found exact match - return immediately
					return method;
				}

			}

			// If we had constraint failures, report them
			if (firstConstraintFailure != null)
			{
				string typeList = string.Join(", ", Array.ConvertAll(genericTypeArguments, t => t.Name));
				throw new InvalidOperationException(
					$"Generic method '{methodName}' found but constraints not satisfied for types '{typeList}': {firstConstraintFailure.Message}",
					firstConstraintFailure);
			}

			return null;
		}

		/// <summary>
		/// Validates that a delegate type's signature matches the target method.
		/// </summary>
		/// <param name="method">The method to validate against.</param>
		/// <param name="delegateType">The delegate type to validate.</param>
		/// <exception cref="InvalidOperationException">When the delegate type is invalid.</exception>
		/// <exception cref="NotSupportedException">When signatures don't match for instance methods.</exception>
		private static void ValidateDelegateSignature(MethodInfo method, Type delegateType)
		{
			var invokeMethod = delegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			var methodParameters = method.GetParameters();

			// For instance methods, first parameter should be the instance type
			if (!method.IsStatic)
			{
				if (invokeParameters.Length != methodParameters.Length + 1)
					throw new NotSupportedException(
						$"Delegate parameter count mismatch. Expected {methodParameters.Length + 1} but got {invokeParameters.Length}");

				// Verify first parameter is compatible with declaring type
				// The delegate's first parameter type must be assignable from the declaring type
				// Example: if method is on Animal, delegate can accept Animal or Dog : Animal
				if (invokeParameters.Length > 0 && method.DeclaringType != null)
				{
					if (!invokeParameters[0].ParameterType.IsAssignableFrom(method.DeclaringType))
						throw new NotSupportedException(
							$"Delegate's first parameter type {invokeParameters[0].ParameterType} " +
							$"cannot accept instances of declaring type {method.DeclaringType}");
				}

				// Verify remaining parameters match
				for (int i = 0; i < methodParameters.Length; i++)
				{
					if (invokeParameters[i + 1].ParameterType != methodParameters[i].ParameterType)
						throw new NotSupportedException(
							$"Parameter type mismatch at position {i}: " +
							$"expected {methodParameters[i].ParameterType} but got {invokeParameters[i + 1].ParameterType}");
				}
			}

			// Verify return type matches
			if (invokeMethod.ReturnType != method.ReturnType)
				throw new NotSupportedException(
					$"Return type mismatch: expected {method.ReturnType} but got {invokeMethod.ReturnType}");
		}
	}
}
