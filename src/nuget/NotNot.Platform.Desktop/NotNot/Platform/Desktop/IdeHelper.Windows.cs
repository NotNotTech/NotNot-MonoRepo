using System.Diagnostics;
using System.Runtime.Versioning;

namespace NotNot.Platform.Desktop;

/// <summary>
/// Windows-specific Visual Studio integration for <see cref="IdeHelper"/>.
/// </summary>
public static partial class IdeHelper
{
	// Cache for running devenv detection (TTL: 3 seconds)
	private static (string? Path, DateTime Expiry) _runningDevenvCache;
	private static readonly Lock _devenvCacheLock = new();

	/// <summary>
	/// Opens a file in Visual Studio at the specified line.
	/// </summary>
	/// <param name="filePath">Absolute path to the file to open.</param>
	/// <param name="line">Line number to navigate to (1-based).</param>
	/// <param name="version">
	/// Visual Studio version to use:
	/// <list type="bullet">
	/// <item><description><c>"vs2026"</c> - Visual Studio 2026 (stable)</description></item>
	/// <item><description><c>"vs2026-insiders"</c> - Visual Studio 2026 Preview/Insiders</description></item>
	/// <item><description><c>"vs2022"</c> - Visual Studio 2022 (stable)</description></item>
	/// <item><description><c>"vs2022-preview"</c> - Visual Studio 2022 Preview</description></item>
	/// <item><description><c>"vs-running"</c> - Use currently running VS instance</description></item>
	/// <item><description><c>null</c> - Auto-detect newest available (default)</description></item>
	/// </list>
	/// </param>
	/// <returns>
	/// <c>true</c> if Visual Studio was launched successfully;
	/// <c>false</c> if VS was not found or launch failed.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Unlike other IDE methods, this returns a boolean because VS detection is complex
	/// and callers may want to provide user feedback or fall back to another editor.
	/// </para>
	/// <para>
	/// <b>Detection Strategy:</b>
	/// <list type="number">
	/// <item><description>If <c>"vs-running"</c>: Find currently running devenv.exe process</description></item>
	/// <item><description>Try vswhere.exe for accurate installation discovery</description></item>
	/// <item><description>Fall back to probing standard Program Files paths</description></item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Edition Priority:</b> Enterprise → Professional → Community → Preview (varies by version parameter)
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// // Open in VS2026
	/// if (!IdeHelper.OpenInVisualStudio(@"C:\Projects\App\Program.cs", 42, "vs2026"))
	/// {
	///     Console.WriteLine("VS2026 not found, trying VSCode...");
	///     IdeHelper.OpenInVSCode(@"C:\Projects\App\Program.cs", 42);
	/// }
	///
	/// // Use whatever VS is currently running
	/// IdeHelper.OpenInVisualStudio(filePath, line, "vs-running");
	/// </code>
	/// </example>
	[SupportedOSPlatform("windows")]
	public static bool OpenInVisualStudio(string filePath, int line, string? version = null)
	{
		if (!OperatingSystem.IsWindows())
			return false;

		if (string.IsNullOrEmpty(filePath) || line < 1)
			return false;

		try
		{
			string? devenvPath;
			var normalizedVersion = version?.ToLowerInvariant();

			if (normalizedVersion == "vs-running")
			{
				devenvPath = FindRunningDevenv();
				if (devenvPath == null)
					return false;
			}
			else
			{
				devenvPath = FindDevenvPath(normalizedVersion);
				if (devenvPath == null)
					return false;
			}

			var psi = new ProcessStartInfo(devenvPath) { UseShellExecute = true };
			psi.ArgumentList.Add("/edit");
			psi.ArgumentList.Add(filePath);
			psi.ArgumentList.Add("/command");
			psi.ArgumentList.Add($"Edit.Goto {line}");

			Process.Start(psi);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Finds a running Visual Studio (devenv.exe) process and returns its executable path.
	/// </summary>
	/// <returns>Path to the running devenv.exe, or <c>null</c> if none found.</returns>
	/// <remarks>
	/// Results are cached for 3 seconds to avoid thundering herd on rapid clicks.
	/// This allows reusing an already-open Visual Studio instance for multiple file opens.
	/// </remarks>
	[SupportedOSPlatform("windows")]
	public static string? FindRunningDevenv()
	{
		if (!OperatingSystem.IsWindows())
			return null;

		// Check cache first
		lock (_devenvCacheLock)
		{
			if (DateTime.UtcNow < _runningDevenvCache.Expiry)
			{
				return _runningDevenvCache.Path;
			}
		}

		string? foundPath = null;
		try
		{
			var devenvProcesses = Process.GetProcessesByName("devenv");
			foreach (var proc in devenvProcesses)
			{
				try
				{
					// Check if process has exited before accessing MainModule (TOCTOU race)
					if (proc.HasExited)
					{
						continue;
					}

					// Get the main module's filename (the devenv.exe path)
					var path = proc.MainModule?.FileName;
					if (!string.IsNullOrEmpty(path) && File.Exists(path))
					{
						foundPath = path;
						break;
					}
				}
				catch (System.ComponentModel.Win32Exception)
				{
					// 32/64-bit mismatch or access denied - continue to next
				}
				catch (InvalidOperationException)
				{
					// Process exited between HasExited check and MainModule access
				}
				catch
				{
					// Other errors - continue to next
				}
				finally
				{
					proc.Dispose();
				}
			}
		}
		catch
		{
			// Process enumeration failed
		}

		// Update cache
		lock (_devenvCacheLock)
		{
			_runningDevenvCache = (foundPath, DateTime.UtcNow.AddSeconds(3));
		}

		return foundPath;
	}

	/// <summary>
	/// Finds the devenv.exe path for a specific Visual Studio version.
	/// </summary>
	/// <param name="version">Version identifier (vs2022, vs2022-preview, vs2026, vs2026-insiders) or null for auto-detect.</param>
	/// <returns>Full path to devenv.exe, or <c>null</c> if not found.</returns>
	/// <remarks>
	/// <para>
	/// <b>Discovery Priority:</b>
	/// <list type="number">
	/// <item><description>vswhere.exe (most reliable, if installed)</description></item>
	/// <item><description>Direct path probing in Program Files</description></item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Edition Search Order by Version:</b>
	/// <list type="bullet">
	/// <item><description><c>vs2026</c>: Enterprise → Professional → Community → Preview</description></item>
	/// <item><description><c>vs2026-insiders</c>: Preview → Enterprise → Professional → Community</description></item>
	/// <item><description><c>vs2022</c>: Enterprise → Professional → Community → Preview</description></item>
	/// <item><description><c>vs2022-preview</c>: Preview → Enterprise → Professional → Community</description></item>
	/// <item><description><c>null</c> (default): 2026 Insiders → 2026 → 2022 Preview → 2022</description></item>
	/// </list>
	/// </para>
	/// </remarks>
	[SupportedOSPlatform("windows")]
	public static string? FindDevenvPath(string? version)
	{
		if (!OperatingSystem.IsWindows())
			return null;

		// Define search order based on requested version
		// Each tuple: (year, edition, vswhere version range)
		var searchOrder = version switch
		{
			"vs2026-insiders" => new[]
			{
				("2026", "Preview", "[18.0,19.0)"),      // VS2026 Insiders/Preview
				("2026", "Enterprise", "[18.0,19.0)"),
				("2026", "Professional", "[18.0,19.0)"),
				("2026", "Community", "[18.0,19.0)"),
			},
			"vs2026" => new[]
			{
				("2026", "Enterprise", "[18.0,19.0)"),
				("2026", "Professional", "[18.0,19.0)"),
				("2026", "Community", "[18.0,19.0)"),
				("2026", "Preview", "[18.0,19.0)"),      // Fallback to preview if stable not found
			},
			"vs2022-preview" => new[]
			{
				("2022", "Preview", "[17.0,18.0)"),
				("2022", "Enterprise", "[17.0,18.0)"),
				("2022", "Professional", "[17.0,18.0)"),
				("2022", "Community", "[17.0,18.0)"),
			},
			"vs2022" => new[]
			{
				("2022", "Enterprise", "[17.0,18.0)"),
				("2022", "Professional", "[17.0,18.0)"),
				("2022", "Community", "[17.0,18.0)"),
				("2022", "Preview", "[17.0,18.0)"),
			},
			// Default: try newest first
			_ => new[]
			{
				("2026", "Preview", "[18.0,19.0)"),
				("2026", "Enterprise", "[18.0,19.0)"),
				("2026", "Professional", "[18.0,19.0)"),
				("2026", "Community", "[18.0,19.0)"),
				("2022", "Preview", "[17.0,18.0)"),
				("2022", "Enterprise", "[17.0,18.0)"),
				("2022", "Professional", "[17.0,18.0)"),
				("2022", "Community", "[17.0,18.0)"),
			}
		};

		// Try vswhere first (most reliable)
		var vswhereResult = TryVswhere(searchOrder);
		if (vswhereResult != null)
		{
			return vswhereResult;
		}

		// Fallback to direct path probing
		return ProbeDevenvPaths(searchOrder);
	}

	/// <summary>
	/// Try to find VS installation using vswhere.exe.
	/// </summary>
	[SupportedOSPlatform("windows")]
	private static string? TryVswhere((string year, string edition, string versionRange)[] searchOrder)
	{
		var vswhere = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			"Microsoft Visual Studio", "Installer", "vswhere.exe");

		if (!File.Exists(vswhere))
		{
			return null;
		}

		// Check if any Preview edition is in the search order (requires -prerelease flag)
		var needsPrerelease = searchOrder.Any(s => s.edition == "Preview");

		// Get unique version ranges to query
		var versionRanges = searchOrder.Select(s => s.versionRange).Distinct().ToArray();

		foreach (var versionRange in versionRanges)
		{
			try
			{
				// Build arguments: -prerelease is required to find Preview/Insiders editions
				var prereleaseArg = needsPrerelease ? " -prerelease" : "";
				var psi = new ProcessStartInfo(vswhere)
				{
					Arguments = $"-version {versionRange}{prereleaseArg} -property installationPath -latest",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var process = Process.Start(psi);
				if (process == null)
				{
					continue;
				}

				// CRITICAL: WaitForExit BEFORE ReadLine to avoid blocking forever
				// ReadLine on redirected stdout can block indefinitely if process hangs
				if (!process.WaitForExit(5000))
				{
					try { process.Kill(); }
					catch { /* ignore */ }
					continue;
				}

				// Now safe to read - process has exited
				var installPath = process.StandardOutput.ReadLine()?.Trim();

				if (!string.IsNullOrEmpty(installPath))
				{
					var devenvPath = Path.Combine(installPath, "Common7", "IDE", "devenv.exe");
					if (File.Exists(devenvPath))
					{
						return devenvPath;
					}
				}
			}
			catch
			{
				// Continue to next version range
			}
		}

		return null;
	}

	/// <summary>
	/// Probe standard VS installation paths in priority order.
	/// </summary>
	[SupportedOSPlatform("windows")]
	private static string? ProbeDevenvPaths((string year, string edition, string versionRange)[] searchOrder)
	{
		var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

		foreach (var (year, edition, _) in searchOrder)
		{
			var path = Path.Combine(programFiles, "Microsoft Visual Studio", year, edition, "Common7", "IDE", "devenv.exe");
			if (File.Exists(path))
			{
				return path;
			}
		}

		return null;
	}
}
