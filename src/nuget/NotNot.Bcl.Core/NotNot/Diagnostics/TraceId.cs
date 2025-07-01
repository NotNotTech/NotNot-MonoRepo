using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NotNot.Diagnostics;


/// <summary>
/// a diagnostics id that can be used to trace a request through the system
/// serializes as a string, for example, normally:  <para>"gitsha7|Machine_Name|Source|Method|Ln|yyyy-MM-ddTHH:mm:ssZ|id"</para>
/// <para>but if the trace is a trail, the former trace callsites will be concatinated, up to 3 deep, like:
/// <para>"...>3rdOldestSource|Ln>2ndOldestSource|Ln>1stOldestSource|Ln>gitsha7|Machine_Name|Source|Method|Ln|yyyy-MM-ddTHH:mm:ssZ|id"</para>
/// </para>
/// </summary>
[JsonConverter(typeof(TraceIdJsonConverter))]
public record TraceId
{

	//public readonly static string AssemblyInformationalVersion = ((Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly())
	//   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?");//._ConvertToAlphanumeric();

	public readonly static string AssemblyShortHash = (Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly())._GetGitShortHash();

	public readonly static string MachineName = Environment.MachineName._ConvertToAlphanumeric();
	public string TracePrefix { get; protected init; } = AssemblyShortHash + "|" + MachineName;

	private static ulong _traceIdCounter;
	public ulong CountId { get; protected init; } = Interlocked.Increment(ref _traceIdCounter);


	public string SourceMemberName { get; protected init; }
	public string SourceFile { get; protected init; }
	public int SourceLineNumber { get; protected init; }
	public DateTime Timestamp { get; protected init; }
	public TraceId? From { get; init; }


	public static TraceId Generate([CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		return new TraceId(sourceMemberName, sourceFilePath, sourceLineNumber);
	}
	public static TraceId Generate(TraceId from, [CallerMemberName] string sourceMemberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
	{
		return new TraceId(from, sourceMemberName, sourceFilePath, sourceLineNumber);
	}


	private TraceId(string sourceMemberName, string sourceFilePath, int sourceLineNumber)
	{
		SourceMemberName = sourceMemberName._ConvertToAlphanumeric();
		SourceFile = sourceFilePath._GetAfter('\\', true)._GetBeforeLast('.', true);
		SourceLineNumber = sourceLineNumber;
		Timestamp = DateTime.UtcNow;
	}

	private TraceId(TraceId from, string memberName, string sourceFilePath, int sourceLineNumber)
		: this(memberName, sourceFilePath, sourceLineNumber)
	{
		From = from;
	}

	/// <summary>
	/// Returns a human-readable string representation of the TraceId.
	/// </summary>
	public override string ToString()
	{
		//var fromPart = From != null ? $"From: {From.ToString()} | " : "";
		var toReturn = $"{TracePrefix}|{SourceFile}|{SourceMemberName}|{SourceLineNumber}|{Timestamp._ToIso()}|{CountId}";

		//append minimal trace of ancestors
		{
			var loop = 0;
			var current = From;
			while (current is not null && loop < 4)
			{
				toReturn = $"{From.SourceFile}:{From.SourceLineNumber}>" + toReturn;
				current = From.From;
				loop++;
			}
			if (loop == 4 && current is not null)
			{
				toReturn = "...>" + toReturn;
			}
		}
		return toReturn;
	}

	public class TraceIdJsonConverter : JsonConverter<TraceId>
	{
		public override TraceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			// Deserialization logic if needed
			//throw new NotImplementedException();
			return new TraceId("traceId_from_json_roundtrip_not_implemented", "", 0);
		}

		public override void Write(Utf8JsonWriter writer, TraceId value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.ToString());
		}
	}
}
