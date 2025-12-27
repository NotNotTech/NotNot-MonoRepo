using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NotNot;

namespace NotNot.AppSettingsHelper;

/// <summary>
/// Manages loading, saving, and auto-saving of strongly-typed settings.
/// <para>
/// Provides a unified API for settings management with:
/// <list type="bullet">
/// <item>Multiple load workflows (streams, file paths, IConfiguration)</item>
/// <item>Opt-in debounced auto-save (500ms default)</item>
/// <item>Manual save with pending auto-save cancellation</item>
/// <item>Reload from disk (picks up external edits)</item>
/// <item>Reset to defaults (deletes user file)</item>
/// </list>
/// </para>
/// </summary>
/// <typeparam name="TSettings">The settings type, must be a generated settings class.</typeparam>
public sealed class AppSettingsManager<TSettings> : IDisposable, IAsyncDisposable
    where TSettings : class, new()
{
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceCts;
    private Task? _debounceTask;
    private bool _isDirty;
    private bool _disposed;
    private bool _suppressNotifications;
    private TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);

    private JsonNode? _originalJson;
    private JsonNode? _baseJsonSnapshot;
    private string[]? _loadedFilePaths;
    private ISettingsStorageProvider? _storageProvider;

    /// <summary>
    /// The loaded settings object. Property changes trigger change notifications
    /// (and auto-save if enabled).
    /// </summary>
    public TSettings Settings { get; private set; } = new();

    /// <summary>
    /// Path to the user settings file. Changes from defaults are persisted here.
    /// Defaults to "appsettings.user.json" in the app directory.
    /// </summary>
    public string UserSettingsPath { get; set; } = "appsettings.user.json";

    /// <summary>
    /// Whether auto-save is currently enabled.
    /// </summary>
    public bool IsAutoSaveEnabled { get; private set; }

    /// <summary>
    /// Whether save operations are available.
    /// False when loaded via <see cref="LoadFromConfiguration(IConfiguration)"/> (read-only mode).
    /// </summary>
    public bool CanSave => _originalJson != null;

    /// <summary>
    /// Callback invoked when an error occurs during auto-save.
    /// If not set, errors are silently ignored.
    /// </summary>
    public Action<Exception>? OnAutoSaveError { get; set; }

    #region Load Workflows (Save-Capable)

    /// <summary>
    /// Loads settings from file paths. Later files override earlier ones (layered merge).
    /// Enables save functionality.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="jsonFilePaths">Paths to JSON files to load and merge.</param>
    /// <returns>The loaded settings object.</returns>
    public async ValueTask<TSettings> LoadAsync(CancellationToken ct = default, params string[] jsonFilePaths)
    {
        _loadedFilePaths = jsonFilePaths;
        _suppressNotifications = true;

        try
        {
            // First, merge base files (without user file) to get _baseJsonSnapshot
            var baseStreams = new List<FileStream>();
            try
            {
                foreach (var path in jsonFilePaths)
                {
                    if (File.Exists(path))
                    {
                        baseStreams.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));
                    }
                }

                var baseMerged = await JsonSettingsUtils.MergeStreamsAsync(baseStreams, ct);
                _baseJsonSnapshot = baseMerged.DeepClone();
            }
            finally
            {
                foreach (var stream in baseStreams)
                {
                    await stream.DisposeAsync();
                }
            }

            // Now merge user settings on top (if exists)
            JsonObject finalMerged = _baseJsonSnapshot?.DeepClone()?.AsObject() ?? new JsonObject();
            if (File.Exists(UserSettingsPath))
            {
                try
                {
                    using var userStream = new FileStream(UserSettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                    var userNode = await JsonNode.ParseAsync(userStream, cancellationToken: ct);
                    if (userNode is JsonObject userObj)
                    {
                        finalMerged = (JsonObject)JsonSettingsUtils.MergeJson(finalMerged, userObj);
                    }
                }
                catch
                {
                    // Ignore user file errors, use base only
                }
            }

            Settings = JsonSettingsUtils.Deserialize<TSettings>(finalMerged) ?? new TSettings();
            _originalJson = finalMerged.DeepClone();
            WireChangeTracking();
            return Settings;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    /// <summary>
    /// Loads settings from streams. Later streams override earlier ones (layered merge).
    /// Enables save functionality.
    /// Note: For Clear() support, use LoadAsync(ct, filePaths) instead.
    /// </summary>
    /// <param name="baseStreams">Streams to merge, in order of priority (last wins).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded settings object.</returns>
    public async ValueTask<TSettings> LoadAsync(IEnumerable<Stream> baseStreams, CancellationToken ct = default)
    {
        _suppressNotifications = true;
        try
        {
            var merged = await JsonSettingsUtils.MergeStreamsAsync(baseStreams, ct);

            Settings = JsonSettingsUtils.Deserialize<TSettings>(merged) ?? new TSettings();
            // When loading from streams, we can't distinguish base from user,
            // so base snapshot is the merged result (Clear() will reset to this)
            _baseJsonSnapshot = merged.DeepClone();
            _originalJson = merged.DeepClone();
            WireChangeTracking();
            return Settings;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    /// <summary>
    /// Loads settings from IConfiguration with base JSON path for diffing.
    /// Enables save functionality (changes are diff'd against base JSON).
    /// </summary>
    /// <param name="config">The IConfiguration to bind from.</param>
    /// <param name="baseJsonPath">Path to base JSON file for diff calculation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded settings object.</returns>
    public async ValueTask<TSettings> LoadFromConfigurationAsync(
        IConfiguration config,
        string baseJsonPath,
        CancellationToken ct = default)
    {
        _suppressNotifications = true;
        try
        {
            Settings = config.Get<TSettings>() ?? new TSettings();

            if (File.Exists(baseJsonPath))
            {
                var json = await File.ReadAllTextAsync(baseJsonPath, ct);
                _baseJsonSnapshot = JsonNode.Parse(json);
                _originalJson = _baseJsonSnapshot?.DeepClone();
            }
            else
            {
                _baseJsonSnapshot = new JsonObject();
                _originalJson = new JsonObject();
            }

            WireChangeTracking();
            return Settings;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    #endregion

    #region Load Workflows (Read-Only)

    /// <summary>
    /// Loads settings from IConfiguration (sync, read-only mode).
    /// Save operations will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="config">The IConfiguration to bind from.</param>
    /// <returns>The loaded settings object.</returns>
    public TSettings LoadFromConfiguration(IConfiguration config)
    {
        _suppressNotifications = true;
        try
        {
            Settings = config.Get<TSettings>() ?? new TSettings();
            _originalJson = null; // Signals read-only mode
            WireChangeTracking();
            return Settings;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    #endregion

    #region Load Workflows (Storage Provider)

    /// <summary>
    /// Loads settings from a storage provider (e.g., localStorage, IndexedDB).
    /// Simpler than file-based: no base/user merge, no diffing.
    /// </summary>
    /// <param name="storage">The storage provider to use for persistence.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded settings object.</returns>
    /// <remarks>
    /// When using a storage provider:
    /// <list type="bullet">
    /// <item>Settings are stored as a single JSON blob</item>
    /// <item>No layered merge - latest value wins</item>
    /// <item>Auto-save works the same way (debounced writes)</item>
    /// <item>ResetToDefaultsAsync deletes storage and creates new TSettings</item>
    /// </list>
    /// </remarks>
    public async ValueTask<TSettings> LoadFromStorageAsync(
        ISettingsStorageProvider storage,
        CancellationToken ct = default)
    {
        _storageProvider = storage;
        _suppressNotifications = true;

        try
        {
            var json = await storage.ReadAsync(ct);
            if (json != null)
            {
                try
                {
                    var node = JsonNode.Parse(json);
                    Settings = JsonSettingsUtils.Deserialize<TSettings>(node) ?? new TSettings();
                }
                catch (JsonException)
                {
                    // Corrupted or incompatible storage data - start fresh
                    Settings = new TSettings();
                }
            }
            else
            {
                Settings = new TSettings();
            }

            _originalJson = JsonSettingsUtils.SerializeToNode(Settings);
            WireChangeTracking();
            return Settings;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    #endregion

    #region Save Operations

    /// <summary>
    /// Immediately saves settings to the user file. Cancels any pending auto-save.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown in read-only mode.</exception>
    public async ValueTask SaveAsync(CancellationToken ct = default)
    {
        await CancelPendingAutoSaveAsync();
        await SaveCoreAsync(ct);
    }

    /// <summary>
    /// Core save logic without cancellation of pending auto-saves.
    /// Used by both public SaveAsync and internal debounce task.
    /// </summary>
    private async ValueTask SaveCoreAsync(CancellationToken ct = default)
    {
        // Storage provider path - simpler than file-based (no diffing)
        if (_storageProvider != null)
        {
            if (Settings == null) return; // Defensive null guard
            lock (_lock)
            {
                if (!_isDirty) return;
                _isDirty = false;
            }
            var json = JsonSerializer.Serialize(Settings, JsonSettingsUtils.DefaultOptions);
            await _storageProvider.WriteAsync(json, ct);
            _originalJson = JsonSettingsUtils.SerializeToNode(Settings); // Update tracking state
            return;
        }

        // File-based path - requires _originalJson to be set
        if (!CanSave)
        {
            throw new InvalidOperationException(
                "Save not supported. Use a save-capable Load workflow " +
                "(LoadAsync with streams/paths, or LoadFromConfigurationAsync with baseJsonPath).");
        }

        JsonNode? serialized;
        lock (_lock)
        {
            if (!_isDirty) return;
            _isDirty = false;
            serialized = JsonSettingsUtils.SerializeToNode(Settings);
        }

        var diff = JsonSettingsUtils.ComputeDiff(_originalJson, serialized);
        if (diff != null && diff is JsonObject diffObj && diffObj.Count > 0)
        {
            var userJson = await LoadUserFileAsync(ct);
            var merged = JsonSettingsUtils.MergeJson(userJson, diff);

            // Ensure directory exists
            var dir = Path.GetDirectoryName(UserSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(UserSettingsPath, merged.ToJsonString(JsonSettingsUtils.DefaultOptions), ct);
        }
        _originalJson = serialized;
    }

    /// <summary>
    /// Reloads settings from disk, discarding in-memory changes.
    /// Picks up external edits to both base files and user file.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if not loaded from file paths.</exception>
    public async ValueTask ReloadAsync(CancellationToken ct = default)
    {
        if (_loadedFilePaths == null)
        {
            throw new InvalidOperationException(
                "ReloadAsync requires file paths. Use LoadAsync with file paths instead of streams.");
        }

        var wasAutoSaveEnabled = IsAutoSaveEnabled;
        await DisableAutoSaveAsync(saveNow: false, ct);

        await LoadAsync(ct, _loadedFilePaths);

        if (wasAutoSaveEnabled)
        {
            EnableAutoSave(_debounceInterval);
        }
    }

    /// <summary>
    /// Deletes the user settings file and reloads base settings.
    /// This is a hard reset - the file is deleted immediately.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown in read-only mode.</exception>
    public async ValueTask ResetToDefaultsAsync(CancellationToken ct = default)
    {
        // Storage provider path - delete storage and create fresh defaults
        if (_storageProvider != null)
        {
            await DisableAutoSaveAsync(saveNow: false, ct);
            await _storageProvider.DeleteAsync(ct);
            _suppressNotifications = true;
            try
            {
                Settings = new TSettings();
                _originalJson = JsonSettingsUtils.SerializeToNode(Settings);
                WireChangeTracking();
            }
            finally
            {
                _suppressNotifications = false;
            }
            EnableAutoSave(); // Re-enable auto-save after reset
            return;
        }

        // File-based path
        if (!CanSave)
        {
            throw new InvalidOperationException("ResetToDefaultsAsync not supported in read-only mode.");
        }

        await DisableAutoSaveAsync(saveNow: false, ct);

        if (File.Exists(UserSettingsPath))
        {
            File.Delete(UserSettingsPath);
        }

        if (_loadedFilePaths != null)
        {
            await LoadAsync(ct, _loadedFilePaths);
        }
        else if (_baseJsonSnapshot != null)
        {
            _suppressNotifications = true;
            try
            {
                Settings = JsonSettingsUtils.Deserialize<TSettings>(_baseJsonSnapshot) ?? new TSettings();
                _originalJson = _baseJsonSnapshot.DeepClone();
                WireChangeTracking();
            }
            finally
            {
                _suppressNotifications = false;
            }
        }
    }

    /// <summary>
    /// Resets settings to defaults without deleting user file immediately.
    /// Triggers change notification, so auto-save will persist the cleared state
    /// after debounce (if enabled).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no base settings snapshot exists.</exception>
    public void Clear()
    {
        if (_baseJsonSnapshot == null)
        {
            throw new InvalidOperationException(
                "Clear() is not supported when loaded from IConfiguration without a base JSON path. " +
                "Use LoadAsync with file paths/streams, or LoadFromConfigurationAsync with baseJsonPath.");
        }

        _suppressNotifications = true;
        try
        {
            Settings = JsonSettingsUtils.Deserialize<TSettings>(_baseJsonSnapshot) ?? new TSettings();
            WireChangeTracking();
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnPropertyChanged();
    }

    #endregion

    #region Auto-Save Control

    /// <summary>
    /// Enables auto-save with optional custom debounce interval.
    /// </summary>
    /// <param name="debounce">Debounce interval. Defaults to 500ms.</param>
    /// <exception cref="InvalidOperationException">Thrown in read-only mode.</exception>
    public void EnableAutoSave(TimeSpan? debounce = null)
    {
        if (!CanSave)
        {
            throw new InvalidOperationException("Cannot enable auto-save in read-only mode.");
        }

        _debounceInterval = debounce ?? TimeSpan.FromMilliseconds(500);
        IsAutoSaveEnabled = true;
    }

    /// <summary>
    /// Disables auto-save and optionally saves immediately.
    /// </summary>
    /// <param name="saveNow">If true, saves immediately if dirty.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask DisableAutoSaveAsync(bool saveNow = false, CancellationToken ct = default)
    {
        IsAutoSaveEnabled = false;
        await CancelPendingAutoSaveAsync();

        if (saveNow && _isDirty && CanSave)
        {
            await SaveAsync(ct);
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Disposes the manager, blocking on final save if dirty.
    /// Prefer <see cref="DisposeAsync"/> for async disposal.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        if (_isDirty && CanSave)
        {
            try
            {
                SaveAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Best effort save on sync dispose
            }
        }
    }

    /// <summary>
    /// Asynchronously disposes the manager with final save if dirty.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await CancelPendingAutoSaveAsync();

        if (_isDirty && CanSave)
        {
            await SaveAsync();
        }
    }

    #endregion

    #region Private Helpers

    private void WireChangeTracking()
    {
        if (Settings is ISettingsChangeAware aware)
        {
            aware._SetChangeCallback(OnPropertyChanged);
        }
    }

    private void OnPropertyChanged()
    {
        if (_suppressNotifications) return;

        lock (_lock)
        {
            _isDirty = true;
        }

        if (IsAutoSaveEnabled)
        {
            ScheduleDebouncedSave();
        }
    }

    private void ScheduleDebouncedSave()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _debounceTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceInterval, token);
                if (!token.IsCancellationRequested)
                {
                    // P1-1: Use SaveCoreAsync to avoid self-deadlock
                    await SaveCoreAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on cancel
            }
            catch (Exception ex)
            {
                // P1-6: Don't swallow exceptions silently
                OnAutoSaveError?.Invoke(ex);
            }
        }, token);
    }

    private async ValueTask CancelPendingAutoSaveAsync()
    {
        if (_debounceCts != null)
        {
            _debounceCts.Cancel();
            try
            {
                if (_debounceTask != null)
                {
                    await _debounceTask;
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            _debounceCts.Dispose();
            _debounceCts = null;
        }
    }

    private async ValueTask<JsonObject> LoadUserFileAsync(CancellationToken ct)
    {
        if (File.Exists(UserSettingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(UserSettingsPath, ct);
                return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                return new JsonObject();
            }
        }
        return new JsonObject();
    }

    #endregion
}
