namespace NotNot.Storage;

/// <summary>
/// File-based storage adapter with atomic write (temp file + rename).
/// Reads and writes raw JSON strings to the specified file path.
/// </summary>
public sealed class FileStorageAdapter : IStorageAdapter
{
	private readonly string _filePath;

	/// <summary>
	/// Creates a new file storage adapter for the specified path.
	/// </summary>
	/// <param name="filePath">Full path to the backing JSON file.</param>
	public FileStorageAdapter(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		_filePath = Path.GetFullPath(filePath);
	}

	/// <inheritdoc/>
	public async ValueTask<string?> ReadAsync(CancellationToken ct = default)
	{
		if (!File.Exists(_filePath))
			return null;

		return await File.ReadAllTextAsync(_filePath, ct);
	}

	/// <inheritdoc/>
	public async ValueTask WriteAsync(string data, CancellationToken ct = default)
	{
		var dir = Path.GetDirectoryName(_filePath);
		if (dir != null)
			Directory.CreateDirectory(dir);

		// Concurrency-safe atomic write (unique temp + per-path serialization + bounded external-lock retry).
		await AtomicFileWriter.WriteAtomicAsync(_filePath, data, ct: ct);
	}

	/// <inheritdoc/>
	public ValueTask DeleteAsync(CancellationToken ct = default)
	{
		if (File.Exists(_filePath))
			File.Delete(_filePath);

		return default;
	}

	/// <summary>
	/// Creates a <see cref="FileStorageAdapter"/> that stores data in the OS-specific local application data folder.
	/// On Windows: <c>%LOCALAPPDATA%/{appName}/{fileName}</c>.
	/// On Linux/macOS: <c>~/.local/share/{appName}/{fileName}</c>.
	/// </summary>
	/// <param name="appName">Application name (used as folder name).</param>
	/// <param name="fileName">File name (e.g., "settings.json").</param>
	public static FileStorageAdapter OsAppDataLocal(string appName, string fileName)
	{
		var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
		return new FileStorageAdapter(Path.Combine(folder, fileName));
	}

	/// <summary>
	/// Creates a <see cref="FileStorageAdapter"/> that stores data alongside the running executable.
	/// </summary>
	/// <param name="fileName">File name (e.g., "settings.json").</param>
	public static FileStorageAdapter OsExeDir(string fileName)
	{
		var exeDir = AppContext.BaseDirectory;
		return new FileStorageAdapter(Path.Combine(exeDir, fileName));
	}

	/// <summary>
	/// Creates a <see cref="FileStorageAdapter"/> that stores data in the user's home directory.
	/// On Windows: <c>%USERPROFILE%/{fileName}</c>.
	/// On Linux/macOS: <c>~/{fileName}</c>.
	/// </summary>
	/// <param name="fileName">File name (e.g., ".myapp-settings.json").</param>
	public static FileStorageAdapter UserHome(string fileName)
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return new FileStorageAdapter(Path.Combine(home, fileName));
	}
}
