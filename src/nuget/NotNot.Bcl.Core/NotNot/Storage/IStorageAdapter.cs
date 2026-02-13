namespace NotNot.Storage;

/// <summary>
/// Strategy interface for backing storage. Implementations handle read/write/delete of raw JSON strings.
/// </summary>
public interface IStorageAdapter
{
	ValueTask<string?> ReadAsync(CancellationToken ct = default);
	ValueTask WriteAsync(string data, CancellationToken ct = default);
	ValueTask DeleteAsync(CancellationToken ct = default);
}
