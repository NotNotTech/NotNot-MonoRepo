using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NotNot.AppSettingsHelper;
using NotNot.Concurrency;

namespace NotNot.Storage;

/// <summary>
/// Generic storage manager that serializes/deserializes a POCO to/from a backing <see cref="IStorageAdapter"/>.
/// Provides debounced auto-save on awaitable mutation and debounced reload on external change notification.
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
/// <b>Mutation tracking:</b> Use <see cref="UpdateAsync"/> to mutate <see cref="Data"/> with automatic dirty-tracking
/// (generation counter), debounced persistence, and an awaitable terminal <see cref="Maybe{T}"/> outcome that
/// reflects the underlying adapter write result. Direct mutations to <see cref="Data"/> are NOT tracked.
/// Use <see cref="SetDataAsync"/> to replace the entire instance under the same await contract.
/// </para>
/// <para>
/// <b>Write lifecycle observation:</b> The <see cref="Writes"/> property exposes an
/// <see cref="System.IObservable{T}"/> of <see cref="WriteEvent{TData}"/> values: Start (per call),
/// Success or Error (per coalesced batch). Subscribers MUST be non-throwing AND thread-tolerant
/// — events may be emitted on the caller's thread (Start) or ThreadPool (Success/Error from the
/// debounced WriteCoreAsync timer callback).
/// </para>
/// <para>
/// <b>Thread safety:</b> <see cref="UpdateAsync"/> and <see cref="SetDataAsync"/> acquire a lock around mutation + dirty flag.
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

	// Disposal-time cleanup callbacks. Registered by callers (e.g., projection-adapter
	// factory wiring) that need to unsubscribe from external sources when this manager is
	// disposed — the canonical use is unhooking parent.OnDataChanged handlers in
	// CreateProjected so per-scope child managers do not leak handler slots into a
	// longer-lived parent.
	private readonly List<Action> _disposalActions = new();

	private Debouncer? _writeDebouncer;
	private Debouncer? _readDebouncer;

	// Writes-observable event stream + correlation map between in-flight UpdateAsync calls
	// and the eventual WriteCoreAsync drain (Phase 1 R1.13).
	private readonly SimpleObservable<WriteEvent<TData>> _writes = new();
	private readonly ConcurrentQueue<(Guid writeId, TaskCompletionSource<Maybe<TData>> tcs)> _pendingWrites = new();

	/// <summary>
	/// Raised after <see cref="Data"/> is mutated via <see cref="UpdateAsync"/> or <see cref="SetDataAsync"/>.
	/// Also raised after <see cref="ReloadAsync"/> completes.
	/// </summary>
	public event Action? OnDataChanged;

	/// <summary>
	/// Raised after data is loaded from the backing store (during <see cref="InitializeAsync"/> or <see cref="ReloadAsync"/>).
	/// </summary>
	public event Action? OnDataLoaded;

	/// <summary>
	/// Observable stream of write-lifecycle events. Emits <see cref="WriteEventStart{TData}"/> at the
	/// moment <see cref="UpdateAsync"/>/<see cref="SetDataAsync"/> is invoked, then a single terminal
	/// <see cref="WriteEventSuccess{TData}"/> or <see cref="WriteEventError{TData}"/> when the
	/// debounced write completes (success/error apply to ALL writeIds queued in the batch — see
	/// <see cref="UpdateAsync"/> remarks).
	/// </summary>
	/// <remarks>
	/// Subscribers MUST be non-throwing AND thread-tolerant. Start events fire on the caller's thread;
	/// Success/Error events fire on the ThreadPool (debouncer Timer callback). Read failures from
	/// <see cref="ReadCoreAsync"/> also surface here as <see cref="WriteEventError{TData}"/> with a
	/// fresh writeId — read failures are storage-layer events worth surfacing through the same channel.
	/// </remarks>
	public IObservable<WriteEvent<TData>> Writes => _writes;

	/// <summary>
	/// Emits a synthetic <see cref="WriteEventError{TData}"/> on the <see cref="Writes"/> stream with the
	/// supplied exception. Available to external types (e.g. <see cref="ProjectedSubtreeStorageAdapter"/>)
	/// that need to surface manager-related diagnostics through the unified observability channel.
	/// Subscribers are responsible for non-throwing handling.
	/// </summary>
	/// <param name="ex">The non-fatal manager-operation exception to surface to subscribers.</param>
	public void RaiseWriteError(Exception ex)
	{
		EmitSyntheticWriteError(ex, GetDataSnapshot());
	}

	/// <summary>
	/// Constructs and emits a synthetic fresh-ID <see cref="WriteEventError{TData}"/> on the
	/// <see cref="Writes"/> stream: a fresh <see cref="Guid"/> writeId, <see cref="DateTimeOffset.UtcNow"/>,
	/// the supplied <paramref name="snapshot"/>, and a <see cref="Problem"/> derived from
	/// <paramref name="ex"/>. Single construction point for the four non-batch error paths
	/// (public <see cref="RaiseWriteError"/>, read-failure, and the two disposal catches);
	/// the batch-correlated <see cref="WriteCoreAsync"/> path is deliberately NOT routed here
	/// (it reuses batchWriteId + an under-lock pre-write snapshot + TCS drain).
	/// </summary>
	/// <param name="ex">The non-fatal manager-operation exception to surface.</param>
	/// <param name="snapshot">The ALREADY-selected data snapshot (caller chooses
	/// <see cref="GetDataSnapshot"/> vs <see cref="GetDataSnapshotOrDefault"/>).</param>
	/// <remarks>Caller-info params are auto-populated by the compiler AT EACH CALL SITE and
	/// forwarded to <see cref="Problem.FromEx"/>, so <c>Problem.source</c> continues to name the
	/// originating method (RaiseWriteError / ReadCoreAsync / DisposeAsync), not this helper.
	/// Emits synchronously — no Task.Run / async dispatch, and never call while holding <c>_lock</c>.</remarks>
	private void EmitSyntheticWriteError(
		Exception ex,
		TData snapshot,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		_writes.OnNext(new WriteEventError<TData>(
			Guid.NewGuid(),
			DateTimeOffset.UtcNow,
			snapshot,
			Problem.FromEx(ex, memberName, sourceFilePath, sourceLineNumber)));
	}

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
	/// Properties with explicit <c>null</c> in stored JSON are removed from the merged result. STJ then
	/// does not SET that property at all, so it keeps whatever the freshly constructed
	/// <typeparamref name="TData"/> already holds: the C# property initializer when one exists, else the
	/// CLR default (0 for int, null for reference types). CONSEQUENCE: an explicit-null removal is only
	/// OBSERVABLE for properties WITHOUT an initializer — give a type a property initializer and stored
	/// <c>null</c> removals for it become silent no-ops. (Corrected 2026-08-19: this comment previously
	/// claimed <c>default(T)</c> was assigned regardless, which is measurably untrue.)
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

	// WhenWritingNull is REQUIRED: ReadCoreAsync uses JsonSettingsUtils.MergeJson which applies
	// RFC-7396 merge-patch semantics — explicit `null` in stored JSON DELETES the key from defaults.
	// Emitting spurious nulls for unset reference properties would wipe defaults on reload.
	private static readonly JsonSerializerOptions DefaultJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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

			// FALSE_POSITIVE PH_P007: debouncer Timer-callback lambdas are deliberately decoupled
			// from any single caller's CancellationToken — forwarding one caller's cancel would
			// drop other callers' coalesced mutations.
