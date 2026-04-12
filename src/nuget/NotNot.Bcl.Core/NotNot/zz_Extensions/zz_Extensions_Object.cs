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

[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_Object
{
	/// <summary>
	/// create a one-element Span over the value
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="value"></param>
	/// <returns></returns>
	public static Span<T> _AsSpanSingle<T>(this ref T value) where T : struct
	{
		return new Span<T>(ref value);
	}
	/// <summary>
	/// create a one-element Span over the value
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="value"></param>
	/// <returns></returns>
	public static Span<T> _AsSpanSingle<T>(this T value) where T : class
	{
		return MemoryMarshal.CreateSpan(ref value, 1);
	}


	/// <summary>
	/// Ensures value is not null, also returning it.  If null, throws an exception.
	/// <para>shortcut to `__.NotNull(value);`</para>
	/// </summary>

	[return: NotNull]
	public static T _NotNull<T>([NotNull] this T? value, string? message = null, [CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0, [CallerArgumentExpression("value")] string valueName = "") where T : class
	{
		return __.NotNull(value, message, memberName, sourceFilePath, sourceLineNumber, valueName)!;
	}


	//	public static bool _Is<T>(this T value, params Type[] types)
	//{
	//	return types.Any(t => t.IsAssignableFrom(value.GetType()));
	//}

	///// <summary>
	///// if null, will return string.<see cref="String.Empty"/>.  otherwise returns the normal <see cref="ToString"/> 
	///// </summary>
	///// <typeparam name="T"></typeparam>
	///// <param name="target"></param>
	///// <returns></returns>
	//public static string ToStringOrEmpty<T>(this T target)
	//{
	//   if (ReferenceEquals(target, null))
	//   {
	//      return string.Empty;
	//   }
	//   return target.ToString();
	//}

	///// <summary>
	///// 	Determines whether the object is equal to any of the provided values.
	///// </summary>
	///// <typeparam name = "T"></typeparam>
	///// <param name = "obj">The object to be compared.</param>
	///// <param name = "values">The values to compare with the object.</param>
	///// <returns></returns>
	//public static bool Equals<T>(this T obj, params T[] values)
	//   {

	//	  return Array.IndexOf(values, obj) != -1;
	//   }


	///// <summary>
	///// is this value inside any of the given collections
	///// </summary>
	///// <typeparam name="T"></typeparam>
	///// <param name="source"></param>
	///// <param name="collections"></param>
	///// <returns></returns>
	//public static bool IsInAny<T>(this T source, params IEnumerable<T>[] collections)
	//{
	//   if (null == source) throw new ArgumentNullException("source");
	//   foreach (var collection in collections)
	//   {
	//      var iCollection = collection as ICollection<T>;
	//      if (iCollection != null)
	//      {
	//         if (iCollection.Contains(source))
	//         {
	//            return true;
	//         }
	//         continue;
	//      }
	//      if (collection.Contains(source))
	//      {
	//         return true;
	//      }
	//   }
	//   return false;
	//}

	///// <summary>
	///// 	Returns TRUE, if specified target reference is equals with null reference.
	///// 	Othervise returns FALSE.
	///// </summary>
	///// <typeparam name = "T">Type of target.</typeparam>
	///// <param name = "target">Target reference. Can be null.</param>
	///// <remarks>
	///// 	Some types has overloaded '==' and '!=' operators.
	///// 	So the code "null == ((MyClass)null)" can returns <c>false</c>.
	///// 	The most correct way how to test for null reference is using "System.Object.ReferenceEquals(object, object)" method.
	///// 	However the notation with ReferenceEquals method is long and uncomfortable - this extension method solve it.
	///// 
	///// 	Contributed by tencokacistromy, http://www.codeplex.com/site/users/view/tencokacistromy
	///// </remarks>
	///// <example>
	///// 	MyClass someObject = GetSomeObject();
	///// 	if ( someObject.IsNull() ) { /* the someObject is null */ }
	///// 	else { /* the someObject is not null */ }
	///// </example>
	//public static bool IsNull<T>(this T target) where T : class
	//{
	//   var result = ReferenceEquals(target, null);
	//   return result;
	//}

	//public static bool IsDefault<T>(this T target) where T : struct
	//{
	//   //if (ReferenceEquals(target, null))
	//   //{
	//   //   return true;
	//   //}
	//   return Equals(target, default(T));
	//}
}

