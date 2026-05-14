using System;
using NotNot.Diagnostics;
using Xunit;

namespace NotNot.Bcl.Core.Tests;

public class PerfSpikeWatchTests
{
   [Fact]
   public void PercentileSampler_GetLastSample_ReturnsMostRecentRecordedValue()
   {
      var sampler = new PercentileSampler800<int>
      {
         TargetSampleCount = 3,
      };

      sampler.RecordSample(10);
      Assert.Equal(10, sampler.GetLastSample());

      sampler.RecordSample(20);
      Assert.Equal(20, sampler.GetLastSample());

      sampler.RecordSample(30);
      Assert.Equal(30, sampler.GetLastSample());

      sampler.RecordSample(40);
      Assert.Equal(40, sampler.GetLastSample());
   }

   [Fact]
   public void Lap_AfterWarmupOnNonPollBoundary_DoesNotReportWarmupMessage()
   {
      var watch = new PerfSpikeWatch(pollSkipFrequency: 2);
      watch.sampler.TargetSampleCount = 2;
      watch.Start();

      var first = watch.Lap();
      Assert.NotNull(first);
      Assert.Contains("No percentiles yet", first);

      var second = watch.Lap();
      Assert.Null(second);

      var third = watch.Lap();
      Assert.NotNull(third);
      Assert.DoesNotContain("No percentiles yet", third);
   }

   [Fact]
   public void Constructor_RejectsNonPositivePollSkipFrequency()
   {
      var ex = Assert.ThrowsAny<Exception>(() => new PerfSpikeWatch(pollSkipFrequency: 0));
      Assert.Contains("pollSkipFrequency must be greater than zero", ex.Message);
   }

   [Fact]
   public void Lap_RejectsNonPositivePollSkipFrequencyAfterMutation()
   {
      var watch = new PerfSpikeWatch();
      watch.pollSkipFrequency = 0;

      var ex = Assert.ThrowsAny<Exception>(() => watch.Lap());
      Assert.Contains("pollSkipFrequency must be greater than zero", ex.Message);
   }

   [Fact]
   public void TargetSampleCount_RejectsNonPositiveValues()
   {
      var sampler = new PercentileSampler800<int>();

      var ex = Assert.ThrowsAny<Exception>(() => sampler.TargetSampleCount = 0);
      Assert.Contains("TargetSampleCount must be greater than zero", ex.Message);
   }
}