using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NotNot.Analyzers.Diagnostics;

/// <summary>
/// Tracks performance metrics for NotNot.Analyzers to help identify bottlenecks
/// and monitor analyzer efficiency in large codebases
/// </summary>
public static class AnalyzerPerformanceTracker
{
    private static readonly ConcurrentDictionary<string, AnalyzerMetrics> _metrics = new();
    private static readonly object _lockObject = new();

    /// <summary>
    /// Starts tracking performance for an analyzer operation
    /// </summary>
    public static IDisposable StartTracking(string analyzerId, string operation)
    {
        return new PerformanceScope(analyzerId, operation);
    }

    /// <summary>
    /// Gets performance metrics for a specific analyzer
    /// </summary>
    public static AnalyzerMetrics? GetMetrics(string analyzerId)
    {
        return _metrics.TryGetValue(analyzerId, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Gets all performance metrics
    /// </summary>
    public static AnalyzerMetrics[] GetAllMetrics()
    {
        return _metrics.Values.ToArray();
    }

    /// <summary>
    /// Clears all performance metrics
    /// </summary>
    public static void ClearMetrics()
    {
        _metrics.Clear();
    }

    private static void RecordOperation(string analyzerId, string operation, TimeSpan duration)
    {
        var key = $"{analyzerId}:{operation}";
        _metrics.AddOrUpdate(key, 
            new AnalyzerMetrics(analyzerId, operation, duration),
            (_, existing) => existing.AddOperation(duration));
    }

    private sealed class PerformanceScope : IDisposable
    {
        private readonly string _analyzerId;
        private readonly string _operation;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public PerformanceScope(string analyzerId, string operation)
        {
            _analyzerId = analyzerId;
            _operation = operation;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch.Stop();
                RecordOperation(_analyzerId, _operation, _stopwatch.Elapsed);
                _disposed = true;
            }
        }
    }
}

/// <summary>
/// Performance metrics for an analyzer operation
/// </summary>
public sealed class AnalyzerMetrics
{
    private readonly object _lock = new();
    private int _operationCount;
    private TimeSpan _totalTime;
    private TimeSpan _minTime;
    private TimeSpan _maxTime;

    public string AnalyzerId { get; }
    public string Operation { get; }
    public int OperationCount => _operationCount;
    public TimeSpan TotalTime => _totalTime;
    public TimeSpan AverageTime => _operationCount > 0 ? TimeSpan.FromTicks(_totalTime.Ticks / _operationCount) : TimeSpan.Zero;
    public TimeSpan MinTime => _minTime;
    public TimeSpan MaxTime => _maxTime;

    internal AnalyzerMetrics(string analyzerId, string operation, TimeSpan initialDuration)
    {
        AnalyzerId = analyzerId;
        Operation = operation;
        _operationCount = 1;
        _totalTime = initialDuration;
        _minTime = initialDuration;
        _maxTime = initialDuration;
    }

    internal AnalyzerMetrics AddOperation(TimeSpan duration)
    {
        lock (_lock)
        {
            _operationCount++;
            _totalTime = _totalTime.Add(duration);
            
            if (duration < _minTime)
                _minTime = duration;
            
            if (duration > _maxTime)
                _maxTime = duration;
        }
        
        return this;
    }

    public override string ToString()
    {
        return $"{AnalyzerId}:{Operation} - Count: {OperationCount}, Avg: {AverageTime.TotalMilliseconds:F2}ms, Min: {MinTime.TotalMilliseconds:F2}ms, Max: {MaxTime.TotalMilliseconds:F2}ms";
    }
}