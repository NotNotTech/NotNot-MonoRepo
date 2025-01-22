// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NotNot.Diagnostics;

/// <summary>
///    a stopwatch that checks for spikes (2x percentile 100 sample) and logs it.
///    <para>use via the .Lap() method</para>
///    <para>
///       Only writes to console every pollSkipFrequency (ie:100 calls) AND ONLY IF the consoleWriteSensitivityFactor
///       and consoleWriteThreshholdMs parameters are fulfilled
///    </para>
/// </summary>
public class PerfSpikeWatch
{
	private string _caller;

	private int _lapCount;
	private Percentiles<TimeSpan> _lastPollPercentiles;

	public double messageWriteSensitivityFactor, messageWriteThreshholdMs;

	/// <summary>
	///    how often to show results to console.  Default of 100 means every 100 calls to LapAndReset() will write a summary of
	///    results to console.
	/// </summary>
	public int pollSkipFrequency;

	public PercentileSampler800<TimeSpan> sampler = new();

	public Stopwatch sw = new();

	/// <summary>
	/// </summary>
	/// <param name="name">name displayed when console output</param>
	/// <param name="consoleWriteSensitivityFactor">
	///    default x2.0.  if the p100 sample isn't this times the p50 or more, it
	///    won't be displayed to console.
	/// </param>
	/// <param name="consoleWriteThreshholdMs">
	///    default 1ms, if the p100 sample is not more than this much greater than average,
	///    it won't be displayed to console
	/// </param>
	/// <param name="pollSkipFrequency">
	///    how often to show results to console.  Default of 100 means every 100 calls to
	///    LapAndReset() will write a summary of results to console.
	/// </param>
	public PerfSpikeWatch(string? name = null, double messageWriteSensitivityFactor = 2.0,
		double messageWriteThreshholdMs = 1.0, int pollSkipFrequency = 100)
	{
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

	public string Name { get; init; }

	/////// <summary>
	/////// if the absolute difference in the p100 sample from median is less than this, we don't report a p100 spike in the summary.
	/////// </summary>
	////public double p100SpikeIgnorePaddingMs = 3.0;
	//private static string _spikeP100MessageDefault = " "._Repeat(18);
	public void Start()
	{
		sw.Start();
	}

	public void Stop()
	{
		sw.Stop();
	}


	public void Restart()
	{
		sw.Restart();
	}

	public void Reset()
	{
		sw.Reset();
	}

	/// <summary>
	///    mark and store the current time, and restart the counter, restarting immediately.
	///    <para>
	///       Only writes to console every pollSkipFrequency (ie:100 calls) AND ONLY IF the p100 is 2x or more the p50
	///       times, unless you specify otherwise via the alwaysShow=true parameter
	///    </para>
	/// </summary>
	/// <param name="memberName"></param>
	/// <param name="sourceFilePath"></param>
	/// <param name="sourceLineNumber"></param>
	/// <returns>If a lap falls below the messageWriteThreshholdMs, a diagnostics message is returned that can be logged.   otherwise null</returns>
	//[Conditional("CHECKED")]
	public string? Lap([CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		var elapsed = sw.Elapsed;
		sw.Restart();
		sampler.RecordSample(elapsed);
		_lapCount++;

		string? toReturn = null;

		//debugging scratch
		//if (Name.StartsWith("[-----Bogus---]")==false)
		//{
		//	return;
		//}
		//once we fill up, do logging if circumstances dictate
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

				//var spikeP100Message = _spikeP100MessageDefault;
				//if ((percentiles.p100 - percentiles.p50).TotalMilliseconds > p100SpikeIgnorePaddingMs)
				//{
				var spikeP100Message = $"spike p100={percentiles.p100.TotalMilliseconds._Round(2)}ms.";
				//}

				var message =
					//$"PERFSPIKEWATCH {Name}({_caller}): spike p100={percentiles.p100.TotalMilliseconds._Round(2)}ms.  " +
					$"{Name}: {spikeP100Message}  " +
					$"currentStats={percentiles.ToString(val => val.TotalMilliseconds._Round(2))}   " +
					$"priorStats={_lastPollPercentiles.ToString(val => val.TotalMilliseconds._Round(2))} " +
					$"gcTimings={__GcHelper.GetGcTimings()}";
				//__.GetLogger()._EzInfo(message, memberName: memberName, sourceFilePath: sourceFilePath, sourceLineNumber: sourceLineNumber);
				//message = message._FormatAppendArgs(memberName, sourceFilePath, sourceLineNumber);
				currentStatsMessage = message;
				toReturn = message;
			}

			_lastPollPercentiles = percentiles;
		}

		return toReturn;
	}

	/// <summary>
	/// the last message that was returned by `Lap()` or `LapAndReset()`, during a breach of the messageWrite threshhold
	/// </summary>
	public string currentStatsMessage = "";

	//[Conditional("CHECKED")]
	/// <summary>
	///    records the lap, stops and resets the counter.    Be sure to call .Start() after this.
	///    <para>
	///       Only writes to console every pollSkipFrequency (ie:100 calls) AND ONLY IF the p100 is 2x or more the p50
	///       times, unless you specify otherwise via the alwaysShow=true parameter
	///    </para>
	/// </summary>
	/// <param name="alwaysShow">
	///    Only writes to console every pollSkipFrequency (ie:100 calls) AND ONLY IF the p100 is 2x or
	///    more the p50 times, unless you specify otherwise via the alwaysShow=true parameter
	/// </param>
	/// <param name="memberName"></param>
	/// <param name="sourceFilePath"></param>
	/// <param name="sourceLineNumber"></param>
	/// <returns>If a lap falls below the messageWriteThreshholdMs, a diagnostics message is returned that can be logged.   otherwise null</returns>
	public string? LapAndReset([CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		var toReturn = Lap(memberName, sourceFilePath, sourceLineNumber);
		sw.Reset();
		return toReturn;
	}
}