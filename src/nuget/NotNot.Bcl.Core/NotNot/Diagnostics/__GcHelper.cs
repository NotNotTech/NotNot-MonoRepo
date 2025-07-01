// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Runtime;

namespace NotNot.Diagnostics;

/// <summary>
///    help do complex tasks with the GC.
/// </summary>
public static class __GcHelper
{
	private static GcTimingDetails _lastGcTimingDetails;

	public static void ForceFullCollect()
	{
		GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
		GC.Collect();
		GC.WaitForPendingFinalizers();
	}

	public static ref GcTimingDetails GetGcTimings()
	{
		var gcDetails = new GcTimingDetails();
		gcDetails.g0Count = GC.CollectionCount(0);
		gcDetails.g1Count = GC.CollectionCount(1);
		gcDetails.g2Count = GC.CollectionCount(2);


		gcDetails.currentGcCount = 1 + gcDetails.g0Count + gcDetails.g1Count + gcDetails.g2Count;
		if (gcDetails.currentGcCount == _lastGcTimingDetails.currentGcCount)
		{
			return ref _lastGcTimingDetails;
		}


		var lifetimeAllocBytes = GC.GetTotalAllocatedBytes();
		var currentAllocBytes = GC.GetTotalMemory(false);

		//see https://devblogs.microsoft.com/dotnet/the-updated-getgcmemoryinfo-api-in-net-5-0-and-how-it-can-help-you/

		//get info on different kinds of gc https://docs.microsoft.com/en-us/dotnet/api/system.gckind?f1url=%3FappId%3DDev16IDEF1%26l%3DEN-US%26k%3Dk(System.GCKind);k(DevLang-csharp)%26rd%3Dtrue&view=net-6.0
		{
			gcDetails.infoEphemeral = GC.GetGCMemoryInfo(GCKind.Ephemeral);
			gcDetails.infoBackground = GC.GetGCMemoryInfo(GCKind.Background);
			gcDetails.infoFullBlocking = GC.GetGCMemoryInfo(GCKind.FullBlocking);
		}

		_lastGcTimingDetails = gcDetails;
		return ref _lastGcTimingDetails;
	}

	public struct GcTimingDetails
	{
		public int g0Count, g1Count, g2Count;
		/// <summary>
		/// 1 + g0Count + g1Count + g2Count
		/// <para>needs the 1+ for internal state tracking  (genzero not equal to default value)</para>
		/// </summary>
		public int currentGcCount;
		public GCMemoryInfo infoEphemeral, infoBackground, infoFullBlocking;
		private string cachedString;

		public override string ToString()
		{
			if (cachedString == null)
			{
				var counts = $"{currentGcCount} (0={g0Count}/1={g1Count}/2={g2Count})";
				var pauses =
					$"{(infoEphemeral.PauseDurations._Sum() + infoBackground.PauseDurations._Sum() + infoFullBlocking.PauseDurations._Sum()).TotalMilliseconds:00.0}ms(EP={infoEphemeral.PauseDurations._Sum().TotalMilliseconds:00}/BG={infoBackground.PauseDurations._Sum().TotalMilliseconds:00}/FB={infoFullBlocking.PauseDurations._Sum().TotalMilliseconds:00})";

				cachedString = $"counts={counts} pause={pauses}";
			}

			return cachedString;
		}
	}
}
