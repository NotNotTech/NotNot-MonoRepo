// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Blake3;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Helpers;
// using Newtonsoft.Json.Linq; // Removed - no longer needed
using Nito.AsyncEx.Synchronous;
using NotNot;
using NotNot._internal.Threading;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;
using NotNot.Data;
using NotNot.Diagnostics;

//using Xunit.Sdk;

//using CommunityToolkit.HighPerformance;
//using DotNext;

/// <summary>
///    Extension methods for the reflection meta data type "Type"
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_Type
{
	//static zz__Type_Extensions()
	//{
	//   var rand = new Random();

	//   _obfuscationSuffix += rand.NextChar();
	//}
	public static string _GetReadableTypeName(this Type type)
	{
		if (!type.IsGenericType)
		{
			return type.Name;
		}

		StringBuilder sb = new StringBuilder();
		sb.Append(type.Name.Split('`')[0]); // Remove the arity indicator
		sb.Append('<');
		sb.Append(string.Join(", ", type.GetGenericArguments().Select(_GetReadableTypeName)));
		sb.Append('>');

		return sb.ToString();
	}
	public static string _GetReadableTypeNameFull(this Type type)
	{
		StringBuilder sb = new StringBuilder();

		// Include the namespace if available
		if (!string.IsNullOrEmpty(type.Namespace))
		{
			sb.Append(type.Namespace).Append('.');
		}

		sb.Append(GetTypeNameWithoutArity(type));

		if (type.IsGenericType)
		{
			sb.Append('<');
			sb.Append(string.Join(", ", type.GetGenericArguments().Select(_GetReadableTypeNameFull)));
			sb.Append('>');
		}

		return sb.ToString();
	}
	static string GetTypeNameWithoutArity(Type type)
	{
		string name = type.Name;
		int backtickIndex = name.IndexOf('`');
		return backtickIndex == -1 ? name : name.Substring(0, backtickIndex);
	}

	private static string _obfuscationSuffix = " " + new Random()._NextChar(); // " ";

	public static T _GetInstanceField<T>(this Type type, object instance, string fieldName)
	{
		var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		var fieldInfo = type.GetField(fieldName, bindingFlags);
		if (fieldInfo == null)
		{
			throw new ArgumentException($"No field named '{fieldName}' found on type '{type.FullName}'.", nameof(fieldName));
		}
		return (T)fieldInfo.GetValue(instance)!;
	}

	/// <summary>
	/// Gets the value of a property or field from an instance of an object using reflection.
	/// Works with both public and non-public (private, protected, internal) members.
	/// </summary>
	/// <typeparam name="T">The expected type of the property or field value.</typeparam>
	/// <param name="type">The type that contains the property or field definition.</param>
	/// <param name="instance">The object instance from which to get the property or field value.</param>
	/// <param name="memberName">The name of the property or field to get.</param>
	/// <returns>The value of the property or field cast to type T.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type, instance, or memberName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the member cannot be found or is not a property or field.</exception>
	/// <exception cref="InvalidCastException">Thrown when the member value cannot be cast to the expected type.</exception>
	public static T _GetInstanceMember<T>(this Type type, object instance, string memberName)
	{
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (instance == null) throw new ArgumentNullException(nameof(instance));
		if (string.IsNullOrEmpty(memberName)) throw new ArgumentNullException(nameof(memberName));

		var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		// First try to find a property with the given name
		var propertyInfo = type.GetProperty(memberName, bindingFlags);
		if (propertyInfo != null)
		{
			return (T)propertyInfo.GetValue(instance)!;
		}

		// If not a property, try to find a field with the given name
		var fieldInfo = type.GetField(memberName, bindingFlags);
		if (fieldInfo != null)
		{
			return (T)fieldInfo.GetValue(instance)!;
		}

		// If we reach here, no matching property or field was found
		throw new ArgumentException($"No property or field named '{memberName}' found on type '{type.FullName}'.", nameof(memberName));
	}

	/// <summary>
	/// Sets a property or field value on an instance of an object using reflection.
	/// Works with both public and non-public (private, protected, internal) members.
	/// </summary>
	/// <param name="type">The type that contains the property or field definition.</param>
	/// <param name="instance">The object instance on which to set the property or field.</param>
	/// <param name="memberName">The name of the property or field to set.</param>
	/// <param name="value">The value to set the property or field to.</param>
	/// <exception cref="ArgumentNullException">Thrown when type, instance, or memberName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the member cannot be found or is not a property or field.</exception>
	/// <exception cref="TargetException">Thrown when the instance doesn't match the target type.</exception>
	public static void _SetInstanceMember(this Type type, object instance, string memberName, object value)
	{
		// Validate inputs
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (instance == null) throw new ArgumentNullException(nameof(instance));
		if (string.IsNullOrEmpty(memberName)) throw new ArgumentNullException(nameof(memberName));

		// Define binding flags for instance members (both public and non-public)
		const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		// First try to find a property with the given name
		var propertyInfo = type.GetProperty(memberName, bindingFlags);
		if (propertyInfo != null)
		{
			// Check if the property is writable
			if (!propertyInfo.CanWrite)
			{
				throw new ArgumentException($"Property '{memberName}' on type '{type.FullName}' is read-only.", nameof(memberName));
			}

			// Set the property value
			propertyInfo.SetValue(instance, value);
			return;
		}

		// If not a property, try to find a field with the given name
		var fieldInfo = type.GetField(memberName, bindingFlags);
		if (fieldInfo != null)
		{
			// Set the field value
			fieldInfo.SetValue(instance, value);
			return;
		}

		// If we reach here, no matching property or field was found
		throw new ArgumentException($"No property or field named '{memberName}' found on type '{type.FullName}'.", nameof(memberName));
	}

	/// <summary>
	/// Invokes a method on a target object by name using reflection.
	/// </summary>
	/// <param name="type">The type that contains the method definition.</param>
	/// <param name="instance">The instance on which to invoke the method. Use null for static methods.</param>
	/// <param name="methodName">The name of the method to invoke.</param>
	/// <param name="parameters">Optional parameters to pass to the method.</param>
	/// <returns>The result of the method invocation.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type or methodName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the method cannot be found on the specified type.</exception>
	/// <exception cref="TargetException">Thrown when the instance doesn't match the target type for instance methods.</exception>
	public static object? _InvokeInstanceMethod(this Type type, object instance, string methodName, params object[] parameters)
	{
		// Validate inputs
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (methodName == null) throw new ArgumentNullException(nameof(methodName));

		// Determine binding flags based on whether we're invoking a static or instance method
		var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
		if (instance == null)
		{
			bindingFlags |= BindingFlags.Static;
		}
		else
		{
			bindingFlags |= BindingFlags.Instance;
		}

		// Find the method on the type
		var methodInfo = type.GetMethod(methodName, bindingFlags);
		if (methodInfo == null)
		{
			throw new ArgumentException($"Method '{methodName}' not found on type '{type.FullName}'.", nameof(methodName));
		}

		// Invoke the method and return the result
		return methodInfo.Invoke(instance, parameters);
	}

	/// <summary>
	/// Invokes a method on a target object by name using reflection with a specific parameter types signature.
	/// </summary>
	/// <param name="type">The type that contains the method definition.</param>
	/// <param name="instance">The instance on which to invoke the method. Use null for static methods.</param>
	/// <param name="methodName">The name of the method to invoke.</param>
	/// <param name="parameterTypes">An array of parameter types that define the method signature.</param>
	/// <param name="parameters">Parameters to pass to the method.</param>
	/// <returns>The result of the method invocation.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type or methodName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the method cannot be found on the specified type.</exception>
	public static object? _InvokeInstanceMethod(this Type type, object instance, string methodName, Type[] parameterTypes, params object[] parameters)
	{
		// Validate inputs
		if (type == null) throw new ArgumentNullException(nameof(type));
		if (methodName == null) throw new ArgumentNullException(nameof(methodName));
		if (parameterTypes == null) throw new ArgumentNullException(nameof(parameterTypes));

		// Determine binding flags
		var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
		if (instance == null)
		{
			bindingFlags |= BindingFlags.Static;
		}
		else
		{
			bindingFlags |= BindingFlags.Instance;
		}

		// Get the method with the specific parameter types
		var methodInfo = type.GetMethod(methodName, bindingFlags, null, parameterTypes, null);
		if (methodInfo == null)
		{
			throw new ArgumentException($"Method '{methodName}' with the specified parameter types not found on type '{type.FullName}'.", nameof(methodName));
		}

		// Invoke the method and return the result
		return methodInfo.Invoke(instance, parameters);
	}

	/// <summary>
	/// Invokes a method on a target object by name using reflection and returns a strongly-typed result.
	/// </summary>
	/// <typeparam name="TResult">The expected return type of the method.</typeparam>
	/// <param name="type">The type that contains the method definition.</param>
	/// <param name="instance">The instance on which to invoke the method. Use null for static methods.</param>
	/// <param name="methodName">The name of the method to invoke.</param>
	/// <param name="parameters">Optional parameters to pass to the method.</param>
	/// <returns>The result of the method invocation cast to the specified type.</returns>
	/// <exception cref="ArgumentNullException">Thrown when type or methodName is null.</exception>
	/// <exception cref="ArgumentException">Thrown when the method cannot be found on the specified type.</exception>
	/// <exception cref="InvalidCastException">Thrown when the result cannot be cast to the expected type.</exception>
	public static TResult _InvokeInstanceMethod<TResult>(this Type type, object instance, string methodName, params object[] parameters)
	{
		// Call the non-generic version and cast the result
		var result = _InvokeInstanceMethod(type, instance, methodName, parameters);

		// Handle null result for value types
		if (result == null && typeof(TResult).IsValueType)
		{
			// Guarded: TResult is a value type here, so default(TResult) is non-null.
			return default!;
		}

		// Cast to the expected return type
		return (TResult)result!;
	}

	//[Conditional("DEBUG")]
	//public static void _AssertHasObfuscationAttribute(this Type type)
	//{
	//	if (typeof(ObfuscationAttribute).Name != "ObfuscationAttribute")
	//	{
	//		//this has been obfuscated, so ignore this assert check
	//		return;
	//	}
	//	__.ERROR.AssertOnce(type.HasObfuscationAttribute(), "use [System.Reflection.Obfuscation(Exclude = true, StripAfterObfuscation = false, ApplyToMembers = true)] attribute!  type={0}", type.GetName());
	//}

	/// <summary>
	///    returns the "runtime name" of a type.
	///    <para>
	///       if the type is marked by an [Obfuscation(exclude:true)] attribute then we will return the actual name.
	///       otherwise, we add a random unicode suffix to represent obfuscation workflows
	///    </para>
	/// </summary>
	/// <param name="memberInfo"></param>
	/// <returns></returns>
	public static string _GetName(this MemberInfo memberInfo)
	{
		//#if DEBUG
		//         if (methodInfo.HasObfuscationAttribute())
		//         {
		//            return type.Name + _obfuscationSuffix;
		//         }
		//#endif
#if DEBUG

		ObfuscationAttribute? obf;
		if (memberInfo._TryGetAttribute(out obf))
		{
			if (obf.Exclude)
			{
				return memberInfo.Name;
			}
		}

		return memberInfo.Name + _obfuscationSuffix;
#else
		return memberInfo.Name;
#endif
	}

	public static bool HasObfuscationAttribute(this MemberInfo memberInfo)
	{
		if (typeof(ObfuscationAttribute).Name != "ObfuscationAttribute")
		{
			//this has been obfuscated, so our check will always be false
			return false;
		}

		ObfuscationAttribute? attribute;
		return memberInfo._TryGetAttribute(out attribute, noInherit: true);

		//foreach (var attribute in memberInfo.GetCustomAttributes(false))
		//{
		//   if (attribute is ObfuscationAttribute)
		//   {
		//      return true;
		//   }
		//}

		//var type = memberInfo as Type;
		//if (type != null)
		//{
		//   foreach (var interf in type.GetInterfaces())
		//   {
		//      foreach (var attribute in interf.GetCustomAttributes(false))
		//      {
		//         if (attribute is ObfuscationAttribute)
		//         {
		//            return true;
		//         }
		//      }
		//   }
		//}
		//return false;
	}

	/// <summary>
	///    returns the first found attribute
	/// </summary>
	/// <typeparam name="TAttribute"></typeparam>
	/// <param name="memberInfo"></param>
	/// <param name="attributeFound"></param>
	/// <param name="noInherit">default false (include inherited attributes — mirrors BCL <see cref="MemberInfo.GetCustomAttributes(Type, bool)"/> with semantics inverted). set true to skip the inheritance chain.</param>
	/// <returns></returns>
	public static bool _TryGetAttribute<TAttribute>(this MemberInfo memberInfo, [NotNullWhen(true)] out TAttribute? attributeFound,
		bool noInherit = false) where TAttribute : Attribute
	{
		//var found = memberInfo.GetCustomAttributes(typeof(TAttribute), inherit);
		//if (found == null || found.Length == 0)
		//{
		//   attribute = null;
		//   return false;
		//}
		//attribute = found[0] as TAttribute;
		//return true;


		var attributeType = typeof(TAttribute);
		foreach (var attribute in memberInfo.GetCustomAttributes(attributeType, !noInherit))
		{
			//if (attribute is TAttribute)
			{
				attributeFound = (TAttribute)attribute;
				return true;
			}
		}

		var type = memberInfo as Type;
		if (type != null)
		{
			foreach (var interf in type.GetInterfaces())
			{
				foreach (var attribute in interf.GetCustomAttributes(attributeType, !noInherit))
				{
					//if (attribute is TAttribute)
					{
						// attribute came from GetCustomAttributes(typeof(TAttribute)), so it is a TAttribute.
						attributeFound = (TAttribute)attribute;
						return true;
					}
				}
			}
		}

		attributeFound = null;
		return false;
	}

	//   public static string GetObfuscatedAssemblyQualifiedName(this Type type)
	//   {
	//#if DEBUG
	//      //if the obfuscation attribute isn't named, then we know obfuscation is turned on and we should return the actual type name
	//      //otherwise, if no obfusation, if we have the attribute, we return "the original" name
	//      if (typeof(ObfuscationAttribute).Name != "ObfuscationAttribute" || type.HasObfuscationAttribute())
	//      {
	//         return type.AssemblyQualifiedName;
	//      }
	//      //but if the attribute isn't turned on (and if we are not actually obfuscated) lets simulate obfuscation by adjusting our returned string
	//      return ParseHelper.FormatInvariant("{2}{0}_{1}", type.AssemblyQualifiedName.ToUpperInvariant(), random, enclosing);
	//#else
	//         //in release, we just return our normal name
	//         return type.AssemblyQualifiedName;
	//#endif
	//   }


	public static bool _IsAssignableTo<TOther>(this Type type)
	{
		return type.IsAssignableTo(typeof(TOther));
	}

	/// <summary>
	///    Discovers all concrete, non-abstract types in the current AppDomain that inherit from or implement the specified base type.
	/// </summary>
	/// <param name="baseType">The base type or interface to find derived types for.</param>
	/// <returns>A list of all derived types.</returns>
	public static List<Type> _GetDerivedTypes(this Type baseType)
	{
		return AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(assembly =>
			{
				try
				{
					return assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException)
				{
					// In case of a type load exception, just return an empty array.
					// This can happen with dynamic or problematic assemblies.
					return Type.EmptyTypes;
				}
			})
			.Where(type => type != baseType && !type.IsAbstract && !type.IsInterface && baseType.IsAssignableFrom(type))
			.ToList();
	}

	/// <summary>
	///    Creates and returns an instance of the desired type
	/// </summary>
	/// <param name="type">The type to be instanciated.</param>
	/// <param name="constructorParameters">Optional constructor parameters</param>
	/// <returns>The instanciated object</returns>
	/// <example>
	///    <code>
	/// 		var type = Type.GetType(".NET full qualified class Type")
	/// 		var instance = type.CreateInstance();
	/// 	</code>
	/// </example>
	public static object _CreateInstance(this Type type, params object[] constructorParameters)
	{
		return type._CreateInstance<object>(constructorParameters);
	}

	/// <summary>
	///    Creates and returns an instance of the desired type casted to the generic parameter type T
	/// </summary>
	/// <typeparam name="T">The data type the instance is casted to.</typeparam>
	/// <param name="type">The type to be instanciated.</param>
	/// <param name="constructorParameters">Optional constructor parameters</param>
	/// <returns>The instanciated object</returns>
	/// <example>
	///    <code>
	/// 		var type = Type.GetType(".NET full qualified class Type")
	/// 		var instance = type.CreateInstance&lt;IDataType&gt;();
	/// 	</code>
	/// </example>
	public static T _CreateInstance<T>(this Type type, params object[] constructorParameters)
	{
		var instance = Activator.CreateInstance(type, constructorParameters);
		// Activator.CreateInstance on a concrete type returns non-null (or throws).
		return (T)instance!;
	}

	/// <summary>
	///    Check if this is a base type
	/// </summary>
	/// <param name="type"></param>
	/// <param name="checkingType"></param>
	/// <returns></returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static bool _IsBaseType(this Type type, Type checkingType)
	{
		__.GetLogger()._EzError(type is not null);
		// Walk up the base-type chain. Type.BaseType is null for interfaces and for System.Object,
		// so a null here means we've reached the top of the chain: break (a `continue` would spin forever
		// because the loop variable never advances past null).
		Type? current = type;
		while (current != typeof(object))
		{
			if (current is null)
			{
				break;
			}

			if (current == checkingType)
			{
				return true;
			}

			current = current.BaseType;
		}

		return false;
	}

	/// <summary>
	///    Check if this is a sub class generic type
	/// </summary>
	/// <param name="generic"></param>
	/// <param name="toCheck"></param>
	/// <returns></returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static bool _IsSubclassOfRawGeneric(this Type generic, Type toCheck)
	{
		__.GetLogger()._EzError(generic is not null);
		// Walk up the base-type chain. Type.BaseType is null for interfaces and for System.Object,
		// so a null here means we've reached the top of the chain: break (a `continue` would spin forever
		// because the loop variable never advances past null).
		Type? current = toCheck;
		while (current != typeof(object))
		{
			if (current is null)
			{
				break;
			}

			var cur = current.IsGenericType ? current.GetGenericTypeDefinition() : current;
			if (generic == cur)
			{
				return true;
			}

			current = current.BaseType;
		}

		return false;
	}

	/// <summary>
	///    Closes the passed generic type with the provided type arguments and returns an instance of the newly constructed
	///    type.
	/// </summary>
	/// <typeparam name="T">The typed type to be returned.</typeparam>
	/// <param name="genericType">The open generic type.</param>
	/// <param name="typeArguments">The type arguments to close the generic type.</param>
	/// <returns>An instance of the constructed type casted to T.</returns>
	public static T _CreateGenericTypeInstance<T>(this Type genericType, params Type[] typeArguments) where T : class
	{
		var constructedType = genericType.MakeGenericType(typeArguments);
		var instance = Activator.CreateInstance(constructedType);
		// The constructed type is T closed over typeArguments; CreateInstance returns non-null.
		return (instance as T)!;
	}

	//public static bool _IsUnmanagedStruct(this Type type)
	//{
	//	return System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences()
	//}
}

