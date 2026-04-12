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

public static class zz_Extensions_ProcessStartInfo
{
	[Obsolete(
		"doesn't work, can't redirect output if using shell exec.  needs to be reworked to store env vars in a file then extract")]
	private static AsyncLazy<Dictionary<string, string>> _asyncShellEnvironmentVariables = new(async () =>
	{
		var startInfo = new ProcessStartInfo();
		var envVars = new Dictionary<string, string>();

		// Check if the current OS is Windows
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			startInfo.FileName = "cmd.exe";
			startInfo.Arguments = "/c set";
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			startInfo.FileName = "/bin/bash";
			startInfo.Arguments = "-c env";
		}
		else
		{
			throw new NotSupportedException("Unsupported operating system");
		}

		startInfo.UseShellExecute = true;
		startInfo.RedirectStandardOutput = true;

		using (var process = Process.Start(startInfo))
		{
			string output = await process.StandardOutput.ReadToEndAsync();
			string[] lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string line in lines)
			{
				string[] parts = line.Split(new[] { '=' }, 2);
				envVars[parts[0]] = parts[1];
			}
		}

		return envVars;
	});

	[Obsolete(
		"doesn't work, can't redirect output if using shell exec.  needs to be reworked to store env vars in a file then extract")]
	public static async Task _ExecUsingShellEnvVars(this ProcessStartInfo startInfo)
	{
		//append shell env vars to process env vars
		var shellEnvVars = await _asyncShellEnvironmentVariables;
		foreach (var kvp in shellEnvVars)
		{
			startInfo.Environment[kvp.Key] = kvp.Value;
		}

		using var process = new Process
		{
			StartInfo = startInfo,
		};

		process.Start();
		await process.WaitForExitAsync();
	}

	public static async Task<(string stdOut, string stdErr)> _ExecCaptureIO(this ProcessStartInfo startInfo)
	{
		startInfo.UseShellExecute = false;
		startInfo.RedirectStandardError = true;
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardInput = true;
		startInfo.CreateNoWindow = true;

		using var process = new Process
		{
			StartInfo = startInfo,
		};

		process.Start();

		var stdOut = await process.StandardOutput.ReadToEndAsync();
		var stdErr = await process.StandardError.ReadToEndAsync();

		return (stdOut, stdErr);
	}
}

