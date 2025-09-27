using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace NotNot.Advanced
{
	/// <summary>
	/// Provides cached, strongly-typed delegates for invoking open-generic methods at runtime
	/// when the generic type argument is only known as a <see cref="Type"/>.
	/// <para/>
	/// Supports instance/static methods with the following shapes:
	/// <list type="bullet">
	///   <item><description><c>void M&lt;TGen&gt;()</c></description></item>
	///   <item><description><c>void M&lt;TGen&gt;(TElement)</c></description></item>
	///   <item><description><c>void M&lt;TGen&gt;(TArg1, TArg2)</c></description></item>
	///   <item><description><c>void M&lt;TGen&gt;(Span&lt;TElement&gt;)</c></description></item>
	/// </list>
	/// Avoids <see cref="MethodBase.Invoke(object?, object?[])"/> to prevent boxing and improve performance,
	/// making it safe for use with <see cref="Span{T}"/> and other value types.
	/// </summary>
	public static class OpenGenericMethodExecutor
	{
		// ---- cache shape ----------------------------------------------------

		private enum ParamKind { InstanceSpan, InstanceValue, StaticSpan, StaticValue, InstanceValue_0, InstanceValue_2, StaticValue_0, StaticValue_2 } // parameter & dispatch classification

		private readonly record struct CacheKey(
			 Type DeclaringType,        // the type that declares the method
			 string Name,               // method name
			 Type GenericArg,           // runtime generic type to close with
			 ParamKind Kind,            // shape classification
			 Type? TargetType,          // instance target type (null for static)
			 Type? ElementType,         // Span<TElement> / TElement / TArg1
			 Type? ElementType2);       // second argument for 2-arg methods

		private static readonly ConcurrentDictionary<CacheKey, Delegate> _cache = new();

		// ---- public API : instance (0 arguments) ----------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>instance</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;()</c>.
		/// Returns an open-instance delegate: <c>(TTarget) =&gt; void</c>.
		/// </summary>
		public static Action<TTarget> GetOrAllocValueInvoker<TTarget>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Instance) == 0) throw new ArgumentException("Use instance BindingFlags.", nameof(flags));
			if (!declaringType.IsAssignableFrom(typeof(TTarget)))
				throw new ArgumentException($"{typeof(TTarget)} is not assignable to {declaringType}.", nameof(TTarget));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.InstanceValue_0, typeof(TTarget), null, null);

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: Type.EmptyTypes,
					 requireGenericDef: true, requiredGenericArity: 1);

				var closed = def.MakeGenericMethod(k.GenericArg);

				var delType = typeof(Action<>).MakeGenericType(k.TargetType!);
				return closed.CreateDelegate(delType);
			});

			return (Action<TTarget>)del;
		}

		/// <summary>
		/// Bound-instance convenience for <c>void M&lt;TGen&gt;()</c>.
		/// Returns <c>() =&gt; void</c> with <paramref name="target"/> pre-bound.
		/// </summary>
		public static Action GetOrAllocBoundValueInvoker<TTarget>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 TTarget target,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (target is null) throw new ArgumentNullException(nameof(target));

			var openDelegate = GetOrAllocValueInvoker<TTarget>(declaringType, methodName, genericTypeArgument, flags);
			return (Action)Delegate.CreateDelegate(typeof(Action), target, openDelegate.Method);
		}

		// ---- public API : instance (Span<TElement>) -------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>instance</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;(Span&lt;TElement&gt;)</c>.
		/// Returns an open-instance delegate: <c>(TTarget, Span&lt;TElement&gt;) =&gt; void</c>.
		/// </summary>
		public static Action<TTarget, Span<TElement>> GetOrAllocSpanInvoker<TTarget, TElement>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));          // guard: null
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Instance) == 0) throw new ArgumentException("Use instance BindingFlags.", nameof(flags));
			if (!declaringType.IsAssignableFrom(typeof(TTarget)))
				throw new ArgumentException($"{typeof(TTarget)} is not assignable to {declaringType}.", nameof(TTarget)); // ensure target fits

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.InstanceSpan, typeof(TTarget), typeof(TElement), null);

			var del = _cache.GetOrAdd(key, k =>
			{
				// resolve method definition (walk bases for non-public instance)
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: new[] { typeof(Span<TElement>) },
					 requireGenericDef: true, requiredGenericArity: 1);

				// sanity: parameter must be Span<TElement>
				var p = def.GetParameters()[0].ParameterType;
				if (!(p.IsConstructedGenericType && p.GetGenericTypeDefinition() == typeof(Span<>) && p.GenericTypeArguments[0] == k.ElementType))
					throw new InvalidOperationException($"Method '{k.Name}' must accept Span<{k.ElementType.Name}>.");

				// close <TGen> with runtime type
				var closed = def.MakeGenericMethod(k.GenericArg);

				// produce open-instance delegate: (TTarget, Span<TElement>) -> void
				var delType = typeof(Action<,>).MakeGenericType(k.TargetType!, typeof(Span<>).MakeGenericType(k.ElementType!));
				return closed.CreateDelegate(delType);
			});

			return (Action<TTarget, Span<TElement>>)del;                                                // cast safe by construction
		}

		/// <summary>
		/// Bound-instance convenience for <c>void M&lt;TGen&gt;(Span&lt;TElement&gt;)</c>.
		/// Returns <c>Span&lt;TElement&gt; =&gt; void</c> with <paramref name="target"/> pre-bound.
		/// </summary>
		public static Action<Span<TElement>> GetOrAllocBoundSpanInvoker<TTarget, TElement>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 TTarget target,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (target is null) throw new ArgumentNullException(nameof(target));

			// Reuse open-instance build, then bind 'this' via the CreateDelegate(instance) overload to avoid closure alloc.
			var openDelegate = GetOrAllocSpanInvoker<TTarget, TElement>(declaringType, methodName, genericTypeArgument, flags);
			return (Action<Span<TElement>>)Delegate.CreateDelegate(typeof(Action<Span<TElement>>), target, openDelegate.Method);
		}

		// ---- public API : instance (TElement) -------------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>instance</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;(TElement)</c>.
		/// Returns an open-instance delegate: <c>(TTarget, TElement) =&gt; void</c>.
		/// </summary>
		public static Action<TTarget, TElement> GetOrAllocValueInvoker<TTarget, TElement>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Instance) == 0) throw new ArgumentException("Use instance BindingFlags.", nameof(flags));
			if (!declaringType.IsAssignableFrom(typeof(TTarget)))
				throw new ArgumentException($"{typeof(TTarget)} is not assignable to {declaringType}.", nameof(TTarget));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.InstanceValue, typeof(TTarget), typeof(TElement), null);

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: new[] { k.ElementType! },
					 requireGenericDef: true, requiredGenericArity: 1);

				if (def.GetParameters()[0].ParameterType != k.ElementType)
					throw new InvalidOperationException($"Method '{k.Name}' must accept {k.ElementType.Name}.");

				var closed = def.MakeGenericMethod(k.GenericArg);

				var delType = typeof(Action<,>).MakeGenericType(k.TargetType!, k.ElementType!);
				return closed.CreateDelegate(delType);
			});

			return (Action<TTarget, TElement>)del;
		}

		/// <summary>
		/// Bound-instance convenience for <c>void M&lt;TGen&gt;(TElement)</c>.
		/// Returns <c>TElement =&gt; void</c> with <paramref name="target"/> pre-bound.
		/// </summary>
		public static Action<TElement> GetOrAllocBoundValueInvoker<TTarget, TElement>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 TTarget target,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (target is null) throw new ArgumentNullException(nameof(target));

			var openDelegate = GetOrAllocValueInvoker<TTarget, TElement>(declaringType, methodName, genericTypeArgument, flags);
			return (Action<TElement>)Delegate.CreateDelegate(typeof(Action<TElement>), target, openDelegate.Method);
		}

		// ---- public API : instance (TArg1, TArg2) ---------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>instance</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;(TArg1, TArg2)</c>.
		/// Returns an open-instance delegate: <c>(TTarget, TArg1, TArg2) =&gt; void</c>.
		/// </summary>
		public static Action<TTarget, TArg1, TArg2> GetOrAllocValueInvoker<TTarget, TArg1, TArg2>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Instance) == 0) throw new ArgumentException("Use instance BindingFlags.", nameof(flags));
			if (!declaringType.IsAssignableFrom(typeof(TTarget)))
				throw new ArgumentException($"{typeof(TTarget)} is not assignable to {declaringType}.", nameof(TTarget));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.InstanceValue_2, typeof(TTarget), typeof(TArg1), typeof(TArg2));

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: new[] { k.ElementType!, k.ElementType2! },
					 requireGenericDef: true, requiredGenericArity: 1);

				if (def.GetParameters()[0].ParameterType != k.ElementType)
					throw new InvalidOperationException($"Method '{k.Name}' first parameter must be {k.ElementType!.Name}.");
				if (def.GetParameters()[1].ParameterType != k.ElementType2)
					throw new InvalidOperationException($"Method '{k.Name}' second parameter must be {k.ElementType2!.Name}.");

				var closed = def.MakeGenericMethod(k.GenericArg);

				var delType = typeof(Action<,,>).MakeGenericType(k.TargetType!, k.ElementType!, k.ElementType2!);
				return closed.CreateDelegate(delType);
			});

			return (Action<TTarget, TArg1, TArg2>)del;
		}

		/// <summary>
		/// Bound-instance convenience for <c>void M&lt;TGen&gt;(TArg1, TArg2)</c>.
		/// Returns <c>(TArg1, TArg2) =&gt; void</c> with <paramref name="target"/> pre-bound.
		/// </summary>
		public static Action<TArg1, TArg2> GetOrAllocBoundValueInvoker<TTarget, TArg1, TArg2>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 TTarget target,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance)
			 where TTarget : class
		{
			if (target is null) throw new ArgumentNullException(nameof(target));

			var openDelegate = GetOrAllocValueInvoker<TTarget, TArg1, TArg2>(declaringType, methodName, genericTypeArgument, flags);
			return (Action<TArg1, TArg2>)Delegate.CreateDelegate(typeof(Action<TArg1, TArg2>), target, openDelegate.Method);
		}

		// ---- public API : static (0 arguments) ------------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>static</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;()</c>.
		/// Returns <c>() =&gt; void</c>.
		/// </summary>
		public static Action GetOrAllocStaticValueInvoker(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Static) == 0) throw new ArgumentException("Use static BindingFlags.", nameof(flags));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.StaticValue_0, null, null, null);

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: Type.EmptyTypes,
					 requireGenericDef: true, requiredGenericArity: 1);

				var closed = def.MakeGenericMethod(k.GenericArg);

				return closed.CreateDelegate(typeof(Action));
			});

			return (Action)del;
		}

		// ---- public API : static (Span<TElement>) ---------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>static</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;(Span&lt;TElement&gt;)</c>.
		/// Returns <c>Span&lt;TElement&gt; =&gt; void</c>.
		/// </summary>
		public static Action<Span<TElement>> GetOrAllocStaticSpanInvoker<TElement>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Static) == 0) throw new ArgumentException("Use static BindingFlags.", nameof(flags));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.StaticSpan, TargetType: null, typeof(TElement), null);

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: new[] { typeof(Span<>).MakeGenericType(k.ElementType!) },
					 requireGenericDef: true, requiredGenericArity: 1);

				var p = def.GetParameters()[0].ParameterType;
				if (!(p.IsConstructedGenericType && p.GetGenericTypeDefinition() == typeof(Span<>) && p.GenericTypeArguments[0] == k.ElementType))
					throw new InvalidOperationException($"Static method '{k.Name}' must accept Span<{k.ElementType.Name}>.");

				var closed = def.MakeGenericMethod(k.GenericArg);

				return closed.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(Span<>).MakeGenericType(k.ElementType!)));
			});

			return (Action<Span<TElement>>)del;
		}

		// ---- public API : static (TElement) --------------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>static</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;(TElement)</c>.
		/// Returns <c>TElement =&gt; void</c>.
		/// </summary>
		public static Action<TElement> GetOrAllocStaticValueInvoker<TElement>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Static) == 0) throw new ArgumentException("Use static BindingFlags.", nameof(flags));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.StaticValue, TargetType: null, typeof(TElement), null);

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: new[] { k.ElementType! },
					 requireGenericDef: true, requiredGenericArity: 1);

				if (def.GetParameters()[0].ParameterType != k.ElementType)
					throw new InvalidOperationException($"Static method '{k.Name}' must accept {k.ElementType.Name}.");

				var closed = def.MakeGenericMethod(k.GenericArg);

				return closed.CreateDelegate(typeof(Action<>).MakeGenericType(k.ElementType!));
			});

			return (Action<TElement>)del;
		}

		// ---- public API : static (TArg1, TArg2) -----------------------------

		/// <summary>
		/// Gets or creates an invoker for an open-generic <b>static</b> method with signature:
		/// <c>void {methodName}&lt;TGen&gt;(TArg1, TArg2)</c>.
		/// Returns <c>(TArg1, TArg2) =&gt; void</c>.
		/// </summary>
		public static Action<TArg1, TArg2> GetOrAllocStaticValueInvoker<TArg1, TArg2>(
			 Type declaringType,
			 string methodName,
			 Type genericTypeArgument,
			 BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static)
		{
			if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
			if (genericTypeArgument is null) throw new ArgumentNullException(nameof(genericTypeArgument));
			if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("Required", nameof(methodName));
			if ((flags & BindingFlags.Static) == 0) throw new ArgumentException("Use static BindingFlags.", nameof(flags));

			var key = new CacheKey(declaringType, methodName, genericTypeArgument, ParamKind.StaticValue_2, null, typeof(TArg1), typeof(TArg2));

			var del = _cache.GetOrAdd(key, k =>
			{
				var def = ResolveMethodOrThrow(
					 k.DeclaringType, k.Name, flags,
					 parameterTypes: new[] { k.ElementType!, k.ElementType2! },
					 requireGenericDef: true, requiredGenericArity: 1);

				if (def.GetParameters()[0].ParameterType != k.ElementType)
					throw new InvalidOperationException($"Static method '{k.Name}' first parameter must be {k.ElementType!.Name}.");
				if (def.GetParameters()[1].ParameterType != k.ElementType2)
					throw new InvalidOperationException($"Static method '{k.Name}' second parameter must be {k.ElementType2!.Name}.");

				var closed = def.MakeGenericMethod(k.GenericArg);

				return closed.CreateDelegate(typeof(Action<,>).MakeGenericType(k.ElementType!, k.ElementType2!));
			});

			return (Action<TArg1, TArg2>)del;
		}

		// ---- disposal ------------------------------------------------------

		/// <summary>
		/// Clears the internal delegate cache.
		/// <para/>
		/// This can be useful in dynamic scenarios (e.g., unloading plugins or assemblies)
		/// to release references to types and methods, allowing for garbage collection.
		/// </summary>
		public static void Dispose()
		{
			_cache.Clear();
		}

		// ---- internal resolver ---------------------------------------------

		/// <summary>
		/// Resolves a method (optionally walking base types for non-public instance methods),
		/// validates open-generic arity, and returns the <b>generic method definition</b>.
		/// </summary>
		private static MethodInfo ResolveMethodOrThrow(
			 Type declaringType,
			 string methodName,
			 BindingFlags flags,
			 Type[] parameterTypes,
			 bool requireGenericDef,
			 int requiredGenericArity)
		{
			// local: attempt direct lookup on a specific type
			static MethodInfo? TryGet(Type t, string name, BindingFlags f, Type[] ps)
				 => t.GetMethod(name, f, binder: null, types: ps, modifiers: null);

			MethodInfo? mi = null;

			// for non-public instance methods, walk base chain (GetMethod won't surface them via inheritance)
			bool needWalk = (flags & BindingFlags.Instance) != 0 && (flags & BindingFlags.NonPublic) != 0;

			for (Type? t = declaringType; t is not null; t = needWalk ? t.BaseType : null)
			{
				mi = TryGet(t, methodName, flags, parameterTypes);
				if (mi != null)
					break;
				if (!needWalk) break; // no walk requested
			}

			if (mi is null)
				throw new MissingMethodException(declaringType.FullName, methodName); // not found

			// if a generic method is required, ensure we return the generic definition
			if (requireGenericDef)
			{
				if (!mi.IsGenericMethod)
					throw new InvalidOperationException($"Method '{methodName}' must be generic.");

				if (!mi.IsGenericMethodDefinition)
					mi = mi.GetGenericMethodDefinition(); // normalize to definition

				if (mi.GetGenericArguments().Length != requiredGenericArity)
					throw new InvalidOperationException($"Method '{methodName}' must have exactly {requiredGenericArity} generic parameter(s).");
			}

			// must return void (API contract for these helpers)
			if (mi.ReturnType != typeof(void))
				throw new InvalidOperationException($"Method '{methodName}' must return void.");

			// must be instance or static per flags given
			bool isStatic = mi.IsStatic;
			if (isStatic && (flags & BindingFlags.Static) == 0)
				throw new InvalidOperationException($"Method '{methodName}' is static but instance flags were provided.");
			if (!isStatic && (flags & BindingFlags.Instance) == 0)
				throw new InvalidOperationException($"Method '{methodName}' is instance but static flags were provided.");

			return mi;
		}
	}
}
