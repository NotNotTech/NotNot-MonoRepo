using System;
using System.Reflection;
using NotNot.Collections.SpanLike;

namespace NotNot.Advanced
{
	/// <summary>
	/// Static method support for OpenGenericMethodExecutor.
	/// Provides parallel API surface for creating delegates from static generic methods.
	/// </summary>
	/// <remarks>
	/// This partial class extends OpenGenericMethodExecutor with static method delegate creation capabilities.
	/// Static methods have different signature validation requirements (no instance parameter) but share
	/// the same constraint validation and exact-match semantics as instance methods.
	/// </remarks>
	public partial class OpenGenericMethodExecutor
	{
		#region Public Static APIs

		/// <summary>
		/// Creates a delegate from a known static MethodInfo, bypassing all search logic.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate type to create.</typeparam>
		/// <param name="method">The static method to create a delegate for (can be generic method definition).</param>
		/// <param name="genericTypeArguments">Type arguments to close the generic method (empty for non-generic).</param>
		/// <returns>A delegate that invokes the specified static method.</returns>
		/// <exception cref="ArgumentNullException">When method or type arguments are null.</exception>
		/// <exception cref="ArgumentException">When type argument count doesn't match method requirements or method is not static.</exception>
		/// <exception cref="InvalidOperationException">When method has open generic parameters or declaring type is open.</exception>
		/// <example>
		/// <code>
		/// var methodDefinition = typeof(MathHelpers)
		///     .GetMethod("Add", BindingFlags.Static | BindingFlags.Public);
		/// var invoker = OpenGenericMethodExecutor.CreateExactStaticInvoker&lt;Func&lt;int, int, int&gt;&gt;(
		///     methodDefinition!,
		///     typeof(int));
		///
		/// var result = invoker(5, 3); // Returns 8
		/// </code>
		/// </example>
		public static TDelegate CreateExactStaticInvoker<TDelegate>(
			MethodInfo method,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			if (method == null) throw new ArgumentNullException(nameof(method));
			if (!method.IsStatic)
				throw new ArgumentException($"Method '{method.Name}' is not static. Use CreateExactInstanceInvoker for instance methods.", nameof(method));
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
			ValidateStaticDelegateSignature(targetMethod, typeof(TDelegate));

			// Create the delegate
			return targetMethod.CreateDelegate<TDelegate>();
		}

		/// <summary>
		/// Creates a delegate of type <typeparamref name="TDelegate"/> that closes and invokes an open generic static method on the specified <paramref name="declaringType"/>.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate signature to produce. All parameters match the static method's parameters.</typeparam>
		/// <param name="declaringType">The type that declares the static generic method definition.</param>
		/// <param name="methodName">The name of the static generic method definition to bind.</param>
		/// <param name="genericTypeArguments">The types used to close the method's generic arguments.</param>
		/// <returns>A delegate instance that invokes the resolved static method.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="declaringType"/> or <paramref name="genericTypeArguments"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentException">Thrown when <paramref name="methodName"/> is missing or whitespace, or declaring type is open.</exception>
		/// <exception cref="NotSupportedException">Thrown when the delegate represents an instance method signature.</exception>
		/// <exception cref="MissingMethodException">Thrown when no matching static generic method definition can be found.</exception>
		/// <example>
		/// <code>
		/// var invoker = OpenGenericMethodExecutor.CreateStaticInvoker&lt;Action&lt;Span&lt;int&gt;&gt;&gt;(
		///     typeof(MathHelpers),
		///     "ProcessValues",
		///     typeof(int));
		///
		/// invoker(mySpan);
		/// </code>
		/// </example>
		public static TDelegate CreateStaticInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
			where TDelegate : Delegate
		{
			return CreateStaticInvoker<TDelegate>(declaringType, methodName,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, genericTypeArguments);
		}

