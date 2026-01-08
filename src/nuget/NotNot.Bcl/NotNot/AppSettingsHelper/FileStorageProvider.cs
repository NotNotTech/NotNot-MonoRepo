using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NotNot.AppSettingsHelper;

/// <summary>
/// IUserSettingsStorageProvider implementation using the local file system.
/// Stores settings as JSON in a single file at the specified path.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe for use with AppSettingsManager debounced auto-save.
/// The manager's internal lock + debounce pattern serializes writes.
/// </para>
/// <para>
/// Directory creation is handled automatically on first write.
/// </para>
/// </remarks>
public class FileUserSettingsStorageProvider : IUserSettingsStorageProvider
{
    private readonly string _filePath;

    /// <summary>
    /// Creates a file storage provider for the specified path.
    /// </summary>
    /// <param name="filePath">Full path to the settings JSON file.</param>
    /// <exception cref="ArgumentException">Thrown if filePath is null or whitespace.</exception>
    public FileUserSettingsStorageProvider(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>
    /// Gets the file path used by this provider.
    /// </summary>
    public string FilePath => _filePath;

    /// <inheritdoc/>
    public async ValueTask<string?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(_filePath, ct);
        }
        catch (IOException)
        {
            // File locked or inaccessible - return null, caller uses defaults
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(string json, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(CancellationToken ct = default)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExistsAsync(CancellationToken ct = default)
        => ValueTask.FromResult(File.Exists(_filePath));
}

