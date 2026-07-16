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

public static class zz_Extensions_MemberInfo
{

	public static Type _FirstParameterType(this MethodBase method) => method.GetParameters()[0].ParameterType;

	public static bool _IsPublic(this PropertyInfo propertyInfo) => (propertyInfo.GetGetMethod() ?? propertyInfo.GetSetMethod()) != null;
	public static bool _Has<TAttribute>(this MemberInfo member) where TAttribute : Attribute => member.IsDefined(typeof(TAttribute));
	public static bool _CanBeSet(this MemberInfo member) => member is PropertyInfo property ? property.CanWrite : !((FieldInfo)member).IsInitOnly;

	public static void _SetMemberValue(this MemberInfo propertyOrField, object target, object value)
	{
		if (propertyOrField is PropertyInfo property)
		{
			if (property.CanWrite)
			{
				property.SetValue(target, value, null);
			}
			return;
		}
		if (propertyOrField is FieldInfo field)
		{
			if (!field.IsInitOnly)
			{
				field.SetValue(target, value);
			}
			return;
		}
		throw _Expected(propertyOrField);
	}
	private static ArgumentOutOfRangeException _Expected(MemberInfo propertyOrField) => new(nameof(propertyOrField), "Expected a property or field, not " + propertyOrField);
	public static object _GetMemberValue(this MemberInfo propertyOrField, object target) => propertyOrField switch
	{
		PropertyInfo property => property.GetValue(target, null),
		FieldInfo field => field.GetValue(target),
		_ => throw _Expected(propertyOrField)
	};

	public static MemberInfo _FindProperty(LambdaExpression lambdaExpression)
	{
		Expression expressionToCheck = lambdaExpression.Body;
		while (true)
		{
			switch (expressionToCheck)
			{
				case MemberExpression { Member: var member, Expression.NodeType: ExpressionType.Parameter or ExpressionType.Convert }:
					return member;
				case UnaryExpression { Operand: var operand }:
					expressionToCheck = operand;
					break;
				default:
					throw new ArgumentException(
						 $"Expression '{lambdaExpression}' must resolve to top-level member and not any child object's properties. You can use ForPath, a custom resolver on the child type or the AfterMap option instead.",
						 nameof(lambdaExpression));
			}
		}
	}
	public static Type _GetMemberType(this MemberInfo member) => member switch
	{
		PropertyInfo property => property.PropertyType,
		MethodInfo method => method.ReturnType,
		FieldInfo field => field.FieldType,
		null => throw new ArgumentNullException(nameof(member)),
		_ => throw new ArgumentOutOfRangeException(nameof(member))
	};


}

//    The included numeric extension methods utilize experimental CLR behavior to allow generic numerical operations.
//    Might work great, might have hidden perf costs?

