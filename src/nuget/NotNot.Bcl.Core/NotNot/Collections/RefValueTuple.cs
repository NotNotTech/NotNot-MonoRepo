// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using NotNot.Advanced;
using NotNot.Collections;

namespace NotNot.Collections;

public ref struct RefValueTuple<T1, T2>
{
	public ref T1 Item1;
	public ref T2 Item2;
	public RefValueTuple(ref T1 item1, ref T2 item2)
	{
		Item1 = ref item1;
		Item2 = ref item2;
	}


	public override string ToString()
	{
		return $"({Item1}, {Item2})";
	}

}
