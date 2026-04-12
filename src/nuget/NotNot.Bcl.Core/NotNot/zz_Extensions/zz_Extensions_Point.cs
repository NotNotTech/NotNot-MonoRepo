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

public static class zz_Extensions_Point
{
	public static System.Drawing.PointF _ToPointF(this System.Drawing.Point point)
	{
		return new System.Drawing.PointF(point.X, point.Y);
	}
	public static System.Drawing.Point _ToPoint(this System.Drawing.PointF point)
	{
		return new System.Drawing.Point((int)point.X, (int)point.Y);
	}

	public static Vector3 _ToMsVec3XY(this System.Drawing.Point point, float z = 0)
	{
		return new Vector3(point.X, point.Y, z);
	}
	public static Vector3 _ToMsVec3XZ(this System.Drawing.Point point, float y = 0)
	{
		return new Vector3(point.X, y, point.Y);
	}
	public static Vector2 _ToMsVec2(this System.Drawing.Point point)
	{
		return new Vector2(point.X, point.Y);
	}
}