#pragma warning disable PH_P007
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
#pragma warning restore PH_P007

			_isInitialized = true;
		}
		finally
		{
			_initGate.Release();
		}
	}

	/// <summary>
	/// The current in-memory data object.
	/// Direct mutations are NOT tracked. Use <see cref="UpdateAsync"/> to mutate with automatic persistence.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	public TData Data
	{
		get
		{
			if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
			return GetDataSnapshot();
		}
	}

	private TData GetDataSnapshot()
	{
		lock (_lock)
		{
			return _data!;
		}
	}

	private TData GetDataSnapshotOrDefault()
	{
		lock (_lock)
		{
			return _data ?? new TData();
		}
	}

	/// <summary>
	/// Mutates the current data under lock, schedules a debounced write to backing storage, and
	/// awaits the terminal write outcome.
	/// </summary>
	/// <param name="mutator">Action that mutates the current <see cref="Data"/> object.</param>
	/// <param name="ct">
	/// Cancels the AWAIT for this caller. The underlying debounced write is NOT cancelled; it
	/// proceeds on its own schedule and other queued callers complete with the actual outcome.
	/// </param>
	/// <returns>
	/// <see cref="Maybe{TData}"/> with <see cref="Maybe{T}.Value"/> = the post-write data snapshot
	/// on success; <see cref="Maybe{T}.Problem"/> populated from the underlying adapter exception on
	/// failure (or a synthesized "wait cancelled" Problem if the await was cancelled before the
	/// debounced write completed).
	/// </returns>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
	/// <remarks>
	/// <b>Coalescing:</b> Multiple <see cref="UpdateAsync"/> calls within the
	/// <see cref="SimpleStorageOptions.WriteDebounce"/> window coalesce into ONE underlying
	/// <see cref="IStorageAdapter.WriteAsync"/>. Each caller's Task completes with the same
	/// terminal outcome (success or failure) once the coalesced write resolves.
	/// </remarks>
	public Task<Maybe<TData>> UpdateAsync(Action<TData> mutator, CancellationToken ct = default)
	{
		// ACCEPTED_BY_DESIGN PH_S032: synchronous argument/state guards from a Task-returning
		// method are this API's documented public contract (<exception> tags, standard BCL
		// convention); Task.FromException would silently change throw-timing for every caller.
#pragma warning disable PH_S032
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));
#pragma warning restore PH_S032

		var writeId = Guid.NewGuid();
		var snapshot = GetDataSnapshot();
		_writes.OnNext(new WriteEventStart<TData>(writeId, DateTimeOffset.UtcNow, snapshot));

		lock (_lock)
		{
			Interlocked.Increment(ref _dirtyGeneration);  // Mark dirty FIRST — worst case we write unchanged data
			mutator(_data!);
		}

		var tcs = new TaskCompletionSource<Maybe<TData>>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingWrites.Enqueue((writeId, tcs));
		_writeDebouncer!.Trigger();
		OnDataChanged?.Invoke();

		return AwaitWithCancellation(tcs.Task, ct);
	}

	/// <summary>
	/// Replaces the entire data object under lock, schedules a debounced write, and awaits the
	/// terminal write outcome. Mirrors <see cref="UpdateAsync"/> with full-replacement semantics.
	/// </summary>
	/// <param name="newData">The new data object. Must not be null.</param>
	/// <param name="ct">Cancels the AWAIT for this caller (see <see cref="UpdateAsync"/> semantics).</param>
	/// <returns>Same shape as <see cref="UpdateAsync"/>.</returns>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="newData"/> is null.</exception>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
	public Task<Maybe<TData>> SetDataAsync(TData newData, CancellationToken ct = default)
	{
		// ACCEPTED_BY_DESIGN PH_S032: same documented sync-guard contract as UpdateAsync.
#pragma warning disable PH_S032
		ArgumentNullException.ThrowIfNull(newData);
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));
#pragma warning restore PH_S032

		var writeId = Guid.NewGuid();
		_writes.OnNext(new WriteEventStart<TData>(writeId, DateTimeOffset.UtcNow, newData));

		lock (_lock)
		{
			_data = newData;
			Interlocked.Increment(ref _dirtyGeneration);
		}

		var tcs = new TaskCompletionSource<Maybe<TData>>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingWrites.Enqueue((writeId, tcs));
		_writeDebouncer!.Trigger();
		OnDataChanged?.Invoke();

		return AwaitWithCancellation(tcs.Task, ct);
	}

	private static async Task<Maybe<TData>> AwaitWithCancellation(Task<Maybe<TData>> task, CancellationToken ct)
	{
		if (!ct.CanBeCanceled)
		{
			return await task.ConfigureAwait(false);
		}

		var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		// await using: CancellationTokenRegistration.DisposeAsync guarantees the (trivial TrySetResult)
		// callback has completed before disposal returns. Sync Dispose() could return while the callback
		// still races on another thread; the async form is the correct shape here (method already async).
		await using var ctReg = ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelTcs);
		var completed = await Task.WhenAny(task, cancelTcs.Task).ConfigureAwait(false);
		if (ReferenceEquals(completed, task))
		{
			return await task.ConfigureAwait(false);
		}

		return Maybe<TData>.Error(new Problem
		{
			Status = HttpStatusCode.Gone,
			Title = "WaitCancelled",
			Detail = "UpdateAsync wait cancelled — underlying write may still proceed",
			category = Problem.CategoryNames.Timeout,
		});
	}

	/// <summary>
	/// Replaces the in-memory data with <paramref name="newData"/> WITHOUT scheduling a write
	/// to the backing store. Intended for projection-style adapters whose backing store is another
	/// <see cref="SimpleStorageManager{T}"/>: when the parent manager mutates, the projected child
	/// must observe the new sub-tree value, but the new value ALREADY came from the persistence
	/// layer (the parent), so writing it back through the projection adapter would re-enter the
	/// parent and produce a recursive write storm.
	/// </summary>
	/// <param name="newData">The new data object (read out of the upstream source). Must not be null.</param>
	/// <remarks>
	/// <para>
	/// Invariants vs <see cref="SetDataAsync"/>:
	/// <list type="bullet">
	///   <item><b>Same:</b> guards (null/initialized/disposed), lock acquisition for the assignment, raises <see cref="OnDataChanged"/>.</item>
	///   <item><b>Different:</b> does NOT increment <c>_dirtyGeneration</c> and does NOT trigger the write debouncer.</item>
	/// </list>
	/// The "no dirty mark / no write trigger" combination is the recursive-write-prevention
	/// guarantee: an upstream change feeds in via this method, raising <see cref="OnDataChanged"/>
	/// for downstream subscribers without ever asking the adapter to persist (the adapter's
	/// upstream IS the source of truth).
	/// </para>
	/// <para>
	/// <b>Dirty-race guard:</b> if this manager has a pending local mutation that has not yet
	/// flushed (<c>_dirtyGeneration &gt; _writtenGeneration</c>), the refresh is SKIPPED and
	/// <see cref="OnDataChanged"/> is NOT raised. Skipping is self-converging: the pending
	/// local write will flush upstream, the upstream will re-fire its own change notification,
	/// and the next <see cref="RefreshFromExternalSource"/> call will see a clean dirty state
	/// and proceed normally. Without this guard, an in-flight upstream refresh would silently
	/// overwrite the local mutation AND, because <c>_dirtyGeneration</c> stays elevated, the
	/// subsequent debounced write would push the externally-supplied data BACK upstream —
	/// silently losing the local mutation and triggering a recursive
	/// <see cref="OnDataChanged"/> cascade through the projection adapter.
	/// </para>
	/// <para>
	/// <see cref="OnDataChanged"/> is raised AFTER the lock is released to match the invariant
	/// established by <see cref="SetDataAsync"/>/<see cref="UpdateAsync"/> (subscribers must not observe
	/// <c>_lock</c> being held).
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="newData"/> is null.</exception>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
	public void RefreshFromExternalSource(TData newData)
	{
		ArgumentNullException.ThrowIfNull(newData);
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));

		bool didRefresh = false;
		lock (_lock)
		{
			// Dirty-race guard: skip the refresh when a pending local mutation is in flight.
			// Self-converging — the local write flushes upstream, upstream re-fires
			// OnDataChanged, and the NEXT refresh call sees clean dirty state and proceeds.
			// Without this guard the local mutation would be silently overwritten AND the
			// dirty mark would re-write the externally-supplied data back upstream
			// (recursive write storm + silent data loss).
			if (Volatile.Read(ref _dirtyGeneration) != Volatile.Read(ref _writtenGeneration))
			{
				return;
			}
			_data = newData;
			didRefresh = true;
			// Intentionally NOT incrementing _dirtyGeneration and NOT triggering _writeDebouncer:
			// newData came FROM the persistence layer (e.g., projection upstream), so writing it
			// back would recursively call into the same source and produce a write storm.
		}
		if (didRefresh)
		{
			OnDataChanged?.Invoke();
		}
	}

	/// <summary>
	/// Registers an action to be invoked when this manager is disposed.
	/// </summary>
	/// <param name="action">Cleanup callback. Must not be null.</param>
	/// <remarks>
	/// <para>
	/// Used by projection wiring (e.g., <c>ProjectedSubtreeStorageAdapter.CreateProjected</c>)
	/// to unsubscribe from a parent manager's <see cref="OnDataChanged"/> event when the child
	/// manager is disposed. Without this hook, a child whose lifetime is shorter than the parent
	/// (e.g., DI scoped child off a singleton parent) would accumulate dead handler slots in the
	/// parent's invocation list — an unbounded subscription leak.
	/// </para>
	/// <para>
	/// Each registered action is invoked exactly once during <see cref="DisposeAsync"/>;
	/// exceptions thrown by an action are surfaced via <see cref="Writes"/> as
	/// <see cref="WriteEventError{TData}"/> so a single failing cleanup does not block
	/// disposal of other resources. Actions fire BEFORE debouncer/adapter teardown so any
	/// unsubscriptions complete while the manager is still in a coherent state.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="action"/> is null.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has already been disposed.</exception>
	public void RegisterDisposalAction(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);
		lock (_lock)
		{
			// Re-check _isDisposed under lock — paired with DisposeAsync's locked
			// snapshot+clear so a concurrent dispose either (a) observes our action
			// and invokes it, or (b) throws ObjectDisposedException here. The pre-fix
			// outside-the-lock check could observe _isDisposed=false, then dispose
			// snapshots+clears, then this thread Adds to a list that will never be
			// drained — a silent no-op leak.
			if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));
			_disposalActions.Add(action);
		}
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
	/// Resets the in-memory data to the seeded defaults (if supplied via the seeded-defaults constructor)
	/// or to a fresh <typeparamref name="TData"/> instance otherwise, then flushes to the backing store.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The seeded defaults captured at construction (<c>_initialDataJson</c>) are the source of truth.
	/// Each call re-deserializes that JSON into a fresh object, guaranteeing no mutation leakage
	/// between successive resets (the original <c>initialData</c> reference is never retained).
	/// </para>
	/// <para>
	/// For instances constructed without seeded defaults, this is equivalent to
	/// <c>SetDataAsync(new TData())</c> — matches the pre-seeded-defaults behavior.
	/// </para>
	/// <para>
	/// Raises <see cref="OnDataChanged"/> via the internal <see cref="SetDataAsync"/> call and flushes
	/// pending writes synchronously so the backing store reflects defaults on return.
	/// </para>
	/// </remarks>
	/// <param name="ct">Cancellation token.</param>
	/// <exception cref="InvalidOperationException">Thrown if <see cref="InitializeAsync"/> has not been called.</exception>
	/// <exception cref="ObjectDisposedException">Thrown if the manager has been disposed.</exception>
	public async Task ResetToDefaultsAsync(CancellationToken ct = default)
	{
		if (!_isInitialized) throw new InvalidOperationException($"SimpleStorageManager<{typeof(TData).Name}> not initialized. Call InitializeAsync() first.");
		if (_isDisposed) throw new ObjectDisposedException(nameof(SimpleStorageManager<TData>));

		TData defaults;
		if (_initialDataJson != null)
		{
			// Re-deserialize from the captured snapshot each call — guarantees a fresh object
			// with no mutation carry-over from previous resets or live Data mutations.
			defaults = JsonSerializer.Deserialize<TData>(_initialDataJson, _jsonOptions) ?? new TData();
		}
		else
		{
			// No seeded defaults — original behavior: stamp a blank POCO.
			defaults = new TData();
		}

		_ = await SetDataAsync(defaults, ct);
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
	/// Drains all pending UpdateAsync TCSes with the terminal Maybe outcome and emits
	/// <see cref="WriteEventSuccess{TData}"/> or <see cref="WriteEventError{TData}"/>.
	/// </summary>
	private async Task WriteCoreAsync(CancellationToken ct = default)
	{
		string json;
		long capturedGeneration;
		TData snapshot;
		lock (_lock)
		{
			if (Volatile.Read(ref _dirtyGeneration) == Volatile.Read(ref _writtenGeneration)) return;
			capturedGeneration = _dirtyGeneration;
			json = JsonSerializer.Serialize(_data, _jsonOptions);
			snapshot = _data!;
		}

		// Drain pending TCSes for ALL writeIds queued before this WriteCoreAsync started.
		// Coalescing semantic: every pending UpdateAsync caller in this batch resolves with the
		// same Maybe outcome derived from the single underlying adapter call. The coalesced
		// "first" id is used for the WriteEventSuccess/Error correlation.
		var batch = new List<(Guid writeId, TaskCompletionSource<Maybe<TData>> tcs)>();
		while (_pendingWrites.TryDequeue(out var entry))
		{
			batch.Add(entry);
		}
		var batchWriteId = batch.Count > 0 ? batch[0].writeId : Guid.NewGuid();
		var stopwatch = Stopwatch.StartNew();

		using (await _ioGate.LockAsync(ct))
		{
			try
			{
				await _adapter.WriteAsync(json, ct);
				// Advance _writtenGeneration to what we serialized.
				// _ioGate serializes writes, so no concurrent writer can race here.
				// If _dirtyGeneration advanced past capturedGeneration (concurrent UpdateAsync()),
				// the gap remains and the next debounced write picks up the newer data.
				Volatile.Write(ref _writtenGeneration, capturedGeneration);
				stopwatch.Stop();

				_writes.OnNext(new WriteEventSuccess<TData>(batchWriteId, DateTimeOffset.UtcNow, snapshot, stopwatch.Elapsed));
				var success = Maybe<TData>.Success(snapshot);
				foreach (var entry in batch)
				{
					entry.tcs.TrySetResult(success);
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				stopwatch.Stop();
				// Cancellation is propagated to the awaiter; drain pending TCSes with cancellation.
				foreach (var entry in batch)
				{
					entry.tcs.TrySetCanceled(ct);
				}
				throw;
			}
			// Adapter write failures are non-fatal — surface via Writes observable, do not crash the app.
#pragma warning disable NN_R005
			catch (Exception ex)
#pragma warning restore NN_R005
			{
				stopwatch.Stop();
				var problem = Problem.FromEx(ex);
				_writes.OnNext(new WriteEventError<TData>(batchWriteId, DateTimeOffset.UtcNow, snapshot, problem));
				var failure = Maybe<TData>.Error(problem);
				foreach (var entry in batch)
				{
					entry.tcs.TrySetResult(failure);
				}
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
			// Adapter read failures are non-fatal — surface via Writes observable, create default TData.
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
				EmitSyntheticWriteError(ex, GetDataSnapshot());
			}
		}
		OnDataLoaded?.Invoke();
	}

	/// <summary>
	/// Flushes pending changes and disposes debouncers.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		// Invoke registered disposal actions BEFORE the rest of teardown so external
		// subscriptions (e.g., parent.OnDataChanged unhooks from CreateProjected) detach
		// while this manager is still coherent. Each action is best-effort — a single
		// failing cleanup must not block subsequent actions or downstream teardown.
		//
		// Locked double-check + locked snapshot+clear + locked _isDisposed=true is paired
		// with RegisterDisposalAction's locked re-check: a concurrent register either (a)
		// completes before _isDisposed flips and its action is captured here, or (b)
		// throws ObjectDisposedException — never silently no-ops by Adding to a list
		// that has already been snapshotted+cleared.
		List<Action> actionsToInvoke;
		lock (_lock)
		{
			if (_isDisposed) return;  // double-check inside lock; short-circuit concurrent dispose
			_isDisposed = true;
			actionsToInvoke = new List<Action>(_disposalActions);
			_disposalActions.Clear();
		}
		foreach (var action in actionsToInvoke)
		{
			// Disposal actions are best-effort — surface failures via Writes so a single
			// failing callback does not block other cleanup nor the manager's own teardown.
#pragma warning disable NN_R005
			try { action(); }
			catch (Exception ex)
			{
				EmitSyntheticWriteError(ex, GetDataSnapshotOrDefault());
			}
#pragma warning restore NN_R005
		}

		// FALSE_POSITIVE PH_B009: this teardown runs AFTER the locked _isDisposed=true snapshot;
		// disposal is single-entry (locked double-check), so it is single-threaded by construction.
		// The generation counters follow the class's lock-free Volatile/Interlocked protocol,
		// which the monitor-lock-modeling analyzer does not recognize (same basis as PH_B010 below).
#pragma warning disable PH_B009
		// Dispose debouncers FIRST — prevents new callbacks from firing during flush
		_writeDebouncer?.Dispose();
		_readDebouncer?.Dispose();

		// Flush any pending dirty data after debouncers are stopped
		// Both counters use Interlocked/Volatile for every cross-thread access. PH_B010
		// models monitor locks but not this lock-free generation-counter protocol.
#pragma warning disable PH_B010 // Interlocked/Volatile provide the synchronization invariant for both counters.
		if (_isInitialized && Volatile.Read(ref _dirtyGeneration) != Volatile.Read(ref _writtenGeneration))
#pragma warning restore PH_B010
#pragma warning restore PH_B009
		{
			try
			{
				await WriteCoreAsync();
			}
			// Flush failure during disposal is non-fatal — surface via Writes observable.
#pragma warning disable NN_R005
			catch (Exception ex)
#pragma warning restore NN_R005
			{
				EmitSyntheticWriteError(ex, GetDataSnapshotOrDefault());
			}
		}

		_initGate.Dispose();

		// Dispose adapter if it implements IAsyncDisposable (e.g., BrowserLocalStorageAdapter)
		if (_adapter is IAsyncDisposable disposableAdapter)
			await disposableAdapter.DisposeAsync();
	}
}
