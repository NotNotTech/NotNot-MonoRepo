namespace NotNot.Storage;

/// <summary>
/// In-memory storage adapter. Data survives only for the lifetime of this instance.
/// Useful for testing or ephemeral scenarios where persistence is not required.
/// </summary>
public sealed class EphemeralMemoryStorageAdapter : IStorageAdapter
{
	private string? _data;

	/// <inheritdoc/>
	public ValueTask<string?> ReadAsync(CancellationToken ct = default) => new(_data);

	/// <inheritdoc/>
	public ValueTask WriteAsync(string data, CancellationToken ct = default)
	{
		_data = data;
		return default;
	}

	/// <inheritdoc/>
	public ValueTask DeleteAsync(CancellationToken ct = default)
	{
		_data = null;
		return default;
	}
}