		/// <summary>
		/// Creates a delegate of type <typeparamref name="TDelegate"/> that closes and invokes an open generic static method on the specified <paramref name="declaringType"/> with custom binding flags.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate signature to produce. All parameters match the static method's parameters.</typeparam>
		/// <param name="declaringType">The type that declares the static generic method definition.</param>
		/// <param name="methodName">The name of the static generic method definition to bind.</param>
		/// <param name="bindingFlags">Binding flags that control how the method lookup is performed.</param>
		/// <param name="genericTypeArguments">The types used to close the method's generic arguments.</param>
		/// <returns>A delegate instance that invokes the resolved static method.</returns>
		/// <example>
		/// <code>
		/// var invoker = OpenGenericMethodExecutor.CreateStaticInvoker&lt;Action&lt;Span&lt;int&gt;&gt;&gt;(
		///     typeof(MathHelpers),
		///     "ProcessValues",
		///     BindingFlags.Static | BindingFlags.NonPublic,
		///     typeof(int));
		///
		/// invoker(mySpan);
		/// </code>
		/// </example>
		public static TDelegate CreateStaticInvoker<TDelegate>(
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

			return (TDelegate)CreateStaticDelegate(typeof(TDelegate), declaringType, methodName, genericTypeArguments, bindingFlags);
		}

