using System.Text.Json;
using System.Text.Json.Nodes;
using NotNot.AppSettingsHelper;
using NotNot.Concurrency;

namespace NotNot.Storage;

/// <summary>
/// Generic storage manager that serializes/deserializes a POCO to/from a backing <see cref="IStorageAdapter"/>.
/// Provides debounced auto-save on mutation and debounced reload on external change notification.
/// </summary>
/// <typeparam name="TData">
/// The data type to persist. Must be a reference type with a parameterless constructor.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Lifecycle:</b> Call <see cref="InitializeAsync"/> before accessing <see cref="Data"/>.
/// The manager loads existing data from the adapter (or creates a new <typeparamref name="TData"/> instance if none exists).
/// </para>
/// <para>
/// <b>Mutation tracking:</b> Use <see cref="Update"/> to mutate <see cref="Data"/> with automatic dirty-tracking
/// (generation counter) and debounced persistence. Direct mutations to <see cref="Data"/> are NOT tracked.
/// </para>
/// <para>
/// <b>Thread safety:</b> <see cref="Update"/> and <see cref="SetData"/> acquire a lock around mutation + dirty flag.
/// Serialization in <see cref="WriteCoreAsync"/> also acquires the lock to prevent reading partially-mutated state.
/// All adapter I/O is serialized via an <c>AsyncLock</c> (<c>_ioGate</c>) to prevent concurrent read/write races.
/// </para>
/// </remarks>
public sealed class SimpleStorageManager<TData> : IAsyncDisposable where TData : class, new()
{
	private readonly IStorageAdapter _adapter;
	private readonly SimpleStorageOptions _options;
	private readonly JsonSerializerOptions _jsonOptions;
	private readonly string? _initialDataJson;
	private readonly object _lock = new();
	private readonly SemaphoreSlim _initGate = new(1, 1);
	private readonly AsyncLock _ioGate = new();

	private TData? _data;
	private long _dirtyGeneration;
	private long _writtenGeneration;
	private bool _isInitialized;
	private bool _isDisposed;

	private Debouncer? _writeDebouncer;
	private Debouncer? _readDebouncer;

	/// <summary>
	/// Raised after <see cref="Data"/> is mutated via <see cref="Update"/> or <see cref="SetData"/>.
	/// Also raised after <see cref="ReloadAsync"/> completes.
	/// </summary>
	public event Action? OnDataChanged;

	/// <summary>
	/// Raised after data is loaded from the backing store (during <see cref="InitializeAsync"/> or <see cref="ReloadAsync"/>).
	/// </summary>
	public event Action? OnDataLoaded;

	/// <summary>
	/// Raised when an error occurs during background write or read operations.
	/// </summary>
	public event Action<Exception>? OnError;

	/// <summary>
	/// Creates a new storage manager with the specified adapter and default options.
	/// </summary>
	/// <param name="adapter">The backing storage adapter.</param>
	/// <param name="options">Optional configuration. When null, defaults are used.</param>
	public SimpleStorageManager(IStorageAdapter adapter, SimpleStorageOptions? options = null)
	{
		_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		_options = options ?? new SimpleStorageOptions();
		_jsonOptions = _options.JsonSerializerOptions ?? DefaultJsonOptions;
	}

	/// <summary>
	/// Creates a new storage manager seeded with an existing <typeparamref name="TData"/> instance.
	/// On load, stored JSON is deep-merged onto the serialized <paramref name="initialData"/>:
	/// nested objects are patched (deep merge), arrays and leaf values are replaced wholesale.
	/// Properties absent from stored JSON retain their <paramref name="initialData"/> values.
	/// Properties with explicit <c>null</c> in stored JSON are removed from the merged result;
	/// STJ assigns <c>default(T)</c> for the missing property (0 for int, null for reference types, etc.),
	/// NOT the C# property initializer value.
	/// </summary>
	/// <param name="adapter">The backing storage adapter.</param>
	/// <param name="initialData">
	/// The starting state whose values serve as defaults. Serialized to JSON at construction time
	/// and reused on every load/reload — the original object is not retained.
	/// </param>
	/// <param name="options">Optional configuration. When null, defaults are used.</param>
	public SimpleStorageManager(IStorageAdapter adapter, TData initialData, SimpleStorageOptions? options = null)
	{
		_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		ArgumentNullException.ThrowIfNull(initialData);
		_options = options ?? new SimpleStorageOptions();
		_jsonOptions = _options.JsonSerializerOptions ?? DefaultJsonOptions;
		_initialDataJson = JsonSerializer.Serialize(initialData, _jsonOptions);
	}

