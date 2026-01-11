namespace NotNot.Platform.Desktop;

using System.Diagnostics;

/// <summary>
/// Desktop file system utilities with platform-specific implementations.
/// </summary>
/// <remarks>
/// <para>
/// This class provides file system operations that require platform-specific
/// implementations (Windows, macOS, Linux). Operations are best-effort and
/// fail silently to avoid disrupting application flow.
/// </para>
/// <para>
/// <b>Platform Support:</b>
/// <list type="bullet">
/// <item><description>Windows: Full support via explorer.exe</description></item>
/// <item><description>macOS: Full support via open command</description></item>
/// <item><description>Linux: Partial support via xdg-open (opens parent folder only, no file selection)</description></item>
/// </list>
/// </para>
/// </remarks>
public static class FileSystemHelper
{
	/// <summary>
	/// Opens the native file explorer and highlights/selects the specified file.
	/// </summary>
	/// <param name="filePath">Absolute path to the file to reveal.</param>
	/// <remarks>
	/// <para>
	/// This is a best-effort operation. If the file or folder doesn't exist,
	/// or the platform command fails, the method returns silently without throwing.
	/// </para>
	/// <para>
	/// <b>Platform Behavior:</b>
	/// <list type="bullet">
	/// <item><description><b>Windows:</b> Opens Explorer with file selected (<c>explorer.exe /select,"path"</c>)</description></item>
	/// <item><description><b>macOS:</b> Opens Finder with file selected (<c>open -R "path"</c>)</description></item>
	/// <item><description><b>Linux:</b> Opens parent folder in default file manager (<c>xdg-open "folder"</c>). Note: File selection is not standardized across Linux file managers.</description></item>
	/// <item><description><b>Other:</b> No-op (silent return)</description></item>
	/// </list>
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// // Reveal a log file in the OS file explorer
	/// FileSystemHelper.RevealInOsGui(@"C:\Logs\app.log");
	///
	/// // Works on macOS too
	/// FileSystemHelper.RevealInOsGui("/Users/dev/project/file.txt");
	/// </code>
	/// </example>
	public static void RevealInOsGui(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return;

		try
		{
			if (OperatingSystem.IsWindows())
			{
				RevealInWindows(filePath);
			}
			else if (OperatingSystem.IsMacOS())
			{
				RevealInMacOS(filePath);
			}
			else if (OperatingSystem.IsLinux())
			{
				RevealInLinux(filePath);
			}
			// Other platforms: silent no-op
		}
		catch
		{
			// Best effort - silently fail
			// File explorer launch is a UX convenience, not critical functionality
		}
	}

	/// <summary>
	/// Windows implementation using explorer.exe /select
	/// </summary>
	private static void RevealInWindows(string filePath)
	{
		var folderPath = Path.GetDirectoryName(filePath);
		if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
			return;

		// Use /select to highlight the specific file in Explorer
		// Quote the path to handle spaces
		Process.Start(new ProcessStartInfo
		{
			FileName = "explorer.exe",
			Arguments = $"/select,\"{filePath}\"",
			UseShellExecute = true
		});
	}

	/// <summary>
	/// macOS implementation using open -R
	/// </summary>
	private static void RevealInMacOS(string filePath)
	{
		if (!File.Exists(filePath) && !Directory.Exists(filePath))
			return;

		// open -R reveals the file in Finder
		Process.Start(new ProcessStartInfo
		{
			FileName = "open",
			ArgumentList = { "-R", filePath },
			UseShellExecute = false
		});
	}

	/// <summary>
	/// Linux implementation using xdg-open (opens parent folder only)
	/// </summary>
	/// <remarks>
	/// Linux file managers (Nautilus, Dolphin, Thunar, etc.) don't have a
	/// standardized "reveal and select" command. xdg-open opens the containing
	/// folder but cannot select the specific file.
	/// </remarks>
	private static void RevealInLinux(string filePath)
	{
		// If filePath is itself a directory, open it directly
		// Otherwise open its parent folder
		string targetFolder;
		if (Directory.Exists(filePath))
			targetFolder = filePath;
		else
			targetFolder = Path.GetDirectoryName(filePath) ?? string.Empty;

		if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
			return;

		// xdg-open opens the folder in the default file manager
		// Note: Cannot select specific file - Linux limitation
		Process.Start(new ProcessStartInfo
		{
			FileName = "xdg-open",
			ArgumentList = { targetFolder },
			UseShellExecute = false
		});
	}
}
