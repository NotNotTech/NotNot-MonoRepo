// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NotNot.Diagnostics;

/// <summary>
///    Records lap timings into a rolling percentile sampler and formats diagnostics when a latency spike is detected.
/// </summary>
/// <remarks>
///    <para>
///       Use <see cref="Start" />, <see cref="Restart" />, or the public <see cref="sw" /> directly before the work you want
///       to measure, then call <see cref="Lap" /> or <see cref="LapAndReset" /> after the work completes.
///    </para>
///    <para>
///       This type does not write to the console or to a logger on its own. It returns a formatted message string and mirrors
///       the latest emitted string into <see cref="currentStatsMessage" />.
///    </para>
///    <para>
///       Spike detection is evaluated only when <see cref="sampler" /> is full and the lap count reaches a multiple of
///       <see cref="pollSkipFrequency" />. A spike requires all of the following: the current p100 is at least
///       <see cref="messageWriteSensitivityFactor" /> times the current p50, the current p100 is greater than the prior poll's
///       p100 by the same factor, and the absolute p100 minus p50 gap is at least <see cref="messageWriteThreshholdMs" />
///       milliseconds.
///    </para>
///    <para>
///       The first filled poll establishes the baseline percentile snapshot and returns <see langword="null" /> instead of a
///       message.
///    </para>
///    <para>This type is mutable and is not thread-safe.</para>
/// </remarks>
/// <example>
/// <code>
/// var watch = new PerfSpikeWatch("ImportBatch");
/// watch.Start();
///
/// RunBatch();
///
/// var message = watch.Lap();
/// if (message is not null)
/// {
///     logger.LogDebug("{Message}", message);
/// }
/// </code>
/// </example>
public class PerfSpikeWatch
{
	private string _caller;

	private int _lapCount;
	private Percentiles<TimeSpan> _lastPollPercentiles;

	/// <summary>
	///    Relative multiplier used when comparing the current p100 against the current p50 and the previous poll's p100.
	/// </summary>
	/// <remarks>Higher values make spike reporting less sensitive. The default is <c>2.0</c>.</remarks>
	public double messageWriteSensitivityFactor;

	/// <summary>
	///    Minimum absolute gap, in milliseconds, required between the current p100 and p50 before a spike is reported.
	/// </summary>
	/// <remarks>Higher values suppress reports caused by smaller absolute latency changes. The default is <c>1.0</c>.</remarks>
	public double messageWriteThreshholdMs;

	/// <summary>
	///    Number of laps between percentile comparisons after the sampler has filled.
	/// </summary>
	/// <remarks>
	///    Must be greater than zero. The default is <c>100</c>, which means percentile-based spike detection runs every hundredth
	///    recorded lap once the rolling sample window is full.
	/// </remarks>
	public int pollSkipFrequency;

	/// <summary>
	///    Rolling window of lap durations used to compute percentile snapshots.
	/// </summary>
	/// <remarks>
	///    The default target sample count for <see cref="TimeSpan" /> values is 100 samples, but callers may reconfigure the
	///    sampler before collecting data.
	/// </remarks>
	public PercentileSampler800<TimeSpan> sampler = new();

	/// <summary>
	///    Stopwatch that measures the current lap.
	/// </summary>
	/// <remarks>
	///    <see cref="Lap" /> restarts this stopwatch after recording the elapsed time. <see cref="LapAndReset" /> records a lap and
	///    then resets the stopwatch so timing remains stopped until <see cref="Start" /> or <see cref="Restart" /> is called again.
	/// </remarks>
	public Stopwatch sw = new();

	/// <summary>
	///    Creates a new spike watch with an optional label and configurable reporting thresholds.
	/// </summary>
	/// <param name="name">
	///    Label prefixed to generated messages. <see langword="null" /> becomes an empty label. The stored value is padded to 20
	///    characters for aligned output.
	/// </param>
	/// <param name="messageWriteSensitivityFactor">
	///    Relative multiplier used by spike detection. The default is <c>2.0</c>.
	/// </param>
	/// <param name="messageWriteThreshholdMs">
	///    Minimum absolute gap, in milliseconds, required between p100 and p50 before a spike is reported. The default is
	///    <c>1.0</c>.
	/// </param>
	/// <param name="pollSkipFrequency">
	///    Number of laps between percentile comparisons once the sampler is full. Must be greater than zero. The default is
	///    <c>100</c>.
	/// </param>
	public PerfSpikeWatch(string? name = null, double messageWriteSensitivityFactor = 2.0,
		double messageWriteThreshholdMs = 1.0, int pollSkipFrequency = 100)
	{
		__.ThrowIfNot(pollSkipFrequency > 0, $"{nameof(pollSkipFrequency)} must be greater than zero.");

		if (name == null)
		{
			name = "";
		}

		this.messageWriteSensitivityFactor = messageWriteSensitivityFactor;
		this.messageWriteThreshholdMs = messageWriteThreshholdMs;
		this.pollSkipFrequency = pollSkipFrequency;
		//name += $"({sourceFilePath._GetAfter('\\', true)}:{sourceLineNumber})";

		Name = name.PadRight(20);
	}

	/// <summary>
	///    Label included at the start of generated messages.
	/// </summary>
	/// <remarks>The constructor stores this value right-padded to 20 characters for aligned text output.</remarks>
	public string Name { get; init; }

	/// <summary>
	///    Starts timing the current lap.
	/// </summary>
	public void Start()
	{
		sw.Start();
	}

	/// <summary>
	///    Stops timing without clearing the current elapsed duration.
	/// </summary>
	public void Stop()
	{
		sw.Stop();
	}


