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
			 BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
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
			 Type[] parameterTypes,
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
			 Type[] parameterTypes,
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
			 Type[] parameterTypes,
			 BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
		{
			if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
			if (returnType == null) throw new ArgumentNullException(nameof(returnType));

			return CreateDynamicDelegate(
				 null,
				 declaringType,
				 methodName,
				 genericTypeArgument,
				 returnType,
				 parameterTypes,
				 flags);
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
			//var args = parameterTypes ?? Array.Empty<Type>();
			var args = parameterTypes;
			var signatureLength = args.Length + (instanceType is null ? 1 : 2);
			using var signatureGuard = SpanGuard<Type>.Allocate(signatureLength);// new Type[signatureLength];
			var signature = signatureGuard.Span;
			var index = 0;

			if (instanceType is not null)
			{
				signature[index++] = instanceType;
			}

			if (args.Length > 0)
			{
				//Array.Copy(args, 0, signature, index, args.Length);
				args.CopyTo(signature.Slice(index));

				index += args.Length;
			}

			signature[index] = returnType;

			var delegateType = Expression.GetDelegateType(signatureGuard.DangerousGetArray().Array);
			var flags = instanceType is null
				 ? (bindingFlags | BindingFlags.Static) & ~BindingFlags.Instance
				 : bindingFlags;

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

			var invokeMethod = delegateType.GetMethod("Invoke")
				 ?? throw new InvalidOperationException($"{delegateType} is not a valid delegate type");

			var invokeParameters = invokeMethod.GetParameters();
			var parameterTypes = new Type[invokeParameters.Length];
			for (int i = 0; i < parameterTypes.Length; i++)
			{
				parameterTypes[i] = invokeParameters[i].ParameterType;
			}

			Type[] methodParameterTypes;
			var isInstanceDelegate = parameterTypes.Length > 0 && parameterTypes[0].IsAssignableFrom(declaringType);
			if (isInstanceDelegate)
			{
				methodParameterTypes = new Type[parameterTypes.Length - 1];
				Array.Copy(parameterTypes, 1, methodParameterTypes, 0, methodParameterTypes.Length);
			}
			else
			{
				methodParameterTypes = parameterTypes;
			}

			var searchFlags = bindingFlags;
			if (isInstanceDelegate)
			{
				searchFlags = (searchFlags | BindingFlags.Instance) & ~BindingFlags.Static;
			}
			else if (parameterTypes.Length == methodParameterTypes.Length)
			{
				searchFlags = (searchFlags | BindingFlags.Static) & ~BindingFlags.Instance;
			}

			var method = FindGenericMethod(
				 declaringType,
				 methodName,
				 searchFlags,
				 methodParameterTypes,
				 requiredGenericArity: 1)
				 ?? throw new MissingMethodException(declaringType.FullName, methodName);

			var closedMethod = method.MakeGenericMethod(genericTypeArgument);
			return closedMethod.CreateDelegate(delegateType);
		}

		private static MethodInfo? FindGenericMethod(
			 Type declaringType,
			 string methodName,
			 BindingFlags flags,
			 Type[] parameterTypes,
			 int requiredGenericArity)
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

					var parameters = method.GetParameters();
					if (parameters.Length != parameterTypes.Length) continue;

					return method;
				}

				if (!needWalk) break;
			}

			return null;
		}
	}
}