	private static readonly JsonSerializerOptions DefaultJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	/// <summary>
	/// Loads data from the backing store. Must be called before accessing <see cref="Data"/>.
	/// Safe to call multiple times (idempotent after first call).
	/// </summary>
	public async Task InitializeAsync(CancellationToken ct = default)
	{
		if (_isInitialized) return;
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));

		await _initGate.WaitAsync(ct);
		try
		{
			if (_isInitialized) return;  // Double-check after acquiring

			await ReadCoreAsync(ct);

			_writeDebouncer = new Debouncer(
				async () =>
				{
					if (_isDisposed) return;
					await WriteCoreAsync();
				},
				(int)_options.WriteDebounce.TotalMilliseconds);

			_readDebouncer = new Debouncer(
				async () =>
				{
					if (_isDisposed) return;
					await ReadCoreAsync();
					OnDataChanged?.Invoke();
				},
				(int)_options.ReadDebounce.TotalMilliseconds);

			_isInitialized = true;
		}
		finally
		{
			_initGate.Release();
		}
	}

	/// <summary>
	/// The current in-memory data object.
	/// Direct mutations are NOT tracked. Use <see cref="Update"/> to mutate with automatic persistence.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	public TData Data
	{
		get
		{
			if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
			return _data!;
		}
	}

	/// <summary>
	/// Mutates the current data under lock and schedules a debounced write to backing storage.
	/// The lock ensures serialization in <see cref="WriteCoreAsync"/> cannot read partially-mutated state.
	/// </summary>
	/// <param name="mutator">Action that mutates the current <see cref="Data"/> object.</param>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
	public void Update(Action<TData> mutator)
	{
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));

		lock (_lock)
		{
			Interlocked.Increment(ref _dirtyGeneration);  // Mark dirty FIRST — worst case we write unchanged data
			mutator(_data!);
		}
		_writeDebouncer!.Trigger();
		OnDataChanged?.Invoke();
	}

	/// <summary>
	/// Replaces the entire data object under lock and schedules a debounced write to backing storage.
	/// </summary>
	/// <param name="newData">The new data object. Must not be null.</param>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
	public void SetData(TData newData)
	{
		ArgumentNullException.ThrowIfNull(newData);
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));

		lock (_lock)
		{
			_data = newData;
			Interlocked.Increment(ref _dirtyGeneration);
		}
		_writeDebouncer!.Trigger();
		OnDataChanged?.Invoke();
	}

	/// <summary>
	/// Immediately flushes any pending dirty data to the backing store.
	/// Call before disposal to ensure all changes are persisted.
	/// </summary>
	public async Task FlushAsync(CancellationToken ct = default)
	{
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));
		if (!_isInitialized) return;
		await WriteCoreAsync(ct);  // WriteCoreAsync checks generation counter under lock
	}

	/// <summary>
	/// Reloads data from the backing store, replacing the in-memory state.
	/// Raises <see cref="OnDataLoaded"/> and <see cref="OnDataChanged"/>.
	/// </summary>
	public async Task ReloadAsync(CancellationToken ct = default)
	{
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");

		await ReadCoreAsync(ct);
		OnDataChanged?.Invoke();
	}

	/// <summary>
	/// Notifies the manager that the backing store was changed externally.
	/// Schedules a debounced reload.
	/// </summary>
	public void NotifyExternalChange()
	{
		if (_isDisposed) return;
		if (!_isInitialized) return;

		_readDebouncer!.Trigger();
	}

	/// <summary>
	/// Serializes current data to JSON under lock and writes to the backing store.
	/// </summary>
	private async Task WriteCoreAsync(CancellationToken ct = default)
	{
		string json;
		long capturedGeneration;
		lock (_lock)
		{
			if (Volatile.Read(ref _dirtyGeneration) == Volatile.Read(ref _writtenGeneration)) return;
			capturedGeneration = _dirtyGeneration;
			json = JsonSerializer.Serialize(_data, _jsonOptions);
		}

		using (await _ioGate.LockAsync(ct))
		{
			try
			{
				await _adapter.WriteAsync(json, ct);
				// Advance _writtenGeneration to what we serialized.
				// _ioGate serializes writes, so no concurrent writer can race here.
				// If _dirtyGeneration advanced past capturedGeneration (concurrent Update()),
				// the gap remains and the next debounced write picks up the newer data.
				Volatile.Write(ref _writtenGeneration, capturedGeneration);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;  // Propagate cancellation — not an error
			}
			// Adapter write failures are non-fatal — surface via OnError event, do not crash the app.
#pragma warning disable NN_R005
			catch (Exception ex)
#pragma warning restore NN_R005
			{
				OnError?.Invoke(ex);
			}
		}
	}

	/// <summary>
	/// Reads from the backing store and deserializes into <typeparamref name="TData"/>.
	/// When <c>_initialDataJson</c> is set, deep-merges stored JSON onto the defaults
	/// (objects patched, arrays/leaves replaced). Otherwise creates a new default instance.
	/// </summary>
	private async Task ReadCoreAsync(CancellationToken ct = default)
	{
		using (await _ioGate.LockAsync(ct))
		{
			try
			{
				var json = await _adapter.ReadAsync(ct);

				TData result;
				if (_initialDataJson != null)
				{
					// IMPORTANT: Must pass _jsonOptions (camelCase) — JsonSettingsUtils.DefaultOptions uses PascalCase
					var defaultsNode = JsonNode.Parse(_initialDataJson);
					if (json != null && defaultsNode != null)
					{
						var storedNode = JsonNode.Parse(json);
						if (storedNode != null)
						{
							var merged = JsonSettingsUtils.MergeJson(defaultsNode, storedNode);
							result = JsonSettingsUtils.Deserialize<TData>(merged, _jsonOptions) ?? new TData();
						}
						else
						{
							// Stored JSON parsed to null (e.g., literal "null") — use defaults
							result = JsonSettingsUtils.Deserialize<TData>(defaultsNode, _jsonOptions) ?? new TData();
						}
					}
					else if (defaultsNode != null)
					{
						// No stored data — deserialize defaults (creates a clean copy)
						result = JsonSettingsUtils.Deserialize<TData>(defaultsNode, _jsonOptions) ?? new TData();
					}
					else
					{
						result = new TData();
					}
				}
				else
				{
					// Original path: no initial data, simple deserialize
					if (json != null)
					{
						result = JsonSerializer.Deserialize<TData>(json, _jsonOptions) ?? new TData();
					}
					else
					{
						result = new TData();
					}
				}

				lock (_lock)
				{
					_data = result;
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;  // Propagate cancellation — not an error
			}
			// Adapter read failures are non-fatal — surface via OnError event, create default TData.
#pragma warning disable NN_R005
			catch (Exception ex)
#pragma warning restore NN_R005
			{
				lock (_lock)
				{
					if (_initialDataJson != null)
					{
						_data = JsonSerializer.Deserialize<TData>(_initialDataJson, _jsonOptions) ?? new TData();
					}
					else
					{
						_data ??= new TData();
					}
				}
				OnError?.Invoke(ex);
			}
		}
		OnDataLoaded?.Invoke();
	}

	/// <summary>
	/// Flushes pending changes and disposes debouncers.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_isDisposed) return;
		_isDisposed = true;

		// Dispose debouncers FIRST — prevents new callbacks from firing during flush
		_writeDebouncer?.Dispose();
		_readDebouncer?.Dispose();

		// Flush any pending dirty data after debouncers are stopped
		if (_isInitialized && Volatile.Read(ref _dirtyGeneration) != Volatile.Read(ref _writtenGeneration))
		{
			try
			{
				await WriteCoreAsync();
			}
			// Flush failure during disposal is non-fatal — surface via OnError event.
#pragma warning disable NN_R005
			catch (Exception ex)
#pragma warning restore NN_R005
			{
				OnError?.Invoke(ex);
			}
		}

		_initGate.Dispose();

		// Dispose adapter if it implements IAsyncDisposable (e.g., BrowserLocalStorageAdapter)
		if (_adapter is IAsyncDisposable disposableAdapter)
			await disposableAdapter.DisposeAsync();
	}
}
