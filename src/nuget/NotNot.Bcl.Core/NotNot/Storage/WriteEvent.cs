namespace NotNot.Storage;

/// <summary>
/// Discriminated-union base type for write-lifecycle events emitted by
/// <see cref="SimpleStorageManager{T}"/> on its <see cref="SimpleStorageManager{T}.Writes"/>
/// observable. Sub-types: <see cref="WriteEventStart{TData}"/>, <see cref="WriteEventSuccess{TData}"/>,
/// <see cref="WriteEventError{TData}"/>.
/// </summary>
/// <typeparam name="TData">Data type managed by the storage manager.</typeparam>
/// <param name="WriteId">
/// Correlation id linking <see cref="WriteEventStart{TData}"/> to its terminating
/// <see cref="WriteEventSuccess{TData}"/> or <see cref="WriteEventError{TData}"/>.
/// Coalesced (debounced) writes share a single id — every queued caller receives the same
/// terminal Maybe outcome.
/// </param>
/// <param name="Timestamp">UTC timestamp at the moment the event was emitted.</param>
/// <param name="Data">Snapshot of the data at the time the event was emitted.</param>
public abstract record WriteEvent<TData>(System.Guid WriteId, System.DateTimeOffset Timestamp, TData Data);

/// <summary>
/// Emitted at the start of an <see cref="SimpleStorageManager{T}.UpdateAsync"/> /
/// <see cref="SimpleStorageManager{T}.SetDataAsync"/> invocation.
/// </summary>
public sealed record WriteEventStart<TData>(System.Guid WriteId, System.DateTimeOffset Timestamp, TData Data)
	: WriteEvent<TData>(WriteId, Timestamp, Data);

/// <summary>
/// Emitted after a debounced write completes successfully. <paramref name="Duration"/> is the
/// elapsed wall time from the start of the underlying adapter write call.
/// </summary>
public sealed record WriteEventSuccess<TData>(System.Guid WriteId, System.DateTimeOffset Timestamp, TData Data, System.TimeSpan Duration)
	: WriteEvent<TData>(WriteId, Timestamp, Data);

/// <summary>
/// Emitted when a debounced write or read fails. Carries the structured <see cref="NotNot.Problem"/>
/// derived from the underlying adapter exception via <see cref="Problem.FromEx"/>.
/// </summary>
public sealed record WriteEventError<TData>(System.Guid WriteId, System.DateTimeOffset Timestamp, TData Data, NotNot.Problem Problem)
	: WriteEvent<TData>(WriteId, Timestamp, Data);
