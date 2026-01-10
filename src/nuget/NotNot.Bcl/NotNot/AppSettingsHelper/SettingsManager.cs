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
public sealed class SettingsManager<TSettings> : IDisposable, IAsyncDisposable
	 where TSettings : class
{
	private readonly object _lock = new();
	private readonly Func<TSettings> _factory;
	private readonly Type _concreteType;
	private CancellationTokenSource? _debounceCts;
	private Task? _debounceTask;
	private bool _isDirty;
	private bool _disposed;
	private bool _suppressNotifications;
	private TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
	private TSettings? _proxy;
	private bool _proxyModeActive;

	private JsonNode? _originalJson;
	private JsonNode? _baseJsonSnapshot;
	private string[]? _loadedFilePaths;
	private IUserSettingsStorageProvider? _storageProvider;

	/// <summary>
	/// Creates a new SettingsManager using a factory function for settings instantiation.
	/// </summary>
	/// <param name="factory">Factory function that creates new TSettings instances.</param>
	/// <remarks>
	/// <para>Use this constructor for interface-based settings (Workflow B: POCO/DispatchProxy).</para>
	/// <para>The factory's return type determines the concrete type for JSON deserialization.</para>
	/// </remarks>
	public SettingsManager(Func<TSettings> factory)
	{
		_factory = factory ?? throw new ArgumentNullException(nameof(factory));
		// Capture concrete type from factory for deserialization (SME blocker fix)
		var sample = _factory();
		_concreteType = sample.GetType();
		Settings = sample;
	}

	/// <summary>
	/// Creates a new SettingsManager using Activator for types with parameterless constructors.
	/// </summary>
	/// <remarks>
	/// <para>Use this constructor for source-generated settings classes (Workflow A).</para>
	/// <para>Requires TSettings to have a public parameterless constructor.</para>
	/// </remarks>
	public SettingsManager() : this(() => Activator.CreateInstance<TSettings>()!) { }

	/// <summary>
	/// The loaded settings object. Property changes trigger change notifications
	/// (and auto-save if enabled).
	/// </summary>
	/// <remarks>
	/// <para>For source-generated types with ISettingsChangeAware, use this for change tracking.</para>
	/// <para><b>Warning</b>: After accessing <see cref="Proxy"/>, ISettingsChangeAware callbacks
	/// are disabled. The Proxy handles change detection instead.</para>
	/// </remarks>
	public TSettings Settings { get; private set; } = default!;

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

	/// <summary>
	/// Callback invoked when any setting property is modified.
	/// Use this to wire up external change notification (e.g., UI events).
	/// </summary>
	/// <remarks>
	/// <para>Called synchronously from the setter thread. Keep handlers fast and non-blocking.</para>
	/// <para>Not called during load/reload operations (only on user-initiated property changes).</para>
	/// </remarks>
	public Action? OnSettingsModified { get; set; }

	/// <summary>
	/// Gets a proxy wrapper that intercepts property setters for change notification.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Use this for POCO/interface-based settings that don't implement ISettingsChangeAware.
	/// For source-generated settings classes, use <see cref="Settings"/> directly.
	/// </para>
	/// <para>
	/// <b>Mode Selection</b>: Accessing this property activates Proxy Mode. Once activated,
	/// ISettingsChangeAware callbacks are disabled to prevent double notification.
	/// To reset to Settings Mode, call <see cref="ReloadAsync"/> or create a new manager instance.
	/// </para>
	/// <para>
	/// <b>Limitation</b>: Only top-level property mutations are detected. Nested object
	/// changes (e.g., Settings.Config.Value = x) bypass the proxy. Use flat structures only.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown if TSettings is not a public interface.
	/// </exception>
	public TSettings Proxy
	{
		get
		{
			lock (_lock)
			{
				if (_proxy == null)
				{
					if (!typeof(TSettings).IsInterface)
					{
						throw new InvalidOperationException(
							 $"Proxy requires TSettings '{typeof(TSettings).Name}' to be an interface. " +
							 $"Source-generated settings classes implement ISettingsChangeAware and work " +
							 $"with 'manager.Settings' directly for change tracking. " +
							 $"To use Proxy with source-generated types, use the generated interface " +
							 $"(e.g., SettingsManager<IAppSettings>).");
					}

					_proxy = SettingsProxy<TSettings>.Create(Settings, OnPropertyChanged);

					// Clear ISettingsChangeAware callback - proxy handles change detection now
					// This prevents double notification when TSettings implements both
					if (Settings is ISettingsChangeAware aware)
					{
						aware._SetChangeCallback(null);
					}
					_proxyModeActive = true;
				}
				return _proxy;
			}
		}
	}

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
				// Greenfield: let exceptions propagate - if user settings file is corrupt, we need to know
				using var userStream = new FileStream(UserSettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
				var userNode = await JsonNode.ParseAsync(userStream, cancellationToken: ct);
				if (userNode is JsonObject userObj)
				{
					finalMerged = (JsonObject)JsonSettingsUtils.MergeJson(finalMerged, userObj);
				}
			}

			ResetProxyMode();
			Settings = DeserializeToConcreteType(finalMerged) ?? _factory();
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

			ResetProxyMode();
			Settings = DeserializeToConcreteType(merged) ?? _factory();
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
			ResetProxyMode();
			Settings = config.Get<TSettings>() ?? _factory();

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
			ResetProxyMode();
			Settings = config.Get<TSettings>() ?? _factory();
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
	/// Registers a storage provider and attempts to load settings from it.
	/// Simpler than file-based: no base/user merge, no diffing.
	/// </summary>
	/// <param name="storage">The storage provider to use for persistence.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The loaded settings object.</returns>
	/// <remarks>
	/// <para>This method performs TWO functions:</para>
	/// <list type="number">
	/// <item>Registers the storage provider for future save operations</item>
	/// <item>Attempts to load existing settings from the provider</item>
	/// </list>
	/// <para>When using a storage provider:</para>
	/// <list type="bullet">
	/// <item>Settings are stored as a single JSON blob</item>
	/// <item>No layered merge - latest value wins</item>
	/// <item>Auto-save works the same way (debounced writes)</item>
	/// <item>ResetToDefaultsAsync deletes storage and creates new TSettings</item>
	/// </list>
	/// </remarks>
	public async ValueTask<TSettings> RegisterUserStorageAndTryLoad(
		 IUserSettingsStorageProvider storage,
		 CancellationToken ct = default)
	{
		_storageProvider = storage;
		_suppressNotifications = true;

		try
		{
			ResetProxyMode();
			var json = await storage.ReadAsync(ct);
			if (json != null)
			{
				try
				{
					var node = JsonNode.Parse(json);
					Settings = DeserializeToConcreteType(node) ?? _factory();
				}
				catch (JsonException)
				{
					// Corrupted or incompatible storage data - start fresh
					Settings = _factory();
				}
			}
			else
			{
				Settings = _factory();
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

			string json;
			lock (_lock)
			{
				if (!_isDirty) return;
				// Serialize while holding lock to capture consistent state
				// Do NOT clear _isDirty here - must wait for successful write
				json = JsonSerializer.Serialize(Settings, JsonSettingsUtils.DefaultOptions);
			}

			// Write can fail (quota exceeded, circuit disconnected, etc.)
			// If it throws, _isDirty remains true so retry will occur
			await _storageProvider.WriteAsync(json, ct);

			// Only mark clean AFTER successful write to prevent data loss
			lock (_lock)
			{
				_isDirty = false;
			}
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
			// Serialize while holding lock to capture consistent state
			// Do NOT clear _isDirty here - must wait for successful write
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

			// Write can fail (disk full, permissions, etc.)
			// If it throws, _isDirty remains true so retry will occur
			await File.WriteAllTextAsync(UserSettingsPath, merged.ToJsonString(JsonSettingsUtils.DefaultOptions), ct);
		}

		// Only mark clean AFTER successful write to prevent data loss
		// Note: If a mutation occurs while WriteAsync is in flight, _isDirty will be
		// set true again by the change callback. This is correct - next save cycle catches it.
		lock (_lock)
		{
			_isDirty = false;
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
				ResetProxyMode();
				Settings = _factory();
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
				ResetProxyMode();
				Settings = DeserializeToConcreteType(_baseJsonSnapshot) ?? _factory();
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
			ResetProxyMode();
			Settings = DeserializeToConcreteType(_baseJsonSnapshot) ?? _factory();
			WireChangeTracking();
		}
		finally
		{
			_suppressNotifications = false;
		}

		OnPropertyChanged();
	}

	/// <summary>
	/// Explicitly marks settings as dirty and schedules auto-save if enabled.
	/// Use for reference-type properties (like immutable collections) accessed via Settings
	/// rather than Proxy.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This method is a no-op if notifications are suppressed (during load operations).
	/// </para>
	/// <para>
	/// <b>When to use:</b> When accessing Settings directly (not Proxy) to modify
	/// reference-type properties like <see cref="System.Collections.Immutable.ImmutableList{T}"/>.
	/// </para>
	/// </remarks>
	/// <example>
	/// <code>
	/// manager.Settings.Filters = manager.Settings.Filters.Add("newItem");
	/// manager.MarkDirty(); // Notify that save is needed
	/// </code>
	/// </example>
	public void MarkDirty() => OnPropertyChanged();

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
#pragma warning disable NN_R005 // Dispose must not throw, but report errors via callback
			catch (Exception ex)
			{
				// Greenfield: surface dispose save errors via callback - they indicate real problems
				OnAutoSaveError?.Invoke(ex);
			}
#pragma warning restore NN_R005
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

	/// <summary>
	/// Resets proxy mode state, allowing Settings to be used for change tracking again.
	/// Called by all Load methods before assigning new Settings.
	/// </summary>
	private void ResetProxyMode()
	{
		lock (_lock)
		{
			_proxy = default;
			_proxyModeActive = false;
		}
	}

	/// <summary>
	/// Deserializes JSON to the concrete type captured from the factory.
	/// This enables interface-based TSettings to work with JSON deserialization.
	/// </summary>
	private TSettings? DeserializeToConcreteType(JsonNode? node)
	{
		if (node == null)
			return default;

		// Use runtime type from factory for deserialization (SME blocker fix)
		// This allows TSettings to be an interface while deserializing to concrete type
		var result = node.Deserialize(_concreteType, JsonSettingsUtils.DefaultOptions);
		return result as TSettings;
	}

	private void WireChangeTracking()
	{
		// Skip if proxy mode is active - proxy handles change detection
		if (_proxyModeActive)
			return;

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

		// External notification hook for UI change events
		OnSettingsModified?.Invoke();
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
				// P1-1: Use SaveCoreAsync to avoid self-deadlock
				await SaveCoreAsync(token);
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
			// Greenfield: let exceptions propagate - if user settings file is corrupt, we need to know
			var json = await File.ReadAllTextAsync(UserSettingsPath, ct);
			return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
		}
		return new JsonObject();
	}

	#endregion
}
