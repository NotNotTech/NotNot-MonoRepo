namespace NotNot.Storage;

/// <summary>
/// No-op storage adapter. All operations are no-ops; reads return null.
/// Use when persistence is not needed (e.g., in-memory-only mode).
/// </summary>
public sealed class NoneStorageAdapter : IStorageAdapter
{
	/// <summary>
	/// Singleton instance.
	/// </summary>
	public static readonly NoneStorageAdapter Instance = new();

	private NoneStorageAdapter() { }

	/// <inheritdoc/>
	public ValueTask<string?> ReadAsync(CancellationToken ct = default) => new((string?)null);

	/// <inheritdoc/>
	public ValueTask WriteAsync(string data, CancellationToken ct = default) => default;

	/// <inheritdoc/>
	public ValueTask DeleteAsync(CancellationToken ct = default) => default;
}
