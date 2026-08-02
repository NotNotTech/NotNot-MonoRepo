namespace NotNot.Concurrency;

/// <summary>
/// Tracks elapsed awake-time (excluding system sleep/hibernate).
/// Uses a shared static 1-minute heartbeat: <c>Task.Delay(1 min)</c> naturally
/// pauses during sleep, so only minutes with active CPU are counted.
/// </summary>
/// <remarks>
/// Intended for long-duration timeouts (hours/days) where wall-clock time
/// would incorrectly penalize system sleep. +-1 minute granularity.
/// Thread-safe for concurrent <see cref="GetElapsed"/> / <see cref="Reset"/> calls.
/// The static heartbeat runs indefinitely once started; for a local dev tool with
/// &lt;=100 sessions this is negligible.
/// </remarks>
public sealed class ElapsedMinuteCounter
{
    private long _elapsedMinutes; // Interlocked for thread safety

    // --- Static heartbeat ---
    private static readonly Lock _staticLock = new();
    private static readonly List<WeakReference<ElapsedMinuteCounter>> _instances = new();
    private static Task? _heartbeatTask;

    /// <summary>Creates a new counter and registers it with the static heartbeat.</summary>
    public ElapsedMinuteCounter()
    {
        Register(this);
    }

    /// <summary>
    /// Returns the accumulated awake-time since this instance was created (or last <see cref="Reset"/>).
    /// </summary>
    public TimeSpan GetElapsed()
    {
        return TimeSpan.FromMinutes(Interlocked.Read(ref _elapsedMinutes));
    }

    /// <summary>Resets the counter to zero.</summary>
    public void Reset() => Interlocked.Exchange(ref _elapsedMinutes, 0);

    private static void EnsureHeartbeat()
    {
        if (_heartbeatTask is not null) return;
        using (_staticLock.EnterScope())
        {
            _heartbeatTask ??= Task.Run(HeartbeatLoopAsync);
        }
    }

    private static void Register(ElapsedMinuteCounter instance)
    {
        EnsureHeartbeat();
        using (_staticLock.EnterScope())
        {
            _instances.Add(new WeakReference<ElapsedMinuteCounter>(instance));
        }
    }

    private static async Task HeartbeatLoopAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));

            using (_staticLock.EnterScope())
            {
                // Tick all live instances, prune dead WeakReferences
                for (int i = _instances.Count - 1; i >= 0; i--)
                {
                    if (_instances[i].TryGetTarget(out var counter))
                    {
                        Interlocked.Increment(ref counter._elapsedMinutes);
                    }
                    else
                    {
                        _instances.RemoveAt(i); // GC'd instance, prune
                    }
                }
            }
        }
    }
}
