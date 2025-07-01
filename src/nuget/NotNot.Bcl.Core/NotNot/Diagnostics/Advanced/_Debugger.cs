// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotNot.Diagnostics.Advanced;

[DebuggerNonUserCode]
public static class _Debugger
{

	/// <summary>
	/// if a debugger is not attached, this will attempt to do so once during the app lifetime.
	/// If a debugger is attached, every call will break into the debugger.
	/// </summary>
	public static void LaunchOnce()
	{
		if (Debugger.IsAttached)
		{
			Debugger.Break();
			return;
		}

		if (_hasLaunched is false)
		{
			_hasLaunched = true;
			Debugger.Launch();
		}
		else
		{
			Debugger.Break();
		}
	}

	private static bool _hasLaunched = false;
}

//test

