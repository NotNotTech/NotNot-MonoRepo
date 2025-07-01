// /path/to/AssertOnErrorSink.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace NotNot.Logging;

/// <summary>
/// allows asserting when log events are emitted.  useful for troubleshooting errors in dev.
/// ONLY RUNS IN DEBUG | CHECKED BUILDS.  DOES NOT RUN IN RELEASE BUILDS.
/// </summary>
[DebuggerNonUserCode]
public class AssertOnMsgSink : ILogEventSink
{
	private readonly List<Regex> _assertAlwaysPatterns;
	private readonly List<Regex> _assertOncePatterns;
	private readonly List<Regex> _ignorePatterns;
	private readonly bool _isEnabled = false;
	private readonly HashSet<string> _assertOnceSeen = new();

	public AssertOnMsgSink(IConfiguration configuration)
	{
		var assertSection = configuration.GetSection("NotNot:Logging:AssertOnError");
		_assertAlwaysPatterns = ReadRegexPatterns(assertSection, "AssertAlways");
		_assertOncePatterns = ReadRegexPatterns(assertSection, "AssertOnce");
		_ignorePatterns = ReadRegexPatterns(assertSection, "Ignore");

		_isEnabled = _assertAlwaysPatterns.Count > 0 || _assertOncePatterns.Count > 0;
	}

	private List<Regex> ReadRegexPatterns(IConfigurationSection section, string key)
	{
		var patterns = new List<Regex>();
		var patternSection = section.GetSection(key);
		if (patternSection.Exists())
		{
			foreach (var pattern in patternSection.Get<IEnumerable<string>>())
			{
				patterns.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.NonBacktracking));
			}
		}
		return patterns;
	}

	[DebuggerNonUserCode]
	public void Emit(LogEvent logEvent)
	{
		DEBUG_Emit(logEvent);
	}
	[DebuggerNonUserCode]
	[Conditional("DEBUG"), Conditional("CHECKED")]
	private void DEBUG_Emit(LogEvent logEvent)
	{
		if (_isEnabled)//logEvent.Level >= LogEventLevel.Error &&
		{
			if (IsAssertRequired(logEvent))
			{
				if (Debugger.IsAttached)
				{
					Debugger.Break();
				}
				else
				{
					var result = Debugger.Launch();
					if (result is false)
					{
						Debug.Assert(false, $"{this.GetType()._GetReadableTypeName()}: {logEvent.RenderMessage()}");
					}
				}
			}
		}
	}


	private bool IsAssertRequired(LogEvent logEvent)
	{
		string category;
		if (!logEvent.Properties.TryGetValue("SourceContext", out var categoryProperty))
		{
			category = "";
		}
		else
		{
			category = categoryProperty.ToString().Trim('\"');
		}


		string fullMsg = $"<{DateTime.UtcNow.ToLocalTime().TimeOfDay:hh\\:mm\\:ss\\.fff}> [{logEvent.Level.ToString().ToUpperInvariant()}] {logEvent.RenderMessage()} <s:{category}>";

		//checks are prioritized Ignore > AssertAlways > AssertOnce
		//patterns are regex, and are matched against first the Category, and later the full message
		{

			if (!_CheckIgnored(category))
			{
				return false;
			}
			if (!_CheckIgnored(fullMsg))
			{
				return false;
			}

			if (_CheckAssertAlways(category))
			{
				return true;
			}
			if (_CheckAssertAlways(fullMsg))
			{
				return true;
			}

			if (_CheckAssertOnce(logEvent, category))
			{
				//Debugger.Launch();
				//Debugger.Break();
				return true;
			}
			if (_CheckAssertOnce(logEvent, fullMsg))
			{
				//Debugger.Launch();
				//Debugger.Break();
				return true;
			}
		}
		//no match, so no assert
		return false;
	}

	private bool _CheckAssertAlways(string text)
	{
		//check assert always
		var doAssert = _assertAlwaysPatterns.Any(p => p.IsMatch(text));
		if (doAssert)
		{
			return true;
		}

		return false;
	}

	private bool _CheckIgnored(string text)
	{
		//see if ignored
		if (_ignorePatterns.Any(p => p.IsMatch(text)))
		{
			return false;
		}

		return true;
	}

	private bool _CheckAssertOnce(LogEvent logEvent, string text)
	{
		bool doAssert;
		//check assertOnce patterns
		string callsite;
		if (!logEvent.Properties.TryGetValue("callsite", out var callsiteProperty))
		{
			callsite = "";
		}
		else
		{
			callsite = callsiteProperty.ToString().Trim('\"');
		}

		if (!_assertOnceSeen.Contains(callsite))
		{
			doAssert = _assertOncePatterns.Any(p => p.IsMatch(text));
			if (doAssert)
			{
				_assertOnceSeen.Add(callsite);
				return true;
			}
		}

		return false;
	}
}
