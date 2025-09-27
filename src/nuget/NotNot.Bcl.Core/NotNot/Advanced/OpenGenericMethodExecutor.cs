using System;
using System.Linq.Expressions;
using System.Reflection;
using NotNot;

namespace NotNot.Advanced
{
	public static class OpenGenericMethodExecutor
	{
		public static TDelegate CreateInvoker<TDelegate>(
			Type declaringType,
			string methodName,
			Type genericTypeArgument,
			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			where TDelegate : Delegate
		{
			return (TDelegate)CreateDelegate(typeof(TDelegate), declaringType, methodName, genericTypeArgument, bindingFlags);
		}

		public static Action<TTarget> CreateAction<TTarget>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Action<TTarget>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		public static Func<TTarget, TResult> CreateFunc<TTarget, TResult>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Func<TTarget, TResult>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		public static Action<TTarget, Span<TElement>> CreateSpanInvoker<TTarget, TElement>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Action<TTarget, Span<TElement>>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

		public static Func<TTarget, Span<TElement>, TResult> CreateSpanFunc<TTarget, TElement, TResult>(
			string methodName,
			Type genericTypeArgument,
			BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			where TTarget : class
		{
			return CreateInvoker<Func<TTarget, Span<TElement>, TResult>>(
				typeof(TTarget), methodName, genericTypeArgument, flags);
		}

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

		public static void Dispose()
		{
		}

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
