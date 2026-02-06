using System;
using System.Threading;
using System.Threading.Tasks;

namespace NotNot.AppSettingsHelper;

/// <summary>
/// Async storage abstraction for USER settings persistence.
/// Enables SettingsManager to use alternative backends such as localStorage, IndexedDB, cloud storage, etc.
/// </summary>
/// <remarks>
/// <para>
/// This interface is specifically for USER-level settings that may differ from base/default settings.
/// The naming clarifies that this is distinct from base configuration sources (like appsettings.json).
/// </para>
/// <para>
/// This interface provides a simple key-value-like contract where:
/// </para>
/// <list type="bullet">
/// <item>Each provider instance represents a single settings "document"</item>
/// <item>The storage key/location is determined at provider construction</item>
/// <item>All operations are async to support both local and remote backends</item>
/// </list>
/// </remarks>
public interface IUserSettingsStorageProvider
{
    /// <summary>
    /// Reads settings JSON from storage.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settings JSON string, or null if not found.</returns>
    ValueTask<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes settings JSON to storage.
    /// </summary>
    /// <param name="json">The settings JSON string to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask WriteAsync(string json, CancellationToken ct = default);

    /// <summary>
    /// Deletes settings from storage.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask DeleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if settings exist in storage.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if settings exist, false otherwise.</returns>
    ValueTask<bool> ExistsAsync(CancellationToken ct = default);
}