		/// <summary>
		/// Creates an <see cref="Action"/> delegate that invokes an open generic static method with no parameters.
		/// </summary>
		/// <param name="declaringType">The type that declares the static method to bind.</param>
		/// <param name="methodName">The name of the static method to bind.</param>
		/// <param name="genericTypeArguments">The types that replace the method's generic parameters.</param>
		/// <returns>An action that invokes the resolved static method.</returns>
		/// <example>
		/// <code>
		/// var action = OpenGenericMethodExecutor.CreateStaticAction(
		///     typeof(Logger),
		///     "Log",
		///     typeof(string));
		///
		/// action(); // Invokes Logger.Log&lt;string&gt;()
		/// </code>
		/// </example>
		public static Action CreateStaticAction(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
		{
			return CreateStaticInvoker<Action>(
				declaringType, methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, genericTypeArguments);
		}

		/// <summary>
		/// Creates a <see cref="Func{TResult}"/> delegate that invokes a static generic method with no parameters and returns <typeparamref name="TResult"/>.
		/// </summary>
		/// <typeparam name="TResult">The return type produced by the static method.</typeparam>
		/// <param name="declaringType">The type that declares the static method.</param>
		/// <param name="methodName">The target static method name.</param>
		/// <param name="genericTypeArguments">The concrete types that close the method's generic parameters.</param>
		/// <returns>A function delegate that executes the closed generic static method.</returns>
		/// <example>
		/// <code>
		/// var func = OpenGenericMethodExecutor.CreateStaticFunc&lt;int&gt;(
		///     typeof(Counter),
		///     "GetCount",
		///     typeof(string));
		///
		/// var count = func(); // Returns Counter.GetCount&lt;string&gt;()
		/// </code>
		/// </example>
		public static Func<TResult> CreateStaticFunc<TResult>(
			Type declaringType,
			string methodName,
			params Type[] genericTypeArguments)
		{
			return CreateStaticInvoker<Func<TResult>>(
				declaringType, methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, genericTypeArguments);
		}

		#endregion

		#region Private Implementation Helpers

		/// <summary>
		/// Validates that a delegate type's signature matches a static method.
		/// </summary>
		/// <param name="method">The static method to validate against.</param>
		/// <param name="delegateType">The delegate type to validate.</param>
		/// <exception cref="InvalidOperationException">When the delegate type is invalid.</exception>
		/// <exception cref="NotSupportedException">When signatures don't match.</exception>
		private static void ValidateStaticDelegateSignature(MethodInfo method, Type delegateType)
		{
			if (!method.IsStatic)
				throw new ArgumentException($"Method '{method.Name}' is not static.", nameof(method));

			var invokeMethod = delegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			var methodParameters = method.GetParameters();

			// For static methods, all delegate parameters match method parameters directly
			if (invokeParameters.Length != methodParameters.Length)
				throw new NotSupportedException(
					$"Delegate parameter count mismatch. Expected {methodParameters.Length} but got {invokeParameters.Length}");

			// Verify each parameter type matches exactly (no variance)
			for (int i = 0; i < methodParameters.Length; i++)
			{
				if (invokeParameters[i].ParameterType != methodParameters[i].ParameterType)
					throw new NotSupportedException(
						$"Parameter type mismatch at position {i}: " +
						$"expected {methodParameters[i].ParameterType} but got {invokeParameters[i].ParameterType}");
			}

			// Verify return type matches exactly
			if (invokeMethod.ReturnType != method.ReturnType)
				throw new NotSupportedException(
					$"Return type mismatch: expected {method.ReturnType} but got {invokeMethod.ReturnType}");
		}

		/// <summary>
		/// Resolves the static generic method definition corresponding to <paramref name="methodName"/> and creates a delegate of <paramref name="delegateType"/>.
		/// </summary>
		/// <param name="delegateType">The delegate type whose signature must match the method.</param>
		/// <param name="declaringType">The type that declares the method definition.</param>
		/// <param name="methodName">The method name to bind.</param>
		/// <param name="genericTypeArguments">The types that close the generic method parameters.</param>
		/// <param name="bindingFlags">Binding flags used during discovery.</param>
		/// <returns>A delegate that can invoke the closed static method.</returns>
		/// <exception cref="ArgumentNullException">Thrown when required arguments are <see langword="null"/>.</exception>
		/// <exception cref="ArgumentException">Thrown when <paramref name="methodName"/> is blank or declaring type is open.</exception>
		/// <exception cref="NotSupportedException">Thrown when attempting to bind instance method with static API.</exception>
		/// <exception cref="MissingMethodException">Thrown when the underlying method cannot be located.</exception>
		private static Delegate CreateStaticDelegate(
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
			if ((bindingFlags & BindingFlags.Instance) != 0)
				throw new NotSupportedException("Instance method binding not supported by static API. Use CreateInstanceInvoker instead.");

			var invokeMethod = delegateType.GetMethod("Invoke")
				?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			using var parameterGuard = RentedMem<Type>.Allocate(invokeParameters.Length);
			var parameterSpan = parameterGuard.Span;
			for (int i = 0; i < parameterSpan.Length; i++)
			{
				parameterSpan[i] = invokeParameters[i].ParameterType;
			}

			// For static methods, all delegate parameters match method parameters directly
			ReadOnlySpan<Type> methodParameterTypes = parameterSpan;

			var searchFlags = (bindingFlags | BindingFlags.Static) & ~BindingFlags.Instance;

			var method = FindStaticGenericMethod(
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
		/// Searches <paramref name="declaringType"/> and optionally its base types for a static generic method that matches the provided signature.
		/// </summary>
		/// <param name="declaringType">The type that declares the candidate methods.</param>
		/// <param name="methodName">The name of the method to locate.</param>
		/// <param name="flags">Binding flags controlling visibility and inheritance.</param>
		/// <param name="parameterTypes">The parameter types expected by the closed method.</param>
		/// <param name="requiredGenericArity">The number of generic parameters the method definition must expose.</param>
		/// <param name="genericTypeArguments">The type arguments used to close the method during evaluation.</param>
		/// <param name="expectedReturnType">The return type required for a successful match.</param>
		/// <returns>The open generic <see cref="MethodInfo"/> that matches the supplied criteria, or <see langword="null"/> when none is found.</returns>
		private static MethodInfo? FindStaticGenericMethod(
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
					if (!method.IsStatic) continue; // Skip instance methods
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
					// For static methods, all delegate parameters match method parameters directly (no instance offset)
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
					$"Static generic method '{methodName}' found but constraints not satisfied for types '{typeList}': {firstConstraintFailure.Message}",
					firstConstraintFailure);
			}

			return null;
		}

		#endregion
	}
}