	/// <summary>
	///    Resets the elapsed time to zero and immediately starts timing the next lap.
	/// </summary>
	public void Restart()
	{
		sw.Restart();
	}

	/// <summary>
	///    Clears the elapsed time and leaves the stopwatch stopped.
	/// </summary>
	public void Reset()
	{
		sw.Reset();
	}

	/// <summary>
	///    Records the current elapsed time, restarts the stopwatch, and returns a formatted diagnostic string.
	/// </summary>
	/// <remarks>
	///    <para>Before the sampler fills, the returned message contains the latest sample and warm-up progress.</para>
	///    <para>
	///       After the sampler fills, percentile comparisons run only on poll boundaries controlled by <see cref="pollSkipFrequency" />.
	///       The first filled poll captures the baseline percentile snapshot and returns <see langword="null" />.
	///    </para>
	///    <para>
	///       Subsequent calls usually return a non-null string even when no spike is detected. In the non-spike case the message contains
	///       the latest sample and current GC timing details.
	///    </para>
	/// </remarks>
	/// <param name="memberName">
	///    Compiler-supplied caller member name. The current implementation keeps this parameter for instrumentation symmetry but does not
	///    include it in the returned message.
	/// </param>
	/// <param name="sourceFilePath">
	///    Compiler-supplied caller file path. Used to lazily capture internal caller metadata the first time a spike is detected.
	/// </param>
	/// <param name="sourceLineNumber">
	///    Compiler-supplied caller line number paired with <paramref name="sourceFilePath" /> when capturing internal caller metadata.
	/// </param>
	/// <returns>
	///    A formatted status string for most calls, or <see langword="null" /> on the first filled poll while the baseline percentile
	///    snapshot is being established.
	/// </returns>
	//[Conditional("CHECKED")]
	public string? Lap([CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		__.ThrowIfNot(pollSkipFrequency > 0, $"{nameof(pollSkipFrequency)} must be greater than zero.");

		var elapsed = sw.Elapsed;
		sw.Restart();
		sampler.RecordSample(elapsed);
		_lapCount++;

		string? toReturn = null;

		var message = $"{Name}: Latest={sampler.GetLastSample().TotalMilliseconds._Round(2)}ms ";


		// Once the rolling buffer is full, compare the current percentile snapshot to the previous poll window.
		if (sampler.IsFilled && _lapCount % pollSkipFrequency == 0)
		{
			var percentiles = sampler.GetPercentiles();
			if (_lastPollPercentiles.sampleCount == 0)
			{
				_lastPollPercentiles = percentiles;
				return toReturn;
			}

			if (percentiles.p100 >= percentiles.p50 * messageWriteSensitivityFactor &&
				 percentiles.p100 > _lastPollPercentiles.p100 * messageWriteSensitivityFactor
				 && (percentiles.p100 - percentiles.p50).TotalMilliseconds >= messageWriteThreshholdMs
				)
			{
				if (_caller == null)
				{
					_caller = $"{sourceFilePath._GetAfter('\\', true)}:{sourceLineNumber}";
				}

				var spikeP100Message = $"spike p100={percentiles.p100.TotalMilliseconds._Round(2)}ms.";

				message +=
					$"{spikeP100Message}  " +
					$"currentStats={percentiles.ToString(val => val.TotalMilliseconds._Round(2))}   " +
					$"priorStats={_lastPollPercentiles.ToString(val => val.TotalMilliseconds._Round(2))}";

				currentStatsMessage = message;
				toReturn = message;
			}

			_lastPollPercentiles = percentiles;


		}
		else if (!sampler.IsFilled)
		{
			message += $"No percentiles yet ({_lapCount}/{sampler.TargetSampleCount} samples).";
		}

		message += $" gcTimings={__GcHelper.GetGcTimings()}";
		currentStatsMessage = message;
		toReturn = message;

		return toReturn;
	}

	/// <summary>
	///    Stores the most recent message produced by <see cref="Lap" /> or <see cref="LapAndReset" />.
	/// </summary>
	/// <remarks>
	///    This field is not updated during the initial filled poll that returns <see langword="null" /> while the baseline percentile
	///    snapshot is being established.
	/// </remarks>
	public string currentStatsMessage = "";

	//[Conditional("CHECKED")]
	/// <summary>
	///    Records a lap via <see cref="Lap" />, then resets the stopwatch so timing stays stopped.
	/// </summary>
	/// <remarks>
	///    Use this when each timing window should start explicitly. Call <see cref="Start" /> or <see cref="Restart" /> before measuring
	///    the next lap.
	/// </remarks>
	/// <param name="memberName">
	///    Compiler-supplied caller member name. The current implementation keeps this parameter for instrumentation symmetry but does not
	///    include it in the returned message.
	/// </param>
	/// <param name="sourceFilePath">
	///    Compiler-supplied caller file path. Used to lazily capture internal caller metadata the first time a spike is detected.
	/// </param>
	/// <param name="sourceLineNumber">
	///    Compiler-supplied caller line number paired with <paramref name="sourceFilePath" /> when capturing internal caller metadata.
	/// </param>
	/// <returns>
	///    The same value returned by <see cref="Lap" />: usually a formatted status string, or <see langword="null" /> on the first
	///    filled poll while the baseline percentile snapshot is being established.
	/// </returns>
	public string? LapAndReset([CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		var toReturn = Lap(memberName, sourceFilePath, sourceLineNumber);
		sw.Reset();
		return toReturn;
	}
}
