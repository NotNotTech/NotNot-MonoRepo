using System.Text.Json;

namespace NotNot.Storage;

/// <summary>
/// Configuration for <see cref="SimpleStorageManager{TData}"/>.
/// </summary>
public record SimpleStorageOptions
{
	/// <summary>
	/// JSON serialization options. When null, defaults to camelCase + WriteIndented.
	/// </summary>
	public JsonSerializerOptions? JsonSerializerOptions { get; init; }

	/// <summary>
	/// Delay before flushing in-memory changes to backing storage. Default: 1 second.
	/// </summary>
	public TimeSpan WriteDebounce { get; init; } = TimeSpan.FromSeconds(1);

	/// <summary>
	/// Delay before re-reading backing storage after <see cref="SimpleStorageManager{TData}.NotifyExternalChange"/>. Default: 1 second.
	/// </summary>
	public TimeSpan ReadDebounce { get; init; } = TimeSpan.FromSeconds(1);
}
