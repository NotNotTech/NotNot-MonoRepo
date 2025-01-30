// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]


// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot.Collections._unused;

namespace NotNot.Diagnostics;

/// <summary>
///    sample unmanaged structs to provide statistics
/// </summary>
/// <typeparam name="T"></typeparam>
public unsafe struct PercentileSampler800<T> where T : unmanaged, IComparable<T>
{
   public const int BUFFER_SIZE = 800;
   public int MaxCapacity { get; } = BUFFER_SIZE / sizeof(T);

   public StructArray800<T> _samples;


   private int _nextIndex = 0;
   private bool _isCtored = true;

   /// <summary>
   ///    if we have not filled our sample count, don't generate percentiles based on the blanks
   /// </summary>
   private int _fill = 0;

   public bool IsFilled => _fill >= _targetSampleCount;

   /// <summary>
   ///    maximum samples this instance supports.   must be less than or equal to <see cref="MaxCapacity" />
   /// </summary>
   public int TargetSampleCount
   {
      get => _targetSampleCount;
      set
      {
         __.GetLogger()._EzErrorThrow(value <= MaxCapacity,
            $"({value}) is too big.  Samples must be equal to or less than MaxCapacity ({MaxCapacity})");
         _targetSampleCount = value;
         if (_fill >= _targetSampleCount)
         {
            _fill = _targetSampleCount;
         }
      }
   }

   private int _targetSampleCount = BUFFER_SIZE / sizeof(T);

   public PercentileSampler800()
   {
   }

   public void Clear()
   {
      _nextIndex = 0;
      _fill = 0;
      _samples.Clear();
   }

   public void RecordSample(T value)
   {
      __.GetLogger()._EzErrorThrow(_isCtored, "you need to use a .ctor() otherwise fields are not init");
      fixed (byte* pBuffer = _samples._buffer)
      {
         var tBuffer = (T*)pBuffer;
         tBuffer[_nextIndex % TargetSampleCount] = value;
      }

      _nextIndex = (_nextIndex + 1) % TargetSampleCount;
      if (_fill < TargetSampleCount)
      {
         _fill++;
      }
   }

   public T GetLastSample()
   {
      __.GetLogger()._EzErrorThrow(_isCtored, "you need to use a .ctor() otherwise fields are not init");
      var lastIndex = (TargetSampleCount + _nextIndex - 1) % TargetSampleCount;
      fixed (byte* pBuffer = _samples._buffer)
      {
         var tBuffer = (T*)pBuffer;
         return tBuffer[_nextIndex % TargetSampleCount];
      }
   }

   public Percentiles<T> GetPercentiles()
   {
      __.GetLogger()._EzErrorThrow(_isCtored, "you need to use a .ctor() otherwise fields are not init");

      return new Percentiles<T>(_samples.AsSpan().Slice(0, Math.Min(TargetSampleCount, _fill)));
   }

   public override string ToString()
   {
      __.GetLogger()._EzErrorThrow(_isCtored, "you need to use a .ctor() otherwise fields are not init");
      return GetPercentiles().ToString();
   }

   public string ToString<TOut>(Func<T, TOut> formater)
   {
      __.GetLogger()._EzErrorThrow(_isCtored, "you need to use a .ctor() otherwise fields are not init");
      return GetPercentiles().ToString(formater);
   }
}

/// <summary>
///    Provides a 7-number-summary of data.
///    <para>see https://en.wikipedia.org/wiki/Seven-number_summary</para>
///    <para>also includes quartiles, see: https://en.wikipedia.org/wiki/Quartile</para>
/// </summary>
/// <remarks>
///    for a good explanation of "why", see:
///    https://www.dynatrace.com/news/blog/why-averages-suck-and-percentiles-are-great/
/// </remarks>
/// <typeparam name="T"></typeparam>
public struct Percentiles<T> where T : unmanaged, IComparable<T>
{
   /// <summary>
   ///    how many samples were present on the input data
   /// </summary>
   public int sampleCount;

   /// <summary>
   ///    the minimum.  percentile 0
   /// </summary>
   public T p0;

   /// <summary>
   ///    the 5th percentile.  useful as a minimum if you want to avoid outliers.
   /// </summary>
   public T p5;

   /// <summary>
   ///    1st quartile
   /// </summary>
   public T p25;

   /// <summary>
   ///    2nd quartile, aka median
   /// </summary>
   public T p50;

   /// <summary>
   ///    3rd quartile
   /// </summary>
   public T p75;

   /// <summary>
   ///    the 95th percentile.  useful as a maximum if you want to avoid outliers.
   /// </summary>
   public T p95;

   /// <summary>
   ///    the maximum
   /// </summary>
   public T p100;

   public Percentiles(Span<T> samples)
   {
      if (samples.Length == 0)
      {
         this = default;
         return;
      }

      var len = samples.Length;
      sampleCount = len;
      Span<T> sortedSamples = stackalloc T[len];
      samples.CopyTo(sortedSamples);
      sortedSamples.Sort();
      p0 = sortedSamples[0];
      p5 = sortedSamples[5 * len / 100];
      p25 = sortedSamples[25 * len / 100];
      p50 = sortedSamples[50 * len / 100];
      p75 = sortedSamples[75 * len / 100];
      p95 = sortedSamples[95 * len / 100];
      p100 = sortedSamples[len - 1];
   }

   public override string ToString()
   {
      return $"[0={p0}, 5={p5}, 25={p25},  50={p50}, 75={p75}, 95={p95}, 100={p100}](x{sampleCount})";
   }

   /// <summary>
   ///    generate string while passing a custom format function to the percentile samples
   /// </summary>
   /// <typeparam name="TOut"></typeparam>
   /// <param name="formater"></param>
   /// <returns></returns>
   public string ToString<TOut>(Func<T, TOut> formater)
   {
      return
         $"[0={formater(p0)}, 5={formater(p5)}, 25={formater(p25)}, 50={formater(p50)}, 75={formater(p75)}, 95={formater(p95)}, 100={formater(p100)}](x{sampleCount})";
   }
}