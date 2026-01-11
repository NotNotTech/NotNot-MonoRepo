namespace NotNot.Platform.Desktop;

using System.Diagnostics;

/// <summary>
/// Cross-platform IDE launching utilities.
/// Opens files at specific line numbers in common code editors.
/// </summary>
/// <remarks>
/// <para>
/// This class provides IDE launch operations that work across Windows, macOS, and Linux.
/// Operations are best-effort and fail silently to avoid disrupting application flow.
/// </para>
/// <para>
/// <b>Supported Editors:</b>
/// <list type="bullet">
/// <item><description>VSCode: Full cross-platform support via <c>code</c> command</description></item>
/// <item><description>VSCode Insiders: Full cross-platform support via <c>code-insiders</c> command</description></item>
/// <item><description>JetBrains Rider: Full cross-platform support via <c>rider64</c> (Windows) or <c>rider</c> (macOS/Linux)</description></item>
/// <item><description>Visual Studio: Windows-only, see <see cref="OpenInVisualStudio"/> overloads</description></item>
/// </list>
/// </para>
/// <para>
/// <b>PATH Requirements:</b>
/// Editors must be accessible via PATH environment variable. Most installers add this automatically.
/// macOS: Shell command wrappers installed to <c>/usr/local/bin/</c> by default.
/// Linux: Package managers typically add to PATH; Toolbox/Snap installs may require manual setup.
/// </para>
/// </remarks>
public static partial class IdeHelper
{
	/// <summary>
	/// Opens a file in Visual Studio Code at the specified line.
	/// </summary>
	/// <param name="filePath">Absolute path to the file to open.</param>
	/// <param name="line">Line number to navigate to (1-based).</param>
	/// <param name="insiders">If true, use VSCode Insiders (<c>code-insiders</c>) instead of stable (<c>code</c>).</param>
	/// <remarks>
	/// <para>
	/// This is a best-effort operation. If VSCode is not installed, the file doesn't exist,
	/// or the launch fails, the method returns silently without throwing.
	/// </para>
	/// <para>
	/// <b>Platform Behavior:</b>
	/// <list type="bullet">
	/// <item><description><b>Windows:</b> Uses <c>code --goto "path:line"</c> with UseShellExecute</description></item>
	/// <item><description><b>macOS:</b> Uses <c>code --goto "path:line"</c> (requires shell command installed)</description></item>
	/// <item><description><b>Linux:</b> Uses <c>code --goto "path:line"</c></description></item>
	/// </list>
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// // Open a file at line 42
	/// IdeHelper.OpenInVSCode(@"C:\Projects\MyApp\Program.cs", 42);
	///
	/// // Use VSCode Insiders
	/// IdeHelper.OpenInVSCode(@"C:\Projects\MyApp\Program.cs", 42, insiders: true);
	/// </code>
	/// </example>
	public static void OpenInVSCode(string filePath, int line, bool insiders = false)
	{
		if (string.IsNullOrEmpty(filePath) || line < 1)
			return;

		try
		{
			var command = insiders ? "code-insiders" : "code";
			var psi = new ProcessStartInfo(command)
			{
				UseShellExecute = OperatingSystem.IsWindows()
			};
			psi.ArgumentList.Add("--goto");
			psi.ArgumentList.Add($"{filePath}:{line}");

			Process.Start(psi);
		}
		catch
		{
			// Best effort - silently fail
			// IDE launch is a UX convenience, not critical functionality
		}
	}

	/// <summary>
	/// Opens a file in JetBrains Rider at the specified line.
	/// </summary>
	/// <param name="filePath">Absolute path to the file to open.</param>
	/// <param name="line">Line number to navigate to (1-based).</param>
	/// <remarks>
	/// <para>
	/// This is a best-effort operation. If Rider is not installed, the file doesn't exist,
	/// or the launch fails, the method returns silently without throwing.
	/// </para>
	/// <para>
	/// <b>Platform Behavior:</b>
	/// <list type="bullet">
	/// <item><description><b>Windows:</b> Uses <c>rider64 --line N "path"</c></description></item>
	/// <item><description><b>macOS/Linux:</b> Uses <c>rider --line N "path"</c></description></item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Installation Notes:</b>
	/// JetBrains Toolbox installs may not add Rider to PATH by default.
	/// Enable "Generate shell scripts" in Toolbox settings for PATH access.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// // Open a file at line 100
	/// IdeHelper.OpenInRider(@"C:\Projects\MyApp\Program.cs", 100);
	/// </code>
	/// </example>
	public static void OpenInRider(string filePath, int line)
	{
		if (string.IsNullOrEmpty(filePath) || line < 1)
			return;

		try
		{
			// Windows uses rider64, macOS/Linux use rider
			var command = OperatingSystem.IsWindows() ? "rider64" : "rider";
			var psi = new ProcessStartInfo(command)
			{
				UseShellExecute = OperatingSystem.IsWindows()
			};
			psi.ArgumentList.Add("--line");
			psi.ArgumentList.Add(line.ToString());
			psi.ArgumentList.Add(filePath);

			Process.Start(psi);
		}
		catch
		{
			// Best effort - silently fail
		}
	}
}
